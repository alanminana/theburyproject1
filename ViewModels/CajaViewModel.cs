using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels;

/// <summary>
/// Constantes compartidas para validación de caja
/// </summary>
public static class CajaConstants
{
    /// <summary>
    /// Tolerancia para diferencias de arqueo (en pesos)
    /// </summary>
    public const decimal TOLERANCIA_DIFERENCIA = 0.01m;
}

/// <summary>
/// ViewModel para crear/editar caja
/// </summary>
public class CajaViewModel
{
    public int Id { get; set; }

    public byte[]? RowVersion { get; set; }

    [Required(ErrorMessage = "El código es obligatorio")]
    [StringLength(50)]
    [Display(Name = "Código")]
    public string Codigo { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre es obligatorio")]
    [StringLength(100)]
    [Display(Name = "Nombre")]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Descripción")]
    public string? Descripcion { get; set; }

    [StringLength(100)]
    [Display(Name = "Sucursal")]
    public string? Sucursal { get; set; }

    [StringLength(100)]
    [Display(Name = "Ubicación")]
    public string? Ubicacion { get; set; }

    [Display(Name = "Activa")]
    public bool Activa { get; set; } = true;

    [Display(Name = "Estado")]
    public EstadoCaja Estado { get; set; }

    // ── Padrón de usuarios habilitados (solo Edit) ──
    // Base del enforcement "cada usuario sobre su propia caja": vendedores que venden contra
    // la caja y cajeros que la operan. RBAC define el verbo; esta membresía define la caja.

    /// <summary>Ids de usuarios habilitados (vendedores y cajeros) seleccionados para esta caja.</summary>
    public List<string> VendedorIds { get; set; } = new();

    /// <summary>Usuarios con rol Vendedor disponibles para asignar (para render del formulario).</summary>
    public List<TheBuryProject.Services.Interfaces.UsuarioSelectItem> VendedoresDisponibles { get; set; } = new();

    /// <summary>Usuarios con rol Cajero disponibles para asignar (para render del formulario).</summary>
    public List<TheBuryProject.Services.Interfaces.UsuarioSelectItem> CajerosDisponibles { get; set; } = new();

    /// <summary>
    /// Marca que el formulario gestionó el padrón de usuarios (lo envía el modal de edición).
    /// Evita que flujos que no incluyen el campo borren las asignaciones existentes.
    /// </summary>
    public bool VendedoresGestionados { get; set; }
}

/// <summary>
/// ViewModel para apertura de caja
/// </summary>
public class AbrirCajaViewModel
{
    [Required(ErrorMessage = "La caja es obligatoria")]
    [Display(Name = "Caja")]
    public int CajaId { get; set; }

    [Required(ErrorMessage = "El monto inicial es obligatorio")]
    [Range(0, double.MaxValue, ErrorMessage = "El monto debe ser mayor o igual a 0")]
    [Display(Name = "Monto Inicial")]
    public decimal MontoInicial { get; set; }

    [StringLength(500)]
    [Display(Name = "Observaciones")]
    public string? ObservacionesApertura { get; set; }

    // Para mostrar en el formulario
    public string? CajaNombre { get; set; }
    public string? CajaCodigo { get; set; }
}

/// <summary>
/// ViewModel para registrar movimiento de caja
/// </summary>
public class MovimientoCajaViewModel
{
    public int Id { get; set; }

    [Required]
    public int AperturaCajaId { get; set; }

    [Required(ErrorMessage = "El tipo es obligatorio")]
    [Display(Name = "Tipo")]
    public TipoMovimientoCaja Tipo { get; set; }

    [Required(ErrorMessage = "El concepto es obligatorio")]
    [Display(Name = "Concepto")]
    public ConceptoMovimientoCaja Concepto { get; set; }

