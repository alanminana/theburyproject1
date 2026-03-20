using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Regresión: AplicarRapido sin ListasPrecioIds debe usar la lista predeterminada.
/// Antes del fix se consultaba _context.ListasPrecios directamente (sin pasar por IPrecioService)
/// y la rama del fallback no existía — el request retornaba 400 si no se enviaban listas.
/// </summary>
public class CambiosPreciosAplicarRapidoTest : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CambiosPreciosAplicarRapidoTest(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_AplicarRapido_SinListasExplicitas_UsaListaPredeterminada()
    {
        // Arrange: sembrar datos mínimos necesarios para el flujo
        using (var scope = _factory.Services.CreateScope())
        {
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync();

            var categoria = new Categoria { Id = 10, Nombre = "Cat Test", RowVersion = new byte[8] };
            var marca = new Marca { Id = 10, Nombre = "Marca Test", RowVersion = new byte[8] };
            var lista = new ListaPrecio
            {
                Id = 10,
                Nombre = "Lista Predeterminada Test",
                EsPredeterminada = true,
                Activa = true,
                RowVersion = new byte[8]
            };
            var producto = new Producto
            {
                Id = 10,
                Codigo = "TEST-001",
                Nombre = "Producto Test",
                CategoriaId = 10,
                MarcaId = 10,
                RowVersion = new byte[8]
            };
            var precio = new ProductoPrecioLista
            {
                Id = 10,
                ProductoId = 10,
                ListaId = 10,
                Precio = 1000m,
                Costo = 700m,
                MargenPorcentaje = 30m,
                VigenciaDesde = DateTime.UtcNow.AddDays(-1),
                VigenciaHasta = null,
                EsVigente = true,
                RowVersion = new byte[8]
            };

            context.Categorias.Add(categoria);
            context.Marcas.Add(marca);
            context.ListasPrecios.Add(lista);
            await context.SaveChangesAsync();

            context.Productos.Add(producto);
            await context.SaveChangesAsync();

            context.ProductosPrecios.Add(precio);
            await context.SaveChangesAsync();
        }

        // Act: POST sin ListasPrecioIds — debe usar la lista predeterminada
        await _factory.SeedTestUserAsync();
        var client = _factory.CreateAuthenticatedClient();
        var request = new AplicarRapidoRequest
        {
            Modo = "seleccionados",
            Porcentaje = 10m,
            ProductoIds = new List<int> { 10 },
            ListasPrecioIds = null   // aquí está el caso a cubrir
        };

        var json = System.Text.Json.JsonSerializer.Serialize(request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/CambiosPrecios/AplicarRapido");
        httpRequest.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        httpRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");
        var response = await client.SendAsync(httpRequest);

        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert: el endpoint no debe retornar BadRequest por falta de lista
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Status: {response.StatusCode}, Body: {responseBody}");

        var body = await response.Content.ReadFromJsonAsync<AplicarRapidoResponse>();
        Assert.NotNull(body);
        Assert.True(body!.Success, $"success esperado true, body: {await response.Content.ReadAsStringAsync()}");
    }

    private record AplicarRapidoResponse(bool Success, int? BatchId, int? ProductosAfectados, string? Mensaje);
}
