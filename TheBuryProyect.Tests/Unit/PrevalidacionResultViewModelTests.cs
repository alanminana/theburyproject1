using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// PUN-ML10-F: contrato tipado de <see cref="MotivoPrevalidacion"/>/<see cref="ProblemaCredito"/>
/// para la superficie de prevalidación (wizard de venta, antes de guardar).
/// </summary>
public class PrevalidacionResultViewModelTests
{
    [Fact]
    public void MotivoPrevalidacion_DefaultValores_SonNullNoZero()
    {
        var motivo = new MotivoPrevalidacion();

        Assert.Null(motivo.MontoAsociado);
        Assert.Null(motivo.DiasAsociado);
        Assert.Null(motivo.Unidad);
    }

    [Fact]
    public void ProblemaCredito_DefaultValores_SonNullNoZero()
    {
        var problema = new ProblemaCredito();

        Assert.Null(problema.MontoAsociado);
        Assert.Null(problema.DiasAsociado);
        Assert.Null(problema.ValorAsociado);
        Assert.Null(problema.Unidad);
    }

    [Fact]
    public void MotivoPrevalidacion_CategoriaPunitorio_NoEsMora()
    {
        // PUN-ML10-D/F: la categoría Punitorio nunca debe colapsar en Mora — evita que la UI
        // (icono/etiqueta) trate el punitorio como si fuera mora de capital.
        var motivo = new MotivoPrevalidacion { Categoria = CategoriaMotivo.Punitorio };

        Assert.NotEqual(CategoriaMotivo.Mora, motivo.Categoria);
        Assert.Equal(CategoriaMotivo.Punitorio, motivo.Categoria);
    }

    [Fact]
    public void PrevalidacionResult_MensajeResumen_NoRompeConMotivoPunitorioConMonto()
    {
        var result = new PrevalidacionResultViewModel
        {
            Resultado = ResultadoPrevalidacion.RequiereAutorizacion,
            Motivos = new List<MotivoPrevalidacion>
            {
                new()
                {
                    Categoria = CategoriaMotivo.Punitorio,
                    Titulo = "Punitorio aplicado pendiente",
                    Descripcion = "Punitorio aplicado pendiente: $75 (1 cuota)",
                    MontoAsociado = 75m
                }
            }
        };

        Assert.Contains("Punitorio", result.MensajeResumen);
        Assert.Contains("$75", result.MensajeResumen);
        Assert.True(result.PermiteGuardar);
    }
}
