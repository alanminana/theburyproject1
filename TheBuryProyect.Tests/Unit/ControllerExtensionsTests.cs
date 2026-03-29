using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para ControllerExtensions.JsonModelErrors.
/// Función pura de extracción de errores del ModelState — no requiere base de datos.
/// </summary>
public class ControllerExtensionsTests
{
    // Subclase mínima de Controller para poder instanciarlo en tests
    private sealed class FakeController : Controller { }

    private static FakeController BuildController()
    {
        var controller = new FakeController();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    [Fact]
    public void JsonModelErrors_ModelStateValido_RetornaSuccessFalseConFallback()
    {
        var controller = BuildController();
        // ModelState vacío (sin errores) → debe retornar el fallback genérico

        var result = controller.JsonModelErrors();
        var json = result.Value as dynamic;

        Assert.NotNull(result.Value);
        var dict = (IDictionary<string, object?>)result.Value
            .GetType()
            .GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(result.Value));

        Assert.Equal(false, dict["success"]);
        var errors = (string[])dict["errors"]!;
        Assert.Single(errors);
        Assert.Equal("No se pudo completar la operación.", errors[0]);
    }

    [Fact]
    public void JsonModelErrors_ConUnError_RetornaEseError()
    {
        var controller = BuildController();
        controller.ModelState.AddModelError("Campo", "El campo es requerido.");

        var result = controller.JsonModelErrors();
        var dict = GetDict(result);

        Assert.Equal(false, dict["success"]);
        var errors = (string[])dict["errors"]!;
        Assert.Single(errors);
        Assert.Equal("El campo es requerido.", errors[0]);
    }

    [Fact]
    public void JsonModelErrors_ConMultiplesErrores_RetornaTodos()
    {
        var controller = BuildController();
        controller.ModelState.AddModelError("Campo1", "Error uno.");
        controller.ModelState.AddModelError("Campo2", "Error dos.");

        var result = controller.JsonModelErrors();
        var dict = GetDict(result);

        var errors = (string[])dict["errors"]!;
        Assert.Equal(2, errors.Length);
        Assert.Contains("Error uno.", errors);
        Assert.Contains("Error dos.", errors);
    }

    [Fact]
    public void JsonModelErrors_ErrorDuplicado_SeDeduplicaEnLaSalida()
    {
        var controller = BuildController();
        controller.ModelState.AddModelError("Campo1", "Mensaje repetido.");
        controller.ModelState.AddModelError("Campo2", "Mensaje repetido.");

        var result = controller.JsonModelErrors();
        var dict = GetDict(result);

        var errors = (string[])dict["errors"]!;
        Assert.Single(errors);
        Assert.Equal("Mensaje repetido.", errors[0]);
    }

    [Fact]
    public void JsonModelErrors_ErrorConMensajeVacio_UsaFallback()
    {
        var controller = BuildController();
        // Agregar error con mensaje vacío (puede pasar con excepciones de binding)
        controller.ModelState.AddModelError("Campo", string.Empty);

        var result = controller.JsonModelErrors();
        var dict = GetDict(result);

        var errors = (string[])dict["errors"]!;
        Assert.Single(errors);
        Assert.Equal("No se pudo completar la operación.", errors[0]);
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private static Dictionary<string, object?> GetDict(JsonResult result)
        => result.Value!
            .GetType()
            .GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(result.Value));
}
