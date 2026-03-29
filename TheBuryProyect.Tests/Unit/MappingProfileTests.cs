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
}
