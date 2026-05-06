using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

public class ConfiguracionPagoServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ConfiguracionPagoService _service;

    public ConfiguracionPagoServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = new ConfiguracionPagoService(
            _context,
            mapper,
            NullLogger<ConfiguracionPagoService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Cliente> SeedCliente(
        decimal? tasaPersonalizada = null,
        decimal? gastosPersonalizados = null,
        int? cuotasMaxPersonalizadas = null,
        decimal? montoMinimo = null,
        decimal? montoMaximo = null,
        int? perfilPreferidoId = null)
    {
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Cliente",
            TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..10],
            Telefono = "1234567890",
            Domicilio = "Calle 123",
            TasaInteresMensualPersonalizada = tasaPersonalizada,
            GastosAdministrativosPersonalizados = gastosPersonalizados,
            CuotasMaximasPersonalizadas = cuotasMaxPersonalizadas,
            MontoMinimoPersonalizado = montoMinimo,
            MontoMaximoPersonalizado = montoMaximo,
            PerfilCreditoPreferidoId = perfilPreferidoId
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<PerfilCredito> SeedPerfil(
        decimal tasaMensual = 5m,
        decimal gastos = 100m,
        int minCuotas = 3,
        int maxCuotas = 18)
    {
        var perfil = new PerfilCredito
        {
            Nombre = "TestPerfil",
            TasaMensual = tasaMensual,
            GastosAdministrativos = gastos,
            MinCuotas = minCuotas,
            MaxCuotas = maxCuotas,
            Activo = true
        };
        _context.PerfilesCredito.Add(perfil);
        await _context.SaveChangesAsync();
        return perfil;
    }

    private async Task<ConfiguracionPago> SeedConfigPago(
        TipoPago tipo,
        bool permiteDescuento = false,
        decimal? maxDescuento = null,
        bool tieneRecargo = false,
        decimal? porcentajeRecargo = null)
    {
        var config = new ConfiguracionPago
        {
            TipoPago = tipo,
            Nombre = tipo.ToString(),
            Activo = true,
            PermiteDescuento = permiteDescuento,
            PorcentajeDescuentoMaximo = maxDescuento,
            TieneRecargo = tieneRecargo,
            PorcentajeRecargo = porcentajeRecargo
        };
        _context.ConfiguracionesPago.Add(config);
        await _context.SaveChangesAsync();
        return config;
    }

    // =========================================================================
    // ObtenerParametrosCreditoClienteAsync — cadena de prioridad
    // =========================================================================

    // -------------------------------------------------------------------------
    // 1. Cliente sin personalización y sin perfil → usa tasaGlobal, defaults hardcoded
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerParametros_SinPersonalizacion_UsaTasaGlobal()
    {
        var cliente = await SeedCliente();

        var result = await _service.ObtenerParametrosCreditoClienteAsync(cliente.Id, tasaGlobal: 3.5m);

        Assert.Equal(3.5m, result.TasaMensual);
        Assert.Equal(FuenteConfiguracionCredito.Global, result.Fuente);
        Assert.False(result.TieneConfiguracionPersonalizada);
    }

    // -------------------------------------------------------------------------
    // 2. Cliente sin personalización → cuotas máximas = 24 (hardcoded global)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerParametros_SinPersonalizacion_CuotasMaximas24()
    {
        var cliente = await SeedCliente();

        var result = await _service.ObtenerParametrosCreditoClienteAsync(cliente.Id, tasaGlobal: 2m);

        Assert.Equal(24, result.CuotasMaximas);
        Assert.Equal(1, result.CuotasMinimas);
    }

    // -------------------------------------------------------------------------
    // 3. Cliente con tasa personalizada → usa tasa personalizada, fuente PorCliente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerParametros_TasaPersonalizada_UsaTasaCliente()
    {
        var cliente = await SeedCliente(tasaPersonalizada: 8m);

        var result = await _service.ObtenerParametrosCreditoClienteAsync(cliente.Id, tasaGlobal: 3m);

        Assert.Equal(8m, result.TasaMensual);
        Assert.Equal(FuenteConfiguracionCredito.PorCliente, result.Fuente);
        Assert.True(result.TieneConfiguracionPersonalizada);
        Assert.True(result.TieneTasaPersonalizada);
        Assert.Equal(8m, result.TasaPersonalizada);
    }

    // -------------------------------------------------------------------------
    // 4. Cliente con gastos personalizados → usa gastos del cliente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerParametros_GastosPersonalizados_UsaGastosCliente()
    {
        var cliente = await SeedCliente(gastosPersonalizados: 250m);

        var result = await _service.ObtenerParametrosCreditoClienteAsync(cliente.Id, tasaGlobal: 3m);

        Assert.Equal(250m, result.GastosAdministrativos);
        Assert.Equal(FuenteConfiguracionCredito.PorCliente, result.Fuente);
    }

    // -------------------------------------------------------------------------
    // 5. Cliente con cuotas máximas personalizadas → usa cuotas del cliente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerParametros_CuotasMaxPersonalizadas_UsaCuotasCliente()
    {
        var cliente = await SeedCliente(cuotasMaxPersonalizadas: 12);

        var result = await _service.ObtenerParametrosCreditoClienteAsync(cliente.Id, tasaGlobal: 3m);

        Assert.Equal(12, result.CuotasMaximas);
        Assert.Equal(FuenteConfiguracionCredito.PorCliente, result.Fuente);
    }

    // -------------------------------------------------------------------------
    // 6. Cliente con perfil preferido → usa tasa del perfil (si no tiene personalización)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerParametros_ConPerfil_SinPersonalizacion_UsaTasaGlobal()
    {
        // Un perfil no activa la ruta "personalizado" — la prioridad personalizado
        // requiere campos propios del cliente
        var perfil = await SeedPerfil(tasaMensual: 6m, minCuotas: 3, maxCuotas: 18);
        var cliente = await SeedCliente(perfilPreferidoId: perfil.Id);

        var result = await _service.ObtenerParametrosCreditoClienteAsync(cliente.Id, tasaGlobal: 3m);

        // Sin configuración personalizada → fuente Global, tasa global
        Assert.Equal(FuenteConfiguracionCredito.Global, result.Fuente);
        Assert.Equal(3m, result.TasaMensual);
        // Pero cuotas mínimas vienen del perfil
        Assert.Equal(3, result.CuotasMinimas);
        Assert.Equal(perfil.Id, result.PerfilPreferidoId);
        Assert.Equal("TestPerfil", result.PerfilPreferidoNombre);
    }

    // -------------------------------------------------------------------------
    // 7. Cliente con tasa personalizada y perfil → tasa personalizada gana sobre perfil
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerParametros_TasaPersonalizadaYPerfil_TasaPersonalizadaGana()
    {
        var perfil = await SeedPerfil(tasaMensual: 6m);
        var cliente = await SeedCliente(tasaPersonalizada: 9m, perfilPreferidoId: perfil.Id);

        var result = await _service.ObtenerParametrosCreditoClienteAsync(cliente.Id, tasaGlobal: 3m);

        Assert.Equal(9m, result.TasaMensual);
        Assert.Equal(FuenteConfiguracionCredito.PorCliente, result.Fuente);
    }

    // -------------------------------------------------------------------------
    // 8. Cliente con monto min/max personalizados — se exponen correctamente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerParametros_MontoPersonalizado_SeExpone()
    {
        var cliente = await SeedCliente(montoMinimo: 5000m, montoMaximo: 50000m);

        var result = await _service.ObtenerParametrosCreditoClienteAsync(cliente.Id, tasaGlobal: 3m);

        Assert.Equal(5000m, result.MontoMinimo);
        Assert.Equal(50000m, result.MontoMaximo);
    }

    // -------------------------------------------------------------------------
    // 9. Cliente inexistente → valores globales por defecto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerParametros_ClienteNoExiste_UsaGlobal()
    {
        var result = await _service.ObtenerParametrosCreditoClienteAsync(
            clienteId: 99999, tasaGlobal: 4m);

        Assert.Equal(4m, result.TasaMensual);
        Assert.Equal(FuenteConfiguracionCredito.Global, result.Fuente);
        Assert.False(result.TieneConfiguracionPersonalizada);
        Assert.Equal(24, result.CuotasMaximas);
        Assert.Equal(1, result.CuotasMinimas);
    }

    // =========================================================================
    // ValidarDescuento
    // =========================================================================

    // -------------------------------------------------------------------------
    // 10. Sin configuración para el tipo de pago → permite cualquier descuento
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidarDescuento_SinConfig_Permite()
    {
        var result = await _service.ValidarDescuento(TipoPago.Efectivo, 50m);

        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // 11. Config sin descuento → solo permite descuento 0
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidarDescuento_NoPermiteDescuento_SoloAceptaCero()
    {
        await SeedConfigPago(TipoPago.Efectivo, permiteDescuento: false);

        Assert.True(await _service.ValidarDescuento(TipoPago.Efectivo, 0m));
        Assert.False(await _service.ValidarDescuento(TipoPago.Efectivo, 1m));
    }

    // -------------------------------------------------------------------------
    // 12. Config con descuento máximo → rechaza si supera el límite
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidarDescuento_ConMaximo_RechazaSiSupera()
    {
        await SeedConfigPago(TipoPago.Efectivo, permiteDescuento: true, maxDescuento: 10m);

        Assert.True(await _service.ValidarDescuento(TipoPago.Efectivo, 10m));
        Assert.False(await _service.ValidarDescuento(TipoPago.Efectivo, 10.01m));
    }

    // -------------------------------------------------------------------------
    // 13. Config con descuento permitido sin máximo → acepta cualquier valor
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidarDescuento_PermitidoSinMaximo_AceptaCualquiera()
    {
        await SeedConfigPago(TipoPago.Efectivo, permiteDescuento: true, maxDescuento: null);

        Assert.True(await _service.ValidarDescuento(TipoPago.Efectivo, 99m));
    }

    // =========================================================================
    // CalcularRecargo
    // =========================================================================

    // -------------------------------------------------------------------------
    // 14. Sin configuración → recargo 0
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CalcularRecargo_SinConfig_RetornaCero()
    {
        var result = await _service.CalcularRecargo(TipoPago.Efectivo, 1000m);

        Assert.Equal(0m, result);
    }

    // -------------------------------------------------------------------------
    // 15. Config sin recargo → recargo 0
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CalcularRecargo_SinRecargo_RetornaCero()
    {
        await SeedConfigPago(TipoPago.Efectivo, tieneRecargo: false, porcentajeRecargo: 5m);

        var result = await _service.CalcularRecargo(TipoPago.Efectivo, 1000m);

        Assert.Equal(0m, result);
    }

    // -------------------------------------------------------------------------
    // 16. Config con recargo 10% sobre $2000 → $200
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CalcularRecargo_ConRecargo_CalculaCorrectamente()
    {
        await SeedConfigPago(TipoPago.Efectivo, tieneRecargo: true, porcentajeRecargo: 10m);

        var result = await _service.CalcularRecargo(TipoPago.Efectivo, 2000m);

        Assert.Equal(200m, result);
    }

    // =========================================================================
    // ObtenerTasaInteresMensualCreditoPersonalAsync
    // =========================================================================

    // -------------------------------------------------------------------------
    // 17. Sin configuración existente → retorna null, NO crea registro en DB
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerTasa_SinConfig_RetornaNull_SinCrearRegistro()
    {
        var result = await _service.ObtenerTasaInteresMensualCreditoPersonalAsync();

        Assert.Null(result);

        var enDb = await _context.ConfiguracionesPago
            .FirstOrDefaultAsync(c => c.TipoPago == TipoPago.CreditoPersonal);
        Assert.Null(enDb);
    }

    // -------------------------------------------------------------------------
    // 18. Con configuración con tasa NULL → retorna null
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerTasa_TasaNull_RetornaNull()
    {
        _context.ConfiguracionesPago.Add(new ConfiguracionPago
        {
            TipoPago = TipoPago.CreditoPersonal,
            Nombre = "CreditoPersonal",
            Activo = true,
            TasaInteresMensualCreditoPersonal = null
        });
        await _context.SaveChangesAsync();

        var result = await _service.ObtenerTasaInteresMensualCreditoPersonalAsync();

        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // 19. Con configuración con tasa 0 → retorna null
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerTasa_TasaCero_RetornaNull()
    {
        _context.ConfiguracionesPago.Add(new ConfiguracionPago
        {
            TipoPago = TipoPago.CreditoPersonal,
            Nombre = "CreditoPersonal",
            Activo = true,
            TasaInteresMensualCreditoPersonal = 0m
        });
        await _context.SaveChangesAsync();

        var result = await _service.ObtenerTasaInteresMensualCreditoPersonalAsync();

        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // 20. Con configuración con tasa válida → retorna tasa registrada
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerTasa_TasaValida_RetornaTasa()
    {
        _context.ConfiguracionesPago.Add(new ConfiguracionPago
        {
            TipoPago = TipoPago.CreditoPersonal,
            Nombre = "CreditoPersonal",
            Activo = true,
            TasaInteresMensualCreditoPersonal = 7.5m
        });
        await _context.SaveChangesAsync();

        var result = await _service.ObtenerTasaInteresMensualCreditoPersonalAsync();

        Assert.Equal(7.5m, result);
    }

    // =========================================================================
    // GuardarCreditoPersonalAsync — batch update de perfiles
    // =========================================================================

    // -------------------------------------------------------------------------
    // 21. Actualiza defaults globales cuando existe ConfiguracionPago CreditoPersonal
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarCreditoPersonal_DefaultsGlobales_ActualizaConfigPago()
    {
        _context.ConfiguracionesPago.Add(new ConfiguracionPago
        {
            TipoPago = TipoPago.CreditoPersonal,
            Nombre = "CreditoPersonal",
            Activo = true,
            TasaInteresMensualCreditoPersonal = 3m
        });
        await _context.SaveChangesAsync();

        var config = new CreditoPersonalConfigViewModel
        {
            DefaultsGlobales = new DefaultsGlobalesViewModel
            {
                TasaMensual = 6m,
                GastosAdministrativos = 200m,
                MinCuotas = 2,
                MaxCuotas = 36
            }
        };

        await _service.GuardarCreditoPersonalAsync(config);

        var enDb = await _context.ConfiguracionesPago
            .FirstAsync(c => c.TipoPago == TipoPago.CreditoPersonal);
        Assert.Equal(6m, enDb.TasaInteresMensualCreditoPersonal);
        Assert.Equal(200m, enDb.GastosAdministrativosDefaultCreditoPersonal);
        Assert.Equal(2, enDb.MinCuotasDefaultCreditoPersonal);
        Assert.Equal(36, enDb.MaxCuotasDefaultCreditoPersonal);
    }

    // -------------------------------------------------------------------------
    // 20. Actualiza perfil existente (batch load — un solo hit a BD por todos)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarCreditoPersonal_PerfilExistente_ActualizaDatos()
    {
        var perfil = await SeedPerfil(tasaMensual: 5m, maxCuotas: 12);

        var config = new CreditoPersonalConfigViewModel
        {
            Perfiles = new List<PerfilCreditoViewModel>
            {
                new()
                {
                    Id = perfil.Id,
                    Nombre = perfil.Nombre,
                    TasaMensual = 9m,
                    GastosAdministrativos = 300m,
                    MinCuotas = 1,
                    MaxCuotas = 24,
                    Activo = true
                }
            }
        };

        await _service.GuardarCreditoPersonalAsync(config);

        var enDb = await _context.PerfilesCredito.FindAsync(perfil.Id);
        Assert.Equal(9m, enDb!.TasaMensual);
        Assert.Equal(24, enDb.MaxCuotas);
    }

    // -------------------------------------------------------------------------
    // 21. Crea perfil nuevo (Id == 0)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarCreditoPersonal_PerfilNuevo_CreaRegistro()
    {
        var config = new CreditoPersonalConfigViewModel
        {
            Perfiles = new List<PerfilCreditoViewModel>
            {
                new()
                {
                    Id = 0,
                    Nombre = "Conservador",
                    TasaMensual = 4m,
                    GastosAdministrativos = 50m,
                    MinCuotas = 1,
                    MaxCuotas = 12,
                    Activo = true
                }
            }
        };

        await _service.GuardarCreditoPersonalAsync(config);

        var enDb = await _context.PerfilesCredito
            .FirstOrDefaultAsync(p => p.Nombre == "Conservador");
        Assert.NotNull(enDb);
        Assert.Equal(4m, enDb.TasaMensual);
    }

    // -------------------------------------------------------------------------
    // 22. Mezcla de nuevos y existentes — batch resuelve todo en una query
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarCreditoPersonal_MezclaNuevosYExistentes_TodosPersisten()
    {
        var perfilExistente = await SeedPerfil(tasaMensual: 5m);

        var config = new CreditoPersonalConfigViewModel
        {
            Perfiles = new List<PerfilCreditoViewModel>
            {
                new()
                {
                    Id = perfilExistente.Id,
                    Nombre = perfilExistente.Nombre,
                    TasaMensual = 7m,
                    GastosAdministrativos = 0m,
                    MinCuotas = 1,
                    MaxCuotas = 24,
                    Activo = true
                },
                new()
                {
                    Id = 0,
                    Nombre = "Riesgoso",
                    TasaMensual = 12m,
                    GastosAdministrativos = 500m,
                    MinCuotas = 1,
                    MaxCuotas = 6,
                    Activo = true
                }
            }
        };

        await _service.GuardarCreditoPersonalAsync(config);

        var totalPerfiles = await _context.PerfilesCredito.CountAsync(p => !p.IsDeleted);
        Assert.Equal(2, totalPerfiles);

        var actualizado = await _context.PerfilesCredito.FindAsync(perfilExistente.Id);
        Assert.Equal(7m, actualizado!.TasaMensual);

        var nuevo = await _context.PerfilesCredito
            .FirstOrDefaultAsync(p => p.Nombre == "Riesgoso");
        Assert.NotNull(nuevo);
    }

    // -------------------------------------------------------------------------
    // 23. ID inexistente en lista — se ignora sin lanzar excepción
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarCreditoPersonal_IdInexistente_SeIgnora()
    {
        var config = new CreditoPersonalConfigViewModel
        {
            Perfiles = new List<PerfilCreditoViewModel>
            {
                new()
                {
                    Id = 99999,
                    Nombre = "Fantasma",
                    TasaMensual = 5m,
                    GastosAdministrativos = 0m,
                    MinCuotas = 1,
                    MaxCuotas = 12,
                    Activo = true
                }
            }
        };

        // No debe lanzar excepción
        await _service.GuardarCreditoPersonalAsync(config);

        var count = await _context.PerfilesCredito.CountAsync();
        Assert.Equal(0, count);
    }

    // =========================================================================
    // GetAllAsync / GetByIdAsync / GetByTipoPagoAsync
    // =========================================================================

    [Fact]
    public async Task GetAll_SinConfigs_RetornaVacio()
    {
        var resultado = await _service.GetAllAsync();

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetAll_ConConfigs_RetornaTodasActivas()
    {
        await SeedConfigPago(TipoPago.Efectivo);
        await SeedConfigPago(TipoPago.Transferencia);

        var resultado = await _service.GetAllAsync();

        Assert.True(resultado.Count >= 2);
    }

    [Fact]
    public async Task GetById_Existente_RetornaViewModel()
    {
        var config = await SeedConfigPago(TipoPago.Transferencia);

        var resultado = await _service.GetByIdAsync(config.Id);

        Assert.NotNull(resultado);
        Assert.Equal(config.Id, resultado!.Id);
        Assert.Equal(TipoPago.Transferencia, resultado.TipoPago);
    }

    [Fact]
    public async Task GetById_Inexistente_RetornaNull()
    {
        var resultado = await _service.GetByIdAsync(99999);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetByTipoPago_Existente_RetornaViewModel()
    {
        await SeedConfigPago(TipoPago.Cheque, permiteDescuento: true);

        var resultado = await _service.GetByTipoPagoAsync(TipoPago.Cheque);

        Assert.NotNull(resultado);
        Assert.Equal(TipoPago.Cheque, resultado!.TipoPago);
        Assert.True(resultado.PermiteDescuento);
    }

    [Fact]
    public async Task GetByTipoPago_Inexistente_RetornaNull()
    {
        var resultado = await _service.GetByTipoPagoAsync(TipoPago.Cheque);

        Assert.Null(resultado);
    }

    // =========================================================================
    // CreateAsync / UpdateAsync / DeleteAsync
    // =========================================================================

    [Fact]
    public async Task Create_Persiste_RetornaViewModel()
    {
        var vm = new ConfiguracionPagoViewModel
        {
            TipoPago = TipoPago.Transferencia,
            Nombre = "Transferencia Bancaria",
            Activo = true,
            PermiteDescuento = true,
            PorcentajeDescuentoMaximo = 5m
        };

        var resultado = await _service.CreateAsync(vm);

        Assert.True(resultado.Id > 0);
        Assert.Equal(TipoPago.Transferencia, resultado.TipoPago);
        var enDb = await _context.ConfiguracionesPago.FindAsync(resultado.Id);
        Assert.NotNull(enDb);
    }

    [Fact]
    public async Task Update_Existente_ActualizaCampos()
    {
        var config = await SeedConfigPago(TipoPago.Efectivo, tieneRecargo: false);

        var vm = new ConfiguracionPagoViewModel
        {
            Id = config.Id,
            TipoPago = TipoPago.Efectivo,
            Nombre = "Efectivo Actualizado",
            Activo = true,
            TieneRecargo = true,
            PorcentajeRecargo = 3m
        };

        var resultado = await _service.UpdateAsync(config.Id, vm);

        Assert.NotNull(resultado);
        Assert.Equal("Efectivo Actualizado", resultado!.Nombre);
        Assert.True(resultado.TieneRecargo);
        Assert.Equal(3m, resultado.PorcentajeRecargo);
    }

    [Fact]
    public async Task Update_Inexistente_RetornaNull()
    {
        var vm = new ConfiguracionPagoViewModel
        {
            TipoPago = TipoPago.Efectivo,
            Nombre = "X",
            Activo = true
        };

        var resultado = await _service.UpdateAsync(99999, vm);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task Delete_Existente_RetornaTrue_YSoftDelete()
    {
        var config = await SeedConfigPago(TipoPago.Efectivo);

        var resultado = await _service.DeleteAsync(config.Id);

        Assert.True(resultado);
        _context.ChangeTracker.Clear();
        var enDb = await _context.ConfiguracionesPago
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == config.Id);
        Assert.True(enDb.IsDeleted);
    }

    [Fact]
    public async Task Delete_Inexistente_RetornaFalse()
    {
        var resultado = await _service.DeleteAsync(99999);

        Assert.False(resultado);
    }

    // =========================================================================
    // GuardarConfiguracionesModalAsync
    // =========================================================================

    [Fact]
    public async Task GuardarConfiguracionesModal_ListaVacia_NoHaceNada()
    {
        // no debe lanzar excepción
        await _service.GuardarConfiguracionesModalAsync(new List<ConfiguracionPagoViewModel>());
    }

    [Fact]
    public async Task GuardarConfiguracionesModal_ActualizaExistente()
    {
        var config = await SeedConfigPago(TipoPago.Efectivo, permiteDescuento: false);

        var vms = new List<ConfiguracionPagoViewModel>
        {
            new()
            {
                Id = config.Id,
                TipoPago = TipoPago.Efectivo,
                Nombre = "Efectivo Modal",
                Activo = true,
                PermiteDescuento = true,
                PorcentajeDescuentoMaximo = 15m
            }
        };

        await _service.GuardarConfiguracionesModalAsync(vms);

        _context.ChangeTracker.Clear();
        var enDb = await _context.ConfiguracionesPago.FindAsync(config.Id);
        Assert.Equal("Efectivo Modal", enDb!.Nombre);
        Assert.True(enDb.PermiteDescuento);
    }

    [Fact]
    public async Task GuardarConfiguracionesModal_CreaRegistroConIdCero()
    {
        var vms = new List<ConfiguracionPagoViewModel>
        {
            new()
            {
                Id = 0,
                TipoPago = TipoPago.Cheque,
                Nombre = "Cheque Nuevo",
                Activo = true
            }
        };

        await _service.GuardarConfiguracionesModalAsync(vms);

        var enDb = await _context.ConfiguracionesPago
            .FirstOrDefaultAsync(c => c.TipoPago == TipoPago.Cheque);
        Assert.NotNull(enDb);
    }

    [Fact]
    public async Task GuardarConfiguracionesModal_ActualizaConfiguracionTarjeta()
    {
        var config = await SeedConfigPago(TipoPago.Tarjeta);
        var tarjeta = await SeedTarjetaAsync(config.Id);

        var vms = new List<ConfiguracionPagoViewModel>
        {
            new()
            {
                Id = config.Id,
                TipoPago = TipoPago.Tarjeta,
                Nombre = "Tarjeta",
                Activo = true,
                ConfiguracionesTarjeta = new List<ConfiguracionTarjetaViewModel>
                {
                    new()
                    {
                        Id = tarjeta.Id,
                        ConfiguracionPagoId = config.Id,
                        NombreTarjeta = tarjeta.NombreTarjeta,
                        TipoTarjeta = TipoTarjeta.Credito,
                        Activa = true,
                        PermiteCuotas = true,
                        CantidadMaximaCuotas = 12,
                        TipoCuota = TipoCuotaTarjeta.ConInteres,
                        TasaInteresesMensual = 8.5m
                    }
                }
            }
        };

        await _service.GuardarConfiguracionesModalAsync(vms);

        _context.ChangeTracker.Clear();
        var enDb = await _context.ConfiguracionesTarjeta.FindAsync(tarjeta.Id);
        Assert.True(enDb!.PermiteCuotas);
        Assert.Equal(12, enDb.CantidadMaximaCuotas);
        Assert.Equal(TipoCuotaTarjeta.ConInteres, enDb.TipoCuota);
        Assert.Equal(8.5m, enDb.TasaInteresesMensual);
    }

    [Fact]
    public async Task GuardarConfiguracionesModal_ConInteresSinTasa_Rechaza()
    {
        var config = await SeedConfigPago(TipoPago.Tarjeta);
        var tarjeta = await SeedTarjetaAsync(config.Id, permiteCuotas: true, cantidadMaximaCuotas: 12,
            tipoCuota: TipoCuotaTarjeta.ConInteres, tasaInteresesMensual: 4m);

        var vms = new List<ConfiguracionPagoViewModel>
        {
            new()
            {
                Id = config.Id,
                TipoPago = TipoPago.Tarjeta,
                Nombre = "Tarjeta",
                Activo = true,
                ConfiguracionesTarjeta = new List<ConfiguracionTarjetaViewModel>
                {
                    new()
                    {
                        Id = tarjeta.Id,
                        ConfiguracionPagoId = config.Id,
                        NombreTarjeta = tarjeta.NombreTarjeta,
                        TipoTarjeta = TipoTarjeta.Credito,
                        Activa = true,
                        PermiteCuotas = true,
                        CantidadMaximaCuotas = 12,
                        TipoCuota = TipoCuotaTarjeta.ConInteres,
                        TasaInteresesMensual = null
                    }
                }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.GuardarConfiguracionesModalAsync(vms));
        Assert.Contains("tasa de interés mensual", ex.Message, StringComparison.OrdinalIgnoreCase);

        _context.ChangeTracker.Clear();
        var enDb = await _context.ConfiguracionesTarjeta.FindAsync(tarjeta.Id);
        Assert.Equal(4m, enDb!.TasaInteresesMensual);
    }

    [Fact]
    public async Task GuardarConfiguracionesModal_SinInteresConTasa_LimpiaTasa()
    {
        var config = await SeedConfigPago(TipoPago.Tarjeta);
        var tarjeta = await SeedTarjetaAsync(config.Id, permiteCuotas: true, cantidadMaximaCuotas: 12,
            tipoCuota: TipoCuotaTarjeta.ConInteres, tasaInteresesMensual: 4m);

        var vms = new List<ConfiguracionPagoViewModel>
        {
            new()
            {
                Id = config.Id,
                TipoPago = TipoPago.Tarjeta,
                Nombre = "Tarjeta",
                Activo = true,
                ConfiguracionesTarjeta = new List<ConfiguracionTarjetaViewModel>
                {
                    new()
                    {
                        Id = tarjeta.Id,
                        ConfiguracionPagoId = config.Id,
                        NombreTarjeta = tarjeta.NombreTarjeta,
                        TipoTarjeta = TipoTarjeta.Credito,
                        Activa = true,
                        PermiteCuotas = true,
                        CantidadMaximaCuotas = 12,
                        TipoCuota = TipoCuotaTarjeta.SinInteres,
                        TasaInteresesMensual = 9m
                    }
                }
            }
        };

        await _service.GuardarConfiguracionesModalAsync(vms);

        _context.ChangeTracker.Clear();
        var enDb = await _context.ConfiguracionesTarjeta.FindAsync(tarjeta.Id);
        Assert.Equal(TipoCuotaTarjeta.SinInteres, enDb!.TipoCuota);
        Assert.Null(enDb.TasaInteresesMensual);
    }

    [Fact]
    public async Task GuardarConfiguracionesModal_ConInteresConTasaValida_SiguePasando()
    {
        var config = await SeedConfigPago(TipoPago.Tarjeta);
        var tarjeta = await SeedTarjetaAsync(config.Id);

        var vms = new List<ConfiguracionPagoViewModel>
        {
            new()
            {
                Id = config.Id,
                TipoPago = TipoPago.Tarjeta,
                Nombre = "Tarjeta",
                Activo = true,
                ConfiguracionesTarjeta = new List<ConfiguracionTarjetaViewModel>
                {
                    new()
                    {
                        Id = tarjeta.Id,
                        ConfiguracionPagoId = config.Id,
                        NombreTarjeta = tarjeta.NombreTarjeta,
                        TipoTarjeta = TipoTarjeta.Credito,
                        Activa = true,
                        PermiteCuotas = true,
                        CantidadMaximaCuotas = 12,
                        TipoCuota = TipoCuotaTarjeta.ConInteres,
                        TasaInteresesMensual = 6.25m
                    }
                }
            }
        };

        await _service.GuardarConfiguracionesModalAsync(vms);

        _context.ChangeTracker.Clear();
        var enDb = await _context.ConfiguracionesTarjeta.FindAsync(tarjeta.Id);
        Assert.Equal(TipoCuotaTarjeta.ConInteres, enDb!.TipoCuota);
        Assert.Equal(6.25m, enDb.TasaInteresesMensual);
    }

    // =========================================================================
    // GetTarjetasActivasAsync / GetTarjetaByIdAsync
    // =========================================================================

    private async Task<ConfiguracionTarjeta> SeedTarjetaAsync(
        int configPagoId,
        bool activa = true,
        string? nombre = null,
        TipoTarjeta tipoTarjeta = TipoTarjeta.Credito,
        bool permiteCuotas = false,
        int? cantidadMaximaCuotas = null,
        TipoCuotaTarjeta? tipoCuota = null,
        decimal? tasaInteresesMensual = null,
        bool tieneRecargoDebito = false,
        decimal? porcentajeRecargoDebito = null)
    {
        var t = new ConfiguracionTarjeta
        {
            ConfiguracionPagoId = configPagoId,
            NombreTarjeta = nombre ?? "Visa-" + Guid.NewGuid().ToString("N")[..4],
            TipoTarjeta = tipoTarjeta,
            Activa = activa,
            PermiteCuotas = permiteCuotas,
            CantidadMaximaCuotas = cantidadMaximaCuotas,
            TipoCuota = tipoCuota,
            TasaInteresesMensual = tasaInteresesMensual,
            TieneRecargoDebito = tieneRecargoDebito,
            PorcentajeRecargoDebito = porcentajeRecargoDebito
        };
        _context.ConfiguracionesTarjeta.Add(t);
        await _context.SaveChangesAsync();
        return t;
    }

    [Fact]
    public async Task GetTarjetasActivas_SinTarjetas_RetornaVacio()
    {
        var resultado = await _service.GetTarjetasActivasAsync();

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetTarjetasActivas_FiltroActiva_ExcluyeInactivas()
    {
        var config = await SeedConfigPago(TipoPago.Tarjeta);
        var activa = await SeedTarjetaAsync(config.Id, activa: true);
        var inactiva = await SeedTarjetaAsync(config.Id, activa: false);

        var resultado = await _service.GetTarjetasActivasAsync();

        Assert.Contains(resultado, t => t.Id == activa.Id);
        Assert.DoesNotContain(resultado, t => t.Id == inactiva.Id);
    }

    [Fact]
    public async Task GetTarjetasActivasParaVenta_FiltroActiva_ExcluyeInactivas()
    {
        var config = await SeedConfigPago(TipoPago.Tarjeta);
        var activa = await SeedTarjetaAsync(config.Id, activa: true);
        var inactiva = await SeedTarjetaAsync(config.Id, activa: false);

        var resultado = await _service.GetTarjetasActivasParaVentaAsync();

        Assert.Contains(resultado, t => t.Id == activa.Id);
        Assert.DoesNotContain(resultado, t => t.Id == inactiva.Id);
    }

    [Fact]
    public async Task GetTarjetasActivasParaVenta_DevuelveCamposEsperados()
    {
        var config = await SeedConfigPago(TipoPago.Tarjeta);
        var tarjeta = await SeedTarjetaAsync(
            config.Id,
            nombre: "Visa Venta",
            tipoTarjeta: TipoTarjeta.Credito,
            permiteCuotas: true,
            cantidadMaximaCuotas: 12,
            tipoCuota: TipoCuotaTarjeta.ConInteres,
            tasaInteresesMensual: 5.5m,
            tieneRecargoDebito: true,
            porcentajeRecargoDebito: 2.25m);

        var resultado = await _service.GetTarjetasActivasParaVentaAsync();

        var dto = Assert.Single(resultado);
        Assert.Equal(tarjeta.Id, dto.Id);
        Assert.Equal("Visa Venta", dto.Nombre);
        Assert.Equal(TipoTarjeta.Credito, dto.Tipo);
        Assert.True(dto.PermiteCuotas);
        Assert.Equal(12, dto.CantidadMaximaCuotas);
        Assert.Equal(TipoCuotaTarjeta.ConInteres, dto.TipoCuota);
        Assert.Equal(5.5m, dto.TasaInteres);
        Assert.True(dto.TieneRecargo);
        Assert.Equal(2.25m, dto.PorcentajeRecargo);
    }

    [Fact]
    public async Task GetTarjetasActivasParaVenta_MantieneOrdenPorTipoYNombre()
    {
        var config = await SeedConfigPago(TipoPago.Tarjeta);
        var creditoZ = await SeedTarjetaAsync(config.Id, nombre: "Zeta", tipoTarjeta: TipoTarjeta.Credito);
        var debitoA = await SeedTarjetaAsync(config.Id, nombre: "Alfa", tipoTarjeta: TipoTarjeta.Debito);
        var creditoA = await SeedTarjetaAsync(config.Id, nombre: "Alfa", tipoTarjeta: TipoTarjeta.Credito);

        var resultado = await _service.GetTarjetasActivasParaVentaAsync();

        Assert.Equal(new[] { debitoA.Id, creditoA.Id, creditoZ.Id }, resultado.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task GetTarjetaById_Existente_RetornaViewModel()
    {
        var config = await SeedConfigPago(TipoPago.Tarjeta);
        var tarjeta = await SeedTarjetaAsync(config.Id);

        var resultado = await _service.GetTarjetaByIdAsync(tarjeta.Id);

        Assert.NotNull(resultado);
        Assert.Equal(tarjeta.Id, resultado!.Id);
    }

    [Fact]
    public async Task GetTarjetaById_Inexistente_RetornaNull()
    {
        var resultado = await _service.GetTarjetaByIdAsync(99999);

        Assert.Null(resultado);
    }

    // =========================================================================
    // GetPerfilesCreditoAsync / GetPerfilesCreditoActivosAsync
    // =========================================================================

    [Fact]
    public async Task GetPerfilesCredito_SinPerfiles_RetornaVacio()
    {
        var resultado = await _service.GetPerfilesCreditoAsync();

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetPerfilesCredito_RetornaTodosNoEliminados()
    {
        var baseCount = (await _service.GetPerfilesCreditoAsync()).Count;
        var p1 = new PerfilCredito { Nombre = "Perfil-A", TasaMensual = 5m, GastosAdministrativos = 0m, MinCuotas = 1, MaxCuotas = 12, Activo = true };
        var p2 = new PerfilCredito { Nombre = "Perfil-B", TasaMensual = 7m, GastosAdministrativos = 0m, MinCuotas = 1, MaxCuotas = 24, Activo = true };
        _context.PerfilesCredito.AddRange(p1, p2);
        await _context.SaveChangesAsync();

        var resultado = await _service.GetPerfilesCreditoAsync();

        Assert.Equal(baseCount + 2, resultado.Count);
    }

    [Fact]
    public async Task GetPerfilesCreditoActivos_FiltraInactivos()
    {
        var activo = await SeedPerfil();
        var inactivo = new PerfilCredito
        {
            Nombre = "Inactivo", TasaMensual = 5m, GastosAdministrativos = 0m,
            MinCuotas = 1, MaxCuotas = 12, Activo = false
        };
        _context.PerfilesCredito.Add(inactivo);
        await _context.SaveChangesAsync();

        var resultado = await _service.GetPerfilesCreditoActivosAsync();

        Assert.Contains(resultado, p => p.Id == activo.Id);
        Assert.DoesNotContain(resultado, p => p.Id == inactivo.Id);
    }

    // =========================================================================
    // ResolverRangoCuotasAsync
    // =========================================================================

    [Fact]
    public async Task ResolverRangoCuotas_UsarPerfil_RetornaRangoDePerfil()
    {
        var perfil = await SeedPerfil(minCuotas: 3, maxCuotas: 18);

        var (min, max, desc, nombre) = await _service.ResolverRangoCuotasAsync(
            MetodoCalculoCredito.UsarPerfil, perfil.Id, null);

        Assert.Equal(3, min);
        Assert.Equal(18, max);
        Assert.Equal("TestPerfil", nombre);
    }

    [Fact]
    public async Task ResolverRangoCuotas_UsarCliente_RetornaRangoDeCliente()
    {
        var cliente = await SeedCliente(cuotasMaxPersonalizadas: 6);

        var (min, max, desc, nombre) = await _service.ResolverRangoCuotasAsync(
            MetodoCalculoCredito.UsarCliente, null, cliente.Id);

        Assert.True(max <= 6);
    }

    // =========================================================================
    // ObtenerMaxCuotasSinInteresEfectivoAsync
    // =========================================================================

    private async Task<ConfiguracionTarjeta> SeedTarjetaSinInteres(int maxCuotas = 12)
    {
        var config = await SeedConfigPago(TipoPago.TarjetaCredito);
        var tarjeta = new ConfiguracionTarjeta
        {
            ConfiguracionPagoId = config.Id,
            NombreTarjeta = "Visa-Test",
            TipoTarjeta = TipoTarjeta.Credito,
            Activa = true,
            PermiteCuotas = true,
            CantidadMaximaCuotas = maxCuotas,
            TipoCuota = TipoCuotaTarjeta.SinInteres
        };
        _context.ConfiguracionesTarjeta.Add(tarjeta);
        await _context.SaveChangesAsync();
        return tarjeta;
    }

    private async Task<ConfiguracionTarjeta> SeedTarjetaConInteres(int maxCuotas = 12)
    {
        var config = await SeedConfigPago(TipoPago.TarjetaCredito);
        var tarjeta = new ConfiguracionTarjeta
        {
            ConfiguracionPagoId = config.Id,
            NombreTarjeta = "Visa-ConInteres-Test",
            TipoTarjeta = TipoTarjeta.Credito,
            Activa = true,
            PermiteCuotas = true,
            CantidadMaximaCuotas = maxCuotas,
            TipoCuota = TipoCuotaTarjeta.ConInteres,
            TasaInteresesMensual = 3m
        };
        _context.ConfiguracionesTarjeta.Add(tarjeta);
        await _context.SaveChangesAsync();
        return tarjeta;
    }

    private async Task<Producto> SeedProductoConMaxCuotas(int? maxCuotas)
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = codigo, Nombre = "Cat-" + codigo, Activo = true };
        var marca = new Marca { Codigo = codigo, Nombre = "Marca-" + codigo, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = Guid.NewGuid().ToString("N")[..8],
            Nombre = "Prod-Test",
            CategoriaId = cat.Id,
            MarcaId = marca.Id,
            PrecioCompra = 10m,
            PrecioVenta = 50m,
            PorcentajeIVA = 21m,
            Activo = true,
            MaxCuotasSinInteresPermitidas = maxCuotas
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    [Fact]
    public async Task MaxCuotasEfectivo_SinProductosRestringidos_DevuelveMaxTarjeta()
    {
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 12);
        var prod = await SeedProductoConMaxCuotas(null);

        var resultado = await _service.ObtenerMaxCuotasSinInteresEfectivoAsync(tarjeta.Id, new[] { prod.Id });

        Assert.NotNull(resultado);
        Assert.Equal(12, resultado.MaxCuotas);
        Assert.False(resultado.LimitadoPorProducto);
    }

    [Fact]
    public async Task MaxCuotasEfectivo_ProductoMax6TarjetaMax12_Devuelve6()
    {
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 12);
        var prod = await SeedProductoConMaxCuotas(6);

        var resultado = await _service.ObtenerMaxCuotasSinInteresEfectivoAsync(tarjeta.Id, new[] { prod.Id });

        Assert.NotNull(resultado);
        Assert.Equal(6, resultado.MaxCuotas);
        Assert.True(resultado.LimitadoPorProducto);
    }

    [Fact]
    public async Task MaxCuotasEfectivo_VariosProductos_DevuelveMinimoComun()
    {
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 12);
        var prod6 = await SeedProductoConMaxCuotas(6);
        var prod3 = await SeedProductoConMaxCuotas(3);

        var resultado = await _service.ObtenerMaxCuotasSinInteresEfectivoAsync(
            tarjeta.Id, new[] { prod6.Id, prod3.Id });

        Assert.NotNull(resultado);
        Assert.Equal(3, resultado.MaxCuotas);
        Assert.True(resultado.LimitadoPorProducto);
    }

    [Fact]
    public async Task MaxCuotasEfectivo_ProductoNullSeIgnora()
    {
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 12);
        var prodSinRestr = await SeedProductoConMaxCuotas(null);
        var prodConRestr = await SeedProductoConMaxCuotas(6);

        var resultado = await _service.ObtenerMaxCuotasSinInteresEfectivoAsync(
            tarjeta.Id, new[] { prodSinRestr.Id, prodConRestr.Id });

        Assert.NotNull(resultado);
        Assert.Equal(6, resultado.MaxCuotas);
        Assert.True(resultado.LimitadoPorProducto);
    }

    [Fact]
    public async Task MaxCuotasEfectivo_TarjetaMax3ProductoMax6_DevuelveTarjetaGana()
    {
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 3);
        var prod = await SeedProductoConMaxCuotas(6);

        var resultado = await _service.ObtenerMaxCuotasSinInteresEfectivoAsync(tarjeta.Id, new[] { prod.Id });

        Assert.NotNull(resultado);
        Assert.Equal(3, resultado.MaxCuotas);
        Assert.False(resultado.LimitadoPorProducto);
    }

    [Fact]
    public async Task MaxCuotasEfectivo_TarjetaConInteres_DevuelveNull()
    {
        var tarjeta = await SeedTarjetaConInteres(maxCuotas: 12);
        var prod = await SeedProductoConMaxCuotas(6);

        var resultado = await _service.ObtenerMaxCuotasSinInteresEfectivoAsync(tarjeta.Id, new[] { prod.Id });

        Assert.Null(resultado);
    }

    [Fact]
    public async Task MaxCuotasEfectivo_TarjetaInexistente_DevuelveNull()
    {
        var resultado = await _service.ObtenerMaxCuotasSinInteresEfectivoAsync(int.MaxValue, Array.Empty<int>());

        Assert.Null(resultado);
    }

    [Fact]
    public async Task MaxCuotasEfectivo_SinProductos_DevuelveMaxTarjeta()
    {
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 9);

        var resultado = await _service.ObtenerMaxCuotasSinInteresEfectivoAsync(tarjeta.Id, Array.Empty<int>());

        Assert.NotNull(resultado);
        Assert.Equal(9, resultado.MaxCuotas);
        Assert.False(resultado.LimitadoPorProducto);
    }

    [Fact]
    public async Task MaxCuotasEfectivo_TarjetaMax1ProductoMax1_DevuelveMin1()
    {
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 1);
        var prod = await SeedProductoConMaxCuotas(1);

        var resultado = await _service.ObtenerMaxCuotasSinInteresEfectivoAsync(tarjeta.Id, new[] { prod.Id });

        Assert.NotNull(resultado);
        Assert.Equal(1, resultado.MaxCuotas);
    }

    [Fact]
    public async Task MaxCuotasEfectivo_ProductoIdInexistente_SeIgnoraYUsaMaxTarjeta()
    {
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 12);

        var resultado = await _service.ObtenerMaxCuotasSinInteresEfectivoAsync(
            tarjeta.Id, new[] { int.MaxValue });

        Assert.NotNull(resultado);
        Assert.Equal(12, resultado.MaxCuotas);
        Assert.False(resultado.LimitadoPorProducto);
    }
}
