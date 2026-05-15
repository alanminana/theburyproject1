using TheBuryProject.Models.Enums;

namespace TheBuryProject.Helpers;

public static class OrdenCompraUiHelper
{
    public static string EstadoBadgeClass(EstadoOrdenCompra estado) => estado switch
    {
        EstadoOrdenCompra.Borrador   => "badge-erp border border-slate-500/20 bg-slate-500/10 text-slate-300",
        EstadoOrdenCompra.Enviada    => "badge-erp border border-blue-500/20 bg-blue-500/10 text-blue-300",
        EstadoOrdenCompra.Confirmada => "badge-erp border border-green-500/20 bg-green-500/10 text-green-300",
        EstadoOrdenCompra.EnTransito => "badge-erp border border-amber-500/20 bg-amber-500/10 text-amber-300",
        EstadoOrdenCompra.Recibida   => "badge-erp border border-emerald-500/20 bg-emerald-500/10 text-emerald-300",
        EstadoOrdenCompra.Cancelada  => "badge-erp border border-rose-500/20 bg-rose-500/10 text-rose-300",
        _                            => "badge-erp badge-erp-neutral"
    };

    public static string EstadoNombre(EstadoOrdenCompra estado) => estado switch
    {
        EstadoOrdenCompra.Borrador   => "Borrador",
        EstadoOrdenCompra.Enviada    => "Enviada",
        EstadoOrdenCompra.Confirmada => "Confirmada",
        EstadoOrdenCompra.EnTransito => "En Tránsito",
        EstadoOrdenCompra.Recibida   => "Recibida",
        EstadoOrdenCompra.Cancelada  => "Cancelada",
        _                            => estado.ToString()
    };
}
