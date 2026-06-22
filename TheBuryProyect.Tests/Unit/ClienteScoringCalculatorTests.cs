using System;
using System.Collections.Generic;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

[Trait("Category", "Scoring")]
public sealed class ClienteScoringCalculatorTests
{
    private static readonly DateTime Ahora = new(2026, 6, 22, 0, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------------
    // Builders
    // ---------------------------------------------------------------------

    private static Venta Venta(EstadoVenta estado, DateTime fecha, bool deleted = false) =>
        new() { Numero = "V", ClienteId = 1, Estado = estado, FechaVenta = fecha, IsDeleted = deleted };

    private static Cuota Cuota(EstadoCuota estado, DateTime venc, DateTime? pago = null, decimal pagado = 0m) =>
        new() { Estado = estado, FechaVencimiento = venc, FechaPago = pago, MontoPagado = pagado, MontoTotal = 100m };

    private static Credito Credito(bool deleted = false, params Cuota[] cuotas) =>
        new() { ClienteId = 1, IsDeleted = deleted, Cuotas = new List<Cuota>(cuotas) };

    // ---------------------------------------------------------------------
    // Snapshot: antigüedad
    // ---------------------------------------------------------------------

    [Fact]
    public void Snapshot_Antiguedad_EsDiferenciaEnDias()
    {
        var snap = ClienteScoringCalculator.CalcularSnapshot(
            Ahora.AddDays(-100), new List<Venta>(), new List<Credito>(), Ahora);

        Assert.Equal(100, snap.AntiguedadDias);
    }

    [Fact]
    public void Snapshot_Antiguedad_NuncaNegativa()
    {
        var snap = ClienteScoringCalculator.CalcularSnapshot(
            Ahora.AddDays(5), new List<Venta>(), new List<Credito>(), Ahora);

        Assert.Equal(0, snap.AntiguedadDias);
    }

    // ---------------------------------------------------------------------
    // Snapshot: última venta
    // ---------------------------------------------------------------------

    [Fact]
    public void Snapshot_UltimaVenta_TomaMaxDeVentasReales_IgnoraCotizacionYCancelada()
    {
        var ventas = new List<Venta>
        {
            Venta(EstadoVenta.Confirmada, Ahora.AddDays(-50)),
            Venta(EstadoVenta.Facturada, Ahora.AddDays(-10)),  // <- la más reciente real
            Venta(EstadoVenta.Cotizacion, Ahora.AddDays(-1)),  // ignorada
            Venta(EstadoVenta.Cancelada, Ahora),               // ignorada
        };

        var snap = ClienteScoringCalculator.CalcularSnapshot(
            Ahora.AddDays(-100), ventas, new List<Credito>(), Ahora);

        Assert.Equal(Ahora.AddDays(-10), snap.UltimaVentaFecha);
    }

    [Fact]
    public void Snapshot_UltimaVenta_NullSiNoHayVentasReales()
    {
        var ventas = new List<Venta>
        {
            Venta(EstadoVenta.Cotizacion, Ahora.AddDays(-1)),
            Venta(EstadoVenta.Facturada, Ahora.AddDays(-10), deleted: true),
        };

        var snap = ClienteScoringCalculator.CalcularSnapshot(
            Ahora.AddDays(-100), ventas, new List<Credito>(), Ahora);

        Assert.Null(snap.UltimaVentaFecha);
    }

    // ---------------------------------------------------------------------
    // Snapshot: créditos en término / con atraso
    // ---------------------------------------------------------------------

    [Fact]
    public void Snapshot_CuotaVencida_CuentaComoAtraso()
    {
        var credito = Credito(false, Cuota(EstadoCuota.Vencida, Ahora.AddDays(-5)));

        var snap = ClienteScoringCalculator.CalcularSnapshot(
            Ahora.AddDays(-100), new List<Venta>(), new[] { credito }, Ahora);

        Assert.Equal(1, snap.CreditosConAtraso);
        Assert.Equal(0, snap.CreditosEnTermino);
    }

    [Fact]
    public void Snapshot_CuotaPagadaTarde_CuentaComoAtraso()
    {
        var credito = Credito(false,
            Cuota(EstadoCuota.Pagada, venc: Ahora.AddDays(-30), pago: Ahora.AddDays(-20), pagado: 100m));

        var snap = ClienteScoringCalculator.CalcularSnapshot(
            Ahora.AddDays(-100), new List<Venta>(), new[] { credito }, Ahora);

        Assert.Equal(1, snap.CreditosConAtraso);
        Assert.Equal(0, snap.CreditosEnTermino);
    }

    [Fact]
    public void Snapshot_CuotaPendienteYaVencida_CuentaComoAtraso()
    {
        var credito = Credito(false, Cuota(EstadoCuota.Pendiente, venc: Ahora.AddDays(-1)));

        var snap = ClienteScoringCalculator.CalcularSnapshot(
            Ahora.AddDays(-100), new List<Venta>(), new[] { credito }, Ahora);

        Assert.Equal(1, snap.CreditosConAtraso);
    }

    [Fact]
    public void Snapshot_CuotaPagadaEnTermino_CuentaComoEnTermino()
    {
        var credito = Credito(false,
            Cuota(EstadoCuota.Pagada, venc: Ahora.AddDays(-30), pago: Ahora.AddDays(-31), pagado: 100m));

        var snap = ClienteScoringCalculator.CalcularSnapshot(
            Ahora.AddDays(-100), new List<Venta>(), new[] { credito }, Ahora);

        Assert.Equal(1, snap.CreditosEnTermino);
        Assert.Equal(0, snap.CreditosConAtraso);
    }

    [Fact]
    public void Snapshot_CreditoFresco_SinPagosYCuotasFuturas_NoCuentaEnNinguno()
    {
        var credito = Credito(false,
            Cuota(EstadoCuota.Pendiente, venc: Ahora.AddDays(30)),
            Cuota(EstadoCuota.Pendiente, venc: Ahora.AddDays(60)));

        var snap = ClienteScoringCalculator.CalcularSnapshot(
            Ahora.AddDays(-100), new List<Venta>(), new[] { credito }, Ahora);

        Assert.Equal(0, snap.CreditosEnTermino);
        Assert.Equal(0, snap.CreditosConAtraso);
    }

    [Fact]
    public void Snapshot_CuotasCanceladas_SeExcluyen()
    {
        var credito = Credito(false,
            Cuota(EstadoCuota.Cancelada, venc: Ahora.AddDays(-30)),
            Cuota(EstadoCuota.Cancelada, venc: Ahora.AddDays(-1)));

        var snap = ClienteScoringCalculator.CalcularSnapshot(
            Ahora.AddDays(-100), new List<Venta>(), new[] { credito }, Ahora);

        Assert.Equal(0, snap.CreditosEnTermino);
        Assert.Equal(0, snap.CreditosConAtraso);
    }

    [Fact]
    public void Snapshot_CreditoEliminado_SeIgnora()
    {
        var credito = Credito(deleted: true, Cuota(EstadoCuota.Vencida, Ahora.AddDays(-5)));

        var snap = ClienteScoringCalculator.CalcularSnapshot(
            Ahora.AddDays(-100), new List<Venta>(), new[] { credito }, Ahora);

        Assert.Equal(0, snap.CreditosConAtraso);
    }

    // ---------------------------------------------------------------------
    // Puntaje (config default)
    // ---------------------------------------------------------------------

    private static ClienteScoringSnapshot Snap(
        int antiguedadDias = 0,
        DateTime? ultimaVenta = null,
        int enTermino = 0,
        int conAtraso = 0) =>
        new()
        {
            AntiguedadDias = antiguedadDias,
            UltimaVentaFecha = ultimaVenta,
            CreditosEnTermino = enTermino,
            CreditosConAtraso = conAtraso
        };

    [Fact]
    public void Puntaje_ClienteNuevoSinHistorial_EsLaBase1()
    {
        var config = ConfiguracionScoringCliente.CrearDefault();

        var puntaje = ClienteScoringCalculator.CalcularPuntaje(Snap(), null, config, Ahora);

        Assert.Equal(1, puntaje);
    }

    [Fact]
    public void Puntaje_AntiguedadSobreUmbral_SumaPuntos()
    {
        var config = ConfiguracionScoringCliente.CrearDefault(); // 12 meses => 360 días

        var puntaje = ClienteScoringCalculator.CalcularPuntaje(Snap(antiguedadDias: 400), null, config, Ahora);

        Assert.Equal(2, puntaje); // base 1 + antigüedad 1
    }

    [Fact]
    public void Puntaje_AntiguedadFactorApagado_NoSuma()
    {
        var config = ConfiguracionScoringCliente.CrearDefault();
        config.AntiguedadActiva = false;

        var puntaje = ClienteScoringCalculator.CalcularPuntaje(Snap(antiguedadDias: 400), null, config, Ahora);

        Assert.Equal(1, puntaje);
    }

    [Fact]
    public void Puntaje_ActividadReciente_SumaPuntos()
    {
        var config = ConfiguracionScoringCliente.CrearDefault(); // 6 meses => 180 días

        var puntaje = ClienteScoringCalculator.CalcularPuntaje(
            Snap(ultimaVenta: Ahora.AddDays(-30)), null, config, Ahora);

        Assert.Equal(2, puntaje); // base 1 + actividad 1
    }

    [Fact]
    public void Puntaje_ActividadVieja_NoSuma()
    {
        var config = ConfiguracionScoringCliente.CrearDefault();

        var puntaje = ClienteScoringCalculator.CalcularPuntaje(
            Snap(ultimaVenta: Ahora.AddDays(-365)), null, config, Ahora);

        Assert.Equal(1, puntaje);
    }

    [Fact]
    public void Puntaje_BuenPagador_SumaBonus()
    {
        var config = ConfiguracionScoringCliente.CrearDefault();

        var puntaje = ClienteScoringCalculator.CalcularPuntaje(Snap(enTermino: 2), null, config, Ahora);

        Assert.Equal(3, puntaje); // base 1 + pago 2
    }

    [Fact]
    public void Puntaje_ConAtraso_PenalizaYAcotaAlMinimo()
    {
        var config = ConfiguracionScoringCliente.CrearDefault(); // -2, min 1

        var puntaje = ClienteScoringCalculator.CalcularPuntaje(Snap(conAtraso: 1), null, config, Ahora);

        Assert.Equal(1, puntaje); // 1 - 2 = -1, clamp a min 1
    }

    [Fact]
    public void Puntaje_Sueldo_SoloSumaSiActivoYSobreUmbral()
    {
        var config = ConfiguracionScoringCliente.CrearDefault();
        config.SueldoActivo = true;
        config.SueldoUmbral = 500_000m;
        config.SueldoPuntos = 1;

        Assert.Equal(2, ClienteScoringCalculator.CalcularPuntaje(Snap(), 600_000m, config, Ahora));
        Assert.Equal(1, ClienteScoringCalculator.CalcularPuntaje(Snap(), 400_000m, config, Ahora));
        Assert.Equal(1, ClienteScoringCalculator.CalcularPuntaje(Snap(), null, config, Ahora));
    }

    [Fact]
    public void Puntaje_SeAcotaAlMaximo()
    {
        var config = ConfiguracionScoringCliente.CrearDefault(); // max 5
        // antigüedad(+1) + actividad(+1) + pago(+2) = base1 => 5; sueldo intentaría exceder
        config.SueldoActivo = true;
        config.SueldoUmbral = 0m;
        config.SueldoPuntos = 5;

        var puntaje = ClienteScoringCalculator.CalcularPuntaje(
            Snap(antiguedadDias: 400, ultimaVenta: Ahora.AddDays(-10), enTermino: 1), 100m, config, Ahora);

        Assert.Equal(5, puntaje);
    }
}
