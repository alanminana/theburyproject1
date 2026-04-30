using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

public class ContratoVentaCreditoServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ContratoVentaCreditoService _service;

    public ContratoVentaCreditoServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new ContratoVentaCreditoService(
            _context,
            new StubFinancialCalculationService(),
            new StubWebHostEnvironment(),
            NullLogger<ContratoVentaCreditoService>.Instance);
    }

    [Fact]
    public async Task GenerarAsync_SinDescuentoGeneral_ConservaSubtotalDeLinea()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_210m, subtotalFinal: 1_210m)
        }, total: 1_210m);

        var contrato = await _service.GenerarAsync(venta.Id, "tester");

        var productos = LeerProductosSnapshot(contrato);
        Assert.Single(productos);
        Assert.Equal(1_210m, productos[0].GetProperty("Subtotal").GetDecimal());
    }

    [Fact]
    public async Task GenerarAsync_ConDescuentoGeneral_UsaSubtotalFinal()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_210m, subtotalFinal: 1_089m)
        }, total: 1_089m, descuento: 121m);

        var contrato = await _service.GenerarAsync(venta.Id, "tester");

        var productos = LeerProductosSnapshot(contrato);
        Assert.Single(productos);
        Assert.Equal(1_089m, productos[0].GetProperty("Subtotal").GetDecimal());
    }

    [Fact]
    public async Task GenerarAsync_ProductosMixtosIva_ImportesVisiblesCierranConVentaTotal()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P21", "Producto IVA 21", subtotal: 1_210m, subtotalFinal: 1_089m),
            DetalleSeed("P105", "Producto IVA 10.5", subtotal: 110.50m, subtotalFinal: 100m),
            DetalleSeed("P0", "Producto IVA 0", subtotal: 100m, subtotalFinal: 80m)
        }, total: 1_269m, descuento: 151.50m);

        var contrato = await _service.GenerarAsync(venta.Id, "tester");

        var productos = LeerProductosSnapshot(contrato);
        Assert.Equal(3, productos.Count);
        Assert.Equal(venta.Total, productos.Sum(p => p.GetProperty("Subtotal").GetDecimal()));
    }

    [Fact]
    public async Task GenerarAsync_LegacySinSubtotalFinal_UsaSubtotal()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("LEG", "Producto legacy", subtotal: 500m, subtotalFinal: 0m)
        }, total: 500m);

        var contrato = await _service.GenerarAsync(venta.Id, "tester");

        var productos = LeerProductosSnapshot(contrato);
        Assert.Single(productos);
        Assert.Equal(500m, productos[0].GetProperty("Subtotal").GetDecimal());
    }

    private async Task<Venta> SeedVentaCreditoAsync(
        IEnumerable<DetalleSeedData> detalles,
        decimal total,
        decimal descuento = 0m)
    {
        var cliente = new Cliente
        {
            Nombre = "Juan",
            Apellido = "Perez",
            TipoDocumento = "DNI",
            NumeroDocumento = "12345678",
            Domicilio = "Calle 123",
            Localidad = "Ciudad",
            Telefono = "1122334455"
        };

        var credito = new Credito
        {
            Cliente = cliente,
            Numero = "CRE-001",
            MontoSolicitado = total,
            MontoAprobado = total,
            SaldoPendiente = total,
            TasaInteres = 5m,
            CantidadCuotas = 1,
            MontoCuota = total,
            TotalAPagar = total,
            FechaPrimeraCuota = DateTime.UtcNow.Date.AddMonths(1)
        };

        credito.Cuotas.Add(new Cuota
        {
            NumeroCuota = 1,
            MontoCapital = total,
            MontoInteres = 0m,
            MontoTotal = total,
            FechaVencimiento = credito.FechaPrimeraCuota.Value
        });

        var categoria = new Categoria { Nombre = $"Categoria {Guid.NewGuid():N}" };
        var marca = new Marca { Nombre = $"Marca {Guid.NewGuid():N}" };

        var venta = new Venta
        {
            Numero = $"VTA-{Guid.NewGuid():N}",
            Cliente = cliente,
            Credito = credito,
            TipoPago = TipoPago.CreditoPersonal,
            Estado = EstadoVenta.Confirmada,
            FechaVenta = DateTime.UtcNow,
            Subtotal = total,
            IVA = 0m,
            Descuento = descuento,
            Total = total
        };

        foreach (var detalleSeed in detalles)
        {
            var producto = new Producto
            {
                Codigo = detalleSeed.Codigo,
                Nombre = detalleSeed.Nombre,
                Categoria = categoria,
                Marca = marca,
                PrecioCompra = 0m,
                PrecioVenta = detalleSeed.Subtotal,
                PorcentajeIVA = 21m
            };

            venta.Detalles.Add(new VentaDetalle
            {
                Producto = producto,
                Cantidad = 1,
                PrecioUnitario = detalleSeed.Subtotal,
                Subtotal = detalleSeed.Subtotal,
                SubtotalFinal = detalleSeed.SubtotalFinal
            });
        }

        _context.PlantillasContratoCredito.Add(new PlantillaContratoCredito
        {
            Nombre = $"Plantilla {Guid.NewGuid():N}",
            Activa = true,
            NombreVendedor = "The Bury",
            DomicilioVendedor = "Local 1",
            CiudadFirma = "Ciudad",
            Jurisdiccion = "Provincia",
            InteresMoraDiarioPorcentaje = 1m,
            TextoContrato = "{{Venta.Productos}} {{Venta.Total}}",
            TextoPagare = "{{Venta.Total}}",
            VigenteDesde = DateTime.UtcNow.Date.AddDays(-1)
        });

        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    private static List<JsonElement> LeerProductosSnapshot(ContratoVentaCredito contrato)
    {
        using var document = JsonDocument.Parse(contrato.DatosSnapshotJson);
        return document.RootElement
            .GetProperty("Venta")
            .GetProperty("Productos")
            .EnumerateArray()
            .Select(p => p.Clone())
            .ToList();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static DetalleSeedData DetalleSeed(string codigo, string nombre, decimal subtotal, decimal subtotalFinal)
        => new(codigo, nombre, subtotal, subtotalFinal);

    private sealed record DetalleSeedData(string Codigo, string Nombre, decimal Subtotal, decimal SubtotalFinal);

    private sealed class StubFinancialCalculationService : IFinancialCalculationService
    {
        public decimal CalcularCuotaSistemaFrances(decimal monto, decimal tasaMensual, int cuotas)
            => cuotas > 0 ? Math.Round(monto / cuotas, 2, MidpointRounding.AwayFromZero) : 0m;

        public decimal CalcularTotalConInteres(decimal monto, decimal tasaMensual, int cuotas) => monto;

        public decimal CalcularCFTEA(decimal totalAPagar, decimal montoInicial, int cuotas) => 0m;

        public decimal CalcularInteresTotal(decimal monto, decimal tasaMensual, int cuotas) => 0m;

        public decimal ComputePmt(decimal tasaMensual, int cuotas, decimal monto)
            => cuotas > 0 ? Math.Round(monto / cuotas, 2, MidpointRounding.AwayFromZero) : 0m;

        public decimal ComputeFinancedAmount(decimal total, decimal anticipo) => total - anticipo;

        public decimal CalcularCFTEADesdeTasa(decimal tasaMensual) => 0m;

        public SimulacionPlanCreditoDto SimularPlanCredito(
            decimal totalVenta,
            decimal anticipo,
            int cuotas,
            decimal tasaMensual,
            decimal gastosAdministrativos,
            DateTime fechaPrimeraCuota,
            decimal semaforoRatioVerdeMax = 0.08m,
            decimal semaforoRatioAmarilloMax = 0.15m)
            => throw new NotImplementedException();
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "TheBuryProject.Tests";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
