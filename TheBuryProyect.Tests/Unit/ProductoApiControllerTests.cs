using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

public class ProductoApiControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task GetUnidadesDisponibles_DevuelveContratoSelectorVenta()
    {
        var unidadService = new StubProductoUnidadService
        {
            Disponibles =
            {
                new ProductoUnidad
                {
                    Id = 7,
                    ProductoId = 3,
                    CodigoInternoUnidad = "UNI-0007",
                    NumeroSerie = "SN-7",
                    UbicacionActual = "Deposito",
                    Estado = EstadoUnidad.EnStock
                }
            }
        };
        var controller = new ProductoApiController(unidadService, NullLogger<ProductoApiController>.Instance);

        var result = await controller.GetUnidadesDisponibles(3);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        var item = json.RootElement[0];
        Assert.Equal(7, item.GetProperty("id").GetInt32());
        Assert.Equal("UNI-0007", item.GetProperty("codigoInternoUnidad").GetString());
        Assert.Equal("SN-7", item.GetProperty("numeroSerie").GetString());
        Assert.Equal("Deposito", item.GetProperty("ubicacionActual").GetString());
        Assert.Equal("EnStock", item.GetProperty("estado").GetString());
    }

    [Fact]
    public async Task GetUnidadesDisponibles_UsaSoloResultadoDisponibleDelService()
    {
        var unidadService = new StubProductoUnidadService
        {
            Disponibles =
            {
                new ProductoUnidad
                {
                    Id = 8,
                    ProductoId = 3,
                    CodigoInternoUnidad = "UNI-0008",
                    Estado = EstadoUnidad.EnStock
                }
            }
        };
        var controller = new ProductoApiController(unidadService, NullLogger<ProductoApiController>.Instance);

        await controller.GetUnidadesDisponibles(3);

        Assert.Equal(3, unidadService.UltimoProductoIdConsultado);
    }

    private static JsonDocument ToJson(object? value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonDocument.Parse(json);
    }

    private sealed class StubProductoUnidadService : IProductoUnidadService
    {
        public List<ProductoUnidad> Disponibles { get; } = new();
        public int? UltimoProductoIdConsultado { get; private set; }

        public Task<ProductoUnidad> CrearUnidadAsync(int productoId, string? numeroSerie = null, string? ubicacionActual = null, string? observaciones = null, string? usuario = null) => throw new NotImplementedException();
        public Task<IReadOnlyList<ProductoUnidad>> CrearUnidadesAsync(int productoId, IReadOnlyCollection<string?> numerosSerie, string? ubicacionActual = null, string? observaciones = null, string? usuario = null) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoUnidad>> ObtenerPorProductoAsync(int productoId) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoUnidad>> ObtenerPorProductoFiltradoAsync(int productoId, ProductoUnidadFiltros filtros) => throw new NotImplementedException();
        public Task<ProductoUnidad?> ObtenerPorIdAsync(int productoUnidadId) => throw new NotImplementedException();

        public Task<IEnumerable<ProductoUnidad>> ObtenerDisponiblesPorProductoAsync(int productoId)
        {
            UltimoProductoIdConsultado = productoId;
            return Task.FromResult<IEnumerable<ProductoUnidad>>(Disponibles);
        }

        public Task<IEnumerable<ProductoUnidadMovimiento>> ObtenerHistorialAsync(int productoUnidadId) => throw new NotImplementedException();
        public Task<ProductoUnidad> MarcarVendidaAsync(int productoUnidadId, int ventaDetalleId, int? clienteId = null, string? usuario = null) => throw new NotImplementedException();
        public Task<ProductoUnidad> MarcarFaltanteAsync(int productoUnidadId, string motivo, string? usuario = null) => throw new NotImplementedException();
        public Task<ProductoUnidad> MarcarBajaAsync(int productoUnidadId, string motivo, string? usuario = null) => throw new NotImplementedException();
        public Task<ProductoUnidad> ReintegrarAStockAsync(int productoUnidadId, string motivo, string? usuario = null) => throw new NotImplementedException();
        public Task<ProductoUnidad> RevertirVentaAsync(int productoUnidadId, string motivo, string? usuario = null) => throw new NotImplementedException();
        public Task<ProductoUnidad> MarcarDevueltaAsync(int productoUnidadId, string motivo, string? usuario = null) => throw new NotImplementedException();
    }
}
