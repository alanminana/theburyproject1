using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Base;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Services.Interfaces;

/// <summary>
/// Servicio principal para gestión de tickets internos del ERP.
/// </summary>
public interface ITicketService
{
    // ── Consultas ──────────────────────────────────────────────────────────
    Task<PageResult<TicketViewModel>> ListarAsync(TicketFilterViewModel filtro);
    Task<TicketDetalleViewModel?> ObtenerDetalleAsync(int id);
    Task<TicketMetricasViewModel> ObtenerMetricasAsync();

    // ── Ciclo de vida ──────────────────────────────────────────────────────
    Task<Ticket> CrearAsync(CreateTicketRequest request);
    Task<Ticket> ActualizarAsync(int id, UpdateTicketRequest request);
    Task<Ticket> CambiarEstadoAsync(int id, UpdateTicketStatusRequest request);
    Task<int> CambiarEstadoMasivoAsync(IEnumerable<int> ids, EstadoTicket nuevoEstado, string? descripcion);
    Task<Ticket> RegistrarResolucionAsync(int id, UpdateTicketResolutionRequest request);
    Task EliminarAsync(int id);
    Task<int> EliminarMasivoAsync(IEnumerable<int> ids);

    // ── Checklist ──────────────────────────────────────────────────────────
    Task<TicketChecklistItem> AgregarItemChecklistAsync(int ticketId, string descripcion, int orden);
    Task<TicketChecklistItem> MarcarItemChecklistAsync(int itemId, bool completado);
    Task EliminarItemChecklistAsync(int itemId);

    // ── Adjuntos ───────────────────────────────────────────────────────────
    Task<TicketAdjunto> SubirAdjuntoAsync(int ticketId, IFormFile archivo);
    Task EliminarAdjuntoAsync(int adjuntoId);
}
