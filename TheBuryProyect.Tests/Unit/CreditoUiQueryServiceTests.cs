using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Tests.Unit;

public class CreditoUiQueryServiceTests
{
    [Fact]
    public void AgruparCreditosPorCliente_ConservaCantidadTotalesYOrden()
    {
        var service = new CreditoUiQueryService();
        var clienteA = Cliente(1, "Ana Lopez", "301");
        var clienteB = Cliente(2, "Bruno Diaz", "302");
        var proximoA = DateTime.Today.AddDays(5);
        var proximoB = DateTime.Today.AddDays(10);

        var creditos = new[]
        {
            Credito(10, clienteB, EstadoCredito.Activo, 100m, DateTime.Today.AddDays(-5), Cuota(100, 1, EstadoCuota.Pendiente, proximoB, 100m)),
            Credito(11, clienteA, EstadoCredito.Activo, 200m, DateTime.Today.AddDays(-10), Cuota(101, 1, EstadoCuota.Pendiente, proximoA, 100m)),
            Credito(12, clienteA, EstadoCredito.Generado, 300m, DateTime.Today.AddDays(-1), Cuota(102, 2, EstadoCuota.Pagada, proximoA.AddDays(30), 100m))
        };

        var grupos = service.AgruparCreditosPorCliente(creditos);

        Assert.Equal(2, grupos.Count);
        Assert.Equal("Ana Lopez", grupos[0].Cliente.NombreCompleto);
        Assert.Equal(2, grupos[0].CantidadCreditos);
        Assert.Equal(500m, grupos[0].SaldoPendienteTotal);
        Assert.Equal(proximoA, grupos[0].ProximoVencimiento);
        Assert.Equal(new[] { 12, 11 }, grupos[0].Creditos.Select(c => c.Id));
    }

    [Theory]
    [InlineData(1, EstadoCredito.Finalizado, EstadoCredito.Activo, "En mora")]
    [InlineData(0, EstadoCredito.Activo, EstadoCredito.Solicitado, "Activo")]
    [InlineData(0, EstadoCredito.Solicitado, EstadoCredito.Aprobado, "Pendiente")]
    [InlineData(0, EstadoCredito.Aprobado, EstadoCredito.Finalizado, "Aprobado")]
    [InlineData(0, EstadoCredito.Finalizado, EstadoCredito.Finalizado, "Finalizado")]
    [InlineData(0, EstadoCredito.Rechazado, EstadoCredito.Rechazado, "Rechazado")]
    [InlineData(0, EstadoCredito.Cancelado, EstadoCredito.Cancelado, "Cancelado")]
    [InlineData(0, EstadoCredito.Finalizado, EstadoCredito.Rechazado, "Mixto")]
    public void ResolverEstadoConsolidado_ConservaPrioridadActual(
        int cuotasVencidas,
        EstadoCredito estado1,
        EstadoCredito estado2,
        string esperado)
    {
        var service = new CreditoUiQueryService();
        var cliente = Cliente(1, "Ana Lopez", "301");
        var creditos = new[]
        {
            Credito(1, cliente, estado1, 0m, DateTime.Today),
            Credito(2, cliente, estado2, 0m, DateTime.Today)
        };

        var estado = service.ResolverEstadoConsolidado(creditos, cuotasVencidas);

        Assert.Equal(esperado, estado);
    }

    [Fact]
    public void ObtenerCuotasPendientes_ConservaFiltradoYOrdenPorNumeroCuota()
    {
        var service = new CreditoUiQueryService();
        var cuotas = new[]
        {
            Cuota(1, 3, EstadoCuota.Parcial, DateTime.Today.AddDays(3), 300m),
            Cuota(2, 1, EstadoCuota.Pagada, DateTime.Today.AddDays(1), 100m),
            Cuota(3, 2, EstadoCuota.Vencida, DateTime.Today.AddDays(-1), 200m),
            Cuota(4, 1, EstadoCuota.Pendiente, DateTime.Today.AddDays(1), 100m),
            Cuota(5, 4, EstadoCuota.Cancelada, DateTime.Today.AddDays(4), 400m)
        };

        var pendientes = service.ObtenerCuotasPendientes(cuotas);

        Assert.Equal(new[] { 4, 3, 1 }, pendientes.Select(c => c.Id));
    }

