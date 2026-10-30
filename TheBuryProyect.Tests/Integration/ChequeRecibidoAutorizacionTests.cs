using System.Net;
using System.Reflection;
using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Data;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Hito 10 - autorización del circuito del cheque recibido: Acreditar/Rechazar exigen caja.edit y poder operar la apertura REAL
/// del movimiento (anti-IDOR, igual que FIN-S-006), llevan antiforgery y no filtran la existencia de movimientos ajenos.
/// </summary>
public class ChequeRecibidoAutorizacionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CajaService _service;
    private readonly CajaVendedorService _padron;

    public ChequeRecibidoAutorizacionTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        _service = new CajaService(_context, mapper, NullLogger<CajaService>.Instance, new StubNotificacionService());
        _padron = new CajaVendedorService(_context, null!, NullLogger<CajaVendedorService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class UsuarioStub(string id, bool puedeEditar, params string[] roles) : ICurrentUserService
    {
        public string GetUsername() => id;
        public string GetUserId() => id;
        public bool IsAuthenticated() => true;
        public string? GetEmail() => null;
        public bool IsInRole(string role) => roles.Contains(role);
        // "update" convierte al usuario en administrador de cajas (EsAdminCaja): un cajero común solo tiene view/edit.
        public bool HasPermission(string modulo, string accion) =>
            roles.Contains(Roles.Administrador) || accion == "view" || (accion == "edit" && puedeEditar);
        public string? GetIpAddress() => null;
    }

    private sealed class TempDataProviderStub : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private CajaController ControllerPara(string userId, bool puedeEditar = true, params string[] roles)
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        var controller = new CajaController(
            _service, _padron, new UsuarioStub(userId, puedeEditar, roles), NullLogger<CajaController>.Instance,
            mapper, _context, new RelojComercialFake(), chequeService: _service);
        var http = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, new TempDataProviderStub());
        return controller;
    }

    private async Task<(AperturaCaja Apertura, MovimientoCaja Cheque)> SeedCajaConChequeAsync(string miembro)
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var caja = new Caja { Codigo = codigo, Nombre = "Caja-" + codigo, Activa = true, Estado = EstadoCaja.Cerrada };
        _context.Cajas.Add(caja);
        await _context.SaveChangesAsync();

        if (_context.Users.Find(miembro) == null)
        {
            _context.Users.Add(new ApplicationUser
            {
                Id = miembro, UserName = miembro, NormalizedUserName = miembro.ToUpperInvariant(),
                Email = $"{miembro}@test.com", NormalizedEmail = $"{miembro}@test.com".ToUpperInvariant(), RowVersion = new byte[8]
            });
            await _context.SaveChangesAsync();
        }
        _context.CajaVendedores.Add(new CajaVendedor { CajaId = caja.Id, VendedorUserId = miembro });
        await _context.SaveChangesAsync();

        var apertura = await _service.AbrirCajaAsync(new AbrirCajaViewModel { CajaId = caja.Id, MontoInicial = 100m }, miembro);
        var mov = new MovimientoCaja
        {
            AperturaCajaId = apertura.Id, FechaMovimiento = DateTime.UtcNow, Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.VentaCheque, EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Pendiente,
            TipoPago = TipoPago.Cheque, Monto = 50m, Descripcion = "Cheque", Referencia = "V-" + codigo, Usuario = miembro
        };
        _context.MovimientosCaja.Add(mov);
        await _context.SaveChangesAsync();
        return (apertura, mov);
    }

    private async Task<EstadoAcreditacionMovimientoCaja?> EstadoAsync(int id)
    {
        _context.ChangeTracker.Clear();
        return (await _context.MovimientosCaja.AsNoTracking().SingleAsync(m => m.Id == id)).EstadoAcreditacion;
    }

    private static string Error(CajaController c) => c.TempData["Error"]?.ToString() ?? "";

    // ------------------------------------------------------------------ acreditar

    [Fact]
    public async Task Acreditar_MiembroDeLaCaja_Acredita()
    {
        var a = await SeedCajaConChequeAsync("cajero-a");

        var result = await ControllerPara("cajero-a").AcreditarCheque(a.Cheque.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, await EstadoAsync(a.Cheque.Id));
    }

    [Fact]
    public async Task Acreditar_MiembroDeOtraCaja_NoPuedeAcreditarElChequeAjeno()
    {
        var a = await SeedCajaConChequeAsync("cajero-a");
        var b = await SeedCajaConChequeAsync("cajero-b");

        var controller = ControllerPara("cajero-a");
        await controller.AcreditarCheque(b.Cheque.Id);

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(b.Cheque.Id));
        Assert.NotEmpty(Error(controller));
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(a.Cheque.Id));
    }

    [Fact]
    public async Task Acreditar_UsuarioSinPadron_NoAcredita()
    {
        var a = await SeedCajaConChequeAsync("cajero-a");

        await ControllerPara("intruso").AcreditarCheque(a.Cheque.Id);

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(a.Cheque.Id));
    }

    [Fact]
    public async Task Acreditar_ChequeInexistente_NoFiltraExistenciaAUnNoSupervisor()
    {
        await SeedCajaConChequeAsync("cajero-a");

        var comun = ControllerPara("cajero-a");
        await comun.AcreditarCheque(999_999);
        var supervisor = ControllerPara("admin", true, Roles.Administrador);
        await supervisor.AcreditarCheque(999_999);

        Assert.DoesNotContain("no encontrado", Error(comun), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no encontrado", Error(supervisor), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Acreditar_ElEndpointGenericoDeAcreditarMovimiento_TambienPasaPorElCircuitoDelCheque()
    {
        var a = await SeedCajaConChequeAsync("cajero-a");

        await ControllerPara("cajero-a").AcreditarMovimiento(a.Cheque.Id, a.Apertura.Id);

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, await EstadoAsync(a.Cheque.Id));
        // La auditoría específica del cheque y su trazabilidad (no hay venta: no hay DatosCheque) no se pierden.
        Assert.Equal(0, await _context.DatosCheque.CountAsync());
    }

    // ------------------------------------------------------------------ rechazar

    [Fact]
    public async Task Rechazar_MiembroDeLaCaja_RechazaConMotivo()
    {
        var a = await SeedCajaConChequeAsync("cajero-a");

        await ControllerPara("cajero-a").RechazarCheque(a.Cheque.Id, "sin fondos");

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Rechazado, await EstadoAsync(a.Cheque.Id));
    }

    [Fact]
    public async Task Rechazar_SinMotivo_NoRechaza_YMuestraElError()
    {
        var a = await SeedCajaConChequeAsync("cajero-a");

        var controller = ControllerPara("cajero-a");
        await controller.RechazarCheque(a.Cheque.Id, "  ");

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(a.Cheque.Id));
        Assert.Contains("motivo", Error(controller), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rechazar_MiembroDeOtraCaja_NoPuedeRechazarElChequeAjeno()
    {
        await SeedCajaConChequeAsync("cajero-a");
        var b = await SeedCajaConChequeAsync("cajero-b");

        await ControllerPara("cajero-a").RechazarCheque(b.Cheque.Id, "intento");

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(b.Cheque.Id));
    }

    // ------------------------------------------------------------------ lista

    [Fact]
    public async Task Lista_UnNoSupervisorSoloVeLosChequesDeSuPadronYSoloAccionaLosQuePuedeOperar()
    {
        var a = await SeedCajaConChequeAsync("cajero-a");
        var b = await SeedCajaConChequeAsync("cajero-b");

        var vista = Assert.IsType<ViewResult>(await ControllerPara("cajero-a").Cheques());
        var lista = Assert.IsType<List<ChequePendienteViewModel>>(vista.Model);

        var fila = Assert.Single(lista);
        Assert.Equal(a.Cheque.Id, fila.MovimientoId);
        Assert.True(fila.PuedeAccionar);

        var supervisor = Assert.IsType<List<ChequePendienteViewModel>>(
            Assert.IsType<ViewResult>(await ControllerPara("admin", true, Roles.Administrador).Cheques()).Model);
        Assert.Equal(2, supervisor.Count);
        Assert.Contains(supervisor, c => c.MovimientoId == b.Cheque.Id && c.PuedeAccionar);
    }

    [Fact]
    public async Task Lista_SinPermisoCajaEdit_NoOfreceAcciones()
    {
        await SeedCajaConChequeAsync("cajero-a");

        var lista = Assert.IsType<List<ChequePendienteViewModel>>(
            Assert.IsType<ViewResult>(await ControllerPara("cajero-a", puedeEditar: false).Cheques()).Model);

        Assert.False(Assert.Single(lista).PuedeAccionar);
    }

    // ------------------------------------------------------------------ contrato de seguridad de los endpoints

    [Theory]
    [InlineData(nameof(CajaController.AcreditarCheque))]
    [InlineData(nameof(CajaController.RechazarCheque))]
    public void LosEndpointsDeEscritura_SonPostConAntiforgeryYExigenCajaEdit(string accion)
    {
        var metodo = typeof(CajaController).GetMethods().Single(m => m.Name == accion);

        Assert.NotNull(metodo.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(metodo.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        var permiso = metodo.GetCustomAttribute<PermisoRequeridoAttribute>();
        Assert.NotNull(permiso);
        Assert.Equal("caja", permiso!.Modulo);
        Assert.Equal("edit", permiso.Accion);
    }
}

/// <summary>El POST sin token antiforgery se rechaza en el pipeline real (antes de llegar a la acción).</summary>
[Collection("HttpIntegration")]
public class ChequeRecibidoAntiforgeryHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public ChequeRecibidoAntiforgeryHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedTestUserAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("/Caja/AcreditarCheque", "movimientoId=1")]
    [InlineData("/Caja/RechazarCheque", "movimientoId=1&motivo=x")]
    public async Task PostSinToken_SeRechaza(string url, string cuerpo)
    {
        var client = _factory.CreateAuthenticatedClient();

        var resp = await client.PostAsync(url, new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ListaDeChequesPendientes_RespondeParaUnUsuarioAutenticado()
    {
        var resp = await _factory.CreateAuthenticatedClient().GetAsync("/Caja/Cheques");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Cheques pendientes", await resp.Content.ReadAsStringAsync());
    }
}
