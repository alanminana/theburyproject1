using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class OrdenCompraService : IOrdenCompraService
    {
        #region Constructor y dependencias

        private readonly AppDbContext _context;
        private readonly ILogger<OrdenCompraService> _logger;
        private readonly IMovimientoStockService _movimientoStockService;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISeguridadAuditoriaService? _auditoria;

        public OrdenCompraService(
            AppDbContext context,
            ILogger<OrdenCompraService> logger,
            IMovimientoStockService movimientoStockService,
            ICurrentUserService currentUserService,
            ISeguridadAuditoriaService? auditoria = null)
        {
            _context = context;
            _logger = logger;
            _movimientoStockService = movimientoStockService;
            _currentUserService = currentUserService;
            _auditoria = auditoria;
        }

        #endregion

        #region CRUD

        public async Task<IEnumerable<OrdenCompra>> GetAllAsync()
        {
            return await _context.OrdenesCompra
                .AsNoTracking()
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

            await ValidarProveedorYProductosAsync(ordenCompra, "crear");
            PrepararOrdenNueva(ordenCompra);

            await CalcularTotalesAsync(ordenCompra);
            ValidarDescuentoNoSuperaSubtotal(ordenCompra);
            _context.OrdenesCompra.Add(ordenCompra);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Orden de compra {Numero} creada exitosamente", ordenCompra.Numero);

            return ordenCompra;
        }

        /// <summary>
        /// COM-S-006: una orden nueva solo puede nacer en un estado inicial del circuito (Borrador, Enviada, Confirmada o
        /// Cancelada, los que ofrece el formulario), sin recepción previa: EnTransito y Recibida solo se alcanzan
        /// recorriendo el circuito (Recepcionar ingresa el stock), y CantidadRecibida/FechaRecepcion no vienen del cliente.
        /// Además, cantidades, precios y descuento deben ser coherentes (un descuento negativo subiría el total).
        /// </summary>
        private static void PrepararOrdenNueva(OrdenCompra ordenCompra)
        {
            if (ordenCompra.Estado is EstadoOrdenCompra.EnTransito or EstadoOrdenCompra.Recibida)
            {
                throw new InvalidOperationException(
                    "Una orden nueva no puede crearse En tránsito ni Recibida: avance el estado desde el circuito y registre la recepción.");
            }

            ordenCompra.FechaRecepcion = null;
            foreach (var detalle in ordenCompra.Detalles ?? new List<OrdenCompraDetalle>())
                detalle.CantidadRecibida = 0;

            ValidarImportes(ordenCompra);
        }

        private static void ValidarImportes(OrdenCompra ordenCompra)
        {
            foreach (var detalle in ordenCompra.Detalles ?? new List<OrdenCompraDetalle>())
            {
                if (detalle.Cantidad <= 0)
                    throw new InvalidOperationException("La cantidad de cada producto debe ser mayor a cero.");
                if (detalle.PrecioUnitario < 0)
                    throw new InvalidOperationException("El precio unitario no puede ser negativo.");
            }

            if (ordenCompra.Descuento < 0)
                throw new InvalidOperationException("El descuento no puede ser negativo.");
        }

        private static void ValidarDescuentoNoSuperaSubtotal(OrdenCompra ordenCompra)
        {
            if (ordenCompra.Descuento > ordenCompra.Subtotal)
                throw new InvalidOperationException("El descuento no puede superar el subtotal de la orden.");
        }

        private async Task ValidarProveedorYProductosAsync(OrdenCompra ordenCompra, string accion)
        {
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
                        $"No se puede {accion} la orden. Productos no asociados al proveedor '{proveedor.RazonSocial}': {string.Join(", ", productosNoAsociados)}.");
                }
            }
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

            if (ordenExistente.Estado != EstadoOrdenCompra.Borrador && ordenExistente.Estado != EstadoOrdenCompra.Enviada)
            {
                throw new InvalidOperationException("Solo se pueden editar órdenes en estado Borrador o Enviada");
            }

            if (ordenCompra.Detalles == null || ordenCompra.Detalles.Count == 0)
            {
                throw new InvalidOperationException("Debe agregar al menos un producto a la orden");
            }

            _context.Entry(ordenExistente).Property(o => o.RowVersion).OriginalValue = ordenCompra.RowVersion;

            if (await NumeroOrdenExisteAsync(ordenCompra.Numero, ordenCompra.Id))
            {
                throw new InvalidOperationException($"Ya existe otra orden con el número {ordenCompra.Numero}");
            }

            await ValidarProveedorYProductosAsync(ordenCompra, "guardar");
            ValidarImportes(ordenCompra);
            await CalcularTotalesAsync(ordenCompra);
            ValidarDescuentoNoSuperaSubtotal(ordenCompra);

            ordenExistente.Numero = ordenCompra.Numero;
            ordenExistente.ProveedorId = ordenCompra.ProveedorId;
            ordenExistente.FechaEmision = ordenCompra.FechaEmision;
            ordenExistente.FechaEntregaEstimada = ordenCompra.FechaEntregaEstimada;
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
                    detalleExistente.PorcentajeIVA = detalleNuevo.PorcentajeIVA;
                    detalleExistente.IvaImporte = detalleNuevo.IvaImporte;
                }
                else
                {
                    detalleNuevo.OrdenCompraId = ordenExistente.Id;
                    detalleNuevo.CantidadRecibida = 0;   // COM-S-006: la recepción no se declara al editar
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

            // H9-M6: una orden cancelada con recepción parcial conserva el stock ya ingresado y sus movimientos de Kardex
            // ("Orden de Compra OC-...") apuntan a su número. Eliminarla liberaría el número (índice único filtrado por
            // IsDeleted) y una orden nueva lo reutilizaría, dejando ambigua la referencia del Kardex.
            if ((orden.Detalles ?? new List<OrdenCompraDetalle>()).Any(d => d.CantidadRecibida > 0)
                || await _context.MovimientosStock.AnyAsync(m => m.OrdenCompraId == id && !m.IsDeleted))
            {
                throw new InvalidOperationException(
                    "No se puede eliminar una orden con mercadería ya recepcionada: el stock ingresado y el Kardex referencian su número. Dejala como Cancelada.");
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

        #endregion

        #region Búsqueda

        public async Task<IEnumerable<OrdenCompra>> SearchAsync(
            string? searchTerm = null,
            int? proveedorId = null,
            EstadoOrdenCompra? estado = null,
            DateTime? fechaDesde = null,
            DateTime? fechaHasta = null,
            string? orderBy = null,
            string? orderDirection = "asc",
            int? marcaId = null,
            string? productoTerm = null)
        {
            var query = _context.OrdenesCompra
                .AsNoTracking()
                .Include(o => o.Proveedor)
                .Include(o => o.Detalles.Where(d => !d.IsDeleted))
                    .ThenInclude(d => d.Producto)
                        .ThenInclude(p => p!.Marca)
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

            if (marcaId.HasValue)
                query = query.Where(o => o.Detalles.Any(d => !d.IsDeleted && d.Producto != null && d.Producto.MarcaId == marcaId.Value));

            if (!string.IsNullOrWhiteSpace(productoTerm))
            {
                var termino = productoTerm.Trim();
                query = query.Where(o => o.Detalles.Any(d => !d.IsDeleted && d.Producto != null &&
                    (d.Producto.Nombre.Contains(termino) || d.Producto.Codigo.Contains(termino))));
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
                .AsNoTracking()
                .Include(o => o.Detalles.Where(d => !d.IsDeleted))
                .Where(o => o.ProveedorId == proveedorId && !o.IsDeleted)
                .OrderByDescending(o => o.FechaEmision)
                .ToListAsync();
        }

        #endregion

        #region Operaciones

        public async Task<bool> CambiarEstadoAsync(int id, EstadoOrdenCompra nuevoEstado)
        {
            var orden = await _context.OrdenesCompra.Include(o => o.Detalles).FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted);
            if (orden == null) return false;

            if (!OrdenCompraTransiciones.EsValida(orden.Estado, nuevoEstado))
            {
                throw new InvalidOperationException(
                    $"No se puede pasar una orden de {OrdenCompraUiHelper.EstadoNombre(orden.Estado)} a {OrdenCompraUiHelper.EstadoNombre(nuevoEstado)}. Los estados solo avanzan.");
            }

            // Una orden pasa a Recibida registrando la recepción (RecepcionarAsync): es lo que ingresa el stock y deja
            // CantidadRecibida/FechaRecepcion coherentes. Marcarla Recibida desde acá la daba por recibida SIN stock.
            if (nuevoEstado == EstadoOrdenCompra.Recibida)
            {
                throw new InvalidOperationException(
                    "Una orden pasa a Recibida registrando la recepción de la mercadería (Recepcionar): eso es lo que ingresa el stock.");
            }

            var estadoPrevio = orden.Estado;
            orden.Estado = nuevoEstado;

            // H9-M6: cancelar no revierte la mercadería ya recepcionada (el stock y el Kardex quedan; devolver al proveedor es
            // otro circuito). Se deja constancia en la auditoría, en la misma operación que el cambio de estado.
            if (nuevoEstado == EstadoOrdenCompra.Cancelada)
            {
                var detalles = (orden.Detalles ?? new List<OrdenCompraDetalle>()).Where(d => !d.IsDeleted).ToList();
                var solicitadas = detalles.Sum(d => d.Cantidad);
                var recibidas = detalles.Sum(d => Math.Min(d.CantidadRecibida, d.Cantidad));
                _auditoria?.AgregarEvento("Compras", "CancelarOrdenCompra", $"OrdenCompra:{orden.Id}",
                    $"Numero={orden.Numero}; EstadoPrevio={estadoPrevio}; Recibido={recibidas}/{solicitadas}; " +
                    (recibidas > 0
                        ? $"Pendiente cancelado={solicitadas - recibidas}; stock ingresado conservado (sin reversión)"
                        : "sin mercadería recepcionada"));
            }

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
            var anio = DateTime.UtcNow.Year;
            var prefijo = $"{await PrefijosNumeracion.ObtenerAsync(_context, TipoPrefijoNumeracion.OrdenCompra)}-{anio}-";
            
            // Obtener el último número de orden del año actual
            var ultimaOrden = await _context.OrdenesCompra
                .AsNoTracking()
                .Where(o => o.Numero.StartsWith(prefijo) && !o.IsDeleted)
                .OrderByDescending(o => o.Numero)
                .Select(o => o.Numero)
                .FirstOrDefaultAsync();

            int siguienteNumero = 1;
            
            if (ultimaOrden != null)
            {
                // Extraer el número secuencial del formato OC-YYYY-NNNN
                var partes = ultimaOrden.Split('-');
                if (partes.Length >= 3 && int.TryParse(partes[^1], out int numeroActual))
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

            var usuario = _currentUserService.GetUsername();

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var entradas = new List<(int productoId, decimal cantidad, string? referencia)>();
            var costos = new List<MovimientoStockCostoLinea>();
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
                costos.Add(new MovimientoStockCostoLinea(
                    detalle.ProductoId,
                    recepcion.CantidadARecepcionar,
                    referencia,
                    detalle.PrecioUnitario,
                    "OrdenCompraDetalle"));

            }

            // Nada que ingresar: no es una recepcion (antes una llamada vacia cerraba la orden como Recibida).
            if (entradas.Count == 0)
            {
                throw new InvalidOperationException("Indique al menos una cantidad a recepcionar.");
            }

            // La orden se cierra solo si TODAS sus lineas quedaron completas (no solo las que se recibieron ahora): recibir
            // por completo un producto y dejar otro en cero no puede dar la orden por recibida.
            var todosRecibidos = orden.Detalles != null && orden.Detalles.Count > 0
                && orden.Detalles.All(d => d.CantidadRecibida >= d.Cantidad);

            // H3-Q1: una recepcion parcial sobre una orden que ya esta EnTransito solo modifica las lineas, no la cabecera:
            // el RowVersion de la orden no cambiaba y el mismo formulario reenviado (doble envio, pestana vieja) volvia a
            // ingresar la mercaderia. Tocar la cabecera fuerza el UPDATE con su RowVersion original: un formulario viejo
            // choca (la orden ya fue modificada) y cada recepcion avanza el token que ve el siguiente formulario.
            orden.UpdatedAt = DateTime.UtcNow;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "La orden fue modificada por otro usuario. Recargá la página y volvé a intentar.");
            }

            await _movimientoStockService.RegistrarEntradasAsync(
                entradas,
                "Recepción de mercadería",
                usuario,
                ordenCompraId: orden.Id,
                costos: costos);

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

        #endregion

        #region Cálculos

        public async Task<decimal> CalcularTotalOrdenAsync(int ordenId)
        {
            var orden = await GetByIdAsync(ordenId);
            return orden?.Total ?? 0;
        }

        /// <summary>
        /// Los precios unitarios de compra se ingresan CON IVA incluido. El total es
        /// Subtotal - Descuento (el IVA nunca se vuelve a sumar) y el IVA se desglosa
        /// por línea con la alícuota vigente de cada producto (snapshot histórico),
        /// calculado por diferencia para que neto + IVA = total.
        /// </summary>
        private async Task CalcularTotalesAsync(OrdenCompra ordenCompra)
        {
            var detalles = ordenCompra.Detalles ?? new List<OrdenCompraDetalle>();

            foreach (var detalle in detalles)
            {
                detalle.Subtotal = detalle.Cantidad * detalle.PrecioUnitario;
            }

            ordenCompra.Subtotal = detalles.Sum(d => d.Subtotal);

            // VALID-L4: una cantidad absurda (p. ej. int.MaxValue) producía una orden de billones; el subtotal respeta
            // el mismo tope de monto del ERP que el resto de los importes.
            if (ordenCompra.Subtotal > Validation.MontoArgentinoAttribute.Maximo)
            {
                throw new InvalidOperationException(
                    $"El subtotal de la orden no puede superar ${Validation.MontoArgentinoAttribute.Maximo:N0}. Revise cantidades y precios.");
            }

            var total = Math.Max(0m, ordenCompra.Subtotal - ordenCompra.Descuento);
            ordenCompra.Total = total;

            var productoIds = detalles.Select(d => d.ProductoId).Distinct().ToList();
            var productos = await _context.Productos
                .AsNoTracking()
                .Include(p => p.AlicuotaIVA)
                .Include(p => p.Categoria)
                    .ThenInclude(c => c.AlicuotaIVA)
                .Where(p => productoIds.Contains(p.Id))
                .ToListAsync();

            var porcentajePorProducto = productos.ToDictionary(
                p => p.Id,
                ProductoIvaResolver.ResolverPorcentajeIVAProducto);

            // El descuento de cabecera se prorratea entre líneas para el desglose.
            var factorDescuento = ordenCompra.Subtotal > 0 ? total / ordenCompra.Subtotal : 0m;

            foreach (var detalle in detalles)
            {
                detalle.PorcentajeIVA = porcentajePorProducto.TryGetValue(detalle.ProductoId, out var porcentaje)
                    ? porcentaje
                    : ProductoIvaResolver.PorcentajeDefault;

                var subtotalConDescuento = Math.Round(detalle.Subtotal * factorDescuento, 2, MidpointRounding.AwayFromZero);
                detalle.IvaImporte = PrecioIvaCalculator.CalcularIvaIncluido(subtotalConDescuento, detalle.PorcentajeIVA);
            }

            ordenCompra.Iva = detalles.Sum(d => d.IvaImporte);
        }

        #endregion
    }
}
