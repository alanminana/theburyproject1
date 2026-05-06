using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels.Requests;
using TheBuryProject.ViewModels;

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
        private readonly IClienteService _clienteService;
        private readonly IConfiguracionPagoService _configuracionPagoService;
        private readonly IValidacionVentaService _validacionVentaService;
        private readonly ILogger<VentaApiController> _logger;

        public VentaApiController(
            IProductoService productoService,
            ICreditoService creditoService,
            IVentaService ventaService,
            IClienteService clienteService,
            IConfiguracionPagoService configuracionPagoService,
            IValidacionVentaService validacionVentaService,
            ILogger<VentaApiController> logger)
        {
            _productoService = productoService;
            _creditoService = creditoService;
            _ventaService = ventaService;
            _clienteService = clienteService;
            _configuracionPagoService = configuracionPagoService;
            _validacionVentaService = validacionVentaService;
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
                if (id <= 0)
                    return BadRequest(new { error = "El identificador de producto debe ser válido" });

                var precioProducto = await _productoService.ObtenerPrecioVigenteParaVentaAsync(id);
                if (precioProducto == null)
                {
                    return NotFound(new { error = "Producto no encontrado" });
                }

                return Ok(new
                {
                    precioVenta = precioProducto.PrecioVenta,
                    stockActual = precioProducto.StockActual,
                    codigo = precioProducto.Codigo,
                    nombre = precioProducto.Nombre
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
                    return Ok(new List<object>());

                var resultado = await _productoService.BuscarParaVentaAsync(
                    term, take, categoriaId, marcaId, soloConStock, precioMin, precioMax);

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al buscar productos para venta con término {Term}", term);
                return StatusCode(500, new { error = "Error al buscar productos" });
            }
        }

        #endregion

        #region Crédito y cálculos

        /// <summary>
        /// Endpoint legacy conservado por compatibilidad. No tiene caller UI/JS/Razor visible
        /// en el flujo actual, que usa PrevalidarCredito. No eliminar sin revisar logs
        /// productivos y consumidores externos.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetCreditosCliente(int clienteId)
        {
            try
            {
                if (clienteId <= 0)
                    return BadRequest(new { error = "El identificador de cliente debe ser válido" });

                var creditos = (await _creditoService.GetCreditosDisponiblesParaVentaAsync(clienteId))
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

        /// <summary>
        /// Endpoint legacy conservado por compatibilidad. No tiene caller UI/JS/Razor visible
        /// en el flujo actual, que usa PrevalidarCredito. No eliminar sin revisar logs
        /// productivos y consumidores externos.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetInfoCredito(int creditoId)
        {
            try
            {
                if (creditoId <= 0)
                    return BadRequest(new { error = "El identificador de crédito debe ser válido" });

                _logger.LogInformation("Obteniendo información del crédito {CreditoId}", creditoId);

                var credito = await _creditoService.GetCreditoParaVentaAsync(creditoId);

                if (credito == null)
                {
                    return NotFound(new { error = "Crédito no encontrado" });
                }

                if (!credito.Disponible)
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
                if (tarjetaId <= 0 || monto <= 0 || cuotas <= 0)
                {
                    return BadRequest(new { error = "Los parámetros para calcular cuotas deben ser válidos" });
                }

                var resultado = await _ventaService.CalcularCuotasTarjetaAsync(tarjetaId, monto, cuotas);

                return Ok(new
                {
                    montoCuota = resultado.MontoCuota,
                    montoTotal = resultado.MontoTotalConInteres,
                    interes = resultado.MontoTotalConInteres - monto
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Configuración de tarjeta no disponible para calcular cuotas: {TarjetaId}", tarjetaId);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al calcular cuotas de tarjeta");
                return StatusCode(500, new { error = "Error al calcular las cuotas" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CalcularTotalesVenta([FromBody] CalcularTotalesVentaRequest request)
        {
            try
            {
                if (request == null || !ModelState.IsValid || request.Detalles.Count == 0)
                {
                    return BadRequest(new { error = "Debe especificar al menos un detalle para calcular los totales" });
                }

                var totales = await _ventaService.CalcularTotalesPreviewAsync(
                    request.Detalles,
                    request.DescuentoGeneral,
                    request.DescuentoEsPorcentaje);

                if (request.TarjetaId.HasValue)
                {
                    var productoIds = request.Detalles.Select(d => d.ProductoId);
                    var maxResult = await _configuracionPagoService.ObtenerMaxCuotasSinInteresEfectivoAsync(
                        request.TarjetaId.Value, productoIds);
                    if (maxResult != null)
                    {
                        totales.MaxCuotasSinInteresEfectivo = maxResult.MaxCuotas;
                        totales.CuotasSinInteresLimitadasPorProducto = maxResult.LimitadoPorProducto;
                    }

                    var tarjeta = await _configuracionPagoService.GetTarjetaByIdAsync(request.TarjetaId.Value);
                    if (tarjeta is
                        {
                            Activa: true,
                            TipoTarjeta: TipoTarjeta.Debito,
                            TieneRecargoDebito: true,
                            PorcentajeRecargoDebito: > 0m
                        })
                    {
                        var porcentaje = tarjeta.PorcentajeRecargoDebito.Value;
                        var recargo = RedondearMoneda(totales.Total * (porcentaje / 100m));

                        totales.PorcentajeRecargoDebitoAplicado = porcentaje;
                        totales.RecargoDebitoAplicado = recargo;
                        totales.TotalConRecargoDebito = totales.Total + recargo;
                    }
                }

                return Ok(totales);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al calcular totales de venta desde el backend");
                return StatusCode(500, new { error = "No se pudieron calcular los totales" });
            }
        }

        #endregion

        #region Tarjetas y prevalidación

        [HttpGet]
        public async Task<IActionResult> GetTarjetasActivas()
        {
            try
            {
                var tarjetas = await _configuracionPagoService.GetTarjetasActivasParaVentaAsync();

                var resultado = tarjetas.Select(t => new
                {
                    id = t.Id,
                    nombre = t.Nombre,
                    tipo = t.Tipo,
                    permiteCuotas = t.PermiteCuotas,
                    cantidadMaximaCuotas = t.CantidadMaximaCuotas,
                    tipoCuota = t.TipoCuota,
                    tasaInteres = t.TasaInteres,
                    tieneRecargo = t.TieneRecargo,
                    porcentajeRecargo = t.PorcentajeRecargo
                });

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener tarjetas activas");
                return StatusCode(500, new { error = "Error al obtener las tarjetas" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> PrevalidarCredito(int clienteId, decimal monto)
        {
            try
            {
                if (clienteId <= 0)
                    return BadRequest(new { error = "Debe seleccionar un cliente válido" });

                if (monto <= 0)
                    return BadRequest(new { error = "El monto debe ser mayor a cero" });

                var resultado = await _validacionVentaService.PrevalidarAsync(clienteId, monto);
                return Ok(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al prevalidar crédito para cliente {ClienteId}", clienteId);
                return StatusCode(500, new { error = "Error interno al validar aptitud crediticia" });
            }
        }

        #endregion

        private static decimal RedondearMoneda(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
