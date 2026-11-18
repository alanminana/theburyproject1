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
/// V-02 - el cheque recibido es un pago pendiente informativo: la interfaz y los endpoints de Acreditar/Rechazar se retiraron,
/// el endpoint genérico de acreditar movimientos rechaza los cheques y la lista solo muestra los de las cajas visibles.
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

    // ------------------------------------------------------------------ V-02: sin seguimiento de acreditación

    [Fact]
    public async Task AcreditarMovimiento_ConUnChequeRecibido_NoLoAcredita_YExplicaElAlcanceInformativo()
    {
        var a = await SeedCajaConChequeAsync("cajero-a");

        var controller = ControllerPara("cajero-a");
        await controller.AcreditarMovimiento(a.Cheque.Id, a.Apertura.Id);

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(a.Cheque.Id));
        Assert.Contains("no registra su acreditación", Error(controller));
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.AcreditacionCheque));
    }

    [Theory]
    [InlineData("AcreditarCheque")]
    [InlineData("RechazarCheque")]
    [InlineData("ChequesAcreditados")]
    public void LasAccionesDeAcreditarYRechazarChequesYaNoEstanExpuestas(string accion)
    {
        Assert.DoesNotContain(typeof(CajaController).GetMethods(), m => m.Name == accion);
    }

    // ------------------------------------------------------------------ lista informativa

    [Fact]
    public async Task Lista_UnNoSupervisorSoloVeLosChequesDeSuPadron()
    {
        var a = await SeedCajaConChequeAsync("cajero-a");
        var b = await SeedCajaConChequeAsync("cajero-b");

        var vista = Assert.IsType<ViewResult>(await ControllerPara("cajero-a").Cheques());
        var lista = Assert.IsType<List<ChequePendienteViewModel>>(vista.Model);

        Assert.Equal(a.Cheque.Id, Assert.Single(lista).MovimientoId);

        var supervisor = Assert.IsType<List<ChequePendienteViewModel>>(
            Assert.IsType<ViewResult>(await ControllerPara("admin", true, Roles.Administrador).Cheques()).Model);
        Assert.Equal(2, supervisor.Count);
        Assert.Contains(supervisor, c => c.MovimientoId == b.Cheque.Id);
    }
}

/// <summary>V-02: las rutas retiradas ya no existen y la lista informativa responde sin acciones.</summary>
[Collection("HttpIntegration.4")]
public class ChequeRecibidoAntiforgeryHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public ChequeRecibidoAntiforgeryHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedTestUserAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("/Caja/AcreditarCheque", "movimientoId=1")]
    [InlineData("/Caja/RechazarCheque", "movimientoId=1&motivo=x")]
    public async Task LasRutasRetiradasDeAcreditarYRechazar_ResponderNotFound(string url, string cuerpo)
    {
        var client = _factory.CreateAuthenticatedClient();

        var resp = await client.PostAsync(url, new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task LaListaDeAcreditadosYaNoExiste()
    {
        var resp = await _factory.CreateAuthenticatedClient().GetAsync("/Caja/ChequesAcreditados");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task ListaDeChequesRecibidos_RespondeSinAccionesDeAcreditarNiRechazar()
    {
        var resp = await _factory.CreateAuthenticatedClient().GetAsync("/Caja/Cheques");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Cheques recibidos", html);
        Assert.DoesNotContain("data-cheque-acreditar", html);
        Assert.DoesNotContain("data-cheque-rechazo", html);
        Assert.DoesNotContain("Rechazar un cheque acreditado", html);
    }
}
