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
    // COM-S-008: un alta no toma del formulario lo que calcula el servidor
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_IgnoraSituacionBcraYScoringEnviadosPorElCliente()
    {
        var cliente = BuildCliente();
        cliente.SituacionCrediticiaBcra = 1;
        cliente.SituacionCrediticiaDescripcion = "Situación normal";
        cliente.SituacionCrediticiaPeriodo = "202610";
        cliente.SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow;
        cliente.SituacionCrediticiaConsultaOk = true;
        cliente.SituacionCrediticiaBcraUltimoExito = 1;
        cliente.SituacionCrediticiaDescripcionUltimoExito = "Situación normal";
        cliente.SituacionCrediticiaPeriodoUltimoExito = "202610";
        cliente.SituacionCrediticiaUltimoExitoUtc = DateTime.UtcNow;
        cliente.PuntajeCliente = 5;
        cliente.AntiguedadDias = 3650;
        cliente.UltimaVentaFecha = DateTime.UtcNow;
        cliente.CantidadComprasCliente = 99;
        cliente.CreditosEnTermino = 40;
        cliente.CreditosConAtraso = 0;

        var creado = await _service.CreateAsync(cliente);

        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == creado.Id);
        Assert.Null(bd.SituacionCrediticiaBcra);
        Assert.Null(bd.SituacionCrediticiaDescripcion);
        Assert.Null(bd.SituacionCrediticiaPeriodo);
        Assert.Null(bd.SituacionCrediticiaUltimaConsultaUtc);
        Assert.Null(bd.SituacionCrediticiaConsultaOk);
        Assert.Null(bd.SituacionCrediticiaBcraUltimoExito);
        Assert.Null(bd.SituacionCrediticiaDescripcionUltimoExito);
        Assert.Null(bd.SituacionCrediticiaPeriodoUltimoExito);
        Assert.Null(bd.SituacionCrediticiaUltimoExitoUtc);
        Assert.Equal(1, bd.PuntajeCliente);
        Assert.Equal(0, bd.AntiguedadDias);
        Assert.Null(bd.UltimaVentaFecha);
        Assert.Equal(0, bd.CantidadComprasCliente);
        Assert.Equal(0, bd.CreditosEnTermino);
        Assert.Equal(0, bd.CreditosConAtraso);
    }

    [Fact]
    public async Task Create_ConservaLosDatosEditablesDelFormulario()
    {
        var cliente = BuildCliente(nivel: NivelRiesgoCredito.AprobadoCondicional);
        cliente.Telefono = "341-555-0000";
        cliente.Sueldo = 1500000m;

        var creado = await _service.CreateAsync(cliente);

        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == creado.Id);
        Assert.Equal("341-555-0000", bd.Telefono);
        Assert.Equal(1500000m, bd.Sueldo);
        Assert.Equal((int)NivelRiesgoCredito.AprobadoCondicional * 2m, bd.PuntajeRiesgo);
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

    [Fact]
    public async Task Create_CuilCuitConGuiones_NormalizaAntesDePersistir()
    {
        var cliente = BuildCliente("12345678");
        cliente.CuilCuit = "20-12345678-6";

        await _service.CreateAsync(cliente);

        Assert.Equal("20123456786", cliente.CuilCuit);

        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal("20123456786", bd.CuilCuit);
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
        await _context.Entry(cliente).ReloadAsync();   // RowVersion que genera SQLite: pedir la configuración de crédito lo exige

        var actualizado = new Cliente
        {
            Id = cliente.Id,
            RowVersion = cliente.RowVersion,
            Nombre = "NuevoNombre",
            Apellido = "NuevoApellido",
            TipoDocumento = cliente.TipoDocumento,
            NumeroDocumento = cliente.NumeroDocumento,
            Email = "nuevo@test.com",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            Activo = true
        };

        // La calificación (NivelRiesgo) es parte de la configuración de crédito: solo se cambia pidiéndolo (configurecredit).
        var resultado = await _service.UpdateAsync(actualizado, actualizarConfiguracionCredito: true);

        Assert.Equal("NuevoNombre", resultado.Nombre);
        Assert.Equal(10m, resultado.PuntajeRiesgo); // AprobadoTotal * 2
    }

    [Fact]
    public async Task Update_PersisteLosCamposDelFormularioQueAntesSeDescartaban()
    {
        // COM-S-014: CodigoPostal, Empleador, teléfonos adicionales, Observaciones y la configuración de crédito
        // personalizada viajan en el formulario de edición y no se guardaban.
        var cliente = await SeedClienteAsync();
        var actualizado = new Cliente
        {
            Id = cliente.Id, Nombre = cliente.Nombre, Apellido = cliente.Apellido, TipoDocumento = cliente.TipoDocumento,
            NumeroDocumento = cliente.NumeroDocumento, Email = cliente.Email, NivelRiesgo = cliente.NivelRiesgo, Activo = true,
            CodigoPostal = "2000", Empleador = "ACME SA", TelefonoAlternativo = "341-111-0000", TelefonoLaboral = "341-222-0000",
            Observaciones = "cliente con ficha revisada",
            TasaInteresMensualPersonalizada = 4.5m, GastosAdministrativosPersonalizados = 2m, CuotasMaximasPersonalizadas = 18,
            MontoMinimoPersonalizado = 10000m, MontoMaximoPersonalizado = 900000m
        };

        await _context.Entry(cliente).ReloadAsync();
        actualizado.RowVersion = cliente.RowVersion;   // el formulario real envía la versión que vio el usuario

        await _service.UpdateAsync(actualizado, actualizarConfiguracionCredito: true);

        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal("2000", bd.CodigoPostal);
        Assert.Equal("ACME SA", bd.Empleador);
        Assert.Equal("341-111-0000", bd.TelefonoAlternativo);
        Assert.Equal("341-222-0000", bd.TelefonoLaboral);
        Assert.Equal("cliente con ficha revisada", bd.Observaciones);
        Assert.Equal(4.5m, bd.TasaInteresMensualPersonalizada);
        Assert.Equal(2m, bd.GastosAdministrativosPersonalizados);
        Assert.Equal(18, bd.CuotasMaximasPersonalizadas);
        Assert.Equal(10000m, bd.MontoMinimoPersonalizado);
        Assert.Equal(900000m, bd.MontoMaximoPersonalizado);
    }

    [Fact]
    public async Task Update_ConConfiguracionDeCredito_SinRowVersion_Rechaza_YNoCambiaNada()
    {
        var cliente = await SeedClienteAsync();
        var actualizado = new Cliente
        {
            Id = cliente.Id, Nombre = "Otro", Apellido = cliente.Apellido, TipoDocumento = cliente.TipoDocumento,
            NumeroDocumento = cliente.NumeroDocumento, Email = cliente.Email, NivelRiesgo = cliente.NivelRiesgo, Activo = true,
            TasaInteresMensualPersonalizada = 9m
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(actualizado, actualizarConfiguracionCredito: true));
        actualizado.RowVersion = Array.Empty<byte>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(actualizado, actualizarConfiguracionCredito: true));

        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal("Test", bd.Nombre);
        Assert.Null(bd.TasaInteresMensualPersonalizada);
    }

    [Fact]
    public async Task Update_ConConfiguracionDeCredito_ConRowVersionObsoleta_Conflicto_YNoCambiaNada()
    {
        var cliente = await SeedClienteAsync();
        await _context.Entry(cliente).ReloadAsync();
        var actualizado = new Cliente
        {
            Id = cliente.Id, RowVersion = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 }, Nombre = "Otro", Apellido = cliente.Apellido,
            TipoDocumento = cliente.TipoDocumento, NumeroDocumento = cliente.NumeroDocumento, Email = cliente.Email,
            NivelRiesgo = cliente.NivelRiesgo, Activo = true, TasaInteresMensualPersonalizada = 9m
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(actualizado, actualizarConfiguracionCredito: true));

        Assert.Contains("concurrencia", ex.Message, StringComparison.OrdinalIgnoreCase);
        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal("Test", bd.Nombre);
        Assert.Null(bd.TasaInteresMensualPersonalizada);
    }

    [Fact]
    public async Task Update_NoTocaLaSituacionBcraNiElScoringCalculados()
    {
        var cliente = await SeedClienteAsync();
        cliente.SituacionCrediticiaBcra = 2;
        cliente.PuntajeCliente = 4;
        cliente.CreditosEnTermino = 7;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var actualizado = new Cliente
        {
            Id = cliente.Id, Nombre = "Otro", Apellido = cliente.Apellido, TipoDocumento = cliente.TipoDocumento,
            NumeroDocumento = cliente.NumeroDocumento, Email = cliente.Email, NivelRiesgo = cliente.NivelRiesgo, Activo = true,
            SituacionCrediticiaBcra = 1, PuntajeCliente = 5, CreditosEnTermino = 99
        };

        await _service.UpdateAsync(actualizado);

        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal(2, bd.SituacionCrediticiaBcra);
        Assert.Equal(4, bd.PuntajeCliente);
        Assert.Equal(7, bd.CreditosEnTermino);
    }

    [Fact]
    public async Task Update_ClienteExistente_ActualizaCuilCuit()
    {
        var cliente = await SeedClienteAsync("12345678");

        var actualizado = new Cliente
        {
            Id = cliente.Id,
            Nombre = cliente.Nombre,
            Apellido = cliente.Apellido,
            TipoDocumento = cliente.TipoDocumento,
            NumeroDocumento = cliente.NumeroDocumento,
            CuilCuit = "20-12345678-6",
            Email = cliente.Email,
            NivelRiesgo = cliente.NivelRiesgo,
            Activo = true
        };

        var resultado = await _service.UpdateAsync(actualizado);

        Assert.Equal("20123456786", resultado.CuilCuit);

        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal("20123456786", bd.CuilCuit);
    }

    [Fact]
    public async Task Update_ClienteExistente_PermiteLimpiarCuilCuit()
    {
        var cliente = await SeedClienteAsync("12345678");
        cliente.CuilCuit = "20123456786";
        await _context.SaveChangesAsync();

        var actualizado = new Cliente
        {
            Id = cliente.Id,
            Nombre = cliente.Nombre,
            Apellido = cliente.Apellido,
            TipoDocumento = cliente.TipoDocumento,
            NumeroDocumento = cliente.NumeroDocumento,
            CuilCuit = "",
            Email = cliente.Email,
            NivelRiesgo = cliente.NivelRiesgo,
            Activo = true
        };

        var resultado = await _service.UpdateAsync(actualizado);

        Assert.Null(resultado.CuilCuit);

        _context.ChangeTracker.Clear();
        var bd = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Null(bd.CuilCuit);
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
    // Nivel crediticio manual
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AsignarNivelCreditoManual_ClienteExistente_PersisteYRegistraHistorial()
    {
        var cliente = await SeedClienteAsync(nivel: NivelRiesgoCredito.AprobadoCondicional);

        var resultado = await _service.AsignarNivelCreditoManualAsync(
            cliente.Id,
            1,
            "Control manual",
            "supervisor");

        Assert.True(resultado);

        _context.ChangeTracker.Clear();
        var config = await _context.ClientesCreditoConfiguraciones
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClienteId == cliente.Id);

        Assert.NotNull(config);
        Assert.Equal(1, config!.NivelCreditoManual);
        Assert.Equal("Control manual", config.MotivoNivelCreditoManual);
        Assert.Equal("supervisor", config.NivelCreditoManualAsignadoPor);
        Assert.NotNull(config.NivelCreditoManualAsignadoEnUtc);

        var historial = await _context.ClientesPuntajeHistorial
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.ClienteId == cliente.Id && h.Origen == "PuntajeCreditoManual");

        Assert.NotNull(historial);
        Assert.Equal(1, historial!.Puntaje);
        Assert.Equal("supervisor", historial.RegistradoPor);
    }

    [Fact]
    public async Task LimpiarNivelCreditoManual_ClienteExistente_LimpiaOverrideYRegistraHistorial()
    {
        var cliente = await SeedClienteAsync(nivel: NivelRiesgoCredito.AprobadoCondicional);
        await _service.AsignarNivelCreditoManualAsync(
            cliente.Id,
            1,
            "Control manual",
            "supervisor");

        var resultado = await _service.LimpiarNivelCreditoManualAsync(
            cliente.Id,
            "Fin de excepcion",
            "supervisor2");

        Assert.True(resultado);

        _context.ChangeTracker.Clear();
        var config = await _context.ClientesCreditoConfiguraciones
            .AsNoTracking()
            .FirstAsync(c => c.ClienteId == cliente.Id);

        Assert.Null(config.NivelCreditoManual);
        Assert.Null(config.MotivoNivelCreditoManual);
        Assert.Null(config.NivelCreditoManualAsignadoPor);
        Assert.Null(config.NivelCreditoManualAsignadoEnUtc);

        var historial = await _context.ClientesPuntajeHistorial
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.ClienteId == cliente.Id && h.Origen == "PuntajeCreditoManualLimpio");

        Assert.NotNull(historial);
        Assert.Equal(NivelRiesgoCredito.AprobadoCondicional, historial!.NivelRiesgo);
        Assert.Equal("supervisor2", historial.RegistradoPor);
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
