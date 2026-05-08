using System.Reflection;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Contratos de Fase 16.2: verifica que VentaDetalle y VentaDetalleViewModel
/// exponen los campos de pago por ítem como nullable y que el flujo sin esos
/// campos no se rompe.
/// </summary>
public class VentaDetallePagoPorItemContractTests
{
    // =========================================================================
    // VentaDetalle — campos nullable
    // =========================================================================

    [Fact]
    public void VentaDetalle_TipoPago_EsNullable()
    {
        var prop = typeof(VentaDetalle).GetProperty(nameof(VentaDetalle.TipoPago))!;
        Assert.Equal(typeof(TipoPago?), prop.PropertyType);
    }

    [Fact]
    public void VentaDetalle_ProductoCondicionPagoPlanId_EsNullable()
    {
        var prop = typeof(VentaDetalle).GetProperty(nameof(VentaDetalle.ProductoCondicionPagoPlanId))!;
        Assert.Equal(typeof(int?), prop.PropertyType);
    }

    [Fact]
    public void VentaDetalle_PorcentajeAjustePlanAplicado_EsNullable()
    {
        var prop = typeof(VentaDetalle).GetProperty(nameof(VentaDetalle.PorcentajeAjustePlanAplicado))!;
        Assert.Equal(typeof(decimal?), prop.PropertyType);
    }

    [Fact]
    public void VentaDetalle_MontoAjustePlanAplicado_EsNullable()
    {
        var prop = typeof(VentaDetalle).GetProperty(nameof(VentaDetalle.MontoAjustePlanAplicado))!;
        Assert.Equal(typeof(decimal?), prop.PropertyType);
    }

    [Fact]
    public void VentaDetalle_ProductoCondicionPagoPlan_EsNavigationNullable()
    {
        var prop = typeof(VentaDetalle).GetProperty(nameof(VentaDetalle.ProductoCondicionPagoPlan))!;
        Assert.Equal(typeof(ProductoCondicionPagoPlan), prop.PropertyType);
        // La propiedad existe y no es un tipo valor → admite null en tiempo de ejecución
        Assert.True(prop.PropertyType.IsClass);
    }

    // =========================================================================
    // VentaDetalleViewModel — campos nullable
    // =========================================================================

    [Fact]
    public void VentaDetalleViewModel_TipoPago_EsNullable()
    {
        var prop = typeof(VentaDetalleViewModel).GetProperty(nameof(VentaDetalleViewModel.TipoPago))!;
        Assert.Equal(typeof(TipoPago?), prop.PropertyType);
    }

    [Fact]
    public void VentaDetalleViewModel_ProductoCondicionPagoPlanId_EsNullable()
    {
        var prop = typeof(VentaDetalleViewModel).GetProperty(nameof(VentaDetalleViewModel.ProductoCondicionPagoPlanId))!;
        Assert.Equal(typeof(int?), prop.PropertyType);
    }

    [Fact]
    public void VentaDetalleViewModel_PorcentajeAjustePlanAplicado_EsNullable()
    {
        var prop = typeof(VentaDetalleViewModel).GetProperty(nameof(VentaDetalleViewModel.PorcentajeAjustePlanAplicado))!;
        Assert.Equal(typeof(decimal?), prop.PropertyType);
    }

    [Fact]
    public void VentaDetalleViewModel_MontoAjustePlanAplicado_EsNullable()
    {
        var prop = typeof(VentaDetalleViewModel).GetProperty(nameof(VentaDetalleViewModel.MontoAjustePlanAplicado))!;
        Assert.Equal(typeof(decimal?), prop.PropertyType);
    }

    [Fact]
    public void VentaDetalleViewModel_ResumenFormaPago_EsNullable()
    {
        var prop = typeof(VentaDetalleViewModel).GetProperty(nameof(VentaDetalleViewModel.ResumenFormaPago))!;
        Assert.Equal(typeof(string), prop.PropertyType);
    }

    // =========================================================================
    // Compatibilidad — instanciar sin campos de pago por ítem
    // =========================================================================

    [Fact]
    public void VentaDetalle_SinCamposPago_InstanciaValida()
    {
        var detalle = new VentaDetalle
        {
            VentaId = 1,
            ProductoId = 1,
            Cantidad = 2,
            PrecioUnitario = 100m,
            Subtotal = 200m
        };

        Assert.Null(detalle.TipoPago);
        Assert.Null(detalle.ProductoCondicionPagoPlanId);
        Assert.Null(detalle.PorcentajeAjustePlanAplicado);
        Assert.Null(detalle.MontoAjustePlanAplicado);
        Assert.Null(detalle.ProductoCondicionPagoPlan);
    }

    [Fact]
    public void VentaDetalleViewModel_SinCamposPago_InstanciaValida()
    {
        var vm = new VentaDetalleViewModel
        {
            ProductoId = 1,
            Cantidad = 1,
            PrecioUnitario = 50m
        };

        Assert.Null(vm.TipoPago);
        Assert.Null(vm.ProductoCondicionPagoPlanId);
        Assert.Null(vm.PorcentajeAjustePlanAplicado);
        Assert.Null(vm.MontoAjustePlanAplicado);
        Assert.Null(vm.ResumenFormaPago);
    }

    // =========================================================================
    // Los campos nuevos no tienen [Required] en la entidad
    // =========================================================================

    [Theory]
    [InlineData(nameof(VentaDetalle.TipoPago))]
    [InlineData(nameof(VentaDetalle.ProductoCondicionPagoPlanId))]
    [InlineData(nameof(VentaDetalle.PorcentajeAjustePlanAplicado))]
    [InlineData(nameof(VentaDetalle.MontoAjustePlanAplicado))]
    public void VentaDetalle_CamposPago_NoTienenRequired(string propertyName)
    {
        var prop = typeof(VentaDetalle).GetProperty(propertyName)!;
        var required = prop.GetCustomAttribute<System.ComponentModel.DataAnnotations.RequiredAttribute>();
        Assert.Null(required);
    }
}
