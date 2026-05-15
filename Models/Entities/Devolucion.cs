using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Registro de devolución de productos vendidos
/// </summary>
public class Devolucion  : AuditableEntity
{
    [Required]
    public int VentaId { get; set; }

    [Required]
    public int ClienteId { get; set; }

    [Required]
    [StringLength(20)]
    public string NumeroDevolucion { get; set; } = string.Empty;

    [Required]
    public DateTime FechaDevolucion { get; set; } = DateTime.UtcNow;

    [Required]
    public MotivoDevolucion Motivo { get; set; }

    [Required]
    [StringLength(1000)]
    public string Descripcion { get; set; } = string.Empty;

    [Required]
    public EstadoDevolucion Estado { get; set; } = EstadoDevolucion.Pendiente;

    [Required]
    public TipoResolucionDevolucion TipoResolucion { get; set; } = TipoResolucionDevolucion.NotaCredito;

    public bool RegistrarEgresoCaja { get; set; } = false;

    public bool RequiereRMA { get; set; } = false;

    public int? RMAId { get; set; }

    public decimal TotalDevolucion { get; set; }

    public bool NotaCreditoGenerada { get; set; } = false;

    public int? NotaCreditoId { get; set; }



    [StringLength(500)]
    public string? ObservacionesInternas { get; set; }

    [StringLength(50)]
    public string? AprobadoPor { get; set; }

    public DateTime? FechaAprobacion { get; set; }

    // Navegación
    public virtual Venta Venta { get; set; } = null!;
    public virtual Cliente Cliente { get; set; } = null!;
    public virtual RMA? RMA { get; set; }
    public virtual NotaCredito? NotaCredito { get; set; }
    public virtual ICollection<DevolucionDetalle> Detalles { get; set; } = new List<DevolucionDetalle>();
}

/// <summary>
/// Detalle de ítems devueltos
/// </summary>
public class DevolucionDetalle  : AuditableEntity
{
    [Required]
    public int DevolucionId { get; set; }

    [Required]
    public int ProductoId { get; set; }

    [Required]
    public int Cantidad { get; set; }

    [Required]
    public decimal PrecioUnitario { get; set; }

    [Required]
    public decimal Subtotal { get; set; }

    [Required]
    public EstadoProductoDevuelto EstadoProducto { get; set; }

    public bool TieneGarantia { get; set; } = false;

    public int? GarantiaId { get; set; }

    public bool AccesoriosCompletos { get; set; } = true;

    [StringLength(500)]
    public string? AccesoriosFaltantes { get; set; }

    [StringLength(500)]
    public string? ObservacionesTecnicas { get; set; }

    public AccionProducto AccionRecomendada { get; set; } = AccionProducto.Cuarentena;

    // Trazabilidad individual (Fase 10.3 — nullable para compatibilidad con devoluciones sin unidad)
    public int? ProductoUnidadId { get; set; }

    // Navegación
    public virtual Devolucion Devolucion { get; set; } = null!;
    public virtual Producto Producto { get; set; } = null!;
    public virtual Garantia? Garantia { get; set; }
    public virtual ProductoUnidad? ProductoUnidad { get; set; }
}

/// <summary>
/// Registro de garantía de producto
/// </summary>
public class Garantia  : AuditableEntity
{
    [Required]
    public int VentaDetalleId { get; set; }

    [Required]
    public int ProductoId { get; set; }

    [Required]
    public int ClienteId { get; set; }

    [Required]
    [StringLength(20)]
    public string NumeroGarantia { get; set; } = string.Empty;

    [Required]
    public DateTime FechaInicio { get; set; }

    [Required]
    public DateTime FechaVencimiento { get; set; }

    public int MesesGarantia { get; set; } = 12;

    [Required]
    public EstadoGarantia Estado { get; set; } = EstadoGarantia.Vigente;

    [StringLength(1000)]
    public string? CondicionesGarantia { get; set; }

    public bool GarantiaExtendida { get; set; } = false;

    [StringLength(500)]
    public string? ObservacionesActivacion { get; set; }

    // Navegación
    public virtual VentaDetalle VentaDetalle { get; set; } = null!;
    public virtual Producto Producto { get; set; } = null!;
    public virtual Cliente Cliente { get; set; } = null!;
}

/// <summary>
/// RMA - Return Merchandise Authorization con proveedor
/// </summary>
public class RMA  : AuditableEntity
{
    [Required]
    public int ProveedorId { get; set; }

    [Required]
    public int DevolucionId { get; set; }

    [Required]
    [StringLength(20)]
    public string NumeroRMA { get; set; } = string.Empty;

    [Required]
    public DateTime FechaSolicitud { get; set; } = DateTime.UtcNow;

    [Required]
    public EstadoRMA Estado { get; set; } = EstadoRMA.Pendiente;

    [Required]
    [StringLength(1000)]
    public string MotivoSolicitud { get; set; } = string.Empty;

    public DateTime? FechaAprobacion { get; set; }

    [StringLength(50)]
    public string? NumeroRMAProveedor { get; set; }

    public DateTime? FechaEnvio { get; set; }

    [StringLength(50)]
    public string? NumeroGuiaEnvio { get; set; }

    public DateTime? FechaRecepcionProveedor { get; set; }

    public TipoResolucionRMA? TipoResolucion { get; set; }

    public DateTime? FechaResolucion { get; set; }

    [StringLength(1000)]
    public string? DetalleResolucion { get; set; }

    public decimal? MontoReembolso { get; set; }

    [StringLength(500)]
    public string? ObservacionesProveedor { get; set; }

    // Navegaci�n
    public virtual Proveedor Proveedor { get; set; } = null!;
    public virtual Devolucion Devolucion { get; set; } = null!;
}

