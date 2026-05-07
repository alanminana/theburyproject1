using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces;

public interface ICreditoRangoProductoService
{
    Task<CreditoRangoProductoResultado> ResolverAsync(
        VentaViewModel? venta,
        TipoPago tipoPago,
        int minBase,
        int maxBase,
        CancellationToken cancellationToken = default);
}
