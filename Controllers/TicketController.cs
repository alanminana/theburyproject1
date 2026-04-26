using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Controllers;

[Authorize]
[PermisoRequerido(Modulo = TicketConstants.Modulo, Accion = TicketConstants.Acciones.Ver)]
public class TicketController : Controller
{
    private readonly ITicketService _ticketService;
    private readonly ILogger<TicketController> _logger;

    public TicketController(ITicketService ticketService, ILogger<TicketController> logger)
    {
        _ticketService = ticketService;
        _logger = logger;
    }

    // GET: /Ticket
    public async Task<IActionResult> Index(
        string? busqueda = null,
        EstadoTicket? estado = null,
        TipoTicket? tipo = null,
        string? moduloOrigen = null,
        DateTime? fechaDesde = null,
        DateTime? fechaHasta = null,
        int page = 1)
    {
        var filtro = new TicketFilterViewModel
        {
            Busqueda     = busqueda,
            Estado       = estado,
            Tipo         = tipo,
            ModuloOrigen = moduloOrigen,
            FechaDesde   = fechaDesde,
            FechaHasta   = fechaHasta,
            Page         = page < 1 ? 1 : page,
            PageSize     = 20,
        };

        var resultado = await _ticketService.ListarAsync(filtro);
        var metricas  = await _ticketService.ObtenerMetricasAsync();
        var puedeCambiarEstado = User.TienePermiso(TicketConstants.Modulo, TicketConstants.Acciones.CambiarEstado);
        var puedeResolver = User.TienePermiso(TicketConstants.Modulo, TicketConstants.Acciones.Resolver);
        var puedeEliminar = User.TienePermiso(TicketConstants.Modulo, TicketConstants.Acciones.Eliminar);

        var vm = new TicketMvcIndexViewModel
        {
            Resultado  = resultado,
            Filtro     = filtro,
            TotalTickets = metricas.Total,
            Pendientes   = metricas.Pendientes,
            EnCurso      = metricas.EnCurso,
            Resueltos    = metricas.Resueltos,
            Cancelados   = metricas.Cancelados,
            Recientes    = metricas.Recientes,
            PuedeCambiarEstado = puedeCambiarEstado,
            PuedeResolver      = puedeResolver,
            PuedeEliminar      = puedeEliminar,
        };

        ViewData["Title"] = "Gestión de Tickets";
        ViewData["MetaDescription"] = "Revisión y seguimiento de reportes e incidencias generados desde el ERP TheBuryProject.";
        return View("Index_tw", vm);
    }

    // GET: /Ticket/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var ticket = await _ticketService.ObtenerDetalleAsync(id);
        if (ticket is null)
        {
            _logger.LogWarning("Ticket #{Id} no encontrado o eliminado.", id);
            return NotFound();
        }

        ViewData["Title"] = $"Ticket #{ticket.Id} — {ticket.Titulo}";
        ViewData["PuedeCambiarEstadoTicket"] = User.TienePermiso(TicketConstants.Modulo, TicketConstants.Acciones.CambiarEstado);
        ViewData["PuedeResolverTicket"] = User.TienePermiso(TicketConstants.Modulo, TicketConstants.Acciones.Resolver);
        ViewData["PuedeEliminarTicket"] = User.TienePermiso(TicketConstants.Modulo, TicketConstants.Acciones.Eliminar);
        return View("Details_tw", ticket);
    }

    // POST: /Ticket/CambiarEstado
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarEstado(CambiarEstadoTicketsRequest request)
    {
        var ids = ParseTicketIds(request.TicketIds);
        if (ids.Count == 0 || request.NuevoEstado is null)
        {
            TempData["TicketError"] = "Seleccioná al menos un ticket y un estado válido.";
            return RedirectToTicketReturnUrl(request.ReturnUrl);
        }

        var requiereResolver = request.NuevoEstado == EstadoTicket.Resuelto;
        var tienePermiso = requiereResolver
            ? User.TienePermiso(TicketConstants.Modulo, TicketConstants.Acciones.Resolver)
            : User.TienePermiso(TicketConstants.Modulo, TicketConstants.Acciones.CambiarEstado);

        if (!tienePermiso)
        {
            TempData["TicketError"] = requiereResolver
                ? "No tenés permiso para resolver tickets."
                : "No tenés permiso para cambiar el estado de tickets.";
            return RedirectToTicketReturnUrl(request.ReturnUrl);
        }

        if (requiereResolver && string.IsNullOrWhiteSpace(request.Descripcion))
        {
            TempData["TicketError"] = "La descripción es obligatoria para marcar tickets como resueltos.";
            return RedirectToTicketReturnUrl(request.ReturnUrl);
        }

        try
        {
            if (ids.Count == 1)
            {
                await CambiarEstadoIndividualAsync(ids[0], request.NuevoEstado.Value, request.Descripcion);
                TempData["TicketSuccess"] = $"Ticket #{ids[0]} actualizado a {request.NuevoEstado.Value.GetDisplayName()}.";
            }
            else
            {
                var cantidad = await _ticketService.CambiarEstadoMasivoAsync(
                    ids,
                    request.NuevoEstado.Value,
                    request.Descripcion);

                TempData["TicketSuccess"] = $"{cantidad} tickets actualizados a {request.NuevoEstado.Value.GetDisplayName()}.";
            }
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "No se pudieron cambiar estados de tickets.");
            TempData["TicketError"] = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            TempData["TicketError"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cambiar estado de tickets.");
            TempData["TicketError"] = "No se pudo cambiar el estado de los tickets seleccionados.";
        }

        return RedirectToTicketReturnUrl(request.ReturnUrl);
    }

    // POST: /Ticket/Eliminar
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = TicketConstants.Modulo, Accion = TicketConstants.Acciones.Eliminar)]
    public async Task<IActionResult> Eliminar(EliminarTicketsRequest request)
    {
        var ids = ParseTicketIds(request.TicketIds);
        if (ids.Count == 0)
        {
            TempData["TicketError"] = "Seleccioná al menos un ticket para eliminar.";
            return RedirectToTicketReturnUrl(request.ReturnUrl);
        }

        try
        {
            var cantidad = await _ticketService.EliminarMasivoAsync(ids);
            TempData["TicketSuccess"] = cantidad == 1
                ? "Ticket eliminado correctamente."
                : $"{cantidad} tickets eliminados correctamente.";
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "No se pudieron eliminar tickets.");
            TempData["TicketError"] = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            TempData["TicketError"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar tickets.");
            TempData["TicketError"] = "No se pudieron eliminar los tickets seleccionados.";
        }

        return RedirectToTicketReturnUrl(request.ReturnUrl);
    }

    private async Task CambiarEstadoIndividualAsync(int id, EstadoTicket nuevoEstado, string? descripcion)
    {
        if (nuevoEstado == EstadoTicket.Resuelto)
        {
            await _ticketService.RegistrarResolucionAsync(id, new UpdateTicketResolutionRequest
            {
                Resolucion = descripcion?.Trim() ?? string.Empty
            });
            return;
        }

        await _ticketService.CambiarEstadoAsync(id, new UpdateTicketStatusRequest
        {
            NuevoEstado = nuevoEstado
        });
    }

    private IActionResult RedirectToTicketReturnUrl(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(Index));
    }

    private static List<int> ParseTicketIds(string? rawIds)
    {
        if (string.IsNullOrWhiteSpace(rawIds))
            return [];

        return rawIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => int.TryParse(id, out var parsed) ? parsed : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }
}
