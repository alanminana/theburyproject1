using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// COM-S-014 + revisión adversarial de #110: la configuración personalizada de crédito (clientes.configurecredit) viaja en
/// el formulario de edición. Sin ese permiso no puede cambiar NI revertir un cambio autorizado concurrente: el servicio no
/// asigna esas propiedades salvo que el llamador lo pida, así que no entran en el UPDATE. Servicio real, mapper real y
/// controller real sobre SQLite.
/// </summary>
public class ClienteConfiguracionCreditoPermisoTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ClienteService _service;
    private readonly IMapper _mapper;

    public ClienteConfiguracionCreditoPermisoTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _service = new ClienteService(_context, NullLogger<ClienteService>.Instance);
        _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class UsuarioStub : ICurrentUserService
    {
        private readonly bool _configureCredit;
        public UsuarioStub(bool configureCredit) => _configureCredit = configureCredit;
        public string GetUsername() => "editor";
        public string GetUserId() => "editor-id";
        public bool IsAuthenticated() => true;
        public string? GetEmail() => null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string modulo, string accion) =>
            _configureCredit && modulo == "clientes" && accion == "configurecredit";
        public string? GetIpAddress() => null;
    }

    private ClienteController CrearController(bool puedeConfigurarCredito) => new(
        _service, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        new UsuarioStub(puedeConfigurarCredito), null!, _mapper, NullLogger<ClienteController>.Instance)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private async Task<Cliente> SeedClienteConConfiguracionAsync()
    {
        var doc = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Cliente", TipoDocumento = "DNI", NumeroDocumento = doc, Email = $"{doc}@test.com",
            Telefono = "341-000-0000", NivelRiesgo = NivelRiesgoCredito.AprobadoCondicional, Activo = true,
            TasaInteresMensualPersonalizada = 3m, GastosAdministrativosPersonalizados = 1m, CuotasMaximasPersonalizadas = 12,
            MontoMinimoPersonalizado = 1000m, MontoMaximoPersonalizado = 100000m
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        await _context.Entry(cliente).ReloadAsync();   // trae el RowVersion que genera SQLite, como el que viaja en el formulario
        _context.ChangeTracker.Clear();
        return cliente;
    }

    private static ClienteViewModel FormularioConConfiguracionNueva(Cliente c) => new()
    {
        Id = c.Id, RowVersion = c.RowVersion, Nombre = c.Nombre, Apellido = c.Apellido, TipoDocumento = c.TipoDocumento,
        NumeroDocumento = c.NumeroDocumento, Email = c.Email, Activo = true, NivelRiesgo = c.NivelRiesgo,
        Telefono = "341-999-9999",
        TasaInteresMensualPersonalizada = 9m, GastosAdministrativosPersonalizados = 8m, CuotasMaximasPersonalizadas = 60,
        MontoMinimoPersonalizado = 5m, MontoMaximoPersonalizado = 999999m
    };

    [Fact]
    public async Task EditAjax_SinConfigureCredit_GuardaLosDemasCamposPeroNoCambiaLaConfiguracionDeCredito()
    {
        var cliente = await SeedClienteConConfiguracionAsync();

        var resultado = await CrearController(puedeConfigurarCredito: false).EditAjax(FormularioConConfiguracionNueva(cliente));

        var json = Assert.IsType<JsonResult>(resultado);
        Assert.True(System.Text.Json.JsonSerializer.Serialize(json.Value).Contains("\"success\":true"), System.Text.Json.JsonSerializer.Serialize(json.Value));
        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal("341-999-9999", bd.Telefono);
        Assert.Equal(3m, bd.TasaInteresMensualPersonalizada);
        Assert.Equal(1m, bd.GastosAdministrativosPersonalizados);
        Assert.Equal(12, bd.CuotasMaximasPersonalizadas);
        Assert.Equal(1000m, bd.MontoMinimoPersonalizado);
        Assert.Equal(100000m, bd.MontoMaximoPersonalizado);
    }

    [Fact]
    public async Task EditAjax_ConConfigureCredit_GuardaLaConfiguracionDeCredito()
    {
        var cliente = await SeedClienteConConfiguracionAsync();

        await CrearController(puedeConfigurarCredito: true).EditAjax(FormularioConConfiguracionNueva(cliente));

        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal(9m, bd.TasaInteresMensualPersonalizada);
        Assert.Equal(8m, bd.GastosAdministrativosPersonalizados);
        Assert.Equal(60, bd.CuotasMaximasPersonalizadas);
        Assert.Equal(5m, bd.MontoMinimoPersonalizado);
        Assert.Equal(999999m, bd.MontoMaximoPersonalizado);
    }

    [Fact]
    public async Task EditAjax_SinConfigureCredit_NoCambiaElNivelDeRiesgo()
    {
        // El formulario de edicion lleva NivelRiesgo en un campo oculto: sin clientes.configurecredit nadie puede
        // subirse la calificacion editando ese valor.
        var cliente = await SeedClienteConConfiguracionAsync();
        var form = FormularioConConfiguracionNueva(cliente);
        form.NivelRiesgo = NivelRiesgoCredito.AprobadoTotal;

        await CrearController(puedeConfigurarCredito: false).EditAjax(form);

        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal(NivelRiesgoCredito.AprobadoCondicional, bd.NivelRiesgo);
        Assert.Equal((int)NivelRiesgoCredito.AprobadoCondicional * 2m, bd.PuntajeRiesgo);
        Assert.Equal("341-999-9999", bd.Telefono);   // el resto del formulario sí se guarda
    }

    [Fact]
    public async Task EditAjax_ConConfigureCredit_CambiaElNivelDeRiesgoYDerivaElPuntaje()
    {
        var cliente = await SeedClienteConConfiguracionAsync();
        var form = FormularioConConfiguracionNueva(cliente);
        form.NivelRiesgo = NivelRiesgoCredito.AprobadoTotal;

        await CrearController(puedeConfigurarCredito: true).EditAjax(form);

        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal(NivelRiesgoCredito.AprobadoTotal, bd.NivelRiesgo);
        Assert.Equal((int)NivelRiesgoCredito.AprobadoTotal * 2m, bd.PuntajeRiesgo);
    }

    [Fact]
    public async Task Update_ServicioSinPedirConfiguracion_NoToca_NivelRiesgoNiPuntaje()
    {
        var cliente = await SeedClienteConConfiguracionAsync();
        var puntajeAntes = (await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id)).PuntajeRiesgo;
        var entrante = new Cliente
        {
            Id = cliente.Id, RowVersion = cliente.RowVersion, Nombre = cliente.Nombre, Apellido = cliente.Apellido,
            TipoDocumento = cliente.TipoDocumento, NumeroDocumento = cliente.NumeroDocumento, Email = cliente.Email,
            Activo = true, Telefono = "341-555-5555", NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };

        await _service.UpdateAsync(entrante);

        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal(NivelRiesgoCredito.AprobadoCondicional, bd.NivelRiesgo);
        Assert.Equal(puntajeAntes, bd.PuntajeRiesgo);
        Assert.Equal("341-555-5555", bd.Telefono);
    }

    [Fact]
    public async Task Update_SinPedirConfiguracion_NoRevierteUnCambioAutorizadoConcurrente()
    {
        // El usuario sin configurecredit abrió el formulario con la configuración vieja (3 %, 12 cuotas)...
        var cliente = await SeedClienteConConfiguracionAsync();
        var entranteConValoresViejos = new Cliente
        {
            Id = cliente.Id, Nombre = cliente.Nombre, Apellido = cliente.Apellido, TipoDocumento = cliente.TipoDocumento,
            NumeroDocumento = cliente.NumeroDocumento, Email = cliente.Email, NivelRiesgo = cliente.NivelRiesgo, Activo = true,
            Telefono = "341-777-7777",
            TasaInteresMensualPersonalizada = 3m, GastosAdministrativosPersonalizados = 1m, CuotasMaximasPersonalizadas = 12,
            MontoMinimoPersonalizado = 1000m, MontoMaximoPersonalizado = 100000m
        };

        // ...y entre tanto otro usuario AUTORIZADO la cambió desde otro contexto.
        await using (var otro = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options))
        {
            var c = await otro.Clientes.FirstAsync(x => x.Id == cliente.Id);
            c.TasaInteresMensualPersonalizada = 7m;
            c.CuotasMaximasPersonalizadas = 24;
            await otro.SaveChangesAsync();
        }

        await _service.UpdateAsync(entranteConValoresViejos);

        var bd = await _context.Clientes.AsNoTracking().FirstAsync(x => x.Id == cliente.Id);
        Assert.Equal("341-777-7777", bd.Telefono);
        Assert.Equal(7m, bd.TasaInteresMensualPersonalizada);
        Assert.Equal(24, bd.CuotasMaximasPersonalizadas);
    }

    // ---- H11: valores históricos de crédito fuera de tope (revisión adversarial de Codex) ----
    // Al llamar a la acción directamente el ModelState lo arma el test como lo dejaría MVC: error de propiedad en el monto
    // histórico (MontoArgentino) y SIN validación de objeto (MVC la omite cuando falla una propiedad).

    private async Task<Cliente> SeedClienteConMaximoHistoricoAsync(decimal? maximo = 100_000_000m)
    {
        var cliente = await SeedClienteConConfiguracionAsync();
        var c = await _context.Clientes.SingleAsync(x => x.Id == cliente.Id);
        c.MontoMaximoPersonalizado = maximo;
        await _context.SaveChangesAsync();
        await _context.Entry(c).ReloadAsync();
        _context.ChangeTracker.Clear();
        return c;
    }

    private static ClienteViewModel FormularioConCreditoSinCambios(Cliente c) => new()
    {
        Id = c.Id, RowVersion = c.RowVersion, Nombre = c.Nombre, Apellido = c.Apellido, TipoDocumento = c.TipoDocumento,
        NumeroDocumento = c.NumeroDocumento, Email = c.Email, Activo = true, NivelRiesgo = c.NivelRiesgo,
        Telefono = "341-999-9999",
        TasaInteresMensualPersonalizada = c.TasaInteresMensualPersonalizada, GastosAdministrativosPersonalizados = c.GastosAdministrativosPersonalizados,
        CuotasMaximasPersonalizadas = c.CuotasMaximasPersonalizadas,
        MontoMinimoPersonalizado = c.MontoMinimoPersonalizado, MontoMaximoPersonalizado = c.MontoMaximoPersonalizado
    };

    private static bool Exito(IActionResult r) =>
        System.Text.Json.JsonSerializer.Serialize(Assert.IsType<JsonResult>(r).Value).Contains("\"success\":true");

    private async Task<(string? Telefono, decimal? Maximo, decimal? Minimo)> LeerAsync(int id)
    {
        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == id);
        return (bd.Telefono, bd.MontoMaximoPersonalizado, bd.MontoMinimoPersonalizado);
    }

    [Fact]
    public async Task EditAjax_ConConfigureCredit_MaximoHistoricoSinCambios_GuardaLosDemasDatos()
    {
        var cliente = await SeedClienteConMaximoHistoricoAsync();
        var controller = CrearController(puedeConfigurarCredito: true);
        controller.ModelState.AddModelError(nameof(ClienteViewModel.MontoMaximoPersonalizado), "monto fuera de tope");

        var ok = Exito(await controller.EditAjax(FormularioConCreditoSinCambios(cliente)));

        Assert.True(ok);
        var (telefono, maximo, _) = await LeerAsync(cliente.Id);
        Assert.Equal("341-999-9999", telefono);
        Assert.Equal(100_000_000m, maximo);
    }

    [Fact]
    public async Task EditAjax_ConConfigureCredit_MinimoSinCambiosYMaximoMenor_SeRechazaPorLaRelacionMinMax()
    {
        // El error cruzado "mínimo > máximo" vive en la clave del mínimo: que el mínimo no haya cambiado no debe borrarlo.
        var cliente = await SeedClienteConMaximoHistoricoAsync(maximo: 100_000m);
        var form = FormularioConCreditoSinCambios(cliente);
        form.MontoMaximoPersonalizado = 500m;                       // menor que el mínimo guardado (1000)
        var controller = CrearController(puedeConfigurarCredito: true);
        controller.ModelState.AddModelError(nameof(ClienteViewModel.MontoMinimoPersonalizado), "El monto mínimo no puede ser mayor al monto máximo.");

        Assert.False(Exito(await controller.EditAjax(form)));

        var (telefono, maximo, _) = await LeerAsync(cliente.Id);
        Assert.NotEqual("341-999-9999", telefono);
        Assert.Equal(100_000m, maximo);
    }

    [Fact]
    public async Task EditAjax_ConConfigureCredit_FechaDeNacimientoInvalidaJuntoAlMaximoHistorico_SeRechaza()
    {
        // MVC no ejecuta Validate (fecha de nacimiento, CUIL) cuando una propiedad falla: al tolerar el máximo se ejecuta igual.
        var cliente = await SeedClienteConMaximoHistoricoAsync();
        var form = FormularioConCreditoSinCambios(cliente);
        form.FechaNacimiento = DateTime.UtcNow.Date.AddDays(10);
        var controller = CrearController(puedeConfigurarCredito: true);
        controller.ModelState.AddModelError(nameof(ClienteViewModel.MontoMaximoPersonalizado), "monto fuera de tope");

        Assert.False(Exito(await controller.EditAjax(form)));

        Assert.NotEqual("341-999-9999", (await LeerAsync(cliente.Id)).Telefono);
    }

    [Fact]
    public async Task EditAjax_SinConfigureCredit_MontoManipuladoConFechaInvalida_SeRechaza_Y_SinFechaGuardaConElCreditoIntacto()
    {
        var cliente = await SeedClienteConMaximoHistoricoAsync();

        var conFecha = FormularioConCreditoSinCambios(cliente);
        conFecha.MontoMaximoPersonalizado = 30_000_000m;            // manipulado: se descarta, pero la fecha inválida no
        conFecha.FechaNacimiento = DateTime.UtcNow.Date.AddDays(10);
        var c1 = CrearController(puedeConfigurarCredito: false);
        c1.ModelState.AddModelError(nameof(ClienteViewModel.MontoMaximoPersonalizado), "monto fuera de tope");
        Assert.False(Exito(await c1.EditAjax(conFecha)));
        Assert.NotEqual("341-999-9999", (await LeerAsync(cliente.Id)).Telefono);

        var sinFecha = FormularioConCreditoSinCambios(cliente);
        sinFecha.MontoMaximoPersonalizado = 30_000_000m;
        var c2 = CrearController(puedeConfigurarCredito: false);
        c2.ModelState.AddModelError(nameof(ClienteViewModel.MontoMaximoPersonalizado), "monto fuera de tope");
        Assert.True(Exito(await c2.EditAjax(sinFecha)));
        var (telefono, maximo, _) = await LeerAsync(cliente.Id);
        Assert.Equal("341-999-9999", telefono);
        Assert.Equal(100_000_000m, maximo);                          // el crédito no se tocó
    }

    [Fact]
    public async Task EditAjax_ConConfigureCredit_MaximoNoInterpretable_ConGuardadoNull_SeRechaza()
    {
        var cliente = await SeedClienteConMaximoHistoricoAsync(maximo: null);
        var form = FormularioConCreditoSinCambios(cliente);         // el binder dejó null el "abc" escrito
        var controller = CrearController(puedeConfigurarCredito: true);
        controller.ModelState.SetModelValue(nameof(ClienteViewModel.MontoMaximoPersonalizado), "abc", "abc");
        controller.ModelState.AddModelError(nameof(ClienteViewModel.MontoMaximoPersonalizado), "Ingresá un monto válido.");

        Assert.False(Exito(await controller.EditAjax(form)));

        Assert.NotEqual("341-999-9999", (await LeerAsync(cliente.Id)).Telefono);
    }

    [Fact]
    public async Task EditAjax_SinConfigureCredit_ValorNoInterpretable_SeRechazaAunqueElCreditoSeDescarte()
    {
        // Revisión Codex (2ª): sin el permiso el valor se sustituye por el guardado, pero un error de conversión del valor
        // escrito no se tolera igual (el error se detecta antes de sustituir).
        var cliente = await SeedClienteConMaximoHistoricoAsync(maximo: null);
        var form = FormularioConCreditoSinCambios(cliente);
        var controller = CrearController(puedeConfigurarCredito: false);
        controller.ModelState.SetModelValue(nameof(ClienteViewModel.MontoMaximoPersonalizado), "abc", "abc");
        controller.ModelState.AddModelError(nameof(ClienteViewModel.MontoMaximoPersonalizado), "Ingresá un monto válido.");

        Assert.False(Exito(await controller.EditAjax(form)));

        Assert.NotEqual("341-999-9999", (await LeerAsync(cliente.Id)).Telefono);
    }
}
