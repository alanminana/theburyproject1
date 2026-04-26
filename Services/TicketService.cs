using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Base;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Services;

/// <summary>
/// Implementación del servicio de tickets internos del ERP.
/// </summary>
public class TicketService : ITicketService
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly IFileStorageService _fileStorage;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<TicketService> _logger;

    public TicketService(
        AppDbContext context,
        IMapper mapper,
        IFileStorageService fileStorage,
        ICurrentUserService currentUser,
        ILogger<TicketService> logger)
    {
        _context = context;
        _mapper = mapper;
        _fileStorage = fileStorage;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ── Consultas ──────────────────────────────────────────────────────────

    public async Task<PageResult<TicketViewModel>> ListarAsync(TicketFilterViewModel filtro)
    {
        var query = _context.Tickets
            .AsNoTracking()
            .Where(t => !t.IsDeleted);

        if (filtro.Estado.HasValue)
            query = query.Where(t => t.Estado == filtro.Estado.Value);

        if (filtro.Tipo.HasValue)
            query = query.Where(t => t.Tipo == filtro.Tipo.Value);

        if (!string.IsNullOrWhiteSpace(filtro.ModuloOrigen))
            query = query.Where(t => t.ModuloOrigen == filtro.ModuloOrigen);

        if (filtro.FechaDesde.HasValue)
        {
            var fechaDesde = filtro.FechaDesde.Value.Date;
            query = query.Where(t => t.CreatedAt >= fechaDesde);
        }

        if (filtro.FechaHasta.HasValue)
        {
            var fechaHastaExclusiva = filtro.FechaHasta.Value.Date.AddDays(1);
            query = query.Where(t => t.CreatedAt < fechaHastaExclusiva);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Busqueda))
        {
            var term = filtro.Busqueda.Trim();
            query = query.Where(t =>
                t.Titulo.Contains(term) ||
                t.Descripcion.Contains(term));
        }

        var total = await query.CountAsync();

        var page = filtro.Page < 1 ? 1 : filtro.Page;
        var pageSize = filtro.PageSize < 1 ? 20 : filtro.PageSize;

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PageResult<TicketViewModel>
        {
            Items = _mapper.Map<List<TicketViewModel>>(items),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<TicketDetalleViewModel?> ObtenerDetalleAsync(int id)
    {
        var ticket = await _context.Tickets
            .AsNoTracking()
            .Include(t => t.Adjuntos.Where(a => !a.IsDeleted))
            .Include(t => t.ChecklistItems.Where(c => !c.IsDeleted))
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

        return ticket is null ? null : _mapper.Map<TicketDetalleViewModel>(ticket);
    }

    public async Task<TicketMetricasViewModel> ObtenerMetricasAsync()
    {
        var fechaRecienteDesde = DateTime.UtcNow.AddDays(-7);

        var conteos = await _context.Tickets
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total      = g.Count(),
                Pendientes = g.Count(t => t.Estado == EstadoTicket.Pendiente),
                EnCurso    = g.Count(t => t.Estado == EstadoTicket.EnCurso),
                Resueltos  = g.Count(t => t.Estado == EstadoTicket.Resuelto),
                Cancelados = g.Count(t => t.Estado == EstadoTicket.Cancelado),
                Recientes  = g.Count(t => t.CreatedAt >= fechaRecienteDesde),
            })
            .FirstOrDefaultAsync();

        return new TicketMetricasViewModel
        {
            Total      = conteos?.Total      ?? 0,
            Pendientes = conteos?.Pendientes ?? 0,
            EnCurso    = conteos?.EnCurso    ?? 0,
            Resueltos  = conteos?.Resueltos  ?? 0,
            Cancelados = conteos?.Cancelados ?? 0,
            Recientes  = conteos?.Recientes  ?? 0,
        };
    }

    // ── Ciclo de vida ──────────────────────────────────────────────────────

    public async Task<Ticket> CrearAsync(CreateTicketRequest request)
    {
        var ticket = _mapper.Map<Ticket>(request);
        ticket.Estado = EstadoTicket.Pendiente;

        _context.Tickets.Add(ticket);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Ticket #{Id} creado por {Usuario}.", ticket.Id, _currentUser.GetUsername());
        return ticket;
    }

    public async Task<Ticket> ActualizarAsync(int id, UpdateTicketRequest request)
    {
        var ticket = await ObtenerEntidadOFallarAsync(id);

        ValidarEditable(ticket);

        _mapper.Map(request, ticket);
        await _context.SaveChangesAsync();

        return ticket;
    }

    public async Task<Ticket> CambiarEstadoAsync(int id, UpdateTicketStatusRequest request)
    {
        var ticket = await ObtenerEntidadOFallarAsync(id);

        ValidarTransicionEstado(ticket.Estado, request.NuevoEstado);

        ticket.Estado = request.NuevoEstado;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Ticket #{Id} cambiado a estado {Estado} por {Usuario}.",
            id, request.NuevoEstado, _currentUser.GetUsername());

        return ticket;
    }

    public async Task<int> CambiarEstadoMasivoAsync(IEnumerable<int> ids, EstadoTicket nuevoEstado, string? descripcion)
    {
        var idsNormalizados = NormalizarIds(ids);
        if (idsNormalizados.Count == 0)
            throw new InvalidOperationException("Debe seleccionar al menos un ticket.");

        if (nuevoEstado == EstadoTicket.Resuelto && string.IsNullOrWhiteSpace(descripcion))
            throw new InvalidOperationException("La resolución es obligatoria para marcar tickets como resueltos.");

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var tickets = await _context.Tickets
            .Where(t => idsNormalizados.Contains(t.Id) && !t.IsDeleted)
            .ToListAsync();

        if (tickets.Count != idsNormalizados.Count)
            throw new KeyNotFoundException("Uno o más tickets no existen o fueron eliminados.");

        var now = DateTime.UtcNow;
        var username = _currentUser.GetUsername();
        var resolucion = descripcion?.Trim();

        foreach (var ticket in tickets)
        {
            ValidarTransicionEstado(ticket.Estado, nuevoEstado);

            if (nuevoEstado == EstadoTicket.Resuelto)
            {
                ticket.Resolucion = resolucion;
                ticket.ResueltoPor = username;
                ticket.FechaResolucion = now;
            }

            ticket.Estado = nuevoEstado;
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        _logger.LogInformation("{Cantidad} tickets cambiados a estado {Estado} por {Usuario}.",
            tickets.Count, nuevoEstado, username);

        return tickets.Count;
    }

    public async Task<Ticket> RegistrarResolucionAsync(int id, UpdateTicketResolutionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Resolucion))
            throw new InvalidOperationException("La resolución es obligatoria para marcar tickets como resueltos.");

        var ticket = await ObtenerEntidadOFallarAsync(id);

        // Delega al mismo validador que CambiarEstadoAsync: solo EnCurso→Resuelto es válido
        ValidarTransicionEstado(ticket.Estado, EstadoTicket.Resuelto);

        ticket.Resolucion = request.Resolucion.Trim();
        ticket.ResueltoPor = _currentUser.GetUsername();
        ticket.FechaResolucion = DateTime.UtcNow;
        ticket.Estado = EstadoTicket.Resuelto;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Ticket #{Id} resuelto por {Usuario}.", id, _currentUser.GetUsername());
        return ticket;
    }

    public async Task EliminarAsync(int id)
    {
        await EliminarMasivoAsync([id]);
    }

    public async Task<int> EliminarMasivoAsync(IEnumerable<int> ids)
    {
        var idsNormalizados = NormalizarIds(ids);
        if (idsNormalizados.Count == 0)
            throw new InvalidOperationException("Debe seleccionar al menos un ticket.");

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var tickets = await _context.Tickets
            .Include(t => t.Adjuntos.Where(a => !a.IsDeleted))
            .Include(t => t.ChecklistItems.Where(c => !c.IsDeleted))
            .Where(t => idsNormalizados.Contains(t.Id) && !t.IsDeleted)
            .ToListAsync();

        if (tickets.Count != idsNormalizados.Count)
            throw new KeyNotFoundException("Uno o más tickets no existen o ya fueron eliminados.");

        foreach (var ticket in tickets)
        {
            ticket.IsDeleted = true;

            foreach (var adjunto in ticket.Adjuntos)
                adjunto.IsDeleted = true;

            foreach (var item in ticket.ChecklistItems)
                item.IsDeleted = true;
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        _logger.LogInformation("{Cantidad} tickets eliminados por {Usuario}.",
            tickets.Count, _currentUser.GetUsername());

        return tickets.Count;
    }

    // ── Checklist ──────────────────────────────────────────────────────────

    public async Task<TicketChecklistItem> AgregarItemChecklistAsync(int ticketId, string descripcion, int orden)
    {
        var ticket = await ObtenerEntidadOFallarAsync(ticketId);
        ValidarEditable(ticket);

        var item = new TicketChecklistItem
        {
            TicketId = ticketId,
            Descripcion = descripcion,
            Orden = orden
        };

        _context.TicketChecklistItems.Add(item);
        await _context.SaveChangesAsync();
        return item;
    }

    public async Task<TicketChecklistItem> MarcarItemChecklistAsync(int itemId, bool completado)
    {
        var item = await _context.TicketChecklistItems
            .FirstOrDefaultAsync(i => i.Id == itemId && !i.IsDeleted)
            ?? throw new KeyNotFoundException($"ChecklistItem #{itemId} no encontrado.");

        item.Completado = completado;
        item.CompletadoPor = completado ? _currentUser.GetUsername() : null;
        item.FechaCompletado = completado ? DateTime.UtcNow : null;

        await _context.SaveChangesAsync();
        return item;
    }

    public async Task EliminarItemChecklistAsync(int itemId)
    {
        var item = await _context.TicketChecklistItems
            .FirstOrDefaultAsync(i => i.Id == itemId && !i.IsDeleted)
            ?? throw new KeyNotFoundException($"ChecklistItem #{itemId} no encontrado.");

        item.IsDeleted = true;
        await _context.SaveChangesAsync();
    }

    // ── Adjuntos ───────────────────────────────────────────────────────────

    public async Task<TicketAdjunto> SubirAdjuntoAsync(int ticketId, IFormFile archivo)
    {
        await ObtenerEntidadOFallarAsync(ticketId);

        var rutaRelativa = await _fileStorage.SaveAsync(archivo, TicketConstants.UploadFolder);

        var adjunto = new TicketAdjunto
        {
            TicketId = ticketId,
            NombreArchivo = archivo.FileName,
            RutaArchivo = rutaRelativa,
            TipoMIME = archivo.ContentType,
            TamanoBytes = archivo.Length
        };

        _context.TicketAdjuntos.Add(adjunto);
        await _context.SaveChangesAsync();
        return adjunto;
    }

    public async Task EliminarAdjuntoAsync(int adjuntoId)
    {
        var adjunto = await _context.TicketAdjuntos
            .FirstOrDefaultAsync(a => a.Id == adjuntoId && !a.IsDeleted)
            ?? throw new KeyNotFoundException($"Adjunto #{adjuntoId} no encontrado.");

        await _fileStorage.DeleteAsync(adjunto.RutaArchivo);

        adjunto.IsDeleted = true;
        await _context.SaveChangesAsync();
    }

    // ── Helpers privados ───────────────────────────────────────────────────

    private async Task<Ticket> ObtenerEntidadOFallarAsync(int id)
    {
        return await _context.Tickets
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted)
            ?? throw new KeyNotFoundException($"Ticket #{id} no encontrado.");
    }

    private static List<int> NormalizarIds(IEnumerable<int> ids)
    {
        return ids
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }

    private static void ValidarEditable(Ticket ticket)
    {
        if (ticket.Estado == EstadoTicket.Resuelto || ticket.Estado == EstadoTicket.Cancelado)
            throw new InvalidOperationException(
                $"No se puede editar un ticket con estado '{ticket.Estado}'.");
    }

    private static void ValidarTransicionEstado(EstadoTicket estadoActual, EstadoTicket nuevoEstado)
    {
        var transicionesValidas = new Dictionary<EstadoTicket, EstadoTicket[]>
        {
            [EstadoTicket.Pendiente]  = [EstadoTicket.EnCurso, EstadoTicket.Cancelado],
            [EstadoTicket.EnCurso]    = [EstadoTicket.Resuelto, EstadoTicket.Cancelado],
            [EstadoTicket.Resuelto]   = [EstadoTicket.EnCurso],   // permite reabrir
            [EstadoTicket.Cancelado]  = [EstadoTicket.Pendiente], // permite reactivar
        };

        if (!transicionesValidas.TryGetValue(estadoActual, out var validos) ||
            !validos.Contains(nuevoEstado))
        {
            throw new InvalidOperationException(
                $"Transición inválida: '{estadoActual}' → '{nuevoEstado}'.");
        }
    }
}
