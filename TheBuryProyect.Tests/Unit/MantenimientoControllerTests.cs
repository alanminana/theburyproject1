using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// PUN-ML10-G: contrato de permisos/antiforgery y comportamiento de
/// MantenimientoController (recálculo global de scoring). Operación de máximo privilegio: sólo
/// SuperAdmin (permiso configuracion.recalcularscoringglobal, excluido del grant en bloque de Admin
/// en RolesPermisosSeeder — ver ese archivo).
/// </summary>
public class MantenimientoControllerTests
{
    [Fact]
    public void Controller_RequierePermisoDeMaximoPrivilegio()
    {
        var permiso = Assert.Single(
            typeof(MantenimientoController).GetCustomAttributes<PermisoRequeridoAttribute>());

        Assert.Equal("configuracion", permiso.Modulo);
        Assert.Equal("recalcularscoringglobal", permiso.Accion);
    }

    [Fact]
    public void RecalcularScoringGlobal_EsPostConAntiforgery()
    {
        var endpoint = typeof(MantenimientoController).GetMethod(nameof(MantenimientoController.RecalcularScoringGlobal));

        Assert.NotNull(endpoint);
        Assert.NotNull(endpoint!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(endpoint.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void ScoringGlobal_EsGet()
    {
        var endpoint = typeof(MantenimientoController).GetMethod(nameof(MantenimientoController.ScoringGlobal));

        Assert.NotNull(endpoint);
        Assert.NotNull(endpoint!.GetCustomAttribute<HttpGetAttribute>());
    }

    private static MantenimientoController CrearController(RecordingScoringService servicio)
    {
        var controller = new MantenimientoController(
            servicio, new StubCurrentUserService("admin.superadmin"), NullLogger<MantenimientoController>.Instance);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new InMemoryTempDataProvider());
        return controller;
    }

    [Fact]
    public async Task RecalcularScoringGlobal_Preview_InvocaServicioConPreviewTrueYNoAuditaOrigenManual()
    {
        var servicio = new RecordingScoringService();
        var controller = CrearController(servicio);

        var result = await controller.RecalcularScoringGlobal(preview: true, cancellationToken: default);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(servicio.UltimasOpciones);
        Assert.True(servicio.UltimasOpciones!.Preview);
        Assert.Equal("admin.superadmin", servicio.UltimasOpciones.RegistradoPor);
    }

    [Fact]
    public async Task RecalcularScoringGlobal_Ejecucion_InvocaServicioConPreviewFalse()
    {
        var servicio = new RecordingScoringService();
        var controller = CrearController(servicio);

        var result = await controller.RecalcularScoringGlobal(preview: false, cancellationToken: default);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(servicio.UltimasOpciones!.Preview);
    }

    [Fact]
    public async Task RecalcularScoringGlobal_PropagaCancellationTokenAlServicio()
    {
        var servicio = new RecordingScoringService();
        var controller = CrearController(servicio);
        using var cts = new CancellationTokenSource();

        await controller.RecalcularScoringGlobal(preview: false, cancellationToken: cts.Token);

        Assert.Equal(cts.Token, servicio.UltimoToken);
    }

    private sealed class RecordingScoringService : IClienteScoringService
    {
        public RecalculoGlobalScoringOpciones? UltimasOpciones { get; private set; }
        public CancellationToken UltimoToken { get; private set; }

        public Task<ConfiguracionScoringCliente> GetConfiguracionAsync(CancellationToken ct = default) =>
            throw new System.NotImplementedException();

        public Task<ClienteScoringResultado?> RecalcularAsync(int clienteId, CancellationToken ct = default) =>
            throw new System.NotImplementedException();

        public Task<ClienteScoringResultado?> RecalcularYAuditarAsync(
            int clienteId, string origen, string? observacion = null, string? registradoPor = null, CancellationToken ct = default) =>
            throw new System.NotImplementedException();

        public Task<RecalculoGlobalScoringResultado> RecalcularTodosAsync(
            RecalculoGlobalScoringOpciones opciones, CancellationToken ct = default)
        {
            UltimasOpciones = opciones;
            UltimoToken = ct;
            return Task.FromResult(new RecalculoGlobalScoringResultado
            {
                Examinados = 2,
                Recalculados = 1,
                SinCambios = 1,
                Fallidos = 0,
                Preview = opciones.Preview
            });
        }
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        private readonly string _username;
        public StubCurrentUserService(string username) => _username = username;

        public string GetUsername() => _username;
        public string GetUserId() => "1";
        public bool IsAuthenticated() => true;
        public string? GetEmail() => null;
        public bool IsInRole(string role) => role == "SuperAdmin";
        public bool HasPermission(string modulo, string accion) => true;
        public string? GetIpAddress() => null;
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
