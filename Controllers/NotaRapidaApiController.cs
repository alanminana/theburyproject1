using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Controllers;

/// <summary>
/// Notas rápidas personales del usuario autenticado: texto libre o checklist con ítems.
/// Cada usuario solo ve y modifica las suyas; no requiere permiso granular.
/// </summary>
[Authorize]
[ApiController]
[AutoValidateAntiforgeryToken]
[Route("api/notas-rapidas")]
public class NotaRapidaApiController : ControllerBase
{
    private const int MaxNotas = 200;
    private const int MaxItemsPorChecklist = 100;
    private const int MaxTexto = 1000;
    private const int MaxItemTexto = 500;

    private readonly AppDbContext _context;

    public NotaRapidaApiController(AppDbContext context)
    {
        _context = context;
    }

    public sealed record ItemDto(int Id, string Texto, bool Completado);
    public sealed record NotaDto(int Id, string Tipo, string Texto, DateTime Fecha, IReadOnlyList<ItemDto> Items);
    public sealed record CrearRequest(string? Tipo, string? Texto);
    public sealed record TextoRequest(string? Texto);
    public sealed record CompletadoRequest(bool Completado);

    private string? UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static NotaDto ToDto(NotaRapida n) => new(
        n.Id,
        n.Tipo == NotaRapidaTipo.Checklist ? "checklist" : "texto",
        n.Texto,
        n.CreatedAt,
        n.Items.Where(i => !i.IsDeleted).OrderBy(i => i.Id).Select(i => new ItemDto(i.Id, i.Texto, i.Completado)).ToList());

    private Task<NotaRapida?> BuscarNota(string uid, int id, bool conItems = false)
    {
        var q = _context.NotasRapidas.AsQueryable();
        if (conItems) q = q.Include(n => n.Items);
        return q.FirstOrDefaultAsync(n => n.Id == id && n.UsuarioId == uid);
    }

    private static string? Validar(string? texto, int max, string vacio, out string limpio)
    {
        limpio = texto?.Trim() ?? string.Empty;
        if (limpio.Length == 0) return vacio;
        if (limpio.Length > max) return $"El texto supera los {max} caracteres.";
        return null;
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var uid = UsuarioId;
        if (uid is null) return Unauthorized();

        var notas = await _context.NotasRapidas
            .AsNoTracking()
            .Include(n => n.Items)
            .Where(n => n.UsuarioId == uid)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        return Ok(notas.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearRequest request)
    {
        var uid = UsuarioId;
        if (uid is null) return Unauthorized();

        var error = Validar(request.Texto, MaxTexto, "Escribí el texto de la nota.", out var texto);
        if (error is not null) return BadRequest(new { error });

        var tipo = string.Equals(request.Tipo, "checklist", StringComparison.OrdinalIgnoreCase)
            ? NotaRapidaTipo.Checklist
            : NotaRapidaTipo.Texto;

        if (await _context.NotasRapidas.CountAsync(n => n.UsuarioId == uid) >= MaxNotas)
            return BadRequest(new { error = $"Llegaste al máximo de {MaxNotas} notas. Eliminá alguna para agregar otra." });

        var nota = new NotaRapida { UsuarioId = uid, Tipo = tipo, Texto = texto, CreatedBy = User.Identity?.Name };
        _context.NotasRapidas.Add(nota);
        await _context.SaveChangesAsync();
        return Ok(ToDto(nota));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Editar(int id, [FromBody] TextoRequest request)
    {
        var uid = UsuarioId;
        if (uid is null) return Unauthorized();

        var error = Validar(request.Texto, MaxTexto, "El texto no puede quedar vacío.", out var texto);
        if (error is not null) return BadRequest(new { error });

        var nota = await BuscarNota(uid, id, conItems: true);
        if (nota is null) return NotFound();

        nota.Texto = texto;
        nota.UpdatedAt = DateTime.UtcNow;
        nota.UpdatedBy = User.Identity?.Name;
        await _context.SaveChangesAsync();
        return Ok(ToDto(nota));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id)
    {
        var uid = UsuarioId;
        if (uid is null) return Unauthorized();

        var nota = await BuscarNota(uid, id, conItems: true);
        if (nota is null) return NotFound();

        var ahora = DateTime.UtcNow;
        nota.IsDeleted = true;
        nota.UpdatedAt = ahora;
        nota.UpdatedBy = User.Identity?.Name;
        foreach (var item in nota.Items) item.IsDeleted = true;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ── Ítems de checklist ─────────────────────────────────────────────────

    [HttpPost("{id:int}/items")]
    public async Task<IActionResult> AgregarItem(int id, [FromBody] TextoRequest request)
    {
        var uid = UsuarioId;
        if (uid is null) return Unauthorized();

        var error = Validar(request.Texto, MaxItemTexto, "Escribí el texto del ítem.", out var texto);
        if (error is not null) return BadRequest(new { error });

        var nota = await BuscarNota(uid, id, conItems: true);
        if (nota is null) return NotFound();
        if (nota.Tipo != NotaRapidaTipo.Checklist)
            return BadRequest(new { error = "Solo los checklists admiten ítems." });
        if (nota.Items.Count >= MaxItemsPorChecklist)
            return BadRequest(new { error = $"Un checklist admite hasta {MaxItemsPorChecklist} ítems." });

        nota.Items.Add(new NotaRapidaItem { Texto = texto, CreatedBy = User.Identity?.Name });
        await _context.SaveChangesAsync();
        return Ok(ToDto(nota));
    }

    [HttpPatch("{id:int}/items/{itemId:int}")]
    public async Task<IActionResult> CompletarItem(int id, int itemId, [FromBody] CompletadoRequest request)
    {
        var uid = UsuarioId;
        if (uid is null) return Unauthorized();

        var nota = await BuscarNota(uid, id, conItems: true);
        var item = nota?.Items.FirstOrDefault(i => i.Id == itemId);
        if (nota is null || item is null) return NotFound();

        item.Completado = request.Completado;
        item.FechaCompletado = request.Completado ? DateTime.UtcNow : null;
        item.UpdatedAt = DateTime.UtcNow;
        item.UpdatedBy = User.Identity?.Name;
        await _context.SaveChangesAsync();
        return Ok(ToDto(nota));
    }

    [HttpDelete("{id:int}/items/{itemId:int}")]
    public async Task<IActionResult> EliminarItem(int id, int itemId)
    {
        var uid = UsuarioId;
        if (uid is null) return Unauthorized();

        var nota = await BuscarNota(uid, id, conItems: true);
        var item = nota?.Items.FirstOrDefault(i => i.Id == itemId);
        if (nota is null || item is null) return NotFound();

        item.IsDeleted = true;
        item.UpdatedAt = DateTime.UtcNow;
        item.UpdatedBy = User.Identity?.Name;
        await _context.SaveChangesAsync();
        return Ok(ToDto(nota));
    }
}
