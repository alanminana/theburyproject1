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

    public async Task<MediosPagoPorProductoResultado> ObtenerMediosPorProductoAsync(
        int productoId,
        int? configuracionTarjetaId = null,
        CancellationToken cancellationToken = default)
    {
        if (productoId <= 0)
            return new MediosPagoPorProductoResultado { SinRestriccionesPropias = true };

        var condicionesEntidades = await _context.ProductoCondicionesPago
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
                c.ProductoId == productoId
                && c.Activo
                && !c.IsDeleted
                && !c.Producto.IsDeleted)
            .OrderBy(c => c.TipoPago)
            .ToListAsync(cancellationToken);

        if (condicionesEntidades.Count == 0)
            return new MediosPagoPorProductoResultado { SinRestriccionesPropias = true };

        var medios = condicionesEntidades
            .Where(c => c.Permitido != false)
            .Select(c =>
            {
                var tarjetaEspecifica = configuracionTarjetaId.HasValue
                    ? c.Tarjetas.FirstOrDefault(t => t.Activo && !t.IsDeleted && t.ConfiguracionTarjetaId == configuracionTarjetaId.Value)
                    : null;
                var tarjetaGeneral = c.Tarjetas.FirstOrDefault(t => t.Activo && !t.IsDeleted && t.ConfiguracionTarjetaId == null);

                IEnumerable<ProductoCondicionPagoPlan> planes;
                if (tarjetaEspecifica != null && tarjetaEspecifica.Planes.Any(p => p.Activo && !p.IsDeleted))
                    planes = tarjetaEspecifica.Planes.Where(p => p.Activo && !p.IsDeleted);
                else if (tarjetaGeneral != null && tarjetaGeneral.Planes.Any(p => p.Activo && !p.IsDeleted))
                    planes = tarjetaGeneral.Planes.Where(p => p.Activo && !p.IsDeleted);
                else
                    planes = c.Planes.Where(p => p.Activo && !p.IsDeleted);

                var porcentajeRecargo = tarjetaEspecifica?.PorcentajeRecargo
                    ?? tarjetaGeneral?.PorcentajeRecargo
                    ?? c.PorcentajeRecargo;

                return new MedioHabilitadoDto
                {
                    TipoPago = (int)c.TipoPago,
                    PorcentajeRecargo = porcentajeRecargo,
                    Planes = planes.OrderBy(p => p.CantidadCuotas).Select(MapPlan).ToArray()
                };
            })
            .ToArray();

        return new MediosPagoPorProductoResultado { SinRestriccionesPropias = false, Medios = medios };
    }
}
