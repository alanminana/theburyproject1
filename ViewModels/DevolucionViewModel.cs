using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.ViewModels;

/// <summary>
/// ViewModel para crear nueva devolución
/// </summary>
public class CrearDevolucionViewModel
{
    [Required(ErrorMessage = "La venta es obligatoria")]
    [Display(Name = "Venta")]
    public int VentaId { get; set; }

    [Required(ErrorMessage = "El cliente es obligatorio")]
    [Display(Name = "Cliente")]
    public int ClienteId { get; set; }

    [Required(ErrorMessage = "El motivo es obligatorio")]
    [Display(Name = "Motivo de Devolución")]
    public MotivoDevolucion Motivo { get; set; }

    [Required(ErrorMessage = "La descripción es obligatoria")]
    [StringLength(1000, MinimumLength = 20, ErrorMessage = "La descripción debe tener entre 20 y 1000 caracteres")]
    [Display(Name = "Descripción")]
    public string Descripcion { get; set; } = string.Empty;

    [Display(Name = "Productos a Devolver")]
    public List<ProductoDevolucionViewModel> Productos { get; set; } = new();

    // Info de la venta (solo lectura)
    public string? NumeroVenta { get; set; }
    public string? ClienteNombre { get; set; }
    public DateTime? FechaVenta { get; set; }
    public decimal? TotalVenta { get; set; }
    public int? DiasDesdeVenta { get; set; }
    public bool? PuedeDevolver { get; set; }
}

/// <summary>
/// ViewModel para iniciar una devolución rápida desde la grilla/detalle de ventas.
/// </summary>
public class CrearDevolucionRapidaViewModel
{
    [Required]
    public int VentaId { get; set; }

    [Required]
    public int ClienteId { get; set; }

    [Required(ErrorMessage = "Debe seleccionar un motivo")]
    [Display(Name = "Motivo")]
    public MotivoDevolucion Motivo { get; set; }

    [Required(ErrorMessage = "Debe seleccionar cómo se resuelve la devolución")]
    [Display(Name = "Resolución")]
    public TipoResolucionDevolucion TipoResolucion { get; set; } = TipoResolucionDevolucion.NotaCredito;

    [Display(Name = "Registrar egreso en caja")]
    public bool RegistrarEgresoCaja { get; set; }

    [Required(ErrorMessage = "La descripción es obligatoria")]
    [StringLength(1000, MinimumLength = 20, ErrorMessage = "La descripción debe tener entre 20 y 1000 caracteres")]
    [Display(Name = "Descripción")]
    public string Descripcion { get; set; } = string.Empty;

    public List<CrearDevolucionRapidaItemViewModel> Items { get; set; } = new();
}

public class CrearDevolucionRapidaItemViewModel
{
    public int ProductoId { get; set; }
    public string ProductoNombre { get; set; } = string.Empty;
    public string? ProductoCodigo { get; set; }
    public int CantidadDisponible { get; set; }
    public decimal PrecioUnitario { get; set; }

    public bool Seleccionado { get; set; }

    [Range(0, int.MaxValue)]
    public int CantidadDevolver { get; set; }

    [Required]
    public EstadoProductoDevuelto EstadoProducto { get; set; } = EstadoProductoDevuelto.Nuevo;
}

/// <summary>
/// ViewModel para producto en devolución
/// </summary>
public class ProductoDevolucionViewModel
{
    public int ProductoId { get; set; }
    public string ProductoNombre { get; set; } = string.Empty;
    public int CantidadComprada { get; set; }
    public int CantidadDevolver { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal => CantidadDevolver * PrecioUnitario;

    [Required]
    [Display(Name = "Estado del Producto")]
    public EstadoProductoDevuelto EstadoProducto { get; set; } = EstadoProductoDevuelto.UsadoBuenEstado;

    [Display(Name = "Accesorios Completos")]
    public bool AccesoriosCompletos { get; set; } = true;

    [Display(Name = "Accesorios Faltantes")]
    [StringLength(500)]
    public string? AccesoriosFaltantes { get; set; }

    [Display(Name = "Tiene Garantía")]
    public bool TieneGarantia { get; set; }

    [Display(Name = "Observaciones Técnicas")]
    [StringLength(500)]
    public string? ObservacionesTecnicas { get; set; }
}

/// <summary>
/// ViewModel para lista de devoluciones
/// </summary>
public class DevolucionesListViewModel
{
    public List<Devolucion> Pendientes { get; set; } = new();
    public List<Devolucion> EnRevision { get; set; } = new();
    public List<Devolucion> Aprobadas { get; set; } = new();
    public List<Devolucion> Completadas { get; set; } = new();

    public int TotalPendientes { get; set; }
    public int TotalAprobadas { get; set; }
    public int TotalRechazadas { get; set; }
    public decimal MontoTotalMes { get; set; }
}

/// <summary>
/// ViewModel para detalles de devolución
/// </summary>
public class DevolucionDetallesViewModel
{
    public Devolucion Devolucion { get; set; } = null!;
    public List<DevolucionDetalle> Detalles { get; set; } = new();
    public NotaCredito? NotaCredito { get; set; }
    public RMA? RMA { get; set; }

