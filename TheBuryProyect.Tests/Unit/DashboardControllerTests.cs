using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public class DashboardControllerTests
{
    private sealed class ServicioDashboardStub(Func<Task<DashboardViewModel>> factory) : IDashboardService
    {
        public Task<DashboardViewModel> GetDashboardDataAsync() => factory();
    }

    [Fact]
    public async Task Index_CuandoElServicioFalla_MarcaCargaFallidaParaNoMostrarTodoAlDia()
    {
        var controller = new DashboardController(
            new ServicioDashboardStub(() => throw new InvalidOperationException("detalle interno")),
            NullLogger<DashboardController>.Instance);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(true, controller.ViewData["CargaFallida"]);
        Assert.IsType<DashboardViewModel>(view.Model);
    }

    [Fact]
    public async Task Index_CuandoElServicioResponde_NoMarcaCargaFallida()
    {
        var controller = new DashboardController(
            new ServicioDashboardStub(() => Task.FromResult(new DashboardViewModel())),
            NullLogger<DashboardController>.Instance);

        await controller.Index();

        Assert.Null(controller.ViewData["CargaFallida"]);
    }
}
