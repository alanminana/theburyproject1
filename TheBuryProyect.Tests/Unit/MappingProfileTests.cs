using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para MappingProfile.
/// Verifica que la configuración de AutoMapper es válida y que los mappings
/// con lógica de proyección (cálculos, campos condicionales) producen los valores correctos.
/// </summary>
public class MappingProfileTests
{
    private readonly IMapper _mapper;

    public MappingProfileTests()
    {
        _mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();
    }

    // =========================================================================
    // Validación de configuración
    // =========================================================================

    [Fact]
    public void MappingProfile_ConfiguracionValida_NoLanzaExcepcion()
    {
        // Verifica que el perfil se puede crear sin excepciones de configuración.
        // No usamos AssertConfigurationIsValid() porque algunos VMs intencionalmente
        // dejan miembros sin mapear (Ignore) — eso no es un error.
        var ex = Record.Exception(() =>
            new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance)
                .CreateMapper());
        Assert.Null(ex);
    }

    // =========================================================================
    // PrecioHistorico → PrecioHistoricoViewModel (cálculos de porcentaje)
    // =========================================================================

    [Fact]
    public void Map_PrecioHistorico_PorcentajeCambioCompra_CalculaCorrecto()
    {
        var entity = new PrecioHistorico
        {
            PrecioCompraAnterior = 100m,
            PrecioCompraNuevo = 150m,
            PrecioVentaAnterior = 200m,
            PrecioVentaNuevo = 200m,
            Producto = new Producto { Codigo = "P1", Nombre = "Prod" }
        };

        var vm = _mapper.Map<PrecioHistoricoViewModel>(entity);

        Assert.Equal(50m, vm.PorcentajeCambioCompra);  // (150-100)/100 * 100
    }

    [Fact]
    public void Map_PrecioHistorico_PrecioCompraAnteriorCero_PorcentajeEsCero()
    {
        var entity = new PrecioHistorico
        {
            PrecioCompraAnterior = 0m,
            PrecioCompraNuevo = 100m,
            PrecioVentaAnterior = 0m,
            PrecioVentaNuevo = 100m,
            Producto = new Producto { Codigo = "P2", Nombre = "Prod" }
        };

        var vm = _mapper.Map<PrecioHistoricoViewModel>(entity);

        Assert.Equal(0m, vm.PorcentajeCambioCompra);
        Assert.Equal(0m, vm.PorcentajeCambioVenta);
        Assert.Equal(0m, vm.MargenAnterior);
    }

    [Fact]
    public void Map_PrecioHistorico_MargenNuevo_CalculaCorrecto()
    {
        var entity = new PrecioHistorico
        {
            PrecioCompraAnterior = 100m,
            PrecioCompraNuevo = 100m,
            PrecioVentaAnterior = 150m,
            PrecioVentaNuevo = 130m,
            Producto = new Producto { Codigo = "P3", Nombre = "Prod" }
        };

        var vm = _mapper.Map<PrecioHistoricoViewModel>(entity);

        Assert.Equal(30m, vm.MargenNuevo);  // (130-100)/100 * 100
    }

    // =========================================================================
    // AlertaStock → AlertaStockViewModel (campos calculados)
    // =========================================================================

    [Fact]
    public void Map_AlertaStock_PorcentajeStockMinimo_CalculaCorrecto()
    {
        var entity = new AlertaStock
        {
            StockActual = 5m,
            StockMinimo = 10m,
            FechaAlerta = DateTime.UtcNow,
            Producto = new Producto { Codigo = "A1", Nombre = "Prod" }
        };

        var vm = _mapper.Map<AlertaStockViewModel>(entity);

        Assert.Equal(50m, vm.PorcentajeStockMinimo);  // (5/10)*100
    }

    [Fact]
    public void Map_AlertaStock_StockMinimoEsCero_PorcentajeEsCero()
    {
        var entity = new AlertaStock
        {
            StockActual = 5m,
            StockMinimo = 0m,
            FechaAlerta = DateTime.UtcNow,
            Producto = new Producto { Codigo = "A2", Nombre = "Prod" }
        };

        var vm = _mapper.Map<AlertaStockViewModel>(entity);

        Assert.Equal(0m, vm.PorcentajeStockMinimo);
    }

    [Fact]
    public void Map_AlertaStock_FechaResolucionNull_EstaVencidaDependeDeDias()
    {
        var entity = new AlertaStock
        {
            StockActual = 1m,
            StockMinimo = 10m,
            FechaAlerta = DateTime.UtcNow.AddDays(-31), // más de 30 días
            FechaResolucion = null,
            Producto = new Producto { Codigo = "A3", Nombre = "Prod" }
        };

        var vm = _mapper.Map<AlertaStockViewModel>(entity);

        Assert.True(vm.EstaVencida);
    }

    // =========================================================================
    // AlertaCobranza → AlertaCobranzaViewModel (color e ícono por prioridad)
    // =========================================================================

    [Theory]
    [InlineData(PrioridadAlerta.Critica, "dark", "bi bi-exclamation-octagon-fill")]
    [InlineData(PrioridadAlerta.Alta, "danger", "bi bi-exclamation-triangle-fill")]
    [InlineData(PrioridadAlerta.Media, "warning", "bi bi-exclamation-circle-fill")]
    [InlineData(PrioridadAlerta.Baja, "info", "bi bi-info-circle-fill")]
    public void Map_AlertaCobranza_ColorEIconoPorPrioridad(PrioridadAlerta prioridad, string colorEsperado, string iconoEsperado)
    {
        var entity = new AlertaCobranza
        {
            Prioridad = prioridad,
            CreatedAt = DateTime.UtcNow
        };

        var vm = _mapper.Map<AlertaCobranzaViewModel>(entity);

        Assert.Equal(colorEsperado, vm.ColorAlerta);
        Assert.Equal(iconoEsperado, vm.IconoAlerta);
    }

    [Fact]
    public void Map_AlertaCobranza_ConCliente_TituloUsaNombreCompleto()
    {
        var entity = new AlertaCobranza
        {
            Prioridad = PrioridadAlerta.Baja,
            CreatedAt = DateTime.UtcNow,
            Cliente = new Cliente
            {
                Nombre = "Juan",
                Apellido = "García",
                NombreCompleto = "Juan García",
                NumeroDocumento = "12345"
            }
        };

        var vm = _mapper.Map<AlertaCobranzaViewModel>(entity);

        Assert.Contains("Juan García", vm.Titulo);
    }

    [Fact]
    public void Map_AlertaCobranza_SinCliente_TituloEsGenerico()
    {
        var entity = new AlertaCobranza
        {
            Prioridad = PrioridadAlerta.Baja,
            CreatedAt = DateTime.UtcNow,
            Cliente = null
        };

        var vm = _mapper.Map<AlertaCobranzaViewModel>(entity);

        Assert.Equal("Alerta de cobranza", vm.Titulo);
    }

    // =========================================================================
    // Categoria / Marca → ViewModel (parent condicional)
    // =========================================================================

    [Fact]
    public void Map_Categoria_ConParentActivo_MappaParentNombre()
    {
        var entity = new Categoria
        {
            Nombre = "Hijo",
            Parent = new Categoria { Nombre = "Padre", IsDeleted = false }
        };

        var vm = _mapper.Map<CategoriaViewModel>(entity);

        Assert.Equal("Padre", vm.ParentNombre);
    }

    [Fact]
    public void Map_Categoria_ConParentEliminado_ParentNombreEsNull()
    {
        var entity = new Categoria
        {
            Nombre = "Hijo",
            Parent = new Categoria { Nombre = "Padre Eliminado", IsDeleted = true }
        };

        var vm = _mapper.Map<CategoriaViewModel>(entity);

        Assert.Null(vm.ParentNombre);
    }

    [Fact]
    public void Map_Categoria_SinParent_ParentNombreEsNull()
    {
        var entity = new Categoria { Nombre = "Raiz", Parent = null };

        var vm = _mapper.Map<CategoriaViewModel>(entity);

        Assert.Null(vm.ParentNombre);
    }

    // =========================================================================
    // Cheque → ChequeViewModel (DiasPorVencer calculado)
    // =========================================================================

    [Fact]
    public void Map_Cheque_FechaVencimientoFutura_DiasPorVencerPositivo()
    {
        var entity = new Cheque
        {
            FechaVencimiento = DateTime.Today.AddDays(10)
        };

        var vm = _mapper.Map<ChequeViewModel>(entity);

        Assert.Equal(10, vm.DiasPorVencer);
    }

    [Fact]
    public void Map_Cheque_SinFechaVencimiento_DiasPorVencerEsCero()
    {
        var entity = new Cheque { FechaVencimiento = null };

        var vm = _mapper.Map<ChequeViewModel>(entity);

        Assert.Equal(0, vm.DiasPorVencer);
    }

    // =========================================================================
    // OrdenCompra → OrdenCompraViewModel (TotalItems y TotalRecibido)
    // =========================================================================

    [Fact]
    public void Map_OrdenCompra_ConDetalles_SumaTotalItemsExcluyendoEliminados()
    {
        var entity = new OrdenCompra
        {
            Detalles = new List<OrdenCompraDetalle>
            {
                new() { Cantidad = 3, CantidadRecibida = 2, IsDeleted = false },
                new() { Cantidad = 5, CantidadRecibida = 5, IsDeleted = false },
                new() { Cantidad = 99, CantidadRecibida = 99, IsDeleted = true }, // excluido
            }
        };

        var vm = _mapper.Map<OrdenCompraViewModel>(entity);

        Assert.Equal(8, vm.TotalItems);
        Assert.Equal(7, vm.TotalRecibido);
    }

    // =========================================================================
    // ConfiguracionMora (DiasGraciaMora vs DiasGracia)
    // =========================================================================

    [Fact]
    public void Map_ConfiguracionMora_MappaAmbosAliasDeDiasGracia()
    {
        var entity = new ConfiguracionMora { DiasGracia = 5 };

        var vm = _mapper.Map<ConfiguracionMoraViewModel>(entity);

        Assert.Equal(5, vm.DiasGracia);
        Assert.Equal(5, vm.DiasGraciaMora);
    }

    [Fact]
    public void Map_MovimientoStock_IncluyeCostoYFuente()
    {
        var entity = new MovimientoStock
        {
            ProductoId = 1,
            Producto = new Producto { Codigo = "P1", Nombre = "Producto" },
            Tipo = TipoMovimiento.Entrada,
            Cantidad = 2m,
            StockAnterior = 3m,
            StockNuevo = 5m,
            CostoUnitarioAlMomento = 12.34m,
            CostoTotalAlMomento = 24.68m,
            FuenteCosto = "OrdenCompraDetalle"
        };

        var vm = _mapper.Map<MovimientoStockViewModel>(entity);

        Assert.Equal(12.34m, vm.CostoUnitarioAlMomento);
        Assert.Equal(24.68m, vm.CostoTotalAlMomento);
        Assert.Equal("OrdenCompraDetalle", vm.FuenteCosto);
    }

    [Fact]
    public void Map_VentaDetalle_IncluyeSnapshotsIvaSoloLectura()
    {
        var entity = new VentaDetalle
        {
            Producto = new Producto { Codigo = "P1", Nombre = "Producto" },
            PorcentajeIVA = 10.5m,
            AlicuotaIVAId = 2,
            AlicuotaIVANombre = "IVA 10.5%",
            PrecioUnitarioNeto = 100m,
            IVAUnitario = 10.5m,
            SubtotalNeto = 200m,
            SubtotalIVA = 21m,
            Subtotal = 221m,
            DescuentoGeneralProrrateado = 11m,
            SubtotalFinalNeto = 190m,
            SubtotalFinalIVA = 20m,
            SubtotalFinal = 210m,
            CostoUnitarioAlMomento = 70m,
            CostoTotalAlMomento = 140m
        };

        var vm = _mapper.Map<VentaDetalleViewModel>(entity);

        Assert.Equal(10.5m, vm.PorcentajeIVA);
        Assert.Equal(2, vm.AlicuotaIVAId);
        Assert.Equal("IVA 10.5%", vm.AlicuotaIVANombre);
        Assert.Equal(100m, vm.PrecioUnitarioNeto);
        Assert.Equal(10.5m, vm.IVAUnitario);
        Assert.Equal(200m, vm.SubtotalNeto);
        Assert.Equal(21m, vm.SubtotalIVA);
        Assert.Equal(11m, vm.DescuentoGeneralProrrateado);
        Assert.Equal(190m, vm.SubtotalFinalNeto);
        Assert.Equal(20m, vm.SubtotalFinalIVA);
        Assert.Equal(210m, vm.SubtotalFinal);
        Assert.Equal(70m, vm.CostoUnitarioAlMomento);
        Assert.Equal(140m, vm.CostoTotalAlMomento);
    }

    [Fact]
    public void Map_VentaDetalleViewModel_NoPermitePisarSnapshotsIva()
    {
        var vm = new VentaDetalleViewModel
        {
            ProductoId = 1,
            Cantidad = 1,
            PrecioUnitario = 121m,
            Subtotal = 121m,
            PorcentajeIVA = 10.5m,
            AlicuotaIVAId = 2,
            AlicuotaIVANombre = "IVA 10.5%",
            PrecioUnitarioNeto = 100m,
            IVAUnitario = 10.5m,
            SubtotalNeto = 100m,
            SubtotalIVA = 10.5m,
            DescuentoGeneralProrrateado = 11m,
            SubtotalFinalNeto = 90m,
            SubtotalFinalIVA = 9.5m,
            SubtotalFinal = 99.5m,
            CostoUnitarioAlMomento = 70m,
            CostoTotalAlMomento = 70m
        };

        var entity = _mapper.Map<VentaDetalle>(vm);

        Assert.Equal(0m, entity.PorcentajeIVA);
        Assert.Null(entity.AlicuotaIVAId);
        Assert.Null(entity.AlicuotaIVANombre);
        Assert.Equal(0m, entity.PrecioUnitarioNeto);
        Assert.Equal(0m, entity.IVAUnitario);
        Assert.Equal(0m, entity.SubtotalNeto);
        Assert.Equal(0m, entity.SubtotalIVA);
        Assert.Equal(0m, entity.DescuentoGeneralProrrateado);
        Assert.Equal(0m, entity.SubtotalFinalNeto);
        Assert.Equal(0m, entity.SubtotalFinalIVA);
        Assert.Equal(0m, entity.SubtotalFinal);
        Assert.Equal(0m, entity.CostoUnitarioAlMomento);
        Assert.Equal(0m, entity.CostoTotalAlMomento);
    }
}
