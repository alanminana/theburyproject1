using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public class FacturaAlicuotaResumenBuilderTests
{
    [Fact]
    public void Build_ProductosMixtos_GeneraUnaFilaPorAlicuota()
    {
        var detalles = new List<VentaDetalleViewModel>
        {
            new()
            {
                PorcentajeIVA = 21m,
                AlicuotaIVANombre = "IVA 21",
                SubtotalFinalNeto = 100m,
                SubtotalFinalIVA = 21m,
                SubtotalFinal = 121m
            },
            new()
            {
                PorcentajeIVA = 10.5m,
                AlicuotaIVANombre = "IVA 10.5",
                SubtotalFinalNeto = 100m,
                SubtotalFinalIVA = 10.5m,
                SubtotalFinal = 110.5m
            },
            new()
            {
                PorcentajeIVA = 0m,
                AlicuotaIVANombre = "Exento",
                SubtotalFinalNeto = 50m,
                SubtotalFinalIVA = 0m,
                SubtotalFinal = 50m
            }
        };

        var resumen = FacturaAlicuotaResumenBuilder.Build(detalles);

        Assert.Equal(3, resumen.Count);
        Assert.Collection(
            resumen,
            item =>
            {
                Assert.Equal(21m, item.PorcentajeIVA);
                Assert.Equal(100m, item.BaseImponible);
                Assert.Equal(21m, item.IVA);
                Assert.Equal(121m, item.Total);
                Assert.Equal(item.Total, item.BaseImponible + item.IVA);
            },
            item =>
            {
                Assert.Equal(10.5m, item.PorcentajeIVA);
                Assert.Equal(100m, item.BaseImponible);
                Assert.Equal(10.5m, item.IVA);
                Assert.Equal(110.5m, item.Total);
                Assert.Equal(item.Total, item.BaseImponible + item.IVA);
            },
            item =>
            {
                Assert.Equal(0m, item.PorcentajeIVA);
                Assert.Equal(50m, item.BaseImponible);
                Assert.Equal(0m, item.IVA);
                Assert.Equal(50m, item.Total);
                Assert.Equal(item.Total, item.BaseImponible + item.IVA);
            });

        Assert.Equal(250m, resumen.Sum(item => item.BaseImponible));
        Assert.Equal(31.5m, resumen.Sum(item => item.IVA));
        Assert.Equal(281.5m, resumen.Sum(item => item.Total));
    }

    [Fact]
    public void Build_DescuentoGeneralProrrateado_RespetaSnapshotsFinales()
    {
        var detalles = new List<VentaDetalleViewModel>
        {
            new()
            {
                PorcentajeIVA = 21m,
                AlicuotaIVANombre = "IVA 21",
                SubtotalNeto = 100m,
                SubtotalIVA = 21m,
                Subtotal = 121m,
                DescuentoGeneralProrrateado = 31m,
                SubtotalFinalNeto = 74.38m,
                SubtotalFinalIVA = 15.62m,
                SubtotalFinal = 90m
            },
            new()
            {
                PorcentajeIVA = 10.5m,
                AlicuotaIVANombre = "IVA 10.5",
                SubtotalNeto = 110m,
                SubtotalIVA = 11.55m,
                Subtotal = 121.55m,
                DescuentoGeneralProrrateado = 11.05m,
                SubtotalFinalNeto = 100m,
                SubtotalFinalIVA = 10.5m,
                SubtotalFinal = 110.5m
            }
        };

        var resumen = FacturaAlicuotaResumenBuilder.Build(detalles);

        Assert.Equal(174.38m, resumen.Sum(item => item.BaseImponible));
        Assert.Equal(26.12m, resumen.Sum(item => item.IVA));
        Assert.Equal(200.5m, resumen.Sum(item => item.Total));
    }

    [Fact]
    public void Build_SinSnapshotsFinales_UsaSnapshotsLegacy()
    {
        var detalles = new List<VentaDetalleViewModel>
        {
            new()
            {
                PorcentajeIVA = 21m,
                SubtotalNeto = 100m,
                SubtotalIVA = 21m,
                Subtotal = 121m
            }
        };

        var resumen = FacturaAlicuotaResumenBuilder.Build(detalles);

        var item = Assert.Single(resumen);
        Assert.Equal("IVA 21%", item.AlicuotaIVANombre);
        Assert.Equal(100m, item.BaseImponible);
        Assert.Equal(21m, item.IVA);
        Assert.Equal(121m, item.Total);
    }
}
