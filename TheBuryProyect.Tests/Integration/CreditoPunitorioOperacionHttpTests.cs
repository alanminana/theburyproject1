using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels.Punitorio;

namespace TheBuryProject.Tests.Integration;

[Collection("HttpIntegration")]
public class CreditoPunitorioOperacionHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const int ConfiguracionId = 997_800;
    private readonly CustomWebApplicationFactory _factory;

    public CreditoPunitorioOperacionHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Aplicar_FormRealRecalculaIgnoraCamposManipulados_YDevuelveImporteAutoritativo()
    {
        var ids = await SeedCuotaAsync(offset: 1);
        await _factory.SeedUserWithCreditoPunitorioBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(
            CustomWebApplicationFactory.CreditoPunitorioBothPermsUserId);
        var form = await GetOperationFormAsync(client, ids, "Acciones.Aplicar.CuotaRowVersionBase64");

        var response = await client.PostAsync(
            $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio/Aplicar",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = form.AntiForgeryToken,
                ["Acciones.Aplicar.Motivo"] = "  Mora verificada por HTTP  ",
                ["Acciones.Aplicar.CuotaRowVersionBase64"] = form.RowVersion,
                ["Acciones.Aplicar.Importe"] = "99999999",
                ["Acciones.Aplicar.Usuario"] = "atacante",
                ["Acciones.Aplicar.Autorizado"] = "true",
                ["Acciones.Aplicar.Estado"] = "Pagado"
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PunitorioOperacionResponseViewModel>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        Assert.True(body.ReloadPanel);
        Assert.True(body.ImporteAplicadoReal > 0m);
        Assert.NotEqual(99_999_999m, body.ImporteAplicadoReal);

        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var aplicado = await context.PunitoriosAplicados
            .AsNoTracking()
            .SingleAsync(p => p.CuotaId == ids.CuotaId);
        Assert.Equal(body.ImporteAplicadoReal, aplicado.Importe);
        Assert.Equal("Mora verificada por HTTP", aplicado.MotivoAplicacion);
        Assert.Equal("testuser", aplicado.UsuarioAplicacion);
        Assert.Equal(EstadoPunitorioAplicado.Aplicado, aplicado.Estado);
    }

    [Fact]
    public async Task Aplicar_SinPermiso_Devuelve403()
    {
        var ids = await SeedCuotaAsync(offset: 2);
        await _factory.SeedUserWithCreditoViewPermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoViewPermsUserId);

        var response = await client.PostAsync(
            $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio/Aplicar",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Aplicar_SinAntiforgery_Devuelve400()
    {
        var ids = await SeedCuotaAsync(offset: 3);
        await _factory.SeedUserWithCreditoPunitorioBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(
            CustomWebApplicationFactory.CreditoPunitorioBothPermsUserId);

        var response = await client.PostAsync(
            $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio/Aplicar",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Acciones.Aplicar.Motivo"] = "Sin token",
                ["Acciones.Aplicar.CuotaRowVersionBase64"] = Convert.ToBase64String(new byte[8])
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Aplicar_MotivoEnBlancoYBase64Invalido_Devuelve400SinPersistir()
    {
        var ids = await SeedCuotaAsync(offset: 4);
        await _factory.SeedUserWithCreditoPunitorioBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(
            CustomWebApplicationFactory.CreditoPunitorioBothPermsUserId);
        var form = await GetOperationFormAsync(client, ids, "Acciones.Aplicar.CuotaRowVersionBase64");

        var response = await client.PostAsync(
            $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio/Aplicar",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = form.AntiForgeryToken,
                ["Acciones.Aplicar.Motivo"] = "   ",
                ["Acciones.Aplicar.CuotaRowVersionBase64"] = "base64-invalido"
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PunitorioOperacionResponseViewModel>();
        Assert.NotNull(body?.Errors);
        Assert.Contains("Acciones.Aplicar.Motivo", body!.Errors!.Keys);
        Assert.Contains("Acciones.Aplicar.CuotaRowVersionBase64", body.Errors.Keys);
        Assert.Equal(0, await CountAplicacionesAsync(ids.CuotaId));
    }

    [Fact]
    public async Task Aplicar_CuotaDeOtroCredito_Devuelve404()
    {
        var ids = await SeedCuotaAsync(offset: 5);
        await _factory.SeedUserWithCreditoPunitorioBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(
            CustomWebApplicationFactory.CreditoPunitorioBothPermsUserId);
        var form = await GetOperationFormAsync(client, ids, "Acciones.Aplicar.CuotaRowVersionBase64");

        var response = await client.PostAsync(
            $"/Credito/{ids.CreditoId + 999}/Cuotas/{ids.CuotaId}/Punitorio/Aplicar",
            ApplyContent(form, "Credito incorrecto"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await CountAplicacionesAsync(ids.CuotaId));
    }

    [Fact]
    public async Task Aplicar_CuotaInexistente_Devuelve404()
    {
        var tokenSource = await SeedCuotaAsync(offset: 15);
        await _factory.SeedUserWithCreditoPunitorioBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(
            CustomWebApplicationFactory.CreditoPunitorioBothPermsUserId);
        var form = await GetOperationFormAsync(client, tokenSource, "Acciones.Aplicar.CuotaRowVersionBase64");

        var response = await client.PostAsync(
            $"/Credito/{tokenSource.CreditoId}/Cuotas/404/Punitorio/Aplicar",
            ApplyContent(form, "Cuota inexistente"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Aplicar_RowVersionVencida_Devuelve409YOrdenaRecargar()
    {
        var ids = await SeedCuotaAsync(offset: 6);
        await _factory.SeedUserWithCreditoPunitorioBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(
            CustomWebApplicationFactory.CreditoPunitorioBothPermsUserId);
        var form = await GetOperationFormAsync(client, ids, "Acciones.Aplicar.CuotaRowVersionBase64");
        form = form with { RowVersion = Convert.ToBase64String(Enumerable.Repeat((byte)9, 8).ToArray()) };

        var response = await client.PostAsync(
            $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio/Aplicar",
            ApplyContent(form, "Version vencida"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PunitorioOperacionResponseViewModel>();
        Assert.False(body!.Success);
        Assert.True(body.ReloadPanel);
        Assert.Equal(0, await CountAplicacionesAsync(ids.CuotaId));
    }

    [Fact]
    public async Task Aplicar_DoblePost_NoCreaDosAplicaciones()
    {
        var ids = await SeedCuotaAsync(offset: 7);
        await _factory.SeedUserWithCreditoPunitorioBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(
            CustomWebApplicationFactory.CreditoPunitorioBothPermsUserId);
        var form = await GetOperationFormAsync(client, ids, "Acciones.Aplicar.CuotaRowVersionBase64");
        var url = $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio/Aplicar";

        var first = await client.PostAsync(url, ApplyContent(form, "Primer envio"));
        var second = await client.PostAsync(url, ApplyContent(form, "Segundo envio"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, await CountAplicacionesAsync(ids.CuotaId));
    }

    [Fact]
    public async Task Anular_FormRealConservaFilaYPanelMuestraEstadoAnulado()
    {
        var ids = await SeedCuotaAsync(offset: 8, applicationState: ApplicationSeedState.Active);
        await _factory.SeedUserWithCreditoPunitorioBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(
            CustomWebApplicationFactory.CreditoPunitorioBothPermsUserId);
        var form = await GetOperationFormAsync(
            client,
            ids,
            "Acciones.Anulacion.Form.PunitorioAplicadoRowVersionBase64");

        var response = await client.PostAsync(
            $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio/{ids.AplicacionId}/Anular",
            AnularContent(form, "  Aplicacion incorrecta  "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var aplicaciones = await context.PunitoriosAplicados
            .AsNoTracking()
            .Where(p => p.CuotaId == ids.CuotaId)
            .ToListAsync();
        var anulado = Assert.Single(aplicaciones);
        Assert.Equal(EstadoPunitorioAplicado.Anulado, anulado.Estado);
        Assert.Equal("Aplicacion incorrecta", anulado.MotivoAnulacion);

        var panel = await client.GetStringAsync(
            $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio");
        Assert.Contains("Anulado", panel);
    }

    [Fact]
    public async Task Anular_AplicacionDeOtraCuota_Devuelve404()
    {
        var target = await SeedCuotaAsync(offset: 9, applicationState: ApplicationSeedState.Active);
        var other = await SeedCuotaAsync(offset: 10, applicationState: ApplicationSeedState.Active);
        await _factory.SeedUserWithCreditoPunitorioBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(
            CustomWebApplicationFactory.CreditoPunitorioBothPermsUserId);
        var form = await GetOperationFormAsync(
            client,
            target,
            "Acciones.Anulacion.Form.PunitorioAplicadoRowVersionBase64");

        var response = await client.PostAsync(
            $"/Credito/{target.CreditoId}/Cuotas/{target.CuotaId}/Punitorio/{other.AplicacionId}/Anular",
            AnularContent(form, "Relacion incorrecta"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(EstadoPunitorioAplicado.Aplicado, await GetApplicationStateAsync(other.AplicacionId!.Value));
    }

    [Fact]
    public async Task Anular_AplicacionInexistente_Devuelve404()
    {
        var ids = await SeedCuotaAsync(offset: 16, applicationState: ApplicationSeedState.Active);
        await _factory.SeedUserWithCreditoPunitorioBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(
            CustomWebApplicationFactory.CreditoPunitorioBothPermsUserId);
        var form = await GetOperationFormAsync(
            client,
            ids,
            "Acciones.Anulacion.Form.PunitorioAplicadoRowVersionBase64");

        var response = await client.PostAsync(
            $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio/404/Anular",
            AnularContent(form, "Aplicacion inexistente"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anular_MotivoEnBlancoYBase64Invalido_Devuelve400()
    {
        var ids = await SeedCuotaAsync(offset: 17, applicationState: ApplicationSeedState.Active);
        await _factory.SeedUserWithCreditoPunitorioBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(
            CustomWebApplicationFactory.CreditoPunitorioBothPermsUserId);
        var form = await GetOperationFormAsync(
            client,
            ids,
            "Acciones.Anulacion.Form.PunitorioAplicadoRowVersionBase64");
        form = form with { RowVersion = "base64-invalido" };

        var response = await client.PostAsync(
            $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio/{ids.AplicacionId}/Anular",
            AnularContent(form, "   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PunitorioOperacionResponseViewModel>();
        Assert.Contains("Acciones.Anulacion.Form.Motivo", body!.Errors!.Keys);
        Assert.Contains("Acciones.Anulacion.Form.PunitorioAplicadoRowVersionBase64", body.Errors.Keys);
        Assert.Equal(EstadoPunitorioAplicado.Aplicado, await GetApplicationStateAsync(ids.AplicacionId!.Value));
    }

    [Fact]
    public async Task Anular_SinPermiso_Devuelve403()
    {
        var ids = await SeedCuotaAsync(offset: 18, applicationState: ApplicationSeedState.Active);
        await _factory.SeedUserWithCreditoViewPermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoViewPermsUserId);

        var response = await client.PostAsync(
            $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio/{ids.AplicacionId}/Anular",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(EstadoPunitorioAplicado.Aplicado, await GetApplicationStateAsync(ids.AplicacionId!.Value));
    }

    [Fact]
    public async Task Anular_RowVersionVencida_Devuelve409()
    {
        var ids = await SeedCuotaAsync(offset: 11, applicationState: ApplicationSeedState.Active);
        await _factory.SeedUserWithCreditoPunitorioBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(
            CustomWebApplicationFactory.CreditoPunitorioBothPermsUserId);
        var form = await GetOperationFormAsync(
            client,
            ids,
            "Acciones.Anulacion.Form.PunitorioAplicadoRowVersionBase64");
        form = form with { RowVersion = Convert.ToBase64String(Enumerable.Repeat((byte)7, 8).ToArray()) };

        var response = await client.PostAsync(
            $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio/{ids.AplicacionId}/Anular",
            AnularContent(form, "Version vencida"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(EstadoPunitorioAplicado.Aplicado, await GetApplicationStateAsync(ids.AplicacionId!.Value));
    }

    [Fact]
    public async Task Anular_ConPagoParcial_Devuelve409YConservaAplicacion()
    {
        var ids = await SeedCuotaAsync(offset: 12, applicationState: ApplicationSeedState.ActiveWithPartialPayment);
        var tokenSource = await SeedCuotaAsync(offset: 13);
        await _factory.SeedUserWithCreditoPunitorioBothPermissionsAsync();
        using var client = _factory.CreateClientWithUserId(
            CustomWebApplicationFactory.CreditoPunitorioBothPermsUserId);
        var tokenForm = await GetOperationFormAsync(
            client,
            tokenSource,
            "Acciones.Aplicar.CuotaRowVersionBase64");
        var rowVersion = await GetApplicationRowVersionAsync(ids.AplicacionId!.Value);
        var form = new OperationForm(tokenForm.AntiForgeryToken, rowVersion);

        var response = await client.PostAsync(
            $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio/{ids.AplicacionId}/Anular",
            AnularContent(form, "No debe anular"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(EstadoPunitorioAplicado.Aplicado, await GetApplicationStateAsync(ids.AplicacionId.Value));
    }

    [Fact]
    public async Task Detalle_SinPermisosDeOperacion_NoRenderizaForms()
    {
        var ids = await SeedCuotaAsync(offset: 14);
        await _factory.SeedUserWithCreditoViewPermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoViewPermsUserId);

        var html = await client.GetStringAsync(
            $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio");

        Assert.DoesNotContain("data-punitorio-operation-form", html);
        Assert.DoesNotContain("Acciones.Aplicar.Motivo", html);
        Assert.DoesNotContain("Acciones.Anulacion.Form.Motivo", html);
    }

    private async Task<ScenarioIds> SeedCuotaAsync(
        int offset,
        ApplicationSeedState applicationState = ApplicationSeedState.None)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        if (!await context.ConfiguracionesPunitorio.AnyAsync(c => c.Id == ConfiguracionId))
        {
            context.ConfiguracionesPunitorio.Add(new ConfiguracionPunitorio
            {
                Id = ConfiguracionId,
                Porcentaje = 10m,
                PeriodoDias = 30,
                DiasGracia = 0,
                ProrrateoDiario = true,
                VigenteDesde = new DateOnly(2020, 1, 1),
                Activa = true
            });
            await context.SaveChangesAsync();
        }

        var clienteId = 997_800 + offset * 10 + 1;
        var creditoId = clienteId + 1;
        var cuotaId = clienteId + 2;
        if (!await context.Cuotas.AnyAsync(c => c.Id == cuotaId))
        {
            var cliente = new Cliente
            {
                Id = clienteId,
                TipoDocumento = "DNI",
                NumeroDocumento = $"PUNML9C-{offset}",
                Apellido = "Punitorio",
                Nombre = "HTTP",
                NombreCompleto = "Punitorio HTTP",
                Telefono = "0000",
                Domicilio = "Base aislada",
                Activo = true
            };
            var credito = new Credito
            {
                Id = creditoId,
                ClienteId = clienteId,
                Cliente = cliente,
                Numero = $"PUN-ML9-C-{offset}",
                MontoSolicitado = 100_000m,
                MontoAprobado = 100_000m,
                TasaInteres = 10m,
                CantidadCuotas = 1,
                MontoCuota = 100_000m,
                TotalAPagar = 100_000m,
                SaldoPendiente = 100_000m,
                Estado = EstadoCredito.Activo,
                FechaSolicitud = DateTime.UtcNow.AddMonths(-4),
                PuntajeRiesgoInicial = 100m
            };
            var cuota = new Cuota
            {
                Id = cuotaId,
                CreditoId = creditoId,
                Credito = credito,
                NumeroCuota = 1,
                MontoCapital = 100_000m,
                MontoInteres = 0m,
                MontoTotal = 100_000m,
                MontoPagado = 0m,
                MontoPunitorio = 987_654m,
                FechaVencimiento = DateTime.UtcNow.AddDays(-60),
                Estado = EstadoCuota.Vencida
            };
            context.AddRange(cliente, credito, cuota);
            await context.SaveChangesAsync();
        }

        int? aplicacionId = null;
        if (applicationState != ApplicationSeedState.None)
        {
            aplicacionId = cuotaId + 1;
            if (!await context.PunitoriosAplicados.AnyAsync(p => p.Id == aplicacionId))
            {
                var aplicacion = new PunitorioAplicado
                {
                    Id = aplicacionId.Value,
                    CuotaId = cuotaId,
                    FechaCalculo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                    SaldoBase = 100_000m,
                    DiasComputados = 60,
                    Importe = 20_000m,
                    Estado = EstadoPunitorioAplicado.Aplicado,
                    DesgloseSnapshotJson = "{}",
                    MotivoAplicacion = "Aplicacion sembrada",
                    FechaAplicacion = DateTime.UtcNow.AddDays(-1),
                    UsuarioAplicacion = "integration"
                };
                context.PunitoriosAplicados.Add(aplicacion);

                if (applicationState == ApplicationSeedState.ActiveWithPartialPayment)
                {
                    context.PagosCuota.Add(new PagoCuota
                    {
                        Id = cuotaId + 2,
                        CuotaId = cuotaId,
                        PunitorioAplicadoId = aplicacionId,
                        FechaPagoComercial = DateOnly.FromDateTime(DateTime.UtcNow),
                        ImporteTotal = 1_000m,
                        ImporteAplicadoCuota = 0m,
                        ImporteAplicadoPunitorio = 1_000m,
                        MedioPago = "Efectivo",
                        Origen = OrigenPagoCuota.RegistradoPorSistema,
                        Estado = EstadoPagoCuota.Aplicado,
                        HistorialCompleto = true
                    });
                }
                await context.SaveChangesAsync();
            }
        }

        return new ScenarioIds(creditoId, cuotaId, aplicacionId);
    }

    private async Task<OperationForm> GetOperationFormAsync(
        HttpClient client,
        ScenarioIds ids,
        string rowVersionName)
    {
        var html = await client.GetStringAsync(
            $"/Credito/{ids.CreditoId}/Cuotas/{ids.CuotaId}/Punitorio");
        return new OperationForm(
            ExtractInputValue(html, "__RequestVerificationToken"),
            ExtractInputValue(html, rowVersionName));
    }

    private static string ExtractInputValue(string html, string name)
    {
        var pattern = $"<input(?=[^>]*name=\"{Regex.Escape(name)}\")(?=[^>]*value=\"([^\"]*)\")[^>]*>";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"No se encontro el input '{name}' en el DOM renderizado.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static FormUrlEncodedContent ApplyContent(OperationForm form, string motivo) => new(
        new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = form.AntiForgeryToken,
            ["Acciones.Aplicar.Motivo"] = motivo,
            ["Acciones.Aplicar.CuotaRowVersionBase64"] = form.RowVersion
        });

    private static FormUrlEncodedContent AnularContent(OperationForm form, string motivo) => new(
        new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = form.AntiForgeryToken,
            ["Acciones.Anulacion.Form.Motivo"] = motivo,
            ["Acciones.Anulacion.Form.PunitorioAplicadoRowVersionBase64"] = form.RowVersion
        });

    private async Task<int> CountAplicacionesAsync(int cuotaId)
    {
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        return await context.PunitoriosAplicados.CountAsync(p => p.CuotaId == cuotaId);
    }

    private async Task<EstadoPunitorioAplicado> GetApplicationStateAsync(int aplicacionId)
    {
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        return await context.PunitoriosAplicados
            .Where(p => p.Id == aplicacionId)
            .Select(p => p.Estado)
            .SingleAsync();
    }

    private async Task<string> GetApplicationRowVersionAsync(int aplicacionId)
    {
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        var rowVersion = await context.PunitoriosAplicados
            .Where(p => p.Id == aplicacionId)
            .Select(p => p.RowVersion)
            .SingleAsync();
        return Convert.ToBase64String(rowVersion);
    }

    private enum ApplicationSeedState
    {
        None,
        Active,
        ActiveWithPartialPayment
    }

    private sealed record ScenarioIds(int CreditoId, int CuotaId, int? AplicacionId);
    private sealed record OperationForm(string AntiForgeryToken, string RowVersion);
}
