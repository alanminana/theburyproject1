using TheBuryProject.Models.Entities;

namespace TheBuryProject.ViewModels;

/// <summary>
/// ViewModel consolidado del módulo de postventa.
/// </summary>
public class DevolucionIndexViewModel
{
    public string ActiveTab { get; set; } = "devoluciones";
    public string Search { get; set; } = string.Empty;
    public string EstadoFilter { get; set; } = string.Empty;
    public string ResolucionFilter { get; set; } = string.Empty;
    public string GarantiaEstadoFilter { get; set; } = string.Empty;
    public string GarantiaVentanaFilter { get; set; } = string.Empty;
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalFiltered { get; set; }
    public int TotalPages { get; set; } = 1;
    public int FirstItemIndex { get; set; }
    public int LastItemIndex { get; set; }

    public List<Devolucion> TodasDevoluciones { get; set; } = new();
    public List<Devolucion> Pendientes { get; set; } = new();
    public List<Devolucion> EnRevision { get; set; } = new();
    public List<Devolucion> Aprobadas { get; set; } = new();
    public List<Devolucion> Completadas { get; set; } = new();
    public List<Devolucion> Rechazadas { get; set; } = new();
    public List<Devolucion> DevolucionesPagina { get; set; } = new();

    public List<Garantia> TodasGarantias { get; set; } = new();
    public List<Garantia> Vigentes { get; set; } = new();
    public List<Garantia> ProximasVencer { get; set; } = new();
    public List<Garantia> Vencidas { get; set; } = new();
    public List<Garantia> EnUso { get; set; } = new();
    public List<Garantia> GarantiasPagina { get; set; } = new();

    public int TotalPendientes { get; set; }
    public int TotalAprobadas { get; set; }
    public int TotalRechazadas { get; set; }
    public int TotalCompletadas { get; set; }
    public decimal MontoTotalMes { get; set; }

    public int TotalGarantiasVigentes { get; set; }
    public int TotalGarantiasProximasVencer { get; set; }
    public int TotalGarantiasVencidas { get; set; }
    public int TotalGarantiasEnUso { get; set; }

    public int RmasPendientes { get; set; }
    public int ReembolsosPendientesCaja { get; set; }
    public decimal MontoReembolsosPendientesCaja { get; set; }
    public Dictionary<MotivoDevolucion, int> MotivosFrecuentes { get; set; } = new();
}
