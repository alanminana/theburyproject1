using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "dashboard", Accion = "view")]
    public class DashboardController : Controller
    {
        private readonly IDashboardService _dashboardService;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            IDashboardService dashboardService,
            ILogger<DashboardController> logger)
        {
            _dashboardService = dashboardService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var dashboard = await _dashboardService.GetDashboardDataAsync();
                return View("Index", dashboard);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar el dashboard");
                // La vista muestra su propio panel de error; el modelo vacío no debe leerse como "Todo al día".
                ViewData["CargaFallida"] = true;
                return View("Index", new ViewModels.DashboardViewModel());
            }
        }
    }
}
