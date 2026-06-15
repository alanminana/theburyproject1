using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.ViewModels;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

public class MercadoLibreBorradorViewModelTests
{
    [Fact]
    public void PuedePublicar_ValidadoConPublicacionErpDeshabilitada_EsFalse()
    {
        var viewModel = new MercadoLibreBorradorEditViewModel
        {
            Estado = MercadoLibreBorradorEstado.Validado,
            PermitirPublicacionDesdeErp = false
        };

        Assert.False(viewModel.PuedePublicar);
    }

    [Fact]
    public void PuedePublicar_ValidadoConPublicacionErpHabilitada_EsTrue()
    {
        var viewModel = new MercadoLibreBorradorEditViewModel
        {
            Estado = MercadoLibreBorradorEstado.Validado,
            PermitirPublicacionDesdeErp = true
        };

        Assert.True(viewModel.PuedePublicar);
    }
}
