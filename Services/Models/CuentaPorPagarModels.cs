using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models
{
    /// <summary>Resultado de una operación del libro de cuentas por pagar (D-03).</summary>
    public sealed record ResultadoCxp(bool Ok, string? Error = null, int? Id = null, bool YaRegistrado = false)
    {
        public static ResultadoCxp Exito(int? id = null, bool yaRegistrado = false) => new(true, null, id, yaRegistrado);
        public static ResultadoCxp Falla(string error) => new(false, error);
    }

    public class ComprobanteProveedorRequest
    {
        public int ProveedorId { get; set; }
        public int? OrdenCompraId { get; set; }
        public TipoComprobanteProveedor Tipo { get; set; } = TipoComprobanteProveedor.Factura;
        public string? NumeroComprobante { get; set; }
        public DateTime? FechaEmision { get; set; }
        public DateTime? FechaVencimiento { get; set; }
        public decimal ImporteNeto { get; set; }
        public decimal ImporteIva { get; set; }
        public decimal ImporteTotal { get; set; }
        public string? Observaciones { get; set; }
    }

    /// <summary>Parte de un pago (o de una nota de crédito) aplicada a un comprobante. Monto vacío o 0 = fila ignorada.</summary>
    public class ImputacionRequest
    {
        public int ComprobanteId { get; set; }
        public decimal? Monto { get; set; }
    }

    public class PagoProveedorRequest
    {
        public int ProveedorId { get; set; }
        public DateTime? Fecha { get; set; }
        public MedioPagoProveedor Medio { get; set; } = MedioPagoProveedor.Transferencia;
        public decimal Monto { get; set; }
        public string? Referencia { get; set; }
        public int? ChequeId { get; set; }
        /// <summary>Origen de los fondos (Tesorería administrativa). Obligatorio.</summary>
        public int? OrigenFondosId { get; set; }
        public string? Observaciones { get; set; }
        /// <summary>Token por formulario: reenviar el mismo formulario no registra un segundo pago.</summary>
        public string? ClaveIdempotencia { get; set; }
        public List<ImputacionRequest> Imputaciones { get; set; } = new();
    }

    public class FiltroComprobantesProveedor
    {
        public int? ProveedorId { get; set; }
        public EstadoComprobanteProveedor? Estado { get; set; }
        public TipoComprobanteProveedor? Tipo { get; set; }
        /// <summary>Solo facturas/notas de débito con saldo y vencimiento anterior al día comercial de hoy.</summary>
        public bool SoloVencidos { get; set; }
        public DateTime? VencimientoDesde { get; set; }
        public DateTime? VencimientoHasta { get; set; }
        public string? Busqueda { get; set; }
        public int Pagina { get; set; } = 1;
        public int TamanoPagina { get; set; } = 25;
    }

    public class ComprobanteProveedorItem
    {
        public int Id { get; set; }
        public int ProveedorId { get; set; }
        public string ProveedorNombre { get; set; } = string.Empty;
        public TipoComprobanteProveedor Tipo { get; set; }
        public string NumeroComprobante { get; set; } = string.Empty;
        public DateTime FechaEmision { get; set; }
        public DateTime? FechaVencimiento { get; set; }
        public decimal ImporteTotal { get; set; }
        public decimal SaldoPendiente { get; set; }
        public EstadoComprobanteProveedor Estado { get; set; }
        /// <summary>Días de atraso respecto del día comercial (0 si no está vencido o no corresponde).</summary>
        public int DiasVencido { get; set; }
        public bool EsDeuda => Tipo != TipoComprobanteProveedor.NotaCredito;
    }

    public class ListadoComprobantesProveedor
    {
        public List<ComprobanteProveedorItem> Items { get; set; } = new();
        public int Total { get; set; }
        public int Pagina { get; set; }
        public int TamanoPagina { get; set; }
        /// <summary>Suma de saldos de facturas/notas de débito vigentes que cumplen el filtro (no solo la página).</summary>
        public decimal SaldoDeuda { get; set; }
        public int TotalPaginas => TamanoPagina <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Total / (double)TamanoPagina));
    }

    public class ImputacionPagoItem
    {
        public int PagoId { get; set; }
        public int ComprobanteId { get; set; }
        public string ComprobanteNumero { get; set; } = string.Empty;
        public DateTime FechaPago { get; set; }
        public MedioPagoProveedor Medio { get; set; }
        public decimal Monto { get; set; }
        public bool PagoAnulado { get; set; }
    }

    public class ImputacionNotaCreditoItem
    {
        public int Id { get; set; }
        public int NotaCreditoId { get; set; }
        public string NotaCreditoNumero { get; set; } = string.Empty;
        public int ComprobanteId { get; set; }
        public string ComprobanteNumero { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public DateTime Fecha { get; set; }
        public bool Anulada { get; set; }
        public string? MotivoAnulacion { get; set; }
    }

    public class ComprobanteProveedorDetalle
    {
        public ComprobanteProveedorItem Cabecera { get; set; } = new();
        public decimal ImporteNeto { get; set; }
        public decimal ImporteIva { get; set; }
        public int? OrdenCompraId { get; set; }
        public string? OrdenCompraNumero { get; set; }
        public string? Observaciones { get; set; }
        public string? CreadoPor { get; set; }
        public DateTime CreadoEn { get; set; }
        public string? AnuladoPor { get; set; }
        public DateTime? FechaAnulacion { get; set; }
        public string? MotivoAnulacion { get; set; }
        public List<ImputacionPagoItem> Pagos { get; set; } = new();
        /// <summary>Notas de crédito aplicadas a este comprobante (si es deuda) o aplicaciones de esta NC (si es NC).</summary>
        public List<ImputacionNotaCreditoItem> NotasCredito { get; set; } = new();
    }

    public class PagoProveedorItem
    {
        public int Id { get; set; }
        public int ProveedorId { get; set; }
        public string ProveedorNombre { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public MedioPagoProveedor Medio { get; set; }
        public decimal Monto { get; set; }
        public string? Referencia { get; set; }
        public int? ChequeId { get; set; }
        public string? ChequeNumero { get; set; }
        public int? OrigenFondosId { get; set; }
        public string? OrigenFondosNombre { get; set; }
        public string? Observaciones { get; set; }
        public bool Anulado { get; set; }
        public string? AnuladoPor { get; set; }
        public DateTime? FechaAnulacion { get; set; }
        public string? MotivoAnulacion { get; set; }
        public string? CreadoPor { get; set; }
        public List<ImputacionPagoItem> Imputaciones { get; set; } = new();
    }

    public enum TipoMovimientoCuentaPorPagar
    {
        Factura = 1,
        NotaDebito = 2,
        NotaCredito = 3,
        Pago = 4,
        AnulacionPago = 5,
        AnulacionComprobante = 6,
        Anticipo = 7,
        AnulacionAnticipo = 8
    }

    public class MovimientoCuentaPorPagar
    {
        public DateTime Fecha { get; set; }
        public TipoMovimientoCuentaPorPagar Tipo { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        /// <summary>Aumenta la deuda con el proveedor.</summary>
        public decimal Debe { get; set; }
        /// <summary>Reduce la deuda con el proveedor.</summary>
        public decimal Haber { get; set; }
        public decimal SaldoCorrido { get; set; }
        public int? ComprobanteId { get; set; }
        public int? PagoId { get; set; }
        public int? AnticipoId { get; set; }
    }

    public class EstadoCuentaProveedor
    {
        public int ProveedorId { get; set; }
        public string ProveedorNombre { get; set; } = string.Empty;
        public string? ProveedorCuit { get; set; }
        public List<MovimientoCuentaPorPagar> Movimientos { get; set; } = new();
        /// <summary>Deuda neta con el proveedor: facturas y notas de débito menos notas de crédito menos pagos vigentes.</summary>
        public decimal SaldoNeto { get; set; }
        /// <summary>Suma de saldos pendientes de facturas y notas de débito.</summary>
        public decimal SaldoComprobantes { get; set; }
        /// <summary>Notas de crédito vigentes aún sin aplicar a facturas.</summary>
        public decimal NotasCreditoSinAplicar { get; set; }
        /// <summary>Anticipos vigentes con saldo a favor aún sin aplicar a facturas (ya forman parte del saldo neto).</summary>
        public decimal AnticiposSinAplicar { get; set; }
    }

    public class AntiguedadDeudaProveedor
    {
        public int ProveedorId { get; set; }
        public string ProveedorNombre { get; set; } = string.Empty;
        /// <summary>No vencido (incluye vence hoy y los comprobantes sin fecha de vencimiento).</summary>
        public decimal PorVencer { get; set; }
        public decimal Vencido1a30 { get; set; }
        public decimal Vencido31a60 { get; set; }
        public decimal VencidoMas60 { get; set; }
        public decimal Total => PorVencer + Vencido1a30 + Vencido31a60 + VencidoMas60;
    }

    public class AntiguedadDeuda
    {
        public DateTime FechaCorte { get; set; }
        public List<AntiguedadDeudaProveedor> Proveedores { get; set; } = new();
        public decimal PorVencer => Proveedores.Sum(p => p.PorVencer);
        public decimal Vencido1a30 => Proveedores.Sum(p => p.Vencido1a30);
        public decimal Vencido31a60 => Proveedores.Sum(p => p.Vencido31a60);
        public decimal VencidoMas60 => Proveedores.Sum(p => p.VencidoMas60);
        public decimal Total => Proveedores.Sum(p => p.Total);
    }

    public record ProveedorOpcion(int Id, string Nombre);

    public record OrdenCompraOpcion(int Id, string Numero);

    public record ChequeOpcion(int Id, string Descripcion);

    // ---------------- D-03 Tesorería administrativa: orígenes de fondos y anticipos ----------------

    public class OrigenFondosRequest
    {
        public int? Id { get; set; }
        public string? Codigo { get; set; }
        public string? Nombre { get; set; }
        public TipoOrigenFondos Tipo { get; set; } = TipoOrigenFondos.Otro;
        public string? Descripcion { get; set; }
        public bool Activo { get; set; } = true;
        public int Orden { get; set; }
        /// <summary>Control de concurrencia optimista (solo en edición).</summary>
        public byte[]? RowVersion { get; set; }
    }

    public class OrigenFondosItem
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public TipoOrigenFondos Tipo { get; set; }
        public string? Descripcion { get; set; }
        public bool Activo { get; set; }
        public int Orden { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        /// <summary>Cantidad de pagos y anticipos vigentes o anulados que lo usan (si &gt; 0 no se borra: se desactiva).</summary>
        public int Movimientos { get; set; }
    }

    /// <summary>Egresos vigentes (pagos y anticipos no anulados) de un origen de fondos en un período.</summary>
    public class EgresosOrigenFondos
    {
        public int? OrigenFondosId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public decimal Pagos { get; set; }
        public decimal Anticipos { get; set; }
        public decimal Total => Pagos + Anticipos;
        public int Cantidad { get; set; }
    }

    public class ResumenTesoreria
    {
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public List<EgresosOrigenFondos> Origenes { get; set; } = new();
        public decimal Total => Origenes.Sum(o => o.Total);
    }

    public class AnticipoProveedorRequest
    {
        public int ProveedorId { get; set; }
        public DateTime? Fecha { get; set; }
        public decimal Monto { get; set; }
        public int? OrigenFondosId { get; set; }
        public MedioPagoProveedor Medio { get; set; } = MedioPagoProveedor.Transferencia;
        public string? Referencia { get; set; }
        public int? ChequeId { get; set; }
        public string? Observaciones { get; set; }
        public string? ClaveIdempotencia { get; set; }
    }

    public class AnticipoAplicacionItem
    {
        public int Id { get; set; }
        public int AnticipoId { get; set; }
        public int ComprobanteId { get; set; }
        public string ComprobanteNumero { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public DateTime Fecha { get; set; }
        public bool Anulada { get; set; }
        public string? MotivoAnulacion { get; set; }
    }

    public class AnticipoProveedorItem
    {
        public int Id { get; set; }
        public int ProveedorId { get; set; }
        public string ProveedorNombre { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public decimal Monto { get; set; }
        /// <summary>Saldo a favor aún sin aplicar (0 si está anulado).</summary>
        public decimal SaldoSinAplicar { get; set; }
        public int OrigenFondosId { get; set; }
        public string OrigenFondosNombre { get; set; } = string.Empty;
        public MedioPagoProveedor Medio { get; set; }
        public string? Referencia { get; set; }
        public int? ChequeId { get; set; }
        public string? ChequeNumero { get; set; }
        public string? Observaciones { get; set; }
        public bool Anulado { get; set; }
        public string? AnuladoPor { get; set; }
        public DateTime? FechaAnulacion { get; set; }
        public string? MotivoAnulacion { get; set; }
        public string? CreadoPor { get; set; }
        public List<AnticipoAplicacionItem> Aplicaciones { get; set; } = new();
    }
}
