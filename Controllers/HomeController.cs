using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Models;

namespace TheBuryProject.Controllers
{
    // Explícito: el FallbackPolicy global de Program.cs exige autenticación por defecto.
    // Este controller debe seguir siendo público (landing, Error usado por
    // UseExceptionHandler incluso antes de loguearse, y Privacy).
    [AllowAnonymous]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }
        public IActionResult Index()
        {
            return RedirectToAction("Index", "Dashboard");
        }
        public IActionResult Privacy()
        {
            return View();
        }

        // Destino de UseStatusCodePagesWithReExecute: conserva el código original de la respuesta.
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Estado(int code)
        {
            Response.StatusCode = code is >= 400 and <= 599 ? code : 404;
            if (code != 404)
            {
                return new EmptyResult();
            }
            return View("NoEncontrado");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
