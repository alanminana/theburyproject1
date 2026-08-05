using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TheBuryProject.Filters;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// PUN-ML10-G: operaciones administrativas de mantenimiento de datos, de máximo privilegio.
    /// Ningún método corre automáticamente (ni al iniciar la app, ni desde una migración) — sólo
    /// vía este endpoint HTTP explícito, autenticado y con permiso dedicado.
    /// </summary>
    [PermisoRequerido(Modulo = "configuracion", Accion = "recalcularscoringglobal")]
    public class MantenimientoController : Controller
    {
        private readonly IClienteScoringService _scoringService;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<MantenimientoController> _logger;

        public MantenimientoController(
            IClienteScoringService scoringService,
            ICurrentUserService currentUser,
            ILogger<MantenimientoController> logger)
        {
            _scoringService = scoringService;
            _currentUser = currentUser;
            _logger = logger;
        }

        // GET: Mantenimiento/ScoringGlobal
        [HttpGet]
        public IActionResult ScoringGlobal()
        {
            return View("ScoringGlobal_tw");
        }

        // POST: Mantenimiento/RecalcularScoringGlobal
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecalcularScoringGlobal(bool preview, CancellationToken cancellationToken)
        {
            try
            {
                var opciones = new RecalculoGlobalScoringOpciones
                {
                    Preview = preview,
                    Origen = preview ? "RecalculoGlobalPreview" : "RecalculoGlobalManual",
                    Observacion = preview
                        ? "Vista previa de recálculo global (nada persistido)"
                        : "Recálculo global manual desde Mantenimiento",
                    RegistradoPor = _currentUser.GetUsername(),
                    PoliticaErrores = PoliticaErroresRecalculoGlobal.ContinuarTrasFallos
                };

                var resultado = await _scoringService.RecalcularTodosAsync(opciones, cancellationToken);

                TempData["Success"] = preview
                    ? $"Vista previa: {resultado.Examinados} examinados, {resultado.Recalculados} cambiarían, {resultado.SinCambios} sin cambios, {resultado.Fallidos} fallidos."
                    : $"Recálculo global completado: {resultado.Examinados} examinados, {resultado.Recalculados} recalculados, {resultado.SinCambios} sin cambios, {resultado.Fallidos} fallidos.";

                if (resultado.Interrumpido)
                    TempData["Success"] += " (Interrumpido antes de examinar a todos los elegibles.)";

                TempData["RecalculoGlobalIdsFallidos"] = string.Join(", ", resultado.IdsFallidos);

                return RedirectToAction(nameof(ScoringGlobal));
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar el recálculo global de scoring (preview={Preview})", preview);
                TempData["Error"] = "Error al ejecutar el recálculo global de scoring.";
                return RedirectToAction(nameof(ScoringGlobal));
            }
        }
    }
}
