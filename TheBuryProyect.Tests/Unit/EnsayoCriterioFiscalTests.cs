using Microsoft.Extensions.Hosting;
using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

/// <summary>P-02: la activación de ensayo de CriterioFiscalV2 está cerrada por defecto y nunca puede afectar a producción.</summary>
public class EnsayoCriterioFiscalTests
{
    private sealed class Entorno(string nombre) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nombre;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SinClave_NoActivaNada_EnCualquierEntorno(string? valor)
    {
        Assert.Null(EnsayoCriterioFiscal.Resolver(new Entorno("Production"), valor));
        Assert.Null(EnsayoCriterioFiscal.Resolver(new Entorno("Development"), valor));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void ConClaveFueraDeDevelopment_AbortaElArranque_NoSeIgnoraEnSilencio(string entorno)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EnsayoCriterioFiscal.Resolver(new Entorno(entorno), "2"));
        Assert.Contains("Development", ex.Message);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    public void EnDevelopment_AceptaSoloVersionesRegistradas(string valor, int esperado) =>
        Assert.Equal(esperado, EnsayoCriterioFiscal.Resolver(new Entorno("Development"), valor));

    [Theory]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("dos")]
    public void EnDevelopment_RechazaVersionesNoRegistradas(string valor) =>
        Assert.Throws<InvalidOperationException>(() => EnsayoCriterioFiscal.Resolver(new Entorno("Development"), valor));

    [Fact]
    public void LaVersionVigenteDeProduccionSigueSiendo1()
    {
        // Nada en este proceso de prueba llamó a ActivarEnsayo: la vigente es la inicial.
        Assert.Equal(CriterioFiscalRegistry.VersionInicial, CriterioFiscalRegistry.VersionVigente);
        Assert.Equal(1, CriterioFiscalRegistry.VersionVigente);
    }
}