    [Display(Name = "Observaciones Internas")]
    [StringLength(500)]
    public string? ObservacionesInternas { get; set; }

    [Display(Name = "Requiere RMA")]
    public bool RequiereRMA { get; set; }

    [Display(Name = "Proveedor para RMA")]
    public int? ProveedorId { get; set; }
}

/// <summary>
/// ViewModel para gestión de garantías
/// </summary>
public class GarantiasListViewModel
{
    public List<Garantia> Vigentes { get; set; } = new();
    public List<Garantia> ProximasVencer { get; set; } = new();
    public List<Garantia> Vencidas { get; set; } = new();
    public List<Garantia> EnUso { get; set; } = new();

    public int TotalVigentes { get; set; }
    public int TotalProximasVencer { get; set; }
}

/// <summary>
/// ViewModel para crear garantía
/// </summary>
public class CrearGarantiaViewModel
{
    [Required]
    [Display(Name = "Venta Detalle")]
    public int VentaDetalleId { get; set; }

    [Required]
    [Display(Name = "Producto")]
    public int ProductoId { get; set; }

    [Required]
    [Display(Name = "Cliente")]
    public int ClienteId { get; set; }

    [Required]
    [Display(Name = "Fecha de Inicio")]
    [DataType(DataType.Date)]
    public DateTime FechaInicio { get; set; } = DateTime.UtcNow;

    [Required]
    [Range(1, 60, ErrorMessage = "Los meses de garantía deben estar entre 1 y 60")]
    [Display(Name = "Meses de Garantía")]
    public int MesesGarantia { get; set; } = 12;

    [Display(Name = "Garantía Extendida")]
    public bool GarantiaExtendida { get; set; } = false;

    [Display(Name = "Condiciones de Garantía")]
    [StringLength(1000)]
    public string? CondicionesGarantia { get; set; }

    [Display(Name = "Observaciones")]
    [StringLength(500)]
    public string? ObservacionesActivacion { get; set; }
}

/// <summary>
/// ViewModel para crear RMA
/// </summary>
public class CrearRMAViewModel
{
    [Required]
    [Display(Name = "Devolución")]
    public int DevolucionId { get; set; }

    [Required]
    [Display(Name = "Proveedor")]
    public int ProveedorId { get; set; }

    [Required]
    [StringLength(1000, MinimumLength = 20)]
    [Display(Name = "Motivo de Solicitud")]
    public string MotivoSolicitud { get; set; } = string.Empty;

    // Info de la devolución (solo lectura)
    public string? NumeroDevolucion { get; set; }
    public string? ClienteNombre { get; set; }
    public decimal? MontoDevolucion { get; set; }
}

/// <summary>
/// ViewModel para lista de RMAs
/// </summary>
public class RMAsListViewModel
{
    public List<RMA> Pendientes { get; set; } = new();
    public List<RMA> EnProceso { get; set; } = new();
    public List<RMA> Resueltos { get; set; } = new();

    public int TotalPendientes { get; set; }
    public int TotalEnProceso { get; set; }
    public int TotalResueltos { get; set; }
}

/// <summary>
/// ViewModel para gestionar RMA
/// </summary>
public class GestionarRMAViewModel
{
    public RMA RMA { get; set; } = null!;

    [Display(Name = "Número RMA Proveedor")]
    [StringLength(50)]
    public string? NumeroRMAProveedor { get; set; }

    [Display(Name = "Número de Guía")]
    [StringLength(50)]
    public string? NumeroGuiaEnvio { get; set; }

    [Display(Name = "Tipo de Resolución")]
    public TipoResolucionRMA? TipoResolucion { get; set; }

    [Display(Name = "Monto Reembolso")]
    [DataType(DataType.Currency)]
    public decimal? MontoReembolso { get; set; }

    [Display(Name = "Detalle de Resolución")]
    [StringLength(1000)]
    public string? DetalleResolucion { get; set; }

    [Display(Name = "Observaciones del Proveedor")]
    [StringLength(500)]
    public string? ObservacionesProveedor { get; set; }
}

/// <summary>
/// ViewModel para notas de crédito del cliente
/// </summary>
public class NotasCreditoClienteViewModel
{
    public int ClienteId { get; set; }
    public string ClienteNombre { get; set; } = string.Empty;
    public List<NotaCredito> NotasVigentes { get; set; } = new();
    public List<NotaCredito> NotasUtilizadas { get; set; } = new();
    public decimal CreditoTotalDisponible { get; set; }
}

/// <summary>
/// ViewModel para estadísticas de devoluciones
/// </summary>
public class EstadisticasDevolucionViewModel
{
    public DateTime FechaDesde { get; set; } = DateTime.UtcNow.AddMonths(-1);
    public DateTime FechaHasta { get; set; } = DateTime.UtcNow;

    public int TotalDevoluciones { get; set; }
    public decimal MontoTotalDevuelto { get; set; }
    public Dictionary<MotivoDevolucion, int> DevolucionesPorMotivo { get; set; } = new();
    public List<Producto> ProductosMasDevueltos { get; set; } = new();
    public int RMAsPendientes { get; set; }
    public decimal PromedioTiempoResolucion { get; set; }
}
