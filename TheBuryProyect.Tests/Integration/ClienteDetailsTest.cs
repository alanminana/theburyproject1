using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Regresión: GET Cliente/Details no debe persistir la aptitud crediticia.
/// Antes del fix llamaba EvaluarAptitudAsync(guardarResultado: true) desde ConstructDetalleViewModel,
/// lo que actualizaba FechaUltimaEvaluacion en cada carga de la ficha.
/// </summary>
public class ClienteDetailsTest : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ClienteDetailsTest(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_ClienteDetails_NoModifica_FechaUltimaEvaluacion()
    {
        // Arrange: sembrar un cliente con FechaUltimaEvaluacion conocida.
        // Se usa IDbContextFactory directamente para evitar resolver el AppDbContext scoped
        // fuera del contexto de una request HTTP (donde el scope DI no está activo).
        var fechaOriginal = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        using (var scope = _factory.Services.CreateScope())
        {
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync();
            context.Clientes.Add(new Cliente
            {
                Id = 1,
                Nombre = "Test",
                Apellido = "Regresion",
                TipoDocumento = "DNI",
                NumeroDocumento = "12345678",
                FechaUltimaEvaluacion = fechaOriginal,
                IsDeleted = false,
                // InMemory no genera RowVersion automáticamente (a diferencia de SQL Server)
                RowVersion = new byte[8]
            });
            await context.SaveChangesAsync();
        }

        // Act
        await _factory.SeedTestUserAsync();
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/Cliente/Details/1");

        // Assert: la request no debe haber fallado por problema interno
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);

        // Assert: FechaUltimaEvaluacion no fue modificada por el GET
        using (var scope = _factory.Services.CreateScope())
        {
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync();
            var cliente = await context.Clientes.AsNoTracking().FirstAsync(c => c.Id == 1);
            Assert.Equal(fechaOriginal, cliente.FechaUltimaEvaluacion);
        }
    }
}
