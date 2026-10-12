namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Datos de un modal de decisión sobre una venta (autorizar, rechazar, cancelar) en Venta/Details.
    /// Cada modal postea a la misma acción MVC que la página independiente equivalente.
    /// </summary>
    public sealed record VentaDecisionModalViewModel(
        string Name,
        string Action,
        int VentaId,
        string Title,
        string Description,
        string MotivoLabel,
        string MotivoPlaceholder,
        string ConfirmText,
        string ConfirmIcon,
        string ConfirmButtonClass,
        string? RequiredCheckText = null);
}
