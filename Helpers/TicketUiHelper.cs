using TheBuryProject.Models.Enums;

namespace TheBuryProject.Helpers;

public static class TicketUiHelper
{
    public static string EstadoBadgeClass(EstadoTicket estado) => estado switch
    {
        EstadoTicket.Pendiente => "badge-erp border border-amber-500/20 bg-amber-500/10 text-amber-300",
        EstadoTicket.EnCurso => "badge-erp border border-blue-500/20 bg-blue-500/10 text-blue-300",
        EstadoTicket.Resuelto => "badge-erp border border-green-500/20 bg-green-500/10 text-green-300",
        EstadoTicket.Cancelado => "badge-erp border border-slate-500/20 bg-slate-500/10 text-slate-300",
        _ => "badge-erp badge-erp-neutral"
    };

    public static string EstadoIcon(EstadoTicket estado) => estado switch
    {
        EstadoTicket.Pendiente => "schedule",
        EstadoTicket.EnCurso => "sync",
        EstadoTicket.Resuelto => "check_circle",
        EstadoTicket.Cancelado => "cancel",
        _ => "label"
    };

    public static IReadOnlyList<EstadoTicket> GetTransicionesDisponibles(EstadoTicket estado) => estado switch
    {
        EstadoTicket.Pendiente => [EstadoTicket.EnCurso, EstadoTicket.Cancelado],
        EstadoTicket.EnCurso => [EstadoTicket.Resuelto, EstadoTicket.Cancelado],
        EstadoTicket.Resuelto => [EstadoTicket.EnCurso],
        EstadoTicket.Cancelado => [EstadoTicket.Pendiente],
        _ => []
    };

    public static bool RequiereDescripcion(EstadoTicket estado) => estado == EstadoTicket.Resuelto;

    public static string AccionEstadoLabel(EstadoTicket estado) => estado switch
    {
        EstadoTicket.Pendiente => "Reabrir",
        EstadoTicket.EnCurso => "Marcar en curso",
        EstadoTicket.Resuelto => "Marcar resuelto",
        EstadoTicket.Cancelado => "Cancelar",
        _ => $"Cambiar a {estado.GetDisplayName()}"
    };
}
