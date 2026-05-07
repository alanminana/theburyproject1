using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services;

public class CreditoUiQueryService : ICreditoUiQueryService
{
    public virtual List<CuotaViewModel> ObtenerCuotasPendientes(IEnumerable<CuotaViewModel>? cuotas) =>
        (cuotas ?? Enumerable.Empty<CuotaViewModel>())
            .Where(c => c.Estado == EstadoCuota.Pendiente || c.Estado == EstadoCuota.Vencida || c.Estado == EstadoCuota.Parcial)
            .OrderBy(c => c.NumeroCuota)
            .ToList();

    public virtual List<SelectListItem> ProyectarCuotasPendientes(IEnumerable<CuotaViewModel>? cuotas) =>
        ObtenerCuotasPendientes(cuotas)
            .Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = $"Cuota #{c.NumeroCuota} - Vto: {c.FechaVencimiento:dd/MM/yyyy} - {c.MontoTotal:C}"
            })
            .ToList();

    public virtual string BuildCuotasJson(IEnumerable<CuotaViewModel>? cuotas)
    {
        var data = ObtenerCuotasPendientes(cuotas)
            .ToDictionary(
                c => c.Id.ToString(),
                c => new
                {
                    saldo        = c.SaldoPendiente,
                    montoCuota   = c.MontoTotal,
                    punitorio    = c.MontoPunitorio,
                    numeroCuota  = c.NumeroCuota,
                    vencimiento  = c.FechaVencimiento.ToString("dd/MM/yyyy"),
                    estaVencida  = c.EstaVencida,
                    diasAtraso   = c.DiasAtraso
                });

        return System.Text.Json.JsonSerializer.Serialize(data);
    }

    public virtual List<CreditoClienteIndexViewModel> AgruparCreditosPorCliente(IEnumerable<CreditoViewModel> creditos)
    {
        return creditos
            .Where(c => c.ClienteId > 0)
            .GroupBy(c => c.ClienteId)
            .Select(grupo =>
            {
                var creditosCliente = grupo
                    .OrderByDescending(c => c.FechaSolicitud)
                    .ThenByDescending(c => c.Id)
                    .ToList();

                var cliente = creditosCliente.First().Cliente;
                var cuotas = creditosCliente
                    .SelectMany(c => c.Cuotas ?? Enumerable.Empty<CuotaViewModel>())
                    .ToList();

                var cuotasVencidas = cuotas.Count(c => c.EstaVencida);

                return new CreditoClienteIndexViewModel
                {
                    Cliente = cliente,
                    Documento = !string.IsNullOrWhiteSpace(cliente.NumeroDocumento)
                        ? cliente.NumeroDocumento
                        : $"Cliente #{cliente.Id}",
                    CantidadCreditos = creditosCliente.Count,
                    SaldoPendienteTotal = creditosCliente.Sum(c => c.SaldoPendiente),
                    CuotasVencidas = cuotasVencidas,
                    ProximoVencimiento = ObtenerProximoVencimiento(cuotas),
                    EstadoConsolidado = ResolverEstadoConsolidado(creditosCliente, cuotasVencidas),
                    Creditos = creditosCliente
                };
            })
            .OrderByDescending(c => c.CuotasVencidas > 0)
            .ThenBy(c => c.ProximoVencimiento ?? DateTime.MaxValue)
            .ThenBy(c => c.Cliente.NombreCompleto)
            .ToList();
    }

    public virtual string ResolverEstadoConsolidado(IReadOnlyCollection<CreditoViewModel> creditos, int cuotasVencidas)
    {
        if (cuotasVencidas > 0)
            return "En mora";

        if (creditos.Any(c => c.Estado == EstadoCredito.Activo || c.Estado == EstadoCredito.Generado))
            return "Activo";

        if (creditos.Any(c => c.Estado == EstadoCredito.Solicitado || c.Estado == EstadoCredito.PendienteConfiguracion))
            return "Pendiente";

        if (creditos.Any(c => c.Estado == EstadoCredito.Aprobado || c.Estado == EstadoCredito.Configurado))
            return "Aprobado";

        if (creditos.All(c => c.Estado == EstadoCredito.Finalizado))
            return "Finalizado";

        if (creditos.All(c => c.Estado == EstadoCredito.Rechazado))
            return "Rechazado";

        if (creditos.All(c => c.Estado == EstadoCredito.Cancelado))
            return "Cancelado";

        return "Mixto";
    }

    private static DateTime? ObtenerProximoVencimiento(IEnumerable<CuotaViewModel> cuotas) =>
        cuotas
            .Where(c => c.Estado == EstadoCuota.Pendiente || c.Estado == EstadoCuota.Vencida || c.Estado == EstadoCuota.Parcial)
            .OrderBy(c => c.FechaVencimiento)
            .Select(c => (DateTime?)c.FechaVencimiento)
            .FirstOrDefault();
}
