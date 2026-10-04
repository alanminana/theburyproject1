using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Documentos
{
    /// <summary>
    /// Configuración inicial del sistema documental, equivalente al comportamiento anterior:
    /// "todo crédito personal genera contrato + pagaré" ahora es una regla configurable, y los
    /// contratos ya emitidos se migran a documentos generados. Es idempotente y no destructiva.
    /// </summary>
    public static class DocumentoSeeder
    {
        public const string CodContratoEstandar = "CONTRATO_CREDITO_ESTANDAR";
        public const string CodPagareEstandar = "PAGARE_ESTANDAR";
        public const string CodReciboEstandar = "RECIBO_ESTANDAR";
        public const string CodPresupuestoEstandar = "PRESUPUESTO_ESTANDAR";
        public const string CodConstanciaEstandar = "CONSTANCIA_ENTREGA_ESTANDAR";
        public const string CodContratoLegado = "CONTRATO_LEGADO";
        public const string CodPagareLegado = "PAGARE_LEGADO";

        private static readonly DateTime Vigencia = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static async Task EnsureAsync(AppDbContext context, ILogger logger)
        {
            if (!await context.TiposDocumento.AnyAsync())
                await SembrarConfiguracionAsync(context, logger);

            await AsegurarEmpresaAsync(context, logger);
            await ActivarConstanciaDeEntregaAsync(context, logger);
            await MigrarContratosLegadosAsync(context, logger);
        }

        private static async Task SembrarConfiguracionAsync(AppDbContext context, ILogger logger)
        {
            var legada = await context.PlantillasContratoCredito
                .AsNoTracking()
                .OrderByDescending(p => p.Activa).ThenByDescending(p => p.VigenteDesde).ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync();

            var tipoPresupuesto = Tipo("PRESUPUESTO", "Presupuesto", "PRE", CategoriaDocumento.Comercial, multiples: false, firma: false);
            var tipoContrato = Tipo("CONTRATO", "Contrato de venta", "CVC", CategoriaDocumento.Legal, multiples: false, firma: true);
            var tipoPagare = Tipo("PAGARE", "Pagaré", "PAG", CategoriaDocumento.Legal, multiples: false, firma: true);
            var tipoRecibo = Tipo("RECIBO", "Recibo", "REC", CategoriaDocumento.Financiero, multiples: true, firma: false);
            var tipoConstancia = Tipo("CONSTANCIA_ENTREGA", "Constancia de entrega", "CEN", CategoriaDocumento.Logistico, multiples: false, firma: false);
            context.TiposDocumento.AddRange(tipoPresupuesto, tipoContrato, tipoPagare, tipoRecibo, tipoConstancia);
            await context.SaveChangesAsync();

            // El contador de contratos/pagarés continúa desde los ya emitidos para no pisar números legados.
            var emitidos = await context.ContratosVentaCredito.IgnoreQueryFilters().CountAsync();
            tipoContrato.UltimoNumero = emitidos;
            tipoPagare.UltimoNumero = emitidos;

            var contrato = await CrearPlantillaAsync(context, tipoContrato, CodContratoEstandar, "Contrato de crédito estándar",
                legada?.TextoContrato ?? TextoContratoPorDefecto, "cliente.nombreCompleto,cliente.dni,cliente.direccion",
                "vendedor,comprador,fiador", firma: true, activa: true);
            var pagare = await CrearPlantillaAsync(context, tipoPagare, CodPagareEstandar, "Pagaré estándar",
                legada?.TextoPagare ?? TextoPagarePorDefecto, "cliente.nombreCompleto,cliente.dni",
                "firmante", firma: true, activa: true);
            var recibo = await CrearPlantillaAsync(context, tipoRecibo, CodReciboEstandar, "Recibo estándar",
                TextoRecibo, "cliente.nombreCompleto,pago.importe", null, firma: false, activa: true);
            var presupuesto = await CrearPlantillaAsync(context, tipoPresupuesto, CodPresupuestoEstandar, "Presupuesto estándar",
                TextoPresupuesto, "cotizacion.numero", null, firma: false, activa: true);
            var constancia = await CrearPlantillaAsync(context, tipoConstancia, CodConstanciaEstandar, "Constancia de entrega estándar",
                TextoConstancia, "venta.numero", "comprador", firma: false, activa: true);

            var paquete = new PaqueteDocumental
            {
                Codigo = "CREDITO_ESTANDAR",
                Nombre = "Crédito estándar (contrato + pagaré)",
                Descripcion = "Documentos que se emiten juntos al formalizar un crédito personal."
            };
            context.PaquetesDocumentales.Add(paquete);
            await context.SaveChangesAsync();
            context.PaquetesDocumentalesItems.AddRange(
                new PaqueteDocumentalItem { PaqueteDocumentalId = paquete.Id, PlantillaDocumentoId = contrato.Id, Orden = 1 },
                new PaqueteDocumentalItem { PaqueteDocumentalId = paquete.Id, PlantillaDocumentoId = pagare.Id, Orden = 2 });

            context.ReglasDocumento.AddRange(
                new ReglaDocumento
                {
                    Nombre = "Contrato y pagaré — Crédito personal",
                    EventoCodigo = EventosDocumentales.ContratoCreditoSolicitado,
                    CondicionJson = """{"op":"todas","condiciones":[{"campo":"venta.tipoPago","operador":"igual","valor":"CreditoPersonal"}]}""",
                    PaqueteDocumentalId = paquete.Id,
                    Prioridad = 100,
                    Obligatoria = true,
                    GrupoExclusion = "contrato-credito"
                },
                new ReglaDocumento
                {
                    Nombre = "Recibo de pago de cuota",
                    EventoCodigo = EventosDocumentales.PagoRegistrado,
                    CondicionJson = """{"op":"todas","condiciones":[{"campo":"pago.importe","operador":"mayor","valor":0}]}""",
                    PlantillaDocumentoId = recibo.Id,
                    Prioridad = 100
                },
                new ReglaDocumento
                {
                    Nombre = "Presupuesto",
                    EventoCodigo = EventosDocumentales.PresupuestoGenerado,
                    PlantillaDocumentoId = presupuesto.Id,
                    Prioridad = 100
                },
                new ReglaDocumento
                {
                    Nombre = "Constancia de entrega",
                    EventoCodigo = EventosDocumentales.EntregaRealizada,
                    PlantillaDocumentoId = constancia.Id,
                    Prioridad = 100
                });

            await context.SaveChangesAsync();
            logger.LogInformation("Sistema documental inicializado: 5 tipos, 5 plantillas, 1 paquete, 4 reglas");
        }

        /// <summary>
        /// La empresa tiene su propia configuración. La primera vez se copia de la plantilla de contrato vigente
        /// (que era su única fuente), así los documentos siguen mostrando los mismos datos.
        /// </summary>
        private static async Task AsegurarEmpresaAsync(AppDbContext context, ILogger logger)
        {
            if (await context.EmpresasConfiguracion.AnyAsync())
                return;

            var legada = await context.PlantillasContratoCredito.AsNoTracking()
                .OrderByDescending(p => p.Activa).ThenByDescending(p => p.VigenteDesde).ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync();
            if (legada == null)
                return;

            context.EmpresasConfiguracion.Add(DesdePlantillaLegada(legada, new EmpresaConfiguracion { CreatedBy = "sistema" }));
            await context.SaveChangesAsync();
            logger.LogInformation("Datos de empresa inicializados desde la plantilla de contrato");
        }

        private static EmpresaConfiguracion DesdePlantillaLegada(PlantillaContratoCredito legada, EmpresaConfiguracion empresa)
        {
            empresa.Nombre = legada.NombreVendedor;
            empresa.Cuit = legada.CuitVendedor;
            empresa.Dni = legada.DniVendedor;
            empresa.Domicilio = legada.DomicilioVendedor;
            empresa.Ciudad = legada.CiudadFirma;
            empresa.Jurisdiccion = legada.Jurisdiccion;
            empresa.InteresMoraDiarioPorcentaje = legada.InteresMoraDiarioPorcentaje;
            return empresa;
        }

        /// <summary>
        /// Mantiene los datos de empresa alineados con el editor de contrato anterior (que sigue guardando los
        /// datos del vendedor): si el editor legado guarda una plantilla activa, la empresa toma sus datos. No guarda.
        /// </summary>
        public static async Task SincronizarEmpresaDesdeLegadaAsync(AppDbContext context, PlantillaContratoCredito legada)
        {
            if (!legada.Activa)
                return;

            var empresa = await context.EmpresasConfiguracion.OrderBy(e => e.Id).FirstOrDefaultAsync();
            if (empresa == null)
            {
                context.EmpresasConfiguracion.Add(DesdePlantillaLegada(legada, new EmpresaConfiguracion()));
                return;
            }

            DesdePlantillaLegada(legada, empresa);
        }

        /// <summary>
        /// La constancia de entrega ahora se emite por defecto al marcar Entregado. Las bases que ya tenían la regla
        /// sembrada (inactiva y nunca editada) se activan una sola vez; si alguien la desactiva después, queda desactivada.
        /// </summary>
        private static async Task ActivarConstanciaDeEntregaAsync(AppDbContext context, ILogger logger)
        {
            var regla = await context.ReglasDocumento.FirstOrDefaultAsync(r =>
                r.EventoCodigo == EventosDocumentales.EntregaRealizada && r.Nombre == "Constancia de entrega"
                && !r.Activa && r.UpdatedAt == null);
            if (regla == null)
                return;

            regla.Activa = true;
            await context.SaveChangesAsync();
            logger.LogInformation("Regla 'Constancia de entrega' activada");
        }

        private static TipoDocumento Tipo(string codigo, string nombre, string prefijo, CategoriaDocumento categoria, bool multiples, bool firma)
            => new()
            {
                Codigo = codigo, Nombre = nombre, Prefijo = prefijo, Categoria = categoria,
                PermiteMultiples = multiples, RequiereFirma = false, EsSistema = true,
                Descripcion = firma ? "Documento que debe firmarse." : null
            };

        private static async Task<PlantillaDocumento> CrearPlantillaAsync(
            AppDbContext context, TipoDocumento tipo, string codigo, string nombre, string contenido,
            string? requeridas, string? firmantes, bool firma, bool activa)
        {
            var plantilla = new PlantillaDocumento
            {
                TipoDocumentoId = tipo.Id, Codigo = codigo, Nombre = nombre, Activa = activa,
                VigenteDesde = Vigencia, RequiereFirma = firma, FirmantesRequeridos = firmantes,
                VersionActual = 1, Copias = 1
            };
            context.PlantillasDocumento.Add(plantilla);
            await context.SaveChangesAsync();

            context.PlantillasDocumentoVersion.Add(new PlantillaDocumentoVersion
            {
                PlantillaDocumentoId = plantilla.Id, Numero = 1, Contenido = contenido,
                VariablesRequeridas = requeridas, Comentario = "Versión inicial"
            });
            await context.SaveChangesAsync();
            return plantilla;
        }

        /// <summary>
        /// Mantiene las plantillas estándar de contrato/pagaré alineadas con el editor legado
        /// (ConfiguracionContratoCredito): si el texto cambió se crea una versión nueva. No guarda: lo hace el llamador.
        /// </summary>
        public static async Task SincronizarPlantillasLegadasAsync(AppDbContext context, PlantillaContratoCredito legada)
        {
            if (!legada.Activa)
                return;

            await SincronizarAsync(context, CodContratoEstandar, legada.TextoContrato);
            await SincronizarAsync(context, CodPagareEstandar, legada.TextoPagare);
        }

        private static async Task SincronizarAsync(AppDbContext context, string codigo, string texto)
        {
            var plantilla = await context.PlantillasDocumento.FirstOrDefaultAsync(p => p.Codigo == codigo);
            if (plantilla == null)
                return;

            var vigente = await context.PlantillasDocumentoVersion.AsNoTracking()
                .FirstOrDefaultAsync(v => v.PlantillaDocumentoId == plantilla.Id && v.Numero == plantilla.VersionActual);
            var nuevoTexto = texto.Replace("\r\n", "\n").Trim();
            if (vigente != null && vigente.Contenido.Replace("\r\n", "\n").Trim() == nuevoTexto)
                return;

            // El texto legado puede referenciar variables que el catálogo no conoce: se guarda igual
            // (el editor legado nunca las validó) y el renderer las deja sin resolver como advertencia.
            var siguiente = (await context.PlantillasDocumentoVersion.IgnoreQueryFilters()
                .Where(v => v.PlantillaDocumentoId == plantilla.Id).MaxAsync(v => (int?)v.Numero) ?? 0) + 1;
            context.PlantillasDocumentoVersion.Add(new PlantillaDocumentoVersion
            {
                PlantillaDocumentoId = plantilla.Id, Numero = siguiente, Contenido = nuevoTexto,
                VariablesRequeridas = vigente?.VariablesRequeridas, Comentario = "Sincronizada desde la plantilla de contrato"
            });
            plantilla.VersionActual = siguiente;
        }

        // ------------------------------------------------------------------ Migración de contratos históricos

        /// <summary>
        /// Convierte cada ContratoVentaCredito emitido en dos documentos independientes (contrato y
        /// pagaré) con el texto histórico ya resuelto. El contrato legado NO se borra ni se modifica:
        /// sigue siendo la fuente del PDF y del gate de confirmación.
        /// </summary>
        public static async Task MigrarContratosLegadosAsync(AppDbContext context, ILogger logger)
        {
            var tipoContrato = await context.TiposDocumento.FirstOrDefaultAsync(t => t.Codigo == "CONTRATO");
            var tipoPagare = await context.TiposDocumento.FirstOrDefaultAsync(t => t.Codigo == "PAGARE");
            if (tipoContrato == null || tipoPagare == null)
                return;

            // Un contrato legado ya está representado si existe un documento de su mismo tipo y número:
            // sea uno migrado antes o uno emitido por el motor (que reutiliza la numeración legada).
            // Así el arranque nunca duplica ni choca con el índice único (Tipo, Número).
            var numerosExistentes = (await context.DocumentosGenerados.IgnoreQueryFilters()
                .Where(d => d.TipoDocumentoId == tipoContrato.Id || d.TipoDocumentoId == tipoPagare.Id)
                .Select(d => new { d.TipoDocumentoId, d.Numero })
                .ToListAsync())
                .Select(d => (d.TipoDocumentoId, d.Numero))
                .ToHashSet();

            var clavesExistentes = (await context.DocumentosGenerados.IgnoreQueryFilters()
                .Where(d => d.ContratoLegadoId != null)
                .Select(d => d.ClaveIdempotencia)
                .ToListAsync())
                .ToHashSet();

            var pendientes = (await context.ContratosVentaCredito.AsNoTracking()
                    .OrderBy(c => c.Id)
                    .ToListAsync())
                .Where(c => !numerosExistentes.Contains((tipoContrato.Id, c.NumeroContrato))
                         || !numerosExistentes.Contains((tipoPagare.Id, c.NumeroPagare)))
                .ToList();

            if (pendientes.Count == 0)
                return;

            var plantillaContrato = await ObtenerOCrearArchivoAsync(context, tipoContrato, CodContratoLegado, "Contrato (migrado del sistema anterior)");
            var plantillaPagare = await ObtenerOCrearArchivoAsync(context, tipoPagare, CodPagareLegado, "Pagaré (migrado del sistema anterior)");

            var migrados = 0;
            foreach (var c in pendientes)
            {
                string textoContrato, textoPagare;
                try
                {
                    (textoContrato, textoPagare) = ContratoVentaCreditoService.ResolverTextosHistoricos(c);
                }
                catch (Exception ex)
                {
                    // Snapshot ilegible: se conserva el texto crudo guardado en lugar de perder el contrato.
                    logger.LogWarning(ex, "Contrato legado {ContratoId}: no se pudo resolver el snapshot; se migra el texto sin variables", c.Id);
                    textoContrato = c.TextoContratoSnapshot;
                    textoPagare = c.TextoPagareSnapshot;
                }

                var grupo = Guid.NewGuid();
                foreach (var (tipo, plantilla, numero, texto, sufijo) in new[]
                {
                    (tipoContrato, plantillaContrato, c.NumeroContrato, textoContrato, "contrato"),
                    (tipoPagare, plantillaPagare, c.NumeroPagare, textoPagare, "pagare")
                })
                {
                    if (numerosExistentes.Contains((tipo.Id, numero)) || clavesExistentes.Contains($"legado|{sufijo}|{c.Id}"))
                        continue;

                    context.DocumentosGenerados.Add(new DocumentoGenerado
                    {
                        TipoDocumentoId = tipo.Id,
                        PlantillaDocumentoId = plantilla.Id,
                        PlantillaDocumentoVersionId = plantilla.Versiones.First().Id,
                        Numero = numero,
                        ClienteId = c.ClienteId,
                        VentaId = c.VentaId,
                        CreditoId = c.CreditoId,
                        EventoOrigen = EventosDocumentales.ContratoCreditoSolicitado,
                        ClaveIdempotencia = $"legado|{sufijo}|{c.Id}",
                        GrupoImpresionId = grupo,
                        Estado = EstadoDocumentoGenerado.Generado,
                        FechaGeneracionUtc = c.FechaGeneracionUtc,
                        UsuarioGeneracion = c.UsuarioGeneracion,
                        ContenidoRenderizado = texto,
                        DatosSnapshotJson = c.DatosSnapshotJson,
                        MetadataJson = $"{{\"migradoDeContratoVentaCredito\":{c.Id},\"plantillaLegadaId\":{c.PlantillaContratoCreditoId}}}",
                        ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto))),
                        ContratoLegadoId = c.Id,
                        CreatedBy = "migracion"
                    });
                }

                migrados++;
            }

            await context.SaveChangesAsync();
            logger.LogInformation("Contratos legados migrados a documentos generados: {Cantidad}", migrados);
        }

        private static async Task<PlantillaDocumento> ObtenerOCrearArchivoAsync(AppDbContext context, TipoDocumento tipo, string codigo, string nombre)
        {
            var existente = await context.PlantillasDocumento.Include(p => p.Versiones).FirstOrDefaultAsync(p => p.Codigo == codigo);
            if (existente != null)
                return existente;

            var plantilla = new PlantillaDocumento
            {
                TipoDocumentoId = tipo.Id, Codigo = codigo, Nombre = nombre, Activa = false,
                Descripcion = "Plantilla de archivo: agrupa los documentos importados del sistema anterior. No se usa para generar documentos nuevos.",
                VigenteDesde = Vigencia, VersionActual = 1
            };
            plantilla.Versiones.Add(new PlantillaDocumentoVersion
            {
                Numero = 1, Contenido = "(Documento migrado: el contenido histórico está guardado en cada documento.)",
                Comentario = "Archivo de migración"
            });
            context.PlantillasDocumento.Add(plantilla);
            await context.SaveChangesAsync();
            return plantilla;
        }

        // ------------------------------------------------------------------ Textos por defecto

        private const string TextoContratoPorDefecto = @"CONTRATO DE COMPRAVENTA A CRÉDITO PERSONAL

