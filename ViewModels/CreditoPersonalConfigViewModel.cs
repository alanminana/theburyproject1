namespace TheBuryProject.ViewModels;

/// <summary>
/// ViewModel para recibir configuración completa de crédito personal
/// </summary>
public class CreditoPersonalConfigViewModel
{
    public DefaultsGlobalesViewModel? DefaultsGlobales { get; set; }
    public List<PerfilCreditoViewModel>? Perfiles { get; set; }
}

public class DefaultsGlobalesViewModel
{
    public decimal TasaMensual { get; set; }
    public decimal GastosAdministrativos { get; set; }
    public int MinCuotas { get; set; }
    public int MaxCuotas { get; set; }
}
