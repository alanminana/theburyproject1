using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Controllers;

/// <summary>
/// API read-only para simulacion de cotizaciones no persistidas.
/// </summary>
[Authorize]
[ApiController]
[Route("api/cotizacion")]
[PermisoRequerido(Modulo = "ventas", Accion = "view")]
public sealed class CotizacionApiController : ControllerBase
{
    private readonly ICotizacionPagoCalculator _calculator;
    private readonly ILogger<CotizacionApiController> _logger;

    public CotizacionApiController(
        ICotizacionPagoCalculator calculator,
        ILogger<CotizacionApiController> logger)
    {
        _calculator = calculator;
        _logger = logger;
    }

    [HttpPost("simular")]
    public async Task<IActionResult> Simular(
        [FromBody] CotizacionSimulacionRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(new { error = "El request de cotizacion es obligatorio." });

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var resultado = await _calculator.SimularAsync(request, cancellationToken);
            return Ok(resultado);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al simular cotizacion read-only");
            return StatusCode(500, new { error = "No se pudo simular la cotizacion." });
        }
    }
}
