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
[PermisoRequerido(Modulo = "cotizaciones", Accion = "view")]
public sealed class CotizacionApiController : ControllerBase
{
    private readonly ICotizacionPagoCalculator _calculator;
    private readonly ICotizacionService _cotizacionService;
    private readonly ILogger<CotizacionApiController> _logger;

    public CotizacionApiController(
        ICotizacionPagoCalculator calculator,
        ICotizacionService cotizacionService,
        ILogger<CotizacionApiController> logger)
    {
        _calculator = calculator;
        _cotizacionService = cotizacionService;
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

    [HttpPost("guardar")]
    [PermisoRequerido(Modulo = "cotizaciones", Accion = "create")]
    public async Task<IActionResult> Guardar(
        [FromBody] CotizacionCrearRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(new { error = "El request de guardado de cotizacion es obligatorio." });

        try
        {
            var usuario = User?.Identity?.Name ?? "System";
            var resultado = await _cotizacionService.CrearAsync(request, usuario, cancellationToken);
            return Ok(new
            {
                resultado.Id,
                resultado.Numero,
                detalleUrl = Url.Action("Detalles", "Cotizacion", new { id = resultado.Id })
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar cotizacion");
            return StatusCode(500, new { error = "No se pudo guardar la cotizacion." });
        }
    }
}
