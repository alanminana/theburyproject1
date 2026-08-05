using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// PUN-ML10-F: contrato tipado de <see cref="RazonAutorizacion"/> — MontoAsociado/DiasAsociado
/// nunca mezclan unidades sin metadato, y el legacy <see cref="RazonAutorizacion.ValorAsociado"/>
/// se conserva por compatibilidad pero acompañado de <see cref="RazonAutorizacion.Unidad"/>.
/// </summary>
public class ValidacionVentaViewModelTests
{
    [Fact]
    public void RazonAutorizacion_DefaultValores_SonNullNoZero()
    {
        // Un valor nulo (desconocido/no aplica) nunca debe confundirse con un $0 real
        // (regla PUN-ML10-F: "si el dato es desconocido, no mostrar cero como si fuera conocido").
        var razon = new RazonAutorizacion();

        Assert.Null(razon.MontoAsociado);
        Assert.Null(razon.DiasAsociado);
        Assert.Null(razon.ValorAsociado);
        Assert.Null(razon.Unidad);
    }

    [Fact]
    public void RazonAutorizacion_TipoDisplay_PunitorioTieneTextoEspecifico()
    {
        var razon = new RazonAutorizacion { Tipo = TipoRazonAutorizacion.Punitorio };

        Assert.Equal("Punitorio aplicado pendiente", razon.TipoDisplay);
        Assert.NotEqual("Requiere revisión", razon.TipoDisplay);
    }

    [Theory]
    [InlineData(TipoRazonAutorizacion.MoraActiva, "Mora activa")]
    [InlineData(TipoRazonAutorizacion.ExcedeCupo, "Excede cupo disponible")]
    [InlineData(TipoRazonAutorizacion.DocumentacionVencida, "Documentación vencida")]
    [InlineData(TipoRazonAutorizacion.ExcedeUmbralRol, "Excede umbral del rol")]
    [InlineData(TipoRazonAutorizacion.ClienteRequiereAutorizacion, "Cliente requiere autorización")]
    public void RazonAutorizacion_TipoDisplay_OtrosTiposNoCambianConML10F(TipoRazonAutorizacion tipo, string esperado)
    {
        var razon = new RazonAutorizacion { Tipo = tipo };

        Assert.Equal(esperado, razon.TipoDisplay);
    }

    [Fact]
    public void UnidadValorAsociado_DistingueMontoDeDias()
    {
        var razonMonto = new RazonAutorizacion { MontoAsociado = 75m, Unidad = UnidadValorAsociado.Monto };
        var razonDias = new RazonAutorizacion { DiasAsociado = 5, Unidad = UnidadValorAsociado.Dias };

        Assert.Equal(UnidadValorAsociado.Monto, razonMonto.Unidad);
        Assert.Null(razonMonto.DiasAsociado);
        Assert.Equal(UnidadValorAsociado.Dias, razonDias.Unidad);
        Assert.Null(razonDias.MontoAsociado);
    }
}
