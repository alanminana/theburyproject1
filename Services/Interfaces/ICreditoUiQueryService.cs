using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces;

public interface ICreditoUiQueryService
{
    List<CuotaViewModel> ObtenerCuotasPendientes(IEnumerable<CuotaViewModel>? cuotas);

    List<SelectListItem> ProyectarCuotasPendientes(IEnumerable<CuotaViewModel>? cuotas);

    string BuildCuotasJson(IEnumerable<CuotaViewModel>? cuotas);

    List<CreditoClienteIndexViewModel> AgruparCreditosPorCliente(IEnumerable<CreditoViewModel> creditos);

    string ResolverEstadoConsolidado(IReadOnlyCollection<CreditoViewModel> creditos, int cuotasVencidas);
}
