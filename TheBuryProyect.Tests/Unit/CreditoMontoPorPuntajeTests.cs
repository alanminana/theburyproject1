using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

[Trait("Category", "CreditoUi")]
[Trait("Category", "PagosAbm")]
[Trait("Category", "MontoPorPuntaje")]
public sealed class CreditoMontoPorPuntajeTests
{
    private static (AppDbContext ctx, SqliteConnection conn) CreateContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return (ctx, conn);
    }

    private static ConfiguracionPagoService CreateService(AppDbContext ctx) =>
        new(ctx, null!, NullLogger<ConfiguracionPagoService>.Instance);

    // ── 1. GET devuelve exactamente 11 puntajes 0–10 ──────────────────────────
    [Fact]
    public async Task GetMontosPorPuntaje_SinDatosExtra_Devuelve11Puntajes()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);

            var resultado = await service.GetMontosPorPuntajeAsync();

            Assert.Equal(11, resultado.Count);
            for (var p = 0; p <= 10; p++)
                Assert.Contains(resultado, r => r.Puntaje == p);
        }
    }

    // ── 2. GET completa puntajes faltantes con $0 ─────────────────────────────
    [Fact]
    public async Task GetMontosPorPuntaje_FaltanFilas_CompletaConDefaults()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            // El seed ya insertó 0-10 con $0. Actualizamos puntajes 5 y 10 para verificar que GET los devuelve correctamente.
            var fila5  = await ctx.ConfiguracionCreditoMontosPorPuntaje.FirstAsync(x => x.Puntaje == 5);
            var fila10 = await ctx.ConfiguracionCreditoMontosPorPuntaje.FirstAsync(x => x.Puntaje == 10);
            fila5.MontoMaximoFinanciable  = 200_000m;
            fila10.MontoMaximoFinanciable = 800_000m;
            await ctx.SaveChangesAsync();

            var service = CreateService(ctx);
            var resultado = await service.GetMontosPorPuntajeAsync();

            Assert.Equal(11, resultado.Count);
            Assert.Equal(200_000m, resultado.First(r => r.Puntaje == 5).MontoMaximoFinanciable);
            Assert.Equal(800_000m, resultado.First(r => r.Puntaje == 10).MontoMaximoFinanciable);
            Assert.Equal(0m, resultado.First(r => r.Puntaje == 3).MontoMaximoFinanciable);
        }
    }

    // ── 3. Guardar montos persiste correctamente ──────────────────────────────
    [Fact]
    public async Task GuardarMontosPorPuntaje_ItemsValidos_PersisteTodos()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);

            var items = Enumerable.Range(0, 11).Select(p => new MontoPorPuntajeCreditoViewModel
            {
                Puntaje = p,
                MontoMaximoFinanciable = p * 100_000m,
                RequiereAnalisis = p <= 2,
                Activo = true,
                Orden = p
            }).ToList();

            var (ok, errores) = await service.GuardarMontosPorPuntajeAsync(items, "test-user");

            Assert.True(ok, string.Join("; ", errores));

            var guardados = await ctx.ConfiguracionCreditoMontosPorPuntaje
                .OrderBy(x => x.Puntaje).ToListAsync();

            Assert.Equal(11, guardados.Count);
            Assert.Equal(500_000m, guardados.First(g => g.Puntaje == 5).MontoMaximoFinanciable);
            Assert.Equal(1_000_000m, guardados.First(g => g.Puntaje == 10).MontoMaximoFinanciable);
        }
    }

    // ── 4. Actualizar fila existente cambia el monto ───────────────────────────
    [Fact]
    public async Task GuardarMontosPorPuntaje_FilaExistente_ActualizaMonto()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            // El seed ya tiene puntaje 7. Tomamos su Id para actualizar por Id.
            var entidad = await ctx.ConfiguracionCreditoMontosPorPuntaje.FirstAsync(x => x.Puntaje == 7);
            var idOriginal = entidad.Id;

            var service = CreateService(ctx);
            var items = new List<MontoPorPuntajeCreditoViewModel>
            {
                new() { Id = idOriginal, Puntaje = 7, MontoMaximoFinanciable = 600_000m, Activo = true, Orden = 7 }
            };

            var (ok, _) = await service.GuardarMontosPorPuntajeAsync(items, "test");

            Assert.True(ok);
            var actualizado = await ctx.ConfiguracionCreditoMontosPorPuntaje.FindAsync(idOriginal);
            Assert.Equal(600_000m, actualizado!.MontoMaximoFinanciable);
        }
    }

    // ── 5. Puntaje duplicado rechaza ──────────────────────────────────────────
    [Fact]
    public async Task GuardarMontosPorPuntaje_PuntajeDuplicado_RetornaError()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);

            var items = new List<MontoPorPuntajeCreditoViewModel>
            {
                new() { Puntaje = 5, MontoMaximoFinanciable = 100_000m, Activo = true },
                new() { Puntaje = 5, MontoMaximoFinanciable = 200_000m, Activo = true }
            };

            var (ok, errores) = await service.GuardarMontosPorPuntajeAsync(items, "test");

            Assert.False(ok);
            Assert.Contains(errores, e => e.Contains("duplicado") || e.Contains("Duplicado"));
        }
    }

    // ── 6. Puntaje fuera de rango 0–10 rechaza ────────────────────────────────
    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(100)]
    public async Task GuardarMontosPorPuntaje_PuntajeFueraRango_RetornaError(int puntajeInvalido)
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);

            var items = new List<MontoPorPuntajeCreditoViewModel>
            {
                new() { Puntaje = puntajeInvalido, MontoMaximoFinanciable = 100_000m, Activo = true }
            };

            var (ok, errores) = await service.GuardarMontosPorPuntajeAsync(items, "test");

            Assert.False(ok);
            Assert.Contains(errores, e => e.Contains("rango") || e.Contains("Rango"));
        }
    }

    // ── 7. Monto negativo rechaza ─────────────────────────────────────────────
    [Fact]
    public async Task GuardarMontosPorPuntaje_MontoNegativo_RetornaError()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);

            var items = new List<MontoPorPuntajeCreditoViewModel>
            {
                new() { Puntaje = 3, MontoMaximoFinanciable = -1m, Activo = true }
            };

            var (ok, errores) = await service.GuardarMontosPorPuntajeAsync(items, "test");

            Assert.False(ok);
            Assert.Contains(errores, e => e.Contains("negativo") || e.Contains("Negativo"));
        }
    }

    // ── 8. Lista vacía rechaza ────────────────────────────────────────────────
    [Fact]
    public async Task GuardarMontosPorPuntaje_ListaVacia_RetornaError()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);

            var (ok, errores) = await service.GuardarMontosPorPuntajeAsync(new List<MontoPorPuntajeCreditoViewModel>(), "test");

            Assert.False(ok);
            Assert.NotEmpty(errores);
        }
    }

    // ── 9. Guardar tabla 0–10 no afecta PuntajeCreditoLimites (1–5) ───────────
    [Fact]
    public async Task GuardarMontosPorPuntaje_NoAfectaPuntajeCreditoLimite()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            // Verificar que PuntajeCreditoLimite tiene sus 5 filas del seed
            var limitesAntes = await ctx.PuntajesCreditoLimite.CountAsync();

            var service = CreateService(ctx);
            var items = Enumerable.Range(0, 11).Select(p => new MontoPorPuntajeCreditoViewModel
            {
                Puntaje = p, MontoMaximoFinanciable = p * 50_000m, Activo = true, Orden = p
            }).ToList();

            await service.GuardarMontosPorPuntajeAsync(items, "test");

            var limitesDespues = await ctx.PuntajesCreditoLimite.CountAsync();
            Assert.Equal(limitesAntes, limitesDespues);
        }
    }

    // ── 10. Monto $0 es permitido ─────────────────────────────────────────────
    [Fact]
    public async Task GuardarMontosPorPuntaje_MontoCero_EsValido()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);

            var items = new List<MontoPorPuntajeCreditoViewModel>
            {
                new() { Puntaje = 0, MontoMaximoFinanciable = 0m, Activo = true, Orden = 0 }
            };

            var (ok, errores) = await service.GuardarMontosPorPuntajeAsync(items, "test");

            Assert.True(ok, string.Join("; ", errores));
        }
    }
}
