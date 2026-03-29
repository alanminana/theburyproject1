using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos.
/// Función pura — no requiere base de datos ni infraestructura.
/// Cubre todos los métodos de cálculo y sus combinaciones.
/// </summary>
public class CreditoConfiguracionHelperTests
{
    // =========================================================================
    // MetodoCalculoCredito.Manual
    // =========================================================================

    [Fact]
    public void ResolverRango_Manual_RetornaRangoAmplio()
    {
        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.Manual, null, null);

        Assert.Equal(1, min);
        Assert.Equal(120, max);
        Assert.Equal("Manual", desc);
    }

    // =========================================================================
    // MetodoCalculoCredito.UsarPerfil
    // =========================================================================

    [Fact]
    public void ResolverRango_UsarPerfil_ConPerfil_RetornaRangoDePerfil()
    {
        var perfil = new PerfilCredito { Nombre = "Premium", MinCuotas = 3, MaxCuotas = 48 };

        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.UsarPerfil, perfil, null);

        Assert.Equal(3, min);
        Assert.Equal(48, max);
        Assert.Contains("Premium", desc);
    }

    [Fact]
    public void ResolverRango_UsarPerfil_SinPerfil_RetornaRangoGlobal()
    {
        var (min, max, _) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.UsarPerfil, null, null);

        Assert.Equal(1, min);
        Assert.Equal(120, max);
    }

    // =========================================================================
    // MetodoCalculoCredito.AutomaticoPorCliente
    // =========================================================================

    [Fact]
    public void ResolverRango_AutomaticoPorCliente_ConPerfil_RetornaRangoDePerfil()
    {
        var perfil = new PerfilCredito { Nombre = "Estandar", MinCuotas = 1, MaxCuotas = 24 };

        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.AutomaticoPorCliente, perfil, null);

        Assert.Equal(1, min);
        Assert.Equal(24, max);
        Assert.Contains("Estandar", desc);
    }

    [Fact]
    public void ResolverRango_AutomaticoPorCliente_SinPerfil_RetornaRangoGlobal()
    {
        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.AutomaticoPorCliente, null, null);

        Assert.Equal(1, min);
        Assert.Equal(24, max);
        Assert.Equal("Global", desc);
    }

    // =========================================================================
    // MetodoCalculoCredito.UsarCliente
    // =========================================================================

    [Fact]
    public void ResolverRango_UsarCliente_ConCuotasPersonalizadas_RetornaRangoDeCliente()
    {
        var cliente = new Cliente { CuotasMaximasPersonalizadas = 36 };

        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.UsarCliente, null, cliente);

        Assert.Equal(1, min);
        Assert.Equal(36, max);
        Assert.Equal("Cliente", desc);
    }

    [Fact]
    public void ResolverRango_UsarCliente_SinCuotasPersonalizadas_RetornaDefault()
    {
        var cliente = new Cliente { CuotasMaximasPersonalizadas = null };

        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.UsarCliente, null, cliente);

        Assert.Equal(1, min);
        Assert.Equal(24, max);
        Assert.Contains("sin config", desc);
    }

    [Fact]
    public void ResolverRango_UsarCliente_ClienteNull_RetornaDefault()
    {
        var (min, max, _) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.UsarCliente, null, null);

        Assert.Equal(1, min);
        Assert.Equal(24, max);
    }

    // =========================================================================
    // MetodoCalculoCredito.Global
    // =========================================================================

    [Fact]
    public void ResolverRango_Global_RetornaRangoEstandar()
    {
        var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.Global, null, null);

        Assert.Equal(1, min);
        Assert.Equal(24, max);
        Assert.Equal("Global", desc);
    }

    // =========================================================================
    // Descripción contiene nombre del perfil
    // =========================================================================

    [Fact]
    public void ResolverRango_UsarPerfil_DescripcionContienNombrePerfil()
    {
        var perfil = new PerfilCredito { Nombre = "VIP Cliente", MinCuotas = 6, MaxCuotas = 60 };

        var (_, _, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito.UsarPerfil, perfil, null);

        Assert.Contains("VIP Cliente", desc);
    }
}
