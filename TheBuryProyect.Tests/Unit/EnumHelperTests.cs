using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para EnumHelper.
/// Función pura con reflection y caché — no requiere base de datos.
/// Cubre GetDisplayName y GetSelectList con y sin DisplayAttribute,
/// valores obsoletos, duplicados y selección.
/// </summary>
public class EnumHelperTests
{
    // -------------------------------------------------------------------------
    // Test enums
    // -------------------------------------------------------------------------

    private enum SimpleEnum
    {
        [Display(Name = "Primer Valor")]
        PrimerValor = 1,
        [Display(Name = "Segundo Valor")]
        SegundoValor = 2,
        SinAtributo = 3
    }

    private enum EnumConObsoleto
    {
        Activo = 1,
        [Obsolete]
        Legado = 2,
        Nuevo = 3
    }

    private enum EnumConDuplicado
    {
        Original = 1,
        Alias = 1 // mismo valor numérico
    }

    // =========================================================================
    // GetDisplayName
    // =========================================================================

    [Fact]
    public void GetDisplayName_ConDisplayAttribute_RetornaNombreDelAtributo()
    {
        var resultado = SimpleEnum.PrimerValor.GetDisplayName();
        Assert.Equal("Primer Valor", resultado);
    }

    [Fact]
    public void GetDisplayName_SinDisplayAttribute_RetornaNombreDelCampo()
    {
        var resultado = SimpleEnum.SinAtributo.GetDisplayName();
        Assert.Equal("SinAtributo", resultado);
    }

    [Fact]
    public void GetDisplayName_MismoValor_SegundaLlamada_RetornaDesdeCaché()
    {
        // Call twice to exercise cache path
        var primera = SimpleEnum.SegundoValor.GetDisplayName();
        var segunda = SimpleEnum.SegundoValor.GetDisplayName();

        Assert.Equal(primera, segunda);
        Assert.Equal("Segundo Valor", segunda);
    }

    // =========================================================================
    // GetSelectList
    // =========================================================================

    [Fact]
    public void GetSelectList_RetornaTodosLosValoresNoObsoletos()
    {
        var lista = EnumHelper.GetSelectList<SimpleEnum>().ToList();

        Assert.Equal(3, lista.Count);
        Assert.Contains(lista, i => i.Text == "Primer Valor");
        Assert.Contains(lista, i => i.Text == "Segundo Valor");
        Assert.Contains(lista, i => i.Text == "SinAtributo");
    }

    [Fact]
    public void GetSelectList_ExcluyeValoresObsoletos()
    {
        var lista = EnumHelper.GetSelectList<EnumConObsoleto>().ToList();

        Assert.Equal(2, lista.Count);
        Assert.DoesNotContain(lista, i => i.Value == "2"); // Legado = 2
    }

    [Fact]
    public void GetSelectList_EliminaDuplicadosPorValor()
    {
        var lista = EnumHelper.GetSelectList<EnumConDuplicado>().ToList();

        // Both Original and Alias have int value 1, so only one should appear
        Assert.Single(lista);
    }

    [Fact]
    public void GetSelectList_ValueEsEnteroDelEnum()
    {
        var lista = EnumHelper.GetSelectList<SimpleEnum>().ToList();

        var item = lista.First(i => i.Text == "Primer Valor");
        Assert.Equal("1", item.Value);
    }

    [Fact]
    public void GetSelectList_SinSeleccionado_NingunEstaSeleccionado()
    {
        var lista = EnumHelper.GetSelectList<SimpleEnum>().ToList();

        Assert.All(lista, i => Assert.False(i.Selected));
    }

    [Fact]
    public void GetSelectList_ConSeleccionado_SoloEseEstaSeleccionado()
    {
        var lista = EnumHelper.GetSelectList<SimpleEnum>(SimpleEnum.SegundoValor).ToList();

        var seleccionados = lista.Where(i => i.Selected).ToList();
        Assert.Single(seleccionados);
        Assert.Equal("2", seleccionados[0].Value);
    }

    [Fact]
    public void GetSelectList_SelectionNoExistente_NingunEstaSeleccionado()
    {
        // null passed — no selection
        var lista = EnumHelper.GetSelectList<SimpleEnum>(null).ToList();

        Assert.All(lista, i => Assert.False(i.Selected));
    }
}
