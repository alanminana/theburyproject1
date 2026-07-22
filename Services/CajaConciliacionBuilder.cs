using System.Globalization;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services;

/// <summary>
/// Arma el <see cref="CajaConciliacionViewModel"/> a partir de un
/// <see cref="DetallesAperturaViewModel"/> ya poblado por <see cref="CajaService"/>.
///
/// Es un cálculo PURO (sin DB, sin estado): reutiliza los totales físico/digital y la
/// separación de medios que ya resuelve el service, y SOLO deriva los agregados nuevos de
/// presentación (Vendido/Cobrado/Pendiente por venta y por medio, y el libro mayor con saldo
/// esperado acumulado). No cambia reglas de negocio.
///
/// El criterio "impacta caja física" (<see cref="EsMovimientoFisico"/>) replica el de
/// <c>CajaService.EsMovimientoFisico</c>; se mantiene acá para no tocar el service (entangled)
/// y queda cubierto por <c>CajaConciliacionBuilderTests</c>. Deuda: unificar en una sola fuente.
/// </summary>
public static class CajaConciliacionBuilder
{
    public static CajaConciliacionViewModel Build(
        DetallesAperturaViewModel detalle,
        CierreCaja? cierre,
        bool puedeOperar)
    {
        ArgumentNullException.ThrowIfNull(detalle);
        var apertura = detalle.Apertura;
        var movimientos = (detalle.Movimientos ?? new List<MovimientoCaja>())
            .Where(m => !m.IsDeleted)
            .ToList();
        var ventas = (detalle.VentasDelTurno ?? new List<Venta>())
            .Where(v => !v.IsDeleted)
            .ToList();

        var estaCerrada = apertura.Cerrada;

        // Cobrado real por venta = ingresos − egresos de movimientos ligados a esa venta.
        // (los egresos cubren reversiones por cancelación). Crédito personal / cuenta corriente
        // no generan movimiento ⇒ cobrado 0 ⇒ todo queda pendiente.
        var cobradoPorVenta = movimientos
            .Where(m => m.VentaId.HasValue)
            .GroupBy(m => m.VentaId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Where(m => m.Tipo == TipoMovimientoCaja.Ingreso).Sum(m => m.Monto)
                   - g.Where(m => m.Tipo == TipoMovimientoCaja.Egreso).Sum(m => m.Monto));

        var lineasVenta = ventas.Select(v => MapVenta(v, cobradoPorVenta)).ToList();

        var ventasEfectivas = lineasVenta.Where(v => v.Categoria == VentaTurnoCategoria.Efectiva).ToList();
        var totalVendido = ventasEfectivas.Sum(v => v.TotalVenta);
        var totalPendiente = ventasEfectivas.Sum(v => v.Pendiente);

        var vm = new CajaConciliacionViewModel
        {
            AperturaId = apertura.Id,
            CierreId = cierre?.Id,
            NombreCaja = apertura.Caja?.Nombre ?? "Caja",
            CodigoCaja = apertura.Caja?.Codigo,
            Ubicacion = apertura.Caja?.Ubicacion,
            Sucursal = apertura.Caja?.Sucursal,
            EstaCerrada = estaCerrada,
            PuedeOperar = puedeOperar && !estaCerrada,
            FechaApertura = apertura.FechaApertura.ToLocalTime(),
            FechaCierre = cierre?.FechaCierre.ToLocalTime(),
            Responsable = apertura.UsuarioApertura,
            UsuarioCierre = cierre?.UsuarioCierre,
            ObservacionesApertura = apertura.ObservacionesApertura,
            ObservacionesCierre = cierre?.ObservacionesCierre,
            UltimaActividad = movimientos.Count == 0
                ? null
                : movimientos.Max(m => m.FechaMovimiento).ToLocalTime(),

            FondoInicial = apertura.MontoInicial,
            TotalVendido = totalVendido,
            TotalCobrado = detalle.TotalIngresosFisicos + detalle.TotalIngresosDigitales,
            TotalCobradoEfectivo = detalle.TotalIngresosFisicos,
            TotalCobradoDigital = detalle.TotalIngresosDigitales,
            TotalPendiente = totalPendiente,
            IngresosEfectivo = detalle.TotalIngresosFisicos,
            EgresosEfectivo = detalle.TotalEgresosFisicos,
            CajaFisicaEsperada = detalle.CajaFisicaEsperada,

            Ventas = lineasVenta,
            Movimientos = movimientos
                .OrderByDescending(m => m.FechaMovimiento)
                .Select(MapMovimiento)
                .ToList(),
            LibroMayor = BuildLibroMayor(apertura.MontoInicial, movimientos),
            ResumenPorMedio = BuildResumenPorMedio(ventasEfectivas, detalle.ResumenRealPorMedioPago),
            Auditoria = BuildAuditoria(apertura, movimientos, cierre, lineasVenta),
        };

        if (apertura.FechaApertura != default && vm.FechaCierre.HasValue)
        {
            vm.Duracion = vm.FechaCierre.Value - vm.FechaApertura;
        }

        // Ventas que generan deuda o cobranza no física (evita pensar que falta plata en caja).
        vm.VentasSinImpacto = lineasVenta
            .Where(v => v.Categoria != VentaTurnoCategoria.Registro && !v.ImpactaCajaFisica)
            .ToList();

        if (cierre != null)
        {
            vm.EfectivoContado = cierre.EfectivoContado;
            vm.ChequesContados = cierre.ChequesContados;
            vm.ValesContados = cierre.ValesContados;
            vm.MontoEsperadoSistema = cierre.MontoEsperadoSistema;
            vm.MontoTotalReal = cierre.MontoTotalReal;
            vm.DiferenciaCaja = cierre.Diferencia;
            vm.TieneDiferencia = cierre.TieneDiferencia;
            vm.JustificacionDiferencia = cierre.JustificacionDiferencia;
            vm.DetalleArqueo = cierre.DetalleArqueo;
        }

        return vm;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Mapeos de venta
    // ──────────────────────────────────────────────────────────────────────

    private static VentaTurnoLineaViewModel MapVenta(Venta v, IReadOnlyDictionary<int, decimal> cobradoPorVenta)
    {
        var categoria = CategoriaVenta(v.Estado);
        var impacta = v.TipoPago == TipoPago.Efectivo;

        // Crédito personal y cuenta corriente nunca generan movimiento inmediato ⇒ cobrado 0.
        decimal cobrado = GeneraIngresoInmediato(v.TipoPago) && cobradoPorVenta.TryGetValue(v.Id, out var c)
            ? c
            : 0m;
        if (cobrado < 0m) cobrado = 0m;
        if (cobrado > v.Total) cobrado = v.Total;

        var pendiente = v.Total - cobrado;

        string? motivo = null;
        if (!impacta && categoria != VentaTurnoCategoria.Registro)
        {
            motivo = v.TipoPago switch
            {
                TipoPago.CreditoPersonal => "Crédito personal pendiente de cobro",
                TipoPago.CuentaCorriente => "Cuenta corriente pendiente de cobro",
                _ => "Cobro digital (no es efectivo en caja)"
            };
        }

        return new VentaTurnoLineaViewModel
        {
            VentaId = v.Id,
            Numero = v.Numero,
            Fecha = v.FechaVenta.ToLocalTime(),
            Cliente = ClienteLabel(v),
            Estado = v.Estado,
            EstadoLabel = EstadoLabel(v.Estado),
            EstadoChipClass = EstadoChipClass(v.Estado),
            MedioPago = TipoPagoLabel(v.TipoPago),
            MedioKey = MedioKey(v.TipoPago),
            TotalVenta = v.Total,
            CobradoAhora = cobrado,
            Pendiente = pendiente,
            ImpactaCajaFisica = impacta,
            Referencia = v.Numero,
            MotivoNoImpacta = motivo,
            Categoria = categoria
        };
    }

    private static VentaTurnoCategoria CategoriaVenta(EstadoVenta estado) => estado switch
    {
        EstadoVenta.Confirmada or EstadoVenta.Facturada or EstadoVenta.Entregada => VentaTurnoCategoria.Efectiva,
        EstadoVenta.PendienteRequisitos or EstadoVenta.PendienteFinanciacion => VentaTurnoCategoria.Pendiente,
        _ => VentaTurnoCategoria.Registro
    };

    // ──────────────────────────────────────────────────────────────────────
    // Movimientos
    // ──────────────────────────────────────────────────────────────────────

    private static MovimientoCajaLineaViewModel MapMovimiento(MovimientoCaja m)
    {
        var esIngreso = m.Tipo == TipoMovimientoCaja.Ingreso;
        var medio = MedioLabelMovimiento(m);
        return new MovimientoCajaLineaViewModel
        {
            MovimientoId = m.Id,
            FechaHora = m.FechaMovimiento.ToLocalTime(),
            EsIngreso = esIngreso,
            TipoLabel = esIngreso ? "Ingreso" : "Egreso",
            Concepto = ConceptoLabel(m.Concepto),
            MedioPago = medio,
            MedioKey = MedioKey(medio),
            Referencia = m.Referencia,
            Descripcion = m.Descripcion,
            Entra = esIngreso ? m.Monto : 0m,
            Sale = esIngreso ? 0m : m.Monto,
            Usuario = m.Usuario,
            Observacion = m.Observaciones,
            ImpactaCajaFisica = EsMovimientoFisico(m),
            ImporteBase = m.ImporteBase,
            RecargoMedioPago = m.RecargoMedioPago,
            DescuentoMedioPago = m.DescuentoMedioPago,
            CategoriaImpacto = CategoriaImpactoMovimiento(m, medio),
            EstadoAcreditacion = m.EstadoAcreditacion
        };
    }

    /// <summary>
    /// Clasifica el impacto financiero del movimiento por medio de pago (spec 5.2), sin cambiar
    /// reglas de negocio: solo separa dinero real, saldo bancario, valores a acreditar y
    /// operaciones sin ingreso inmediato. Consistente con <see cref="EsMovimientoFisico"/>.
    /// </summary>
    private static CategoriaImpactoCaja CategoriaImpactoMovimiento(MovimientoCaja m, string medioLabel)
    {
        var key = m.TipoPago.HasValue ? MedioKey(m.TipoPago.Value) : MedioKey(medioLabel);
        return key switch
        {
            "efectivo" => CategoriaImpactoCaja.CajaFisica,
            "transferencia" or "mercadopago" => CategoriaImpactoCaja.CuentaBancaria,
            "tarjeta" or "cheque" => CategoriaImpactoCaja.AAcreditar,
            "credito" or "cuentacorriente" => CategoriaImpactoCaja.SinIngresoInmediato,
            // Ajustes/gastos/extracciones/depósitos de efectivo caen en "otro" pero mueven caja física.
            _ => EsMovimientoFisico(m) ? CategoriaImpactoCaja.CajaFisica : CategoriaImpactoCaja.SinIngresoInmediato
        };
    }

    // ──────────────────────────────────────────────────────────────────────
    // Libro mayor de caja: saldo esperado acumulado fila por fila.
    // La apertura es el primer registro; solo las filas que impactan caja física
    // modifican el saldo; las que no, quedan marcadas y mantienen el saldo.
    // ──────────────────────────────────────────────────────────────────────

    private static List<ConciliacionLineaViewModel> BuildLibroMayor(decimal fondoInicial, List<MovimientoCaja> movimientos)
    {
        var filas = new List<ConciliacionLineaViewModel>();
        var saldo = fondoInicial;
        var orden = 1;

        filas.Add(new ConciliacionLineaViewModel
        {
            Orden = orden++,
            FechaHora = movimientos.Count > 0
                ? movimientos.Min(m => m.FechaMovimiento).ToLocalTime()
                : DateTime.Now,
            TipoLabel = "Apertura",
            Concepto = "Fondo inicial",
            MedioPago = "Efectivo",
            Referencia = "Apertura de caja",
            Entra = fondoInicial,
            Sale = 0m,
            SaldoEsperado = saldo,
            ImpactaCajaFisica = true,
            EsApertura = true
        });

        foreach (var m in movimientos.OrderBy(x => x.FechaMovimiento).ThenBy(x => x.Id))
        {
            var esIngreso = m.Tipo == TipoMovimientoCaja.Ingreso;
            var fisico = EsMovimientoFisico(m);
            var entra = esIngreso ? m.Monto : 0m;
            var sale = esIngreso ? 0m : m.Monto;

            if (fisico)
            {
                saldo += entra - sale;
            }

            filas.Add(new ConciliacionLineaViewModel
            {
                Orden = orden++,
                FechaHora = m.FechaMovimiento.ToLocalTime(),
                TipoLabel = esIngreso ? "Ingreso" : "Egreso",
                Concepto = ConceptoLabel(m.Concepto),
                MedioPago = MedioLabelMovimiento(m),
                Referencia = m.Referencia,
                Entra = entra,
                Sale = sale,
                SaldoEsperado = saldo,
                ImpactaCajaFisica = fisico,
                EsApertura = false
            });
        }

        return filas;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Resumen por medio: Vendido (ventas por TipoPago) vs Cobrado (movimientos reales).
    // ──────────────────────────────────────────────────────────────────────

    private static List<ResumenMedioConciliacionViewModel> BuildResumenPorMedio(
        List<VentaTurnoLineaViewModel> ventasEfectivas,
        List<ResumenMedioPagoCajaViewModel> resumenReal)
    {
        var acc = new Dictionary<string, ResumenMedioConciliacionViewModel>();

        ResumenMedioConciliacionViewModel Get(string key) =>
            acc.TryGetValue(key, out var r)
                ? r
                : acc[key] = new ResumenMedioConciliacionViewModel
                {
                    MedioKey = key,
                    MedioPago = MedioLabelFromKey(key),
                    ImpactaCajaFisica = key == "efectivo"
                };

        foreach (var v in ventasEfectivas)
        {
            var r = Get(v.MedioKey);
            r.TotalVendido += v.TotalVenta;
            r.TotalPendiente += v.Pendiente;
        }

        foreach (var medio in resumenReal ?? new List<ResumenMedioPagoCajaViewModel>())
        {
            var key = MedioKey(medio.MedioPago);
            var r = Get(key);
            r.TotalCobrado += medio.TotalIngresos;
        }

        return acc.Values
            .OrderByDescending(r => r.ImpactaCajaFisica)
            .ThenByDescending(r => r.TotalVendido + r.TotalCobrado)
            .ToList();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Auditoría del turno (apertura + movimientos + cierre + anulaciones).
    // ──────────────────────────────────────────────────────────────────────

    private static List<AuditoriaCajaLineaViewModel> BuildAuditoria(
        AperturaCaja apertura,
        List<MovimientoCaja> movimientos,
        CierreCaja? cierre,
        List<VentaTurnoLineaViewModel> ventas)
    {
        var eventos = new List<AuditoriaCajaLineaViewModel>
        {
            new()
            {
                FechaHora = apertura.FechaApertura.ToLocalTime(),
                Usuario = apertura.UsuarioApertura,
                Accion = "Apertura de caja",
                Entidad = "Caja",
                Referencia = apertura.Caja?.Codigo,
                Detalle = string.IsNullOrWhiteSpace(apertura.ObservacionesApertura)
                    ? "Apertura del turno"
                    : apertura.ObservacionesApertura,
                Monto = apertura.MontoInicial
            }
        };

        eventos.AddRange(movimientos.Select(m => new AuditoriaCajaLineaViewModel
        {
            FechaHora = m.FechaMovimiento.ToLocalTime(),
            Usuario = m.Usuario,
            Accion = m.Concepto == ConceptoMovimientoCaja.ReversionVenta
                ? "Reversión de venta"
                : $"Movimiento · {(m.Tipo == TipoMovimientoCaja.Ingreso ? "Ingreso" : "Egreso")}",
            Entidad = "Movimiento de caja",
            Referencia = m.Referencia,
            Detalle = m.Descripcion,
            Monto = m.Monto
        }));

        eventos.AddRange(ventas
            .Where(v => v.Cancelada)
            .Select(v => new AuditoriaCajaLineaViewModel
            {
                FechaHora = v.Fecha,
                Usuario = "—",
                Accion = "Venta anulada",
                Entidad = "Venta",
                Referencia = v.Numero,
                Detalle = ClienteCorto(v.Cliente),
                Monto = v.TotalVenta
            }));

        if (cierre != null)
        {
            eventos.Add(new AuditoriaCajaLineaViewModel
            {
                FechaHora = cierre.FechaCierre.ToLocalTime(),
                Usuario = cierre.UsuarioCierre,
                Accion = "Cierre de caja",
                Entidad = "Caja",
                Referencia = apertura.Caja?.Codigo,
                Detalle = cierre.TieneDiferencia
                    ? $"Diferencia ${cierre.Diferencia.ToString("N2", CultureInfo.GetCultureInfo("es-AR"))}"
                    : "Cierre sin diferencias",
                Monto = cierre.MontoTotalReal
            });
        }

        return eventos.OrderBy(e => e.FechaHora).ToList();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers de clasificación / etiquetas (portados de las vistas / CajaService)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replica de <c>CajaService.EsMovimientoFisico</c>: solo el efectivo modifica la caja física.
    /// </summary>
    public static bool EsMovimientoFisico(MovimientoCaja mov)
    {
        if (mov.TipoPago.HasValue)
            return mov.TipoPago.Value == TipoPago.Efectivo;

        if (MedioLabelMovimiento(mov).Equals("Efectivo", StringComparison.OrdinalIgnoreCase))
            return true;

        return mov.Concepto is ConceptoMovimientoCaja.GastoOperativo
            or ConceptoMovimientoCaja.ExtraccionEfectivo
            or ConceptoMovimientoCaja.DepositoEfectivo
            or ConceptoMovimientoCaja.DevolucionCliente
            or ConceptoMovimientoCaja.AjusteCaja;
    }

    private static bool GeneraIngresoInmediato(TipoPago tipoPago) =>
        tipoPago != TipoPago.CreditoPersonal && tipoPago != TipoPago.CuentaCorriente;

    private static string MedioLabelMovimiento(MovimientoCaja mov)
    {
        if (mov.TipoPago.HasValue)
            return TipoPagoLabel(mov.TipoPago.Value);

        return mov.Concepto switch
        {
            ConceptoMovimientoCaja.VentaEfectivo => "Efectivo",
            ConceptoMovimientoCaja.VentaTarjeta => "Tarjeta",
            ConceptoMovimientoCaja.VentaCheque => "Cheque",
            ConceptoMovimientoCaja.VentaTransferencia => "Transferencia",
            ConceptoMovimientoCaja.VentaMercadoPago => "Mercado Pago",
            ConceptoMovimientoCaja.DepositoEfectivo => "Efectivo",
            ConceptoMovimientoCaja.ExtraccionEfectivo => "Efectivo",
            ConceptoMovimientoCaja.GastoOperativo => "Efectivo",
            ConceptoMovimientoCaja.AjusteCaja => "Ajuste",
            _ => InferirMedioPorObservaciones(mov.Observaciones)
        };
    }

    private static string InferirMedioPorObservaciones(string? observaciones)
    {
        if (string.IsNullOrWhiteSpace(observaciones)) return "Efectivo";
        var obs = observaciones.ToLowerInvariant();
        if (obs.Contains("transferencia")) return "Transferencia";
        if (obs.Contains("mercadopago") || obs.Contains("mercado pago")) return "Mercado Pago";
        if (obs.Contains("débito") || obs.Contains("debito")) return "Tarjeta Débito";
        if (obs.Contains("crédito") || obs.Contains("credito") || obs.Contains("tarjeta")) return "Tarjeta";
        if (obs.Contains("cheque")) return "Cheque";
        return "Efectivo";
    }

    private static string MedioKey(TipoPago tipoPago) => tipoPago switch
    {
        TipoPago.Efectivo => "efectivo",
        TipoPago.Tarjeta or TipoPago.TarjetaDebito or TipoPago.TarjetaCredito => "tarjeta",
        TipoPago.MercadoPago => "mercadopago",
        TipoPago.Transferencia => "transferencia",
        TipoPago.Cheque => "cheque",
        TipoPago.CreditoPersonal => "credito",
        TipoPago.CuentaCorriente => "cuentacorriente",
        _ => "otro"
    };

    private static string MedioKey(string medioPago)
    {
        var medio = (medioPago ?? string.Empty).ToLowerInvariant();
        if (medio.Contains("efectivo")) return "efectivo";
        if (medio.Contains("tarjeta")) return "tarjeta";
        if (medio.Contains("mercado")) return "mercadopago";
        if (medio.Contains("transferencia")) return "transferencia";
        if (medio.Contains("cheque")) return "cheque";
        if (medio.Contains("crédito") || medio.Contains("credito")) return "credito";
        if (medio.Contains("cuenta")) return "cuentacorriente";
        return "otro";
    }

    private static string MedioLabelFromKey(string key) => key switch
    {
        "efectivo" => "Efectivo",
        "tarjeta" => "Tarjeta",
        "mercadopago" => "Mercado Pago",
        "transferencia" => "Transferencia",
        "cheque" => "Cheque",
        "credito" => "Crédito personal",
        "cuentacorriente" => "Cuenta corriente",
        _ => "Otro"
    };

    private static string TipoPagoLabel(TipoPago tipoPago) => tipoPago switch
    {
        TipoPago.Efectivo => "Efectivo",
        TipoPago.Transferencia => "Transferencia",
        TipoPago.TarjetaDebito => "Tarjeta débito",
        TipoPago.TarjetaCredito => "Tarjeta crédito",
        TipoPago.Tarjeta => "Tarjeta",
        TipoPago.Cheque => "Cheque",
        TipoPago.CreditoPersonal => "Crédito personal",
        TipoPago.CuentaCorriente => "Cuenta corriente",
        TipoPago.MercadoPago => "Mercado Pago",
        _ => tipoPago.ToString()
    };

    private static string EstadoLabel(EstadoVenta estado) => estado switch
    {
        EstadoVenta.Cotizacion => "Cotización",
        EstadoVenta.Presupuesto => "Presupuesto",
        EstadoVenta.Confirmada => "Confirmada",
        EstadoVenta.Facturada => "Facturada",
        EstadoVenta.Entregada => "Entregada",
        EstadoVenta.Cancelada => "Cancelada",
        EstadoVenta.PendienteRequisitos => "Pendiente requisitos",
        EstadoVenta.PendienteFinanciacion => "Pendiente financiación",
        _ => estado.ToString()
    };

    private static string EstadoChipClass(EstadoVenta estado) => estado switch
    {
        EstadoVenta.Confirmada => "chip chip-info",
        EstadoVenta.Facturada or EstadoVenta.Entregada => "chip chip-ok",
        EstadoVenta.PendienteRequisitos or EstadoVenta.PendienteFinanciacion => "chip chip-warn",
        EstadoVenta.Cancelada => "chip chip-bad",
        _ => "chip chip-neutral"
    };

    private static string ConceptoLabel(ConceptoMovimientoCaja concepto) => concepto switch
    {
        ConceptoMovimientoCaja.VentaEfectivo => "Venta efectivo",
        ConceptoMovimientoCaja.VentaTarjeta => "Venta tarjeta",
        ConceptoMovimientoCaja.VentaCheque => "Venta cheque",
        ConceptoMovimientoCaja.VentaTransferencia => "Venta transferencia",
        ConceptoMovimientoCaja.VentaMercadoPago => "Venta Mercado Pago",
        ConceptoMovimientoCaja.LiquidacionMercadoLibre => "Liquidación Mercado Libre",
        ConceptoMovimientoCaja.CobroCuota => "Cobro cuota",
        ConceptoMovimientoCaja.CancelacionCredito => "Cancelación crédito",
        ConceptoMovimientoCaja.AnticipoCredito => "Anticipo crédito",
        ConceptoMovimientoCaja.GastoOperativo => "Gasto operativo",
        ConceptoMovimientoCaja.ExtraccionEfectivo => "Extracción efectivo",
        ConceptoMovimientoCaja.DepositoEfectivo => "Depósito efectivo",
        ConceptoMovimientoCaja.DevolucionCliente => "Devolución cliente",
        ConceptoMovimientoCaja.ReversionVenta => "Reversión venta",
        ConceptoMovimientoCaja.AjusteCaja => "Ajuste caja",
        ConceptoMovimientoCaja.Otro => "Otro",
        _ => concepto.ToString()
    };

    private static string ClienteLabel(Venta venta)
    {
        if (venta.Cliente == null) return "Sin cliente";
        if (!string.IsNullOrWhiteSpace(venta.Cliente.NombreCompleto)) return venta.Cliente.NombreCompleto!;
        var nombre = $"{venta.Cliente.Apellido}, {venta.Cliente.Nombre}".Trim(' ', ',');
        return string.IsNullOrWhiteSpace(nombre) ? "Sin cliente" : nombre;
    }

    private static string ClienteCorto(string cliente) =>
        string.IsNullOrWhiteSpace(cliente) ? "Sin cliente" : cliente;
}
