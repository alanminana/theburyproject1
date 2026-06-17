using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.ViewModels;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

public class MercadoLibreBorradorViewModelTests
{
    [Fact]
    public void PuedeSimular_BorradorValidado_EsTrueSinPermisoNiCuenta()
    {
        // La simulación es el default: no exige permiso ni cuenta.
        var viewModel = new MercadoLibreBorradorEditViewModel
        {
            Estado = MercadoLibreBorradorEstado.Validado,
            PermitirPublicacionDesdeErp = false,
            CuentaConectada = false
        };

        Assert.True(viewModel.PuedeSimular);
        Assert.True(viewModel.PuedePublicar); // el botón queda habilitado (simula por defecto)
    }

    [Fact]
    public void PuedeSimular_BorradorNoValidado_EsFalse()
    {
        var viewModel = new MercadoLibreBorradorEditViewModel
        {
            Estado = MercadoLibreBorradorEstado.Borrador
        };

        Assert.False(viewModel.PuedeSimular);
        Assert.False(viewModel.PuedePublicar);
    }

    [Fact]
    public void PuedePublicarReal_ValidadoSinPermiso_EsFalse()
    {
        var viewModel = new MercadoLibreBorradorEditViewModel
        {
            Estado = MercadoLibreBorradorEstado.Validado,
            PermitirPublicacionDesdeErp = false,
            CuentaConectada = true
        };

        Assert.False(viewModel.PuedePublicarReal);
    }

    [Fact]
    public void PuedePublicarReal_ValidadoSinCuenta_EsFalse()
    {
        var viewModel = new MercadoLibreBorradorEditViewModel
        {
            Estado = MercadoLibreBorradorEstado.Validado,
            PermitirPublicacionDesdeErp = true,
            CuentaConectada = false
        };

        Assert.False(viewModel.PuedePublicarReal);
    }

    [Fact]
    public void PuedePublicarReal_ValidadoConPermisoYCuenta_EsTrue()
    {
        var viewModel = new MercadoLibreBorradorEditViewModel
        {
            Estado = MercadoLibreBorradorEstado.Validado,
            PermitirPublicacionDesdeErp = true,
            CuentaConectada = true
        };

        Assert.True(viewModel.PuedePublicarReal);
    }
}
