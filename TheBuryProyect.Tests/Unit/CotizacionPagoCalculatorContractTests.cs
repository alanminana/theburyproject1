using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public sealed class CotizacionPagoCalculatorContractTests
{
    [Fact]
    public void CotizacionRequest_PermiteClienteOpcionalYNombreLibre()
    {
        var request = new CotizacionSimulacionRequest
        {
            ClienteId = null,
            NombreClienteLibre = "Cliente mostrador",
            Productos =
            {
                new CotizacionProductoRequest
                {
                    ProductoId = 10,
                    Cantidad = 1
                }
            }
        };

        Assert.Null(request.ClienteId);
        Assert.Equal("Cliente mostrador", request.NombreClienteLibre);
        Assert.True(request.IncluirEfectivo);
        Assert.True(request.IncluirCreditoPersonal);
    }

    [Fact]
    public void CotizacionResultado_RepresentaOpcionDisponible()
    {
        var resultado = new CotizacionMedioPagoResultado
        {
            MedioPago = CotizacionMedioPagoTipo.Efectivo,
            NombreMedioPago = "Efectivo",
            Disponible = true,
            Estado = CotizacionOpcionPagoEstado.Disponible,
            Planes =
            {
                new CotizacionPlanPagoResultado
                {
                    Plan = "1 pago",
                    CantidadCuotas = 1,
                    Total = 100_000m,
                    ValorCuota = 100_000m,
                    Recomendado = true
                }
            }
        };

        Assert.True(resultado.Disponible);
        Assert.Equal(CotizacionOpcionPagoEstado.Disponible, resultado.Estado);
        Assert.Single(resultado.Planes);
    }

    [Fact]
    public void CotizacionResultado_RepresentaOpcionNoDisponibleConMotivo()
    {
        var resultado = new CotizacionMedioPagoResultado
        {
            MedioPago = CotizacionMedioPagoTipo.CreditoPersonal,
            NombreMedioPago = "Credito personal",
            Disponible = false,
            Estado = CotizacionOpcionPagoEstado.BloqueadoPorProducto,
            MotivoNoDisponible = "Producto bloquea credito personal."
        };

        Assert.False(resultado.Disponible);
        Assert.Equal(CotizacionOpcionPagoEstado.BloqueadoPorProducto, resultado.Estado);
        Assert.Contains("bloquea", resultado.MotivoNoDisponible);
    }

    [Fact]
    public void CotizacionResultado_RepresentaAdvertenciasGeneralesYPorProducto()
    {
        var resultado = new CotizacionSimulacionResultado
        {
            Exitoso = true,
            Advertencias = { "Credito personal requiere evaluacion." },
            Productos =
            {
                new CotizacionProductoResultado
                {
                    ProductoId = 7,
                    Codigo = "P-7",
                    Nombre = "Producto",
                    Cantidad = 2,
                    PrecioUnitario = 50_000m,
                    Subtotal = 100_000m,
                    TieneRestricciones = true,
                    Advertencias = { "Limita cuotas." }
                }
            }
        };

        Assert.Contains("requiere evaluacion", resultado.Advertencias[0]);
        Assert.True(resultado.Productos[0].TieneRestricciones);
        Assert.Contains("Limita", resultado.Productos[0].Advertencias[0]);
    }

    [Fact]
    public void CotizacionEnums_RepresentanMediosYEstadosEsperados()
    {
        var medios = Enum.GetValues<CotizacionMedioPagoTipo>();
        var estados = Enum.GetValues<CotizacionOpcionPagoEstado>();

        Assert.Contains(CotizacionMedioPagoTipo.Efectivo, medios);
        Assert.Contains(CotizacionMedioPagoTipo.Transferencia, medios);
        Assert.Contains(CotizacionMedioPagoTipo.TarjetaCredito, medios);
        Assert.Contains(CotizacionMedioPagoTipo.TarjetaDebito, medios);
        Assert.Contains(CotizacionMedioPagoTipo.MercadoPago, medios);
        Assert.Contains(CotizacionMedioPagoTipo.CreditoPersonal, medios);
        Assert.Contains(CotizacionOpcionPagoEstado.RequiereEvaluacion, estados);
        Assert.Contains(CotizacionOpcionPagoEstado.CuotaInactiva, estados);
    }

    [Fact]
    public void CotizacionCalculator_ImplementaContrato()
    {
        ICotizacionPagoCalculator calculator = CreateCalculator();

        Assert.IsType<CotizacionPagoCalculator>(calculator);
    }

    [Fact]
    public async Task CotizacionCalculator_SinProductos_DevuelveErrorControlado()
    {
        var calculator = CreateCalculator();

        var resultado = await calculator.SimularAsync(new CotizacionSimulacionRequest());

        Assert.False(resultado.Exitoso);
        Assert.Contains(resultado.Errores, e => e.Contains("al menos un producto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Simular_ProductoValido_CalculaSubtotalYTotalBase()
    {
        var calculator = CreateCalculator();

        var resultado = await calculator.SimularAsync(DefaultRequest());

        Assert.True(resultado.Exitoso);
        Assert.Equal(200_000m, resultado.Subtotal);
        Assert.Equal(200_000m, resultado.TotalBase);
        Assert.Single(resultado.Productos);
    }

    [Fact]
    public async Task Simular_SinCliente_PermiteCotizacion()
    {
        var calculator = CreateCalculator();

        var resultado = await calculator.SimularAsync(DefaultRequest(clienteId: null));

        Assert.True(resultado.Exitoso);
        Assert.Empty(resultado.Errores);
    }

    [Fact]
    public async Task Simular_ProductoSinPrecio_DevuelveError()
    {
        var calculator = CreateCalculator(precios: new Dictionary<int, ProductoPrecioVentaResultado?> { [1] = null });

        var resultado = await calculator.SimularAsync(DefaultRequest());

        Assert.False(resultado.Exitoso);
        Assert.Contains(resultado.Errores, e => e.Contains("precio vigente", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Simular_CantidadInvalida_DevuelveError()
    {
        var calculator = CreateCalculator();

        var resultado = await calculator.SimularAsync(DefaultRequest(cantidad: 0));

        Assert.False(resultado.Exitoso);
        Assert.Contains(resultado.Errores, e => e.Contains("cantidad mayor a cero", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Simular_IncluirEfectivo_GeneraOpcionDisponible()
    {
        var resultado = await CreateCalculator().SimularAsync(DefaultRequest());

        var efectivo = Assert.Single(resultado.OpcionesPago, o => o.MedioPago == CotizacionMedioPagoTipo.Efectivo);
        Assert.True(efectivo.Disponible);
    }

    [Fact]
    public async Task Simular_IncluirTransferencia_GeneraOpcionDisponible()
    {
        var resultado = await CreateCalculator().SimularAsync(DefaultRequest());

        var transferencia = Assert.Single(resultado.OpcionesPago, o => o.MedioPago == CotizacionMedioPagoTipo.Transferencia);
        Assert.True(transferencia.Disponible);
    }

    [Fact]
    public async Task Simular_Efectivo_NoGeneraCuotas()
    {
        var resultado = await CreateCalculator().SimularAsync(DefaultRequest());

        var efectivo = Assert.Single(resultado.OpcionesPago, o => o.MedioPago == CotizacionMedioPagoTipo.Efectivo);
        var plan = Assert.Single(efectivo.Planes);
        Assert.Equal(1, plan.CantidadCuotas);
    }

    [Fact]
    public async Task Simular_Transferencia_NoGeneraCuotas()
    {
        var resultado = await CreateCalculator().SimularAsync(DefaultRequest());

        var transferencia = Assert.Single(resultado.OpcionesPago, o => o.MedioPago == CotizacionMedioPagoTipo.Transferencia);
        var plan = Assert.Single(transferencia.Planes);
        Assert.Equal(1, plan.CantidadCuotas);
    }

    [Fact]
    public async Task Simular_TarjetaCredito_GeneraPlanesActivos()
    {
        var resultado = await CreateCalculator().SimularAsync(DefaultRequest());

        var tarjeta = Assert.Single(resultado.OpcionesPago, o => o.MedioPago == CotizacionMedioPagoTipo.TarjetaCredito);
        Assert.True(tarjeta.Disponible);
        Assert.Contains(tarjeta.Planes, p => p.CantidadCuotas == 3);
    }

    [Fact]
    public async Task Simular_Tarjeta_NoMuestraCuotasInactivas()
    {
        var configuracion = DefaultConfiguracion();

        var resultado = await CreateCalculator(configuracion: configuracion).SimularAsync(DefaultRequest());

        var tarjeta = Assert.Single(resultado.OpcionesPago, o => o.MedioPago == CotizacionMedioPagoTipo.TarjetaCredito);
        Assert.DoesNotContain(tarjeta.Planes, p => p.CantidadCuotas == 6);
    }

    [Fact]
    public async Task Simular_Tarjeta_NoMuestraPlanInactivo()
    {
        var resultado = await CreateCalculator().SimularAsync(DefaultRequest(cuotas: new[] { 6 }));

        var tarjeta = Assert.Single(resultado.OpcionesPago, o => o.MedioPago == CotizacionMedioPagoTipo.TarjetaCredito);
        Assert.False(tarjeta.Disponible);
        Assert.Empty(tarjeta.Planes);
    }

    [Fact]
    public async Task Simular_Tarjeta_CalculaValorCuota()
    {
        var resultado = await CreateCalculator().SimularAsync(DefaultRequest(cuotas: new[] { 3 }));

        var tarjeta = Assert.Single(resultado.OpcionesPago, o => o.MedioPago == CotizacionMedioPagoTipo.TarjetaCredito);
        var plan = Assert.Single(tarjeta.Planes);
        Assert.Equal(220_000m, plan.Total);
        Assert.Equal(73_333.33m, plan.ValueOrDefaultCuota());
    }

    [Fact]
    public async Task Simular_TarjetaConfiguracionInactiva_DevuelveNoDisponible()
    {
        var resultado = await CreateCalculator().SimularAsync(DefaultRequest(tarjetaId: 999));

        var tarjeta = Assert.Single(resultado.OpcionesPago, o => o.MedioPago == CotizacionMedioPagoTipo.TarjetaCredito);
        Assert.False(tarjeta.Disponible);
        Assert.Contains("inactiva", tarjeta.MotivoNoDisponible, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Simular_MercadoPago_SiNoHayMapeo_DevuelveAdvertencia()
    {
        var configuracion = DefaultConfiguracion(includeMercadoPago: false);

        var resultado = await CreateCalculator(configuracion: configuracion).SimularAsync(DefaultRequest());

        Assert.DoesNotContain(resultado.OpcionesPago, o => o.MedioPago == CotizacionMedioPagoTipo.MercadoPago);
        Assert.Contains(resultado.Advertencias, a => a.Contains("MercadoPago pendiente", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Simular_CreditoPersonalSinCliente_DevuelveRequiereClienteOEvaluacion()
    {
        var resultado = await CreateCalculator().SimularAsync(DefaultRequest(clienteId: null));

        var credito = Assert.Single(resultado.OpcionesPago, o => o.MedioPago == CotizacionMedioPagoTipo.CreditoPersonal);
        Assert.False(credito.Disponible);
        Assert.Equal(CotizacionOpcionPagoEstado.RequiereCliente, credito.Estado);
    }

    [Fact]
    public async Task Simular_CreditoPersonal_NoCreaCreditoDefinitivo()
    {
        var resultado = await CreateCalculator().SimularAsync(DefaultRequest(clienteId: 44));

        var credito = Assert.Single(resultado.OpcionesPago, o => o.MedioPago == CotizacionMedioPagoTipo.CreditoPersonal);
        Assert.False(credito.Disponible);
        Assert.Equal(CotizacionOpcionPagoEstado.RequiereEvaluacion, credito.Estado);
    }

    [Fact]
    public async Task Simular_NoCreaVenta_NoTocaStock_NoRegistraCaja()
    {
        var productoService = new FakeProductoService(DefaultPrecios());
        var configuracionService = new FakeConfiguracionPagoGlobalQueryService(DefaultConfiguracion());
        var calculator = new CotizacionPagoCalculator(productoService, configuracionService);

        var resultado = await calculator.SimularAsync(DefaultRequest());

        Assert.True(resultado.Exitoso);
        Assert.Equal(1, productoService.ConsultasPrecio);
        Assert.Equal(1, configuracionService.ConsultasConfiguracion);
    }

    private static CotizacionPagoCalculator CreateCalculator(
        Dictionary<int, ProductoPrecioVentaResultado?>? precios = null,
        ConfiguracionPagoGlobalResultado? configuracion = null) =>
        new(
            new FakeProductoService(precios ?? DefaultPrecios()),
            new FakeConfiguracionPagoGlobalQueryService(configuracion ?? DefaultConfiguracion()));

    private static CotizacionSimulacionRequest DefaultRequest(
        int cantidad = 2,
        int? clienteId = null,
        int? tarjetaId = null,
        int[]? cuotas = null) =>
        new()
        {
            ClienteId = clienteId,
            ConfiguracionTarjetaId = tarjetaId,
            CuotasSolicitadas = cuotas,
            Productos =
            {
                new CotizacionProductoRequest
                {
                    ProductoId = 1,
                    Cantidad = cantidad
                }
            }
        };

    private static Dictionary<int, ProductoPrecioVentaResultado?> DefaultPrecios() =>
        new()
        {
            [1] = new ProductoPrecioVentaResultado
            {
                ProductoId = 1,
                Codigo = "P-1",
                Nombre = "Producto 1",
                PrecioVenta = 100_000m,
                FuentePrecio = FuentePrecioVigente.ProductoPrecioBase,
                StockActual = 10
            }
        };

    private static ConfiguracionPagoGlobalResultado DefaultConfiguracion(bool includeMercadoPago = true)
    {
        var medios = new List<MedioPagoGlobalDto>
        {
            new()
            {
                Id = 1,
                TipoPago = TipoPago.Efectivo,
                NombreVisible = "Efectivo",
                Activo = true,
                Planes = new List<PlanPagoGlobalConfiguradoDto>
                {
                    Plan(1, 1, TipoPago.Efectivo, cuotas: 1, ajuste: -5m, etiqueta: "Efectivo")
                }
            },
            new()
            {
                Id = 2,
                TipoPago = TipoPago.Transferencia,
                NombreVisible = "Transferencia",
                Activo = true,
                Planes = new List<PlanPagoGlobalConfiguradoDto>
                {
                    Plan(2, 2, TipoPago.Transferencia, cuotas: 1, ajuste: 0m, etiqueta: "Transferencia")
                }
            },
            new()
            {
                Id = 3,
                TipoPago = TipoPago.TarjetaCredito,
                NombreVisible = "Tarjeta credito",
                Activo = true,
                Tarjetas = new List<TarjetaPagoGlobalDto>
                {
                    new()
                    {
                        Id = 10,
                        ConfiguracionPagoId = 3,
                        Nombre = "Visa",
                        TipoTarjeta = TipoTarjeta.Credito,
                        Activa = true,
                        PermiteCuotas = true,
                        CantidadMaximaCuotas = 12
                    }
                },
                Planes = new List<PlanPagoGlobalConfiguradoDto>
                {
                    Plan(3, 3, TipoPago.TarjetaCredito, cuotas: 1, ajuste: 0m, etiqueta: "1 pago", tarjetaId: 10),
                    Plan(4, 3, TipoPago.TarjetaCredito, cuotas: 3, ajuste: 10m, etiqueta: "3 cuotas", tarjetaId: 10)
                }
            },
            new()
            {
                Id = 4,
                TipoPago = TipoPago.TarjetaDebito,
                NombreVisible = "Tarjeta debito",
                Activo = true,
                Tarjetas = new List<TarjetaPagoGlobalDto>
                {
                    new()
                    {
                        Id = 20,
                        ConfiguracionPagoId = 4,
                        Nombre = "Visa Debito",
                        TipoTarjeta = TipoTarjeta.Debito,
                        Activa = true,
                        PermiteCuotas = false
                    }
                },
                Planes = new List<PlanPagoGlobalConfiguradoDto>
                {
                    Plan(5, 4, TipoPago.TarjetaDebito, cuotas: 1, ajuste: 2m, etiqueta: "Debito", tarjetaId: 20)
                }
            }
        };

        if (includeMercadoPago)
        {
            medios.Add(new MedioPagoGlobalDto
            {
                Id = 5,
                TipoPago = TipoPago.MercadoPago,
                NombreVisible = "MercadoPago",
                Activo = true,
                Planes = new List<PlanPagoGlobalConfiguradoDto>
                {
                    Plan(6, 5, TipoPago.MercadoPago, cuotas: 1, ajuste: 3m, etiqueta: "MercadoPago")
                }
            });
        }

        return new ConfiguracionPagoGlobalResultado { Medios = medios };
    }

    private static PlanPagoGlobalConfiguradoDto Plan(
        int id,
        int medioId,
        TipoPago tipoPago,
        int cuotas,
        decimal ajuste,
        string etiqueta,
        int? tarjetaId = null) =>
        new()
        {
            Id = id,
            ConfiguracionPagoId = medioId,
            ConfiguracionTarjetaId = tarjetaId,
            TipoPago = tipoPago,
            CantidadCuotas = cuotas,
            Activo = true,
            AjustePorcentaje = ajuste,
            Etiqueta = etiqueta,
            Orden = id
        };

    private sealed class FakeConfiguracionPagoGlobalQueryService : IConfiguracionPagoGlobalQueryService
    {
        private readonly ConfiguracionPagoGlobalResultado _resultado;

        public FakeConfiguracionPagoGlobalQueryService(ConfiguracionPagoGlobalResultado resultado)
        {
            _resultado = resultado;
        }

        public int ConsultasConfiguracion { get; private set; }

        public Task<ConfiguracionPagoGlobalResultado> ObtenerActivaParaVentaAsync(
            CancellationToken cancellationToken = default)
        {
            ConsultasConfiguracion++;
            return Task.FromResult(_resultado);
        }
    }

    private sealed class FakeProductoService : IProductoService
    {
        private readonly Dictionary<int, ProductoPrecioVentaResultado?> _precios;

        public FakeProductoService(Dictionary<int, ProductoPrecioVentaResultado?> precios)
        {
            _precios = precios;
        }

        public int ConsultasPrecio { get; private set; }

        public Task<ProductoPrecioVentaResultado?> ObtenerPrecioVigenteParaVentaAsync(int productoId)
        {
            ConsultasPrecio++;
            return Task.FromResult(_precios.GetValueOrDefault(productoId));
        }

        public Task<IEnumerable<Producto>> GetAllAsync() => throw new NotSupportedException();
        public Task<Producto?> GetByIdAsync(int id) => throw new NotSupportedException();
        public Task<IEnumerable<Producto>> GetByCategoriaAsync(int categoriaId) => throw new NotSupportedException();
        public Task<IEnumerable<Producto>> GetByMarcaAsync(int marcaId) => throw new NotSupportedException();
        public Task<IEnumerable<Producto>> GetProductosConStockBajoAsync() => throw new NotSupportedException();
        public Task<Producto> CreateAsync(Producto producto) => throw new NotSupportedException();
        public Task<Producto> UpdateAsync(Producto producto) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(int id) => throw new NotSupportedException();
        public Task PrepararPrecioVentaConIvaAsync(Producto producto) => throw new NotSupportedException();
        public decimal ObtenerPrecioVentaSinIva(decimal precioVentaConIva, decimal porcentajeIVA) => throw new NotSupportedException();
        public Task<IEnumerable<Producto>> SearchAsync(string? searchTerm = null, int? categoriaId = null, int? marcaId = null, bool stockBajo = false, bool soloActivos = false, string? orderBy = null, string? orderDirection = "asc") => throw new NotSupportedException();
        public Task<List<int>> SearchIdsAsync(string? searchTerm = null, int? categoriaId = null, int? marcaId = null, bool stockBajo = false, bool soloActivos = false) => throw new NotSupportedException();
        public Task<IEnumerable<ProductoVentaDto>> BuscarParaVentaAsync(string term, int take = 20, int? categoriaId = null, int? marcaId = null, bool soloConStock = true, decimal? precioMin = null, decimal? precioMax = null) => throw new NotSupportedException();
        public Task<Producto> ActualizarStockAsync(int id, decimal cantidad) => throw new NotSupportedException();
        public Task<Producto> ActualizarComisionAsync(int id, decimal porcentaje) => throw new NotSupportedException();
        public Task<bool> ToggleDestacadoAsync(int id) => throw new NotSupportedException();
        public Task CambiarTrazabilidadIndividualAsync(int productoId, bool requiereTrazabilidad) => throw new NotSupportedException();
        public Task<bool> ExistsCodigoAsync(string codigo, int? excludeId = null) => throw new NotSupportedException();
    }
}

internal static class CotizacionPlanPagoResultadoTestExtensions
{
    public static decimal ValueOrDefaultCuota(this CotizacionPlanPagoResultado plan) =>
        plan.ValorCuota.GetValueOrDefault();
}
