using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

public sealed class ProductoCondicionPagoServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _context = null!;
    private ProductoCondicionPagoService _service = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.EnsureCreatedAsync();
        _service = new ProductoCondicionPagoService(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ObtenerPorProducto_SinCondiciones_DevuelveEstructuraVacia()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.ObtenerPorProductoAsync(producto.Id);
        var editable = await _service.ObtenerEstadoEditableAsync(producto.Id);

        Assert.Equal(producto.Id, resultado.ProductoId);
        Assert.Equal(producto.Codigo, resultado.ProductoCodigo);
        Assert.Empty(resultado.Condiciones);
        Assert.Empty(editable.Condiciones);
        Assert.Empty(editable.Validaciones);
    }

    [Fact]
    public async Task GuardarCondicion_CreaCondicionRaizValida()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
        {
            TipoPago = TipoPago.CreditoPersonal,
            Permitido = true,
            MaxCuotasCredito = 12,
            PorcentajeRecargo = 10m,
            PorcentajeDescuentoMaximo = 5m,
            Observaciones = "Credito limitado"
        });

        Assert.Equal(TipoPago.CreditoPersonal, resultado.TipoPago);
        Assert.Equal(12, resultado.MaxCuotasCredito);
        Assert.NotNull(resultado.Id);
        Assert.NotEmpty(resultado.RowVersion!);

        var enDb = await _context.ProductoCondicionesPago.SingleAsync(c => c.Id == resultado.Id);
        Assert.Equal(producto.Id, enDb.ProductoId);
        Assert.Equal("Credito limitado", enDb.Observaciones);
    }

    [Fact]
    public async Task GuardarCondicion_ActualizaCondicionRaizYConservaIntegridad()
    {
        var producto = await SeedProductoAsync();
        var creada = await _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
        {
            TipoPago = TipoPago.Transferencia,
            Permitido = true,
            PorcentajeDescuentoMaximo = 3m
        });

        var actualizada = await _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
        {
            Id = creada.Id,
            RowVersion = creada.RowVersion,
            TipoPago = TipoPago.Transferencia,
            Permitido = false,
            PorcentajeDescuentoMaximo = 7m,
            Activo = true
        });

        Assert.Equal(creada.Id, actualizada.Id);
        Assert.False(actualizada.Permitido);
        Assert.Equal(7m, actualizada.PorcentajeDescuentoMaximo);
        Assert.Single(await _context.ProductoCondicionesPago.ToListAsync());
    }

    [Fact]
    public async Task GuardarReglaTarjeta_GeneralConConfiguracionTarjetaNull_Persiste()
    {
        var condicion = await SeedCondicionAsync(TipoPago.TarjetaCredito);

        var regla = await _service.GuardarReglaTarjetaAsync(condicion.Id!.Value, new GuardarProductoCondicionPagoTarjetaItem
        {
            ConfiguracionTarjetaId = null,
            Permitido = true,
            MaxCuotasSinInteres = 6
        });

        Assert.Null(regla.ConfiguracionTarjetaId);
        Assert.Equal(6, regla.MaxCuotasSinInteres);
        Assert.NotEmpty(regla.RowVersion!);
    }

    [Fact]
    public async Task GuardarReglaTarjeta_EspecificaPorTarjeta_Persiste()
    {
        var condicion = await SeedCondicionAsync(TipoPago.TarjetaCredito);
        var tarjeta = await SeedTarjetaAsync();

        var regla = await _service.GuardarReglaTarjetaAsync(condicion.Id!.Value, new GuardarProductoCondicionPagoTarjetaItem
        {
            ConfiguracionTarjetaId = tarjeta.Id,
            Permitido = false,
            PorcentajeRecargo = 4.5m
        });

        Assert.Equal(tarjeta.Id, regla.ConfiguracionTarjetaId);
        Assert.False(regla.Permitido);
        Assert.Equal(4.5m, regla.PorcentajeRecargo);
    }

    [Fact]
    public async Task GuardarReglaTarjeta_DuplicadoGeneral_SeRechazaPorServicio()
    {
        var condicion = await SeedCondicionAsync(TipoPago.TarjetaCredito);
        await _service.GuardarReglaTarjetaAsync(condicion.Id!.Value, new GuardarProductoCondicionPagoTarjetaItem());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GuardarReglaTarjetaAsync(condicion.Id!.Value, new GuardarProductoCondicionPagoTarjetaItem()));

        Assert.Contains("regla general", ex.Message);
    }

    [Fact]
    public async Task GuardarReglaTarjeta_DuplicadoEspecifico_SeRechazaPorServicio()
    {
        var condicion = await SeedCondicionAsync(TipoPago.TarjetaCredito);
        var tarjeta = await SeedTarjetaAsync();
        await _service.GuardarReglaTarjetaAsync(condicion.Id!.Value, new GuardarProductoCondicionPagoTarjetaItem
        {
            ConfiguracionTarjetaId = tarjeta.Id
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GuardarReglaTarjetaAsync(condicion.Id!.Value, new GuardarProductoCondicionPagoTarjetaItem
            {
                ConfiguracionTarjetaId = tarjeta.Id
            }));

        Assert.Contains("tarjeta indicada", ex.Message);
    }

    [Fact]
    public async Task GuardarReglaTarjeta_CuotasCero_SeRechaza()
    {
        var condicion = await SeedCondicionAsync(TipoPago.TarjetaCredito);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GuardarReglaTarjetaAsync(condicion.Id!.Value, new GuardarProductoCondicionPagoTarjetaItem
            {
                MaxCuotasSinInteres = 0
            }));

        Assert.Contains("cuotas", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GuardarCondicion_PorcentajeFueraDeRango_SeRechaza()
    {
        var producto = await SeedProductoAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
            {
                TipoPago = TipoPago.Efectivo,
                PorcentajeRecargo = 101m
            }));

        Assert.Contains("PorcentajeRecargo", ex.Message);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-10)]
    public async Task GuardarCondicion_PorcentajeNegativoActual_SeRechaza(decimal porcentaje)
    {
        var producto = await SeedProductoAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
            {
                TipoPago = TipoPago.TarjetaCredito,
                PorcentajeRecargo = porcentaje
            }));

        Assert.Contains("PorcentajeRecargo", ex.Message);
    }

    [Fact]
    public async Task GuardarReglaTarjeta_PorcentajeNegativoActual_SeRechaza()
    {
        var condicion = await SeedCondicionAsync(TipoPago.TarjetaCredito);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GuardarReglaTarjetaAsync(condicion.Id!.Value, new GuardarProductoCondicionPagoTarjetaItem
            {
                PorcentajeDescuentoMaximo = -1m
            }));

        Assert.Contains("PorcentajeDescuentoMaximo", ex.Message);
    }

    [Fact]
    public async Task GuardarCondicion_ProductoInexistente_SeRechaza()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GuardarCondicionAsync(int.MaxValue, new GuardarProductoCondicionPagoItem
            {
                TipoPago = TipoPago.Efectivo
            }));

        Assert.Contains("producto", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GuardarReglaTarjeta_TarjetaInexistente_SeRechaza()
    {
        var condicion = await SeedCondicionAsync(TipoPago.TarjetaCredito);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GuardarReglaTarjetaAsync(condicion.Id!.Value, new GuardarProductoCondicionPagoTarjetaItem
            {
                ConfiguracionTarjetaId = int.MaxValue
            }));

        Assert.Contains("tarjeta", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GuardarCondicion_TipoPagoTarjetaLegacy_SeConservaSinNormalizar()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
        {
            TipoPago = TipoPago.Tarjeta
        });

        Assert.Equal(TipoPago.Tarjeta, resultado.TipoPago);

        var editable = await _service.ObtenerEstadoEditableAsync(producto.Id);
        var validacion = Assert.Single(editable.Validaciones);
        Assert.Equal(CodigoValidacionCondicionPago.TipoPagoTarjetaLegacyAmbiguo, validacion.Codigo);
        Assert.Equal(SeveridadValidacionCondicionPago.Advertencia, validacion.Severidad);
    }

    // ─────────────────────────────────────────────────────────────
    // GuardarCondicionesCompletas
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GuardarCondicionesCompletas_CondicionYTarjetaValida_PersisteTodo()
    {
        var producto = await SeedProductoAsync();
        var tarjeta = await SeedTarjetaAsync();
        var request = new GuardarProductoCondicionesPagoRequest
        {
            Condiciones = new[]
            {
                new GuardarProductoCondicionPagoItem
                {
                    TipoPago = TipoPago.TarjetaCredito,
                    Activo = true,
                    Tarjetas = new[]
                    {
                        new GuardarProductoCondicionPagoTarjetaItem
                        {
                            ConfiguracionTarjetaId = tarjeta.Id,
                            MaxCuotasSinInteres = 3,
                            Activo = true
                        }
                    }
                }
            }
        };

        await _service.GuardarCondicionesCompletasAsync(producto.Id, request);

        var condiciones = await _context.ProductoCondicionesPago
            .Include(c => c.Tarjetas)
            .AsNoTracking()
            .ToListAsync();
        var condicion = Assert.Single(condiciones);
        var regla = Assert.Single(condicion.Tarjetas);
        Assert.Equal(3, regla.MaxCuotasSinInteres);
    }

    [Fact]
    public async Task GuardarCondicionesCompletas_FalloEnTarjeta_RevierteCondicionRaiz()
    {
        var producto = await SeedProductoAsync();
        var request = new GuardarProductoCondicionesPagoRequest
        {
            Condiciones = new[]
            {
                new GuardarProductoCondicionPagoItem
                {
                    TipoPago = TipoPago.TarjetaCredito,
                    Activo = true,
                    Tarjetas = new[]
                    {
                        new GuardarProductoCondicionPagoTarjetaItem
                        {
                            ConfiguracionTarjetaId = int.MaxValue,  // inexistente → falla
                            Activo = true
                        }
                    }
                }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GuardarCondicionesCompletasAsync(producto.Id, request));

        Assert.Contains("tarjeta", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _context.ProductoCondicionesPago.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task GuardarCondicionesCompletas_DuplicadoDeTarjetaEnLote_RevierteCondicionYPrimeraTarjeta()
    {
        var producto = await SeedProductoAsync();
        var tarjeta = await SeedTarjetaAsync();
        var request = new GuardarProductoCondicionesPagoRequest
        {
            Condiciones = new[]
            {
                new GuardarProductoCondicionPagoItem
                {
                    TipoPago = TipoPago.TarjetaCredito,
                    Activo = true,
                    Tarjetas = new[]
                    {
                        new GuardarProductoCondicionPagoTarjetaItem { ConfiguracionTarjetaId = tarjeta.Id, Activo = true },
                        new GuardarProductoCondicionPagoTarjetaItem { ConfiguracionTarjetaId = tarjeta.Id, Activo = true }
                    }
                }
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GuardarCondicionesCompletasAsync(producto.Id, request));

        Assert.Empty(await _context.ProductoCondicionesPago.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task GuardarCondicionesCompletas_ProductoInexistente_RechazaSinPersistir()
    {
        var request = new GuardarProductoCondicionesPagoRequest
        {
            Condiciones = new[]
            {
                new GuardarProductoCondicionPagoItem { TipoPago = TipoPago.Efectivo, Activo = true }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GuardarCondicionesCompletasAsync(int.MaxValue, request));

        Assert.Contains("producto", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ProductoCondicionPagoDto> SeedCondicionAsync(TipoPago tipoPago)
    {
        var producto = await SeedProductoAsync();
        return await _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
        {
            TipoPago = tipoPago
        });
    }

    private async Task<Producto> SeedProductoAsync()
    {
        var codigo = Guid.NewGuid().ToString("N")[..12];
        var categoria = new Categoria { Codigo = "C" + codigo, Nombre = "Categoria test" };
        var marca = new Marca { Codigo = "M" + codigo, Nombre = "Marca test" };
        var producto = new Producto
        {
            Codigo = "P" + codigo,
            Nombre = "Producto test",
            Categoria = categoria,
            Marca = marca,
            PrecioCompra = 100m,
            PrecioVenta = 150m,
            StockActual = 1m
        };

        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task<ConfiguracionTarjeta> SeedTarjetaAsync()
    {
        var configuracionPago = new ConfiguracionPago
        {
            TipoPago = TipoPago.TarjetaCredito,
            Nombre = "Tarjeta-" + Guid.NewGuid().ToString("N")[..8]
        };
        var tarjeta = new ConfiguracionTarjeta
        {
            ConfiguracionPago = configuracionPago,
            NombreTarjeta = "Visa-" + Guid.NewGuid().ToString("N")[..8],
            TipoTarjeta = TipoTarjeta.Credito,
            PermiteCuotas = true,
            CantidadMaximaCuotas = 12
        };

        _context.ConfiguracionesTarjeta.Add(tarjeta);
        await _context.SaveChangesAsync();
        return tarjeta;
    }

    // ─────────────────────────────────────────────────────────────
    // Tests Fase 15.6: Persistencia de planes
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GuardarCondicion_ConPlanes_CreaRegistrosPlan()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
        {
            TipoPago = TipoPago.TarjetaCredito,
            Planes = new[]
            {
                new GuardarProductoCondicionPagoPlanItem { CantidadCuotas = 3, Activo = true, AjustePorcentaje = 4m },
                new GuardarProductoCondicionPagoPlanItem { CantidadCuotas = 6, Activo = true, AjustePorcentaje = 0m }
            }
        });

        Assert.Equal(2, resultado.Planes.Count);
        Assert.All(resultado.Planes, p => Assert.NotNull(p.Id));
        var plan3 = Assert.Single(resultado.Planes, p => p.CantidadCuotas == 3);
        Assert.Equal(4m, plan3.AjustePorcentaje);
        Assert.True(plan3.Activo);
        Assert.Equal(2, await _context.ProductoCondicionPagoPlanes.CountAsync());
    }

    [Fact]
    public async Task GuardarCondicion_SinPlanes_SigueFuncionando()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
        {
            TipoPago = TipoPago.Efectivo
        });

        Assert.Empty(resultado.Planes);
        Assert.Equal(0, await _context.ProductoCondicionPagoPlanes.CountAsync());
    }

    [Fact]
    public async Task GuardarCondicion_ConPlanes_PlanActivoSePersisteComoActivo()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
        {
            TipoPago = TipoPago.TarjetaCredito,
            Planes = new[] { new GuardarProductoCondicionPagoPlanItem { CantidadCuotas = 6, Activo = true } }
        });

        Assert.True(resultado.Planes.Single().Activo);
    }

    [Fact]
    public async Task GuardarCondicion_ConPlanes_PlanInactivoSePersisteComoInactivo()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
        {
            TipoPago = TipoPago.TarjetaCredito,
            Planes = new[] { new GuardarProductoCondicionPagoPlanItem { CantidadCuotas = 6, Activo = false } }
        });

        Assert.False(resultado.Planes.Single().Activo);
    }

    [Fact]
    public async Task GuardarCondicion_ActualizaCondicion_ReemplazaPlanessinDuplicar()
    {
        var producto = await SeedProductoAsync();
        var creada = await _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
        {
            TipoPago = TipoPago.TarjetaCredito,
            Planes = new[]
            {
                new GuardarProductoCondicionPagoPlanItem { CantidadCuotas = 3, AjustePorcentaje = 4m },
                new GuardarProductoCondicionPagoPlanItem { CantidadCuotas = 6, AjustePorcentaje = 0m }
            }
        });

        var plan3Id = creada.Planes.Single(p => p.CantidadCuotas == 3).Id;

        // Conservar plan-3 (actualizado), eliminar plan-6, agregar plan-12
        var actualizada = await _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
        {
            Id = creada.Id,
            RowVersion = creada.RowVersion,
            TipoPago = TipoPago.TarjetaCredito,
            Planes = new[]
            {
                new GuardarProductoCondicionPagoPlanItem { Id = plan3Id, CantidadCuotas = 3, AjustePorcentaje = 5m },
                new GuardarProductoCondicionPagoPlanItem { CantidadCuotas = 12, AjustePorcentaje = 8m }
            }
        });

        Assert.Equal(2, actualizada.Planes.Count);
        Assert.Contains(actualizada.Planes, p => p.CantidadCuotas == 3 && p.AjustePorcentaje == 5m);
        Assert.Contains(actualizada.Planes, p => p.CantidadCuotas == 12);
        Assert.DoesNotContain(actualizada.Planes, p => p.CantidadCuotas == 6);

        // Solo 2 activos en DB (el soft-deleted de cuota-6 no se cuenta)
        Assert.Equal(2, await _context.ProductoCondicionPagoPlanes.CountAsync());
    }

    [Fact]
    public async Task GuardarCondicion_CuotasDuplicadasEnLote_SeRechaza()
    {
        var producto = await SeedProductoAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
            {
                TipoPago = TipoPago.TarjetaCredito,
                Planes = new[]
                {
                    new GuardarProductoCondicionPagoPlanItem { CantidadCuotas = 6 },
                    new GuardarProductoCondicionPagoPlanItem { CantidadCuotas = 6 }
                }
            }));

        Assert.Contains("duplicadas", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GuardarCondicion_CantidadCuotasCero_SeRechaza()
    {
        var producto = await SeedProductoAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
            {
                TipoPago = TipoPago.TarjetaCredito,
                Planes = new[] { new GuardarProductoCondicionPagoPlanItem { CantidadCuotas = 0 } }
            }));

        Assert.Contains("cuotas", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ObtenerPorProducto_ConPlanes_DevuelvePlanesGuardados()
    {
        var producto = await SeedProductoAsync();
        await _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
        {
            TipoPago = TipoPago.TarjetaCredito,
            Planes = new[]
            {
                new GuardarProductoCondicionPagoPlanItem { CantidadCuotas = 3, AjustePorcentaje = 4m, Observaciones = "Plan test" },
                new GuardarProductoCondicionPagoPlanItem { CantidadCuotas = 6, AjustePorcentaje = 0m }
            }
        });

        var resultado = await _service.ObtenerPorProductoAsync(producto.Id);

        var condicion = Assert.Single(resultado.Condiciones);
        Assert.Equal(2, condicion.Planes.Count);
        var plan3 = condicion.Planes.Single(p => p.CantidadCuotas == 3);
        Assert.Equal(4m, plan3.AjustePorcentaje);
        Assert.Equal("Plan test", plan3.Observaciones);
    }

    [Fact]
    public async Task GuardarReglaTarjeta_ConPlanes_CreaRegistrosPlan()
    {
        var condicion = await SeedCondicionAsync(TipoPago.TarjetaCredito);
        var tarjeta = await SeedTarjetaAsync();

        var resultado = await _service.GuardarReglaTarjetaAsync(condicion.Id!.Value, new GuardarProductoCondicionPagoTarjetaItem
        {
            ConfiguracionTarjetaId = tarjeta.Id,
            Planes = new[] { new GuardarProductoCondicionPagoPlanItem { CantidadCuotas = 3, AjustePorcentaje = 4m, Activo = true } }
        });

        Assert.Single(resultado.Planes);
        var plan = resultado.Planes.Single();
        Assert.Equal(3, plan.CantidadCuotas);
        Assert.Equal(4m, plan.AjustePorcentaje);
        Assert.True(plan.Activo);
        Assert.Equal(1, await _context.ProductoCondicionPagoPlanes.CountAsync());
    }

    [Fact]
    public async Task GuardarCondicion_ConPlanes_ConservaObservacionesPlan()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.GuardarCondicionAsync(producto.Id, new GuardarProductoCondicionPagoItem
        {
            TipoPago = TipoPago.TarjetaCredito,
            Planes = new[]
            {
                new GuardarProductoCondicionPagoPlanItem
                {
                    CantidadCuotas = 12,
                    AjustePorcentaje = 6.5m,
                    Activo = true,
                    Observaciones = "Recargo plan anual"
                }
            }
        });

        var plan = resultado.Planes.Single();
        Assert.Equal(12, plan.CantidadCuotas);
        Assert.Equal(6.5m, plan.AjustePorcentaje);
        Assert.Equal("Recargo plan anual", plan.Observaciones);
    }
}
