using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels.Punitorio;

namespace TheBuryProject.Tests.Unit;

public class CreditoPunitorioDetalleControllerTests
{
    [Fact]
    public void Endpoint_HeredaAutenticacionYPermisoCreditosView()
    {
        var authorize = typeof(CreditoController).GetCustomAttributes<AuthorizeAttribute>().ToList();
        var permiso = Assert.Single(typeof(CreditoController).GetCustomAttributes<PermisoRequeridoAttribute>());

        Assert.Contains(authorize, attribute => attribute.GetType() == typeof(AuthorizeAttribute));
        Assert.Equal("creditos", permiso.Modulo);
        Assert.Equal("view", permiso.Accion);

        var endpoint = typeof(CreditoController).GetMethod(nameof(CreditoController.DetallePunitorioCuota));
        Assert.NotNull(endpoint?.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public async Task Endpoint_DevuelvePartialTipado_UsaContratoB1YPropagaCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var service = new RecordingPunitorioService(Detalle());
        var controller = Controller(service);

        var result = await controller.DetallePunitorioCuota(creditoId: 7, cuotaId: 11, cts.Token);

        var partial = Assert.IsType<PartialViewResult>(result);
        Assert.Equal("_PunitorioCuotaDetallePartial", partial.ViewName);
        var model = Assert.IsType<CuotaPunitorioDetalleViewModel>(partial.Model);
        Assert.Equal(80m, model.Resumen.CapitalPendiente);
        Assert.Equal(35m, model.Resumen.PunitorioCalculadoHoy);
        Assert.Equal(15m, model.Resumen.PunitorioAplicadoPendiente);
        Assert.Equal(95m, model.Resumen.TotalCobrableActual);
        Assert.Equal(1, service.Calls);
        Assert.Equal(11, service.LastCuotaId);
        Assert.Equal(cts.Token, service.LastCancellationToken);
    }

    [Fact]
    public async Task Endpoint_CuotaDeOtroCredito_Devuelve404()
    {
        var service = new RecordingPunitorioService(Detalle(creditoId: 8));
        var controller = Controller(service);

        var result = await controller.DetallePunitorioCuota(creditoId: 7, cuotaId: 11, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Endpoint_CuotaInexistente_Devuelve404()
    {
        var service = new RecordingPunitorioService(new KeyNotFoundException("No existe"));
        var controller = Controller(service);

        var result = await controller.DetallePunitorioCuota(creditoId: 7, cuotaId: 404, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Endpoint_HistorialIncompleto_Conserva200YNullsDesconocidos()
    {
        var aplicacion = new PunitorioAplicacionDetalle
        {
            PunitorioAplicadoId = 3,
            Estado = EstadoPunitorioAplicado.Aplicado,
            ImporteTeorico = null,
            ImportePreviamenteAplicado = null,
            ImporteNuevoAplicado = 15m,
            ImporteAplicado = 15m,
            ImportePagado = null,
            ImportePendiente = null,
            FechaCalculo = new DateOnly(2026, 8, 3),
            FechaAplicacion = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc),
            MotivoAplicacion = "Aplicación histórica",
            UsuarioAplicacion = "qa",
            EsActiva = true,
            RowVersionBase64 = "AQ=="
        };
        var detalle = Detalle(
            historialCompleto: false,
            aplicadoPendiente: null,
            aplicaciones: [aplicacion],
            aplicacionActiva: aplicacion);
        var controller = Controller(new RecordingPunitorioService(detalle));

        var result = await controller.DetallePunitorioCuota(7, 11, CancellationToken.None);

        var partial = Assert.IsType<PartialViewResult>(result);
        var model = Assert.IsType<CuotaPunitorioDetalleViewModel>(partial.Model);
        Assert.False(model.HistorialCompleto);
        Assert.Null(model.Resumen.PunitorioAplicadoPendiente);
        Assert.Null(model.Resumen.TotalCobrableActual);
        var aplicacionProyectada = Assert.Single(model.Aplicaciones);
        Assert.Null(aplicacionProyectada.ImporteTeorico);
        Assert.Null(aplicacionProyectada.ImportePagado);
    }

    [Fact]
    public void ViewModels_NoExponenEntidadesEfNiSnapshotJson()
    {
        var tipos = new[]
        {
            typeof(CuotaPunitorioDetalleViewModel),
            typeof(CuotaPunitorioResumenViewModel),
            typeof(PunitorioAplicacionHistorialViewModel),
            typeof(PagoCuotaHistorialViewModel)
        };

        foreach (var propiedad in tipos.SelectMany(t => t.GetProperties()))
        {
            Assert.DoesNotContain("Models.Entities", propiedad.PropertyType.FullName ?? string.Empty);
            Assert.DoesNotContain("SnapshotJson", propiedad.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(EstadoCalculoPunitorioDetalle.TasaCero, "Tasa 0%")]
    [InlineData(EstadoCalculoPunitorioDetalle.SinConfiguracion, "Sin configuración")]
    [InlineData(EstadoCalculoPunitorioDetalle.ConfiguracionInactiva, "Configuración inactiva")]
    [InlineData(EstadoCalculoPunitorioDetalle.DentroDeGracia, "Dentro de gracia")]
    [InlineData(EstadoCalculoPunitorioDetalle.HistorialIncompleto, "Historial incompleto")]
    [InlineData(EstadoCalculoPunitorioDetalle.SinSaldo, "Sin saldo")]
    public void ViewModel_DistingueEstadosDeCalculoConTextoEIcono(
        EstadoCalculoPunitorioDetalle estado,
        string texto)
    {
        var model = CuotaPunitorioDetalleViewModel.Desde(Detalle(estadoCalculo: estado));

        Assert.Equal(texto, model.Resumen.EstadoCalculo.Texto);
        Assert.False(string.IsNullOrWhiteSpace(model.Resumen.EstadoCalculo.Icono));
    }

    [Fact]
    public void ViewModel_AplicacionConPagoParcial_MuestraEstadoEspecifico()
    {
        var aplicacion = new PunitorioAplicacionDetalle
        {
            PunitorioAplicadoId = 5,
            Estado = EstadoPunitorioAplicado.Aplicado,
            ImporteNuevoAplicado = 20m,
            ImporteAplicado = 20m,
            ImportePagado = 5m,
            ImportePendiente = 15m,
            FechaCalculo = new DateOnly(2026, 8, 3),
            FechaAplicacion = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc),
            MotivoAplicacion = "Aplicación parcial",
            UsuarioAplicacion = "qa",
            EsActiva = true,
            RowVersionBase64 = "AQ=="
        };

        var model = CuotaPunitorioDetalleViewModel.Desde(Detalle(
            aplicaciones: [aplicacion],
            aplicacionActiva: aplicacion));

        Assert.Equal("Aplicado parcialmente pagado", model.Resumen.EstadoAplicacion.Texto);
        Assert.Equal("Aplicado parcialmente pagado", Assert.Single(model.Aplicaciones).Estado.Texto);
    }

    private static CreditoController Controller(IPunitorioService service) => new(
        creditoService: null!,
        financialService: null!,
        configuracionPagoService: null!,
        configuracionMoraService: null!,
        ventaService: null!,
        logger: NullLogger<CreditoController>.Instance,
        creditoDisponibleService: null!,
        currentUser: null!,
        viewBagBuilder: null!,
        contratoVentaCreditoService: null!,
        punitorioService: service);

    private static PunitorioCuotaDetalleResultado Detalle(
        int creditoId = 7,
        bool historialCompleto = true,
        decimal? aplicadoPendiente = 15m,
        IReadOnlyList<PunitorioAplicacionDetalle>? aplicaciones = null,
        PunitorioAplicacionDetalle? aplicacionActiva = null,
        EstadoCalculoPunitorioDetalle estadoCalculo = EstadoCalculoPunitorioDetalle.Calculado) => new()
    {
        CuotaId = 11,
        CreditoId = creditoId,
        NumeroCuota = 2,
        FechaVencimiento = new DateOnly(2026, 7, 1),
        FechaCalculoComercial = new DateOnly(2026, 8, 3),
        EstadoCuota = EstadoCuota.Parcial,
        MontoTotalCuota = 100m,
        MontoPagadoCapital = 20m,
        CapitalPendiente = 80m,
        HistorialCompleto = historialCompleto,
        MotivoHistorialIncompleto = historialCompleto ? null : "Composición no reconstruible.",
        CuotaRowVersionBase64 = "AQ==",
        CalculoActual = new PunitorioCalculoActualDetalle
        {
            Estado = historialCompleto
                ? estadoCalculo
                : EstadoCalculoPunitorioDetalle.HistorialIncompleto,
            SaldoCapitalSegunLedger = 80m,
            ImporteCalculado = 35m,
            PunitorioAplicadoPendienteReal = aplicadoPendiente
        },
        AplicacionActiva = aplicacionActiva,
        AplicacionesHistoricas = aplicaciones ?? Array.Empty<PunitorioAplicacionDetalle>()
    };

    private sealed class RecordingPunitorioService : IPunitorioService
    {
        private readonly PunitorioCuotaDetalleResultado? _resultado;
        private readonly Exception? _exception;

        public RecordingPunitorioService(PunitorioCuotaDetalleResultado resultado) => _resultado = resultado;
        public RecordingPunitorioService(Exception exception) => _exception = exception;

        public int Calls { get; private set; }
        public int LastCuotaId { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<PunitorioCuotaDetalleResultado> ObtenerDetalleCuotaAsync(
            int cuotaId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastCuotaId = cuotaId;
            LastCancellationToken = cancellationToken;
            return _exception is not null
                ? Task.FromException<PunitorioCuotaDetalleResultado>(_exception)
                : Task.FromResult(_resultado!);
        }

        public Task<PunitorioConsultaResultado> CalcularCuotaAsync(
            int cuotaId,
            DateOnly? fechaCalculo = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<decimal> ObtenerPunitorioAplicadoPendienteAsync(
            int cuotaId,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<IReadOnlyDictionary<int, decimal>> ObtenerPunitorioAplicadoPendientePorCuotasAsync(
            IEnumerable<int> cuotaIds,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<PunitorioAplicadoProgreso?> ObtenerAplicacionActivaConProgresoAsync(
            int cuotaId,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<PunitorioAplicado> AplicarAsync(
            int cuotaId,
            PunitorioAplicarComando comando,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<PunitorioAplicado> AnularAsync(
            int punitorioAplicadoId,
            PunitorioAnularComando comando,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
