using AutoMapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stub mínimo de IWebHostEnvironment (solo WebRootPath es necesario)
// ---------------------------------------------------------------------------

file sealed class StubWebHostEnvironment : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = Path.GetTempPath();
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "TestApp";
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
}

// ---------------------------------------------------------------------------

/// <summary>
/// Tests de integración para DocumentoClienteService.
///
/// Métodos cubiertos (solo usan _context + _logger; _environment solo en Delete/Descargar):
/// - ValidarDocumentacionObligatoriaAsync: completa, faltantes, tipos custom
/// - VerificarAsync: happy path, inexistente→false, persiste campos
/// - RechazarAsync: happy path, inexistente→false, persiste motivo
/// - DeleteAsync: soft-delete, inexistente→false
/// - VerificarTodosAsync: cantidad correcta, sin pendientes→0
/// - MarcarVencidosAsync: vencidos marcados, no-vencidos intactos
/// </summary>
public class DocumentoClienteServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly DocumentoClienteService _service;

    private static int _counter = 600;

    public DocumentoClienteServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => { cfg.AddProfile<MappingProfile>(); },
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = new DocumentoClienteService(
            _context,
            mapper,
            NullLogger<DocumentoClienteService>.Instance,
            new StubWebHostEnvironment());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Cliente> SeedClienteAsync()
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Doc",
            NumeroDocumento = $"8{suffix}",
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<DocumentoCliente> SeedDocumentoAsync(
        int clienteId,
        TipoDocumentoCliente tipo = TipoDocumentoCliente.DNI,
        EstadoDocumento estado = EstadoDocumento.Pendiente,
        DateTime? fechaVencimiento = null,
        string? rutaArchivo = null)
    {
        var doc = new DocumentoCliente
        {
            ClienteId = clienteId,
            TipoDocumento = tipo,
            NombreArchivo = $"archivo_{tipo}.pdf",
            RutaArchivo = rutaArchivo ?? $"uploads/documentos-clientes/archivo_{tipo}.pdf",
            Estado = estado,
            FechaSubida = DateTime.UtcNow,
            FechaVencimiento = fechaVencimiento,
            IsDeleted = false
        };
        _context.DocumentosCliente.Add(doc);
        await _context.SaveChangesAsync();
        return doc;
    }

    // -------------------------------------------------------------------------
    // ValidarDocumentacionObligatoriaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidarDocumentacion_SinDocumentos_NoCompleta()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.ValidarDocumentacionObligatoriaAsync(cliente.Id);

        Assert.False(resultado.Completa);
        Assert.NotEmpty(resultado.Faltantes);
    }

    [Fact]
    public async Task ValidarDocumentacion_TodosVerificados_Completa()
    {
        var cliente = await SeedClienteAsync();

        // Los tipos requeridos por defecto: DNI, ReciboSueldo, Servicio
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Verificado);
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.ReciboSueldo, EstadoDocumento.Verificado);
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.Servicio, EstadoDocumento.Verificado);

        var resultado = await _service.ValidarDocumentacionObligatoriaAsync(cliente.Id);

        Assert.True(resultado.Completa);
        Assert.Empty(resultado.Faltantes);
    }

    [Fact]
    public async Task ValidarDocumentacion_SoloAlgunosVerificados_NoCompleta()
    {
        var cliente = await SeedClienteAsync();

        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Verificado);
        // ReciboSueldo y Servicio faltan

        var resultado = await _service.ValidarDocumentacionObligatoriaAsync(cliente.Id);

        Assert.False(resultado.Completa);
        Assert.Equal(2, resultado.Faltantes.Count);
        Assert.DoesNotContain(TipoDocumentoCliente.DNI, resultado.Faltantes);
    }

    [Fact]
    public async Task ValidarDocumentacion_PendienteNoCuentaComoVerificado()
    {
        var cliente = await SeedClienteAsync();

        // Pendiente no cuenta — debe estar Verificado
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Pendiente);
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.ReciboSueldo, EstadoDocumento.Verificado);
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.Servicio, EstadoDocumento.Verificado);

        var resultado = await _service.ValidarDocumentacionObligatoriaAsync(cliente.Id);

        Assert.False(resultado.Completa);
        Assert.Contains(TipoDocumentoCliente.DNI, resultado.Faltantes);
    }

    [Fact]
    public async Task ValidarDocumentacion_ConTiposCustom_VerificaSoloEsos()
    {
        var cliente = await SeedClienteAsync();

        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Verificado);
        // ReciboSueldo faltante pero no requerido en este llamado

        var resultado = await _service.ValidarDocumentacionObligatoriaAsync(
            cliente.Id,
            requeridos: [TipoDocumentoCliente.DNI]);

        Assert.True(resultado.Completa);
    }

    // -------------------------------------------------------------------------
    // VerificarAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Verificar_DocumentoExistente_RetornaTrue()
    {
        var cliente = await SeedClienteAsync();
        var doc = await SeedDocumentoAsync(cliente.Id);

        var resultado = await _service.VerificarAsync(doc.Id, verificadoPor: "auditor01");

        Assert.True(resultado);
    }

    [Fact]
    public async Task Verificar_DocumentoInexistente_RetornaFalse()
    {
        var resultado = await _service.VerificarAsync(id: 99_999, verificadoPor: "auditor01");

        Assert.False(resultado);
    }

    [Fact]
    public async Task Verificar_PersisteCambioDeEstadoYUsuario()
    {
        var cliente = await SeedClienteAsync();
        var doc = await SeedDocumentoAsync(cliente.Id);
        var antes = DateTime.UtcNow;

        await _service.VerificarAsync(doc.Id, verificadoPor: "auditor01", observaciones: "OK");

        var actualizado = await _context.DocumentosCliente.FindAsync(doc.Id);
        Assert.NotNull(actualizado);
        Assert.Equal(EstadoDocumento.Verificado, actualizado!.Estado);
        Assert.Equal("auditor01", actualizado.VerificadoPor);
        Assert.NotNull(actualizado.FechaVerificacion);
        Assert.True(actualizado.FechaVerificacion >= antes);
        Assert.Equal("OK", actualizado.Observaciones);
    }

    // -------------------------------------------------------------------------
    // RechazarAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Rechazar_DocumentoExistente_RetornaTrue()
    {
        var cliente = await SeedClienteAsync();
        var doc = await SeedDocumentoAsync(cliente.Id);

        var resultado = await _service.RechazarAsync(doc.Id, motivo: "Ilegible", rechazadoPor: "revisor01");

        Assert.True(resultado);
    }

    [Fact]
    public async Task Rechazar_DocumentoInexistente_RetornaFalse()
    {
        var resultado = await _service.RechazarAsync(id: 99_999, motivo: "Motivo", rechazadoPor: "revisor01");

        Assert.False(resultado);
    }

    [Fact]
    public async Task Rechazar_PersisteCambioDeEstadoYMotivo()
    {
        var cliente = await SeedClienteAsync();
        var doc = await SeedDocumentoAsync(cliente.Id);

        await _service.RechazarAsync(doc.Id, motivo: "Foto borrosa", rechazadoPor: "revisor02");

        var actualizado = await _context.DocumentosCliente.FindAsync(doc.Id);
        Assert.NotNull(actualizado);
        Assert.Equal(EstadoDocumento.Rechazado, actualizado!.Estado);
        Assert.Equal("revisor02", actualizado.VerificadoPor);
        Assert.Equal("Foto borrosa", actualizado.MotivoRechazo);
    }

    // -------------------------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_DocumentoExistente_RetornaTrue()
    {
        var cliente = await SeedClienteAsync();
        var doc = await SeedDocumentoAsync(cliente.Id);

        var resultado = await _service.DeleteAsync(doc.Id);

        Assert.True(resultado);
    }

    [Fact]
    public async Task Delete_DocumentoInexistente_RetornaFalse()
    {
        var resultado = await _service.DeleteAsync(id: 99_999);

        Assert.False(resultado);
    }

    [Fact]
    public async Task Delete_MarcaSoftDelete()
    {
        var cliente = await SeedClienteAsync();
        var doc = await SeedDocumentoAsync(cliente.Id);

        await _service.DeleteAsync(doc.Id);

        // Verificar soft-delete ignorando el query filter
        var eliminado = await _context.DocumentosCliente
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == doc.Id);
        Assert.NotNull(eliminado);
        Assert.True(eliminado!.IsDeleted);
    }

    // -------------------------------------------------------------------------
    // VerificarTodosAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task VerificarTodos_ConPendientes_RetornaCantidadVerificada()
    {
        var cliente = await SeedClienteAsync();
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Pendiente);
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.ReciboSueldo, EstadoDocumento.Pendiente);

        var cantidad = await _service.VerificarTodosAsync(cliente.Id, verificadoPor: "admin");

        Assert.Equal(2, cantidad);
    }

    [Fact]
    public async Task VerificarTodos_SinPendientes_RetornaCero()
    {
        var cliente = await SeedClienteAsync();
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Verificado);

        var cantidad = await _service.VerificarTodosAsync(cliente.Id, verificadoPor: "admin");

        Assert.Equal(0, cantidad);
    }

    [Fact]
    public async Task VerificarTodos_PersisteCambioDeEstadoEnTodos()
    {
        var cliente = await SeedClienteAsync();
        var doc1 = await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Pendiente);
        var doc2 = await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.ReciboSueldo, EstadoDocumento.Pendiente);

        await _service.VerificarTodosAsync(cliente.Id, verificadoPor: "supervisor");

        _context.ChangeTracker.Clear();
        var doc1Upd = await _context.DocumentosCliente.FindAsync(doc1.Id);
        var doc2Upd = await _context.DocumentosCliente.FindAsync(doc2.Id);
        Assert.Equal(EstadoDocumento.Verificado, doc1Upd!.Estado);
        Assert.Equal(EstadoDocumento.Verificado, doc2Upd!.Estado);
        Assert.Equal("supervisor", doc1Upd.VerificadoPor);
    }

    [Fact]
    public async Task VerificarTodos_SoloAfectaDocumentosDelCliente()
    {
        var cliente1 = await SeedClienteAsync();
        var cliente2 = await SeedClienteAsync();

        await SeedDocumentoAsync(cliente1.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Pendiente);
        var docCliente2 = await SeedDocumentoAsync(cliente2.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Pendiente);

        await _service.VerificarTodosAsync(cliente1.Id, verificadoPor: "admin");

        _context.ChangeTracker.Clear();
        var noTocado = await _context.DocumentosCliente.FindAsync(docCliente2.Id);
        Assert.Equal(EstadoDocumento.Pendiente, noTocado!.Estado);
    }

    // -------------------------------------------------------------------------
    // MarcarVencidosAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarVencidos_DocumentosVencidos_CambianAVencido()
    {
        var cliente = await SeedClienteAsync();
        var vencido = await SeedDocumentoAsync(
            cliente.Id,
            TipoDocumentoCliente.ReciboSueldo,
            EstadoDocumento.Verificado,
            fechaVencimiento: DateTime.UtcNow.AddDays(-1));

        await _service.MarcarVencidosAsync();

        _context.ChangeTracker.Clear();
        var actualizado = await _context.DocumentosCliente.FindAsync(vencido.Id);
        Assert.Equal(EstadoDocumento.Vencido, actualizado!.Estado);
    }

    [Fact]
    public async Task MarcarVencidos_DocumentosNoVencidos_NoSeCambian()
    {
        var cliente = await SeedClienteAsync();
        var vigente = await SeedDocumentoAsync(
            cliente.Id,
            TipoDocumentoCliente.ReciboSueldo,
            EstadoDocumento.Verificado,
            fechaVencimiento: DateTime.UtcNow.AddDays(30));

        await _service.MarcarVencidosAsync();

        _context.ChangeTracker.Clear();
        var noTocado = await _context.DocumentosCliente.FindAsync(vigente.Id);
        Assert.Equal(EstadoDocumento.Verificado, noTocado!.Estado);
    }

    [Fact]
    public async Task MarcarVencidos_SoloAfectaVerificados_NoPendientes()
    {
        var cliente = await SeedClienteAsync();
        var pendienteVencido = await SeedDocumentoAsync(
            cliente.Id,
            TipoDocumentoCliente.DNI,
            EstadoDocumento.Pendiente, // Pendiente, aunque fecha vencida
            fechaVencimiento: DateTime.UtcNow.AddDays(-10));

        await _service.MarcarVencidosAsync();

        _context.ChangeTracker.Clear();
        var noTocado = await _context.DocumentosCliente.FindAsync(pendienteVencido.Id);
        Assert.Equal(EstadoDocumento.Pendiente, noTocado!.Estado); // Sin cambio
    }

    // -------------------------------------------------------------------------
    // VerificarBatchAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task VerificarBatch_ListaVacia_RetornaCeroExitosos()
    {
        var resultado = await _service.VerificarBatchAsync([], "auditor1");

        Assert.Equal(0, resultado.Exitosos);
        Assert.Equal(0, resultado.Fallidos);
    }

    [Fact]
    public async Task VerificarBatch_TodosPendientes_VerificaTodos()
    {
        var cliente = await SeedClienteAsync();
        var d1 = await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Pendiente);
        var d2 = await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.ReciboSueldo, EstadoDocumento.Pendiente);

        var resultado = await _service.VerificarBatchAsync([d1.Id, d2.Id], "auditor1");

        Assert.Equal(2, resultado.Exitosos);
        Assert.Equal(0, resultado.Fallidos);

        _context.ChangeTracker.Clear();
        var doc1 = await _context.DocumentosCliente.FindAsync(d1.Id);
        var doc2 = await _context.DocumentosCliente.FindAsync(d2.Id);
        Assert.Equal(EstadoDocumento.Verificado, doc1!.Estado);
        Assert.Equal(EstadoDocumento.Verificado, doc2!.Estado);
        Assert.Equal("auditor1", doc1.VerificadoPor);
    }

    [Fact]
    public async Task VerificarBatch_DocumentoYaVerificado_CuentaComoFallido()
    {
        var cliente = await SeedClienteAsync();
        var verificado = await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Verificado);
        var pendiente = await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.ReciboSueldo, EstadoDocumento.Pendiente);

        var resultado = await _service.VerificarBatchAsync([verificado.Id, pendiente.Id], "auditor1");

        Assert.Equal(1, resultado.Exitosos);
        Assert.Equal(1, resultado.Fallidos);
        Assert.Single(resultado.Errores);
        Assert.Equal(verificado.Id, resultado.Errores[0].Id);
    }

    [Fact]
    public async Task VerificarBatch_IdInexistente_CuentaComoFallido()
    {
        var resultado = await _service.VerificarBatchAsync([99999], "auditor1");

        Assert.Equal(0, resultado.Exitosos);
        Assert.Equal(1, resultado.Fallidos);
        Assert.Contains("no encontrado", resultado.Errores[0].Mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerificarBatch_IdsDuplicados_ProcesaSoloUnaVez()
    {
        var cliente = await SeedClienteAsync();
        var doc = await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Pendiente);

        // El mismo ID dos veces — debe procesar una sola vez
        var resultado = await _service.VerificarBatchAsync([doc.Id, doc.Id], "auditor1");

        Assert.Equal(1, resultado.Exitosos);
        Assert.Equal(0, resultado.Fallidos);
    }

    // -------------------------------------------------------------------------
    // RechazarBatchAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RechazarBatch_ListaVacia_RetornaCeroExitosos()
    {
        var resultado = await _service.RechazarBatchAsync([], "Documentación inválida", "revisor1");

        Assert.Equal(0, resultado.Exitosos);
        Assert.Equal(0, resultado.Fallidos);
    }

    [Fact]
    public async Task RechazarBatch_TodosPendientes_RechazaTodos()
    {
        var cliente = await SeedClienteAsync();
        var d1 = await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Pendiente);
        var d2 = await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.ReciboSueldo, EstadoDocumento.Pendiente);

        var resultado = await _service.RechazarBatchAsync([d1.Id, d2.Id], "Foto ilegible", "revisor1");

        Assert.Equal(2, resultado.Exitosos);
        Assert.Equal(0, resultado.Fallidos);

        _context.ChangeTracker.Clear();
        var doc1 = await _context.DocumentosCliente.FindAsync(d1.Id);
        Assert.Equal(EstadoDocumento.Rechazado, doc1!.Estado);
    }

    [Fact]
    public async Task RechazarBatch_IdInexistente_CuentaComoFallido()
    {
        var resultado = await _service.RechazarBatchAsync([99999], "Motivo", "revisor1");

        Assert.Equal(0, resultado.Exitosos);
        Assert.Equal(1, resultado.Fallidos);
    }

    // -------------------------------------------------------------------------
    // BuscarAsync — filtros y paginación
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Buscar_SinFiltros_DevuelveTodos()
    {
        var cliente = await SeedClienteAsync();
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI);
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.ReciboSueldo);

        var (docs, total) = await _service.BuscarAsync(new DocumentoClienteFilterViewModel());

        Assert.Equal(2, total);
        Assert.Equal(2, docs.Count);
    }

    [Fact]
    public async Task Buscar_FiltroClienteId_DevuelveSoloDelCliente()
    {
        var c1 = await SeedClienteAsync();
        var c2 = await SeedClienteAsync();
        await SeedDocumentoAsync(c1.Id, TipoDocumentoCliente.DNI);
        await SeedDocumentoAsync(c2.Id, TipoDocumentoCliente.DNI);

        var (docs, total) = await _service.BuscarAsync(
            new DocumentoClienteFilterViewModel { ClienteId = c1.Id });

        Assert.Equal(1, total);
        Assert.All(docs, d => Assert.Equal(c1.Id, d.ClienteId));
    }

    [Fact]
    public async Task Buscar_FiltroEstado_DevuelveSoloEseEstado()
    {
        var cliente = await SeedClienteAsync();
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Pendiente);
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.ReciboSueldo, EstadoDocumento.Verificado);

        var (docs, total) = await _service.BuscarAsync(
            new DocumentoClienteFilterViewModel { Estado = EstadoDocumento.Verificado });

        Assert.Equal(1, total);
        Assert.All(docs, d => Assert.Equal(EstadoDocumento.Verificado, d.Estado));
    }

    [Fact]
    public async Task Buscar_SoloPendientes_DevuelveSoloPendientes()
    {
        var cliente = await SeedClienteAsync();
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI, EstadoDocumento.Pendiente);
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.ReciboSueldo, EstadoDocumento.Verificado);

        var (docs, total) = await _service.BuscarAsync(
            new DocumentoClienteFilterViewModel { SoloPendientes = true });

        Assert.Equal(1, total);
        Assert.All(docs, d => Assert.Equal(EstadoDocumento.Pendiente, d.Estado));
    }

    [Fact]
    public async Task Buscar_Paginacion_DevuelvePageSizeDocumentos()
    {
        var cliente = await SeedClienteAsync();
        for (int i = 0; i < 5; i++)
            await SeedDocumentoAsync(cliente.Id);

        var (docs, total) = await _service.BuscarAsync(
            new DocumentoClienteFilterViewModel { PageNumber = 1, PageSize = 3 });

        Assert.Equal(5, total);
        Assert.Equal(3, docs.Count);
    }

    [Fact]
    public async Task Buscar_SegundaPagina_DevuelveResto()
    {
        var cliente = await SeedClienteAsync();
        for (int i = 0; i < 5; i++)
            await SeedDocumentoAsync(cliente.Id);

        var (docs, total) = await _service.BuscarAsync(
            new DocumentoClienteFilterViewModel { PageNumber = 2, PageSize = 3 });

        Assert.Equal(5, total);
        Assert.Equal(2, docs.Count);
    }

    [Fact]
    public async Task Buscar_EliminadosNoAparecen()
    {
        var cliente = await SeedClienteAsync();
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI);
        // Documento eliminado manualmente
        var eliminado = new DocumentoCliente
        {
            ClienteId = cliente.Id,
            TipoDocumento = TipoDocumentoCliente.Servicio,
            NombreArchivo = "elim.pdf",
            RutaArchivo = "uploads/elim.pdf",
            Estado = EstadoDocumento.Pendiente,
            FechaSubida = DateTime.UtcNow,
            IsDeleted = true
        };
        _context.DocumentosCliente.Add(eliminado);
        await _context.SaveChangesAsync();

        var (docs, total) = await _service.BuscarAsync(new DocumentoClienteFilterViewModel());

        Assert.Equal(1, total);
    }

    // -------------------------------------------------------------------------
    // GetAllAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAll_SinDocumentos_RetornaVacio()
    {
        var resultado = await _service.GetAllAsync();
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetAll_ConDocumentos_RetornaTodos()
    {
        var cliente = await SeedClienteAsync();
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI);
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.ReciboSueldo);

        var resultado = await _service.GetAllAsync();

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public async Task GetAll_ExcluyeEliminados()
    {
        var cliente = await SeedClienteAsync();
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI);
        var doc = await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.ReciboSueldo);
        doc.IsDeleted = true;
        _context.DocumentosCliente.Update(doc);
        await _context.SaveChangesAsync();

        var resultado = await _service.GetAllAsync();

        Assert.Single(resultado);
    }

    // -------------------------------------------------------------------------
    // GetByIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetById_DocumentoExistente_RetornaViewModel()
    {
        var cliente = await SeedClienteAsync();
        var doc = await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI);

        var resultado = await _service.GetByIdAsync(doc.Id);

        Assert.NotNull(resultado);
        Assert.Equal(doc.Id, resultado!.Id);
        Assert.Equal(TipoDocumentoCliente.DNI, resultado.TipoDocumento);
    }

    [Fact]
    public async Task GetById_DocumentoInexistente_RetornaNull()
    {
        var resultado = await _service.GetByIdAsync(99999);
        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetById_DocumentoEliminado_RetornaNull()
    {
        var cliente = await SeedClienteAsync();
        var doc = await SeedDocumentoAsync(cliente.Id);
        await _service.DeleteAsync(doc.Id);

        var resultado = await _service.GetByIdAsync(doc.Id);
        Assert.Null(resultado);
    }

    // -------------------------------------------------------------------------
    // GetByClienteIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByClienteId_SinDocumentos_RetornaVacio()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.GetByClienteIdAsync(cliente.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetByClienteId_ConDocumentos_RetornaSoloLosDelCliente()
    {
        var c1 = await SeedClienteAsync();
        var c2 = await SeedClienteAsync();
        await SeedDocumentoAsync(c1.Id, TipoDocumentoCliente.DNI);
        await SeedDocumentoAsync(c1.Id, TipoDocumentoCliente.ReciboSueldo);
        await SeedDocumentoAsync(c2.Id, TipoDocumentoCliente.DNI);

        var resultado = await _service.GetByClienteIdAsync(c1.Id);

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, d => Assert.Equal(c1.Id, d.ClienteId));
    }

    [Fact]
    public async Task GetByClienteId_ExcluyeEliminados()
    {
        var cliente = await SeedClienteAsync();
        await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI);
        var doc = await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.ReciboSueldo);
        await _service.DeleteAsync(doc.Id);

        var resultado = await _service.GetByClienteIdAsync(cliente.Id);

        Assert.Single(resultado);
    }

    // -------------------------------------------------------------------------
    // UploadAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Upload_ArchivoNulo_LanzaArgumentException()
    {
        var vm = new DocumentoClienteViewModel
        {
            ClienteId = 1,
            TipoDocumento = TipoDocumentoCliente.DNI,
            Archivo = null
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UploadAsync(vm));
    }

    [Fact]
    public async Task Upload_ExtensionInvalida_LanzaArgumentException()
    {
        var cliente = await SeedClienteAsync();
        var archivo = new FakeFormFile("document.exe", "application/octet-stream",
            new byte[] { 0x4D, 0x5A, 0x00, 0x00 }); // EXE magic bytes

        var vm = new DocumentoClienteViewModel
        {
            ClienteId = cliente.Id,
            TipoDocumento = TipoDocumentoCliente.DNI,
            Archivo = archivo
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UploadAsync(vm));
    }

    [Fact]
    public async Task Upload_ArchivoPdfValido_GuardaDocumentoEnBD()
    {
        var cliente = await SeedClienteAsync();
        // PDF magic bytes: %PDF
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };
        var archivo = new FakeFormFile("documento.pdf", "application/pdf", pdfBytes);

        var vm = new DocumentoClienteViewModel
        {
            ClienteId = cliente.Id,
            TipoDocumento = TipoDocumentoCliente.DNI,
            Archivo = archivo
        };

        var resultado = await _service.UploadAsync(vm);

        Assert.True(resultado.Id > 0);
        Assert.Equal(EstadoDocumento.Pendiente, resultado.Estado);
        Assert.Equal("documento.pdf", resultado.NombreArchivo);

        // Verify persisted in DB
        var doc = await _context.DocumentosCliente.FindAsync(resultado.Id);
        Assert.NotNull(doc);
        Assert.Equal(cliente.Id, doc!.ClienteId);
    }

    [Fact]
    public async Task Upload_ArchivoPdfValido_CreaArchivoFisico()
    {
        var cliente = await SeedClienteAsync();
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };
        var archivo = new FakeFormFile("test.pdf", "application/pdf", pdfBytes);

        var vm = new DocumentoClienteViewModel
        {
            ClienteId = cliente.Id,
            TipoDocumento = TipoDocumentoCliente.DNI,
            Archivo = archivo
        };

        var resultado = await _service.UploadAsync(vm);

        // The file should exist on disk (WebRootPath = TempPath)
        var fullPath = Path.Combine(Path.GetTempPath(), resultado.RutaArchivo!);
        Assert.True(File.Exists(fullPath));

        // Cleanup
        if (File.Exists(fullPath)) File.Delete(fullPath);
    }

    // -------------------------------------------------------------------------
    // DescargarArchivoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DescargarArchivo_DocumentoInexistente_LanzaInvalidOperation()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DescargarArchivoAsync(99999));
    }

    [Fact]
    public async Task DescargarArchivo_ArchivoFisicoAusente_LanzaInvalidOperation()
    {
        var cliente = await SeedClienteAsync();
        var doc = await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI,
            rutaArchivo: "uploads/documentos-clientes/nonexistent.pdf");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DescargarArchivoAsync(doc.Id));
    }

    [Fact]
    public async Task DescargarArchivo_ArchivoExistente_RetornaBytesCorrectos()
    {
        var cliente = await SeedClienteAsync();
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };

        // Write the file to temp path first
        var uploadFolder = Path.Combine(Path.GetTempPath(), "uploads", "documentos-clientes");
        Directory.CreateDirectory(uploadFolder);
        var fileName = $"test_{Guid.NewGuid():N}.pdf";
        var fullPath = Path.Combine(uploadFolder, fileName);
        await File.WriteAllBytesAsync(fullPath, pdfBytes);

        var relPath = Path.Combine("uploads", "documentos-clientes", fileName);
        var doc = await SeedDocumentoAsync(cliente.Id, TipoDocumentoCliente.DNI, rutaArchivo: relPath);

        try
        {
            var resultado = await _service.DescargarArchivoAsync(doc.Id);

            Assert.Equal(pdfBytes, resultado);
        }
        finally
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
    }
}

// ---------------------------------------------------------------------------
// Fake IFormFile para tests de upload
// ---------------------------------------------------------------------------
file sealed class FakeFormFile : IFormFile
{
    private readonly byte[] _content;
    private readonly string _fileName;
    private readonly string _contentType;

    public FakeFormFile(string fileName, string contentType, byte[] content)
    {
        _fileName = fileName;
        _contentType = contentType;
        _content = content;
    }

    public string ContentType => _contentType;
    public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{_fileName}\"";
    public IHeaderDictionary Headers => new HeaderDictionary();
    public long Length => _content.Length;
    public string Name => "file";
    public string FileName => _fileName;

    public void CopyTo(Stream target) => target.Write(_content, 0, _content.Length);
    public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        => await target.WriteAsync(_content, 0, _content.Length, cancellationToken);
    public Stream OpenReadStream() => new MemoryStream(_content);
}
