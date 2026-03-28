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
    // ObtenerTasaInteresMensualCreditoPersonalAsync — auto-create si no existe
    // =========================================================================

    // -------------------------------------------------------------------------
    // 17. Sin configuración existente → crea con tasa 0 y la devuelve
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerTasa_SinConfig_CreaRegistroYRetornaCero()
    {
        var result = await _service.ObtenerTasaInteresMensualCreditoPersonalAsync();

        Assert.Equal(0m, result);

        var enDb = await _context.ConfiguracionesPago
            .FirstOrDefaultAsync(c => c.TipoPago == TipoPago.CreditoPersonal);
        Assert.NotNull(enDb);
    }

    // -------------------------------------------------------------------------
    // 18. Con configuración existente → devuelve tasa registrada
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerTasa_ConConfig_RetornaTasaRegistrada()
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
}
