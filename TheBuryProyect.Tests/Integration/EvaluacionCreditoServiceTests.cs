using AutoMapper;
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

public class EvaluacionCreditoServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly EvaluacionCreditoService _service;

    public EvaluacionCreditoServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = new EvaluacionCreditoService(
            _context,
            mapper,
            NullLogger<EvaluacionCreditoService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Crea un cliente básico apto para obtener Aprobado:
    /// PuntajeRiesgo=8 (Excelente=30pts), Sueldo=100000, 3 docs verificados,
    /// 2 créditos finalizados.
    /// </summary>
    private async Task<Cliente> SeedClienteAprobable(
        decimal puntajeRiesgo = 8.0m,
        decimal? sueldo = 100_000m,
        bool agregarCreditos = true)
    {
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Aprobable",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            Telefono = "123456789",
            Domicilio = "Calle 123",
            PuntajeRiesgo = puntajeRiesgo,
            Sueldo = sueldo,
            Activo = true
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        if (agregarCreditos)
        {
            _context.Creditos.AddRange(
                new Credito { ClienteId = cliente.Id, Numero = Guid.NewGuid().ToString("N")[..10], Estado = EstadoCredito.Finalizado, MontoSolicitado = 1000, SaldoPendiente = 0, TasaInteres = 0.05m, CantidadCuotas = 6 },
                new Credito { ClienteId = cliente.Id, Numero = Guid.NewGuid().ToString("N")[..10], Estado = EstadoCredito.Finalizado, MontoSolicitado = 2000, SaldoPendiente = 0, TasaInteres = 0.05m, CantidadCuotas = 12 });
            await _context.SaveChangesAsync();
        }

        return cliente;
    }

    private async Task SeedDocumentosCompletos(int clienteId)
    {
        _context.DocumentosCliente.AddRange(
            new DocumentoCliente { ClienteId = clienteId, TipoDocumento = TipoDocumentoCliente.DNI, Estado = EstadoDocumento.Verificado, NombreArchivo = "dni.pdf", RutaArchivo = "/docs/dni.pdf" },
            new DocumentoCliente { ClienteId = clienteId, TipoDocumento = TipoDocumentoCliente.ReciboSueldo, Estado = EstadoDocumento.Verificado, NombreArchivo = "recibo.pdf", RutaArchivo = "/docs/recibo.pdf" },
            new DocumentoCliente { ClienteId = clienteId, TipoDocumento = TipoDocumentoCliente.Servicio, Estado = EstadoDocumento.Verificado, NombreArchivo = "servicio.pdf", RutaArchivo = "/docs/servicio.pdf" });
        await _context.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // EvaluarSolicitudAsync — cliente no encontrado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EvaluarSolicitud_ClienteNoExiste_LanzaInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.EvaluarSolicitudAsync(clienteId: 9999, montoSolicitado: 10_000m));
    }

    // -------------------------------------------------------------------------
    // EvaluarSolicitudAsync — camino Aprobado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EvaluarSolicitud_TodosCumplidos_RetornaAprobado()
    {
        // PuntajeRiesgo=8 → 30pts, Sueldo=100000, cuota estimada=1000 → relación=1% (<=25%) → 25pts
        // 3 docs verificados → 20pts, 2 créditos finalizados → 15pts, sin garante requerido → 0pts
        // Total=70 → Aprobado
        var cliente = await SeedClienteAprobable(puntajeRiesgo: 8.0m, sueldo: 100_000m);
        await SeedDocumentosCompletos(cliente.Id);

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 10_000m);

        Assert.Equal(ResultadoEvaluacion.Aprobado, result.Resultado);
        Assert.True(result.PuntajeFinal >= 70);
    }

    [Fact]
    public async Task EvaluarSolicitud_AprobadoPersistido_AsignaId()
    {
        var cliente = await SeedClienteAprobable();
        await SeedDocumentosCompletos(cliente.Id);

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 10_000m);

        Assert.True(result.Id > 0);

        var enBd = await _context.EvaluacionesCredito.FindAsync(result.Id);
        Assert.NotNull(enBd);
        Assert.Equal(cliente.Id, enBd.ClienteId);
        Assert.Equal(ResultadoEvaluacion.Aprobado, enBd.Resultado);
    }

    [Fact]
    public async Task EvaluarSolicitud_ConGaranteYMontoAlto_RetornaAprobado()
    {
        // Monto >= 500000 requiere garante; con garante → 10pts extra
        var cliente = await SeedClienteAprobable(puntajeRiesgo: 8.0m, sueldo: 5_000_000m);
        await SeedDocumentosCompletos(cliente.Id);

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 600_000m, garanteId: 1);

        Assert.Equal(ResultadoEvaluacion.Aprobado, result.Resultado);
        Assert.True(result.TieneGarante);
    }

    // -------------------------------------------------------------------------
    // EvaluarSolicitudAsync — camino Rechazado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EvaluarSolicitud_PuntajeRiesgoCritico_RetornaRechazado()
    {
        // PuntajeRiesgo=1 < PuntajeRiesgoMinimo(3.0) → EsCritica=true → Rechazado
        var cliente = await SeedClienteAprobable(puntajeRiesgo: 1.0m, sueldo: 100_000m);
        await SeedDocumentosCompletos(cliente.Id);

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 10_000m);

        Assert.Equal(ResultadoEvaluacion.Rechazado, result.Resultado);
        var reglaRiesgo = result.Reglas.Single(r => r.Nombre == "Puntaje de Riesgo");
        Assert.True(reglaRiesgo.EsCritica);
        Assert.False(reglaRiesgo.Cumple);
    }

    [Fact]
    public async Task EvaluarSolicitud_SinIngresos_RetornaRechazado()
    {
        // Sin sueldo → EsCritica=true en regla Capacidad de Pago → Rechazado
        var cliente = await SeedClienteAprobable(puntajeRiesgo: 8.0m, sueldo: null);
        await SeedDocumentosCompletos(cliente.Id);

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 10_000m);

        Assert.Equal(ResultadoEvaluacion.Rechazado, result.Resultado);
        var reglaIngresos = result.Reglas.Single(r => r.Nombre == "Capacidad de Pago");
        Assert.True(reglaIngresos.EsCritica);
        Assert.False(reglaIngresos.Cumple);
    }

    [Fact]
    public async Task EvaluarSolicitud_SinDocumentos_RetornaRechazado()
    {
        // 0 docs → Peso=-5, EsCritica=true en regla Documentación → Rechazado
        var cliente = await SeedClienteAprobable(puntajeRiesgo: 8.0m, sueldo: 100_000m);
        // No seed de documentos

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 10_000m);

        Assert.Equal(ResultadoEvaluacion.Rechazado, result.Resultado);
        var reglaDoc = result.Reglas.Single(r => r.Nombre == "Documentación");
        Assert.True(reglaDoc.EsCritica);
        Assert.False(reglaDoc.Cumple);
    }

    [Fact]
    public async Task EvaluarSolicitud_HistorialNegativo_RetornaRechazado()
    {
        // Un crédito cancelado → EsCritica=true en regla Historial Crediticio → Rechazado
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Historial",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            Telefono = "123456789",
            Domicilio = "Calle 123",
            PuntajeRiesgo = 8.0m,
            Sueldo = 100_000m,
            Activo = true
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        _context.Creditos.Add(new Credito
        {
            ClienteId = cliente.Id,
            Numero = Guid.NewGuid().ToString("N")[..10],
            Estado = EstadoCredito.Cancelado,
            MontoSolicitado = 5000,
            SaldoPendiente = 2000,
            TasaInteres = 0.05m,
            CantidadCuotas = 12
        });
        await _context.SaveChangesAsync();
        await SeedDocumentosCompletos(cliente.Id);

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 10_000m);

        Assert.Equal(ResultadoEvaluacion.Rechazado, result.Resultado);
        var reglaHistorial = result.Reglas.Single(r => r.Nombre == "Historial Crediticio");
        Assert.True(reglaHistorial.EsCritica);
        Assert.False(reglaHistorial.Cumple);
    }

    [Fact]
    public async Task EvaluarSolicitud_MontoAltoSinGarante_RetornaRechazado()
    {
        // Monto=600000 >= 500000 sin garante → EsCritica=true → Rechazado
        var cliente = await SeedClienteAprobable(puntajeRiesgo: 8.0m, sueldo: 5_000_000m);
        await SeedDocumentosCompletos(cliente.Id);

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 600_000m, garanteId: null);

        Assert.Equal(ResultadoEvaluacion.Rechazado, result.Resultado);
        var reglaGarante = result.Reglas.Single(r => r.Nombre == "Garantía/Garante");
        Assert.True(reglaGarante.EsCritica);
        Assert.False(reglaGarante.Cumple);
    }

    // -------------------------------------------------------------------------
    // EvaluarSolicitudAsync — camino RequiereAnalisis
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EvaluarSolicitud_PuntajeEntreMedioYAlto_RetornaRequiereAnalisis()
    {
        // PuntajeRiesgo=6 (>=5, <7) → 20pts
        // Solo 2 docs → Peso=10pts (Cumple=true, no crítica bloqueante)
        // Sueldo=100000, monto=10000, cuota_est=1000, ratio=1% → 25pts
        // Sin historial (nuevo) → 10pts
        // Sin garante requerido → 0pts
        // Total = 20+10+25+10+0 = 65 → RequiereAnalisis (50<=65<70)
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Analisis",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            Telefono = "123456789",
            Domicilio = "Calle 123",
            PuntajeRiesgo = 6.0m,
            Sueldo = 100_000m,
            Activo = true
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        // Solo 2 docs (DNI + ReciboSueldo) → Peso=10, Cumple=true
        _context.DocumentosCliente.AddRange(
            new DocumentoCliente { ClienteId = cliente.Id, TipoDocumento = TipoDocumentoCliente.DNI, Estado = EstadoDocumento.Verificado, NombreArchivo = "dni.pdf", RutaArchivo = "/docs/dni.pdf" },
            new DocumentoCliente { ClienteId = cliente.Id, TipoDocumento = TipoDocumentoCliente.ReciboSueldo, Estado = EstadoDocumento.Verificado, NombreArchivo = "recibo.pdf", RutaArchivo = "/docs/recibo.pdf" });
        await _context.SaveChangesAsync();

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 10_000m);

        Assert.Equal(ResultadoEvaluacion.RequiereAnalisis, result.Resultado);
        Assert.True(result.PuntajeFinal >= 50 && result.PuntajeFinal < 70);
    }

    // -------------------------------------------------------------------------
    // EvaluarSolicitudAsync — campos de la evaluación
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EvaluarSolicitud_RetornaReglas_ConTodosLosNombresEsperados()
    {
        var cliente = await SeedClienteAprobable();
        await SeedDocumentosCompletos(cliente.Id);

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 10_000m);

        var nombresReglas = result.Reglas.Select(r => r.Nombre).ToList();
        Assert.Contains("Puntaje de Riesgo", nombresReglas);
        Assert.Contains("Documentación", nombresReglas);
        Assert.Contains("Capacidad de Pago", nombresReglas);
        Assert.Contains("Historial Crediticio", nombresReglas);
        Assert.Contains("Garantía/Garante", nombresReglas);
    }

    [Fact]
    public async Task EvaluarSolicitud_ClienteNuevo_HistorialOtorgaPeso10()
    {
        // Cliente sin créditos previos → "Cliente nuevo (sin historial previo)", Peso=10, Cumple=true
        var cliente = await SeedClienteAprobable(agregarCreditos: false);
        await SeedDocumentosCompletos(cliente.Id);

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 10_000m);

        var reglaHistorial = result.Reglas.Single(r => r.Nombre == "Historial Crediticio");
        Assert.True(reglaHistorial.Cumple);
        Assert.Equal(10, reglaHistorial.Peso);
    }

    [Fact]
    public async Task EvaluarSolicitud_ConDocumentosYVeraz_ReglaDocOtorga25pts()
    {
        // 3 docs + Veraz → Peso=25
        var cliente = await SeedClienteAprobable(agregarCreditos: false);
        await SeedDocumentosCompletos(cliente.Id);
        _context.DocumentosCliente.Add(new DocumentoCliente
        {
            ClienteId = cliente.Id,
            TipoDocumento = TipoDocumentoCliente.Veraz,
            Estado = EstadoDocumento.Verificado,
            NombreArchivo = "veraz.pdf",
            RutaArchivo = "/docs/veraz.pdf"
        });
        await _context.SaveChangesAsync();

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 10_000m);

        var reglaDoc = result.Reglas.Single(r => r.Nombre == "Documentación");
        Assert.Equal(25, reglaDoc.Peso);
        Assert.True(reglaDoc.Cumple);
    }

    [Fact]
    public async Task EvaluarSolicitud_DocumentoVencido_NoContaParaDocumentacion()
    {
        // DNI vencido no cuenta → solo ReciboSueldo + Servicio vigentes → 2 docs → Peso=10 (no 20+)
        var cliente = await SeedClienteAprobable(agregarCreditos: false);
        _context.DocumentosCliente.AddRange(
            new DocumentoCliente { ClienteId = cliente.Id, TipoDocumento = TipoDocumentoCliente.DNI, Estado = EstadoDocumento.Verificado, NombreArchivo = "dni.pdf", RutaArchivo = "/docs/dni.pdf", FechaVencimiento = DateTime.Today.AddDays(-1) },
            new DocumentoCliente { ClienteId = cliente.Id, TipoDocumento = TipoDocumentoCliente.ReciboSueldo, Estado = EstadoDocumento.Verificado, NombreArchivo = "recibo.pdf", RutaArchivo = "/docs/recibo.pdf" },
            new DocumentoCliente { ClienteId = cliente.Id, TipoDocumento = TipoDocumentoCliente.Servicio, Estado = EstadoDocumento.Verificado, NombreArchivo = "servicio.pdf", RutaArchivo = "/docs/servicio.pdf" });
        await _context.SaveChangesAsync();

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 10_000m);

        var reglaDoc = result.Reglas.Single(r => r.Nombre == "Documentación");
        Assert.Equal(10, reglaDoc.Peso); // 2 docs vigentes → parcial
    }

    // -------------------------------------------------------------------------
    // EvaluarSolicitudAsync — RelacionCuotaIngreso
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EvaluarSolicitud_SueldoSuficiente_SetRelacionCuotaIngreso()
    {
        // Cuota estimada = monto * 0.10 = 10000 * 0.10 = 1000; sueldo=100000; ratio=1%
        var cliente = await SeedClienteAprobable(sueldo: 100_000m);
        await SeedDocumentosCompletos(cliente.Id);

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 10_000m);

        Assert.Equal(0.01m, result.RelacionCuotaIngreso);
    }

    [Fact]
    public async Task EvaluarSolicitud_SinSueldo_RelacionCuotaIngresoEsNull()
    {
        var cliente = await SeedClienteAprobable(sueldo: null);
        await SeedDocumentosCompletos(cliente.Id);

        var result = await _service.EvaluarSolicitudAsync(cliente.Id, montoSolicitado: 10_000m);

        Assert.Null(result.RelacionCuotaIngreso);
    }
}
