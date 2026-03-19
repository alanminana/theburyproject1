using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio para gestión de alertas de stock
    /// </summary>
    public class AlertaStockService : IAlertaStockService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<AlertaStockService> _logger;

        public AlertaStockService(
            AppDbContext context,
            ILogger<AlertaStockService> _logger)
        {
            _context = context;
            this._logger = _logger;
        }

        public async Task<int> GenerarAlertasStockBajoAsync()
        {
            try
            {
                var productosConStockBajo = await _context.Productos
                    .AsNoTracking()
                    .Include(p => p.Categoria)
                    .Include(p => p.Marca)
                    .Where(p => !p.IsDeleted && p.Activo && p.StockActual <= p.StockMinimo)
                    .ToListAsync();

                if (productosConStockBajo.Count == 0)
                {
                    _logger.LogInformation("Generadas {AlertasCreadas} nuevas alertas de stock bajo", 0);
                    return 0;
                }

                var productoIds = productosConStockBajo.Select(p => p.Id).ToList();

                // Cargar en una sola consulta qué productos ya tienen alerta pendiente (evita N+1)
                var productoIdsConAlertaPendiente = await _context.AlertasStock
                    .AsNoTracking()
                    .Where(a => !a.IsDeleted && a.FechaResolucion == null && productoIds.Contains(a.ProductoId))
                    .Select(a => a.ProductoId)
                    .Distinct()
                    .ToListAsync();

                var setPendientes = new HashSet<int>(productoIdsConAlertaPendiente);

                int alertasCreadas = 0;

                foreach (var producto in productosConStockBajo)
                {
                    if (setPendientes.Contains(producto.Id))
                        continue;

                    var alerta = await CrearAlertaStockAsync(producto);
                    if (alerta != null)
                    {
                        alertasCreadas++;
                        setPendientes.Add(producto.Id);
                    }
                }
                _logger.LogInformation("Generadas {AlertasCreadas} nuevas alertas de stock bajo", alertasCreadas);
                return alertasCreadas;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar alertas de stock bajo");
                throw;
            }
        }

        public async Task<AlertaStock?> VerificarYGenerarAlertaAsync(int productoId)
        {
            try
            {
                var producto = await _context.Productos
                    .AsNoTracking()
                    .Include(p => p.Categoria)
                    .Include(p => p.Marca)
                    .FirstOrDefaultAsync(p => p.Id == productoId && !p.IsDeleted);

                if (producto == null || !producto.Activo)
                    return null;

                if (producto.StockActual > producto.StockMinimo)
                    return null;

                // Verificar si ya existe alerta pendiente
                var alertaExistente = await _context.AlertasStock
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a =>
                        a.ProductoId == productoId &&
                        !a.IsDeleted &&
                        a.FechaResolucion == null);

                if (alertaExistente != null)
                    return alertaExistente;

                return await CrearAlertaStockAsync(producto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar y generar alerta para producto {ProductoId}", productoId);
                throw;
            }
        }

        public async Task<int> VerificarYGenerarAlertasAsync(IEnumerable<int> productoIds)
        {
            try
            {
                var ids = (productoIds ?? Array.Empty<int>()).Distinct().ToList();
                if (ids.Count == 0)
                    return 0;

                var productos = await _context.Productos
                    .AsNoTracking()
                    .Include(p => p.Categoria)
                    .Include(p => p.Marca)
                    .Where(p => ids.Contains(p.Id) && !p.IsDeleted && p.Activo && p.StockActual <= p.StockMinimo)
                    .ToListAsync();

                if (productos.Count == 0)
                    return 0;

                var productoIdsConCondicion = productos.Select(p => p.Id).ToList();

                // Precargar en una sola consulta qué productos ya tienen alerta pendiente
                var productoIdsConAlertaPendiente = await _context.AlertasStock
                    .AsNoTracking()
                    .Where(a => !a.IsDeleted && a.FechaResolucion == null && productoIdsConCondicion.Contains(a.ProductoId))
                    .Select(a => a.ProductoId)
                    .Distinct()
                    .ToListAsync();

                var setPendientes = new HashSet<int>(productoIdsConAlertaPendiente);
                var creadas = 0;

                foreach (var producto in productos)
                {
                    if (setPendientes.Contains(producto.Id))
                        continue;

                    var alerta = await CrearAlertaStockAsync(producto);
                    if (alerta != null)
                    {
                        creadas++;
                        setPendientes.Add(producto.Id);
                    }
                }

                return creadas;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar y generar alertas (batch)");
                throw;
            }
        }

        private async Task<AlertaStock?> CrearAlertaStockAsync(Producto producto)
        {
            try
            {
                TipoAlertaStock tipo;
                PrioridadAlerta prioridad;
                string mensaje;

                if (producto.StockActual <= 0)
                {
                    tipo = TipoAlertaStock.StockAgotado;
                    prioridad = PrioridadAlerta.Critica;
                    mensaje = $"CRÍTICO: El producto '{producto.Nombre}' está AGOTADO. Stock actual: {producto.StockActual}";
                }
                else if (producto.StockActual <= (producto.StockMinimo * 0.3m))
                {
                    tipo = TipoAlertaStock.StockCritico;
                    prioridad = PrioridadAlerta.Alta;
                    mensaje = $"El producto '{producto.Nombre}' tiene stock CRÍTICO. Stock actual: {producto.StockActual}, Mínimo: {producto.StockMinimo}";
                }
                else
                {
                    tipo = TipoAlertaStock.StockBajo;
                    prioridad = PrioridadAlerta.Media;
                    mensaje = $"El producto '{producto.Nombre}' tiene stock bajo. Stock actual: {producto.StockActual}, Mínimo: {producto.StockMinimo}";
                }

                // Calcular cantidad sugerida para reposición (triple del mínimo menos el actual)
                var cantidadSugerida = (producto.StockMinimo * 3) - producto.StockActual;

                var alerta = new AlertaStock
                {
                    ProductoId = producto.Id,
                    Tipo = tipo,
                    Prioridad = prioridad,
                    Estado = EstadoAlerta.Pendiente,
                    Mensaje = mensaje,
                    StockActual = producto.StockActual,
                    StockMinimo = producto.StockMinimo,
                    CantidadSugeridaReposicion = cantidadSugerida > 0 ? cantidadSugerida : null,
                    FechaAlerta = DateTime.UtcNow,
                    NotificacionUrgente = tipo == TipoAlertaStock.StockAgotado || tipo == TipoAlertaStock.StockCritico
                };

                _context.AlertasStock.Add(alerta);

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException ex)
                {
                    // Si hubo condición de carrera, intentar devolver la alerta pendiente ya existente (si existe)
                    _logger.LogWarning(ex, "Posible duplicado al crear alerta pendiente para producto {ProductoId}", producto.Id);

                    var existente = await _context.AlertasStock
                        .AsNoTracking()
                        .FirstOrDefaultAsync(a => a.ProductoId == producto.Id && !a.IsDeleted && a.FechaResolucion == null);

                    if (existente != null)
                        return existente;

                    throw;
                }

                _logger.LogWarning(
                    "Alerta de stock generada: {Tipo} - Producto: {ProductoCodigo} - {ProductoNombre}",
                    tipo, producto.Codigo, producto.Nombre);

                return alerta;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear alerta para producto {ProductoId}", producto.Id);
                return null;
            }
        }

        public async Task<List<AlertaStock>> GetAlertasPendientesAsync()
        {
            return await _context.AlertasStock
                .Include(a => a.Producto)
                    .ThenInclude(p => p.Categoria)
                .Include(a => a.Producto)
                    .ThenInclude(p => p.Marca)
                .Where(a => !a.IsDeleted && !a.Producto.IsDeleted && a.Estado == EstadoAlerta.Pendiente)
                .OrderByDescending(a => a.Prioridad)
                .ThenBy(a => a.FechaAlerta)
                .ToListAsync();
        }

        public async Task<AlertaStockViewModel?> GetByIdAsync(int id)
        {
            return await _context.AlertasStock
                .Include(a => a.Producto)
                    .ThenInclude(p => p.Categoria)
                .Include(a => a.Producto)
                    .ThenInclude(p => p.Marca)
                .Where(a => a.Id == id && !a.IsDeleted && a.Producto != null && !a.Producto.IsDeleted)
                .Select(a => MapToViewModel(a))
                .FirstOrDefaultAsync();
        }

        public async Task<bool> ResolverAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null)
        {
            try
            {
                var alerta = await _context.AlertasStock.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
                if (alerta == null)
                    return false;

                if (alerta.Estado != EstadoAlerta.Pendiente)
                    return true; // idempotente

                if (rowVersion is null || rowVersion.Length == 0)
                    throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la alerta e intentá nuevamente.");

                _context.Entry(alerta).Property(a => a.RowVersion).OriginalValue = rowVersion;

                alerta.Estado = EstadoAlerta.Resuelta;
                alerta.FechaResolucion = DateTime.UtcNow;
                alerta.UsuarioResolucion = usuarioResolucion;
                alerta.Observaciones = observaciones;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException("La alerta fue modificada por otro usuario. Por favor, recargue los datos.");
                }

                _logger.LogInformation(
                    "Alerta {AlertaId} resuelta por {Usuario}",
                    id, usuarioResolucion);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al resolver alerta {AlertaId}", id);
                throw;
            }
        }

        public async Task<bool> IgnorarAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null)
        {
            try
            {
                var alerta = await _context.AlertasStock.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
                if (alerta == null)
                    return false;

                if (alerta.Estado != EstadoAlerta.Pendiente)
                    return true; // idempotente

                if (rowVersion is null || rowVersion.Length == 0)
                    throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la alerta e intentá nuevamente.");

                _context.Entry(alerta).Property(a => a.RowVersion).OriginalValue = rowVersion;

                alerta.Estado = EstadoAlerta.Ignorada;
                alerta.FechaResolucion = DateTime.UtcNow;
                alerta.UsuarioResolucion = usuarioResolucion;
                alerta.Observaciones = observaciones;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException("La alerta fue modificada por otro usuario. Por favor, recargue los datos.");
                }

                _logger.LogInformation(
                    "Alerta {AlertaId} ignorada por {Usuario}",
                    id, usuarioResolucion);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ignorar alerta {AlertaId}", id);
                throw;
            }
        }

        public async Task<PaginatedResult<AlertaStockViewModel>> BuscarAsync(AlertaStockFiltroViewModel filtro)
        {
            var query = _context.AlertasStock
                .Include(a => a.Producto)
                    .ThenInclude(p => p.Categoria)
                .Include(a => a.Producto)
                    .ThenInclude(p => p.Marca)
                .Where(a => !a.IsDeleted && !a.Producto.IsDeleted)
                .AsQueryable();

            // Filtros
            if (filtro.ProductoId.HasValue)
                query = query.Where(a => a.ProductoId == filtro.ProductoId.Value);

            if (!string.IsNullOrEmpty(filtro.ProductoCodigo))
                query = query.Where(a => a.Producto.Codigo.Contains(filtro.ProductoCodigo));

            if (!string.IsNullOrEmpty(filtro.ProductoNombre))
                query = query.Where(a => a.Producto.Nombre.Contains(filtro.ProductoNombre));

            if (filtro.Tipo.HasValue)
                query = query.Where(a => a.Tipo == filtro.Tipo.Value);

            if (filtro.Prioridad.HasValue)
                query = query.Where(a => a.Prioridad == filtro.Prioridad.Value);

            if (filtro.Estado.HasValue)
                query = query.Where(a => a.Estado == filtro.Estado.Value);

            if (filtro.FechaDesde.HasValue)
                query = query.Where(a => a.FechaAlerta >= filtro.FechaDesde.Value);

            if (filtro.FechaHasta.HasValue)
                query = query.Where(a => a.FechaAlerta <= filtro.FechaHasta.Value);

            if (filtro.SoloUrgentes == true)
                query = query.Where(a => a.NotificacionUrgente);

            if (filtro.SoloVencidas == true)
            {
                var hace7Dias = DateTime.UtcNow.AddDays(-7);
                query = query.Where(a => a.Estado == EstadoAlerta.Pendiente && a.FechaAlerta < hace7Dias);
            }

            var totalRecords = await query.CountAsync();

            var items = await query
                .OrderByDescending(a => a.Prioridad)
                .ThenByDescending(a => a.FechaAlerta)
                .Skip((filtro.PageNumber - 1) * filtro.PageSize)
                .Take(filtro.PageSize)
                .Select(a => MapToViewModel(a))
                .ToListAsync();

            return new PaginatedResult<AlertaStockViewModel>
            {
                Items = items,
                TotalRecords = totalRecords,
                PageNumber = filtro.PageNumber,
                PageSize = filtro.PageSize
            };
        }

        public async Task<AlertaStockEstadisticasViewModel> GetEstadisticasAsync()
        {
            var todasAlertas = await _context.AlertasStock
                .Include(a => a.Producto)
                    .ThenInclude(p => p.Categoria)
                .Where(a => !a.IsDeleted && !a.Producto.IsDeleted)
                .ToListAsync();

            var alertasPendientes = todasAlertas.Where(a => a.Estado == EstadoAlerta.Pendiente).ToList();
            var alertasResueltas = todasAlertas.Where(a => a.Estado == EstadoAlerta.Resuelta).ToList();
            var alertasIgnoradas = todasAlertas.Where(a => a.Estado == EstadoAlerta.Ignorada).ToList();

            var hace7Dias = DateTime.UtcNow.AddDays(-7);
            var alertasVencidas = await _context.AlertasStock
                .CountAsync(a => !a.IsDeleted && !a.Producto.IsDeleted && a.Estado == EstadoAlerta.Pendiente && a.FechaAlerta < hace7Dias);

            // Calcular promedio de días para resolver
            var promedioResolucion = alertasResueltas.Any()
                ? alertasResueltas
                    .Where(a => a.FechaResolucion.HasValue)
                    .Average(a => (a.FechaResolucion!.Value - a.FechaAlerta).TotalDays)
                : 0;

            // Calcular tasa de resolución
            var tasaResolucion = todasAlertas.Any()
                ? (alertasResueltas.Count * 100.0m) / todasAlertas.Count
                : 0;

            // Contar productos únicos afectados
            var productosAfectados = todasAlertas
                .Select(a => a.ProductoId)
                .Distinct()
                .Count();

            var estadisticas = new AlertaStockEstadisticasViewModel
            {
                // Totales generales
                TotalAlertas = todasAlertas.Count,
                AlertasPendientes = alertasPendientes.Count,
                AlertasResueltas = alertasResueltas.Count,
                AlertasIgnoradas = alertasIgnoradas.Count,

                // Alertas especiales
                AlertasUrgentes = alertasPendientes.Count(a => a.NotificacionUrgente),
                AlertasVencidas = alertasPendientes.Count(a => a.FechaAlerta < hace7Dias),

                // Por prioridad (solo pendientes)
                AlertasCriticas = alertasPendientes.Count(a => a.Prioridad == PrioridadAlerta.Critica),
                AlertasAltas = alertasPendientes.Count(a => a.Prioridad == PrioridadAlerta.Alta),
                AlertasMedias = alertasPendientes.Count(a => a.Prioridad == PrioridadAlerta.Media),

                // Por tipo de alerta (solo pendientes)
                AlertasStockAgotado = alertasPendientes.Count(a => a.Tipo == TipoAlertaStock.StockAgotado),
                AlertasStockCritico = alertasPendientes.Count(a => a.Tipo == TipoAlertaStock.StockCritico),
                AlertasStockBajo = alertasPendientes.Count(a => a.Tipo == TipoAlertaStock.StockBajo),
                AlertasSinMovimiento = alertasPendientes.Count(a => a.Tipo == TipoAlertaStock.ProductoSinMovimiento),

                // Métricas de rendimiento
                PromedioResolucionDias = (decimal)promedioResolucion,
                TasaResolucionPorcentaje = tasaResolucion,
                ProductosAfectados = productosAfectados,

                // Valores y promedios
                PromedioReposicionSugerida = alertasPendientes
                    .Where(a => a.CantidadSugeridaReposicion.HasValue)
                    .Any()
                    ? alertasPendientes
                        .Where(a => a.CantidadSugeridaReposicion.HasValue)
                        .Average(a => a.CantidadSugeridaReposicion ?? 0)
                    : 0,

                ValorTotalStockCritico = alertasPendientes
                    .Where(a => a.Tipo == TipoAlertaStock.StockCritico || a.Tipo == TipoAlertaStock.StockAgotado)
                    .Sum(a => a.Producto.PrecioCompra * a.StockActual),

                // Listas detalladas
                UltimasAlertas = await _context.AlertasStock
                    .Include(a => a.Producto)
                        .ThenInclude(p => p.Categoria)
                    .Include(a => a.Producto)
                        .ThenInclude(p => p.Marca)
                    .Where(a => !a.IsDeleted && !a.Producto.IsDeleted)
                    .OrderByDescending(a => a.FechaAlerta)
                    .Take(10)
                    .Select(a => MapToViewModel(a))
                    .ToListAsync(),

                ProductosMasAlertas = await _context.AlertasStock
                    .Include(a => a.Producto)
                        .ThenInclude(p => p.Categoria)
                    .Include(a => a.Producto)
                        .ThenInclude(p => p.Marca)
                    .Where(a => !a.IsDeleted && !a.Producto.IsDeleted)
                    .GroupBy(a => new
                    {
                        a.ProductoId,
                        a.Producto.Codigo,
                        a.Producto.Nombre,
                        CategoriaNombre = a.Producto.Categoria.Nombre,
                        MarcaNombre = a.Producto.Marca.Nombre,
                        a.Producto.StockActual,
                        a.Producto.StockMinimo,
                        a.Producto.PrecioVenta,
                        a.Producto.PrecioCompra
                    })
                    .Select(g => new ProductoAlertaViewModel
                    {
                        ProductoId = g.Key.ProductoId,
                        ProductoCodigo = g.Key.Codigo,
                        ProductoNombre = g.Key.Nombre,
                        CategoriaNombre = g.Key.CategoriaNombre,
                        MarcaNombre = g.Key.MarcaNombre,
                        StockActual = g.Key.StockActual,
                        StockMinimo = g.Key.StockMinimo,
                        PrecioVenta = g.Key.PrecioVenta,
                        PrecioCompra = g.Key.PrecioCompra,
                        TotalAlertas = g.Count(),
                        UltimoTipoAlerta = g.OrderByDescending(a => a.FechaAlerta).First().Tipo,
                        UltimaPrioridad = g.OrderByDescending(a => a.FechaAlerta).First().Prioridad,
                        FechaUltimaAlerta = g.Max(a => a.FechaAlerta)
                    })
                    .OrderByDescending(p => p.TotalAlertas)
                    .Take(10)
                    .ToListAsync()
            };

            return estadisticas;
        }

        public async Task<List<AlertaStock>> GetAlertasByProductoIdAsync(int productoId)
        {
            return await _context.AlertasStock
                .Include(a => a.Producto)
                .Where(a => !a.IsDeleted && a.ProductoId == productoId && a.Producto != null && !a.Producto.IsDeleted)
                .OrderByDescending(a => a.FechaAlerta)
                .ToListAsync();
        }

        public async Task<int> LimpiarAlertasAntiguasAsync(int diasAntiguedad = 30)
        {
            try
            {
                var fechaLimite = DateTime.UtcNow.AddDays(-diasAntiguedad);

                var now = DateTime.UtcNow;
                var updated = await _context.AlertasStock
                    .Where(a =>
                        !a.IsDeleted &&
                        (a.Estado == EstadoAlerta.Resuelta || a.Estado == EstadoAlerta.Ignorada) &&
                        a.FechaResolucion.HasValue &&
                        a.FechaResolucion.Value < fechaLimite)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(a => a.IsDeleted, true)
                        .SetProperty(a => a.UpdatedAt, now));

                _logger.LogInformation("Eliminadas {Cantidad} alertas antiguas", updated);
                return updated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al limpiar alertas antiguas");
                return 0;
            }
        }

        public async Task<List<ProductoCriticoViewModel>> GetProductosCriticosAsync()
        {
            // Obtener productos con alertas críticas o agotadas pendientes
            var productosConAlertas = await _context.AlertasStock
                .Include(a => a.Producto)
                    .ThenInclude(p => p.Categoria)
                .Include(a => a.Producto)
                    .ThenInclude(p => p.Marca)
                .Where(a =>
                    !a.IsDeleted &&
                    !a.Producto.IsDeleted &&
                    a.Estado == EstadoAlerta.Pendiente &&
                    (a.Tipo == TipoAlertaStock.StockCritico || a.Tipo == TipoAlertaStock.StockAgotado))
                .Select(a => a.Producto)
                .Distinct()
                .ToListAsync();

            if (productosConAlertas.Count == 0)
                return new List<ProductoCriticoViewModel>();

            var productoIds = productosConAlertas.Select(p => p.Id).ToList();

            var alertasPendientesByProductoId = await _context.AlertasStock
                .AsNoTracking()
                .Where(a => !a.IsDeleted && productoIds.Contains(a.ProductoId) && a.Estado == EstadoAlerta.Pendiente)
                .GroupBy(a => a.ProductoId)
                .Select(g => new { ProductoId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ProductoId, x => x.Count);

            var ultimaVentaByProductoId = await _context.VentaDetalles
                .AsNoTracking()
                .Where(vd => productoIds.Contains(vd.ProductoId))
                .GroupBy(vd => vd.ProductoId)
                .Select(g => new { ProductoId = g.Key, UltimaVenta = g.Max(vd => (DateTime?)vd.Venta.FechaVenta) })
                .ToDictionaryAsync(x => x.ProductoId, x => x.UltimaVenta);

            // Mapear a ViewModels con información adicional
            var resultado = new List<ProductoCriticoViewModel>();

            foreach (var producto in productosConAlertas)
            {
                alertasPendientesByProductoId.TryGetValue(producto.Id, out var alertasPendientes);
                ultimaVentaByProductoId.TryGetValue(producto.Id, out var ultimaVenta);
                var diasDesdeUltimaVenta = ultimaVenta.HasValue
                    ? (DateTime.UtcNow - ultimaVenta.Value).Days
                    : 0;

                // Calcular cantidad sugerida de reposición (3x el mínimo - actual)
                var cantidadSugerida = (producto.StockMinimo * 3) - producto.StockActual;

                resultado.Add(new ProductoCriticoViewModel
                {
                    Id = producto.Id,
                    Codigo = producto.Codigo,
                    Nombre = producto.Nombre,
                    CategoriaNombre = producto.Categoria?.Nombre ?? "",
                    MarcaNombre = producto.Marca?.Nombre,
                    StockActual = producto.StockActual,
                    StockMinimo = producto.StockMinimo,
                    PrecioCompra = producto.PrecioCompra,
                    PrecioVenta = producto.PrecioVenta,
                    CantidadSugeridaReposicion = cantidadSugerida > 0 ? cantidadSugerida : null,
                    AlertasPendientes = alertasPendientes,
                    UltimaVenta = ultimaVenta,
                    DiasDesdeUltimaVenta = diasDesdeUltimaVenta
                });
            }

            return resultado.OrderBy(p => p.StockActual).ToList();
        }

        private static AlertaStockViewModel MapToViewModel(AlertaStock alerta)
        {
            return new AlertaStockViewModel
            {
                Id = alerta.Id,
                RowVersion = alerta.RowVersion,
                ProductoId = alerta.ProductoId,
                ProductoCodigo = alerta.Producto?.Codigo ?? string.Empty,
                ProductoNombre = alerta.Producto?.Nombre ?? string.Empty,
                CategoriaNombre = alerta.Producto?.Categoria?.Nombre ?? string.Empty,
                MarcaNombre = alerta.Producto?.Marca?.Nombre ?? string.Empty,
                Tipo = alerta.Tipo,
                Prioridad = alerta.Prioridad,
                Estado = alerta.Estado,
                Mensaje = alerta.Mensaje,
                StockActual = alerta.StockActual,
                StockMinimo = alerta.StockMinimo,
                CantidadSugeridaReposicion = alerta.CantidadSugeridaReposicion,
                FechaAlerta = alerta.FechaAlerta,
                FechaResolucion = alerta.FechaResolucion,
                UsuarioResolucion = alerta.UsuarioResolucion,
                Observaciones = alerta.Observaciones,
                NotificacionUrgente = alerta.NotificacionUrgente,
                PorcentajeStockMinimo = alerta.PorcentajeStockMinimo,
                DiasDesdeAlerta = alerta.DiasDesdeAlerta,
                EstaVencida = alerta.EstaVencida
            };
        }
    }
}
