using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services;

public sealed class ConfiguracionPagoGlobalQueryService : IConfiguracionPagoGlobalQueryService
{
    private readonly AppDbContext _context;

    public ConfiguracionPagoGlobalQueryService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ConfiguracionPagoGlobalResultado> ObtenerActivaParaVentaAsync(
        CancellationToken cancellationToken = default)
    {
        var medios = await _context.ConfiguracionesPago
            .AsNoTracking()
            .Where(c => c.Activo && !c.IsDeleted)
            .OrderBy(c => c.TipoPago)
            .ThenBy(c => c.Nombre)
            .Select(c => new MedioPagoGlobalDto
            {
                Id = c.Id,
                TipoPago = c.TipoPago,
                NombreVisible = c.Nombre,
                Activo = c.Activo,
                Observaciones = c.Descripcion,
                Ajuste = new AjusteMedioPagoGlobalDto
                {
                    PermiteDescuento = c.PermiteDescuento,
                    PorcentajeDescuentoMaximo = c.PorcentajeDescuentoMaximo,
                    TieneRecargo = c.TieneRecargo,
                    PorcentajeRecargo = c.PorcentajeRecargo
                }
            })
            .ToListAsync(cancellationToken);

        if (medios.Count == 0)
        {
            return new ConfiguracionPagoGlobalResultado();
        }

        var medioIds = medios.Select(m => m.Id).ToArray();

        var tarjetas = await _context.ConfiguracionesTarjeta
            .AsNoTracking()
            .Where(t => medioIds.Contains(t.ConfiguracionPagoId) && t.Activa && !t.IsDeleted)
            .OrderBy(t => t.TipoTarjeta)
            .ThenBy(t => t.NombreTarjeta)
            .Select(t => new TarjetaPagoGlobalDto
            {
                Id = t.Id,
                ConfiguracionPagoId = t.ConfiguracionPagoId,
                Nombre = t.NombreTarjeta,
                TipoTarjeta = t.TipoTarjeta,
                Activa = t.Activa,
                PermiteCuotas = t.PermiteCuotas,
                CantidadMaximaCuotas = t.CantidadMaximaCuotas,
                TipoCuota = t.TipoCuota,
                TasaInteresesMensual = t.TasaInteresesMensual,
                TieneRecargoDebito = t.TieneRecargoDebito,
                PorcentajeRecargoDebito = t.PorcentajeRecargoDebito,
                Observaciones = t.Observaciones
            })
            .ToListAsync(cancellationToken);

        var tarjetasActivasIds = tarjetas.Select(t => t.Id).ToHashSet();

        var planes = await _context.ConfiguracionPagoPlanes
            .AsNoTracking()
            .Where(p => medioIds.Contains(p.ConfiguracionPagoId) && p.Activo && !p.IsDeleted)
            .Where(p => p.ConfiguracionTarjetaId == null || tarjetasActivasIds.Contains(p.ConfiguracionTarjetaId.Value))
            .OrderBy(p => p.Orden)
            .ThenBy(p => p.CantidadCuotas)
            .ThenBy(p => p.Id)
            .Select(p => new PlanPagoGlobalConfiguradoDto
            {
                Id = p.Id,
                ConfiguracionPagoId = p.ConfiguracionPagoId,
                ConfiguracionTarjetaId = p.ConfiguracionTarjetaId,
                TipoPago = p.TipoPago,
                CantidadCuotas = p.CantidadCuotas,
                Activo = p.Activo,
                TipoAjuste = p.TipoAjuste,
                AjustePorcentaje = p.AjustePorcentaje,
                Etiqueta = p.Etiqueta,
                Orden = p.Orden,
                Observaciones = p.Observaciones
            })
            .ToListAsync(cancellationToken);

        var tarjetasPorMedio = tarjetas
            .GroupBy(t => t.ConfiguracionPagoId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TarjetaPagoGlobalDto>)g.ToList());

        var planesPorMedio = planes
            .GroupBy(p => p.ConfiguracionPagoId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PlanPagoGlobalConfiguradoDto>)g.ToList());

        var resultado = medios
            .Select(m => new MedioPagoGlobalDto
            {
                Id = m.Id,
                TipoPago = m.TipoPago,
                NombreVisible = m.NombreVisible,
                Activo = m.Activo,
                Observaciones = m.Observaciones,
                Ajuste = m.Ajuste,
                Tarjetas = tarjetasPorMedio.GetValueOrDefault(m.Id) ?? Array.Empty<TarjetaPagoGlobalDto>(),
                Planes = planesPorMedio.GetValueOrDefault(m.Id) ?? Array.Empty<PlanPagoGlobalConfiguradoDto>()
            })
            .ToList();

        return new ConfiguracionPagoGlobalResultado { Medios = resultado };
    }
}
