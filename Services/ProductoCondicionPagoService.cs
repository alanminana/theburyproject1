using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services;

public sealed class ProductoCondicionPagoService : IProductoCondicionPagoService
{
    private readonly AppDbContext _context;

    public ProductoCondicionPagoService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ProductoCondicionesPagoLecturaDto> ObtenerPorProductoAsync(
        int productoId,
        CancellationToken cancellationToken = default)
    {
        var producto = await ObtenerProductoValidoAsync(productoId, cancellationToken);
        var condiciones = await ObtenerCondicionesQuery(productoId)
            .ToListAsync(cancellationToken);

        return new ProductoCondicionesPagoLecturaDto
        {
            ProductoId = producto.Id,
            ProductoCodigo = producto.Codigo,
            ProductoNombre = producto.Nombre,
            Condiciones = condiciones.Select(MapCondicion).ToArray()
        };
    }

    public async Task<ProductoCondicionesPagoEditableDto> ObtenerEstadoEditableAsync(
        int productoId,
        CancellationToken cancellationToken = default)
    {
        var lectura = await ObtenerPorProductoAsync(productoId, cancellationToken);
        var tarjetas = await _context.ConfiguracionesTarjeta
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.TipoTarjeta)
            .ThenBy(t => t.NombreTarjeta)
            .Select(t => new ProductoCondicionPagoTarjetaDisponibleDto
            {
                Id = t.Id,
                NombreTarjeta = t.NombreTarjeta,
                TipoTarjeta = t.TipoTarjeta,
                Activa = t.Activa
            })
            .ToListAsync(cancellationToken);

