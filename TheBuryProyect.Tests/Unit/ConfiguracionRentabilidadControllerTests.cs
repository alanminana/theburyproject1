using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public class ConfiguracionRentabilidadControllerTests
{
    [Fact]
    public async Task Index_Get_CargaDefaults()
    {
        var service = new FakeConfiguracionRentabilidadService
        {
            Configuracion = new ConfiguracionRentabilidad()
        };
        var controller = CrearController(service);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index_tw", view.ViewName);

        var model = Assert.IsType<ConfiguracionRentabilidadViewModel>(view.Model);
        Assert.Equal(20m, model.MargenBajoMax);
        Assert.Equal(35m, model.MargenAltoMin);
    }

    [Fact]
    public async Task Index_Post_Valido_PersisteYRedirige()
    {
        var service = new FakeConfiguracionRentabilidadService();
        var controller = CrearController(service);
        var model = new ConfiguracionRentabilidadViewModel
        {
            MargenBajoMax = 25m,
            MargenAltoMin = 40m
        };

        var result = await controller.Index(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfiguracionRentabilidadController.Index), redirect.ActionName);
        Assert.Equal(25m, service.SavedMargenBajoMax);
        Assert.Equal(40m, service.SavedMargenAltoMin);
        Assert.Equal("Configuracion de rentabilidad guardada correctamente.", controller.TempData["Success"]);
    }

    [Fact]
    public async Task Index_Post_InvalidoPorRango_VuelveAVista()
    {
        var service = new FakeConfiguracionRentabilidadService();
        var controller = CrearController(service);
        var model = new ConfiguracionRentabilidadViewModel
        {
            MargenBajoMax = -1m,
            MargenAltoMin = 40m
        };
        ValidarModelo(controller, model);

        var result = await controller.Index(model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index_tw", view.ViewName);
        Assert.Same(model, view.Model);
        Assert.Equal(0, service.SaveCount);
    }

    [Fact]
    public async Task Index_Post_MargenBajoMayorOIgualAlto_MuestraError()
    {
        var service = new FakeConfiguracionRentabilidadService();
        var controller = CrearController(service);
        var model = new ConfiguracionRentabilidadViewModel
        {
            MargenBajoMax = 40m,
            MargenAltoMin = 40m
        };

        var result = await controller.Index(model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index_tw", view.ViewName);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(
            controller.ModelState.SelectMany(x => x.Value!.Errors),
            error => error.ErrorMessage == "El margen bajo debe ser menor al margen alto.");
        Assert.Equal(0, service.SaveCount);
    }

    [Fact]
    public async Task Index_Post_ServiceException_QuedaEnModelState()
    {
        var service = new FakeConfiguracionRentabilidadService
        {
            ExceptionOnSave = new InvalidOperationException("fallo test")
        };
        var controller = CrearController(service);
        var model = new ConfiguracionRentabilidadViewModel
        {
            MargenBajoMax = 20m,
            MargenAltoMin = 35m
        };

        var result = await controller.Index(model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index_tw", view.ViewName);
        Assert.Same(model, view.Model);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(
            controller.ModelState.SelectMany(x => x.Value!.Errors),
            error => error.ErrorMessage.Contains("fallo test"));
    }

    private static ConfiguracionRentabilidadController CrearController(FakeConfiguracionRentabilidadService service)
    {
        var httpContext = new DefaultHttpContext();
        return new ConfiguracionRentabilidadController(
            service,
            NullLogger<ConfiguracionRentabilidadController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
    }

    private static void ValidarModelo(Controller controller, object model)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(model);
        Validator.TryValidateObject(model, context, validationResults, validateAllProperties: true);

        foreach (var validationResult in validationResults)
        {
            var memberName = validationResult.MemberNames.FirstOrDefault() ?? string.Empty;
            controller.ModelState.AddModelError(memberName, validationResult.ErrorMessage ?? string.Empty);
        }
    }

    private sealed class FakeConfiguracionRentabilidadService : IConfiguracionRentabilidadService
    {
        public ConfiguracionRentabilidad Configuracion { get; set; } = new();
        public Exception? ExceptionOnSave { get; set; }
        public decimal? SavedMargenBajoMax { get; private set; }
        public decimal? SavedMargenAltoMin { get; private set; }
        public int SaveCount { get; private set; }

        public Task<ConfiguracionRentabilidad> GetConfiguracionAsync()
            => Task.FromResult(Configuracion);

        public Task<ConfiguracionRentabilidad> SaveConfiguracionAsync(decimal margenBajoMax, decimal margenAltoMin)
        {
            if (ExceptionOnSave != null)
                throw ExceptionOnSave;

            SaveCount++;
            SavedMargenBajoMax = margenBajoMax;
            SavedMargenAltoMin = margenAltoMin;

            Configuracion = new ConfiguracionRentabilidad
            {
                MargenBajoMax = margenBajoMax,
                MargenAltoMin = margenAltoMin
            };

            return Task.FromResult(Configuracion);
        }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object> _data = new();

        public IDictionary<string, object> LoadTempData(HttpContext context)
            => new Dictionary<string, object>(_data);

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            _data.Clear();
            foreach (var value in values)
            {
                _data[value.Key] = value.Value;
            }
        }
    }
}
