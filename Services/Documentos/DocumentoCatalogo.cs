using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Documentos
{
    /// <summary>
    /// Eventos documentales que el sistema emite hoy. Los eventos son código (alguien tiene que
    /// dispararlos desde el flujo real); lo que se configura por datos es qué documentos genera cada
    /// uno. Agregar un evento nuevo = una constante acá + una llamada en el flujo que lo origina.
    /// </summary>
    public static class EventosDocumentales
    {
        /// <summary>Venta pasa a Confirmada (VentaService.ConfirmarVentaAsync).</summary>
        public const string VentaConfirmada = "VENTA_CONFIRMADA";

        /// <summary>
        /// El usuario prepara el contrato de una venta a crédito personal (paso previo, obligatorio,
        /// a confirmar la venta). Es el evento que reemplaza al "generar contrato" fijo de antes.
        /// </summary>
        public const string ContratoCreditoSolicitado = "CONTRATO_CREDITO_SOLICITADO";

        /// <summary>Se registró y aplicó un pago de cuota (cobranza).</summary>
        public const string PagoRegistrado = "PAGO_REGISTRADO";

        public const string PresupuestoGenerado = "PRESUPUESTO_GENERADO";

        /// <summary>Se pidió el presupuesto de una venta a crédito ya configurada (comparte número de operación con el contrato y el pagaré).</summary>
        public const string PresupuestoVentaGenerado = "PRESUPUESTO_VENTA_GENERADO";

        /// <summary>El envío de una venta pasó a Entregado.</summary>
        public const string EntregaRealizada = "ENTREGA_REALIZADA";

        public static IReadOnlyList<EventoDocumentalInfo> Todos { get; } = new[]
        {
            new EventoDocumentalInfo(ContratoCreditoSolicitado, "Contrato de crédito solicitado",
                "Se prepara el contrato de una venta a crédito personal, antes de confirmar la venta.", AnclaDocumento.Venta),
            new EventoDocumentalInfo(VentaConfirmada, "Venta confirmada",
                "La venta pasó a estado Confirmada.", AnclaDocumento.Venta),
            new EventoDocumentalInfo(PagoRegistrado, "Pago registrado",
                "Se cobró una cuota (o varias): cada pago aplicado dispara el evento.", AnclaDocumento.Pago),
            new EventoDocumentalInfo(PresupuestoGenerado, "Presupuesto generado",
                "Se pidió un presupuesto a partir de una cotización.", AnclaDocumento.Cotizacion),
            new EventoDocumentalInfo(PresupuestoVentaGenerado, "Presupuesto de venta generado",
                "Se pidió el presupuesto de una venta con el crédito ya configurado.", AnclaDocumento.Venta),
            new EventoDocumentalInfo(EntregaRealizada, "Entrega realizada",
                "El envío de la venta se marcó como Entregado.", AnclaDocumento.Venta)
        };

        public static bool Existe(string? codigo)
            => !string.IsNullOrWhiteSpace(codigo) && Todos.Any(e => string.Equals(e.Codigo, codigo, StringComparison.OrdinalIgnoreCase));

        public static EventoDocumentalInfo? Obtener(string? codigo)
            => Todos.FirstOrDefault(e => string.Equals(e.Codigo, codigo, StringComparison.OrdinalIgnoreCase));
    }

    public enum AnclaDocumento { Venta = 1, Pago = 2, Cotizacion = 3 }

    public sealed record EventoDocumentalInfo(string Codigo, string Nombre, string Descripcion, AnclaDocumento Ancla);

    public enum TipoCampoDocumento { Texto = 1, Numero = 2, Booleano = 3, Fecha = 4 }

    public sealed record CampoDocumento(
        string Ruta,
        string Etiqueta,
        TipoCampoDocumento Tipo,
        string Grupo,
        string[]? Valores = null,
        AnclaDocumento[]? Anclas = null);

    public sealed record ColeccionDocumento(string Nombre, string Etiqueta, string[] Campos, AnclaDocumento[]? Anclas = null);

    /// <summary>
    /// Catálogo de lo que una plantilla puede referenciar y una condición puede comparar. Es la
    /// lista blanca: una ruta que no esté acá no se acepta ni en plantillas ni en reglas.
    /// </summary>
    public static class CatalogoDocumento
    {
        private static readonly AnclaDocumento[] VentaYPago = { AnclaDocumento.Venta, AnclaDocumento.Pago };
        private static readonly AnclaDocumento[] SoloPago = { AnclaDocumento.Pago };

        public static IReadOnlyList<CampoDocumento> Campos { get; } = Construir();

        public static IReadOnlyList<ColeccionDocumento> Colecciones { get; } = new[]
        {
            new ColeccionDocumento("productos", "Productos de la venta",
                new[] { "codigo", "codigoNumerico", "codigoAlfa", "marca", "subRubro", "descripcion", "detalle", "cantidad", "cantidadTexto", "precio", "subtotal" },
                new[] { AnclaDocumento.Venta, AnclaDocumento.Pago, AnclaDocumento.Cotizacion }),
            new ColeccionDocumento("cuotas", "Cuotas financiadas del crédito",
                new[] { "numero", "numeroFormateado", "importe", "importeFormato", "capital", "interes", "vencimiento", "estado" },
                VentaYPago),
            new ColeccionDocumento("medios", "Medios de pago recibidos (recibo)",
                new[] { "descripcion", "numeroCheque", "banco", "observaciones", "fechaVencimiento", "importeFormato" },
                SoloPago),
            new ColeccionDocumento("imputaciones", "Cuotas a las que se aplicó el cobro (recibo)",
                new[] { "fecha", "cuotaNumero", "cuotasTotal", "operacionNumero", "importeFormato" },
                SoloPago)
        };

        public static CampoDocumento? Buscar(string? ruta)
            => Campos.FirstOrDefault(c => string.Equals(c.Ruta, ruta, StringComparison.OrdinalIgnoreCase));

        public static IEnumerable<CampoDocumento> ParaAncla(AnclaDocumento ancla)
            => Campos.Where(c => c.Anclas == null || c.Anclas.Contains(ancla));

        public static ColeccionDocumento? BuscarColeccion(string? nombre)
            => Colecciones.FirstOrDefault(c => string.Equals(c.Nombre, nombre, StringComparison.OrdinalIgnoreCase));

        private static List<CampoDocumento> Construir()
        {
            var tiposPago = Enum.GetNames<TipoPago>();
            var l = new List<CampoDocumento>();
            void T(string r, string e, string g, AnclaDocumento[]? a = null) => l.Add(new(r, e, TipoCampoDocumento.Texto, g, null, a));
            void N(string r, string e, string g, AnclaDocumento[]? a = null) => l.Add(new(r, e, TipoCampoDocumento.Numero, g, null, a));
            void F(string r, string e, string g, AnclaDocumento[]? a = null) => l.Add(new(r, e, TipoCampoDocumento.Fecha, g, null, a));
            void B(string r, string e, string g, AnclaDocumento[]? a = null) => l.Add(new(r, e, TipoCampoDocumento.Booleano, g, null, a));

            T("empresa.nombre", "Nombre / razón social", "Empresa");
            T("empresa.cuit", "CUIT", "Empresa");
            T("empresa.dni", "DNI", "Empresa");
            T("empresa.direccion", "Domicilio", "Empresa");
            T("empresa.ciudad", "Ciudad de firma", "Empresa");
            T("empresa.jurisdiccion", "Jurisdicción", "Empresa");
            T("empresa.interesMoraDiario", "Interés por mora diario (%)", "Empresa");
            T("empresa.nombreComercial", "Nombre comercial", "Empresa");
            T("empresa.titular", "Titular / vendedor", "Empresa");
            T("empresa.titularDni", "DNI del titular", "Empresa");
            T("empresa.direccionCompleta", "Domicilio completo (contrato)", "Empresa");
            T("empresa.localidad", "Localidad", "Empresa");
            T("contrato.interesMoraDiario", "Interés por mora diario con % (ej. 0,20%)", "Empresa");

            T("cliente.nombre", "Nombre", "Cliente");
            T("cliente.apellido", "Apellido", "Cliente");
            T("cliente.nombreCompleto", "Nombre completo", "Cliente");
            T("cliente.tipoDocumento", "Tipo de documento", "Cliente");
            T("cliente.dni", "Documento", "Cliente");
            T("cliente.cuit", "CUIT/CUIL", "Cliente");
            T("cliente.direccion", "Domicilio", "Cliente");
            T("cliente.localidad", "Localidad", "Cliente");
            T("cliente.telefono", "Teléfono", "Cliente");
            T("cliente.email", "Email", "Cliente");
            T("cliente.codigo", "Código de cliente", "Cliente");
            T("cliente.numeroDocumento", "Número de documento", "Cliente");
            T("cliente.condicionFiscal", "Condición fiscal", "Cliente");
            T("cliente.direccionCompleta", "Domicilio completo", "Cliente");
            T("cliente.codigoPostal", "Código postal", "Cliente");

            T("operacion.numero", "Número de operación", "Operación", VentaYPago);
            F("operacion.fecha", "Fecha de la operación", "Operación", VentaYPago);
            T("operacion.fechaTexto", "Fecha en texto (2 de Septiembre del 2026)", "Operación", VentaYPago);
            N("operacion.dia", "Día", "Operación", VentaYPago);
            T("operacion.mesTexto", "Mes en texto", "Operación", VentaYPago);
            N("operacion.anio", "Año", "Operación", VentaYPago);

            T("venta.numero", "Número", "Venta", new[] { AnclaDocumento.Venta, AnclaDocumento.Pago });
            F("venta.fecha", "Fecha", "Venta", VentaYPago);
            N("venta.total", "Total", "Venta", VentaYPago);
            N("venta.subtotal", "Subtotal", "Venta", VentaYPago);
            l.Add(new("venta.tipoPago", "Forma de pago", TipoCampoDocumento.Texto, "Venta", tiposPago, VentaYPago));
            T("venta.condicionPago", "Condición de pago (texto)", "Venta", VentaYPago);
            N("venta.cantidadProductos", "Cantidad de productos", "Venta", VentaYPago);
            T("venta.productosDetalle", "Detalle de productos (texto)", "Venta", VentaYPago);
            T("venta.sucursal", "Sucursal", "Venta", VentaYPago);
            T("venta.caja", "Caja", "Venta", VentaYPago);
            B("venta.conEnvio", "Tiene envío", "Venta", VentaYPago);

            T("credito.numero", "Número", "Crédito", VentaYPago);
            N("credito.total", "Total a pagar", "Crédito", VentaYPago);
            N("credito.saldo", "Saldo", "Crédito", VentaYPago);
            N("credito.montoFinanciado", "Monto financiado", "Crédito", VentaYPago);
            N("credito.saldoFinanciado", "Saldo financiado (suma de las cuotas)", "Crédito", VentaYPago);
            T("credito.saldoFinanciadoFormato", "Saldo financiado formateado (60646,00)", "Crédito", VentaYPago);
            T("credito.saldoFinanciadoLetras", "Saldo financiado en letras", "Crédito", VentaYPago);
            N("credito.entregaInicial", "Entrega inicial (venta − monto financiado)", "Crédito", VentaYPago);
            T("credito.entregaInicialFormato", "Entrega inicial formateada", "Crédito", VentaYPago);
            B("credito.tieneEntrega", "Tiene entrega inicial", "Crédito", VentaYPago);
            N("credito.cantidadCuotas", "Cantidad de cuotas", "Crédito", VentaYPago);
            N("credito.importeCuota", "Importe de cuota", "Crédito", VentaYPago);
            F("credito.fechaPrimeraCuota", "Fecha de primera cuota", "Crédito", VentaYPago);
            T("credito.planCuotasDetalle", "Plan de cuotas (texto)", "Crédito", VentaYPago);
            B("credito.requiereFiador", "Requiere fiador", "Crédito", VentaYPago);

            B("fiador.existe", "Tiene fiador/garante", "Fiador", VentaYPago);
            T("fiador.nombreCompleto", "Nombre completo", "Fiador", VentaYPago);
            T("fiador.dni", "Documento", "Fiador", VentaYPago);
            T("fiador.direccion", "Domicilio", "Fiador", VentaYPago);
            T("fiador.relacion", "Relación con el cliente", "Fiador", VentaYPago);
            T("fiador.tipoDocumento", "Tipo de documento", "Fiador", VentaYPago);
            T("fiador.numeroDocumento", "Número de documento", "Fiador", VentaYPago);
            T("fiador.direccionCompleta", "Domicilio completo", "Fiador", VentaYPago);

            B("pagare.fechaDefinida", "El vencimiento del pagaré está definido", "Pagaré", VentaYPago);
            F("pagare.fechaVencimiento", "Vencimiento del pagaré", "Pagaré", VentaYPago);
            T("pagare.fechaVencimientoTexto", "Vencimiento del pagaré en texto", "Pagaré", VentaYPago);

            T("pago.numero", "Número de pago", "Pago", SoloPago);
            F("pago.fecha", "Fecha", "Pago", SoloPago);
            N("pago.importe", "Importe cobrado", "Pago", SoloPago);
            B("pago.confirmado", "Pago confirmado (aplicado)", "Pago", SoloPago);
            N("pago.importeTotal", "Importe total recibido", "Pago", SoloPago);
            T("pago.importeTotalFormato", "Importe total formateado", "Pago", SoloPago);
            T("pago.importeTotalLetras", "Importe total en letras", "Pago", SoloPago);
            T("recibo.numero", "Número de recibo", "Pago", SoloPago);
            T("pago.importeEnLetras", "Importe en letras", "Pago", SoloPago);
            T("pago.medioPago", "Medio de pago", "Pago", SoloPago);
            N("pago.importeCuota", "Aplicado a la cuota", "Pago", SoloPago);
            N("pago.importePunitorio", "Aplicado a punitorio", "Pago", SoloPago);
            N("cuota.numero", "Número de cuota", "Pago", SoloPago);
            F("cuota.vencimiento", "Vencimiento de la cuota", "Pago", SoloPago);
            T("cuota.estado", "Estado de la cuota", "Pago", SoloPago);

            F("entrega.fecha", "Fecha de entrega", "Entrega", VentaYPago);
            T("entrega.direccion", "Domicilio de entrega", "Entrega", VentaYPago);
            T("entrega.destinatario", "Destinatario", "Entrega", VentaYPago);

            T("cotizacion.numero", "Número", "Cotización", new[] { AnclaDocumento.Cotizacion });
            F("cotizacion.fecha", "Fecha", "Cotización", new[] { AnclaDocumento.Cotizacion });
            N("cotizacion.total", "Total", "Cotización", new[] { AnclaDocumento.Cotizacion });
            F("cotizacion.vigencia", "Vigencia", "Cotización", new[] { AnclaDocumento.Cotizacion });

            T("documento.numero", "Número del documento", "Documento");
            F("documento.fecha", "Fecha de emisión", "Documento");
            T("documento.fechaHora", "Fecha y hora de emisión", "Documento");
            T("documento.usuario", "Usuario emisor", "Documento");
            T("documento.tipo", "Tipo de documento", "Documento");
            T("documento.numeroPagare", "Número del pagaré asociado", "Documento", new[] { AnclaDocumento.Venta });
            T("documento.numeroContrato", "Número del contrato asociado", "Documento", new[] { AnclaDocumento.Venta });

            return l;
        }

        /// <summary>
        /// Tokens históricos de las plantillas de contrato legadas → ruta nueva. Permiten que los
        /// textos existentes sigan renderizando sin reescribirlos.
        /// </summary>
        public static IReadOnlyDictionary<string, string> AliasesLegados { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Vendedor.Nombre"] = "empresa.nombre", ["Vendedor.Domicilio"] = "empresa.direccion",
            ["Vendedor.DNI"] = "empresa.dni", ["Vendedor.CUIT"] = "empresa.cuit",
            ["Comprador.NombreCompleto"] = "cliente.nombreCompleto", ["Comprador.DNI"] = "cliente.dni",
            ["Comprador.CUITCUIL"] = "cliente.cuit", ["Comprador.Domicilio"] = "cliente.direccion",
            ["Comprador.Localidad"] = "cliente.localidad", ["Comprador.Telefono"] = "cliente.telefono",
            ["Garante.NombreCompleto"] = "fiador.nombreCompleto", ["Garante.DNI"] = "fiador.dni",
            ["Garante.Domicilio"] = "fiador.direccion", ["Garante.Relacion"] = "fiador.relacion",
            ["Venta.Numero"] = "venta.numero", ["Venta.Fecha"] = "venta.fecha", ["Venta.Total"] = "venta.total",
            ["Venta.Productos"] = "venta.productosDetalle",
            ["Credito.Numero"] = "credito.numero", ["Credito.CantidadCuotas"] = "credito.cantidadCuotas",
            ["Credito.MontoCuota"] = "credito.importeCuota", ["Credito.TotalAPagar"] = "credito.total",
            ["Credito.FechaPrimeraCuota"] = "credito.fechaPrimeraCuota", ["Credito.PlanCuotas"] = "credito.planCuotasDetalle",
            ["Contrato.Numero"] = "documento.numeroContrato", ["Pagare.Numero"] = "documento.numeroPagare",
            ["Contrato.FechaEmision"] = "documento.fechaHora", ["UsuarioGeneracion"] = "documento.usuario",
            ["Sucursal"] = "venta.sucursal", ["Caja"] = "venta.caja",
            ["VENDEDOR_NOMBRE"] = "empresa.nombre", ["VENDEDOR_DOMICILIO"] = "empresa.direccion",
            ["VENDEDOR_DNI"] = "empresa.dni", ["VENDEDOR_CUIT"] = "empresa.cuit",
            ["COMPRADOR_NOMBRE"] = "cliente.nombreCompleto", ["COMPRADOR_DNI"] = "cliente.dni",
            ["COMPRADOR_CUIL"] = "cliente.cuit", ["COMPRADOR_DOMICILIO"] = "cliente.direccion",
            ["COMPRADOR_LOCALIDAD"] = "cliente.localidad", ["COMPRADOR_TELEFONO"] = "cliente.telefono",
            ["GARANTE_NOMBRE"] = "fiador.nombreCompleto", ["GARANTE_DNI"] = "fiador.dni",
            ["GARANTE_DOMICILIO"] = "fiador.direccion", ["GARANTE_RELACION"] = "fiador.relacion",
            ["NUMERO_CONTRATO"] = "documento.numeroContrato", ["NUMERO_PAGARE"] = "documento.numeroPagare",
            ["FECHA_OPERACION"] = "venta.fecha", ["FECHA_HORA_EMISION"] = "documento.fechaHora",
            ["PRODUCTOS_DETALLE"] = "venta.productosDetalle", ["PRECIO_TOTAL"] = "venta.total",
            ["SALDO_FINANCIADO"] = "credito.saldoFinanciado", ["CANTIDAD_CUOTAS"] = "credito.cantidadCuotas",
            ["VALOR_CUOTA"] = "credito.importeCuota", ["PLAN_CUOTAS"] = "credito.planCuotasDetalle",
            ["INTERES_MORA"] = "empresa.interesMoraDiario", ["USUARIO_GENERACION"] = "documento.usuario",
            ["SUCURSAL"] = "venta.sucursal", ["CAJA"] = "venta.caja"
        };

        /// <summary>¿El token es una variable conocida (ruta, alias legado o colección)?</summary>
        public static bool EsTokenConocido(string token)
            => Buscar(token) != null || AliasesLegados.ContainsKey(token) || BuscarColeccion(token) != null;
    }

    public static class DocumentoErrores
    {
        public const string PlantillaNoEncontrada = "TEMPLATE_NOT_FOUND";
        public const string PlantillaInactiva = "TEMPLATE_INACTIVE";
        public const string ReglaInvalida = "DOCUMENT_RULE_INVALID";
        public const string ContextoIncompleto = "DOCUMENT_CONTEXT_MISSING";
        public const string YaGenerado = "DOCUMENT_ALREADY_GENERATED";
        public const string ErrorNumeracion = "DOCUMENT_NUMBERING_ERROR";
        public const string ObligatorioFallido = "REQUIRED_DOCUMENT_FAILED";
        public const string Operacion = "DOCUMENT_INVALID_OPERATION";
    }

    /// <summary>Error documental con código estable (ver <see cref="DocumentoErrores"/>).</summary>
    public class DocumentoException : InvalidOperationException
    {
        public string Codigo { get; }

        public DocumentoException(string codigo, string mensaje, Exception? inner = null)
            : base(mensaje, inner)
        {
            Codigo = codigo;
        }
    }

    /// <summary>
    /// Un documento marcado como obligatorio no pudo generarse: el flujo que disparó el evento no
    /// debe completarse (el llamador corre dentro de su transacción y hace rollback).
    /// </summary>
    public sealed class DocumentoObligatorioFallidoException : DocumentoException
    {
        public IReadOnlyList<DocumentoErrorItem> Errores { get; }

        public DocumentoObligatorioFallidoException(IReadOnlyList<DocumentoErrorItem> errores)
            : base(DocumentoErrores.ObligatorioFallido,
                "No se pudo generar un documento obligatorio: " + string.Join(" | ", errores.Select(e => e.Mensaje)))
        {
            Errores = errores;
        }
    }

    public sealed record DocumentoErrorItem(string Codigo, string Mensaje, string? Regla, string? Plantilla, bool Obligatoria);
}
