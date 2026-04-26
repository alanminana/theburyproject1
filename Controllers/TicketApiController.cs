using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Controllers;

/// <summary>
/// API REST para gestión de tickets internos del ERP.
/// Preparado para integración global desde cualquier módulo/vista.
/// </summary>
[Authorize]
[ApiController]
[Route("api/tickets")]
[PermisoRequerido(Modulo = TicketConstants.Modulo, Accion = TicketConstants.Acciones.Ver)]
public class TicketApiController : ControllerBase
{
    private readonly ITicketService _ticketService;
    private readonly ILogger<TicketApiController> _logger;

    public TicketApiController(ITicketService ticketService, ILogger<TicketApiController> logger)
    {
        _ticketService = ticketService;
        _logger = logger;
    }

    // ── Listado ────────────────────────────────────────────────────────────

    /// <summary>Lista tickets con filtros y paginación.</summary>
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] TicketFilterViewModel filtro)
    {
        var resultado = await _ticketService.ListarAsync(filtro);
        return Ok(resultado);
    }

    /// <summary>Detalle completo de un ticket (adjuntos + checklist incluidos).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detalle(int id)
    {
        var detalle = await _ticketService.ObtenerDetalleAsync(id);
        if (detalle is null) return NotFound();
        return Ok(detalle);
    }

    // ── Ciclo de vida ──────────────────────────────────────────────────────

    /// <summary>Crea un nuevo ticket.</summary>
    [HttpPost]
    [PermisoRequerido(Modulo = TicketConstants.Modulo, Accion = TicketConstants.Acciones.Crear)]
    public async Task<IActionResult> Crear([FromBody] CreateTicketRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var ticket = await _ticketService.CrearAsync(request);
            return CreatedAtAction(nameof(Detalle), new { id = ticket.Id }, new { ticket.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear ticket.");
            return StatusCode(500, new { error = "Error interno al crear el ticket." });
        }
    }

    /// <summary>Actualiza título, descripción y tipo de un ticket editable.</summary>
    [HttpPut("{id:int}")]
    [PermisoRequerido(Modulo = TicketConstants.Modulo, Accion = TicketConstants.Acciones.Editar)]
    public async Task<IActionResult> Actualizar(int id, [FromBody] UpdateTicketRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            await _ticketService.ActualizarAsync(id, request);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    /// <summary>Cambia el estado de un ticket con validación de transición.</summary>
    [HttpPatch("{id:int}/estado")]
    [PermisoRequerido(Modulo = TicketConstants.Modulo, Accion = TicketConstants.Acciones.CambiarEstado)]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] UpdateTicketStatusRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            await _ticketService.CambiarEstadoAsync(id, request);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    /// <summary>Registra la resolución y cierra el ticket como Resuelto.</summary>
    [HttpPatch("{id:int}/resolucion")]
    [PermisoRequerido(Modulo = TicketConstants.Modulo, Accion = TicketConstants.Acciones.Resolver)]
    public async Task<IActionResult> RegistrarResolucion(int id, [FromBody] UpdateTicketResolutionRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            await _ticketService.RegistrarResolucionAsync(id, request);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    // ── Checklist ──────────────────────────────────────────────────────────

    /// <summary>Agrega un ítem al checklist de un ticket.</summary>
    [HttpPost("{id:int}/checklist")]
    [PermisoRequerido(Modulo = TicketConstants.Modulo, Accion = TicketConstants.Acciones.Editar)]
    public async Task<IActionResult> AgregarChecklistItem(int id, [FromBody] AgregarChecklistItemRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var item = await _ticketService.AgregarItemChecklistAsync(id, request.Descripcion, request.Orden);
            return Ok(new { item.Id });
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>Marca o desmarca un ítem del checklist.</summary>
    [HttpPatch("checklist/{itemId:int}")]
    [PermisoRequerido(Modulo = TicketConstants.Modulo, Accion = TicketConstants.Acciones.Editar)]
    public async Task<IActionResult> MarcarChecklistItem(int itemId, [FromBody] MarcarChecklistItemRequest request)
    {
        try
        {
            await _ticketService.MarcarItemChecklistAsync(itemId, request.Completado);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>Elimina (soft delete) un ítem del checklist.</summary>
    [HttpDelete("checklist/{itemId:int}")]
    [PermisoRequerido(Modulo = TicketConstants.Modulo, Accion = TicketConstants.Acciones.Editar)]
    public async Task<IActionResult> EliminarChecklistItem(int itemId)
    {
        try
        {
            await _ticketService.EliminarItemChecklistAsync(itemId);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    // ── Adjuntos ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sube un archivo adjunto a un ticket.
    /// Responde con { id, nombreArchivo, rutaRelativa } — usar rutaRelativa para construir la URL de descarga.
    /// </summary>
    [HttpPost("{id:int}/adjuntos")]
    [Consumes("multipart/form-data")]
    [PermisoRequerido(Modulo = TicketConstants.Modulo, Accion = TicketConstants.Acciones.Editar)]
    public async Task<IActionResult> SubirAdjunto(int id, [FromForm] IFormFile archivo)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { error = "No se recibió ningún archivo." });

        if (archivo.Length > TicketConstants.MaxAdjuntoBytes)
            return BadRequest(new { error = $"El archivo supera el límite de {TicketConstants.MaxAdjuntoBytes / 1024 / 1024} MB." });

        var ext = Path.GetExtension(archivo.FileName);
        if (!TicketConstants.ExtensionesPermitidas.Contains(ext))
            return BadRequest(new { error = $"Extensión '{ext}' no permitida." });

        try
        {
            var adjunto = await _ticketService.SubirAdjuntoAsync(id, archivo);
            return Ok(new { adjunto.Id, adjunto.NombreArchivo, rutaRelativa = adjunto.RutaArchivo });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al subir adjunto para ticket #{Id}.", id);
            return StatusCode(500, new { error = "Error interno al subir el archivo." });
        }
    }

    /// <summary>Elimina (soft delete) un adjunto y su archivo físico.</summary>
    [HttpDelete("adjuntos/{adjuntoId:int}")]
    [PermisoRequerido(Modulo = TicketConstants.Modulo, Accion = TicketConstants.Acciones.Editar)]
    public async Task<IActionResult> EliminarAdjunto(int adjuntoId)
    {
        try
        {
            await _ticketService.EliminarAdjuntoAsync(adjuntoId);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
