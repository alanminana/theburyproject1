using TheBuryProject.Services.Models;

namespace TheBuryProject.ViewModels
{
    /// <summary>D-03: estado de pantalla de los listados y formularios de cuentas por pagar (sin lógica de negocio).</summary>
    public class CuentaPorPagarIndexViewModel
    {
        public FiltroComprobantesProveedor Filtro { get; set; } = new();
        public ListadoComprobantesProveedor Listado { get; set; } = new();
        public List<ProveedorOpcion> Proveedores { get; set; } = new();
        public bool PuedeRegistrarComprobantes { get; set; }
        public bool PuedeRegistrarPagos { get; set; }
    }

    public class ComprobanteProveedorFormViewModel
    {
        public ComprobanteProveedorRequest Datos { get; set; } = new();
        public List<ProveedorOpcion> Proveedores { get; set; } = new();
        public ProveedorOpcion? Proveedor { get; set; }
        public List<OrdenCompraOpcion> OrdenesCompra { get; set; } = new();
    }

    public class PagoProveedorFormViewModel
    {
        public PagoProveedorRequest Datos { get; set; } = new();
        public List<ProveedorOpcion> Proveedores { get; set; } = new();
        public ProveedorOpcion? Proveedor { get; set; }
        public List<ComprobanteProveedorItem> Pendientes { get; set; } = new();
        public List<ChequeOpcion> Cheques { get; set; } = new();
        /// <summary>Orígenes de fondos activos (Tesorería administrativa).</summary>
        public List<OrigenFondosItem> Origenes { get; set; } = new();
    }

    public class AnticipoFormViewModel
    {
        public AnticipoProveedorRequest Datos { get; set; } = new();
        public List<ProveedorOpcion> Proveedores { get; set; } = new();
        public ProveedorOpcion? Proveedor { get; set; }
        public List<ChequeOpcion> Cheques { get; set; } = new();
        public List<OrigenFondosItem> Origenes { get; set; } = new();
    }

    public class AplicarAnticipoViewModel
    {
        public AnticipoProveedorItem Anticipo { get; set; } = new();
        public List<ComprobanteProveedorItem> Pendientes { get; set; } = new();
        public List<ImputacionRequest> Imputaciones { get; set; } = new();
        public string ClaveIdempotencia { get; set; } = string.Empty;
    }

    public class TesoreriaIndexViewModel
    {
        public ResumenTesoreria Resumen { get; set; } = new();
        public List<OrigenFondosItem> Origenes { get; set; } = new();
        public bool PuedeConfigurar { get; set; }
    }

    public class OrigenFondosFormViewModel
    {
        public OrigenFondosRequest Datos { get; set; } = new();
        public bool EsEdicion => Datos.Id.HasValue;
        public int Movimientos { get; set; }
    }

    public class ImputarNotaCreditoViewModel
    {
        public ComprobanteProveedorItem NotaCredito { get; set; } = new();
        public List<ComprobanteProveedorItem> Pendientes { get; set; } = new();
        public List<ImputacionRequest> Imputaciones { get; set; } = new();
        public string ClaveIdempotencia { get; set; } = string.Empty;
    }

    public class EstadoCuentaViewModel
    {
        public EstadoCuentaProveedor Cuenta { get; set; } = new();
        public List<ComprobanteProveedorItem> Pendientes { get; set; } = new();
        public List<PagoProveedorItem> Pagos { get; set; } = new();
        public List<AnticipoProveedorItem> Anticipos { get; set; } = new();
        public bool PuedeRegistrarComprobantes { get; set; }
        public bool PuedeRegistrarPagos { get; set; }
    }

    public class AntiguedadDeudaViewModel
    {
        public AntiguedadDeuda Antiguedad { get; set; } = new();
        public List<ProveedorOpcion> Proveedores { get; set; } = new();
        public int? ProveedorId { get; set; }
    }
}
