using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio para gestión de historial de precios de productos
    /// </summary>
    public class PrecioHistoricoService : IPrecioHistoricoService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PrecioHistoricoService> _logger;

        public PrecioHistoricoService(
            AppDbContext context,
            ILogger<PrecioHistoricoService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<PrecioHistorico> RegistrarCambioAsync(
            int productoId,
            decimal precioCompraAnterior,
            decimal precioCompraNuevo,
            decimal precioVentaAnterior,
            decimal precioVentaNuevo,
            string? motivoCambio,
            string usuarioModificacion)
        {
            try
            {
                var historial = new PrecioHistorico
                {
                    ProductoId = productoId,
                    PrecioCompraAnterior = precioCompraAnterior,
                    PrecioCompraNuevo = precioCompraNuevo,
                    PrecioVentaAnterior = precioVentaAnterior,
                    PrecioVentaNuevo = precioVentaNuevo,
                    MotivoCambio = motivoCambio,
                    FechaCambio = DateTime.UtcNow,
                    UsuarioModificacion = usuarioModificacion,
                    PuedeRevertirse = true
                };

                _context.PreciosHistoricos.Add(historial);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Registrado cambio de precio para producto {ProductoId}. " +
                    "Compra: {PrecioCompraAnterior} → {PrecioCompraNuevo}, " +
                    "Venta: {PrecioVentaAnterior} → {PrecioVentaNuevo}",
                    productoId, precioCompraAnterior, precioCompraNuevo,
                    precioVentaAnterior, precioVentaNuevo);

                return historial;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar cambio de precio para producto {ProductoId}", productoId);
                throw;
            }
        }

        public async Task<List<PrecioHistorico>> GetHistorialByProductoIdAsync(int productoId)
        {
            return await _context.PreciosHistoricos
                .Include(p => p.Producto)
                .Where(p => p.ProductoId == productoId && !p.IsDeleted && !p.Producto.IsDeleted)
                .OrderByDescending(p => p.FechaCambio)
                .ToListAsync();
        }

        public async Task<PrecioHistorico?> GetUltimoCambioAsync(int productoId)
        {
            return await _context.PreciosHistoricos
                .Include(p => p.Producto)
                .Where(p => p.ProductoId == productoId && !p.IsDeleted && !p.Producto.IsDeleted)
                .OrderByDescending(p => p.FechaCambio)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> RevertirCambioAsync(int historialId)
        {
            try
            {
                var historial = await _context.PreciosHistoricos
                    .Include(p => p.Producto)
                    .FirstOrDefaultAsync(p => p.Id == historialId && !p.IsDeleted && !p.Producto.IsDeleted);

                if (historial == null)
                {
                    _logger.LogWarning("Historial {HistorialId} no encontrado", historialId);
                    return false;
                }

                if (!historial.PuedeRevertirse)
                {
                    _logger.LogWarning(
                        "El cambio de precio {HistorialId} no puede revertirse (hay ventas posteriores)",
                        historialId);
                    return false;
                }

                // Verificar si es el último cambio
                var ultimoCambio = await GetUltimoCambioAsync(historial.ProductoId);
                if (ultimoCambio?.Id != historialId)
                {
                    _logger.LogWarning(
                        "Solo se puede revertir el último cambio de precio. Historial: {HistorialId}",
                        historialId);
                    return false;
                }

                // Verificar si hay ventas posteriores al cambio
                var hayVentasPosteriores = await _context.VentaDetalles
                    .AnyAsync(vd =>
                        vd.ProductoId == historial.ProductoId &&
                        vd.Venta.FechaVenta >= historial.FechaCambio);

                if (hayVentasPosteriores)
                {
                    await MarcarComoNoReversibleAsync(historialId);
                    _logger.LogWarning(
                        "No se puede revertir el cambio {HistorialId} porque hay ventas posteriores",
                        historialId);
                    return false;
                }

                // Revertir precios
                var producto = historial.Producto;
                producto.PrecioCompra = historial.PrecioCompraAnterior;
                producto.PrecioVenta = historial.PrecioVentaAnterior;

                // Soft delete del registro del historial (unificado)
                historial.IsDeleted = true;
                historial.PuedeRevertirse = false;

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Revertido cambio de precio {HistorialId} para producto {ProductoId}",
                    historialId, historial.ProductoId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al revertir cambio de precio {HistorialId}", historialId);
                throw;
            }
        }

        public async Task<PrecioHistoricoEstadisticasViewModel> GetEstadisticasAsync(
            DateTime? fechaDesde,
            DateTime? fechaHasta)
        {
            var query = _context.PreciosHistoricos
                .Include(p => p.Producto)
                .AsQueryable();

            query = query.Where(p => !p.IsDeleted && !p.Producto.IsDeleted);

            if (fechaDesde.HasValue)
                query = query.Where(p => p.FechaCambio >= fechaDesde.Value);

            if (fechaHasta.HasValue)
                query = query.Where(p => p.FechaCambio <= fechaHasta.Value);

            var cambios = await query.ToListAsync();

            var estadisticas = new PrecioHistoricoEstadisticasViewModel
            {
                TotalCambios = cambios.Count,
                CambiosConAumento = cambios.Count(c => c.PrecioVentaNuevo > c.PrecioVentaAnterior),
                CambiosConDisminucion = cambios.Count(c => c.PrecioVentaNuevo < c.PrecioVentaAnterior),
                CambiosReversibles = cambios.Count(c => c.PuedeRevertirse),

                PromedioAumentoCompra = cambios
                    .Where(c => c.PrecioCompraNuevo > c.PrecioCompraAnterior)
                    .Average(c => (decimal?)c.PorcentajeCambioCompra) ?? 0,

                PromedioAumentoVenta = cambios
                    .Where(c => c.PrecioVentaNuevo > c.PrecioVentaAnterior)
                    .Average(c => (decimal?)c.PorcentajeCambioVenta) ?? 0,

                PromedioDisminucionCompra = cambios
                    .Where(c => c.PrecioCompraNuevo < c.PrecioCompraAnterior)
                    .Average(c => (decimal?)c.PorcentajeCambioCompra) ?? 0,

                PromedioDisminucionVenta = cambios
                    .Where(c => c.PrecioVentaNuevo < c.PrecioVentaAnterior)
                    .Average(c => (decimal?)c.PorcentajeCambioVenta) ?? 0,

                UltimosCambios = cambios
                    .OrderByDescending(c => c.FechaCambio)
                    .Take(10)
                    .Select(c => MapToViewModel(c))
                    .ToList(),

                ProductosConMasCambios = await _context.PreciosHistoricos
                    .Where(p => !p.IsDeleted && !p.Producto.IsDeleted)
                    .GroupBy(p => new { p.ProductoId, p.Producto.Codigo, p.Producto.Nombre, p.Producto.PrecioCompra, p.Producto.PrecioVenta })
                    .Select(g => new ProductoConMasCambiosViewModel
                    {
                        ProductoId = g.Key.ProductoId,
                        ProductoCodigo = g.Key.Codigo,
                        ProductoNombre = g.Key.Nombre,
                        TotalCambios = g.Count(),
                        PrecioActualCompra = g.Key.PrecioCompra,
                        PrecioActualVenta = g.Key.PrecioVenta
                    })
                    .OrderByDescending(p => p.TotalCambios)
                    .Take(10)
                    .ToListAsync()
            };

            // Mayor aumento y disminución
            var mayorAumento = cambios
                .Where(c => c.PrecioVentaNuevo > c.PrecioVentaAnterior)
                .OrderByDescending(c => c.PorcentajeCambioVenta)
                .FirstOrDefault();

            if (mayorAumento != null)
            {
                estadisticas.MayorAumentoVenta = mayorAumento.PorcentajeCambioVenta;
                estadisticas.ProductoMayorAumentoVenta = $"{mayorAumento.Producto.Codigo} - {mayorAumento.Producto.Nombre}";
            }

            var mayorDisminucion = cambios
                .Where(c => c.PrecioVentaNuevo < c.PrecioVentaAnterior)
                .OrderBy(c => c.PorcentajeCambioVenta)
                .FirstOrDefault();

            if (mayorDisminucion != null)
            {
                estadisticas.MayorDisminucionVenta = mayorDisminucion.PorcentajeCambioVenta;
                estadisticas.ProductoMayorDisminucionVenta = $"{mayorDisminucion.Producto.Codigo} - {mayorDisminucion.Producto.Nombre}";
            }

            return estadisticas;
        }

        public async Task<PaginatedResult<PrecioHistoricoViewModel>> BuscarAsync(PrecioHistoricoFiltroViewModel filtro)
        {
            var query = _context.PreciosHistoricos
                .Include(p => p.Producto)
                .AsQueryable();

            query = query.Where(p => !p.IsDeleted && !p.Producto.IsDeleted);

            // Filtros
            if (filtro.ProductoId.HasValue)
                query = query.Where(p => p.ProductoId == filtro.ProductoId.Value);

            if (!string.IsNullOrEmpty(filtro.ProductoCodigo))
                query = query.Where(p => p.Producto.Codigo.Contains(filtro.ProductoCodigo));

            if (!string.IsNullOrEmpty(filtro.ProductoNombre))
                query = query.Where(p => p.Producto.Nombre.Contains(filtro.ProductoNombre));

            if (filtro.FechaDesde.HasValue)
                query = query.Where(p => p.FechaCambio >= filtro.FechaDesde.Value);

            if (filtro.FechaHasta.HasValue)
                query = query.Where(p => p.FechaCambio <= filtro.FechaHasta.Value);

            if (!string.IsNullOrEmpty(filtro.UsuarioModificacion))
                query = query.Where(p => p.UsuarioModificacion.Contains(filtro.UsuarioModificacion));

            if (filtro.SoloPuedeRevertirse == true)
                query = query.Where(p => p.PuedeRevertirse);

            if (filtro.PorcentajeMinimoAumento.HasValue)
            {
                var minimo = filtro.PorcentajeMinimoAumento.Value;
                query = query.Where(p =>
                    p.PrecioVentaAnterior != 0 &&
                    ((p.PrecioVentaNuevo - p.PrecioVentaAnterior) / p.PrecioVentaAnterior) * 100 >= minimo);
            }

            var totalRecords = await query.CountAsync();

            var items = await query
                .OrderByDescending(p => p.FechaCambio)
                .Skip((filtro.PageNumber - 1) * filtro.PageSize)
                .Take(filtro.PageSize)
                .Select(p => MapToViewModel(p))
                .ToListAsync();

            return new PaginatedResult<PrecioHistoricoViewModel>
            {
                Items = items,
                TotalRecords = totalRecords,
                PageNumber = filtro.PageNumber,
                PageSize = filtro.PageSize
            };
        }

        public async Task<PrecioSimulacionViewModel> SimularCambioAsync(
            int productoId,
            decimal precioCompraNuevo,
            decimal precioVentaNuevo)
        {
            var producto = await _context.Productos
                .FirstOrDefaultAsync(p => p.Id == productoId && !p.IsDeleted);

            if (producto == null)
                throw new ArgumentException($"Producto {productoId} no encontrado");

            var simulacion = new PrecioSimulacionViewModel
            {
                ProductoId = producto.Id,
                ProductoCodigo = producto.Codigo,
                ProductoNombre = producto.Nombre,

                PrecioCompraActual = producto.PrecioCompra,
                PrecioVentaActual = producto.PrecioVenta,
                MargenActual = producto.PrecioCompra == 0 ? 0 :
                    ((producto.PrecioVenta - producto.PrecioCompra) / producto.PrecioCompra) * 100,

                PrecioCompraPropuesto = precioCompraNuevo,
                PrecioVentaPropuesto = precioVentaNuevo,
                MargenPropuesto = precioCompraNuevo == 0 ? 0 :
                    ((precioVentaNuevo - precioCompraNuevo) / precioCompraNuevo) * 100,

                DiferenciaCompra = precioCompraNuevo - producto.PrecioCompra,
                DiferenciaVenta = precioVentaNuevo - producto.PrecioVenta,
                PorcentajeCambioCompra = producto.PrecioCompra == 0 ? 0 :
                    ((precioCompraNuevo - producto.PrecioCompra) / producto.PrecioCompra) * 100,
                PorcentajeCambioVenta = producto.PrecioVenta == 0 ? 0 :
                    ((precioVentaNuevo - producto.PrecioVenta) / producto.PrecioVenta) * 100
            };

            simulacion.DiferenciaMargen = simulacion.MargenPropuesto - simulacion.MargenActual;

            // Alertas y recomendaciones
            if (simulacion.MargenPropuesto < 0)
            {
                simulacion.Alertas.Add("⚠️ ALERTA: El margen propuesto es NEGATIVO. Venderás con pérdida.");
                simulacion.EsRecomendable = false;
            }
            else if (simulacion.MargenPropuesto < 10)
            {
                simulacion.Alertas.Add("⚠️ ADVERTENCIA: El margen propuesto es muy bajo (< 10%).");
                simulacion.EsRecomendable = false;
            }
            else if (simulacion.DiferenciaMargen < -10)
            {
                simulacion.Alertas.Add($"⚠️ El margen disminuirá en {Math.Abs(simulacion.DiferenciaMargen):F2}%.");
                simulacion.EsRecomendable = false;
            }
            else if (simulacion.DiferenciaMargen > 10)
            {
                simulacion.Alertas.Add($"✅ El margen aumentará en {simulacion.DiferenciaMargen:F2}%.");
                simulacion.EsRecomendable = true;
            }

            if (precioCompraNuevo > precioVentaNuevo)
            {
                simulacion.Alertas.Add("❌ ERROR: El precio de costo NO puede ser mayor al precio de venta.");
                simulacion.EsRecomendable = false;
            }

            if (simulacion.PorcentajeCambioVenta > 50)
            {
                simulacion.Alertas.Add($"⚠️ Aumento muy grande en precio de venta ({simulacion.PorcentajeCambioVenta:F2}%).");
            }

            if (simulacion.EsRecomendable && simulacion.Alertas.Count == 0)
            {
                simulacion.Recomendacion = "✅ El cambio de precio propuesto es aceptable.";
            }
            else if (!simulacion.EsRecomendable)
            {
                simulacion.Recomendacion = "❌ Se recomienda revisar los precios propuestos.";
            }

            return simulacion;
        }

        public async Task MarcarComoNoReversibleAsync(int historialId)
        {
            var historial = await _context.PreciosHistoricos
                .FirstOrDefaultAsync(p => p.Id == historialId && !p.IsDeleted);
            if (historial != null)
            {
                historial.PuedeRevertirse = false;
                await _context.SaveChangesAsync();
            }
        }

        private static PrecioHistoricoViewModel MapToViewModel(PrecioHistorico historial)
        {
            return new PrecioHistoricoViewModel
            {
                Id = historial.Id,
                ProductoId = historial.ProductoId,
                ProductoCodigo = historial.Producto.Codigo,
                ProductoNombre = historial.Producto.Nombre,
                PrecioCompraAnterior = historial.PrecioCompraAnterior,
                PrecioCompraNuevo = historial.PrecioCompraNuevo,
                PrecioVentaAnterior = historial.PrecioVentaAnterior,
                PrecioVentaNuevo = historial.PrecioVentaNuevo,
                MotivoCambio = historial.MotivoCambio,
                PuedeRevertirse = historial.PuedeRevertirse,
                FechaCambio = historial.FechaCambio,
                UsuarioModificacion = historial.UsuarioModificacion,
                PorcentajeCambioCompra = historial.PorcentajeCambioCompra,
                PorcentajeCambioVenta = historial.PorcentajeCambioVenta,
                MargenAnterior = historial.MargenAnterior,
                MargenNuevo = historial.MargenNuevo
            };
        }
    }
}