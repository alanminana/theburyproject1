using System;
using System.Collections.Generic;
using System.Linq;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;
using Xunit;

namespace TheBuryProyect.Tests.Unit;

/// <summary>
/// Cubre la lógica de cálculo de <see cref="CajaConciliacionBuilder"/>: separación
/// Vendido/Cobrado/Pendiente, impacto en caja física por venta y por medio, y el libro mayor
/// con saldo esperado acumulado (criterios de aceptación del rediseño de detalle de caja).
/// </summary>
public class CajaConciliacionBuilderTests
{
    private static readonly DateTime Base = new(2026, 6, 27, 18, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Turno con: apertura $10.000; 2 cobros de cuota en efectivo; 1 venta efectivo;
    /// 1 venta tarjeta (digital); 1 venta a crédito personal; 1 egreso efectivo.
    /// </summary>
    private static DetallesAperturaViewModel BuildDetalleStandard(bool cerrada = false)
    {
        var caja = new Caja { Id = 1, Nombre = "Caja Central", Codigo = "C1" };
        var apertura = new AperturaCaja
        {
            Id = 7,
            Caja = caja,
            CajaId = 1,
            MontoInicial = 10000m,
            UsuarioApertura = "admin",
            FechaApertura = Base,
            Cerrada = cerrada
        };

        var ventaCredito = new Venta { Id = 1, Numero = "VTA-1", Total = 20000m, Estado = EstadoVenta.Facturada, TipoPago = TipoPago.CreditoPersonal, FechaVenta = Base.AddMinutes(6) };
        var ventaEfectivo = new Venta { Id = 2, Numero = "VTA-2", Total = 3000m, Estado = EstadoVenta.Confirmada, TipoPago = TipoPago.Efectivo, FechaVenta = Base.AddMinutes(3) };
        var ventaTarjeta = new Venta { Id = 3, Numero = "VTA-3", Total = 5000m, Estado = EstadoVenta.Facturada, TipoPago = TipoPago.TarjetaCredito, FechaVenta = Base.AddMinutes(4) };

        var movimientos = new List<MovimientoCaja>
        {
            new() { Id = 11, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.CobroCuota, Monto = 1467.63m, FechaMovimiento = Base.AddMinutes(1), Referencia = "CRE-C1", Usuario = "admin", Observaciones = "Medio de pago: Efectivo" },
            new() { Id = 12, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.CobroCuota, Monto = 1467.63m, FechaMovimiento = Base.AddMinutes(2), Referencia = "CRE-C2", Usuario = "admin", Observaciones = "Medio de pago: Efectivo" },
            new() { Id = 13, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaEfectivo, Monto = 3000m, FechaMovimiento = Base.AddMinutes(3), VentaId = 2, TipoPago = TipoPago.Efectivo, Usuario = "admin" },
            new() { Id = 14, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaTarjeta, Monto = 5000m, FechaMovimiento = Base.AddMinutes(4), VentaId = 3, TipoPago = TipoPago.TarjetaCredito, Usuario = "admin" },
            new() { Id = 15, Tipo = TipoMovimientoCaja.Egreso, Concepto = ConceptoMovimientoCaja.GastoOperativo, Monto = 500m, FechaMovimiento = Base.AddMinutes(5), Descripcion = "Pago flete", Usuario = "admin" },
        };
        apertura.Movimientos = movimientos;

        // Totales físico/digital tal como los resolvería CajaService (consistentes con los movimientos).
        const decimal ingresosFisicos = 1467.63m + 1467.63m + 3000m;   // 5935.26
        const decimal egresosFisicos = 500m;
        const decimal ingresosDigitales = 5000m;

        return new DetallesAperturaViewModel
        {
            Apertura = apertura,
            Movimientos = movimientos,
            VentasDelTurno = new List<Venta> { ventaCredito, ventaEfectivo, ventaTarjeta },
            TotalIngresosFisicos = ingresosFisicos,
            TotalEgresosFisicos = egresosFisicos,
            TotalIngresosDigitales = ingresosDigitales,
            TotalEgresosDigitales = 0m,
            CajaFisicaEsperada = apertura.MontoInicial + ingresosFisicos - egresosFisicos, // 15435.26
            ResumenRealPorMedioPago = new List<ResumenMedioPagoCajaViewModel>
            {
                new() { MedioPago = "Efectivo", TotalIngresos = ingresosFisicos, TotalEgresos = egresosFisicos, CantidadMovimientos = 4 },
                new() { MedioPago = "Tarjeta", TotalIngresos = ingresosDigitales, TotalEgresos = 0m, CantidadMovimientos = 1 },
            }
        };
    }

    [Fact]
    public void Cards_SeparanVendidoCobradoPendiente()
    {
        var vm = CajaConciliacionBuilder.Build(BuildDetalleStandard(), cierre: null, puedeOperar: true);

        Assert.Equal(28000m, vm.TotalVendido);                 // 20000 + 3000 + 5000 (efectivas)
        Assert.Equal(20000m, vm.TotalPendiente);               // solo el crédito personal
        Assert.Equal(10935.26m, vm.TotalCobrado);              // 5935.26 efectivo + 5000 digital
        Assert.Equal(5935.26m, vm.TotalCobradoEfectivo);
        Assert.Equal(5000m, vm.TotalCobradoDigital);
        Assert.Equal(15435.26m, vm.CajaFisicaEsperada);        // 10000 + 5935.26 - 500
    }

    [Fact]
    public void Movimiento_ConRecargoMedioPago_MapeaDesgloseBaseYRecargo()
    {
        var detalle = BuildDetalleStandard();
        detalle.Movimientos.Add(new MovimientoCaja
        {
            Id = 99,
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.CobroCuota,
            Monto = 103_000m,
            ImporteBase = 100_000m,
            RecargoMedioPago = 3_000m,
            TipoPago = TipoPago.Transferencia,
            MedioPagoDetalle = "Transferencia",
            FechaMovimiento = Base.AddMinutes(7),
            Referencia = "CRE-C9",
            Usuario = "admin"
        });
        detalle.Apertura.Movimientos = detalle.Movimientos;

        var vm = CajaConciliacionBuilder.Build(detalle, cierre: null, puedeOperar: true);
        var linea = vm.Movimientos.Single(m => m.MovimientoId == 99);

        Assert.Equal(103_000m, linea.Entra);
        Assert.Equal(100_000m, linea.ImporteBase);
        Assert.Equal(3_000m, linea.RecargoMedioPago);
        Assert.True(linea.TieneAjusteMedioPago);
    }

    [Fact]
    public void VentaCreditoPersonal_EsVendidoYPendiente_SinImpactarCajaFisica()
    {
        var vm = CajaConciliacionBuilder.Build(BuildDetalleStandard(), cierre: null, puedeOperar: true);
        var credito = vm.Ventas.Single(v => v.VentaId == 1);

        Assert.Equal(20000m, credito.TotalVenta);
        Assert.Equal(0m, credito.CobradoAhora);
        Assert.Equal(20000m, credito.Pendiente);
        Assert.False(credito.ImpactaCajaFisica);
        Assert.Equal("Venta financiada", credito.MotivoNoImpacta);
        Assert.Contains(vm.VentasSinImpacto, v => v.VentaId == 1);
    }

    [Fact]
    public void VentaEfectivo_QuedaCobradaYSinPendiente()
    {
        var vm = CajaConciliacionBuilder.Build(BuildDetalleStandard(), cierre: null, puedeOperar: true);
        var efectivo = vm.Ventas.Single(v => v.VentaId == 2);

        Assert.Equal(3000m, efectivo.CobradoAhora);
        Assert.Equal(0m, efectivo.Pendiente);
        Assert.True(efectivo.ImpactaCajaFisica);
    }

    [Fact]
    public void VentaDigital_EsCobradaPeroNoImpactaCajaFisica()
    {
        var vm = CajaConciliacionBuilder.Build(BuildDetalleStandard(), cierre: null, puedeOperar: true);
        var tarjeta = vm.Ventas.Single(v => v.VentaId == 3);

        Assert.Equal(5000m, tarjeta.CobradoAhora);
        Assert.Equal(0m, tarjeta.Pendiente);
        Assert.False(tarjeta.ImpactaCajaFisica);
        Assert.Contains(vm.VentasSinImpacto, v => v.VentaId == 3);
    }

    [Fact]
    public void LibroMayor_AperturaPrimera_YSaldoAcumuladoSoloEnFisicos()
    {
        var vm = CajaConciliacionBuilder.Build(BuildDetalleStandard(), cierre: null, puedeOperar: true);
        var filas = vm.LibroMayor;

        // Apertura es el primer registro, con el fondo inicial como saldo.
        Assert.True(filas[0].EsApertura);
        Assert.Equal(10000m, filas[0].SaldoEsperado);
        Assert.True(filas[0].ImpactaCajaFisica);

        // Cobros de cuota en efectivo acumulan saldo.
        Assert.Equal(11467.63m, filas[1].SaldoEsperado);
        Assert.Equal(12935.26m, filas[2].SaldoEsperado);
        Assert.Equal(15935.26m, filas[3].SaldoEsperado); // + venta efectivo 3000

        // La venta con tarjeta (digital) NO cambia el saldo y queda marcada.
        var filaTarjeta = filas[4];
        Assert.False(filaTarjeta.ImpactaCajaFisica);
        Assert.Equal(15935.26m, filaTarjeta.SaldoEsperado);

        // El egreso efectivo resta del saldo esperado.
        Assert.Equal(15435.26m, filas[5].SaldoEsperado);

        // El último saldo del libro mayor coincide con la caja física esperada.
        Assert.Equal(vm.CajaFisicaEsperada, filas[^1].SaldoEsperado);
    }

    [Fact]
    public void ResumenPorMedio_DistingueImpactoYDeudaDeCredito()
    {
        var vm = CajaConciliacionBuilder.Build(BuildDetalleStandard(), cierre: null, puedeOperar: true);

        var credito = vm.ResumenPorMedio.Single(r => r.MedioKey == "credito");
        Assert.Equal(20000m, credito.TotalVendido);
        Assert.Equal(0m, credito.TotalCobrado);
        Assert.Equal(20000m, credito.TotalPendiente);
        Assert.False(credito.ImpactaCajaFisica);

        var efectivo = vm.ResumenPorMedio.Single(r => r.MedioKey == "efectivo");
        Assert.True(efectivo.ImpactaCajaFisica);
        Assert.Equal(5935.26m, efectivo.TotalCobrado);
    }

    [Fact]
    public void Movimientos_UsanColumnasEntraYSaleSeparadas()
    {
        var vm = CajaConciliacionBuilder.Build(BuildDetalleStandard(), cierre: null, puedeOperar: true);

        var egreso = vm.Movimientos.Single(m => m.MovimientoId == 15);
        Assert.False(egreso.EsIngreso);
        Assert.Equal(0m, egreso.Entra);
        Assert.Equal(500m, egreso.Sale);
        Assert.True(egreso.ImpactaCajaFisica);

        var ingreso = vm.Movimientos.Single(m => m.MovimientoId == 13);
        Assert.Equal(3000m, ingreso.Entra);
        Assert.Equal(0m, ingreso.Sale);
    }

    [Fact]
    public void Auditoria_ArrancaConAperturaYListaMovimientos()
    {
        var vm = CajaConciliacionBuilder.Build(BuildDetalleStandard(), cierre: null, puedeOperar: true);

        Assert.Equal("Apertura de caja", vm.Auditoria.First().Accion);
        Assert.Equal(6, vm.Auditoria.Count); // apertura + 5 movimientos
    }

    [Fact]
    public void Cierre_MapeaArqueoYModoLectura()
    {
        var detalle = BuildDetalleStandard(cerrada: true);
        var cierre = new CierreCaja
        {
            Id = 99,
            AperturaCajaId = 7,
            FechaCierre = Base.AddHours(4),
            MontoEsperadoSistema = 15435.26m,
            EfectivoContado = 15000m,
            MontoTotalReal = 15000m,
            Diferencia = -435.26m,
            TieneDiferencia = true,
            JustificacionDiferencia = "Falta de cambio",
            UsuarioCierre = "admin"
        };

        var vm = CajaConciliacionBuilder.Build(detalle, cierre, puedeOperar: true);

        Assert.True(vm.EstaCerrada);
        Assert.True(vm.EsLectura);
        Assert.False(vm.PuedeOperar);                  // cerrada ⇒ no se opera aunque puedeOperar=true
        Assert.Equal(99, vm.CierreId);
        Assert.Equal(-435.26m, vm.DiferenciaCaja);
        Assert.Equal(15000m, vm.EfectivoContado);
        Assert.Equal("Cierre de caja", vm.Auditoria.Last().Accion);
    }
}
