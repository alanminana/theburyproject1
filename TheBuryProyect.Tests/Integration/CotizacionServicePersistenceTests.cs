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

public sealed class CotizacionServicePersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly StubCotizacionPagoCalculator _calculator;
    private readonly CotizacionService _service;
    private readonly Producto _producto;
    private readonly Cliente _cliente;

    public CotizacionServicePersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var categoria = new Categoria { Codigo = "CAT-COT", Nombre = "Categoria cotizacion" };
        var marca = new Marca { Codigo = "MAR-COT", Nombre = "Marca cotizacion" };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        _context.SaveChanges();

        _producto = new Producto
        {
            Codigo = "P-COT",
            Nombre = "Producto cotizado",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = 50m,
            PrecioVenta = 100m,
            StockActual = 10m,
            StockMinimo = 1m,
            Activo = true
        };

        _cliente = new Cliente
        {
            TipoDocumento = "DNI",
            NumeroDocumento = "123",
            Apellido = "Cliente",
            Nombre = "Cotizacion",
            Telefono = "111",
            Domicilio = "Calle 1"
        };

        _context.Productos.Add(_producto);
        _context.Clientes.Add(_cliente);
        _context.SaveChanges();

        _calculator = new StubCotizacionPagoCalculator(_producto.Id);
        _service = new CotizacionService(_context, _calculator, NullLogger<CotizacionService>.Instance);
    }

    [Fact]
    public async Task Cotizacion_PersisteConDetallesYOpciones()
    {
        var resultado = await _service.CrearAsync(Request(clienteId: _cliente.Id), "carlos");

        var entity = await _context.Cotizaciones
            .Include(c => c.Detalles)
            .Include(c => c.OpcionesPago)
            .SingleAsync(c => c.Id == resultado.Id);

        Assert.Equal(EstadoCotizacion.Emitida, entity.Estado);
        Assert.Single(entity.Detalles);
        Assert.Equal("P-COT", entity.Detalles.Single().CodigoProductoSnapshot);
        Assert.Contains(entity.OpcionesPago, p => p.Seleccionado);
        Assert.Equal(1, _calculator.CallCount);
    }

    [Fact]
    public async Task Cotizacion_ClienteNullablePermitido()
    {
        var resultado = await _service.CrearAsync(Request(clienteId: null), "carlos");

        Assert.Null(resultado.ClienteId);
        Assert.Equal("Mostrador", resultado.NombreClienteLibre);
    }

    [Fact]
    public async Task Cotizacion_NumeroUnico()
    {
        _context.Cotizaciones.Add(new Cotizacion
        {
            Numero = "COT-DUP",
            Fecha = DateTime.UtcNow,
            Estado = EstadoCotizacion.Emitida
        });
        _context.Cotizaciones.Add(new Cotizacion
        {
            Numero = "COT-DUP",
            Fecha = DateTime.UtcNow,
            Estado = EstadoCotizacion.Emitida
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task CrearCotizacion_RecalculaYGuardaSnapshot()
    {
        var resultado = await _service.CrearAsync(Request(clienteId: _cliente.Id), "carlos");

        Assert.Equal(100m, resultado.Subtotal);
        Assert.Equal(100m, resultado.TotalBase);
        Assert.Equal(110m, resultado.TotalSeleccionado);
        Assert.Equal("Producto cotizado", resultado.Detalles.Single().NombreProductoSnapshot);
        Assert.Equal(1, _calculator.CallCount);
    }

    [Fact]
    public async Task CrearCotizacion_NoCreaVentaNiTocaStockCaja()
    {
        var stockAntes = _producto.StockActual;

        await _service.CrearAsync(Request(clienteId: _cliente.Id), "carlos");

        Assert.Equal(0, await _context.Ventas.CountAsync());
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync());
        Assert.Equal(0, await _context.MovimientosStock.CountAsync());
        Assert.Equal(stockAntes, await _context.Productos.Where(p => p.Id == _producto.Id).Select(p => p.StockActual).SingleAsync());
    }

    [Fact]
    public async Task ObtenerCotizacion_DevuelveDetalleCompleto()
    {
        var creada = await _service.CrearAsync(Request(clienteId: _cliente.Id), "carlos");

        var detalle = await _service.ObtenerAsync(creada.Id);

        Assert.NotNull(detalle);
        Assert.Single(detalle.Detalles);
        Assert.NotEmpty(detalle.OpcionesPago);
        Assert.Equal("Cliente, Cotizacion", detalle.ClienteNombre);
    }

    [Fact]
    public async Task ListarCotizaciones_FiltraPorClienteYEstado()
    {
        await _service.CrearAsync(Request(clienteId: _cliente.Id), "carlos");
        await _service.CrearAsync(Request(clienteId: null), "carlos");

        var listado = await _service.ListarAsync(new CotizacionFiltros
        {
            ClienteId = _cliente.Id,
            Estado = EstadoCotizacion.Emitida
        });

        Assert.Single(listado.Items);
        Assert.Equal("Cliente, Cotizacion", listado.Items.Single().Cliente);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static CotizacionCrearRequest Request(int? clienteId) =>
        new()
        {
            Simulacion = new CotizacionSimulacionRequest
            {
                ClienteId = clienteId,
                NombreClienteLibre = clienteId.HasValue ? null : "Mostrador",
                Productos =
                {
                    new CotizacionProductoRequest
                    {
                        ProductoId = 1,
                        Cantidad = 1
                    }
                }
            },
            OpcionSeleccionada = new CotizacionOpcionPagoSeleccionadaRequest
            {
                MedioPago = CotizacionMedioPagoTipo.Efectivo,
                Plan = "1 pago",
                CantidadCuotas = 1
            }
        };

    private sealed class StubCotizacionPagoCalculator : ICotizacionPagoCalculator
    {
        private readonly int _productoId;

        public StubCotizacionPagoCalculator(int productoId)
        {
            _productoId = productoId;
        }

        public int CallCount { get; private set; }

        public Task<CotizacionSimulacionResultado> SimularAsync(
            CotizacionSimulacionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new CotizacionSimulacionResultado
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
                        Codigo = "P-COT",
                        Nombre = "Producto cotizado",
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
}