    [Required(ErrorMessage = "El monto es obligatorio")]
    [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a 0")]
    [Display(Name = "Monto")]
    public decimal Monto { get; set; }

    [Required(ErrorMessage = "La descripción es obligatoria")]
    [StringLength(500)]
    [Display(Name = "Descripción")]
    public string Descripcion { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Referencia")]
    public string? Referencia { get; set; }

    [StringLength(500)]
    [Display(Name = "Observaciones")]
    public string? Observaciones { get; set; }

    // Info de la apertura (solo lectura)
    public string? CajaNombre { get; set; }
    public decimal? SaldoActual { get; set; }
}

/// <summary>
/// ViewModel para cerrar caja con arqueo
/// </summary>
public class CerrarCajaViewModel
{
    [Required]
    public int AperturaCajaId { get; set; }

    // Datos del sistema (calculados)
    public decimal MontoInicialSistema { get; set; }
    public decimal TotalIngresosSistema { get; set; }
    public decimal TotalEgresosSistema { get; set; }
    public decimal MontoEsperadoSistema { get; set; }

    // Arqueo físico (ingresado por usuario)
    [Required(ErrorMessage = "El efectivo contado es obligatorio")]
    [Range(0, double.MaxValue, ErrorMessage = "El monto debe ser mayor o igual a 0")]
    [Display(Name = "Efectivo Contado")]
    public decimal EfectivoContado { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "El monto debe ser mayor o igual a 0")]
    [Display(Name = "Cheques Contados")]
    public decimal ChequesContados { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "El monto debe ser mayor o igual a 0")]
    [Display(Name = "Vales/Otros")]
    public decimal ValesContados { get; set; }

    [Display(Name = "Total Real")]
    public decimal MontoTotalReal => EfectivoContado + ChequesContados + ValesContados;

    [Display(Name = "Diferencia")]
    public decimal Diferencia => MontoTotalReal - MontoEsperadoSistema;

    public bool TieneDiferencia => Math.Abs(Diferencia) > CajaConstants.TOLERANCIA_DIFERENCIA;

    [StringLength(1000)]
    [Display(Name = "Justificación de Diferencia")]
    public string? JustificacionDiferencia { get; set; }

    [StringLength(500)]
    [Display(Name = "Observaciones")]
    public string? ObservacionesCierre { get; set; }

    [StringLength(2000)]
    [Display(Name = "Detalle de Arqueo (billetes/monedas)")]
    public string? DetalleArqueo { get; set; }

    // Info de la apertura (solo lectura)
    public string? CajaNombre { get; set; }
    public DateTime? FechaApertura { get; set; }
    public string? UsuarioApertura { get; set; }
    public List<MovimientoCaja>? Movimientos { get; set; }
}

/// <summary>
/// Resumen de caja fisica (efectivo) de una apertura, calculado por el backend.
/// Misma logica que CajaFisicaEsperada del detalle y del cierre: el frontend no recalcula.
/// </summary>
public class AperturaFisicoResumen
{
    public decimal IngresosFisicos { get; set; }
    public decimal EgresosFisicos { get; set; }
    /// <summary>Fondo inicial + ingresos efectivos - egresos efectivos.</summary>
    public decimal CajaFisicaEsperada { get; set; }
}

/// <summary>
/// ViewModel para lista de cajas
/// </summary>
public class CajasListViewModel
{
    public IList<AperturaCaja> AperturasAbiertas { get; set; } = new List<AperturaCaja>();
    public IList<Caja> CajasActivas { get; set; } = new List<Caja>();
    public IList<Caja> CajasInactivas { get; set; } = new List<Caja>();

