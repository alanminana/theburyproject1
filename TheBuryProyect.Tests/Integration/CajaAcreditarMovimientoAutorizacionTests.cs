using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Helpers;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FIN-S-006: <c>POST /Caja/AcreditarMovimiento</c> autorizaba sobre el <c>aperturaId</c> del request mientras el
/// servicio acreditaba el <c>movimientoId</c> sin comprobar que perteneciera a esa apertura (IDOR).
/// La autorización ahora se decide sobre la apertura REAL del movimiento y el aperturaId enviado debe coincidir.
/// Usa el padrón real (<c>CajaVendedores</c> vía <see cref="CajaVendedorService"/>) y la regla existente de supervisores.
/// </summary>
public class CajaAcreditarMovimientoAutorizacionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CajaService _service;
    private readonly CajaVendedorService _padron;

    public CajaAcreditarMovimientoAutorizacionTests()
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

    private sealed class UsuarioStub(string id, params string[] roles) : ICurrentUserService
    {
        public string GetUsername() => id;
        public string GetUserId() => id;
        public bool IsAuthenticated() => true;
        public string? GetEmail() => null;
        public bool IsInRole(string role) => roles.Contains(role);
        public bool HasPermission(string modulo, string accion) => true;
        public string? GetIpAddress() => null;
    }

    private sealed class TempDataProviderStub : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private CajaController ControllerPara(string userId, params string[] roles)
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        var controller = new CajaController(
            _service, _padron, new UsuarioStub(userId, roles), NullLogger<CajaController>.Instance,
            mapper, _context, new RelojComercialFake());
        var http = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, new TempDataProviderStub());
        return controller;
    }

    private async Task<(Caja Caja, AperturaCaja Apertura, MovimientoCaja Pendiente)> SeedCajaConIngresoPendienteAsync(string miembro)
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

        var apertura = await _service.AbrirCajaAsync(new ViewModels.AbrirCajaViewModel { CajaId = caja.Id, MontoInicial = 100m }, miembro);
        var mov = new MovimientoCaja
        {
            AperturaCajaId = apertura.Id,
            FechaMovimiento = DateTime.UtcNow,
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.VentaEfectivo,
            EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Pendiente,
            TipoPago = TipoPago.Transferencia,
            Monto = 50m,
            Descripcion = "Pendiente",
            Usuario = miembro
        };
        _context.MovimientosCaja.Add(mov);
        await _context.SaveChangesAsync();
        return (caja, apertura, mov);
    }

    private async Task<EstadoAcreditacionMovimientoCaja?> EstadoAsync(int movimientoId)
    {
        _context.ChangeTracker.Clear();
        return (await _context.MovimientosCaja.AsNoTracking().SingleAsync(m => m.Id == movimientoId)).EstadoAcreditacion;
    }

    private static string Error(CajaController c) => c.TempData["Error"]?.ToString() ?? "";

    // ---- negativos ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task N1_MiembroDeA_ConAperturaPropia_NoAcreditaMovimientoDeB()
    {
        var a = await SeedCajaConIngresoPendienteAsync("cajero-a");
        var b = await SeedCajaConIngresoPendienteAsync("cajero-b");

        var result = await ControllerPara("cajero-a").AcreditarMovimiento(b.Pendiente.Id, a.Apertura.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(b.Pendiente.Id));
    }

    [Fact]
    public async Task N2_MiembroDeA_ConAperturaAjena_NoAcredita()
    {
        var a = await SeedCajaConIngresoPendienteAsync("cajero-a");
        var b = await SeedCajaConIngresoPendienteAsync("cajero-b");

        var controller = ControllerPara("cajero-a");
        await controller.AcreditarMovimiento(b.Pendiente.Id, b.Apertura.Id);

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(b.Pendiente.Id));
        Assert.NotEmpty(Error(controller));
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(a.Pendiente.Id));
    }

    [Fact]
    public async Task N3_UsuarioSinPadron_NoAcredita()
    {
        var a = await SeedCajaConIngresoPendienteAsync("cajero-a");

        await ControllerPara("intruso").AcreditarMovimiento(a.Pendiente.Id, a.Apertura.Id);

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(a.Pendiente.Id));
    }

    [Fact]
    public async Task N4_MiembroDeAmbasCajas_AperturaEnviadaDistintaDeLaReal_SeRechazaPorMismatch()
    {
        var a = await SeedCajaConIngresoPendienteAsync("cajero-ab");
        var b = await SeedCajaConIngresoPendienteAsync("cajero-ab");

        var controller = ControllerPara("cajero-ab");
        await controller.AcreditarMovimiento(b.Pendiente.Id, a.Apertura.Id);

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(b.Pendiente.Id));
        Assert.Contains("no pertenece", Error(controller));
    }

    [Fact]
    public async Task N5_MovimientoInexistente_NoAcredita_ySinFiltrarExistenciaAUnNoSupervisor()
    {
        var a = await SeedCajaConIngresoPendienteAsync("cajero-a");

        var comun = ControllerPara("cajero-a");
        await comun.AcreditarMovimiento(999_999, a.Apertura.Id);
        var supervisor = ControllerPara("admin", Roles.Administrador);
        await supervisor.AcreditarMovimiento(999_999, a.Apertura.Id);

        Assert.DoesNotContain("no encontrado", Error(comun), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no encontrado", Error(supervisor), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task N5b_MovimientoBorrado_NoSeAcredita()
    {
        var a = await SeedCajaConIngresoPendienteAsync("cajero-a");
        a.Pendiente.IsDeleted = true;
        await _context.SaveChangesAsync();

        await ControllerPara("admin", Roles.Administrador).AcreditarMovimiento(a.Pendiente.Id, a.Apertura.Id);

        _context.ChangeTracker.Clear();
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente,
            (await _context.MovimientosCaja.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == a.Pendiente.Id)).EstadoAcreditacion);
    }

    [Fact]
    public async Task N6_MovimientoEgresoNoPendiente_SigueRechazado()
    {
        var a = await SeedCajaConIngresoPendienteAsync("cajero-a");
        a.Pendiente.Tipo = TipoMovimientoCaja.Egreso;
        await _context.SaveChangesAsync();

        var controller = ControllerPara("cajero-a");
        await controller.AcreditarMovimiento(a.Pendiente.Id, a.Apertura.Id);

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(a.Pendiente.Id));
        Assert.NotEmpty(Error(controller));
    }

    [Fact]
    public async Task N7_ApenturaInexistente_FallaCerrada_ParaNoSupervisor_PeroNoParaSupervisor()
    {
        var comun = ControllerPara("cajero-a");
        var supervisor = ControllerPara("admin", Roles.Administrador);

        // Caso de los demás endpoints que usan PuedeOperarAperturaAsync: apertura inexistente.
        var rComun = await comun.Cerrar(new ViewModels.CerrarCajaViewModel { AperturaCajaId = 987_654 });
        // Residual preexistente (fuera de FIN-S-006): para un supervisor, Cerrar con apertura inexistente deja pasar
        // la excepción "Apertura no encontrada" desde TryPopulateCerrarModelAsync. Lo que importa acá es que NO es
        // un rechazo por permiso: la decisión de autorización del supervisor no cambió.
        var exSup = await Record.ExceptionAsync(() => supervisor.Cerrar(new ViewModels.CerrarCajaViewModel { AperturaCajaId = 987_654 }));

        Assert.Contains("habilitado para operar", Error(comun), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("habilitado para operar", Error(supervisor), StringComparison.OrdinalIgnoreCase);
        Assert.IsType<RedirectToActionResult>(rComun);
        Assert.IsType<InvalidOperationException>(exSup);
        Assert.Contains("no encontrada", exSup!.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- positivos ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task P1_MiembroDeA_AcreditaSuPropioMovimiento()
    {
        var a = await SeedCajaConIngresoPendienteAsync("cajero-a");

        var controller = ControllerPara("cajero-a");
        await controller.AcreditarMovimiento(a.Pendiente.Id, a.Apertura.Id);

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, await EstadoAsync(a.Pendiente.Id));
        Assert.Equal("Movimiento acreditado correctamente.", controller.TempData["Success"]);
    }

    [Theory]
    [InlineData(Roles.Administrador)]
    [InlineData(Roles.SuperAdmin)]
    public async Task P2_Supervisor_AcreditaMovimientoDeCualquierCaja(string rol)
    {
        var b = await SeedCajaConIngresoPendienteAsync("cajero-b");

        await ControllerPara("admin", rol).AcreditarMovimiento(b.Pendiente.Id, b.Apertura.Id);

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, await EstadoAsync(b.Pendiente.Id));
    }

    [Fact]
    public async Task P2b_Gerente_NoCambiaDePolitica_SinPadronNoAcredita()
    {
        var b = await SeedCajaConIngresoPendienteAsync("cajero-b");

        await ControllerPara("gerente-1", Roles.Gerente).AcreditarMovimiento(b.Pendiente.Id, b.Apertura.Id);

        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, await EstadoAsync(b.Pendiente.Id));
    }

    // ---- contrato estático --------------------------------------------------------------------------------------

    [Fact]
    public void Contrato_LaAccionResuelveLaAperturaDesdeElMovimiento()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "Controllers", "CajaController.cs"));
        var inicio = src.IndexOf("Task<IActionResult> AcreditarMovimiento(", StringComparison.Ordinal);
        Assert.True(inicio > 0);
        var cuerpo = src[inicio..src.IndexOf("#endregion", inicio, StringComparison.Ordinal)];

        Assert.Contains("MovimientosCaja", cuerpo);
        Assert.Contains("PuedeOperarAperturaAsync(aperturaRealId.Value)", cuerpo);
        Assert.DoesNotContain("PuedeOperarAperturaAsync(aperturaId)", cuerpo);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Controllers", "CajaController.cs")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repo.");
    }
}
