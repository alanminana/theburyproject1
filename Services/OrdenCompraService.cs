using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class OrdenCompraService : IOrdenCompraService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<OrdenCompraService> _logger;
        private readonly IMovimientoStockService _movimientoStockService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public OrdenCompraService(
            AppDbContext context,
            ILogger<OrdenCompraService> logger,
            IMovimientoStockService movimientoStockService,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _logger = logger;
            _movimientoStockService = movimientoStockService;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IEnumerable<OrdenCompra>> GetAllAsync()
        {
            return await _context.OrdenesCompra
                .Where(o => !o.IsDeleted)
                .Include(o => o.Proveedor)
                .Include(o => o.Detalles.Where(d => !d.IsDeleted))
                    .ThenInclude(d => d.Producto)
                .AsSplitQuery()
                .OrderByDescending(o => o.FechaEmision)
                .ToListAsync();
        }

        public async Task<OrdenCompra?> GetByIdAsync(int id)
        {
            var orden = await _context.OrdenesCompra
                .Include(o => o.Proveedor)
                .Include(o => o.Detalles.Where(d => !d.IsDeleted))
                    .ThenInclude(d => d.Producto)
                        .ThenInclude(p => p!.Marca)
                .Include(o => o.Detalles.Where(d => !d.IsDeleted))
                    .ThenInclude(d => d.Producto)
                        .ThenInclude(p => p!.Categoria)
                .AsSplitQuery()
                .FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted);

            if (orden == null)
            {
                _logger.LogWarning("Orden {Id} NO encontrada", id);
                return null;
            }
            return orden;
        }

        public async Task<OrdenCompra> CreateAsync(OrdenCompra ordenCompra)
        {
            if (await NumeroOrdenExisteAsync(ordenCompra.Numero))
            {
                throw new InvalidOperationException($"Ya existe una orden con el número {ordenCompra.Numero}");
            }

            var proveedor = await _context.Proveedores
                .Include(p => p.ProveedorProductos)
                .FirstOrDefaultAsync(p => p.Id == ordenCompra.ProveedorId && !p.IsDeleted);

            if (proveedor == null)
            {
                throw new InvalidOperationException("El proveedor especificado no existe");
            }

            if (proveedor.ProveedorProductos.Any())
            {
                var productosAsociadosIds = proveedor.ProveedorProductos
                    .Select(pp => pp.ProductoId)
                    .ToHashSet();

                var detalleIds = (ordenCompra.Detalles ?? new List<OrdenCompraDetalle>())
                    .Select(d => d.ProductoId)
                    .Distinct()
                    .ToList();

                var productosNoAsociadosIds = detalleIds
                    .Where(id => !productosAsociadosIds.Contains(id))
                    .ToList();

                if (productosNoAsociadosIds.Any())
                {
                    var nombres = await _context.Productos
                        .AsNoTracking()
                        .Where(p => productosNoAsociadosIds.Contains(p.Id) && !p.IsDeleted)
                        .Select(p => new { p.Id, p.Nombre })
                        .ToListAsync();

                    var nombresPorId = nombres
                        .GroupBy(x => x.Id)
                        .ToDictionary(g => g.Key, g => g.First().Nombre);

                    var productosNoAsociados = productosNoAsociadosIds
                        .Select(id => nombresPorId.TryGetValue(id, out var nombre) ? nombre : $"ID {id}")
                        .ToList();

                    throw new InvalidOperationException(
                        $"No se puede crear la orden. Productos no asociados al proveedor '{proveedor.RazonSocial}': {string.Join(", ", productosNoAsociados)}.");
                }
            }

            CalcularTotales(ordenCompra);
            _context.OrdenesCompra.Add(ordenCompra);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Orden de compra {Numero} creada exitosamente", ordenCompra.Numero);

            return ordenCompra;
        }

        public async Task<OrdenCompra> UpdateAsync(OrdenCompra ordenCompra)
        {
            if (ordenCompra.RowVersion == null || ordenCompra.RowVersion.Length == 0)
                throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la orden e intentá nuevamente.");

            var ordenExistente = await GetByIdAsync(ordenCompra.Id);
            if (ordenExistente == null)
            {
                throw new InvalidOperationException("La orden de compra no existe");
            }

            _context.Entry(ordenExistente).Property(o => o.RowVersion).OriginalValue = ordenCompra.RowVersion;

            if (await NumeroOrdenExisteAsync(ordenCompra.Numero, ordenCompra.Id))
            {
                throw new InvalidOperationException($"Ya existe otra orden con el número {ordenCompra.Numero}");
            }

            CalcularTotales(ordenCompra);

            ordenExistente.Numero = ordenCompra.Numero;
            ordenExistente.ProveedorId = ordenCompra.ProveedorId;
            ordenExistente.FechaEmision = ordenCompra.FechaEmision;
            ordenExistente.FechaEntregaEstimada = ordenCompra.FechaEntregaEstimada;
            ordenExistente.FechaRecepcion = ordenCompra.FechaRecepcion;
            ordenExistente.Estado = ordenCompra.Estado;
            ordenExistente.Subtotal = ordenCompra.Subtotal;
            ordenExistente.Descuento = ordenCompra.Descuento;
            ordenExistente.Iva = ordenCompra.Iva;
            ordenExistente.Total = ordenCompra.Total;
            ordenExistente.Observaciones = ordenCompra.Observaciones;

            var existingDetalles = ordenExistente.Detalles ?? new List<OrdenCompraDetalle>();

            var detallesAEliminar = existingDetalles
                .Where(d => !ordenCompra.Detalles.Any(nd => nd.Id == d.Id))
                .ToList();

            foreach (var detalle in detallesAEliminar)
            {
                detalle.IsDeleted = true;
            }

            foreach (var detalleNuevo in ordenCompra.Detalles)
            {
                var detalleExistente = existingDetalles.FirstOrDefault(d => d.Id == detalleNuevo.Id);
                if (detalleExistente != null)
                {
                    detalleExistente.ProductoId = detalleNuevo.ProductoId;
                    detalleExistente.Cantidad = detalleNuevo.Cantidad;
                    detalleExistente.PrecioUnitario = detalleNuevo.PrecioUnitario;
                    detalleExistente.Subtotal = detalleNuevo.Subtotal;
                    detalleExistente.CantidadRecibida = detalleNuevo.CantidadRecibida;
                }
                else
                {
                    detalleNuevo.OrdenCompraId = ordenExistente.Id;
                    (ordenExistente.Detalles ??= new List<OrdenCompraDetalle>())
                        .Add(detalleNuevo);
                }
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "La orden fue modificada por otro usuario. Recargá la página y volvé a intentar.");
            }
            _logger.LogInformation("Orden de compra {Numero} actualizada exitosamente", ordenCompra.Numero);
            return ordenExistente;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var orden = await GetByIdAsync(id);
            if (orden == null) return false;

            if (orden.Estado == EstadoOrdenCompra.Recibida || orden.Estado == EstadoOrdenCompra.EnTransito)
            {
                throw new InvalidOperationException("No se puede eliminar una orden en tránsito o recibida");
            }

            if (await _context.Cheques.AnyAsync(c => c.OrdenCompraId == id && !c.IsDeleted))
            {
                throw new InvalidOperationException("No se puede eliminar una orden con cheques asociados");
            }

            orden.IsDeleted = true;
            foreach (var detalle in (orden.Detalles ?? new List<OrdenCompraDetalle>()))
                detalle.IsDeleted = true;
            await _context.SaveChangesAsync();
            _logger.LogInformation("Orden de compra {Id} eliminada exitosamente", id);
            return true;
        }

        public async Task<IEnumerable<OrdenCompra>> SearchAsync(
            string? searchTerm = null,
            int? proveedorId = null,
            EstadoOrdenCompra? estado = null,
            DateTime? fechaDesde = null,
            DateTime? fechaHasta = null,
            string? orderBy = null,
            string? orderDirection = "asc")
        {
            var query = _context.OrdenesCompra
                .Include(o => o.Proveedor)
                .Include(o => o.Detalles.Where(d => !d.IsDeleted))
                    .ThenInclude(d => d.Producto)
                .AsSplitQuery()
                .AsQueryable();

            query = query.Where(o => !o.IsDeleted);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(o =>
                    o.Numero.Contains(searchTerm) ||
                    (o.Proveedor != null && o.Proveedor.RazonSocial.Contains(searchTerm)) ||
                    (o.Proveedor != null && o.Proveedor.NombreFantasia != null && o.Proveedor.NombreFantasia.Contains(searchTerm)) ||
                    (o.Observaciones != null && o.Observaciones.Contains(searchTerm)));
            }

            if (proveedorId.HasValue)
                query = query.Where(o => o.ProveedorId == proveedorId.Value);

            if (estado.HasValue)
                query = query.Where(o => o.Estado == estado.Value);

            if (fechaDesde.HasValue)
                query = query.Where(o => o.FechaEmision >= fechaDesde.Value);

            if (fechaHasta.HasValue)
                query = query.Where(o => o.FechaEmision <= fechaHasta.Value);

            query = orderBy?.ToLower() switch
            {
                "numero" => orderDirection == "desc" ? query.OrderByDescending(o => o.Numero) : query.OrderBy(o => o.Numero),
                "proveedor" => orderDirection == "desc" ? query.OrderByDescending(o => o.Proveedor.RazonSocial) : query.OrderBy(o => o.Proveedor.RazonSocial),
                "fechaemision" => orderDirection == "desc" ? query.OrderByDescending(o => o.FechaEmision) : query.OrderBy(o => o.FechaEmision),
                "estado" => orderDirection == "desc" ? query.OrderByDescending(o => o.Estado) : query.OrderBy(o => o.Estado),
                "total" => orderDirection == "desc" ? query.OrderByDescending(o => o.Total) : query.OrderBy(o => o.Total),
                _ => query.OrderByDescending(o => o.FechaEmision)
            };

            return await query.ToListAsync();
        }

        public async Task<IEnumerable<OrdenCompra>> GetByProveedorIdAsync(int proveedorId)
        {
            return await _context.OrdenesCompra
                .Include(o => o.Detalles.Where(d => !d.IsDeleted))
                .Where(o => o.ProveedorId == proveedorId && !o.IsDeleted)
                .OrderByDescending(o => o.FechaEmision)
                .ToListAsync();
        }

        public async Task<bool> CambiarEstadoAsync(int id, EstadoOrdenCompra nuevoEstado)
        {
            var orden = await _context.OrdenesCompra.FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted);
            if (orden == null) return false;

            if (nuevoEstado == EstadoOrdenCompra.Recibida && orden.Estado != EstadoOrdenCompra.Recibida)
                orden.FechaRecepcion = DateTime.UtcNow;

            orden.Estado = nuevoEstado;
            await _context.SaveChangesAsync();
            _logger.LogInformation("Estado de orden {Id} cambiado a {Estado}", id, nuevoEstado);
            return true;
        }

        public async Task<bool> NumeroOrdenExisteAsync(string numero, int? excludeId = null)
        {
            return await _context.OrdenesCompra
                .AnyAsync(o =>
                    o.Numero == numero &&
                    !o.IsDeleted &&
                    (excludeId == null || o.Id != excludeId.Value));
        }

        public async Task<string> GenerarNumeroOrdenAsync()
        {
            var anio = DateTime.Now.Year;
            var prefijo = $"OC-{anio}-";
            
            // Obtener el último número de orden del año actual
            var ultimaOrden = await _context.OrdenesCompra
                .Where(o => o.Numero.StartsWith(prefijo) && !o.IsDeleted)
                .OrderByDescending(o => o.Numero)
                .Select(o => o.Numero)
                .FirstOrDefaultAsync();

            int siguienteNumero = 1;
            
            if (ultimaOrden != null)
            {
                // Extraer el número secuencial del formato OC-YYYY-NNNN
                var partes = ultimaOrden.Split('-');
                if (partes.Length >= 3 && int.TryParse(partes[2], out int numeroActual))
                {
                    siguienteNumero = numeroActual + 1;
                }
            }

            return $"{prefijo}{siguienteNumero:D4}"; // D4 = 4 dígitos con ceros a la izquierda
        }

        public async Task<OrdenCompra> RecepcionarAsync(int ordenId, byte[] rowVersion, List<RecepcionDetalleViewModel> detallesRecepcion)
        {
            var orden = await GetByIdAsync(ordenId);
            if (orden == null)
                throw new InvalidOperationException("Orden no encontrada");

            if (rowVersion == null || rowVersion.Length == 0)
                throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la orden e intentá nuevamente.");

            _context.Entry(orden).Property(o => o.RowVersion).OriginalValue = rowVersion;

            if (orden.Estado != EstadoOrdenCompra.Confirmada &&
                orden.Estado != EstadoOrdenCompra.EnTransito)
            {
                throw new InvalidOperationException("Solo se pueden recepcionar órdenes confirmadas o en tránsito");
            }

            var usuario = _httpContextAccessor?.HttpContext?.User?.Identity?.Name ?? "System";

            await using var transaction = await _context.Database.BeginTransactionAsync();

            bool todosRecibidos = true;

            var entradas = new List<(int productoId, decimal cantidad, string? referencia)>();
            var referencia = $"Orden de Compra {orden.Numero}";

            foreach (var recepcion in detallesRecepcion)
            {
                if (recepcion.CantidadARecepcionar <= 0) continue;

                var detalle = orden.Detalles?.FirstOrDefault(d => d.Id == recepcion.DetalleId);
                if (detalle == null) continue;

                int cantidadSolicitada = detalle.Cantidad;
                int cantidadRecibidaActual = detalle.CantidadRecibida;

                int totalRecibido = cantidadRecibidaActual + recepcion.CantidadARecepcionar;

                if (totalRecibido > cantidadSolicitada)
                {
                    throw new InvalidOperationException(
                        $"No se puede recepcionar más de lo solicitado para {detalle.Producto?.Nombre ?? "producto"}"
                    );
                }

                detalle.CantidadRecibida = totalRecibido;

                // Stock + movimiento centralizado (batch, respeta auditoría, validaciones y transacción)
                entradas.Add((detalle.ProductoId, recepcion.CantidadARecepcionar, referencia));

                if (detalle.CantidadRecibida < cantidadSolicitada)
                    todosRecibidos = false;
            }

            await _movimientoStockService.RegistrarEntradasAsync(
                entradas,
                "Recepción de mercadería",
                usuario,
                ordenCompraId: orden.Id);

            if (todosRecibidos)
            {
                orden.Estado = EstadoOrdenCompra.Recibida;
                orden.FechaRecepcion = DateTime.UtcNow;
            }
            else if (orden.Estado == EstadoOrdenCompra.Confirmada)
            {
                orden.Estado = EstadoOrdenCompra.EnTransito;
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "La orden fue modificada por otro usuario. Recargá la página y volvé a intentar.");
            }
            await transaction.CommitAsync();
            return orden;
        }

        public async Task<decimal> CalcularTotalOrdenAsync(int ordenId)
        {
            var orden = await GetByIdAsync(ordenId);
            return orden?.Total ?? 0;
        }

        private void CalcularTotales(OrdenCompra ordenCompra)
        {
            foreach (var detalle in ordenCompra.Detalles)
            {
                detalle.Subtotal = detalle.Cantidad * detalle.PrecioUnitario;
            }

            ordenCompra.Subtotal = ordenCompra.Detalles.Sum(d => d.Subtotal);
            var subtotalConDescuento = ordenCompra.Subtotal - ordenCompra.Descuento;
            ordenCompra.Iva = subtotalConDescuento * 0.21m;
            ordenCompra.Total = subtotalConDescuento + ordenCompra.Iva;
        }
    }
}
