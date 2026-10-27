using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// VALID-L2 — campos editables de Cliente por el pipeline HTTP real (antiforgery + autorización + binding + servicio):
/// alta (CreateAjax), límite de crédito y puntaje manual. Cada caso fuera de regla debe rechazarse sin persistir.
/// </summary>
[Collection("HttpIntegration")]
public class ClienteCamposValidacionHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static int _counter = 93000;
    private readonly CustomWebApplicationFactory _factory;

    public ClienteCamposValidacionHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedTestUserAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<AppDbContext> NuevoContextoAsync()
    {
        var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await factory.CreateDbContextAsync();
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var match = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Sin token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    // ---- Alta (CreateAjax) ----

    private static Dictionary<string, string> FormCliente(string dni) => new()
    {
        ["TipoDocumento"] = "DNI",
        ["NumeroDocumento"] = dni,
        ["Apellido"] = "Pérez",
        ["Nombre"] = "Juan",
        ["Domicilio"] = "Calle Falsa 123",
    };

    private static string NuevoDni() => (40_000_000 + Interlocked.Increment(ref _counter)).ToString();

    private async Task<bool> CrearAsync(Dictionary<string, string> campos)
    {
        var client = _factory.CreateAuthenticatedClient();
        campos["__RequestVerificationToken"] = await TokenAsync(client);
        var resp = await client.PostAsync("/Cliente/CreateAjax", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("success").GetBoolean();
    }

    private async Task<int> ClientesConDniAsync(string dni)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.Clientes.IgnoreQueryFilters().AsNoTracking().CountAsync(c => c.NumeroDocumento == dni);
    }

    private async Task AssertAltaRechazadaAsync(string campo, string valor)
    {
        var dni = NuevoDni();
        var form = FormCliente(dni);
        form[campo] = valor;

        var ok = await CrearAsync(form);

        Assert.False(ok, $"{campo}='{valor}' fue ACEPTADO.");
        Assert.Equal(0, await ClientesConDniAsync(dni));
    }

    [Fact]
    public async Task Alta_Valida_SeAcepta()
    {
        var dni = NuevoDni();
        var form = FormCliente(dni);
        form["FechaNacimiento"] = "1990-05-05";
        form["Email"] = "juan@example.com";
        form["Telefono"] = "11 4567-8901";
        form["CodigoPostal"] = "C1234ABC";

        Assert.True(await CrearAsync(form));
        Assert.Equal(1, await ClientesConDniAsync(dni));
    }

    [Theory]
    [InlineData("1800-01-01")]
    [InlineData("0001-01-01")]
    [InlineData("2999-12-31")]
    public Task Alta_FechaNacimiento_FueraDeRango_SeRechaza(string fecha) =>
        AssertAltaRechazadaAsync("FechaNacimiento", fecha);

    [Fact]
    public Task Alta_FechaNacimiento_Futura_SeRechaza() =>
        AssertAltaRechazadaAsync("FechaNacimiento", DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd"));

    [Theory]
    [InlineData("99")]
    [InlineData("1")]
    [InlineData("10")]
    public Task Alta_CuilPrefijo_Invalido_SeRechaza(string prefijo) =>
        AssertAltaRechazadaAsync("CuilPrefijo", prefijo);

    [Fact]
    public async Task Alta_CuilConVerificadorIncorrecto_SeRechaza()
    {
        // DNI 12345678 -> CUIL 20-12345678-6 es el válido; verificador 0 no.
        var dni = "12345678";
        var antes = await ClientesConDniAsync(dni);
        var form = FormCliente(dni);
        form["CuilPrefijo"] = "20";
        form["CuilVerificador"] = "0";

        var ok = await CrearAsync(form);

        Assert.False(ok, "CUIL 20-12345678-0 (verificador incorrecto) fue ACEPTADO.");
        Assert.Equal(antes, await ClientesConDniAsync(dni));
    }

    [Fact]
    public async Task Alta_CuilCorrecto_SeAcepta()
    {
        var dni = "12345678";
        if (await ClientesConDniAsync(dni) > 0) return; // otro test de la colección ya lo usó
        var form = FormCliente(dni);
        form["CuilPrefijo"] = "20";
        form["CuilVerificador"] = "6";

        Assert.True(await CrearAsync(form));
    }

    [Fact]
    public async Task Alta_MontoMinimoMayorAlMaximo_SeRechaza()
    {
        var dni = NuevoDni();
        var form = FormCliente(dni);
        form["MontoMinimoPersonalizado"] = "500000";
        form["MontoMaximoPersonalizado"] = "100000";

        Assert.False(await CrearAsync(form), "Mínimo personalizado mayor al máximo fue ACEPTADO.");
        Assert.Equal(0, await ClientesConDniAsync(dni));
    }

    [Theory]
    [InlineData("Observaciones", 501)]
    [InlineData("Domicilio", 201)]
    [InlineData("TiempoTrabajo", 51)]
    [InlineData("Email", 101)]
    public Task Alta_TextosDemasiadoLargos_SeRechazan(string campo, int largo) =>
        AssertAltaRechazadaAsync(campo, campo == "Email" ? new string('a', largo - 12) + "@example.com" : new string('x', largo));

    [Theory]
    [InlineData("Email", "sin-arroba")]
    [InlineData("Email", "a@")]
    [InlineData("Telefono", "abc")]
    [InlineData("CodigoPostal", "12")]
    [InlineData("TasaInteresMensualPersonalizada", "101")]
    [InlineData("TasaInteresMensualPersonalizada", "-1")]
    [InlineData("CuotasMaximasPersonalizadas", "0")]
    [InlineData("CuotasMaximasPersonalizadas", "121")]
    [InlineData("MontoMaximoPersonalizado", "20000001")]
    [InlineData("MontoMaximoPersonalizado", "-5")]
    public Task Alta_CamposFueraDeRegla_SeRechazan(string campo, string valor) =>
        AssertAltaRechazadaAsync(campo, valor);

    [Fact]
    public Task Alta_NivelRiesgoInexistente_SeRechaza() =>
        AssertAltaRechazadaAsync("NivelRiesgo", "99");

    [Theory]
    [InlineData("999")]
    [InlineData("-3")]
    public async Task Alta_PuntajeRiesgoForjado_SeIgnora_ElServidorLoDeriveDelNivel(string puntaje)
    {
        var dni = NuevoDni();
        var form = FormCliente(dni);
        form["PuntajeRiesgo"] = puntaje;

        Assert.True(await CrearAsync(form));
        await using var ctx = await NuevoContextoAsync();
        var cliente = await ctx.Clientes.AsNoTracking().SingleAsync(c => c.NumeroDocumento == dni);
        Assert.Equal((int)cliente.NivelRiesgo * 2m, cliente.PuntajeRiesgo);
    }

    [Theory]
    [InlineData("1234567")]      // 7 dígitos es válido (DNI antiguo)
    public async Task Alta_DniDeSieteDigitos_SeAcepta(string dni)
    {
        dni = $"{Interlocked.Increment(ref _counter)}".PadLeft(7, '9')[^7..];
        Assert.True(await CrearAsync(FormCliente(dni)));
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("123456789")]
    [InlineData("12.345.678")]
    [InlineData("abc12345")]
    public async Task Alta_DniInvalido_SeRechaza(string dni)
    {
        Assert.False(await CrearAsync(FormCliente(dni)), $"DNI '{dni}' fue ACEPTADO.");
        Assert.Equal(0, await ClientesConDniAsync(dni));
    }

    [Fact]
    public async Task Alta_DniDuplicado_SeRechaza()
    {
        var dni = NuevoDni();
        Assert.True(await CrearAsync(FormCliente(dni)));

        Assert.False(await CrearAsync(FormCliente(dni)));
        Assert.Equal(1, await ClientesConDniAsync(dni));
    }

    // ---- Límite de crédito (parámetros sueltos, sin ViewModel) ----

    private async Task<int> SembrarClienteAsync()
    {
        await using var ctx = await NuevoContextoAsync();
        var cliente = new Cliente
        {
            Nombre = "Limite", Apellido = "Prueba", NumeroDocumento = NuevoDni(),
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal, Domicilio = "Calle 1"
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();
        return cliente.Id;
    }

    private async Task PostAsync(string ruta, Dictionary<string, string> campos)
    {
        var client = _factory.CreateAuthenticatedClient();
        campos["__RequestVerificationToken"] = await TokenAsync(client);
        await client.PostAsync(ruta, new FormUrlEncodedContent(campos));
    }

    private async Task<decimal?> LimiteAsync(int clienteId)
    {
        await using var ctx = await NuevoContextoAsync();
        return (await ctx.Clientes.AsNoTracking().SingleAsync(c => c.Id == clienteId)).LimiteCredito;
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("20000001")]
    [InlineData("99999999999")]
    [InlineData("1.500")]       // ambiguo
    [InlineData("abc")]
    [InlineData("1000,555")]    // más de 2 decimales
    public async Task AsignarLimiteCredito_FueraDeRegla_NoCambiaElLimite(string limite)
    {
        var clienteId = await SembrarClienteAsync();
        var antes = await LimiteAsync(clienteId);

        await PostAsync("/Cliente/AsignarLimiteCredito", new()
        {
            ["clienteId"] = clienteId.ToString(),
            ["limiteCredito"] = limite,
            ["motivo"] = "QA",
        });

        Assert.Equal(antes, await LimiteAsync(clienteId));
    }

    [Fact]
    public async Task AsignarLimiteCredito_MotivoDemasiadoLargo_NoCambiaElLimite()
    {
        var clienteId = await SembrarClienteAsync();
        var antes = await LimiteAsync(clienteId);

        await PostAsync("/Cliente/AsignarLimiteCredito", new()
        {
            ["clienteId"] = clienteId.ToString(),
            ["limiteCredito"] = "50000",
            ["motivo"] = new string('m', 1001),
        });

        Assert.Equal(antes, await LimiteAsync(clienteId));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("50000", 50000)]
    [InlineData("1500,50", 1500.50)]
    [InlineData("20000000", 20000000)]
    public async Task AsignarLimiteCredito_ValidoDeBorde_SeAplica(string limite, double esperado)
    {
        var clienteId = await SembrarClienteAsync();

        await PostAsync("/Cliente/AsignarLimiteCredito", new()
        {
            ["clienteId"] = clienteId.ToString(),
            ["limiteCredito"] = limite,
            ["motivo"] = "QA",
        });

        Assert.Equal((decimal)esperado, await LimiteAsync(clienteId));
    }

    // ---- Puntaje crediticio manual (parámetro suelto: int + motivo) ----

    private async Task<int?> NivelManualAsync(int clienteId)
    {
        await using var ctx = await NuevoContextoAsync();
        return (await ctx.ClientesCreditoConfiguraciones.AsNoTracking().FirstOrDefaultAsync(c => c.ClienteId == clienteId))?.NivelCreditoManual;
    }

    [Theory]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("6")]
    public async Task AsignarNivelManual_FueraDeRango_NoSeAplica(string nivel)
    {
        var clienteId = await SembrarClienteAsync();

        await PostAsync("/Cliente/AsignarNivelCreditoManual", new()
        {
            ["clienteId"] = clienteId.ToString(), ["nivelCreditoManual"] = nivel, ["motivo"] = "QA",
        });

        Assert.Null(await NivelManualAsync(clienteId));
    }

    [Fact]
    public async Task AsignarNivelManual_MotivoDemasiadoLargo_NoSeAplica()
    {
        var clienteId = await SembrarClienteAsync();

        await PostAsync("/Cliente/AsignarNivelCreditoManual", new()
        {
            ["clienteId"] = clienteId.ToString(), ["nivelCreditoManual"] = "3", ["motivo"] = new string('m', 1001),
        });

        Assert.Null(await NivelManualAsync(clienteId));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("5", 5)]
    public async Task AsignarNivelManual_ValidoDeBorde_SeAplica(string nivel, int esperado)
    {
        var clienteId = await SembrarClienteAsync();

        await PostAsync("/Cliente/AsignarNivelCreditoManual", new()
        {
            ["clienteId"] = clienteId.ToString(), ["nivelCreditoManual"] = nivel, ["motivo"] = new string('m', 1000),
        });

        Assert.Equal(esperado, await NivelManualAsync(clienteId));
    }

    // ---- Motivos de rechazo / cancelación de crédito ----

    private async Task<int> SembrarCreditoAsync()
    {
        var clienteId = await SembrarClienteAsync();
        await using var ctx = await NuevoContextoAsync();
        var credito = new Credito
        {
            ClienteId = clienteId, Numero = $"CRE-L2-{Interlocked.Increment(ref _counter)}",
            MontoSolicitado = 1000m, MontoAprobado = 1000m, CantidadCuotas = 3, Estado = EstadoCredito.Solicitado,
        };
        ctx.Creditos.Add(credito);
        await ctx.SaveChangesAsync();
        return credito.Id;
    }

    private async Task<EstadoCredito> EstadoCreditoAsync(int id)
    {
        await using var ctx = await NuevoContextoAsync();
        return (await ctx.Creditos.AsNoTracking().SingleAsync(c => c.Id == id)).Estado;
    }

    [Fact]
    public async Task CreditoRechazar_MotivoDemasiadoLargo_NoRechaza()
    {
        var id = await SembrarCreditoAsync();

        await PostAsync("/Credito/Rechazar", new() { ["id"] = id.ToString(), ["motivo"] = new string('m', 981) });

        Assert.Equal(EstadoCredito.Solicitado, await EstadoCreditoAsync(id));
    }

    [Fact]
    public async Task CreditoRechazar_MotivoEnElLimite_Rechaza()
    {
        var id = await SembrarCreditoAsync();

        await PostAsync("/Credito/Rechazar", new() { ["id"] = id.ToString(), ["motivo"] = new string('m', 980) });

        Assert.Equal(EstadoCredito.Rechazado, await EstadoCreditoAsync(id));
    }

    // ---- Documentos del cliente: rechazo con motivo ----

    private async Task<int> SembrarDocumentoAsync()
    {
        var clienteId = await SembrarClienteAsync();
        await using var ctx = await NuevoContextoAsync();
        var doc = new DocumentoCliente
        {
            ClienteId = clienteId, TipoDocumento = TipoDocumentoCliente.DNI, NombreArchivo = "dni.pdf",
            RutaArchivo = "uploads/dni.pdf", TipoMIME = "application/pdf", TamanoBytes = 10, Estado = EstadoDocumento.Pendiente,
            FechaSubida = DateTime.UtcNow,
        };
        ctx.Set<DocumentoCliente>().Add(doc);
        await ctx.SaveChangesAsync();
        return doc.Id;
    }

    private async Task<EstadoDocumento> EstadoDocumentoAsync(int id)
    {
        await using var ctx = await NuevoContextoAsync();
        return (await ctx.Set<DocumentoCliente>().AsNoTracking().SingleAsync(d => d.Id == id)).Estado;
    }

    [Fact]
    public async Task DocumentoRechazar_MotivoDemasiadoLargo_NoRechaza()
    {
        var id = await SembrarDocumentoAsync();

        await PostAsync("/DocumentoCliente/Rechazar", new() { ["id"] = id.ToString(), ["motivo"] = new string('m', 501) });

        Assert.Equal(EstadoDocumento.Pendiente, await EstadoDocumentoAsync(id));
    }

    [Fact]
    public async Task DocumentoRechazar_MotivoEnElLimite_Rechaza()
    {
        var id = await SembrarDocumentoAsync();

        await PostAsync("/DocumentoCliente/Rechazar", new() { ["id"] = id.ToString(), ["motivo"] = new string('m', 500) });

        Assert.Equal(EstadoDocumento.Rechazado, await EstadoDocumentoAsync(id));
    }

    [Fact]
    public async Task DocumentoVerificar_ObservacionesDemasiadoLargas_NoVerifica()
    {
        var id = await SembrarDocumentoAsync();

        await PostAsync("/DocumentoCliente/Verificar", new() { ["id"] = id.ToString(), ["observaciones"] = new string('o', 1001) });

        Assert.Equal(EstadoDocumento.Pendiente, await EstadoDocumentoAsync(id));
    }
}
