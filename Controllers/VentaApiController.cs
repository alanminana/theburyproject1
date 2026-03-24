using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "ventas", Accion = "view")]
    [ApiController]
    [Route("api/ventas/[action]")]
    public class VentaApiController : ControllerBase
    {
        private readonly IProductoService _productoService;
        private readonly ICreditoService _creditoService;
        private readonly IVentaService _ventaService;
        private readonly IPrecioService _precioService;
        private readonly IClienteService _clienteService;
        private readonly ILogger<VentaApiController> _logger;

        public VentaApiController(
            IProductoService productoService,
            ICreditoService creditoService,
            IVentaService ventaService,
            IPrecioService precioService,
            IClienteService clienteService,
            ILogger<VentaApiController> logger)
        {
            _productoService = productoService;
            _creditoService = creditoService;
            _ventaService = ventaService;
            _precioService = precioService;
            _clienteService = clienteService;
            _logger = logger;
        }

        #region Clientes

        /// <summary>
        /// Busca clientes por nombre, apellido o número de documento.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> BuscarClientes(string term, int take = 15)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(term))
                    return Ok(new List<object>());

                var limite = Math.Clamp(take, 1, 50);

                var clientes = (await _clienteService.SearchAsync(
                        searchTerm: term.Trim(),
                        soloActivos: true,
                        orderBy: "nombre"))
                    .Take(limite)
                    .Select(c => new
                    {
                        id = c.Id,
                        nombre = c.Nombre,
                        apellido = c.Apellido,
                        tipoDocumento = c.TipoDocumento,
                        numeroDocumento = c.NumeroDocumento,
                        telefono = c.Telefono,
                        email = c.Email,
                        display = c.ToDisplayName()
                    });

                return Ok(clientes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al buscar clientes con término {Term}", term);
                return StatusCode(500, new { error = "Error al buscar clientes" });
            }
        }

        #endregion

        #region Productos

        [HttpGet]
        public async Task<IActionResult> GetPrecioProducto(int id)
        {
            try
            {
                var producto = await _productoService.GetByIdAsync(id);
                if (producto == null || !producto.Activo)
                {
                    return NotFound(new { error = "Producto no encontrado" });
                }

                decimal precioVenta = producto.PrecioVenta;

                var listaPredeterminada = await _precioService.GetListaPredeterminadaAsync();
                if (listaPredeterminada != null)
                {
                    var vigente = await _precioService.GetPrecioVigenteAsync(producto.Id, listaPredeterminada.Id);
                    if (vigente != null)
                        precioVenta = vigente.Precio;
                }

                return Ok(new
                {
                    precioVenta,
                    stockActual = producto.StockActual,
                    codigo = producto.Codigo,
                    nombre = producto.Nombre
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener precio del producto {Id}", id);
                return StatusCode(500, new { error = "No se pudo obtener el precio del producto" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> BuscarProductos(
            string term,
            int take = 20,
            int? categoriaId = null,
            int? marcaId = null,
            bool soloConStock = true,
            decimal? precioMin = null,
            decimal? precioMax = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(term))
                {
                    return Ok(new List<object>());
                }

                var limite = Math.Clamp(take, 1, 50);
                var termino = term.Trim();

                var productos = (await _productoService.SearchAsync(
                        searchTerm: termino,
                    categoriaId: categoriaId,
                    marcaId: marcaId,
                        soloActivos: true,
                        orderBy: "nombre"))
                    .ToList();

                var listaPredeterminada = await _precioService.GetListaPredeterminadaAsync();
                var preciosBatch = listaPredeterminada != null
                    ? await _precioService.GetPreciosVigentesBatchAsync(
                        productos.Select(p => p.Id), listaPredeterminada.Id)
                    : new Dictionary<int, ProductoPrecioLista>();
                var resultado = new List<object>(productos.Count);

                foreach (var producto in productos)
                {
                    var precioVenta = producto.PrecioVenta;
                    if (preciosBatch.TryGetValue(producto.Id, out var vigente))
                        precioVenta = vigente.Precio;

                    if (soloConStock && producto.StockActual <= 0)
                        continue;

                    if (precioMin.HasValue && precioVenta < precioMin.Value)
                        continue;

                    if (precioMax.HasValue && precioVenta > precioMax.Value)
                        continue;

                    var caracteristicas = producto.Caracteristicas?
                        .Where(c => !c.IsDeleted)
                        .Select(c => (object)new
                        {
                            nombre = c.Nombre,
                            valor = c.Valor
                        })
                        .ToList() ?? new List<object>();

                    var caracteristicasResumen = producto.Caracteristicas?
                        .Where(c => !c.IsDeleted)
                        .Take(3)
                        .Select(c => c.Valor)
                        .ToList() ?? new List<string>();

                    resultado.Add(new
                    {
                        id = producto.Id,
                        codigo = producto.Codigo,
                        nombre = producto.Nombre,
                        marca = producto.Marca?.Nombre,
                        submarca = producto.Submarca?.Nombre,
                        categoria = producto.Categoria?.Nombre,
                        subcategoria = producto.Subcategoria?.Nombre,
                        descripcion = producto.Descripcion,
                        stockActual = producto.StockActual,
                        precioVenta,
                        caracteristicas,
                        caracteristicasResumen = string.Join(" · ", caracteristicasResumen),
                        codigoExacto = string.Equals(producto.Codigo, termino, StringComparison.OrdinalIgnoreCase)
                    });
                }

                return Ok(resultado.Take(limite));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al buscar productos para venta con término {Term}", term);
                return StatusCode(500, new { error = "Error al buscar productos" });
            }
        }

        #endregion

        #region Crédito y cálculos

        [HttpGet]
        public async Task<IActionResult> GetCreditosCliente(int clienteId)
        {
            try
            {
                var creditos = (await _creditoService.GetByClienteIdAsync(clienteId))
                    .Where(c => (c.Estado == EstadoCredito.Activo || c.Estado == EstadoCredito.Aprobado)
                                && c.SaldoPendiente > 0)
                    .OrderByDescending(c => c.FechaAprobacion ?? DateTime.MinValue)
                    .Select(c => new
                    {
                        id = c.Id,
                        numero = c.Numero,
                        montoAprobado = c.MontoAprobado,
                        saldoPendiente = c.SaldoPendiente,
                        tasaInteres = c.TasaInteres,
                        detalle = $"{c.Numero} - Saldo disponible: ${c.SaldoPendiente:N2}"
                    })
                    .ToList();

                if (creditos.Count == 0)
                {
                    _logger.LogWarning("No se encontraron créditos disponibles para cliente {ClienteId}", clienteId);
                }

                return Ok(creditos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener créditos del cliente: {ClienteId}", clienteId);
                return StatusCode(500, new { error = "Error al obtener los créditos del cliente" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetInfoCredito(int creditoId)
        {
            try
            {
                _logger.LogInformation("Obteniendo información del crédito {CreditoId}", creditoId);

                var credito = await _creditoService.GetByIdAsync(creditoId);

                if (credito == null)
                {
                    return NotFound(new { error = "Crédito no encontrado" });
                }

                if (credito.Estado != EstadoCredito.Activo && credito.Estado != EstadoCredito.Aprobado)
                {
                    _logger.LogWarning(
                        "El crédito {Numero} existe pero está en estado {Estado}",
                        credito.Numero,
                        credito.Estado);
                    return NotFound(new { error = $"El crédito está en estado {credito.Estado} y no se puede usar" });
                }

                return Ok(new
                {
                    id = credito.Id,
                    numero = credito.Numero,
                    montoAprobado = credito.MontoAprobado,
                    saldoPendiente = credito.SaldoPendiente,
                    tasaInteres = credito.TasaInteres,
                    estado = credito.Estado
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener información del crédito: {CreditoId}", creditoId);
                return StatusCode(500, new { error = "Error al obtener información del crédito" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> CalcularCuotasTarjeta(int tarjetaId, decimal monto, int cuotas)
        {
            try
            {
                var resultado = await _ventaService.CalcularCuotasTarjetaAsync(tarjetaId, monto, cuotas);

                return Ok(new
                {
                    montoCuota = resultado.MontoCuota,
                    montoTotal = resultado.MontoTotalConInteres,
                    interes = resultado.MontoTotalConInteres - monto
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al calcular cuotas de tarjeta");
                return StatusCode(500, new { error = "Error al calcular las cuotas" });
            }
        }

        [HttpPost]
        public IActionResult CalcularTotalesVenta([FromBody] CalcularTotalesVentaRequest request)
        {
            try
            {
                if (!ModelState.IsValid || request.Detalles.Count == 0)
                {
                    return BadRequest(new { error = "Debe especificar al menos un detalle para calcular los totales" });
                }

                var totales = _ventaService.CalcularTotalesPreview(
                    request.Detalles,
                    request.DescuentoGeneral,
                    request.DescuentoEsPorcentaje);

                return Ok(totales);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al calcular totales de venta desde el backend");
                return StatusCode(500, new { error = "No se pudieron calcular los totales" });
            }
        }

        #endregion
    }
}