N.º {{documento.numero}} — {{empresa.ciudad}}, {{venta.fecha}}

VENDEDOR: {{empresa.nombre}}, CUIT {{empresa.cuit}}, domicilio {{empresa.direccion}}.
COMPRADOR: {{cliente.nombreCompleto}}, {{cliente.tipoDocumento}} {{cliente.dni}}, domicilio {{cliente.direccion}}, {{cliente.localidad}}.

BIENES:
{{#productos}}
- {{cantidad}} x {{descripcion}} ({{codigo}}) — {{subtotal}}
{{/productos}}

PRECIO: {{venta.total}}, financiado en {{credito.cantidadCuotas}} cuotas de {{credito.importeCuota}}.

PLAN DE CUOTAS:
{{#cuotas}}
Cuota {{numero}}: vence {{vencimiento}} — {{importe}}
{{/cuotas}}

{{#credito.requiereFiador}}
FIADOR: {{fiador.nombreCompleto}}, documento {{fiador.dni}}, domicilio {{fiador.direccion}}.
{{/credito.requiereFiador}}

Las partes se someten a la jurisdicción de {{empresa.jurisdiccion}}.";

        private const string TextoPagarePorDefecto = @"PAGARÉ N.º {{documento.numero}}

{{empresa.ciudad}}, {{venta.fecha}}

Debo y pagaré a {{empresa.nombre}} la suma de {{credito.total}} en {{credito.cantidadCuotas}} cuotas, según el plan de la operación {{venta.numero}}.

Deudor: {{cliente.nombreCompleto}}, {{cliente.tipoDocumento}} {{cliente.dni}}, domicilio {{cliente.direccion}}.";

        private const string TextoRecibo = @"RECIBO DE PAGO N.º {{documento.numero}}

Fecha: {{documento.fecha}}

Recibimos de {{cliente.nombreCompleto}}, {{cliente.tipoDocumento}} {{cliente.dni}}, con domicilio en {{cliente.direccion}}, la suma de {{pago.importeEnLetras}} ({{pago.importe}}).

Concepto: pago de la cuota {{cuota.numero}} del crédito {{credito.numero}} (operación {{venta.numero}}).
Medio de pago: {{pago.medioPago}}.
Aplicado a la cuota: {{pago.importeCuota}}. Aplicado a punitorios: {{pago.importePunitorio}}.
Saldo del crédito: {{credito.saldo}}.

Emitido por {{documento.usuario}} el {{documento.fechaHora}}.";

        private const string TextoPresupuesto = @"PRESUPUESTO N.º {{cotizacion.numero}}

Fecha: {{cotizacion.fecha}}    Válido hasta: {{cotizacion.vigencia}}

Cliente: {{cliente.nombreCompleto}} — Documento: {{cliente.dni}}

Productos:
{{#productos}}
- {{cantidad}} x {{descripcion}} ({{codigo}}) — {{subtotal}}
{{/productos}}

Total: {{cotizacion.total}}";

        private const string TextoConstancia = @"CONSTANCIA DE ENTREGA N.º {{documento.numero}}

Fecha de entrega: {{entrega.fecha}}
Operación: {{venta.numero}}
Destinatario: {{entrega.destinatario}} (cliente {{cliente.nombreCompleto}}, documento {{cliente.dni}})
Domicilio de entrega: {{entrega.direccion}}

Productos entregados:
{{#productos}}
- {{cantidad}} x {{descripcion}} ({{codigo}})
{{/productos}}

Recibí conforme los bienes detallados.";
    }
}
