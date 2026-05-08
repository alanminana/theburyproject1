using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Readiness de cuotas por plan.
/// Fase 15.3: entidad ProductoCondicionPagoPlan, DbSet, relaciones EF.
/// Fase 15.4: DTOs de lectura/escritura, Planes en DTOs existentes, campos en resultado del resolver.
/// </summary>
public class ProductoCondicionPagoPlanReadinessTests
{
    // --- Fase 15.3: entidad y DbSet ya existen ---

    [Fact]
    public void ModeloFase153_TieneEntidadYDbSetDePlanIndividualDeCuota()
    {
        var entitiesAssembly = typeof(ProductoCondicionPago).Assembly;
        var planEntity = entitiesAssembly.GetType("TheBuryProject.Models.Entities.ProductoCondicionPagoPlan");

        Assert.NotNull(planEntity);
        Assert.NotNull(typeof(AppDbContext).GetProperty("ProductoCondicionPagoPlanes"));
        Assert.NotNull(typeof(ProductoCondicionPago).GetProperty("Planes"));
        Assert.NotNull(typeof(ProductoCondicionPagoTarjeta).GetProperty("Planes"));
    }

    [Fact]
    public void EntidadProductoCondicionPagoPlan_TienePropiedadesEsenciales()
    {
        var t = typeof(ProductoCondicionPagoPlan);

        Assert.NotNull(t.GetProperty("ProductoCondicionPagoId"));
        Assert.NotNull(t.GetProperty("ProductoCondicionPagoTarjetaId"));
        Assert.NotNull(t.GetProperty("CantidadCuotas"));
        Assert.NotNull(t.GetProperty("Activo"));
        Assert.NotNull(t.GetProperty("AjustePorcentaje"));
        Assert.NotNull(t.GetProperty("TipoAjuste"));
        Assert.NotNull(t.GetProperty("Observaciones"));
    }

    [Fact]
    public void EnumTipoAjustePagoPlan_ExisteConValorPorcentaje()
    {
        Assert.True(Enum.IsDefined(typeof(TipoAjustePagoPlan), TipoAjustePagoPlan.Porcentaje));
    }

    // --- Fase 15.4: DTOs ahora exponen planes ---

    [Fact]
    public void DtosFase154_ExponenPlanesIndividuales()
    {
        var servicesAssembly = typeof(ProductoCondicionPagoDto).Assembly;
        var planDto = servicesAssembly.GetType("TheBuryProject.Services.Models.ProductoCondicionPagoPlanDto");
        var guardarPlanItem = servicesAssembly.GetType("TheBuryProject.Services.Models.GuardarProductoCondicionPagoPlanItem");

        Assert.NotNull(planDto);
        Assert.NotNull(guardarPlanItem);
        Assert.NotNull(typeof(ProductoCondicionPagoDto).GetProperty("Planes"));
        Assert.NotNull(typeof(ProductoCondicionPagoTarjetaDto).GetProperty("Planes"));
    }

    [Fact]
    public void DtoProductoCondicionPagoPlanDto_TienePropiedadesEsenciales()
    {
        var t = typeof(ProductoCondicionPagoPlanDto);

        Assert.NotNull(t.GetProperty("CantidadCuotas"));
        Assert.NotNull(t.GetProperty("Activo"));
        Assert.NotNull(t.GetProperty("AjustePorcentaje"));
        Assert.NotNull(t.GetProperty("TipoAjuste"));
        Assert.NotNull(t.GetProperty("Observaciones"));
    }

    [Fact]
    public void GuardarProductoCondicionPagoPlanItem_TienePropiedadesDeEscritura()
    {
        var t = typeof(GuardarProductoCondicionPagoPlanItem);

        Assert.NotNull(t.GetProperty("Id"));
        Assert.NotNull(t.GetProperty("CantidadCuotas"));
        Assert.NotNull(t.GetProperty("Activo"));
        Assert.NotNull(t.GetProperty("AjustePorcentaje"));
        Assert.NotNull(t.GetProperty("TipoAjuste"));
        Assert.NotNull(t.GetProperty("Observaciones"));
        Assert.NotNull(t.GetProperty("RowVersion"));
    }

    // --- Fase 15.4: resultado del resolver expone máximos escalares Y campos de planes ---

    [Fact]
    public void ResultadoResolverFase154_ExponeMaximosEscalaresYCamposDePlanes()
    {
        var t = typeof(CondicionesPagoCarritoResultado);

        Assert.NotNull(t.GetProperty("MaxCuotasSinInteres"));
        Assert.NotNull(t.GetProperty("MaxCuotasConInteres"));
        Assert.NotNull(t.GetProperty("MaxCuotasCredito"));
        Assert.NotNull(t.GetProperty("PlanesDisponibles"));
        Assert.NotNull(t.GetProperty("UsaPlanesEspecificos"));
        Assert.NotNull(t.GetProperty("UsaFallbackGlobalPlanes"));
    }

    [Fact]
    public void ResultadoResolverSinPlanes_TieneValoresDeDefectoCohesivos()
    {
        var resultado = new CondicionesPagoCarritoResultado();

        Assert.Empty(resultado.PlanesDisponibles);
        Assert.False(resultado.UsaPlanesEspecificos);
        Assert.False(resultado.UsaFallbackGlobalPlanes);
    }

    // --- Fase 15.7.B: DatosTarjeta y ViewModel exponen ProductoCondicionPagoPlanId ---

    [Fact]
    public void DatosTarjeta_TieneProductoCondicionPagoPlanIdNullable()
    {
        var prop = typeof(DatosTarjeta).GetProperty("ProductoCondicionPagoPlanId");

        Assert.NotNull(prop);
        Assert.Equal(typeof(int?), prop!.PropertyType);
    }

    [Fact]
    public void DatosTarjetaViewModel_TieneProductoCondicionPagoPlanIdNullable()
    {
        var t = typeof(DatosTarjetaViewModel);
        var prop = t.GetProperty("ProductoCondicionPagoPlanId");

        Assert.NotNull(prop);
        Assert.Equal(typeof(int?), prop!.PropertyType);
    }

    [Fact]
    public void DatosTarjeta_PlanIdNulloPorDefecto()
    {
        var entity = new DatosTarjeta { NombreTarjeta = "Test", TipoTarjeta = TipoTarjeta.Credito };

        Assert.Null(entity.ProductoCondicionPagoPlanId);
    }
}