/// <summary>
/// Nota de cr�dito generada por devoluci�n
/// </summary>
public class NotaCredito  : AuditableEntity
{
    [Required]
    public int DevolucionId { get; set; }

    [Required]
    public int ClienteId { get; set; }

    [Required]
    [StringLength(20)]
    public string NumeroNotaCredito { get; set; } = string.Empty;

    [Required]
    public DateTime FechaEmision { get; set; } = DateTime.UtcNow;

    [Required]
    public decimal MontoTotal { get; set; }

    [Required]
    public EstadoNotaCredito Estado { get; set; } = EstadoNotaCredito.Vigente;

    public decimal MontoUtilizado { get; set; } = 0;

    public decimal MontoDisponible => MontoTotal - MontoUtilizado;

    public DateTime? FechaVencimiento { get; set; }

    [StringLength(500)]
    public string? Observaciones { get; set; }

    // Navegaci�n
    public virtual Devolucion Devolucion { get; set; } = null!;
    public virtual Cliente Cliente { get; set; } = null!;
}

// ============================================
// ENUMS
// ============================================

public enum MotivoDevolucion
{
    [Display(Name = "Defecto de Fábrica")]
    DefectoFabrica = 0,

    [Display(Name = "Producto Dañado")]
    ProductoDanado = 1,

    [Display(Name = "No Cumple Expectativas")]
    NoCumpleExpectativas = 2,

    [Display(Name = "Producto Incorrecto")]
    ProductoIncorrecto = 3,

    [Display(Name = "Garantía")]
    Garantia = 4,

    [Display(Name = "Arrepentimiento")]
    Arrepentimiento = 5,

    [Display(Name = "Otro")]
    Otro = 6,

    [Display(Name = "Faltante / Incompleto")]
    Faltante = 7,

    [Display(Name = "Error de Despacho")]
    ErrorDespacho = 8
}

public enum EstadoDevolucion
{
    [Display(Name = "Pendiente")]
    Pendiente = 0,

    [Display(Name = "En Revisión")]
    EnRevision = 1,

    [Display(Name = "Aprobada")]
    Aprobada = 2,

    [Display(Name = "Rechazada")]
    Rechazada = 3,

    [Display(Name = "Completada")]
    Completada = 4,

    [Display(Name = "Cancelada")]
    Cancelada = 5
}

public enum EstadoProductoDevuelto
{
    [Display(Name = "Nuevo")]
    Nuevo = 0,

    [Display(Name = "Usado - Buen Estado")]
    UsadoBuenEstado = 1,

    [Display(Name = "Usado - Con Detalles")]
    UsadoConDetalles = 2,

    [Display(Name = "Defectuoso")]
    Defectuoso = 3,

    [Display(Name = "Dañado")]
    Danado = 4,

    [Display(Name = "Nuevo Sellado")]
    NuevoSellado = 5,

    [Display(Name = "Abierto Sin Uso")]
    AbiertoSinUso = 6,

    [Display(Name = "Marcado / Detalle Estético")]
    Marcado = 7,

    [Display(Name = "Incompleto")]
    Incompleto = 8
}

public enum TipoResolucionDevolucion
{
    [Display(Name = "Nota de Crédito")]
    NotaCredito = 0,

    [Display(Name = "Reembolso de Dinero")]
    ReembolsoDinero = 1,

    [Display(Name = "Cambio por el Mismo Producto")]
    CambioMismoProducto = 2,

    [Display(Name = "Cambio por Otro Producto")]
    CambioOtroProducto = 3
}

public enum AccionProducto
{
    [Display(Name = "Reintegrar a Stock")]
    ReintegrarStock = 0,

    [Display(Name = "Cuarentena")]
    Cuarentena = 1,

    [Display(Name = "Reparación")]
    Reparacion = 2,

    [Display(Name = "Devolver a Proveedor (RMA)")]
    DevolverProveedor = 3,

    [Display(Name = "Descarte")]
    Descarte = 4
}

public enum EstadoGarantia
{
    [Display(Name = "Vigente")]
    Vigente = 0,

    [Display(Name = "Vencida")]
    Vencida = 1,

    [Display(Name = "En Uso")]
    EnUso = 2,

    [Display(Name = "Utilizada")]
    Utilizada = 3,

    [Display(Name = "Cancelada")]
    Cancelada = 4
}

public enum EstadoRMA
{
    [Display(Name = "Pendiente")]
    Pendiente = 0,

    [Display(Name = "Aprobado por Proveedor")]
    AprobadoProveedor = 1,

    [Display(Name = "En Tr�nsito")]
    EnTransito = 2,

    [Display(Name = "Recibido por Proveedor")]
    RecibidoProveedor = 3,

    [Display(Name = "En Evaluaci�n")]
    EnEvaluacion = 4,

    [Display(Name = "Resuelto")]
    Resuelto = 5,

    [Display(Name = "Rechazado")]
    Rechazado = 6
}

public enum TipoResolucionRMA
{
    [Display(Name = "Reemplazo")]
    Reemplazo = 0,

    [Display(Name = "Reparaci�n")]
    Reparacion = 1,

    [Display(Name = "Reembolso Total")]
    ReembolsoTotal = 2,

    [Display(Name = "Reembolso Parcial")]
    ReembolsoParcial = 3,

    [Display(Name = "Cr�dito")]
    Credito = 4
}

public enum EstadoNotaCredito
{
    [Display(Name = "Vigente")]
    Vigente = 0,

    [Display(Name = "Utilizada Parcialmente")]
    UtilizadaParcialmente = 1,

    [Display(Name = "Utilizada Totalmente")]
    UtilizadaTotalmente = 2,

    [Display(Name = "Vencida")]
    Vencida = 3,

    [Display(Name = "Cancelada")]
    Cancelada = 4
}
