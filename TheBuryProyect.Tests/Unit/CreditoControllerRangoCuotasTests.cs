using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos.
///
/// El método es lógica pura: recibe objetos ya cargados y devuelve (Min, Max, Descripcion).
/// No accede a DB. No requiere ningún setup de infraestructura.
///
/// Todos los valores esperados se verificaron contra el switch original
/// en ConfigurarVenta POST antes de la extracción.
/// </summary>
public class CreditoControllerRangoCuotasTests
{
    // ---------------------------------------------------------------------------
    // Manual
    // ---------------------------------------------------------------------------

    [Fact]
    public void Manual_SinEntidades_DevuelveRangoMaximo()
    {
        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.Manual, perfil: null, cliente: null);

        Assert.Equal(1, min);
        Assert.Equal(120, max);
        Assert.Equal("Manual", desc);
    }

    // ---------------------------------------------------------------------------
    // Global
    // ---------------------------------------------------------------------------

    [Fact]
    public void Global_SinEntidades_DevuelveRangoGlobal()
    {
        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.Global, perfil: null, cliente: null);

        Assert.Equal(1, min);
        Assert.Equal(24, max);
        Assert.Equal("Global", desc);
    }

    // ---------------------------------------------------------------------------
    // AutomaticoPorCliente sin perfil → cae en default Global
    // ---------------------------------------------------------------------------

    [Fact]
    public void AutomaticoPorCliente_SinPerfil_DevuelveRangoGlobal()
    {
        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.AutomaticoPorCliente, perfil: null, cliente: null);

        Assert.Equal(1, min);
        Assert.Equal(24, max);
        Assert.Equal("Global", desc);
    }

    // ---------------------------------------------------------------------------
    // UsarPerfil con perfil válido
    // ---------------------------------------------------------------------------

    [Fact]
    public void UsarPerfil_ConPerfilValido_UsaRangoYNombreDelPerfil()
    {
        var perfil = new PerfilCredito
        {
            Nombre = "Estándar",
            MinCuotas = 3,
            MaxCuotas = 36,
            TasaMensual = 5m,
            RowVersion = new byte[8]
        };

        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.UsarPerfil, perfil, cliente: null);

        Assert.Equal(3, min);
        Assert.Equal(36, max);
        Assert.Equal("Perfil 'Estándar'", desc);
    }

    [Fact]
    public void AutomaticoPorCliente_ConPerfil_UsaRangoDelPerfil()
    {
        var perfil = new PerfilCredito
        {
            Nombre = "Conservador",
            MinCuotas = 1,
            MaxCuotas = 12,
            TasaMensual = 8m,
            RowVersion = new byte[8]
        };

        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.AutomaticoPorCliente, perfil, cliente: null);

        Assert.Equal(1, min);
        Assert.Equal(12, max);
        Assert.Equal("Perfil 'Conservador'", desc);
    }

    // ---------------------------------------------------------------------------
    // UsarCliente con CuotasMaximasPersonalizadas
    // ---------------------------------------------------------------------------

    [Fact]
    public void UsarCliente_ConCuotasMaximasPersonalizadas_UsaCuotasDelCliente()
    {
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Cliente",
            TipoDocumento = "DNI",
            NumeroDocumento = "30000001",
            CuotasMaximasPersonalizadas = 18,
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            RowVersion = new byte[8]
        };

        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.UsarCliente, perfil: null, cliente);

        Assert.Equal(1, min);
        Assert.Equal(18, max);
        Assert.Equal("Cliente", desc);
    }

    // ---------------------------------------------------------------------------
    // UsarCliente sin config personalizada
    // ---------------------------------------------------------------------------

    [Fact]
    public void UsarCliente_SinCuotasPersonalizadas_DevuelveFallback24()
    {
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Cliente",
            TipoDocumento = "DNI",
            NumeroDocumento = "30000002",
            CuotasMaximasPersonalizadas = null,
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            RowVersion = new byte[8]
        };

        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.UsarCliente, perfil: null, cliente);

        Assert.Equal(1, min);
        Assert.Equal(24, max);
        Assert.Equal("Cliente (sin config)", desc);
    }

    [Fact]
    public void UsarCliente_ClienteNull_DevuelveFallback24()
    {
        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.UsarCliente, perfil: null, cliente: null);

        Assert.Equal(1, min);
        Assert.Equal(24, max);
        Assert.Equal("Cliente (sin config)", desc);
    }
}
