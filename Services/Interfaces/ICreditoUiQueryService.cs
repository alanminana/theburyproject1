using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces;

public interface ICreditoUiQueryService
{
    List<CreditoClienteIndexViewModel> AgruparCreditosPorCliente(IEnumerable<CreditoViewModel> creditos);

    string ResolverEstadoConsolidado(IReadOnlyCollection<CreditoViewModel> creditos, int cuotasVencidas);
}
