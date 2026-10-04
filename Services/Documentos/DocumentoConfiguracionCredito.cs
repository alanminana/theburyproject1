using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Documentos
{
    /// <summary>
    /// Configuración documental de la venta a crédito (presupuesto, pagaré, contrato y recibo de cobranza) armada sobre el
    /// motor existente: tipos, plantillas con su versión 1, paquete "Documentación de Crédito" y reglas. Es idempotente y
    /// NO destructiva: lo que ya existe (por código/nombre) no se modifica. No corre al iniciar la app: se ejecuta a pedido
    /// (<c>dotnet TheBuryProyect.dll --configurar-documentos</c>) o desde los tests.
    /// </summary>
    public static class DocumentoConfiguracionCredito
    {
        public const string CodPresupuesto = "PRESUPUESTO_CREDITO";
        public const string CodContrato = "CONTRATO_VENTA_CREDITO";
        public const string CodPagare = "PAGARE_CREDITO";
        public const string CodRecibo = "RECIBO_COBRANZA";
        public const string CodPaquete = "DOCUMENTACION_CREDITO";

        public const string ReglaPresupuesto = "Presupuesto Crédito";
        public const string ReglaContrato = "Contrato Venta Crédito";
        public const string ReglaPagare = "Pagaré Crédito";
        public const string ReglaRecibo = "Recibo Cobranza";

        private const string CondicionCredito =
            """{"op":"todas","condiciones":[{"campo":"venta.tipoPago","operador":"igual","valor":"CreditoPersonal"}]}""";

        private const string CondicionCreditoConSaldo =
            """{"op":"todas","condiciones":[{"campo":"venta.tipoPago","operador":"igual","valor":"CreditoPersonal"},{"campo":"credito.saldoFinanciado","operador":"mayor","valor":0}]}""";

        private const string CondicionCobroConfirmado =
            """{"op":"todas","condiciones":[{"campo":"pago.confirmado","operador":"verdadero"},{"campo":"pago.importeTotal","operador":"mayor","valor":0}]}""";

        private static readonly DateTime Vigencia = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Datos de la empresa con los que se reproducen los documentos de referencia.</summary>
        public sealed record EmpresaInicial(
            string Titular, string TitularDni, string Cuit, string NombreComercial, string Domicilio, string DomicilioCompleto,
            string Localidad, string Jurisdiccion, decimal InteresMoraDiario, string CondicionFiscalCliente)
        {
            public static EmpresaInicial Referencia { get; } = new(
                "Javier Martinez", "37.898.524", "20-37898524-3", "ELECTRONICA MARTINEZ",
                "Av. San Martín 443", "Av. San Martín n° 443", "Monte", "La Plata", 0.20m, "Consumidor Final");
        }

        public sealed class Resultado
        {
            public List<string> Creado { get; } = new();
            public List<string> Existente { get; } = new();
            public override string ToString()
                => $"Creado: {(Creado.Count == 0 ? "nada" : string.Join(", ", Creado))}. Ya existía: {(Existente.Count == 0 ? "nada" : string.Join(", ", Existente))}.";
        }

        public static async Task<Resultado> ConfigurarAsync(AppDbContext context, ILogger logger, EmpresaInicial? empresaInicial = null)
        {
            var resultado = new Resultado();
            await using var tx = context.Database.CurrentTransaction == null ? await context.Database.BeginTransactionAsync() : null;

            await ConfigurarEmpresaAsync(context, empresaInicial ?? EmpresaInicial.Referencia, resultado);

            var presupuesto = await TipoAsync(context, resultado, "PRESUPUESTO", "Presupuesto", "PRE", CategoriaDocumento.Comercial, multiples: false);
            var contrato = await TipoAsync(context, resultado, "CONTRATO", "Contrato de venta", "CVC", CategoriaDocumento.Legal, multiples: false);
            var pagare = await TipoAsync(context, resultado, "PAGARE", "Pagaré", "PAG", CategoriaDocumento.Legal, multiples: false);
            var recibo = await TipoAsync(context, resultado, "RECIBO", "Recibo", "REC", CategoriaDocumento.Financiero, multiples: true);

            // Si el sistema anterior ya emitió contratos, la numeración continúa después de ellos (no pisa números existentes).
            var emitidos = await context.ContratosVentaCredito.IgnoreQueryFilters().CountAsync();
            foreach (var t in new[] { contrato, pagare })
            {
                if (t.UltimoNumero < emitidos)
                {
                    t.UltimoNumero = emitidos;
                    await context.SaveChangesAsync();
                }
            }

            var plantillaPresupuesto = await PlantillaAsync(context, resultado, presupuesto, CodPresupuesto, "Presupuesto Crédito",
                TextoPresupuesto, "operacion.numero,cliente.nombreCompleto,productos", firmantes: null);
            var plantillaContrato = await PlantillaAsync(context, resultado, contrato, CodContrato, "Contrato de Venta Crédito",
                TextoContrato, "operacion.numero,cliente.nombreCompleto,credito.saldoFinanciado,productos,cuotas", firmantes: "vendedor,comprador,fiador");
            var plantillaPagare = await PlantillaAsync(context, resultado, pagare, CodPagare, "Pagaré Crédito",
                TextoPagare, "operacion.numero,cliente.nombreCompleto,credito.saldoFinanciado", firmantes: "firmante");
            var plantillaRecibo = await PlantillaAsync(context, resultado, recibo, CodRecibo, "Recibo de Cobranza",
                TextoRecibo, "cliente.nombreCompleto,pago.importeTotal,imputaciones", firmantes: null);

            // Orden de impresión de la documentación de crédito: primero el pagaré, después el contrato.
            var paquete = await context.PaquetesDocumentales.Include(p => p.Items).FirstOrDefaultAsync(p => p.Codigo == CodPaquete);
            if (paquete == null)
            {
                paquete = new PaqueteDocumental
                {
                    Codigo = CodPaquete,
                    Nombre = "Documentación de Crédito",
                    Descripcion = "Pagaré y contrato de la venta a crédito, en el orden en que se imprimen: primero el pagaré y luego el contrato."
                };
                paquete.Items.Add(new PaqueteDocumentalItem { PlantillaDocumentoId = plantillaPagare.Id, Orden = 1 });
                paquete.Items.Add(new PaqueteDocumentalItem { PlantillaDocumentoId = plantillaContrato.Id, Orden = 2 });
                context.PaquetesDocumentales.Add(paquete);
                await context.SaveChangesAsync();
                resultado.Creado.Add($"paquete {CodPaquete}");
            }
            else
            {
                resultado.Existente.Add($"paquete {CodPaquete}");
            }

            await ReglaAsync(context, resultado, ReglaPresupuesto, EventosDocumentales.PresupuestoVentaGenerado, CondicionCredito,
                plantillaPresupuesto.Id, prioridad: 100, obligatoria: false);
            // El pagaré y el contrato se emiten juntos al preparar el contrato de la venta (paso previo a confirmarla).
            // Son obligatorios: sin ellos la venta a crédito no avanza. El pagaré va primero (mayor prioridad), como el paquete.
            await ReglaAsync(context, resultado, ReglaPagare, EventosDocumentales.ContratoCreditoSolicitado, CondicionCreditoConSaldo,
                plantillaPagare.Id, prioridad: 100, obligatoria: true);
            await ReglaAsync(context, resultado, ReglaContrato, EventosDocumentales.ContratoCreditoSolicitado, CondicionCreditoConSaldo,
                plantillaContrato.Id, prioridad: 90, obligatoria: true);
            await ReglaAsync(context, resultado, ReglaRecibo, EventosDocumentales.PagoRegistrado, CondicionCobroConfirmado,
                plantillaRecibo.Id, prioridad: 100, obligatoria: false);

            if (tx != null)
                await tx.CommitAsync();

            logger.LogInformation("Configuración documental de crédito: {Resultado}", resultado);
            return resultado;
        }

        private static async Task ConfigurarEmpresaAsync(AppDbContext context, EmpresaInicial datos, Resultado resultado)
        {
            var empresa = await context.EmpresasConfiguracion.OrderBy(e => e.Id).FirstOrDefaultAsync();
            if (empresa == null)
            {
                empresa = new EmpresaConfiguracion { CreatedBy = "configuracion-documental" };
                context.EmpresasConfiguracion.Add(empresa);
                resultado.Creado.Add("datos de la empresa");
            }
            else
            {
                resultado.Existente.Add("datos de la empresa (se completan solo los vacíos)");
            }

            // Los datos que ya cargó el usuario no se pisan; solo se completa lo vacío o lo que sigue siendo el texto de ejemplo.
            static bool Vacio(string? v) => string.IsNullOrWhiteSpace(v) || v.StartsWith("Nombre del vendedor", StringComparison.OrdinalIgnoreCase)
                || v == "Domicilio del vendedor" || v == "Ciudad" || v == "Provincia";

            if (Vacio(empresa.Nombre)) empresa.Nombre = datos.Titular;
            if (Vacio(empresa.Dni)) empresa.Dni = datos.TitularDni;
            if (Vacio(empresa.Cuit)) empresa.Cuit = datos.Cuit;
            if (Vacio(empresa.Domicilio)) empresa.Domicilio = datos.Domicilio;
            if (Vacio(empresa.Ciudad)) empresa.Ciudad = datos.Localidad;
            if (Vacio(empresa.Jurisdiccion)) empresa.Jurisdiccion = datos.Jurisdiccion;
            if (empresa.InteresMoraDiarioPorcentaje <= 0 || empresa.InteresMoraDiarioPorcentaje == 0.05m) empresa.InteresMoraDiarioPorcentaje = datos.InteresMoraDiario;
            if (Vacio(empresa.NombreComercial)) empresa.NombreComercial = datos.NombreComercial;
            if (Vacio(empresa.DomicilioCompleto)) empresa.DomicilioCompleto = datos.DomicilioCompleto;
            if (Vacio(empresa.CondicionFiscalClientePorDefecto)) empresa.CondicionFiscalClientePorDefecto = datos.CondicionFiscalCliente;
            // El vencimiento del pagaré NO se define acá: queda sin definir hasta que se configure explícitamente.

            // El contrato anterior valida y snapshotea los datos del vendedor desde su plantilla vigente: se mantienen alineados.
            foreach (var legada in await context.PlantillasContratoCredito.Where(p => p.Activa).ToListAsync())
            {
                legada.NombreVendedor = empresa.Nombre;
                legada.CuitVendedor = empresa.Cuit;
                legada.DniVendedor = empresa.Dni;
                legada.DomicilioVendedor = empresa.Domicilio;
                legada.CiudadFirma = empresa.Ciudad;
                legada.Jurisdiccion = empresa.Jurisdiccion;
                if (empresa.InteresMoraDiarioPorcentaje > 0)
                    legada.InteresMoraDiarioPorcentaje = empresa.InteresMoraDiarioPorcentaje;
            }

            await context.SaveChangesAsync();
        }

        private static async Task<TipoDocumento> TipoAsync(
            AppDbContext context, Resultado resultado, string codigo, string nombre, string prefijo, CategoriaDocumento categoria, bool multiples)
        {
            var tipo = await context.TiposDocumento.FirstOrDefaultAsync(t => t.Codigo == codigo);
            if (tipo != null)
            {
                resultado.Existente.Add($"tipo {codigo}");
                return tipo;
            }

            tipo = new TipoDocumento
            {
                Codigo = codigo, Nombre = nombre, Prefijo = prefijo, Categoria = categoria,
                PermiteMultiples = multiples, RequiereFirma = false, EsSistema = true
            };
            context.TiposDocumento.Add(tipo);
            await context.SaveChangesAsync();
            resultado.Creado.Add($"tipo {codigo}");
            return tipo;
        }

        private static async Task<PlantillaDocumento> PlantillaAsync(
            AppDbContext context, Resultado resultado, TipoDocumento tipo, string codigo, string nombre, string contenido,
            string requeridas, string? firmantes)
        {
            var plantilla = await context.PlantillasDocumento.FirstOrDefaultAsync(p => p.Codigo == codigo);
            if (plantilla != null)
            {
                resultado.Existente.Add($"plantilla {codigo}");
                return plantilla;
            }

            var errores = PlantillaRenderer.Validar(contenido);
            if (errores.Count > 0)
                throw new DocumentoException(DocumentoErrores.Operacion, $"La plantilla {codigo} no es válida: {string.Join(" ", errores)}");

            plantilla = new PlantillaDocumento
            {
                TipoDocumentoId = tipo.Id, Codigo = codigo, Nombre = nombre, Activa = true, VigenteDesde = Vigencia,
                RequiereFirma = firmantes != null, FirmantesRequeridos = firmantes, VersionActual = 1, Copias = 1
            };
            plantilla.Versiones.Add(new PlantillaDocumentoVersion
            {
                Numero = 1, Contenido = contenido, VariablesRequeridas = requeridas, Comentario = "Versión inicial"
            });
            context.PlantillasDocumento.Add(plantilla);
            await context.SaveChangesAsync();
            resultado.Creado.Add($"plantilla {codigo} v1");
            return plantilla;
        }

        private static async Task ReglaAsync(
            AppDbContext context, Resultado resultado, string nombre, string evento, string condicion, int plantillaId, int prioridad, bool obligatoria)
        {
            if (await context.ReglasDocumento.AnyAsync(r => r.Nombre == nombre))
            {
                resultado.Existente.Add($"regla {nombre}");
                return;
            }

            context.ReglasDocumento.Add(new ReglaDocumento
            {
                Nombre = nombre, EventoCodigo = evento, CondicionJson = condicion, PlantillaDocumentoId = plantillaId,
                Prioridad = prioridad, Obligatoria = obligatoria, Activa = true
            });
            await context.SaveChangesAsync();
            resultado.Creado.Add($"regla {nombre}");
        }

        // ------------------------------------------------------------------ Textos de las plantillas
        // Formato administrativo (ver DocumentoLayout): A4 blanco y negro. Las cláusulas son texto de la plantilla, no código.

        public const string TextoPresupuesto = """
@@admin
@@cols 58/42
** {{empresa.nombreComercial|upper}}
{{empresa.direccion}}, {{empresa.localidad|upper}}
@@col
>> Uso Interno
>> Presupuesto N°: {{operacion.numero}}
>> Fecha: {{operacion.fecha}}
>> Condición: {{venta.condicionPago}}
@@fincols
@@linea
Cliente: ({{cliente.codigo}}) {{cliente.nombreCompleto|upper}}
Calle: {{cliente.direccionCompleta|upper}}
{{cliente.condicionFiscal}} - Tipo Documento: {{cliente.tipoDocumento}} N°: {{cliente.numeroDocumento}}
@@linea
@@cols 70/30
** Detalle de productos
@@tabla 11,9,11,14,31,8;I,I,I,I,I,D
Cód.num¦Marca¦Cód.Alfa¦SubRubro¦Detalle¦Cant.
{{#productos}}
{{codigoNumerico}}¦{{marca}}¦{{codigoAlfa}}¦{{subRubro}}¦{{descripcion}}¦{{cantidadTexto}}
{{/productos}}
@@fintabla
@@col
** Cuota/Vencimiento/Importe
Entrega: $ {{credito.entregaInicialFormato}}
@@tabla 5,12,13;D,I,D;plana
{{#cuotas}}
{{numero}}¦{{vencimiento}}¦$ {{importeFormato}}
{{/cuotas}}
@@fintabla
@@fincols
@@linea
""";

        public const string TextoPagare = """
@@admin
@@cols 50/50
Sellado $ ....................
@@col
>> N° {{operacion.numero}}
@@fincols

{{#pagare.fechaDefinida}}
Vence el: {{pagare.fechaVencimientoTexto}}
{{/pagare.fechaDefinida}}
{{^pagare.fechaDefinida}}
Vence el: ..............................................
{{/pagare.fechaDefinida}}
Por $ {{credito.saldoFinanciadoFormato}}.-

{{empresa.localidad|upper}}, {{operacion.fechaTexto}}, pagaré sin protesto (art.50 D.Ley 5965/63) al Señor {{empresa.titular|upper}} a su orden La cantidad de pesos: {{credito.saldoFinanciadoLetras}}.- por igual valor recibido en mercaderías a entera satisfacción pagadero en {{empresa.direccion}} de la ciudad de {{empresa.localidad|upper}}.-

Firmante: ({{cliente.codigo}}) {{cliente.nombreCompleto|upper}}
Dirección: {{cliente.direccionCompleta}}
Localidad: {{cliente.localidad}} - Telefono: {{cliente.telefono}}
@@firmas

CLAUSULA SIN PROTESTO: Respecto del pagaré que luce precedentemente se pacta la cláusula 'sin protesto', de modo que el tomador y tenedores sucesivos quedan dispensados de formalizar el protesto por falta de pago (art. 50, dec. ley 5.965/63, ratificado por ley 16.478) y les confiere vía ejecutiva en caso de no ser pagado a su vencimiento
""";

        public const string TextoContrato = """
@@admin
** Contrato de Venta N°: {{operacion.numero}}

Entre {{empresa.titular}} con domicilio en {{empresa.direccionCompleta}} de la ciudad de {{empresa.localidad}}, DNI {{empresa.titularDni}} (CUIT {{empresa.cuit}}) en adelante denominado EL VENDEDOR, por una parte y por la otra el señor: {{cliente.nombreCompleto}} con domicilio {{cliente.direccionCompleta}} y D.N.I. N° {{cliente.numeroDocumento}} en adelante denominado EL COMPRADOR, se conviene a celebrar el presente contrato de compraventa de acuerdo a las siguientes cláusulas y condiciones.

** PRIMERA
El bien de objeto de venta es recibido de plena conformidad por el comprador siendo garantizada la calidad y funcionamiento del mismo por su fabricante y/o importador. El vendedor vende al comprador y este adquiere en propiedad su objeto mueble que a continuación se detalla:
{{#productos}}
({{cantidadTexto}}) {{detalle}}
{{/productos}}

** SEGUNDA
El saldo deudor del/de los artículo/s se establece en la suma de Pesos: {{credito.saldoFinanciadoLetras}} ($ {{credito.saldoFinanciadoFormato}}) pagaderos de la siguiente forma:
{{#cuotas}}
Pago: {{numeroFormateado}} Vencimiento: {{vencimiento}} Forma: Cuotas $ {{importeFormato}}
{{/cuotas}}
El lugar de pago se fija en el domicilio del Vendedor. La falta de pago en término de una sola de las cuotas determinará la caducidad y vencimiento de todos los plazos por el presente contrato se establecen y dará derecho al vendedor a exigir la totalidad del saldo adeudado al comprador sin necesidad de intimación extrajudicial, pactándose en forma expresa la vía ejecutiva para perseguir dicho cobro.

** TERCERA
Sin perjuicio de lo anteriormente expresado la falta de pago en término del importe adeudado determinará que se devengue un interés diario a favor del vendedor del {{contrato.interesMoraDiario}} sobre las cuotas adeudadas y/o el saldo del precio adeudado.

** CUARTA
A los efectos legales emergentes del presente contrato, las partes constituyen domicilio legal y especial en los indicados al comienzo del presente, donde se tendrán válidas las notificaciones o intimaciones que en las mismas se practiquen y se someten a la jurisdicción y competencia de los tribunales ordinarios de justicia de La Plata, con renuncia expresa a cualquier otro fuero o jurisdicción que pudiere corresponder.

** QUINTA
{{#fiador.existe}}
El señor: {{fiador.nombreCompleto}} con domicilio {{fiador.direccionCompleta}} y D.N.I. N° {{fiador.numeroDocumento}} se constituye en fiador solidario y principal pagador, con expresa renuncia a los beneficios de división y exclusión, por el cumplimiento de todas y cada una de las cláusulas del presente contrato de compraventa.
{{/fiador.existe}}
{{^fiador.existe}}
El señor: ............................................................ constituye en fiador solidario y principal pagador, con expresa renuncia a los beneficios de división y exclusión, por el cumplimiento de todas y cada una de las cláusulas del presente contrato de compraventa.
{{/fiador.existe}}

En prueba de conformidad se firman dos ejemplares de un mismo tenor y a los mismos efectos en la ciudad de {{empresa.localidad|upper}} a los {{operacion.dia}} días del mes de {{operacion.mesTexto}} del {{operacion.anio}}.-
@@firmas
""";

        public const string TextoRecibo = """
@@admin
@@cols 60/40
** {{empresa.nombreComercial|upper}}
{{empresa.direccion}}, {{empresa.localidad|upper}}
@@col
>> RECIBO N°: {{recibo.numero}}
>> Fecha: {{pago.fecha}}
@@fincols
@@linea
Cliente: ({{cliente.codigo}}) {{cliente.nombreCompleto|upper}}
Calle: {{cliente.direccionCompleta|upper}}
{{cliente.condicionFiscal}} - Tipo Documento: {{cliente.tipoDocumento}} N°: {{cliente.numeroDocumento}}
@@linea
** VALORES RECIBIDOS:
@@tabla 22,22,28,14,14;I,I,I,I,D
Nro. Cheque¦Banco¦Observaciones¦Fec. Vto.¦Importe
{{#medios}}
{{descripcion}}¦{{banco}}¦{{observaciones}}¦{{fechaVencimiento}}¦{{importeFormato}}
{{/medios}}
@@fintabla

** APLICADO A:
@@tabla 14,56,16;I,I,D
Fecha¦Comprobante¦Monto
{{#imputaciones}}
{{fecha}}¦Cuota: {{cuotaNumero}}/{{cuotasTotal}} Operac N°: {{operacionNumero}}¦{{importeFormato}}
{{/imputaciones}}
@@fintabla
@@linea
>> Monto total: {{pago.importeTotalFormato}}

Son Pesos: ${{pago.importeTotalFormato}}

Recibí conforme la suma de {{pago.importeTotalLetras}}
""";
    }
}
