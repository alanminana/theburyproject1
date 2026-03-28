using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para ClienteService.
/// Cubren CreateAsync (documento único, cálculo PuntajeRiesgo), UpdateAsync
/// (concurrencia, documento duplicado), DeleteAsync (soft-delete), ExisteDocumentoAsync,
/// GetByDocumentoAsync, SearchAsync y ActualizarPuntajeRiesgoAsync.
/// </summary>
public class ClienteServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ClienteService _service;

    public ClienteServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new ClienteService(_context, NullLogger<ClienteService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Cliente BuildCliente(
        string? numeroDocumento = null,
        NivelRiesgoCredito nivel = NivelRiesgoCredito.AprobadoCondicional)
    {
        var doc = numeroDocumento ?? Guid.NewGuid().ToString("N")[..8];
        return new Cliente
        {
            Nombre = "Test",
            Apellido = "Cliente",
            TipoDocumento = "DNI",
            NumeroDocumento = doc,
            Email = $"{doc}@test.com",
            NivelRiesgo = nivel,
            Activo = true
        };
    }

    private async Task<Cliente> SeedClienteAsync(
        string? numeroDocumento = null,
        NivelRiesgoCredito nivel = NivelRiesgoCredito.AprobadoCondicional)
    {
        var cliente = BuildCliente(numeroDocumento, nivel);
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        await _context.Entry(cliente).ReloadAsync();
        return cliente;
    }

    // -------------------------------------------------------------------------
    // CreateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_ClienteNuevo_Persiste()
    {
        var cliente = BuildCliente();

        var resultado = await _service.CreateAsync(cliente);

        Assert.True(resultado.Id > 0);
        var bd = await _context.Clientes.FirstOrDefaultAsync(c => c.Id == resultado.Id);
        Assert.NotNull(bd);
        Assert.Equal(cliente.NumeroDocumento, bd!.NumeroDocumento);
    }

    [Fact]
    public async Task Create_CalculaPuntajeRiesgoDeNivelRiesgo()
    {
        // NivelRiesgo.AprobadoTotal = 5 → PuntajeRiesgo = 5 * 2 = 10
        var cliente = BuildCliente(nivel: NivelRiesgoCredito.AprobadoTotal);

        await _service.CreateAsync(cliente);

        Assert.Equal(10m, cliente.PuntajeRiesgo);
    }

    [Theory]
    [InlineData(NivelRiesgoCredito.Rechazado, 2)]
    [InlineData(NivelRiesgoCredito.RechazadoRevisar, 4)]
    [InlineData(NivelRiesgoCredito.AprobadoCondicional, 6)]
    [InlineData(NivelRiesgoCredito.AprobadoLimitado, 8)]
    [InlineData(NivelRiesgoCredito.AprobadoTotal, 10)]
    public async Task Create_PuntajeRiesgoEsNivelPorDos(NivelRiesgoCredito nivel, decimal puntajeEsperado)
    {
        var cliente = BuildCliente(nivel: nivel);

        await _service.CreateAsync(cliente);

        Assert.Equal(puntajeEsperado, cliente.PuntajeRiesgo);
    }

    [Fact]
    public async Task Create_DocumentoDuplicado_LanzaExcepcion()
    {
        var doc = Guid.NewGuid().ToString("N")[..8];
        await SeedClienteAsync(doc);

        var duplicado = BuildCliente(doc);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(duplicado));
    }

    // -------------------------------------------------------------------------
    // UpdateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Update_ClienteExistente_ActualizaCampos()
    {
        var cliente = await SeedClienteAsync();

        var actualizado = new Cliente
        {
            Id = cliente.Id,
            Nombre = "NuevoNombre",
            Apellido = "NuevoApellido",
            TipoDocumento = cliente.TipoDocumento,
            NumeroDocumento = cliente.NumeroDocumento,
            Email = "nuevo@test.com",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            Activo = true
        };

        var resultado = await _service.UpdateAsync(actualizado);

        Assert.Equal("NuevoNombre", resultado.Nombre);
        Assert.Equal(10m, resultado.PuntajeRiesgo); // AprobadoTotal * 2
    }

    [Fact]
    public async Task Update_ClienteNoExiste_LanzaExcepcion()
    {
        var inexistente = BuildCliente();
        inexistente.Id = 99999;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(inexistente));
    }

    [Fact]
    public async Task Update_DocumentoDuplicadoDeOtroCliente_LanzaExcepcion()
    {
        var doc1 = Guid.NewGuid().ToString("N")[..8];
        var doc2 = Guid.NewGuid().ToString("N")[..8];
        await SeedClienteAsync(doc1);
        var cliente2 = await SeedClienteAsync(doc2);

        // Intentar cambiar el doc de cliente2 al doc de cliente1
        var actualizado = new Cliente
        {
            Id = cliente2.Id,
            Nombre = "Test",
            Apellido = "Cliente",
            TipoDocumento = "DNI",
            NumeroDocumento = doc1, // duplicado
            Email = "x@test.com",
            NivelRiesgo = NivelRiesgoCredito.AprobadoCondicional,
            Activo = true
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(actualizado));
    }

    [Fact]
    public async Task Update_MismoDocumento_NoConsideraDocumentoDuplicado()
    {
        var cliente = await SeedClienteAsync();

        // Actualizar sin cambiar el documento — no debe lanzar excepción
        var actualizado = new Cliente
        {
            Id = cliente.Id,
            Nombre = "Modificado",
            Apellido = cliente.Apellido,
            TipoDocumento = cliente.TipoDocumento,
            NumeroDocumento = cliente.NumeroDocumento, // mismo doc
            Email = cliente.Email,
            NivelRiesgo = cliente.NivelRiesgo,
            Activo = true
        };

        var resultado = await _service.UpdateAsync(actualizado);

        Assert.Equal("Modificado", resultado.Nombre);
    }

    // -------------------------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_ClienteExistente_SoftDelete()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.DeleteAsync(cliente.Id);

        Assert.True(resultado);
        // No debe aparecer en queries normales
        var bd = await _context.Clientes.FirstOrDefaultAsync(c => c.Id == cliente.Id && !c.IsDeleted);
        Assert.Null(bd);
        // Pero existe con IsDeleted=true
        var eliminado = await _context.Clientes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == cliente.Id);
        Assert.NotNull(eliminado);
        Assert.True(eliminado!.IsDeleted);
    }

    [Fact]
    public async Task Delete_ClienteNoExiste_RetornaFalse()
    {
        var resultado = await _service.DeleteAsync(99999);

        Assert.False(resultado);
    }

    [Fact]
    public async Task Delete_ClienteYaEliminado_RetornaFalse()
    {
        var cliente = await SeedClienteAsync();
        await _service.DeleteAsync(cliente.Id);

        // Segunda eliminación — no existe ya
        var resultado = await _service.DeleteAsync(cliente.Id);

        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // ExisteDocumentoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExisteDocumento_DocumentoExistente_ReturnsTrue()
    {
        var cliente = await SeedClienteAsync();

        var existe = await _service.ExisteDocumentoAsync(cliente.TipoDocumento, cliente.NumeroDocumento);

        Assert.True(existe);
    }

    [Fact]
    public async Task ExisteDocumento_DocumentoNoExistente_ReturnsFalse()
    {
        var existe = await _service.ExisteDocumentoAsync("DNI", "99999999");

        Assert.False(existe);
    }

    [Fact]
    public async Task ExisteDocumento_ExcluyendoMismoId_ReturnsFalse()
    {
        var cliente = await SeedClienteAsync();

        // Al actualizar el mismo registro, no debe considerarse duplicado
        var existe = await _service.ExisteDocumentoAsync(
            cliente.TipoDocumento, cliente.NumeroDocumento, cliente.Id);

        Assert.False(existe);
    }

    // -------------------------------------------------------------------------
    // GetByDocumentoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByDocumento_DocumentoExistente_RetornaCliente()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.GetByDocumentoAsync(cliente.TipoDocumento, cliente.NumeroDocumento);

        Assert.NotNull(resultado);
        Assert.Equal(cliente.Id, resultado!.Id);
    }

    [Fact]
    public async Task GetByDocumento_DocumentoNoExistente_RetornaNull()
    {
        var resultado = await _service.GetByDocumentoAsync("DNI", "99999999");

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetByDocumento_ClienteEliminado_RetornaNull()
    {
        var cliente = await SeedClienteAsync();
        await _service.DeleteAsync(cliente.Id);

        var resultado = await _service.GetByDocumentoAsync(cliente.TipoDocumento, cliente.NumeroDocumento);

        Assert.Null(resultado);
    }

    // -------------------------------------------------------------------------
    // GetByIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetById_ClienteExistente_RetornaCliente()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.GetByIdAsync(cliente.Id);

        Assert.NotNull(resultado);
        Assert.Equal(cliente.NumeroDocumento, resultado!.NumeroDocumento);
    }

    [Fact]
    public async Task GetById_ClienteNoExiste_RetornaNull()
    {
        var resultado = await _service.GetByIdAsync(99999);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetById_ClienteEliminado_RetornaNull()
    {
        var cliente = await SeedClienteAsync();
        await _service.DeleteAsync(cliente.Id);

        var resultado = await _service.GetByIdAsync(cliente.Id);

        Assert.Null(resultado);
    }

    // -------------------------------------------------------------------------
    // ActualizarPuntajeRiesgoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ActualizarPuntajeRiesgo_ClienteExistente_ActualizaYRegistraHistorial()
    {
        var cliente = await SeedClienteAsync();

        await _service.ActualizarPuntajeRiesgoAsync(cliente.Id, 9.5m, "evaluador1");

        _context.ChangeTracker.Clear();
        var clienteBd = await _context.Clientes.FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal(9.5m, clienteBd.PuntajeRiesgo);

        var historial = await _context.ClientesPuntajeHistorial
            .FirstOrDefaultAsync(h => h.ClienteId == cliente.Id);
        Assert.NotNull(historial);
        Assert.Equal(9.5m, historial!.Puntaje);
        Assert.Equal("evaluador1", historial.RegistradoPor);
    }

    [Fact]
    public async Task ActualizarPuntajeRiesgo_ClienteNoExiste_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ActualizarPuntajeRiesgoAsync(99999, 5m, "evaluador1"));
    }

    // -------------------------------------------------------------------------
    // SearchAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Search_PorNombre_RetornaCoincidencias()
    {
        await SeedClienteAsync();
        var cliente = new Cliente
        {
            Nombre = "Marcelo",
            Apellido = "Garcia",
            TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            Email = "marcelo@test.com",
            Activo = true
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var resultado = await _service.SearchAsync("Marcelo", null, null, null, null, null, null);

        Assert.Single(resultado);
        Assert.Equal("Marcelo", resultado.First().Nombre);
    }

    [Fact]
    public async Task Search_SoloActivos_ExcluyeInactivos()
    {
        var activo = await SeedClienteAsync();
        var inactivo = new Cliente
        {
            Nombre = "Test",
            Apellido = "Inactivo",
            TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            Email = "inactivo@test.com",
            Activo = false
        };
        _context.Clientes.Add(inactivo);
        await _context.SaveChangesAsync();

        var resultado = await _service.SearchAsync(null, null, soloActivos: true, null, null, null, null);

        Assert.All(resultado, c => Assert.True(c.Activo));
        Assert.DoesNotContain(resultado, c => c.Id == inactivo.Id);
    }

    [Fact]
    public async Task Search_PorPuntajeMinimo_FiltraCorrectamente()
    {
        await SeedClienteAsync(nivel: NivelRiesgoCredito.Rechazado);     // puntaje 2
        await SeedClienteAsync(nivel: NivelRiesgoCredito.AprobadoTotal);  // puntaje 10

        var resultado = await _service.SearchAsync(null, null, null, null, puntajeMinimo: 9m, null, null);

        Assert.All(resultado, c => Assert.True(c.PuntajeRiesgo >= 9m));
    }

    // =========================================================================
    // GetAllAsync
    // =========================================================================

    [Fact]
    public async Task GetAll_SinClientes_RetornaVacio()
    {
        var resultado = await _service.GetAllAsync();
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetAll_ConClientes_DevuelveTodos()
    {
        await SeedClienteAsync();
        await SeedClienteAsync();

        var resultado = await _service.GetAllAsync();

        Assert.Equal(2, resultado.Count());
    }

    [Fact]
    public async Task GetAll_ExcluyeEliminados()
    {
        var cliente = await SeedClienteAsync();
        await _service.DeleteAsync(cliente.Id);

        var resultado = await _service.GetAllAsync();

        Assert.Empty(resultado);
    }
}