        return new ProductoCondicionesPagoEditableDto
        {
            ProductoId = lectura.ProductoId,
            ProductoCodigo = lectura.ProductoCodigo,
            ProductoNombre = lectura.ProductoNombre,
            Condiciones = lectura.Condiciones,
            TarjetasDisponibles = tarjetas,
            Validaciones = ProductoCondicionPagoRules.ValidarCondiciones(lectura.Condiciones)
        };
    }

    public async Task<ProductoCondicionPagoDto> GuardarCondicionAsync(
        int productoId,
        GuardarProductoCondicionPagoItem request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await ObtenerProductoValidoAsync(productoId, cancellationToken);
        ValidarCondicionRequest(productoId, request);

        ProductoCondicionPago condicion;
        if (request.Id.HasValue)
        {
            condicion = await _context.ProductoCondicionesPago
                .Include(c => c.Tarjetas)
                .FirstOrDefaultAsync(c => c.Id == request.Id.Value && c.ProductoId == productoId, cancellationToken)
                ?? throw new InvalidOperationException("La condicion de pago del producto no existe.");

            AplicarRowVersion(condicion, request.RowVersion, requiereRowVersion: true);
        }
        else
        {
            condicion = new ProductoCondicionPago { ProductoId = productoId };
            _context.ProductoCondicionesPago.Add(condicion);
        }

        await ValidarDuplicadoCondicionAsync(productoId, request.TipoPago, request.Id, request.Activo, cancellationToken);
        AplicarCondicion(condicion, request);
        await SincronizarPlanesCondicionAsync(condicion, request.Planes, cancellationToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("La condicion de pago fue modificada por otro proceso. Recarga los datos e intenta nuevamente.");
        }

        var condicionFinal = await ObtenerCondicionesQuery(productoId)
            .FirstOrDefaultAsync(c => c.Id == condicion.Id, cancellationToken)
            ?? throw new InvalidOperationException("No se pudo recargar la condicion guardada.");
        return MapCondicion(condicionFinal);
    }

    public async Task<ProductoCondicionPagoTarjetaDto> GuardarReglaTarjetaAsync(
        int productoCondicionPagoId,
        GuardarProductoCondicionPagoTarjetaItem request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidarTarjetaRequest(request);
        await ValidarConfiguracionTarjetaAsync(request.ConfiguracionTarjetaId, cancellationToken);

        var condicion = await _context.ProductoCondicionesPago
            .Include(c => c.Tarjetas)
            .FirstOrDefaultAsync(c => c.Id == productoCondicionPagoId, cancellationToken)
            ?? throw new InvalidOperationException("La condicion de pago del producto no existe.");

        await ObtenerProductoValidoAsync(condicion.ProductoId, cancellationToken);

        ProductoCondicionPagoTarjeta tarjeta;
        var esNueva = !request.Id.HasValue;
        if (request.Id.HasValue)
        {
            tarjeta = condicion.Tarjetas.FirstOrDefault(t => t.Id == request.Id.Value)
                ?? throw new InvalidOperationException("La regla de tarjeta no existe para la condicion indicada.");

            AplicarRowVersion(tarjeta, request.RowVersion, requiereRowVersion: true);
        }
        else
        {
            tarjeta = new ProductoCondicionPagoTarjeta();
        }

        ValidarDuplicadoTarjeta(condicion, request.ConfiguracionTarjetaId, request.Id, request.Activo);
        if (esNueva)
        {
            condicion.Tarjetas.Add(tarjeta);
        }

        AplicarTarjeta(tarjeta, request);
        await SincronizarPlanesTarjetaAsync(tarjeta, condicion.Id, request.Planes, cancellationToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("La regla de tarjeta fue modificada por otro proceso. Recarga los datos e intenta nuevamente.");
        }

        var tarjetaFinal = await _context.ProductoCondicionesPagoTarjeta
            .AsNoTracking()
            .Include(t => t.Planes)
            .FirstOrDefaultAsync(t => t.Id == tarjeta.Id, cancellationToken)
            ?? throw new InvalidOperationException("No se pudo recargar la regla de tarjeta guardada.");
        return MapTarjeta(tarjetaFinal);
    }

    public async Task GuardarCondicionesCompletasAsync(
        int productoId,
        GuardarProductoCondicionesPagoRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await ObtenerProductoValidoAsync(productoId, cancellationToken);

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var condicionRequest in request.Condiciones)
            {
                var condicion = await GuardarCondicionAsync(productoId, condicionRequest, cancellationToken);
                foreach (var tarjetaRequest in condicionRequest.Tarjetas)
                {
                    await GuardarReglaTarjetaAsync(condicion.Id!.Value, tarjetaRequest, cancellationToken);
                }
            }
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Sincronización de planes
    // ─────────────────────────────────────────────────────────────

    private async Task SincronizarPlanesCondicionAsync(
        ProductoCondicionPago condicion,
        IReadOnlyList<GuardarProductoCondicionPagoPlanItem> planesRequest,
        CancellationToken cancellationToken)
    {
        // Para condicion nueva sin planes: nada que hacer
        if (planesRequest.Count == 0 && condicion.Id == 0) return;

        ValidarPlanesRequest(planesRequest);

        if (condicion.Id == 0)
        {
            // Nueva condicion: agregar a navigation property; EF resuelve la FK tras SaveChanges
            foreach (var planRequest in planesRequest)
            {
                var plan = new ProductoCondicionPagoPlan();
                AplicarPlan(plan, planRequest);
                condicion.Planes.Add(plan);
            }
            return;
        }

        // Condicion existente: diff contra los planes actuales en DB
        var existentes = await _context.ProductoCondicionPagoPlanes
            .Where(p => p.ProductoCondicionPagoId == condicion.Id && p.ProductoCondicionPagoTarjetaId == null)
            .ToListAsync(cancellationToken);

        var idsRequest = planesRequest
            .Where(p => p.Id.HasValue)
            .Select(p => p.Id!.Value)
            .ToHashSet();

        // Soft-delete los planes que no aparecen en el request
        foreach (var existente in existentes.Where(e => !idsRequest.Contains(e.Id)))
            existente.IsDeleted = true;

        foreach (var planRequest in planesRequest)
        {
            if (planRequest.Id.HasValue)
            {
                var existente = existentes.FirstOrDefault(e => e.Id == planRequest.Id.Value)
                    ?? throw new InvalidOperationException($"El plan de cuotas {planRequest.Id.Value} no existe para la condicion indicada.");
                AplicarPlan(existente, planRequest);
            }
            else
            {
                var plan = new ProductoCondicionPagoPlan { ProductoCondicionPagoId = condicion.Id };
                AplicarPlan(plan, planRequest);
                _context.ProductoCondicionPagoPlanes.Add(plan);
            }
        }
    }

    private async Task SincronizarPlanesTarjetaAsync(
        ProductoCondicionPagoTarjeta tarjeta,
        int condicionId,
        IReadOnlyList<GuardarProductoCondicionPagoPlanItem> planesRequest,
        CancellationToken cancellationToken)
    {
        // Para tarjeta nueva sin planes: nada que hacer
        if (planesRequest.Count == 0 && tarjeta.Id == 0) return;

        ValidarPlanesRequest(planesRequest);

        if (tarjeta.Id == 0)
        {
            // Nueva tarjeta: agregar a navigation property; ProductoCondicionPagoId se setea explícitamente
            foreach (var planRequest in planesRequest)
            {
                var plan = new ProductoCondicionPagoPlan { ProductoCondicionPagoId = condicionId };
                AplicarPlan(plan, planRequest);
                tarjeta.Planes.Add(plan);
            }
            return;
        }

        // Tarjeta existente: diff contra los planes actuales en DB
        var existentes = await _context.ProductoCondicionPagoPlanes
            .Where(p => p.ProductoCondicionPagoTarjetaId == tarjeta.Id)
            .ToListAsync(cancellationToken);

        var idsRequest = planesRequest
            .Where(p => p.Id.HasValue)
            .Select(p => p.Id!.Value)
            .ToHashSet();

        // Soft-delete los planes que no aparecen en el request
        foreach (var existente in existentes.Where(e => !idsRequest.Contains(e.Id)))
            existente.IsDeleted = true;

        foreach (var planRequest in planesRequest)
        {
            if (planRequest.Id.HasValue)
            {
                var existente = existentes.FirstOrDefault(e => e.Id == planRequest.Id.Value)
                    ?? throw new InvalidOperationException($"El plan de cuotas {planRequest.Id.Value} no existe para la tarjeta indicada.");
                AplicarPlan(existente, planRequest);
            }
            else
            {
                var plan = new ProductoCondicionPagoPlan
                {
                    ProductoCondicionPagoId = condicionId,
                    ProductoCondicionPagoTarjetaId = tarjeta.Id
                };
                AplicarPlan(plan, planRequest);
                _context.ProductoCondicionPagoPlanes.Add(plan);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Query base
    // ─────────────────────────────────────────────────────────────

    private IQueryable<ProductoCondicionPago> ObtenerCondicionesQuery(int productoId)
    {
        return _context.ProductoCondicionesPago
            .AsNoTracking()
            .Include(c => c.Tarjetas.Where(t => !t.IsDeleted))
                .ThenInclude(t => t.Planes.Where(p => !p.IsDeleted))
            .Include(c => c.Planes.Where(p => !p.IsDeleted && p.ProductoCondicionPagoTarjetaId == null))
            .Where(c => c.ProductoId == productoId)
            .OrderBy(c => c.TipoPago);
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers de dominio
    // ─────────────────────────────────────────────────────────────

    private async Task<Producto> ObtenerProductoValidoAsync(int productoId, CancellationToken cancellationToken)
    {
        return await _context.Productos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productoId && !p.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException("El producto no existe o fue eliminado.");
    }

    private async Task ValidarConfiguracionTarjetaAsync(int? configuracionTarjetaId, CancellationToken cancellationToken)
    {
        if (!configuracionTarjetaId.HasValue)
        {
            return;
        }

        var existe = await _context.ConfiguracionesTarjeta
            .AsNoTracking()
            .AnyAsync(t => t.Id == configuracionTarjetaId.Value && !t.IsDeleted, cancellationToken);

        if (!existe)
        {
            throw new InvalidOperationException("La configuracion de tarjeta indicada no existe.");
        }
    }

    private async Task ValidarDuplicadoCondicionAsync(
        int productoId,
        TipoPago tipoPago,
        int? id,
        bool activo,
        CancellationToken cancellationToken)
    {
        if (!activo)
        {
            return;
        }

        var existe = await _context.ProductoCondicionesPago
            .AsNoTracking()
            .AnyAsync(
                c => c.ProductoId == productoId
                    && c.TipoPago == tipoPago
                    && c.Activo
                    && (!id.HasValue || c.Id != id.Value),
                cancellationToken);

        if (existe)
        {
            throw new InvalidOperationException("Ya existe una condicion activa para el producto y tipo de pago indicados.");
        }
    }

    private static void ValidarDuplicadoTarjeta(
        ProductoCondicionPago condicion,
        int? configuracionTarjetaId,
        int? id,
        bool activo)
    {
        if (!activo)
        {
            return;
        }

        var duplicado = condicion.Tarjetas.Any(t =>
            t.Activo
            && !t.IsDeleted
            && (!id.HasValue || t.Id != id.Value)
            && t.ConfiguracionTarjetaId == configuracionTarjetaId);

        if (!duplicado)
        {
            return;
        }

        throw new InvalidOperationException(configuracionTarjetaId.HasValue
            ? "Ya existe una regla activa para la tarjeta indicada."
            : "Ya existe una regla general activa de tarjeta para la condicion indicada.");
    }

    private static void ValidarPlanesRequest(IReadOnlyList<GuardarProductoCondicionPagoPlanItem> planes)
    {
        foreach (var plan in planes)
        {
            if (plan.CantidadCuotas < 1)
                throw new InvalidOperationException("La cantidad de cuotas debe ser mayor a 0.");
            if (plan.AjustePorcentaje is < -100m or > 999.9999m)
                throw new InvalidOperationException("El ajuste de porcentaje debe estar entre -100 y 999.9999.");
        }

        if (planes.GroupBy(p => p.CantidadCuotas).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Existen planes con cantidad de cuotas duplicadas.");
    }

    private static void ValidarCondicionRequest(int productoId, GuardarProductoCondicionPagoItem request)
    {
        var dto = new ProductoCondicionPagoDto
        {
            Id = request.Id,
            ProductoId = productoId,
            TipoPago = request.TipoPago,
            Permitido = request.Permitido,
            MaxCuotasSinInteres = request.MaxCuotasSinInteres,
            MaxCuotasConInteres = request.MaxCuotasConInteres,
            MaxCuotasCredito = request.MaxCuotasCredito,
            PorcentajeRecargo = request.PorcentajeRecargo,
            PorcentajeDescuentoMaximo = request.PorcentajeDescuentoMaximo,
            Activo = request.Activo,
            Observaciones = request.Observaciones,
            RowVersion = request.RowVersion,
            Tarjetas = request.Tarjetas.Select(t => new ProductoCondicionPagoTarjetaDto
            {
                Id = t.Id,
                ConfiguracionTarjetaId = t.ConfiguracionTarjetaId,
                Permitido = t.Permitido,
                MaxCuotasSinInteres = t.MaxCuotasSinInteres,
                MaxCuotasConInteres = t.MaxCuotasConInteres,
                PorcentajeRecargo = t.PorcentajeRecargo,
                PorcentajeDescuentoMaximo = t.PorcentajeDescuentoMaximo,
                Activo = t.Activo,
                Observaciones = t.Observaciones,
                RowVersion = t.RowVersion
            }).ToArray()
        };

        ValidarErroresReglas(new[] { dto });
        ValidarPorcentaje(request.PorcentajeRecargo, nameof(request.PorcentajeRecargo));
        ValidarPorcentaje(request.PorcentajeDescuentoMaximo, nameof(request.PorcentajeDescuentoMaximo));
    }

    private static void ValidarTarjetaRequest(GuardarProductoCondicionPagoTarjetaItem request)
    {
        var dto = new ProductoCondicionPagoDto
        {
            ProductoId = 0,
            TipoPago = TipoPago.TarjetaCredito,
            Tarjetas = new[]
            {
                new ProductoCondicionPagoTarjetaDto
                {
                    Id = request.Id,
                    ConfiguracionTarjetaId = request.ConfiguracionTarjetaId,
                    Permitido = request.Permitido,
                    MaxCuotasSinInteres = request.MaxCuotasSinInteres,
                    MaxCuotasConInteres = request.MaxCuotasConInteres,
                    PorcentajeRecargo = request.PorcentajeRecargo,
                    PorcentajeDescuentoMaximo = request.PorcentajeDescuentoMaximo,
                    Activo = request.Activo,
                    Observaciones = request.Observaciones,
                    RowVersion = request.RowVersion
                }
            }
        };

        ValidarErroresReglas(new[] { dto });
        ValidarPorcentaje(request.PorcentajeRecargo, nameof(request.PorcentajeRecargo));
        ValidarPorcentaje(request.PorcentajeDescuentoMaximo, nameof(request.PorcentajeDescuentoMaximo));
    }

    private static void ValidarErroresReglas(IEnumerable<ProductoCondicionPagoDto> condiciones)
    {
        var error = ProductoCondicionPagoRules.ValidarCondiciones(condiciones)
            .FirstOrDefault(v => v.Severidad == SeveridadValidacionCondicionPago.Error);

        if (error is not null)
        {
            throw new InvalidOperationException(error.Motivo);
        }
    }

    private static void ValidarPorcentaje(decimal? valor, string nombreCampo)
    {
        if (valor is < 0m or > 100m)
        {
            throw new InvalidOperationException($"{nombreCampo} debe estar entre 0 y 100.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // RowVersion
    // ─────────────────────────────────────────────────────────────

    private void AplicarRowVersion(ProductoCondicionPago condicion, byte[]? rowVersion, bool requiereRowVersion)
    {
        if (requiereRowVersion && (rowVersion is null || rowVersion.Length == 0))
        {
            throw new InvalidOperationException("Falta informacion de concurrencia (RowVersion). Recarga los datos e intenta nuevamente.");
        }

        if (rowVersion is { Length: > 0 })
        {
            _context.Entry(condicion).Property(c => c.RowVersion).OriginalValue = rowVersion;
        }
    }

    private void AplicarRowVersion(ProductoCondicionPagoTarjeta tarjeta, byte[]? rowVersion, bool requiereRowVersion)
    {
        if (requiereRowVersion && (rowVersion is null || rowVersion.Length == 0))
        {
            throw new InvalidOperationException("Falta informacion de concurrencia (RowVersion). Recarga los datos e intenta nuevamente.");
        }

        if (rowVersion is { Length: > 0 })
        {
            _context.Entry(tarjeta).Property(t => t.RowVersion).OriginalValue = rowVersion;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Aplicar campos
    // ─────────────────────────────────────────────────────────────

    private static void AplicarCondicion(ProductoCondicionPago condicion, GuardarProductoCondicionPagoItem request)
    {
        condicion.TipoPago = request.TipoPago;
        condicion.Permitido = request.Permitido;
        condicion.MaxCuotasSinInteres = request.MaxCuotasSinInteres;
        condicion.MaxCuotasConInteres = request.MaxCuotasConInteres;
        condicion.MaxCuotasCredito = request.MaxCuotasCredito;
        condicion.PorcentajeRecargo = request.PorcentajeRecargo;
        condicion.PorcentajeDescuentoMaximo = request.PorcentajeDescuentoMaximo;
        condicion.Activo = request.Activo;
        condicion.Observaciones = request.Observaciones;
    }

    private static void AplicarTarjeta(ProductoCondicionPagoTarjeta tarjeta, GuardarProductoCondicionPagoTarjetaItem request)
    {
        tarjeta.ConfiguracionTarjetaId = request.ConfiguracionTarjetaId;
        tarjeta.Permitido = request.Permitido;
        tarjeta.MaxCuotasSinInteres = request.MaxCuotasSinInteres;
        tarjeta.MaxCuotasConInteres = request.MaxCuotasConInteres;
        tarjeta.PorcentajeRecargo = request.PorcentajeRecargo;
        tarjeta.PorcentajeDescuentoMaximo = request.PorcentajeDescuentoMaximo;
        tarjeta.Activo = request.Activo;
        tarjeta.Observaciones = request.Observaciones;
    }

    private static void AplicarPlan(ProductoCondicionPagoPlan plan, GuardarProductoCondicionPagoPlanItem request)
    {
        plan.CantidadCuotas = request.CantidadCuotas;
        plan.Activo = request.Activo;
        plan.AjustePorcentaje = request.AjustePorcentaje;
        plan.TipoAjuste = request.TipoAjuste;
        plan.Observaciones = request.Observaciones;
    }

    // ─────────────────────────────────────────────────────────────
    // Mapeo a DTOs
    // ─────────────────────────────────────────────────────────────

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
                .Where(t => !t.IsDeleted)
                .OrderBy(t => t.ConfiguracionTarjetaId.HasValue)
                .ThenBy(t => t.ConfiguracionTarjetaId)
                .Select(MapTarjeta)
                .ToArray(),
            Planes = condicion.Planes
                .Where(p => !p.IsDeleted)
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
                .Where(p => !p.IsDeleted)
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