    /// <summary>
    /// Resumen de efectivo esperado por apertura (clave: AperturaCaja.Id), calculado por el service.
    /// Index lo consume para mostrar el mismo valor que el detalle de apertura.
    /// </summary>
    public IDictionary<int, AperturaFisicoResumen> ResumenFisicoPorApertura { get; set; }
        = new Dictionary<int, AperturaFisicoResumen>();
}
/// <summary>
/// Resumen de totales agrupados por tipo de pago dentro de una apertura de caja.
/// Derivado de las ventas (TipoPago); no refleja movimientos de caja reales.
/// </summary>
public class TotalPorTipoPagoViewModel
{
    public string TipoPago { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal RecargoDebitoAplicado { get; set; }
    public int Cantidad { get; set; }
    public bool GeneraIngresoInmediato { get; set; }
}

/// <summary>
/// Resumen de movimientos reales de caja agrupados por medio de pago inferido.
/// Derivado de ConceptoMovimientoCaja y Observaciones; no de TipoPago (campo inexistente en MovimientoCaja).
/// </summary>
public class ResumenMedioPagoCajaViewModel
{
    public string MedioPago { get; set; } = string.Empty;
    public decimal TotalIngresos { get; set; }
    public decimal TotalEgresos { get; set; }
    public int CantidadMovimientos { get; set; }
}

/// <summary>
/// Resumen de mercadería (productos) efectivamente movida en un turno.
/// Agregado desde VentaDetalle de las ventas efectivas (Confirmada/Facturada/Entregada).
/// </summary>
public class MercaderiaMovidaViewModel
{
    public int ProductoId { get; set; }
    public string ProductoNombre { get; set; } = string.Empty;
    public string? ProductoCodigo { get; set; }
    /// <summary>Unidades totales vendidas del producto en el turno.</summary>
    public int CantidadTotal { get; set; }
    /// <summary>Importe final total facturado del producto (SubtotalFinal sumado).</summary>
    public decimal ImporteTotal { get; set; }
    /// <summary>Cantidad de ventas distintas que incluyeron el producto.</summary>
    public int CantidadVentas { get; set; }
}

/// <summary>
/// ViewModel para detalles de apertura de caja
/// </summary>
public class DetallesAperturaViewModel
{
    public List<TotalPorTipoPagoViewModel> TotalesPorTipoPago { get; set; } = new();
    /// <summary>
    /// Resumen real de caja por medio de pago inferido desde MovimientoCaja.
    /// Reemplaza TotalesPorTipoPago como fuente de verdad del efectivo real.
    /// </summary>
    public List<ResumenMedioPagoCajaViewModel> ResumenRealPorMedioPago { get; set; } = new();
    public AperturaCaja Apertura { get; set; } = null!;
    public List<MovimientoCaja> Movimientos { get; set; } = new();
    /// <summary>Saldo operativo: incluye todos los movimientos independientemente del estado de acreditación.</summary>
    public decimal SaldoActual { get; set; }
    /// <summary>Saldo real: solo movimientos con dinero efectivamente acreditado o en efectivo.</summary>
    public decimal SaldoReal { get; set; }
    /// <summary>Monto de ingresos pendientes de acreditación bancaria (SaldoActual - SaldoReal para ingresos).</summary>
    public decimal SaldoPendienteAcreditacion { get; set; }
    public decimal TotalIngresos { get; set; }
    public decimal TotalEgresos { get; set; }
    /// <summary>Total de ingresos que impactan la caja fisica (efectivo).</summary>
    public decimal TotalIngresosFisicos { get; set; }
    /// <summary>Total de egresos que impactan la caja fisica (efectivo).</summary>
    public decimal TotalEgresosFisicos { get; set; }
    /// <summary>Total de ingresos no fisicos: tarjeta, Mercado Pago, transferencia, cheque u otros medios digitales.</summary>
    public decimal TotalIngresosDigitales { get; set; }
    /// <summary>Total de egresos no fisicos.</summary>
    public decimal TotalEgresosDigitales { get; set; }
    /// <summary>Monto esperado en caja fisica: fondo inicial + ingresos efectivos - egresos efectivos.</summary>
    public decimal CajaFisicaEsperada { get; set; }
    /// <summary>Neto no fisico registrado en el turno.</summary>
    public decimal TotalDigitalNeto => TotalIngresosDigitales - TotalEgresosDigitales;
    public decimal TotalRecargoDebito { get; set; }
    public int CantidadMovimientos { get; set; }
    /// <summary>
    /// Ventas realizadas durante esta apertura, incluyendo las de crédito personal
    /// que no generan movimiento de caja inmediato pero deben quedar registradas como operaciones del turno.
    /// </summary>
    public List<TheBuryProject.Models.Entities.Venta> VentasDelTurno { get; set; } = new();
    /// <summary>
    /// Mercadería (productos) efectivamente movida en el turno, agregada desde las ventas efectivas.
    /// </summary>
    public List<MercaderiaMovidaViewModel> MercaderiaMovida { get; set; } = new();
}

/// <summary>
/// ViewModel para historial de cierres
/// </summary>
public class HistorialCierresViewModel
{
    public List<CierreCaja> Cierres { get; set; } = new();
    public decimal TotalDiferenciasPositivas { get; set; }
    public decimal TotalDiferenciasNegativas { get; set; }
    public int CierresConDiferencia { get; set; }
    public int TotalCierres { get; set; }
    public decimal PorcentajeCierresExactos { get; set; }
}

/// <summary>
/// ViewModel para reporte de caja
/// </summary>
public class ReporteCajaViewModel
{
    public DateTime FechaDesde { get; set; } = DateTime.Today.AddDays(-30);
    public DateTime FechaHasta { get; set; } = DateTime.Today;
    public int? CajaId { get; set; }

    public List<AperturaCaja> Aperturas { get; set; } = new();
    public decimal TotalIngresos { get; set; }
    public decimal TotalEgresos { get; set; }
    public decimal TotalDiferencias { get; set; }
    public int TotalAperturas { get; set; }
}
