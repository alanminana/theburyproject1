using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "stock", Accion = "viewalerts")]
    public class AlertaStockController : Controller
    {
        private readonly IAlertaStockService _alertaStockService;
        private readonly IProductoService _productoService;
        private readonly ILogger<AlertaStockController> _logger;
        private readonly ICurrentUserService _currentUser;

        public AlertaStockController(
            IAlertaStockService alertaStockService,
            IProductoService productoService,
            ILogger<AlertaStockController> logger,
            ICurrentUserService currentUser)
        {
            _alertaStockService = alertaStockService;
            _productoService = productoService;
            _logger = logger;
            _currentUser = currentUser;
        }

        #region Vistas — Index / Pendientes / Críticos / Detalle / Estadísticas / PorProducto

        // GET: AlertaStock
        public async Task<IActionResult> Index(AlertaStockFiltroViewModel filtro)
        {
            try
            {
                // Por defecto mostrar solo pendientes
                if (!filtro.Estado.HasValue)
                {
                    filtro.Estado = EstadoAlerta.Pendiente;
                }

                var resultado = await _alertaStockService.BuscarAsync(filtro);

                ViewBag.Filtro = filtro;
                ViewBag.TiposAlerta = Enum.GetValues<TipoAlertaStock>();
                ViewBag.Prioridades = Enum.GetValues<PrioridadAlerta>();
                ViewBag.Estados = Enum.GetValues<EstadoAlerta>();

                return View("Index_tw", resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al listar alertas de stock");
                TempData["Error"] = "Error al cargar las alertas de stock";
                return View("Index_tw", new PaginatedResult<AlertaStockViewModel>());
            }
        }

        // GET: AlertaStock/Pendientes
        public async Task<IActionResult> Pendientes()
        {
            try
            {
                // No hay vista dedicada; Index ya soporta filtro por estado
                return RedirectToAction(nameof(Index), new { Estado = (int)EstadoAlerta.Pendiente });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener alertas pendientes");
                TempData["Error"] = "Error al cargar las alertas pendientes";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: AlertaStock/Criticos
        public async Task<IActionResult> Criticos()
        {
            try
            {
                var productosCriticos = await _alertaStockService.GetProductosCriticosAsync();
                return View("Criticos_tw", productosCriticos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener productos críticos");
                TempData["Error"] = "Error al cargar los productos críticos";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: AlertaStock/Details/5
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var alerta = await _alertaStockService.GetByIdAsync(id);
                if (alerta == null)
                {
                    TempData["Error"] = "Alerta no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                return View("Details_tw", alerta);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener detalle de alerta {AlertaId}", id);
                TempData["Error"] = "Error al cargar los detalles de la alerta";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: AlertaStock/Estadisticas
        public async Task<IActionResult> Estadisticas()
        {
            try
            {
                var estadisticas = await _alertaStockService.GetEstadisticasAsync();
                return View("Estadisticas_tw", estadisticas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener estadísticas de alertas");
                TempData["Error"] = "Error al cargar las estadísticas";
                return View("Estadisticas_tw", new AlertaStockEstadisticasViewModel());
            }
        }

        #endregion

        #region Acciones — Resolver / Ignorar / GenerarAlertas

        // POST: AlertaStock/Resolver/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Resolver(int id, string? observaciones, byte[]? rowVersion, string? returnUrl)
        {
            return await ProcesarAccionAlerta(
                id,
                observaciones,
            rowVersion,
                _alertaStockService.ResolverAlertaAsync,
                "La alerta se resolvió exitosamente",
                "No se pudo resolver la alerta",
                "resolver",
                returnUrl);
        }

        // POST: AlertaStock/Ignorar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ignorar(int id, string? observaciones, byte[]? rowVersion, string? returnUrl)
        {
            return await ProcesarAccionAlerta(
                id,
                observaciones,
            rowVersion,
                _alertaStockService.IgnorarAlertaAsync,
                "La alerta se ignoró exitosamente",
                "No se pudo ignorar la alerta",
                "ignorar",
                returnUrl);
        }

        // POST: AlertaStock/GenerarAlertas
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerarAlertas()
        {
            try
            {
                var alertasCreadas = await _alertaStockService.GenerarAlertasStockBajoAsync();

                if (alertasCreadas > 0)
                {
                    TempData["Success"] = $"Se generaron {alertasCreadas} nuevas alertas de stock";
                }
                else
                {
                    TempData["Info"] = "No se generaron nuevas alertas. Todos los productos tienen stock adecuado.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar alertas manualmente");
                TempData["Error"] = "Error al generar alertas de stock";
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: AlertaStock/PorProducto/5
        public async Task<IActionResult> PorProducto(int id)
        {
            try
            {
                var producto = await _productoService.GetByIdAsync(id);
                if (producto == null)
                {
                    TempData["Error"] = "Producto no encontrado";
                    return RedirectToAction("Index", "Producto");
                }

                var alertas = await _alertaStockService.GetAlertasByProductoIdAsync(id);

                ViewBag.Producto = producto;
                return View(alertas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener alertas del producto {ProductoId}", id);
                TempData["Error"] = "Error al cargar las alertas del producto";
                return RedirectToAction("Index", "Producto");
            }
        }

        #endregion

        #region Métodos auxiliares

        private async Task<IActionResult> ProcesarAccionAlerta(
            int id,
            string? observaciones,
            byte[]? rowVersion,
            Func<int, string, string?, byte[]?, Task<bool>> accion,
            string mensajeExito,
            string mensajeError,
            string accionLog,
            string? returnUrl)
        {
            try
            {
                var exito = await accion(id, _currentUser.GetUsername(), observaciones, rowVersion);

                TempData[exito ? "Success" : "Error"] = exito ? mensajeExito : mensajeError;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al {Accion} alerta {AlertaId}", accionLog, id);
                TempData["Error"] = $"Error al {accionLog} la alerta: {ex.Message}";
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(Index));
        }

        #endregion
    }
}