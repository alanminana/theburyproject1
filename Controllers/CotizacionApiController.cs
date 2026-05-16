using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Controllers;

/// <summary>
/// API de cotizaciones: simulación, persistencia y conversión a venta.
/// </summary>
[Authorize]
[ApiController]
[Route("api/cotizacion")]
[PermisoRequerido(Modulo = "cotizaciones", Accion = "view")]
public sealed class CotizacionApiController : ControllerBase
{
    private readonly ICotizacionPagoCalculator _calculator;
    private readonly ICotizacionService _cotizacionService;
    private readonly ICotizacionConversionService _conversionService;
    private readonly ILogger<CotizacionApiController> _logger;

    public CotizacionApiController(
        ICotizacionPagoCalculator calculator,
        ICotizacionService cotizacionService,
        ICotizacionConversionService conversionService,
        ILogger<CotizacionApiController> logger)
    {
        _calculator = calculator;
        _cotizacionService = cotizacionService;
        _conversionService = conversionService;
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

    [HttpPost("{id:int}/conversion/preview")]
    [PermisoRequerido(Modulo = "cotizaciones", Accion = "convert")]
    public async Task<IActionResult> ConversionPreview(
        [FromRoute] int id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resultado = await _conversionService.PreviewConversionAsync(id, cancellationToken);
            return Ok(resultado);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al calcular preview de conversion para cotizacion {Id}", id);
            return StatusCode(500, new { error = "No se pudo calcular el preview de conversión." });
        }
    }

    [HttpPost("{id:int}/cancelar")]
    [PermisoRequerido(Modulo = "cotizaciones", Accion = "cancel")]
    public async Task<IActionResult> Cancelar(
        [FromRoute] int id,
        [FromBody] CotizacionCancelacionRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(new { error = "El request de cancelación es obligatorio." });

        try
        {
            var usuario = User?.Identity?.Name ?? "System";
            var resultado = await _cotizacionService.CancelarAsync(id, request, usuario, cancellationToken);

            if (!resultado.Exitoso)
                return BadRequest(new { errores = resultado.Errores });

            return Ok(resultado);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cancelar cotización {Id}", id);
            return StatusCode(500, new { error = "No se pudo cancelar la cotización." });
        }
    }

    [HttpPost("vencer-expiradas")]
    [PermisoRequerido(Modulo = "cotizaciones", Accion = "expire")]
    public async Task<IActionResult> VencerExpiradas(CancellationToken cancellationToken = default)
    {
        try
        {
            var usuario = User?.Identity?.Name ?? "System";
            var resultado = await _cotizacionService.VencerEmitidasAsync(DateTime.UtcNow, usuario, cancellationToken);

            if (!resultado.Exitoso)
                return StatusCode(500, new { errores = resultado.Errores });

            return Ok(resultado);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al vencer cotizaciones expiradas");
            return StatusCode(500, new { error = "No se pudo ejecutar el vencimiento de cotizaciones." });
        }
    }

    [HttpPost("{id:int}/conversion/convertir")]
    [PermisoRequerido(Modulo = "cotizaciones", Accion = "convert")]
    public async Task<IActionResult> Convertir(
        [FromRoute] int id,
        [FromBody] CotizacionConversionRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(new { error = "El request de conversión es obligatorio." });

        try
        {
            var usuario = User?.Identity?.Name ?? "System";
            var resultado = await _conversionService.ConvertirAVentaAsync(id, request, usuario, cancellationToken);

            if (!resultado.Exitoso)
                return BadRequest(new { errores = resultado.Errores, advertencias = resultado.Advertencias });

            return Ok(resultado);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al convertir cotizacion {Id} a venta", id);
            return StatusCode(500, new { error = "No se pudo convertir la cotización." });
        }
    }
}
