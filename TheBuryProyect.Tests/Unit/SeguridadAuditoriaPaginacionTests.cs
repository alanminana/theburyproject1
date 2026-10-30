using TheBuryProject.Controllers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>VALID-L12 — la paginación de la auditoría no debe desbordar int con páginas enormes.</summary>
public class SeguridadAuditoriaPaginacionTests
{
    [Fact]
    public void PaginaMaxima_NoDesbordaElSalto()
    {
        var skip = (long)(SeguridadController.PaginaAuditoriaMaxima - 1) * SeguridadAuditoriaViewModel.TamanoPaginaDefault;

        Assert.True(skip <= int.MaxValue);
    }

    [Fact]
    public void PaginaMaxima_SinElTopeDesbordaria()
    {
        var sinTope = (long)(int.MaxValue - 1) * SeguridadAuditoriaViewModel.TamanoPaginaDefault;

        Assert.True(sinTope > int.MaxValue);
    }
}
