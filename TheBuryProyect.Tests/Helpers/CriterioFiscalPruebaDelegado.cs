using TheBuryProject.Helpers;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Helpers;

/// <summary>Versión de prueba distinta de 1 con las mismas reglas que v1: permite verificar que una operación estampa la versión vigente.</summary>
internal sealed class CriterioFiscalPruebaDelegado(int version) : ICriterioFiscal
{
    private static readonly ICriterioFiscal V1 = new CriterioFiscalV1();
    public int Version => version;
    public decimal AlicuotaServicios => V1.AlicuotaServicios;
    public string NombreAlicuotaServicios => V1.NombreAlicuotaServicios;
    public (decimal Neto, decimal Iva) SepararServicios(decimal bruto) => V1.SepararServicios(bruto);
    public decimal TruncarAPesos(decimal valor) => V1.TruncarAPesos(valor);
    public decimal CalcularRecargo(decimal totalCobrado, decimal brutoItems) => V1.CalcularRecargo(totalCobrado, brutoItems);
    public decimal[] Prorratear(IReadOnlyList<decimal> pesos, decimal importe) => V1.Prorratear(pesos, importe);
    public List<RecargoMedioPagoIva.ParteAlicuota> Repartir(IEnumerable<(decimal Porcentaje, decimal Bruto)> bases, decimal recargo) => V1.Repartir(bases, recargo);
    public RecargoMedioPagoIva.Discriminacion Discriminar(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas) => V1.Discriminar(total, lineas);
    public List<RecargoMedioPagoIva.ParteAlicuota> ResumenPorAlicuota(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas) => V1.ResumenPorAlicuota(total, lineas);
    public (decimal Neto, decimal Iva) AbsorberTruncado(decimal neto, decimal iva, decimal truncado) => V1.AbsorberTruncado(neto, iva, truncado);
}
