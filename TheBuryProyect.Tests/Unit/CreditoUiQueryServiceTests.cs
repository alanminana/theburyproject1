using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.PagoCuota;
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

    [Fact]
    public void AgruparCreditosPorCliente_MontoMoraCapital_ExcluyeCapitalSaldadoConSoloPunitorioPendiente()
    {
        // PUN-ML10-G: cuota con capital 100% saldado (MontoPagado==MontoTotal) que quedó Parcial
        // únicamente por un punitorio aplicado pendiente (contrato EstadoCuotaResolver.Resolver) NO
        // debe contar en MontoMoraCapital — ese es el mismo hueco ya corregido en
        // ClienteScoringCalculator/ClienteAptitudService/MoraService, ahora también acá.
        var service = new CreditoUiQueryService();
        var cliente = Cliente(1, "Ana Lopez", "301");
        var cuotaCapitalSaldado = Cuota(200, 1, EstadoCuota.Parcial, DateTime.Today.AddDays(-10), montoTotal: 1000m, montoPagado: 1000m);
        var credito = Credito(20, cliente, EstadoCredito.Activo, 0m, DateTime.Today.AddDays(-30), cuotaCapitalSaldado);

        var grupos = service.AgruparCreditosPorCliente(new[] { credito });

        Assert.Equal(0m, Assert.Single(grupos).MontoMoraCapital);
    }

    [Fact]
    public void AgruparCreditosPorCliente_MontoMoraCapital_IncluyeCapitalRealmenteVencido()
    {
        var service = new CreditoUiQueryService();
        var cliente = Cliente(1, "Ana Lopez", "301");
        var cuotaVencida = Cuota(201, 1, EstadoCuota.Vencida, DateTime.Today.AddDays(-10), montoTotal: 1000m, montoPagado: 300m);
        var cuotaAlDia = Cuota(202, 2, EstadoCuota.Pendiente, DateTime.Today.AddDays(10), montoTotal: 500m);
        var credito = Credito(21, cliente, EstadoCredito.Activo, 0m, DateTime.Today.AddDays(-30), cuotaVencida, cuotaAlDia);

        var grupos = service.AgruparCreditosPorCliente(new[] { credito });

        Assert.Equal(700m, Assert.Single(grupos).MontoMoraCapital);
    }

    [Fact]
    public void AgruparCreditosPorCliente_PunitorioAplicadoPendiente_QuedaEnCeroPorDefecto()
    {
        // Sólo CreditoController.Index/PanelCliente lo pueblan vía la consulta batch autoritativa
        // (PoblarPunitorioAplicadoPendienteAsync); el servicio de agrupación puro nunca tiene acceso a DB.
        var service = new CreditoUiQueryService();
        var cliente = Cliente(1, "Ana Lopez", "301");
        var credito = Credito(22, cliente, EstadoCredito.Activo, 0m, DateTime.Today, Cuota(203, 1, EstadoCuota.Pendiente, DateTime.Today.AddDays(5), 100m));

        var grupo = Assert.Single(service.AgruparCreditosPorCliente(new[] { credito }));

        Assert.Equal(0m, grupo.MontoPunitorioAplicadoPendiente);
        Assert.Equal(0, grupo.CuotasConPunitorioAplicadoPendiente);
        Assert.False(grupo.TienePunitorioAplicadoPendiente);
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
    public async Task PagarCuota_UsaIdDeCuotaYContextoAutoritativoTipado()
    {
        var cliente = Cliente(1, "Ana Lopez", "301");
        var cuota = Cuota(9, 4, EstadoCuota.Pendiente, new DateTime(2026, 7, 20), 2500m);
        var credito = Credito(1, cliente, EstadoCredito.Activo, 2500m, DateTime.Today, cuota);
        credito.Numero = "CR-1";
        var contexto = new PagoCuotaContextoResultado(
            cuota.Id, credito.Id, cuota.NumeroCuota, credito.Numero, cliente.NombreCompleto,
            new DateOnly(2026, 7, 20), new DateOnly(2026, 8, 3), cuota.Estado, 14,
            2500m, 300m, EstadoCalculoPunitorioDetalle.Calculado, null,
            0m, 2500m, true, null, Convert.ToBase64String(new byte[8]));
        var preview = new PagoCuotaPreviewResultado(
            cuota.Id, 2500m, 0m, 2500m, 0m, 0m, 2500m, 0m, 0m,
            EstadoCuota.Pagada, new DateOnly(2026, 8, 3), contexto.CuotaRowVersionBase64);
        var creditoService = new RecordingCreditoService(new List<CreditoViewModel> { credito }, contexto, preview);
        var controller = new CreditoController(
            creditoService: creditoService,
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

        var result = await controller.PagarCuota(cuota.Id);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("PagarCuota_tw", view.ViewName);
        var model = Assert.IsType<PagarCuotaPageViewModel>(view.Model);
        Assert.Equal(cuota.Id, model.Contexto.CuotaId);
        Assert.Equal(credito.Id, model.Contexto.CreditoId);
        Assert.Equal(2500m, model.Contexto.CapitalPendiente);
        Assert.Equal(300m, model.Contexto.PunitorioCalculadoInformativo);
        Assert.Equal(2500m, model.Input.MontoIngresado);
        Assert.NotNull(model.Preview);
    }

    [Fact]
    public async Task Index_PueblaPunitorioAplicadoPendienteDeTodosLosClientesConUnaSolaConsultaBatch()
    {
        // PUN-ML10-G: regresión del bug real encontrado — Index_tw.cshtml renderiza
        // _PanelClientePartial INLINE para cada tarjeta (camino real de la UI, sin ningún caller JS
        // hacia PanelCliente); poblar el punitorio pendiente sólo en la acción PanelCliente (nunca
        // invocada por el navegador real) dejaba el panel real siempre en $0,00/sin bloque.
        var clienteA = Cliente(1, "Ana Lopez", "301");
        var clienteB = Cliente(2, "Bruno Diaz", "302");
        var cuotaA = Cuota(400, 1, EstadoCuota.Pendiente, DateTime.Today.AddDays(5), 1000m);
        var cuotaB = Cuota(401, 1, EstadoCuota.Pendiente, DateTime.Today.AddDays(5), 1000m);
        var creditoA = Credito(40, clienteA, EstadoCredito.Activo, 0m, DateTime.Today, cuotaA);
        var creditoB = Credito(41, clienteB, EstadoCredito.Activo, 0m, DateTime.Today, cuotaB);
        var creditoService = new RecordingCreditoService(new List<CreditoViewModel> { creditoA, creditoB });
        var punitorioService = new RecordingPunitorioServicePendientePorCuotas(
            new Dictionary<int, decimal> { [400] = 150m, [401] = 0m });
        var controller = new CreditoController(
            creditoService: creditoService,
            financialService: null!,
            configuracionPagoService: null!,
            configuracionMoraService: null!,
            ventaService: null!,
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            creditoUiQueryService: new CreditoUiQueryService(),
            punitorioService: punitorioService);

        var result = await controller.Index(new CreditoFilterViewModel());

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CreditoIndexViewModel>(view.Model);
        var grupoA = model.Clientes.Single(g => g.Cliente.Id == 1);
        var grupoB = model.Clientes.Single(g => g.Cliente.Id == 2);
        Assert.Equal(150m, grupoA.MontoPunitorioAplicadoPendiente);
        Assert.True(grupoA.TienePunitorioAplicadoPendiente);
        Assert.Equal(0m, grupoB.MontoPunitorioAplicadoPendiente);
        Assert.False(grupoB.TienePunitorioAplicadoPendiente);
        Assert.Equal(1, punitorioService.Calls); // una sola consulta batch para los 2 clientes, no 2
    }

    [Fact]
    public async Task PanelCliente_PueblaPunitorioAplicadoPendienteConUnaSolaConsultaBatch()
    {
        // PUN-ML10-G
        var cliente = Cliente(1, "Ana Lopez", "301");
        var cuotaConPunitorio = Cuota(300, 1, EstadoCuota.Pendiente, DateTime.Today.AddDays(5), 1000m);
        var cuotaSinPunitorio = Cuota(301, 2, EstadoCuota.Pendiente, DateTime.Today.AddDays(35), 1000m);
        var credito = Credito(30, cliente, EstadoCredito.Activo, 0m, DateTime.Today, cuotaConPunitorio, cuotaSinPunitorio);
        var creditoService = new RecordingCreditoService(new List<CreditoViewModel> { credito });
        var punitorioService = new RecordingPunitorioServicePendientePorCuotas(
            new Dictionary<int, decimal> { [300] = 250m, [301] = 0m });
        var controller = new CreditoController(
            creditoService: creditoService,
            financialService: null!,
            configuracionPagoService: null!,
            configuracionMoraService: null!,
            ventaService: null!,
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            creditoUiQueryService: new CreditoUiQueryService(),
            punitorioService: punitorioService);

        var result = await controller.PanelCliente(cliente.Id);

        var partial = Assert.IsType<PartialViewResult>(result);
        var model = Assert.IsType<CreditoClienteIndexViewModel>(partial.Model);
        Assert.Equal(250m, model.MontoPunitorioAplicadoPendiente);
        Assert.Equal(1, model.CuotasConPunitorioAplicadoPendiente);
        Assert.True(model.TienePunitorioAplicadoPendiente);
        Assert.Equal(1, punitorioService.Calls);
        Assert.Equal(new[] { 300, 301 }, punitorioService.LastCuotaIds.OrderBy(x => x));
    }

    [Fact]
    public async Task PanelCliente_SinPunitorioService_RenderizaConPunitorioEnCero()
    {
        // Degradacion segura: el panel no debe romper si IPunitorioService no esta disponible —
        // la mora de capital (lo esencial) igual se renderiza correcta.
        var cliente = Cliente(1, "Ana Lopez", "301");
        var cuota = Cuota(310, 1, EstadoCuota.Vencida, DateTime.Today.AddDays(-5), montoTotal: 1000m, montoPagado: 200m);
        var credito = Credito(31, cliente, EstadoCredito.Activo, 0m, DateTime.Today, cuota);
        var creditoService = new RecordingCreditoService(new List<CreditoViewModel> { credito });
        var controller = new CreditoController(
            creditoService: creditoService,
            financialService: null!,
            configuracionPagoService: null!,
            configuracionMoraService: null!,
            ventaService: null!,
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            creditoUiQueryService: new CreditoUiQueryService(),
            punitorioService: null);

        var result = await controller.PanelCliente(cliente.Id);

        var partial = Assert.IsType<PartialViewResult>(result);
        var model = Assert.IsType<CreditoClienteIndexViewModel>(partial.Model);
        Assert.Equal(0m, model.MontoPunitorioAplicadoPendiente);
        Assert.False(model.TienePunitorioAplicadoPendiente);
        Assert.Equal(800m, model.MontoMoraCapital);
    }

    private sealed class RecordingPunitorioServicePendientePorCuotas : IPunitorioService
    {
        private readonly IReadOnlyDictionary<int, decimal> _resultado;

        public RecordingPunitorioServicePendientePorCuotas(IReadOnlyDictionary<int, decimal> resultado) => _resultado = resultado;

        public int Calls { get; private set; }
        public IReadOnlyCollection<int> LastCuotaIds { get; private set; } = Array.Empty<int>();

        public Task<IReadOnlyDictionary<int, decimal>> ObtenerPunitorioAplicadoPendientePorCuotasAsync(
            IEnumerable<int> cuotaIds,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastCuotaIds = cuotaIds.ToList();
            return Task.FromResult(_resultado);
        }

        public Task<PunitorioConsultaResultado> CalcularCuotaAsync(
            int cuotaId, DateOnly? fechaCalculo = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<PunitorioCuotaDetalleResultado> ObtenerDetalleCuotaAsync(
            int cuotaId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<decimal> ObtenerPunitorioAplicadoPendienteAsync(
            int cuotaId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<PunitorioAplicadoProgreso?> ObtenerAplicacionActivaConProgresoAsync(
            int cuotaId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<PunitorioAplicado> AplicarAsync(
            int cuotaId, PunitorioAplicarComando comando, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<PunitorioAplicado> AnularAsync(
            int punitorioAplicadoId, PunitorioAnularComando comando, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
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
        private readonly PagoCuotaContextoResultado? _contextoPago;
        private readonly PagoCuotaPreviewResultado? _previewPago;

        public RecordingCreditoService(
            List<CreditoViewModel> creditos,
            PagoCuotaContextoResultado? contextoPago = null,
            PagoCuotaPreviewResultado? previewPago = null)
        {
            _creditos = creditos;
            _contextoPago = contextoPago;
            _previewPago = previewPago;
        }

        public Task<List<CreditoViewModel>> GetAllAsync(CreditoFilterViewModel? filter = null) => Task.FromResult(_creditos);
        public Task<CreditoViewModel?> GetByIdAsync(int id) => Task.FromResult(_creditos.FirstOrDefault(c => c.Id == id));
        public Task<List<CreditoViewModel>> GetByClienteIdAsync(int clienteId) =>
            Task.FromResult(_creditos.Where(c => c.Cliente.Id == clienteId).ToList());
        public Task<CreditoViewModel> CreateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<CreditoViewModel> CreatePendienteConfiguracionAsync(int clienteId, decimal montoTotal) => throw new NotImplementedException();
        public Task<bool> UpdateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<bool> AprobarCreditoAsync(int creditoId, string aprobadoPor) => throw new NotImplementedException();
        public Task<bool> RechazarCreditoAsync(int creditoId, string motivo) => throw new NotImplementedException();
        public Task<bool> CancelarCreditoAsync(int creditoId, string motivo) => throw new NotImplementedException();
        public Task<List<CuotaViewModel>> GetCuotasByCreditoAsync(int creditoId) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetCuotaByIdAsync(int cuotaId) => throw new NotImplementedException();
        public Task<bool> PagarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
        public Task<PagoCuotaContextoResultado?> ObtenerContextoPagoCuotaAsync(int cuotaId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_contextoPago?.CuotaId == cuotaId ? _contextoPago : null);
        public Task<PagoCuotaPreviewResultado?> PrevisualizarPagoCuotaAsync(PagoCuotaIndividualComando comando, CancellationToken cancellationToken = default) =>
            Task.FromResult(_previewPago?.CuotaId == comando.CuotaId ? _previewPago : null);
        public Task<PagoCuotaResultado?> RegistrarPagoCuotaIndividualAsync(PagoCuotaIndividualComando comando, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoMultipleCuotasResult> PagarCuotasAsync(PagoMultipleCuotasRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TheBuryProject.Services.Models.CobroPrimeraCuotaResultado> CobrarPrimeraCuotaAlGenerarAsync(int creditoId, string medioPago, string? comprobante = null, string? observaciones = null) => throw new NotImplementedException();
        public Task<bool> AdelantarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
        public Task<PagoCuotaContextoResultado?> ObtenerContextoAdelantoAsync(int creditoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoCuotaPreviewResultado?> PrevisualizarAdelantoAsync(AdelantoCuotaComando comando, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoCuotaResultado?> RegistrarAdelantoAsync(AdelantoCuotaComando comando, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoMultiplePreviewResultado> PrevisualizarPagoMultipleAsync(int clienteId, List<int> cuotaIds, string medioPago, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetPrimeraCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetUltimaCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
        public Task<List<CuotaViewModel>> GetCuotasVencidasAsync() => throw new NotImplementedException();
        public Task ActualizarEstadoCuotasAsync() => throw new NotImplementedException();
        public Task<bool> RecalcularSaldoCreditoAsync(int creditoId) => throw new NotImplementedException();
        public Task ConfigurarCreditoAsync(ConfiguracionCreditoComando comando) => throw new NotImplementedException();
    }
}
