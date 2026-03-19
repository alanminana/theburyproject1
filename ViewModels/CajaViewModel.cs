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
/// ViewModel para lista de cajas
/// </summary>
public class CajasListViewModel
{
    public IList<AperturaCaja> AperturasAbiertas { get; set; } = new List<AperturaCaja>();
    public IList<Caja> CajasActivas { get; set; } = new List<Caja>();
    public IList<Caja> CajasInactivas { get; set; } = new List<Caja>();
}

/// <summary>
/// ViewModel para detalles de apertura de caja
/// </summary>
public class DetallesAperturaViewModel
{
    public AperturaCaja Apertura { get; set; } = null!;
    public List<MovimientoCaja> Movimientos { get; set; } = new();
    public decimal SaldoActual { get; set; }
    public decimal TotalIngresos { get; set; }
    public decimal TotalEgresos { get; set; }
    public int CantidadMovimientos { get; set; }
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