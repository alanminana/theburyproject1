using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public sealed class CreditoRangoProductoServiceTests
{
    [Fact]
    public async Task ProductoSinRestriccion_ConservaRangoBase()
    {
        var service = CrearService(new CondicionesPagoCarritoResultado
        {
            Permitido = true
        });

        var resultado = await service.ResolverAsync(VentaConProductos((7, "Producto libre")), TipoPago.CreditoPersonal, 1, 24);

        Assert.Equal(1, resultado.Min);
        Assert.Equal(24, resultado.Max);
        Assert.Equal(24, resultado.MaxBase);
        Assert.Null(resultado.MaxProducto);
        Assert.Null(resultado.ProductoIdRestrictivo);
        Assert.Null(resultado.Error);
    }

    [Fact]
    public async Task ProductoBloqueante_BloqueaRangoConMensajeEquivalente()
    {
        var service = CrearService(new CondicionesPagoCarritoResultado
        {
            Permitido = false,
            ProductoIdsBloqueantes = new[] { 7 }
        });

        var resultado = await service.ResolverAsync(VentaConProductos((7, "Producto bloqueante")), TipoPago.CreditoPersonal, 1, 24);

        Assert.Equal(1, resultado.Min);
        Assert.Equal(24, resultado.Max);
        Assert.Equal(24, resultado.MaxBase);
        Assert.Null(resultado.ProductoIdRestrictivo);
        Assert.Contains("No se puede configurar", resultado.Error);
        Assert.Contains("Producto bloqueante bloquea el medio de pago", resultado.Error);
    }

    [Fact]
    public async Task MaxCuotasCreditoMenor_ReduceMaximoEfectivoYConservaProductoRestrictivo()
    {
        var service = CrearService(new CondicionesPagoCarritoResultado
        {
            Permitido = true,
            MaxCuotasCredito = 6,
            ProductoIdsRestrictivos = new[] { 7 }
        });

        var resultado = await service.ResolverAsync(VentaConProductos((7, "Producto restrictivo")), TipoPago.CreditoPersonal, 1, 24);

        Assert.Equal(6, resultado.Max);
        Assert.Equal(24, resultado.MaxBase);
        Assert.Equal(6, resultado.MaxProducto);
        Assert.Equal(7, resultado.ProductoIdRestrictivo);
        Assert.Equal("Producto restrictivo", resultado.ProductoRestrictivoNombre);
        Assert.Equal("Límite por producto: hasta 6 cuotas.", resultado.DescripcionProducto);
    }

    [Fact]
    public async Task MaxCuotasCreditoMayor_NoAmpliaRangoBase()
    {
        var service = CrearService(new CondicionesPagoCarritoResultado
        {
            Permitido = true,
            MaxCuotasCredito = 36,
            ProductoIdsRestrictivos = new[] { 7 }
        });

        var resultado = await service.ResolverAsync(VentaConProductos((7, "Producto amplio")), TipoPago.CreditoPersonal, 1, 24);

        Assert.Equal(24, resultado.Max);
        Assert.Equal(24, resultado.MaxBase);
        Assert.Equal(36, resultado.MaxProducto);
        Assert.Equal(7, resultado.ProductoIdRestrictivo);
    }

    [Fact]
    public async Task CarritoMultiproducto_UsaMenorMaxCuotasCreditoDelResolver()
    {
        var resolver = new StubCondicionesPagoCarritoResolver(new CondicionesPagoCarritoResultado
        {
            Permitido = true,
            MaxCuotasCredito = 6,
            ProductoIdsRestrictivos = new[] { 9 }
        });
        var service = new CreditoRangoProductoService(resolver);

        var resultado = await service.ResolverAsync(
            VentaConProductos((7, "Producto A"), (9, "Producto B"), (7, "Producto A")),
            TipoPago.CreditoPersonal,
            1,
            24);

        Assert.Equal(new[] { 7, 9 }, resolver.ProductoIdsRecibidos);
        Assert.Equal(6, resultado.Max);
        Assert.Equal(9, resultado.ProductoIdRestrictivo);
        Assert.Equal("Producto B", resultado.ProductoRestrictivoNombre);
    }

    [Fact]
    public async Task ResultadoConservaMaxCuotasBaseYProductoRestrictivo()
    {
        var service = CrearService(new CondicionesPagoCarritoResultado
        {
            Permitido = true,
            MaxCuotasCredito = 12,
            ProductoIdsRestrictivos = new[] { 17 }
        });

        var resultado = await service.ResolverAsync(VentaConProductos((17, "Notebook")), TipoPago.CreditoPersonal, 3, 36);

        Assert.Equal(36, resultado.MaxBase);
        Assert.Equal(17, resultado.ProductoIdRestrictivo);
        Assert.Equal("Notebook", resultado.ProductoRestrictivoNombre);
    }

    private static CreditoRangoProductoService CrearService(CondicionesPagoCarritoResultado resultado) =>
        new(new StubCondicionesPagoCarritoResolver(resultado));

    private static VentaViewModel VentaConProductos(params (int Id, string Nombre)[] productos) =>
        new()
        {
            Id = 99,
            Total = 10_000m,
            Detalles = productos
                .Select(p => new VentaDetalleViewModel
                {
                    ProductoId = p.Id,
                    ProductoNombre = p.Nombre
                })
                .ToList()
        };

    private sealed class StubCondicionesPagoCarritoResolver : ICondicionesPagoCarritoResolver
    {
        private readonly CondicionesPagoCarritoResultado _resultado;

        public StubCondicionesPagoCarritoResolver(CondicionesPagoCarritoResultado resultado)
        {
            _resultado = resultado;
        }

        public int[] ProductoIdsRecibidos { get; private set; } = Array.Empty<int>();

        public Task<CondicionesPagoCarritoResultado> ResolverAsync(
            IEnumerable<int> productoIds,
            TipoPago tipoPago,
            int? configuracionTarjetaId = null,
            decimal? totalReferencia = null,
            int? maxCuotasSinInteresGlobal = null,
            int? maxCuotasConInteresGlobal = null,
            int? maxCuotasCreditoGlobal = null,
            TipoTarjeta? tipoTarjetaLegacy = null,
            CancellationToken cancellationToken = default)
        {
            ProductoIdsRecibidos = productoIds.ToArray();
            return Task.FromResult(_resultado);
        }

        public Task<MediosPagoPorProductoResultado> ObtenerMediosPorProductoAsync(
            int productoId,
            int? configuracionTarjetaId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediosPagoPorProductoResultado { SinRestriccionesPropias = true });
    }
}
