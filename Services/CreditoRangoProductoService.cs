using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services;

public sealed class CreditoRangoProductoService : ICreditoRangoProductoService
{
    private readonly ICondicionesPagoCarritoResolver _condicionesPagoCarritoResolver;

    public CreditoRangoProductoService(ICondicionesPagoCarritoResolver condicionesPagoCarritoResolver)
    {
        _condicionesPagoCarritoResolver = condicionesPagoCarritoResolver;
    }

    public async Task<CreditoRangoProductoResultado> ResolverAsync(
        VentaViewModel? venta,
        TipoPago tipoPago,
        int minBase,
        int maxBase,
        CancellationToken cancellationToken = default)
    {
        if (venta is null)
        {
            return SinRestriccion(minBase, maxBase);
        }

        var productoIds = venta.Detalles
            .Where(d => d.ProductoId > 0)
            .Select(d => d.ProductoId)
            .Distinct()
            .ToArray();

        if (productoIds.Length == 0)
        {
            return SinRestriccion(minBase, maxBase);
        }

        var resultado = await _condicionesPagoCarritoResolver.ResolverAsync(
            productoIds,
            tipoPago,
            totalReferencia: venta.Total,
            maxCuotasCreditoGlobal: maxBase,
            cancellationToken: cancellationToken);

        if (!resultado.Permitido)
        {
            var productos = DescribirProductos(venta.Detalles, resultado.ProductoIdsBloqueantes);
            return new CreditoRangoProductoResultado(
                minBase,
                maxBase,
                maxBase,
                null,
                null,
                null,
                null,
                $"No se puede configurar crédito personal: {productos} bloquea el medio de pago.");
        }

        var maxEfectivo = resultado.MaxCuotasCredito.HasValue
            ? Math.Min(maxBase, resultado.MaxCuotasCredito.Value)
            : maxBase;
        var maxProducto = resultado.ProductoIdsRestrictivos.Count > 0
            ? resultado.MaxCuotasCredito
            : null;
        var productoIdRestrictivo = resultado.ProductoIdsRestrictivos.Count > 0
            ? resultado.ProductoIdsRestrictivos[0]
            : (int?)null;
        var productoRestrictivoNombre = productoIdRestrictivo.HasValue
            ? venta.Detalles
                .Where(d => d.ProductoId == productoIdRestrictivo.Value)
                .Select(d => d.ProductoNombre)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            : null;
        var descripcion = maxProducto.HasValue
            ? $"Límite por producto: hasta {maxProducto.Value} cuotas."
            : null;

        if (minBase > maxEfectivo)
        {
            return new CreditoRangoProductoResultado(
                minBase,
                maxEfectivo,
                maxBase,
                maxProducto,
                productoIdRestrictivo,
                productoRestrictivoNombre,
                descripcion,
                $"El rango de cuotas de crédito personal queda inválido para esta venta: mínimo {minBase}, máximo efectivo {maxEfectivo}.");
        }

        return new CreditoRangoProductoResultado(
            minBase,
            maxEfectivo,
            maxBase,
            maxProducto,
            productoIdRestrictivo,
            productoRestrictivoNombre,
            descripcion,
            null);
    }

    private static CreditoRangoProductoResultado SinRestriccion(int minBase, int maxBase) =>
        new(minBase, maxBase, maxBase, null, null, null, null, null);

    private static string DescribirProductos(
        IEnumerable<VentaDetalleViewModel> detalles,
        IReadOnlyCollection<int> productoIds)
    {
        var nombres = detalles
            .Where(d => productoIds.Contains(d.ProductoId))
            .Select(d => string.IsNullOrWhiteSpace(d.ProductoNombre)
                ? $"Producto #{d.ProductoId}"
                : d.ProductoNombre!)
            .Distinct()
            .ToArray();

        return nombres.Length == 0
            ? "un producto del carrito"
            : string.Join(", ", nombres);
    }
}
