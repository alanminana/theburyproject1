using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Modal "Completar datos del cliente": lectura de faltantes y actualización acotada
/// a los campos que exige el contrato de crédito.
/// </summary>
public class ClienteDatosContractualesServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ClienteDatosContractualesService _service;

    public ClienteDatosContractualesServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
        _service = new ClienteDatosContractualesService(_context, NullLogger<ClienteDatosContractualesService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<Cliente> SeedAsync(string domicilio = "", string? localidad = null, string telefono = "")
    {
        var doc = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente
        {
            Nombre = "Ana", Apellido = "Paz", TipoDocumento = "DNI", NumeroDocumento = doc,
            Domicilio = domicilio, Localidad = localidad, Telefono = telefono, Email = $"{doc}@t.com"
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private static ClienteDatosContractualesDto Completo(Cliente c) => new()
    {
        ClienteId = c.Id, Nombre = c.Nombre, Apellido = c.Apellido, TipoDocumento = c.TipoDocumento,
        NumeroDocumento = c.NumeroDocumento, Domicilio = "Calle 123", Localidad = "Rosario", Telefono = "3415550000"
    };

    [Fact]
    public async Task Obtener_ListaSoloLosCamposVacios()
    {
        var cliente = await SeedAsync(domicilio: "  ", localidad: null, telefono: "341555");

        var estado = await _service.ObtenerAsync(cliente.Id);

        Assert.NotNull(estado);
        Assert.Equal(new[] { "Domicilio", "Localidad" }, estado!.Faltantes);
    }

    [Fact]
    public async Task Obtener_ClienteInexistente_RetornaNull()
    {
        Assert.Null(await _service.ObtenerAsync(9999));
    }

    [Fact]
    public async Task Actualizar_GuardaLosDatosYQuitaLosFaltantes()
    {
        var cliente = await SeedAsync();

        var resultado = await _service.ActualizarAsync(Completo(cliente));

        Assert.True(resultado.Exito);
        var estado = await _service.ObtenerAsync(cliente.Id);
        Assert.Empty(estado!.Faltantes);
        Assert.Equal("Calle 123", estado.Datos.Domicilio);
    }

    [Fact]
    public async Task Actualizar_CampoObligatorioVacio_NoGuarda()
    {
        var cliente = await SeedAsync();
        var datos = Completo(cliente);
        datos.Localidad = "   ";

        var resultado = await _service.ActualizarAsync(datos);

        Assert.False(resultado.Exito);
        Assert.Contains(resultado.Errores, e => e.Contains("Localidad"));
        Assert.Contains("Domicilio", (await _service.ObtenerAsync(cliente.Id))!.Faltantes);
    }

    [Fact]
    public async Task Actualizar_DocumentoFaltanteDeOtroCliente_Rechaza()
    {
        var otro = await SeedAsync("x", "y", "1");
        otro.NumeroDocumento = "30111222";
        var cliente = await SeedAsync();
        cliente.NumeroDocumento = "";
        await _context.SaveChangesAsync();
        var datos = Completo(cliente);
        datos.NumeroDocumento = otro.NumeroDocumento;

        var resultado = await _service.ActualizarAsync(datos);

        Assert.False(resultado.Exito);
        Assert.Contains(resultado.Errores, e => e.Contains("documento"));
    }

    [Theory]
    [InlineData("abc12345678")]
    [InlineData("123")]
    [InlineData("12345678901234567")]
    public async Task Actualizar_TelefonoInvalido_NoGuarda(string telefono)
    {
        var cliente = await SeedAsync();
        var datos = Completo(cliente);
        datos.Telefono = telefono;

        var resultado = await _service.ActualizarAsync(datos);

        Assert.False(resultado.Exito);
        Assert.Contains("Domicilio", (await _service.ObtenerAsync(cliente.Id))!.Faltantes);
    }

    [Fact]
    public async Task Actualizar_NombreConNumeros_NoGuarda()
    {
        var cliente = await SeedAsync();
        var datos = Completo(cliente);
        datos.Nombre = "Ana123";

        Assert.False((await _service.ActualizarAsync(datos)).Exito);
    }

    [Fact]
    public async Task Actualizar_DocumentoYaCargado_NoSeModifica()
    {
        var cliente = await SeedAsync();
        var original = cliente.NumeroDocumento;
        var datos = Completo(cliente);
        datos.NumeroDocumento = "30111222";

        var resultado = await _service.ActualizarAsync(datos);

        Assert.True(resultado.Exito);
        _context.ChangeTracker.Clear();
        Assert.Equal(original, (await _context.Clientes.FindAsync(cliente.Id))!.NumeroDocumento);
    }

    [Fact]
    public async Task Actualizar_NoTocaOtrosCampos()
    {
        var cliente = await SeedAsync();
        cliente.Email = "keep@t.com";
        await _context.SaveChangesAsync();

        await _service.ActualizarAsync(Completo(cliente));

        _context.ChangeTracker.Clear();
        Assert.Equal("keep@t.com", (await _context.Clientes.FindAsync(cliente.Id))!.Email);
    }
}
