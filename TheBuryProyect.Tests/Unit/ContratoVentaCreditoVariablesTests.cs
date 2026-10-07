using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Auditoría Crédito F4: las variables financieras de la plantilla del contrato deben ser
/// coherentes entre sí dentro del mismo documento.
/// </summary>
public class ContratoVentaCreditoVariablesTests
{
    private static ContratoVentaCreditoSnapshot Snapshot(params decimal[] cuotas)
    {
        var plan = cuotas
            .Select((monto, i) => new CuotaContratoSnapshot { NumeroCuota = i + 1, MontoTotal = monto })
            .ToList();

        return new ContratoVentaCreditoSnapshot
        {
            Vendedor = new VendedorSnapshot(),
            Comprador = new CompradorSnapshot(),
            Venta = new VentaSnapshot(),
            Contrato = new ContratoSnapshot(),
            Credito = new CreditoSnapshot
            {
                CantidadCuotas = plan.Count,
                MontoCuota = plan[0].MontoTotal,
                TotalAPagar = plan.Sum(c => c.MontoTotal),
                PlanCuotas = plan
            }
        };
    }

    [Fact]
    public void SaldoFinanciado_CoincideConTotalAPagar_AunConCuotasDesiguales()
    {
        // Cuota 1 distinta de las demás (redondeo / cuota sin recargo): cuota × cantidad (300)
        // difiere de la suma real del plan (299,99).
        var variables = ContratoVentaCreditoService.CrearVariables(Snapshot(100.01m, 99.99m, 99.99m));

        Assert.Equal(variables["{{Credito.TotalAPagar}}"], variables["{{SALDO_FINANCIADO}}"]);
    }
}
