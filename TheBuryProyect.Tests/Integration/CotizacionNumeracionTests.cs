using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

public sealed class CotizacionNumeracionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly StubCalculator _calculator;
    private readonly CotizacionService _service;
    private readonly int _productoId;

    public CotizacionNumeracionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var categoria = new Categoria { Codigo = "CAT-N", Nombre = "Cat N" };
        var marca = new Marca { Codigo = "MAR-N", Nombre = "Mar N" };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        _context.SaveChanges();

        var producto = new Producto
        {
            Codigo = "P-N",
            Nombre = "Producto N",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = 50m,
            PrecioVenta = 100m,
            StockActual = 10m,
            StockMinimo = 1m,
            Activo = true
        };
        _context.Productos.Add(producto);
        _context.SaveChanges();
        _productoId = producto.Id;

        _calculator = new StubCalculator(_productoId);
        _service = new CotizacionService(_context, _calculator, NullLogger<CotizacionService>.Instance);
    }

    [Fact]
    public async Task CrearCotizacion_GeneraNumeroConFormatoEsperado()
    {
        var resultado = await _service.CrearAsync(Req(), "test");

        var fechaHoy = DateTime.UtcNow.ToString("yyyyMMdd");
        Assert.Matches($@"^COT-{fechaHoy}-\d{{4}}$", resultado.Numero);
    }

    [Fact]
    public async Task CrearCotizacion_PrimeraDeLDia_EmpiezaEn0001()
    {
        var resultado = await _service.CrearAsync(Req(), "test");

        var fechaHoy = DateTime.UtcNow.ToString("yyyyMMdd");
        Assert.Equal($"COT-{fechaHoy}-0001", resultado.Numero);
    }

    [Fact]
    public async Task CrearCotizacion_SegundaDelDia_EsSecuencial()
    {
        var primera = await _service.CrearAsync(Req(), "test");
        var segunda = await _service.CrearAsync(Req(), "test");

        var fechaHoy = DateTime.UtcNow.ToString("yyyyMMdd");
        Assert.Equal($"COT-{fechaHoy}-0001", primera.Numero);
        Assert.Equal($"COT-{fechaHoy}-0002", segunda.Numero);
    }

    [Fact]
    public async Task CrearCotizacion_ConPrefijoPrevioEnDB_GeneraSiguienteNumero()
    {
        // Simula cotizaciones existentes del día insertadas directamente
        var fechaHoy = DateTime.UtcNow.ToString("yyyyMMdd");
        _context.Cotizaciones.Add(new Cotizacion
        {
            Numero = $"COT-{fechaHoy}-0003",
            Fecha = DateTime.UtcNow,
            Estado = EstadoCotizacion.Emitida
        });
        await _context.SaveChangesAsync();

        var resultado = await _service.CrearAsync(Req(), "test");

        Assert.Equal($"COT-{fechaHoy}-0004", resultado.Numero);
    }

    [Fact]
    public async Task CrearMultiplesCotizaciones_NumerosSecuencialesUnicos()
    {
        const int cantidad = 5;
        var resultados = new List<CotizacionResultado>();
        for (var i = 0; i < cantidad; i++)
            resultados.Add(await _service.CrearAsync(Req(), "test"));

        var numeros = resultados.Select(r => r.Numero).ToList();
        Assert.Equal(cantidad, numeros.Distinct().Count());

        var fechaHoy = DateTime.UtcNow.ToString("yyyyMMdd");
        for (var i = 0; i < cantidad; i++)
            Assert.Equal($"COT-{fechaHoy}-{(i + 1):0000}", numeros[i]);
    }

    [Fact]
    public async Task IndiceUnico_NumeroRepetido_LanzaDbUpdateException()
    {
        var fecha = DateTime.UtcNow;
        _context.Cotizaciones.Add(new Cotizacion { Numero = "COT-DUP-0001", Fecha = fecha, Estado = EstadoCotizacion.Emitida });
        _context.Cotizaciones.Add(new Cotizacion { Numero = "COT-DUP-0001", Fecha = fecha, Estado = EstadoCotizacion.Emitida });

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private CotizacionCrearRequest Req() => new()
    {
        Simulacion = new CotizacionSimulacionRequest
        {
            NombreClienteLibre = "Mostrador",
            Productos = { new CotizacionProductoRequest { ProductoId = _productoId, Cantidad = 1 } }
        },
        OpcionSeleccionada = new CotizacionOpcionPagoSeleccionadaRequest
        {
            MedioPago = CotizacionMedioPagoTipo.Efectivo,
            Plan = "1 pago",
            CantidadCuotas = 1
        }
    };

    private sealed class StubCalculator : ICotizacionPagoCalculator
    {
        private readonly int _productoId;
        public StubCalculator(int productoId) => _productoId = productoId;

        public Task<CotizacionSimulacionResultado> SimularAsync(
            CotizacionSimulacionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionSimulacionResultado
            {
                Exitoso = true,
                FechaCalculo = DateTime.Today,
                Subtotal = 100m,
                DescuentoTotal = 0m,
                TotalBase = 100m,
                Productos =
                {
                    new CotizacionProductoResultado
                    {
                        ProductoId = _productoId,
                        Codigo = "P-N",
                        Nombre = "Producto N",
                        Cantidad = 1,
                        PrecioUnitario = 100m,
                        Subtotal = 100m
                    }
                },
                OpcionesPago =
                {
                    new CotizacionMedioPagoResultado
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
                                RecargoPorcentaje = 10m,
                                Total = 110m,
                                ValorCuota = 110m,
                                Recomendado = true
                            }
                        }
                    }
                }
            });
    }
}
