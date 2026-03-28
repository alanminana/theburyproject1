using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;
using TheBuryProject.ViewModels.Responses;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs para DocumentacionService
// ---------------------------------------------------------------------------

file sealed class StubVentaServiceDoc : IVentaService
{
    private readonly VentaViewModel? _venta;
    public bool AsociarCreditoLlamado { get; private set; }

    public StubVentaServiceDoc(VentaViewModel? venta) => _venta = venta;

    public Task<VentaViewModel?> GetByIdAsync(int id)
        => Task.FromResult(_venta);

    public Task AsociarCreditoAVentaAsync(int ventaId, int creditoId)
    {
        AsociarCreditoLlamado = true;
        return Task.CompletedTask;
    }

    // Resto sin usar
    public Task<List<VentaViewModel>> GetAllAsync(VentaFilterViewModel? filter = null) => throw new NotImplementedException();
    public Task<VentaViewModel> CreateAsync(VentaViewModel viewModel) => throw new NotImplementedException();
    public Task<VentaViewModel?> UpdateAsync(int id, VentaViewModel viewModel) => throw new NotImplementedException();
    public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
    public Task<bool> ConfirmarVentaAsync(int id) => throw new NotImplementedException();
    public Task<bool> ConfirmarVentaCreditoAsync(int id) => throw new NotImplementedException();
    public Task<bool> CancelarVentaAsync(int id, string motivo) => throw new NotImplementedException();
    public Task<bool> FacturarVentaAsync(int id, FacturaViewModel facturaViewModel) => throw new NotImplementedException();
    public Task<int?> AnularFacturaAsync(int facturaId, string motivo) => throw new NotImplementedException();
    public Task<bool> ValidarStockAsync(int ventaId) => throw new NotImplementedException();
    public Task<bool> SolicitarAutorizacionAsync(int id, string usuarioSolicita, string motivo) => throw new NotImplementedException();
    public Task<bool> AutorizarVentaAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
    public Task<bool> RechazarVentaAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
    public Task<bool> RegistrarExcepcionDocumentalAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
    public Task<bool> RequiereAutorizacionAsync(VentaViewModel viewModel) => throw new NotImplementedException();
    public Task<bool> GuardarDatosTarjetaAsync(int ventaId, DatosTarjetaViewModel datosTarjeta) => throw new NotImplementedException();
    public Task<bool> GuardarDatosChequeAsync(int ventaId, DatosChequeViewModel datosCheque) => throw new NotImplementedException();
    public Task<DatosTarjetaViewModel> CalcularCuotasTarjetaAsync(int tarjetaId, decimal monto, int cuotas) => throw new NotImplementedException();
    public Task<DatosCreditoPersonallViewModel> CalcularCreditoPersonallAsync(int creditoId, decimal montoAFinanciar, int cuotas, DateTime fechaPrimeraCuota) => throw new NotImplementedException();
    public Task<DatosCreditoPersonallViewModel?> ObtenerDatosCreditoVentaAsync(int ventaId) => throw new NotImplementedException();
    public Task<bool> ValidarDisponibilidadCreditoAsync(int creditoId, decimal monto) => throw new NotImplementedException();
    public CalculoTotalesVentaResponse CalcularTotalesPreview(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
    public Task<decimal?> GetTotalVentaAsync(int ventaId) => throw new NotImplementedException();
}

file sealed class StubCreditoServiceDoc : ICreditoService
{
    private readonly CreditoViewModel _creditoCreado;

    public StubCreditoServiceDoc(CreditoViewModel creditoCreado) => _creditoCreado = creditoCreado;

    public Task<CreditoViewModel> CreatePendienteConfiguracionAsync(int clienteId, decimal montoTotal)
        => Task.FromResult(_creditoCreado);

    // Resto sin usar
    public Task<List<CreditoViewModel>> GetAllAsync(CreditoFilterViewModel? filter = null) => throw new NotImplementedException();
    public Task<CreditoViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CreditoViewModel>> GetByClienteIdAsync(int clienteId) => throw new NotImplementedException();
    public Task<CreditoViewModel> CreateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
    public Task<bool> UpdateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
    public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
    public Task<SimularCreditoViewModel> SimularCreditoAsync(SimularCreditoViewModel modelo) => throw new NotImplementedException();
    public Task<bool> AprobarCreditoAsync(int creditoId, string aprobadoPor) => throw new NotImplementedException();
    public Task<bool> RechazarCreditoAsync(int creditoId, string motivo) => throw new NotImplementedException();
    public Task<bool> CancelarCreditoAsync(int creditoId, string motivo) => throw new NotImplementedException();
    public Task<(bool Success, string? NumeroCredito, string? ErrorMessage)> SolicitarCreditoAsync(SolicitudCreditoViewModel solicitud, string usuarioSolicitante, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<List<CuotaViewModel>> GetCuotasByCreditoAsync(int creditoId) => throw new NotImplementedException();
    public Task<CuotaViewModel?> GetCuotaByIdAsync(int cuotaId) => throw new NotImplementedException();
    public Task<bool> PagarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
    public Task<bool> AdelantarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
    public Task<CuotaViewModel?> GetPrimeraCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
    public Task<CuotaViewModel?> GetUltimaCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
    public Task<List<CuotaViewModel>> GetCuotasVencidasAsync() => throw new NotImplementedException();
    public Task ActualizarEstadoCuotasAsync() => throw new NotImplementedException();
    public Task<bool> RecalcularSaldoCreditoAsync(int creditoId) => throw new NotImplementedException();
    public Task ConfigurarCreditoAsync(ConfiguracionCreditoComando comando) => throw new NotImplementedException();
}

file sealed class StubDocumentoClienteServiceDoc : IDocumentoClienteService
{
    private readonly DocumentacionClienteEstadoViewModel _estado;

    public StubDocumentoClienteServiceDoc(DocumentacionClienteEstadoViewModel estado) => _estado = estado;

    public Task<DocumentacionClienteEstadoViewModel> ValidarDocumentacionObligatoriaAsync(
        int clienteId, IEnumerable<TipoDocumentoCliente>? requeridos = null)
        => Task.FromResult(_estado);

    // Resto sin usar
    public Task<List<DocumentoClienteViewModel>> GetAllAsync() => throw new NotImplementedException();
    public Task<DocumentoClienteViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<List<DocumentoClienteViewModel>> GetByClienteIdAsync(int clienteId) => throw new NotImplementedException();
    public Task<DocumentoClienteViewModel> UploadAsync(DocumentoClienteViewModel viewModel) => throw new NotImplementedException();
    public Task<bool> VerificarAsync(int id, string verificadoPor, string? observaciones = null) => throw new NotImplementedException();
    public Task<bool> RechazarAsync(int id, string motivo, string rechazadoPor) => throw new NotImplementedException();
    public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
    public Task<byte[]> DescargarArchivoAsync(int id) => throw new NotImplementedException();
    public Task<(List<DocumentoClienteViewModel> Documentos, int Total)> BuscarAsync(DocumentoClienteFilterViewModel filtro) => throw new NotImplementedException();
    public Task<int> VerificarTodosAsync(int clienteId, string verificadoPor, string? observaciones = null) => throw new NotImplementedException();
    public Task MarcarVencidosAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<BatchOperacionResultado> VerificarBatchAsync(IEnumerable<int> ids, string verificadoPor, string? observaciones = null) => throw new NotImplementedException();
    public Task<BatchOperacionResultado> RechazarBatchAsync(IEnumerable<int> ids, string motivo, string rechazadoPor) => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------

/// <summary>
/// Tests unitarios para DocumentacionService.ProcesarDocumentacionVentaAsync.
///
/// No necesitan DB — toda la lógica está orquestada via interfaces (stubs).
///
/// Contratos verificados:
/// - Venta inexistente → throws InvalidOperationException
/// - Documentación incompleta → resultado con DocumentacionCompleta=false, sin crédito
/// - Documentación completa, sin crédito previo, crearCreditoSiCompleta=true → crea crédito, llama AsociarCredito, CreditoCreado=true
/// - Documentación completa, con crédito previo → no crea nuevo crédito, CreditoCreado=false
/// - Documentación completa, crearCreditoSiCompleta=false → no crea crédito
/// - Resultado siempre incluye ClienteId y VentaId correctos
/// </summary>
public class DocumentacionServiceTests
{
    private static VentaViewModel BuildVenta(int id = 1, int clienteId = 10, int? creditoId = null)
        => new() { Id = id, ClienteId = clienteId, Total = 5_000m, CreditoId = creditoId };

    private static DocumentacionClienteEstadoViewModel EstadoCompleto()
        => new() { Completa = true, Faltantes = [] };

    private static DocumentacionClienteEstadoViewModel EstadoIncompleto()
        => new()
        {
            Completa = false,
            Faltantes = [TipoDocumentoCliente.DNI, TipoDocumentoCliente.ReciboSueldo]
        };

    private static DocumentacionService BuildService(
        VentaViewModel? venta,
        DocumentacionClienteEstadoViewModel estado,
        CreditoViewModel? creditoCreado = null)
    {
        var ventaStub = new StubVentaServiceDoc(venta);
        var docStub = new StubDocumentoClienteServiceDoc(estado);
        var creditoStub = new StubCreditoServiceDoc(creditoCreado ?? new CreditoViewModel { Id = 99 });

        return new DocumentacionService(
            docStub,
            creditoStub,
            ventaStub,
            NullLogger<DocumentacionService>.Instance);
    }

    // -------------------------------------------------------------------------
    // Tests — guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcesarDocumentacion_VentaInexistente_LanzaInvalidOperationException()
    {
        var service = BuildService(venta: null, estado: EstadoCompleto());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProcesarDocumentacionVentaAsync(ventaId: 1));
    }

    // -------------------------------------------------------------------------
    // Tests — documentación incompleta
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcesarDocumentacion_Incompleta_RetornaDocumentacionCompletaFalse()
    {
        var venta = BuildVenta();
        var service = BuildService(venta, EstadoIncompleto());

        var resultado = await service.ProcesarDocumentacionVentaAsync(venta.Id);

        Assert.False(resultado.DocumentacionCompleta);
    }

    [Fact]
    public async Task ProcesarDocumentacion_Incompleta_NoAsignaCreditoId()
    {
        var venta = BuildVenta();
        var service = BuildService(venta, EstadoIncompleto());

        var resultado = await service.ProcesarDocumentacionVentaAsync(venta.Id);

        Assert.Null(resultado.CreditoId);
        Assert.False(resultado.CreditoCreado);
    }

    [Fact]
    public async Task ProcesarDocumentacion_Incompleta_MensajeFaltantesNoVacio()
    {
        var venta = BuildVenta();
        var service = BuildService(venta, EstadoIncompleto());

        var resultado = await service.ProcesarDocumentacionVentaAsync(venta.Id);

        Assert.False(string.IsNullOrWhiteSpace(resultado.MensajeFaltantes));
    }

    // -------------------------------------------------------------------------
    // Tests — documentación completa, sin crédito previo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcesarDocumentacion_Completa_SinCreditoPrevio_CreaCredito()
    {
        var venta = BuildVenta(creditoId: null);
        var creditoNuevo = new CreditoViewModel { Id = 42 };
        var service = BuildService(venta, EstadoCompleto(), creditoNuevo);

        var resultado = await service.ProcesarDocumentacionVentaAsync(venta.Id);

        Assert.True(resultado.DocumentacionCompleta);
        Assert.Equal(42, resultado.CreditoId);
        Assert.True(resultado.CreditoCreado);
    }

    [Fact]
    public async Task ProcesarDocumentacion_Completa_SinCreditoPrevio_LlamaAsociarCredito()
    {
        var venta = BuildVenta(creditoId: null);
        var ventaStub = new StubVentaServiceDoc(venta);
        var service = new DocumentacionService(
            new StubDocumentoClienteServiceDoc(EstadoCompleto()),
            new StubCreditoServiceDoc(new CreditoViewModel { Id = 7 }),
            ventaStub,
            NullLogger<DocumentacionService>.Instance);

        await service.ProcesarDocumentacionVentaAsync(venta.Id);

        Assert.True(ventaStub.AsociarCreditoLlamado);
    }

    // -------------------------------------------------------------------------
    // Tests — documentación completa, con crédito previo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcesarDocumentacion_Completa_ConCreditoPrevio_NoCreaCredito()
    {
        var venta = BuildVenta(creditoId: 15); // ya tiene crédito
        var service = BuildService(venta, EstadoCompleto());

        var resultado = await service.ProcesarDocumentacionVentaAsync(venta.Id);

        Assert.True(resultado.DocumentacionCompleta);
        Assert.Equal(15, resultado.CreditoId);
        Assert.False(resultado.CreditoCreado);
    }

    [Fact]
    public async Task ProcesarDocumentacion_Completa_ConCreditoPrevio_NoLlamaAsociarCredito()
    {
        var venta = BuildVenta(creditoId: 15);
        var ventaStub = new StubVentaServiceDoc(venta);
        var service = new DocumentacionService(
            new StubDocumentoClienteServiceDoc(EstadoCompleto()),
            new StubCreditoServiceDoc(new CreditoViewModel { Id = 99 }),
            ventaStub,
            NullLogger<DocumentacionService>.Instance);

        await service.ProcesarDocumentacionVentaAsync(venta.Id);

        Assert.False(ventaStub.AsociarCreditoLlamado);
    }

    // -------------------------------------------------------------------------
    // Tests — flag crearCreditoSiCompleta=false
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcesarDocumentacion_Completa_CrearCreditoFalse_NoCreaCredito()
    {
        var venta = BuildVenta(creditoId: null);
        var service = BuildService(venta, EstadoCompleto());

        var resultado = await service.ProcesarDocumentacionVentaAsync(venta.Id, crearCreditoSiCompleta: false);

        Assert.True(resultado.DocumentacionCompleta);
        Assert.Null(resultado.CreditoId);
        Assert.False(resultado.CreditoCreado);
    }

    // -------------------------------------------------------------------------
    // Tests — campos del resultado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcesarDocumentacion_ResultadoIncluye_ClienteIdYVentaId()
    {
        var venta = BuildVenta(id: 7, clienteId: 33);
        var service = BuildService(venta, EstadoCompleto(), new CreditoViewModel { Id = 1 });

        var resultado = await service.ProcesarDocumentacionVentaAsync(venta.Id);

        Assert.Equal(33, resultado.ClienteId);
        Assert.Equal(7, resultado.VentaId);
    }
}
