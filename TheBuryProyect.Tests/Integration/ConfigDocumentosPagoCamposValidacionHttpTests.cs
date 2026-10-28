using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// VALID-L5 — configuración de Documentos (tipos y empresa), prefijos de numeración y configuración de pago por el pipeline
/// HTTP real (antiforgery + autorización + binding + servicio). Los valores fuera de regla no deben guardarse.
/// SQLite no hace cumplir los largos de columna: los tests miran que el valor excesivo no llegue a persistirse.
/// </summary>
[Collection("HttpIntegration")]
public class ConfigDocumentosPagoCamposValidacionHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static int _counter = 96000;
    private readonly CustomWebApplicationFactory _factory;

    public ConfigDocumentosPagoCamposValidacionHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

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

    private async Task PostAsync(string ruta, Dictionary<string, string> campos)
    {
        var client = _factory.CreateAuthenticatedClient();
        campos["__RequestVerificationToken"] = await TokenAsync(client);
        await client.PostAsync(ruta, new FormUrlEncodedContent(campos));
    }

    // ================= Tipo de documento =================

    private static string NuevoCodigoTipo() => $"QA_TIPO_{Interlocked.Increment(ref _counter)}";

    private static Dictionary<string, string> FormTipo(string codigo) => new()
    {
        ["Id"] = "0",
        ["Codigo"] = codigo,
        ["Nombre"] = "Tipo de prueba",
        ["Categoria"] = "Otro",
        ["Activo"] = "true",
        ["Prefijo"] = "QA",
    };

    private async Task<TipoDocumento?> TipoAsync(string codigo)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.TiposDocumento.AsNoTracking().FirstOrDefaultAsync(t => t.Codigo == codigo);
    }

    [Fact]
    public async Task Tipo_Valido_SeCrea()
    {
        var codigo = NuevoCodigoTipo();

        await PostAsync("/DocumentosConfig/TipoEdit", FormTipo(codigo));

        Assert.NotNull(await TipoAsync(codigo));
    }

    [Theory]
    [InlineData("qa tipo")]
    [InlineData("a")]
    [InlineData("QA-TIPO")]
    [InlineData("<b>")]
    public async Task Tipo_CodigoInvalido_NoSeCrea(string codigo)
    {
        await PostAsync("/DocumentosConfig/TipoEdit", FormTipo(codigo));

        Assert.Null(await TipoAsync(codigo));
    }

    [Theory]
    [InlineData("Prefijo", "a")]
    [InlineData("Prefijo", "ABCDEFGHIJK")]
    [InlineData("Prefijo", "QA-1")]
    [InlineData("Nombre", "   ")]
    public async Task Tipo_CamposFueraDeRegla_NoSeCrea(string campo, string valor)
    {
        var codigo = NuevoCodigoTipo();
        var form = FormTipo(codigo);
        form[campo] = valor;

        await PostAsync("/DocumentosConfig/TipoEdit", form);

        Assert.Null(await TipoAsync(codigo));
    }

    [Fact]
    public async Task Tipo_CodigoYPrefijoEnMinusculas_SeNormalizanAMayusculas()
    {
        var codigo = NuevoCodigoTipo();
        var form = FormTipo(codigo.ToLowerInvariant());
        form["Prefijo"] = "qa";

        await PostAsync("/DocumentosConfig/TipoEdit", form);

        var tipo = await TipoAsync(codigo);
        Assert.NotNull(tipo);
        Assert.Equal("QA", tipo!.Prefijo);
    }

    [Theory]
    [InlineData("Nombre", 151)]
    [InlineData("Descripcion", 501)]
    public async Task Tipo_TextosMasLargosQueLaColumna_NoSeCrea(string campo, int largo)
    {
        var codigo = NuevoCodigoTipo();
        var form = FormTipo(codigo);
        form[campo] = new string('x', largo);

        await PostAsync("/DocumentosConfig/TipoEdit", form);

        var tipo = await TipoAsync(codigo);
        Assert.True(tipo == null, $"{campo} de {largo} caracteres se guardó (la columna admite menos).");
    }

    // ================= Empresa (singleton) =================

    private static Dictionary<string, string> FormEmpresa(string nombre) => new()
    {
        ["Nombre"] = nombre,
        ["Cuit"] = "30-70123456-5",
        ["Domicilio"] = "Calle Falsa 123",
        ["Ciudad"] = "Buenos Aires",
        ["Jurisdiccion"] = "Tribunales ordinarios de la Ciudad de Buenos Aires",
        ["InteresMoraDiarioPorcentaje"] = "0,5",
    };

    private async Task<EmpresaConfiguracion?> EmpresaAsync()
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.EmpresasConfiguracion.AsNoTracking().OrderBy(e => e.Id).FirstOrDefaultAsync();
    }

    private async Task<string> SembrarEmpresaBaseAsync()
    {
        var nombre = $"Empresa QA base {Interlocked.Increment(ref _counter)}";
        await PostAsync("/DocumentosConfig/Empresa", FormEmpresa(nombre));
        Assert.Equal(nombre, (await EmpresaAsync())?.Nombre);
        return nombre;
    }

    [Theory]
    [InlineData("Nombre", 201)]
    [InlineData("Domicilio", 301)]
    [InlineData("Ciudad", 121)]
    [InlineData("Jurisdiccion", 201)]
    public async Task Empresa_TextosMasLargosQueLaColumna_NoSeGuardan(string campo, int largo)
    {
        var nombreBase = await SembrarEmpresaBaseAsync();
        var form = FormEmpresa($"Empresa QA {Interlocked.Increment(ref _counter)}");
        form[campo] = new string('x', largo);

        await PostAsync("/DocumentosConfig/Empresa", form);

        var empresa = await EmpresaAsync();
        Assert.True(empresa!.Nombre.Length <= 200 && empresa.Domicilio.Length <= 300 && empresa.Ciudad.Length <= 120
                    && empresa.Jurisdiccion.Length <= 200, $"{campo} de {largo} caracteres se guardó.");
        Assert.Equal(nombreBase, empresa.Nombre);
    }

    [Theory]
    [InlineData("Cuit", "123")]
    [InlineData("Cuit", "30-1234-5")]
    [InlineData("Dni", "123")]
    [InlineData("Dni", "123456789")]
    [InlineData("Cuit", "abc")]
    [InlineData("Cuit", "30 70123456 5")]
    [InlineData("Cuit", "123456789012345678901")]
    [InlineData("Dni", "12 345 678")]
    [InlineData("Nombre", "   ")]
    [InlineData("Domicilio", "")]
    public async Task Empresa_CamposFueraDeRegla_NoSeGuardan(string campo, string valor)
    {
        var nombreBase = await SembrarEmpresaBaseAsync();
        var form = FormEmpresa($"Empresa QA {Interlocked.Increment(ref _counter)}");
        form[campo] = valor;

        await PostAsync("/DocumentosConfig/Empresa", form);

        Assert.Equal(nombreBase, (await EmpresaAsync())!.Nombre);
    }

    [Theory]
    [InlineData("20-37898524-3", "37.898.524")]    // como están cargados hoy: con guiones y puntos de miles
    [InlineData("20378985243", "37898524")]
    [InlineData("30701234565", "")]
    public async Task Empresa_CuitYDniConFormatoArgentino_SeGuardan(string cuit, string dni)
    {
        var nombre = $"Empresa QA ident {Interlocked.Increment(ref _counter)}";
        var form = FormEmpresa(nombre);
        form["Cuit"] = cuit;
        form["Dni"] = dni;

        await PostAsync("/DocumentosConfig/Empresa", form);

        var empresa = await EmpresaAsync();
        Assert.Equal(nombre, empresa!.Nombre);
        if (dni.Length > 0)
            Assert.Equal(new string(dni.Where(char.IsDigit).ToArray()), empresa.Dni);   // sin puntos
    }

    [Theory]
    [InlineData("1000")]
    [InlineData("-1")]
    public async Task Empresa_InteresMoraDiario_SeIgnora_LaFuenteEsConfiguracionMora(string tasaForjada)
    {
        // El interés por mora no se edita en Documentos config: se toma de Configuración → Mora. Lo posteado no se guarda.
        var nombre = $"Empresa QA tasa {Interlocked.Increment(ref _counter)}";
        var form = FormEmpresa(nombre);
        form["InteresMoraDiarioPorcentaje"] = tasaForjada;

        await PostAsync("/DocumentosConfig/Empresa", form);

        var empresa = await EmpresaAsync();
        Assert.Equal(nombre, empresa!.Nombre);
        Assert.InRange(empresa.InteresMoraDiarioPorcentaje, 0m, 100m);
        Assert.NotEqual(1000m, empresa.InteresMoraDiarioPorcentaje);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0,5")]
    [InlineData("100")]
    public async Task Empresa_InteresMoraDiario_ValidoDeBorde_SeGuarda(string tasa)
    {
        var nombre = $"Empresa QA borde {Interlocked.Increment(ref _counter)}";
        var form = FormEmpresa(nombre);
        form["InteresMoraDiarioPorcentaje"] = tasa;

        await PostAsync("/DocumentosConfig/Empresa", form);

        Assert.Equal(nombre, (await EmpresaAsync())!.Nombre);
    }
}