    [Fact]
    public void BuildCuotasJson_ConservaNombresPropiedadesYValores()
    {
        var service = new CreditoUiQueryService();
        var fecha = new DateTime(2026, 6, 15);
        var cuotas = new[]
        {
            Cuota(7, 2, EstadoCuota.Pendiente, fecha, 1500m, montoPagado: 200m, punitorio: 75m),
            Cuota(8, 1, EstadoCuota.Pagada, fecha, 999m)
        };

        using var json = JsonDocument.Parse(service.BuildCuotasJson(cuotas));
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("7", out var cuota));
        Assert.False(root.TryGetProperty("8", out _));
        Assert.Equal(1375m, cuota.GetProperty("saldo").GetDecimal());
        Assert.Equal(1500m, cuota.GetProperty("montoCuota").GetDecimal());
        Assert.Equal(75m, cuota.GetProperty("punitorio").GetDecimal());
        Assert.Equal(2, cuota.GetProperty("numeroCuota").GetInt32());
        Assert.Equal("15/06/2026", cuota.GetProperty("vencimiento").GetString());
        Assert.True(cuota.TryGetProperty("estaVencida", out _));
        Assert.True(cuota.TryGetProperty("diasAtraso", out _));
    }

    [Fact]
    public void ProyectarCuotasPendientes_ConservaValueYTextoBase()
    {
        var service = new CreditoUiQueryService();
        var cuotas = new[]
        {
            Cuota(9, 4, EstadoCuota.Pendiente, new DateTime(2026, 7, 20), 2500m)
        };

        var items = service.ProyectarCuotasPendientes(cuotas);

        var item = Assert.Single(items);
        Assert.Equal("9", item.Value);
        Assert.Contains("Cuota #4 - Vto: 20/07/2026 - ", item.Text);
    }

    [Fact]
    public async Task Index_ConservaVistaYModeloAgrupadoDesdeServicioUi()
    {
        var cliente = Cliente(1, "Ana Lopez", "301");
        var creditos = new List<CreditoViewModel>
        {
            Credito(1, cliente, EstadoCredito.Activo, 100m, DateTime.Today)
        };
        var creditoService = new RecordingCreditoService(creditos);
        var uiService = new RecordingCreditoUiQueryService();
        var controller = new CreditoController(
            creditoService: creditoService,
            evaluacionService: null!,
            financialService: null!,
            configuracionPagoService: null!,
            configuracionMoraService: null!,
            ventaService: null!,
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            creditoUiQueryService: uiService);

        var filter = new CreditoFilterViewModel { ClienteId = 1 };
        var result = await controller.Index(filter);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index_tw", view.ViewName);
        Assert.Same(creditos, uiService.ReceivedCreditos);
        var model = Assert.IsType<CreditoIndexViewModel>(view.Model);
        Assert.Same(filter, model.Filter);
        var grupo = Assert.Single(model.Clientes);
        Assert.Equal("Ana Lopez", grupo.Cliente.NombreCompleto);
    }

    [Fact]
    public async Task PagarCuota_ConservaCuotasYJsonEnViewModelTipado()
    {
        var cliente = Cliente(1, "Ana Lopez", "301");
        var cuota = Cuota(9, 4, EstadoCuota.Pendiente, new DateTime(2026, 7, 20), 2500m);
        var credito = Credito(1, cliente, EstadoCredito.Activo, 2500m, DateTime.Today, cuota);
        credito.Numero = "CR-1";
        var controller = new CreditoController(
            creditoService: new RecordingCreditoService(new List<CreditoViewModel> { credito }),
            evaluacionService: null!,
            financialService: null!,
            configuracionPagoService: null!,
            configuracionMoraService: null!,
            ventaService: null!,
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            creditoUiQueryService: new CreditoUiQueryService());

        var result = await controller.PagarCuota(credito.Id, cuota.Id);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("PagarCuota_tw", view.ViewName);
        var model = Assert.IsType<PagarCuotaViewModel>(view.Model);
        var item = Assert.Single(model.Cuotas);
        Assert.Equal("9", item.Value);
        using var json = JsonDocument.Parse(model.CuotasJson);
        Assert.True(json.RootElement.TryGetProperty("9", out var cuotaJson));
        Assert.Equal(4, cuotaJson.GetProperty("numeroCuota").GetInt32());
    }

    private static ClienteResumenViewModel Cliente(int id, string nombre, string documento) =>
        new()
        {
            Id = id,
            NombreCompleto = nombre,
            NumeroDocumento = documento
        };

    private static CreditoViewModel Credito(
        int id,
        ClienteResumenViewModel cliente,
        EstadoCredito estado,
        decimal saldoPendiente,
        DateTime fechaSolicitud,
        params CuotaViewModel[] cuotas) =>
        new()
        {
            Id = id,
            Cliente = cliente,
            Estado = estado,
            SaldoPendiente = saldoPendiente,
            FechaSolicitud = fechaSolicitud,
            Cuotas = cuotas.ToList()
        };

    private static CuotaViewModel Cuota(
        int id,
        int numero,
        EstadoCuota estado,
        DateTime fechaVencimiento,
        decimal montoTotal,
        decimal montoPagado = 0m,
        decimal punitorio = 0m) =>
        new()
        {
            Id = id,
            NumeroCuota = numero,
            Estado = estado,
            FechaVencimiento = fechaVencimiento,
            MontoTotal = montoTotal,
            MontoPagado = montoPagado,
            MontoPunitorio = punitorio
        };

    private sealed class RecordingCreditoUiQueryService : CreditoUiQueryService
    {
        public IEnumerable<CreditoViewModel>? ReceivedCreditos { get; private set; }

        public override List<CreditoClienteIndexViewModel> AgruparCreditosPorCliente(IEnumerable<CreditoViewModel> creditos)
        {
            ReceivedCreditos = creditos;
            return base.AgruparCreditosPorCliente(creditos);
        }
    }

    private sealed class RecordingCreditoService : ICreditoService
    {
        private readonly List<CreditoViewModel> _creditos;

        public RecordingCreditoService(List<CreditoViewModel> creditos)
        {
            _creditos = creditos;
        }

        public Task<List<CreditoViewModel>> GetAllAsync(CreditoFilterViewModel? filter = null) => Task.FromResult(_creditos);
        public Task<CreditoViewModel?> GetByIdAsync(int id) => Task.FromResult(_creditos.FirstOrDefault(c => c.Id == id));
        public Task<List<CreditoViewModel>> GetByClienteIdAsync(int clienteId) => throw new NotImplementedException();
        public Task<CreditoViewModel> CreateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<CreditoViewModel> CreatePendienteConfiguracionAsync(int clienteId, decimal montoTotal) => throw new NotImplementedException();
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
        public Task<PagoMultipleCuotasResult> PagarCuotasAsync(PagoMultipleCuotasRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> AdelantarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetPrimeraCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetUltimaCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
        public Task<List<CuotaViewModel>> GetCuotasVencidasAsync() => throw new NotImplementedException();
        public Task ActualizarEstadoCuotasAsync() => throw new NotImplementedException();
        public Task<bool> RecalcularSaldoCreditoAsync(int creditoId) => throw new NotImplementedException();
        public Task ConfigurarCreditoAsync(ConfiguracionCreditoComando comando) => throw new NotImplementedException();
    }
}
