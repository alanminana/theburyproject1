using System.Reflection;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels.Punitorio;

namespace TheBuryProject.Tests.Unit;

public class CreditoPunitorioOperacionControllerTests
{
    [Fact]
    public void Aplicar_UsaRutaCanonica_Antiforgery_YPermisoApplyfine()
    {
        var endpoint = typeof(CreditoController).GetMethod(nameof(CreditoController.AplicarPunitorioCuota));

        Assert.NotNull(endpoint);
        Assert.Equal(
            "/Credito/{creditoId:int}/Cuotas/{cuotaId:int}/Punitorio/Aplicar",
            endpoint!.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.NotNull(endpoint.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        var permiso = Assert.Single(endpoint.GetCustomAttributes<PermisoRequeridoAttribute>());
        Assert.Equal("cobranzas", permiso.Modulo);
        Assert.Equal("applyfine", permiso.Accion);
    }

    [Theory]
    [InlineData("es-AR", "$ 42,75")]
    [InlineData("en-US", "$ 42.75")]
    public async Task Aplicar_ValidaRelacion_DecodificaRowVersion_YDevuelveImporteReal(
        string cultura, string importeFormateado)
    {
        var culturaAnterior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultura);
            using var cts = new CancellationTokenSource();
            var service = new RecordingPunitorioService(Detalle())
            {
                Aplicado = new PunitorioAplicado { Id = 21, CuotaId = 11, Importe = 42.75m }
            };
            var controller = Controller(service);

            var result = await controller.AplicarPunitorioCuota(
                creditoId: 7,
                cuotaId: 11,
                new AplicarPunitorioHttpViewModel
                {
                    Motivo = "  Mora verificada  ",
                    CuotaRowVersionBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
                },
                cts.Token);

            var json = Assert.IsType<JsonResult>(result);
            var response = Assert.IsType<PunitorioOperacionResponseViewModel>(json.Value);
            Assert.True(response.Success);
            Assert.True(response.ReloadPanel);
            Assert.Equal(42.75m, response.ImporteAplicadoReal);
            Assert.Contains(importeFormateado, response.Message);
            Assert.Equal(1, service.DetalleCalls);
            Assert.Equal(1, service.AplicarCalls);
            Assert.Equal("Mora verificada", service.LastAplicarCommand!.Motivo);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, service.LastAplicarCommand.CuotaRowVersionEsperada);
            Assert.Equal(cts.Token, service.LastCancellationToken);
        }
        finally
        {
            CultureInfo.CurrentCulture = culturaAnterior;
        }
    }

    [Fact]
    public void Anular_UsaRutaCanonica_Antiforgery_YPermisoRevertfine()
    {
        var endpoint = typeof(CreditoController).GetMethod(nameof(CreditoController.AnularPunitorioCuota));

        Assert.NotNull(endpoint);
        Assert.Equal(
            "/Credito/{creditoId:int}/Cuotas/{cuotaId:int}/Punitorio/{punitorioAplicadoId:int}/Anular",
            endpoint!.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.NotNull(endpoint.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        var permiso = Assert.Single(endpoint.GetCustomAttributes<PermisoRequeridoAttribute>());
        Assert.Equal("cobranzas", permiso.Modulo);
        Assert.Equal("revertfine", permiso.Accion);
    }

    [Fact]
    public async Task Anular_ValidaRelacion_DecodificaRowVersion_YConservaHistorial()
    {
        using var cts = new CancellationTokenSource();
        var aplicacion = Aplicacion(id: 21);
        var service = new RecordingPunitorioService(Detalle(aplicaciones: [aplicacion]))
        {
            Anulado = new PunitorioAplicado
            {
                Id = 21,
                CuotaId = 11,
                Importe = 42.75m,
                Estado = EstadoPunitorioAplicado.Anulado
            }
        };
        var controller = Controller(service);

        var result = await controller.AnularPunitorioCuota(
            creditoId: 7,
            cuotaId: 11,
            punitorioAplicadoId: 21,
            new AnularPunitorioHttpViewModel
            {
                Motivo = "  Aplicación incorrecta  ",
                PunitorioAplicadoRowVersionBase64 = Convert.ToBase64String(new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 })
            },
            cts.Token);

        var json = Assert.IsType<JsonResult>(result);
        var response = Assert.IsType<PunitorioOperacionResponseViewModel>(json.Value);
        Assert.True(response.Success);
        Assert.True(response.ReloadPanel);
        Assert.Null(response.ImporteAplicadoReal);
        Assert.Equal(1, service.AnularCalls);
        Assert.Equal(21, service.LastPunitorioAplicadoId);
        Assert.Equal("Aplicación incorrecta", service.LastAnularCommand!.Motivo);
        Assert.Equal(new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 }, service.LastAnularCommand.RowVersionEsperado);
        Assert.Equal(cts.Token, service.LastCancellationToken);
    }

    [Fact]
    public void HttpDtos_SoloExponenMotivoYRowVersion_SinMassAssignment()
    {
        Assert.Equal(
            ["CuotaRowVersionBase64", "Motivo"],
            typeof(AplicarPunitorioHttpViewModel).GetProperties().Select(p => p.Name).Order().ToArray());
        Assert.Equal(
            ["Motivo", "PunitorioAplicadoRowVersionBase64"],
            typeof(AnularPunitorioHttpViewModel).GetProperties().Select(p => p.Name).Order().ToArray());

        var aplicar = typeof(CreditoController).GetMethod(nameof(CreditoController.AplicarPunitorioCuota))!;
        var anular = typeof(CreditoController).GetMethod(nameof(CreditoController.AnularPunitorioCuota))!;
        Assert.Equal(
            "Acciones.Aplicar",
            aplicar.GetParameters().Single(p => p.ParameterType == typeof(AplicarPunitorioHttpViewModel))
                .GetCustomAttribute<BindAttribute>()?.Prefix);
        Assert.Equal(
            "Acciones.Anulacion.Form",
            anular.GetParameters().Single(p => p.ParameterType == typeof(AnularPunitorioHttpViewModel))
                .GetCustomAttribute<BindAttribute>()?.Prefix);
    }

    [Fact]
    public async Task Aplicar_MotivoSoloEspaciosYBase64Invalido_Devuelve400SinInvocarServicio()
    {
        var service = new RecordingPunitorioService(Detalle());
        var controller = Controller(service);

        var result = await controller.AplicarPunitorioCuota(
            7,
            11,
            new AplicarPunitorioHttpViewModel
            {
                Motivo = "   ",
                CuotaRowVersionBase64 = "no-es-base64"
            },
            CancellationToken.None);

        var response = AssertOperationError(result, StatusCodes.Status400BadRequest);
        Assert.Contains("Acciones.Aplicar.Motivo", response.Errors!.Keys);
        Assert.Contains("Acciones.Aplicar.CuotaRowVersionBase64", response.Errors.Keys);
        Assert.Equal(0, service.DetalleCalls);
        Assert.Equal(0, service.AplicarCalls);
    }

    [Fact]
    public async Task Anular_AplicacionDeOtraCuota_Devuelve404TipadoSinInvocarServicio()
    {
        var service = new RecordingPunitorioService(Detalle(aplicaciones: [Aplicacion(id: 22)]));
        var controller = Controller(service);

        var result = await controller.AnularPunitorioCuota(
            7,
            11,
            21,
            new AnularPunitorioHttpViewModel
            {
                Motivo = "Corrección",
                PunitorioAplicadoRowVersionBase64 = Convert.ToBase64String(new byte[8])
            },
            CancellationToken.None);

        var response = AssertOperationError(result, StatusCodes.Status404NotFound);
        Assert.Contains("no pertenece", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, service.AnularCalls);
    }

    [Theory]
    [InlineData(MotivoRechazoPunitorioAplicado.SolicitudInvalida, StatusCodes.Status400BadRequest, false)]
    [InlineData(MotivoRechazoPunitorioAplicado.NoAutorizado, StatusCodes.Status403Forbidden, false)]
    [InlineData(MotivoRechazoPunitorioAplicado.NoAplicable, StatusCodes.Status409Conflict, true)]
    [InlineData(MotivoRechazoPunitorioAplicado.Conflicto, StatusCodes.Status409Conflict, true)]
    public async Task Aplicar_MapeaRechazoControladoSinFiltrarStack(
        MotivoRechazoPunitorioAplicado motivo,
        int statusCode,
        bool reloadPanel)
    {
        var service = new RecordingPunitorioService(Detalle())
        {
            AplicarException = new PunitorioAplicadoRechazadoException(motivo, "Mensaje funcional")
        };
        var controller = Controller(service);

        var result = await controller.AplicarPunitorioCuota(
            7,
            11,
            new AplicarPunitorioHttpViewModel
            {
                Motivo = "Verificado",
                CuotaRowVersionBase64 = Convert.ToBase64String(new byte[8])
            },
            CancellationToken.None);

        var response = AssertOperationError(result, statusCode);
        Assert.Equal(reloadPanel, response.ReloadPanel);
        Assert.Equal("Mensaje funcional", response.Message);
        Assert.DoesNotContain(nameof(PunitorioAplicadoRechazadoException), response.Message);
    }

    [Fact]
    public void ViewModel_ConPermisos_ProyectaAplicarYAnularDesdeContratoB1()
    {
        var aplicacion = Aplicacion(id: 21);
        var detalleSinAplicacion = Detalle();
        var detalleConAplicacion = Detalle(aplicaciones: [aplicacion]);

        var paraAplicar = CuotaPunitorioDetalleViewModel.Desde(
            detalleSinAplicacion,
            puedeAplicar: true,
            puedeAnular: true);
        var paraAnular = CuotaPunitorioDetalleViewModel.Desde(
            detalleConAplicacion,
            puedeAplicar: true,
            puedeAnular: true);

        Assert.True(paraAplicar.Acciones.PuedeAplicar);
        Assert.Equal(42.75m, paraAplicar.Acciones.DiferencialEstimado);
        Assert.Equal(detalleSinAplicacion.CuotaRowVersionBase64, paraAplicar.Acciones.Aplicar.CuotaRowVersionBase64);
        Assert.False(paraAnular.Acciones.PuedeAplicar);
        Assert.Contains("activa", paraAnular.Acciones.MotivoBloqueoAplicar!, StringComparison.OrdinalIgnoreCase);
        Assert.True(paraAnular.Acciones.Anulacion!.PuedeAnular);
        Assert.Equal(21, paraAnular.Acciones.Anulacion.PunitorioAplicadoId);
        Assert.Equal(aplicacion.RowVersionBase64, paraAnular.Acciones.Anulacion.Form.PunitorioAplicadoRowVersionBase64);
    }

    [Theory]
    [InlineData(EstadoCalculoPunitorioDetalle.SinConfiguracion, "configuraci")]
    [InlineData(EstadoCalculoPunitorioDetalle.ConfiguracionInactiva, "inactiva")]
    [InlineData(EstadoCalculoPunitorioDetalle.DentroDeGracia, "gracia")]
    [InlineData(EstadoCalculoPunitorioDetalle.TasaCero, "0%")]
    [InlineData(EstadoCalculoPunitorioDetalle.HistorialIncompleto, "historial")]
    [InlineData(EstadoCalculoPunitorioDetalle.SinSaldo, "saldo")]
    [InlineData(EstadoCalculoPunitorioDetalle.EntradaInvalida, "lido")]
    public void ViewModel_EstadosNoAplicables_ExplicanBloqueoSinSubmit(
        EstadoCalculoPunitorioDetalle estado,
        string textoEsperado)
    {
        var model = CuotaPunitorioDetalleViewModel.Desde(
            Detalle(estadoCalculo: estado),
            puedeAplicar: true,
            puedeAnular: true);

        Assert.False(model.Acciones.PuedeAplicar);
        Assert.Contains(textoEsperado, model.Acciones.MotivoBloqueoAplicar!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ViewModel_AplicacionConPagoParcial_BloqueaAnulacion()
    {
        var aplicacion = Aplicacion(id: 21, importePagado: 5m, importePendiente: 37.75m);

        var model = CuotaPunitorioDetalleViewModel.Desde(
            Detalle(aplicaciones: [aplicacion]),
            puedeAplicar: true,
            puedeAnular: true);

        Assert.False(model.Acciones.Anulacion!.PuedeAnular);
        Assert.Contains("pago parcial", model.Acciones.Anulacion.MotivoBloqueo!, StringComparison.OrdinalIgnoreCase);
    }

    private static PunitorioOperacionResponseViewModel AssertOperationError(IActionResult result, int statusCode)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(statusCode, objectResult.StatusCode);
        var response = Assert.IsType<PunitorioOperacionResponseViewModel>(objectResult.Value);
        Assert.False(response.Success);
        return response;
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
        IReadOnlyList<PunitorioAplicacionDetalle>? aplicaciones = null,
        EstadoCalculoPunitorioDetalle estadoCalculo = EstadoCalculoPunitorioDetalle.Calculado) => new()
    {
        CuotaId = 11,
        CreditoId = creditoId,
        NumeroCuota = 1,
        FechaVencimiento = new DateOnly(2026, 7, 1),
        FechaCalculoComercial = new DateOnly(2026, 8, 3),
        EstadoCuota = EstadoCuota.Vencida,
        MontoTotalCuota = 100m,
        MontoPagadoCapital = 0m,
        CapitalPendiente = 100m,
        HistorialCompleto = true,
        CuotaRowVersionBase64 = Convert.ToBase64String(new byte[8]),
        CalculoActual = new PunitorioCalculoActualDetalle
        {
            Estado = estadoCalculo,
            SaldoCapitalSegunLedger = 100m,
            ImporteCalculado = 42.75m,
            PunitorioAplicadoPendienteReal = 0m
        },
        AplicacionesHistoricas = aplicaciones ?? Array.Empty<PunitorioAplicacionDetalle>(),
        AplicacionActiva = aplicaciones?.FirstOrDefault(a => a.EsActiva)
    };

    private static PunitorioAplicacionDetalle Aplicacion(
        int id,
        decimal? importePagado = 0m,
        decimal? importePendiente = 42.75m) => new()
    {
        PunitorioAplicadoId = id,
        Estado = EstadoPunitorioAplicado.Aplicado,
        ImporteNuevoAplicado = 42.75m,
        ImporteAplicado = 42.75m,
        ImportePagado = importePagado,
        ImportePendiente = importePendiente,
        FechaCalculo = new DateOnly(2026, 8, 3),
        FechaAplicacion = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc),
        MotivoAplicacion = "Mora verificada",
        UsuarioAplicacion = "operador",
        EsActiva = true,
        RowVersionBase64 = Convert.ToBase64String(new byte[8])
    };

    private sealed class RecordingPunitorioService(PunitorioCuotaDetalleResultado detalle) : IPunitorioService
    {
        public PunitorioAplicado Aplicado { get; init; } = null!;
        public PunitorioAplicado Anulado { get; init; } = null!;
        public Exception? AplicarException { get; init; }
        public Exception? AnularException { get; init; }
        public int DetalleCalls { get; private set; }
        public int AplicarCalls { get; private set; }
        public int AnularCalls { get; private set; }
        public int LastPunitorioAplicadoId { get; private set; }
        public PunitorioAplicarComando? LastAplicarCommand { get; private set; }
        public PunitorioAnularComando? LastAnularCommand { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<PunitorioCuotaDetalleResultado> ObtenerDetalleCuotaAsync(
            int cuotaId,
            CancellationToken cancellationToken = default)
        {
            DetalleCalls++;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(detalle);
        }

        public Task<PunitorioAplicado> AplicarAsync(
            int cuotaId,
            PunitorioAplicarComando comando,
            CancellationToken cancellationToken = default)
        {
            AplicarCalls++;
            LastAplicarCommand = comando;
            LastCancellationToken = cancellationToken;
            if (AplicarException is not null)
                return Task.FromException<PunitorioAplicado>(AplicarException);
            return Task.FromResult(Aplicado);
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

        public Task<PunitorioAplicado> AnularAsync(
            int punitorioAplicadoId,
            PunitorioAnularComando comando,
            CancellationToken cancellationToken = default)
        {
            AnularCalls++;
            LastPunitorioAplicadoId = punitorioAplicadoId;
            LastAnularCommand = comando;
            LastCancellationToken = cancellationToken;
            if (AnularException is not null)
                return Task.FromException<PunitorioAplicado>(AnularException);
            return Task.FromResult(Anulado);
        }
    }
}
