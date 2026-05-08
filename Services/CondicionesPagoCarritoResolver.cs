using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services;

public sealed class CondicionesPagoCarritoResolver : ICondicionesPagoCarritoResolver
{
    private readonly AppDbContext _context;

    public CondicionesPagoCarritoResolver(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CondicionesPagoCarritoResultado> ResolverAsync(
        IEnumerable<int> productoIds,
        TipoPago tipoPago,
        int? configuracionTarjetaId = null,
        decimal? totalReferencia = null,
        int? maxCuotasSinInteresGlobal = null,
        int? maxCuotasConInteresGlobal = null,
        int? maxCuotasCreditoGlobal = null,
        TipoTarjeta? tipoTarjetaLegacy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productoIds);

        var ids = productoIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var condicionesEntidades = ids.Length == 0
            ? new List<ProductoCondicionPago>()
            : await _context.ProductoCondicionesPago
                .AsNoTracking()
                .Include(c => c.Tarjetas
                    .Where(t =>
                        t.Activo
                        && !t.IsDeleted
                        && (!configuracionTarjetaId.HasValue
                            || t.ConfiguracionTarjetaId == null
                            || t.ConfiguracionTarjetaId == configuracionTarjetaId.Value)))
                    .ThenInclude(t => t.Planes.Where(p => p.Activo && !p.IsDeleted))
                .Include(c => c.Planes
                    .Where(p => p.Activo && !p.IsDeleted && p.ProductoCondicionPagoTarjetaId == null))
                .Where(c =>
                    ids.Contains(c.ProductoId)
                    && c.Activo
                    && !c.IsDeleted
                    && !c.Producto.IsDeleted)
                .OrderBy(c => c.ProductoId)
                .ThenBy(c => c.TipoPago)
                .ToListAsync(cancellationToken);

        var condiciones = condicionesEntidades
            .Select(MapCondicion)
            .ToArray();

        return ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            tipoPago,
            configuracionTarjetaId,
            totalReferencia,
            maxCuotasSinInteresGlobal,
            maxCuotasConInteresGlobal,
            maxCuotasCreditoGlobal,
            tipoTarjetaLegacy);
    }

    private static ProductoCondicionPagoDto MapCondicion(ProductoCondicionPago condicion)
    {
        return new ProductoCondicionPagoDto
        {
            Id = condicion.Id,
            ProductoId = condicion.ProductoId,
            TipoPago = condicion.TipoPago,
            Permitido = condicion.Permitido,
            MaxCuotasSinInteres = condicion.MaxCuotasSinInteres,
            MaxCuotasConInteres = condicion.MaxCuotasConInteres,
            MaxCuotasCredito = condicion.MaxCuotasCredito,
            PorcentajeRecargo = condicion.PorcentajeRecargo,
            PorcentajeDescuentoMaximo = condicion.PorcentajeDescuentoMaximo,
            Activo = condicion.Activo,
            Observaciones = condicion.Observaciones,
            RowVersion = condicion.RowVersion,
            Tarjetas = condicion.Tarjetas
                .Where(t => t.Activo && !t.IsDeleted)
                .OrderBy(t => t.ConfiguracionTarjetaId.HasValue)
                .ThenBy(t => t.ConfiguracionTarjetaId)
                .Select(MapTarjeta)
                .ToArray(),
            Planes = condicion.Planes
                .OrderBy(p => p.CantidadCuotas)
                .Select(MapPlan)
                .ToArray()
        };
    }

    private static ProductoCondicionPagoTarjetaDto MapTarjeta(ProductoCondicionPagoTarjeta tarjeta)
    {
        return new ProductoCondicionPagoTarjetaDto
        {
            Id = tarjeta.Id,
            ConfiguracionTarjetaId = tarjeta.ConfiguracionTarjetaId,
            Permitido = tarjeta.Permitido,
            MaxCuotasSinInteres = tarjeta.MaxCuotasSinInteres,
            MaxCuotasConInteres = tarjeta.MaxCuotasConInteres,
            PorcentajeRecargo = tarjeta.PorcentajeRecargo,
            PorcentajeDescuentoMaximo = tarjeta.PorcentajeDescuentoMaximo,
            Activo = tarjeta.Activo,
            Observaciones = tarjeta.Observaciones,
            RowVersion = tarjeta.RowVersion,
            Planes = tarjeta.Planes
                .OrderBy(p => p.CantidadCuotas)
                .Select(MapPlan)
                .ToArray()
        };
    }

    private static ProductoCondicionPagoPlanDto MapPlan(ProductoCondicionPagoPlan plan)
    {
        return new ProductoCondicionPagoPlanDto
        {
            Id = plan.Id,
            CantidadCuotas = plan.CantidadCuotas,
            Activo = plan.Activo,
            AjustePorcentaje = plan.AjustePorcentaje,
            TipoAjuste = plan.TipoAjuste,
            Observaciones = plan.Observaciones
        };
    }
}
