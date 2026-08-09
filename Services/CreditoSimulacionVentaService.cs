using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services;

public sealed class CreditoSimulacionVentaService : ICreditoSimulacionVentaService
{
    private const string TasaGlobalNoConfigurada =
        "La tasa de interés de Crédito Personal no está configurada. " +
        "Configure el valor en Administración → Tipos de Pago.";

    // ML6.1 — Contrato congelado: el plan de cuotas es la ÚNICA fuente del porcentaje que este
    // servicio reporta. Nunca "Producto"/"Perfil"/"Cliente"/"Manual"/"Global": esas etiquetas
    // describían de dónde salía la DISPONIBILIDAD de cantidades (Origen), no de dónde salía el %,
    // que siempre resuelve ResolverTasaDelPlanOTasaGlobalAsync contra el mismo plan.
    private const string FuentePorcentajePlan = "Plan";

    private readonly IFinancialCalculationService _financialService;
    private readonly IConfiguracionPagoService? _configuracionPagoService;
    private readonly IClienteAptitudService? _aptitudService;
    private readonly IVentaService? _ventaService;
    private readonly ICreditoRangoProductoService? _creditoRangoProductoService;

    public CreditoSimulacionVentaService(
        IFinancialCalculationService financialService,
        IConfiguracionPagoService? configuracionPagoService,
        IClienteAptitudService? aptitudService = null,
        IVentaService? ventaService = null,
        ICreditoRangoProductoService? creditoRangoProductoService = null)
    {
        _financialService = financialService;
        _configuracionPagoService = configuracionPagoService;
        _aptitudService = aptitudService;
        _ventaService = ventaService;
        _creditoRangoProductoService = creditoRangoProductoService;
    }

    public async Task<CreditoSimulacionVentaResultado> SimularAsync(
        CreditoSimulacionVentaRequest request,
        CancellationToken cancellationToken = default)
    {
        var anticipoVal = request.Anticipo ?? 0m;
        var gastosVal = request.GastosAdministrativos ?? 0m;

        // Validaciones baratas primero: no dependen de la venta ni de config (evita resolver
        // la tasa/plan solo para descartar el resultado por un dato de entrada inválido).
        if (anticipoVal < 0)
            return CreditoSimulacionVentaResultado.Invalido("El anticipo no puede ser negativo.");
        if (request.Cuotas <= 0)
            return CreditoSimulacionVentaResultado.Invalido("Ingresá una cantidad de cuotas mayor a cero.");
        if (gastosVal < 0)
            return CreditoSimulacionVentaResultado.Invalido("Los gastos administrativos no pueden ser negativos.");

        // Autoridad del monto: con VentaId, el total real de la venta manda siempre sobre
        // cualquier totalVenta que haya mandado el navegador (hidden manipulado, DevTools, etc.).
        // Sin VentaId se preserva el único escenario legítimo demostrado hoy: el total lo manda
        // el caller directamente (no hay venta contra la que verificarlo).
        VentaViewModel? venta = null;
        decimal totalVentaVal;
        if (request.VentaId.HasValue)
        {
            venta = _ventaService is null ? null : await _ventaService.GetByIdAsync(request.VentaId.Value);
            if (venta is null)
                return CreditoSimulacionVentaResultado.Invalido("No se encontró la venta indicada.");

            totalVentaVal = venta.Total;
        }
        else
        {
            totalVentaVal = request.TotalVenta;
        }

        // Resolución de planes sin venta persistida (p. ej. Cotización): mismos productos/cliente
        // que usaría una venta real, para no reconstruir la precedencia plan/cliente/global en un
        // calculator paralelo. Solo aplica cuando el caller no tiene aún una venta contra la cual
        // resolver (con VentaId, venta.Detalles/venta.ClienteId ya cubren este rol).
        var productoIdsEfectivos = venta is not null
            ? venta.Detalles?.Select(d => d.ProductoId) ?? Enumerable.Empty<int>()
            : request.ProductoIds ?? Enumerable.Empty<int>();
        var productoIdsLista = productoIdsEfectivos as IReadOnlyCollection<int> ?? productoIdsEfectivos.ToList();
        var clienteIdEfectivo = venta?.ClienteId ?? request.ClienteId;
        var hayContextoDeProductos = venta is not null || productoIdsLista.Count > 0;

        if (totalVentaVal <= 0)
            return CreditoSimulacionVentaResultado.Invalido("El monto total de la venta debe ser mayor a cero.");
        if (anticipoVal > totalVentaVal)
            return CreditoSimulacionVentaResultado.Invalido("El anticipo no puede superar el total de la venta.");

        // ML6.1 — Contrato congelado (Fase 3): el plan de cuotas es la ÚNICA fuente del porcentaje,
        // siempre. request.TasaMensual/MetodoCalculo/FuenteConfiguracion nunca vuelven a pisarlo —
        // ni siquiera con FuenteConfiguracion+MetodoCalculo ambos Manual (contrato previo a ML6.1,
        // ya inalcanzable desde la UI real de Configurar Venta desde ML6). MetodoCalculo/
        // FuenteConfiguracion siguen decidiendo SOLO qué validación de contexto aplica (p. ej. si
        // hace falta un cliente), nunca de dónde sale la tasa.
        decimal tasaVal;
        // CSR-ML4: mismo criterio que tasaVal — sale del plan resuelto server-side, nunca del
        // request/browser (no hay ningún campo de request que lo transporte: ver P10).
        IReadOnlyList<int> cuotasSinRecargoVal;

        if (hayContextoDeProductos)
        {
            if (_configuracionPagoService is null)
                return CreditoSimulacionVentaResultado.Invalido(TasaGlobalNoConfigurada);

            var planesVenta = await _configuracionPagoService.ResolverPlanesCreditoPersonalAsync(productoIdsLista);

            if (!planesVenta.EsValido)
            {
                return CreditoSimulacionVentaResultado.Invalido(
                    planesVenta.MensajeRechazo ??
                    "No hay planes de Crédito Personal disponibles para los productos de esta venta.");
            }

            if (!planesVenta.RigeConfiguracionUnicaGlobal && planesVenta.BuscarPlan(request.Cuotas) is null)
            {
                return CreditoSimulacionVentaResultado.Invalido(
                    $"La cantidad de cuotas {request.Cuotas} no está habilitada para Crédito personal " +
                    "con los productos de esta venta.");
            }

            // El rango por cantidad de unidades pedidas solo aplica cuando hay una venta real
            // (usa VentaDetalle.Cantidad). Sin venta (p. ej. Cotización) el propio caller ya validó
            // el producto bloqueante/tope de cuotas contra IProductoCreditoRestriccionService.
            if (venta is not null && _creditoRangoProductoService is not null)
            {
                var rango = await _creditoRangoProductoService.ResolverAsync(
                    venta, TipoPago.CreditoPersonal, 1, 120, cancellationToken);
                if (rango.Error is not null)
                    return CreditoSimulacionVentaResultado.Invalido(rango.Error);
            }

            // ML2.1/ML6.1 — Contrato congelado: el plan de cuotas resuelto es la UNICA autoridad del
            // porcentaje, tambien en simulacion (misma regla que CreditoConfiguracionVentaService.
            // ResolverAsync). Cliente/Perfil/Producto ya no aportan ni sustituyen el porcentaje: las
            // tres ramas de abajo (Cliente/Producto-Mixto/Global) llaman exactamente al mismo
            // resolutor (ResolverTasaDelPlanOTasaGlobalAsync) contra el mismo plan de cuotas — Origen
            // (Cliente/Producto/Mixto/Global) es la disponibilidad de cantidades, NUNCA la fuente
            // financiera (ese fue el bug de ML6.1: se reportaba "Producto" como si el producto
            // aportara el %, cuando el % siempre sale del plan). Sin tabla de planes en absoluto
            // (legado RigeConfiguracionUnicaGlobal, solo dobles de test) rige la tasa unica global;
            // con tabla de planes, un porcentaje null es configuracion invalida y nunca cae a la
            // tasa global.
            if (request.MetodoCalculo == MetodoCalculoCredito.UsarCliente ||
                request.FuenteConfiguracion == FuenteConfiguracionCredito.PorCliente)
            {
                if (!clienteIdEfectivo.HasValue)
                    return CreditoSimulacionVentaResultado.Invalido(
                        "Se requiere un cliente para resolver la configuración de Crédito personal por cliente.");

                var (tasaCliente, errorCliente) = await ResolverTasaDelPlanOTasaGlobalAsync(planesVenta, request.Cuotas);
                if (errorCliente is not null)
                    return CreditoSimulacionVentaResultado.Invalido(errorCliente);

                tasaVal = tasaCliente!.Value;
            }
            else if (planesVenta.Origen == OrigenPlanesCredito.Producto || planesVenta.Origen == OrigenPlanesCredito.Mixto)
            {
                var (tasaProducto, errorProducto) = await ResolverTasaDelPlanOTasaGlobalAsync(planesVenta, request.Cuotas);
                if (errorProducto is not null)
                    return CreditoSimulacionVentaResultado.Invalido(errorProducto);

                tasaVal = tasaProducto!.Value;
            }
            else
            {
                var (tasaGlobalPlan, errorGlobal) = await ResolverTasaDelPlanOTasaGlobalAsync(planesVenta, request.Cuotas);
                if (errorGlobal is not null)
                    return CreditoSimulacionVentaResultado.Invalido(errorGlobal);

                tasaVal = tasaGlobalPlan!.Value;
            }

            // CSR-ML4: la lista viaja junto al mismo plan ya resuelto arriba (planesVenta) — no se
            // vuelve a consultar la configuracion. RigeConfiguracionUnicaGlobal (legado, sin tabla
            // de planes) nunca tiene exclusiones: BuscarPlan da null y cae al default vacio.
            cuotasSinRecargoVal = planesVenta.BuscarPlan(request.Cuotas)?.CuotasSinRecargo ?? Array.Empty<int>();
        }
        else
        {
            // ML8 — Sin venta y sin ProductoIds: no hay contexto de productos contra el cual
            // resolver un plan. Auditado (ML8/Fase 1): el único caller histórico de este fallback
            // era GET /Credito/Simular, retirado antes de ML8 (hoy solo redirige a Index, ya no
            // llama a este service); CreditoController.SimularPlanVenta siempre llega con ventaId
            // desde toda navegación real (Venta/Details, VentaController); CotizacionPagoCalculator
            // siempre pasa ProductoIds. Sin caller productivo, este fallback ya no resuelve nada:
            // devuelve inválido en vez de usar el escalar global legacy
            // (ConfiguracionPago.TasaInteresMensualCreditoPersonal) como si fuera el porcentaje
            // vigente (contrato ML2.1/ML6.1: el plan de cuotas es la única autoridad).
            return CreditoSimulacionVentaResultado.Invalido(
                "No hay contexto suficiente para calcular Crédito Personal: se requiere una venta " +
                "o los productos de la operación.");
        }

        if (tasaVal < 0)
            return CreditoSimulacionVentaResultado.Invalido("La tasa mensual no puede ser negativa.");

        var fecha = DateTime.TryParse(request.FechaPrimeraCuota, out var parsed)
            ? parsed
            : DateTime.Today.AddMonths(1);

        cancellationToken.ThrowIfCancellationRequested();

        var semaforo = _aptitudService != null
            ? await _aptitudService.GetSemaforoFinancieroAsync()
            : new SemaforoFinancieroViewModel();

        var plan = _financialService.SimularPlanCredito(
            totalVentaVal,
            anticipoVal,
            request.Cuotas,
            tasaVal,
            gastosVal,
            fecha,
            semaforo.RatioVerdeMax,
            semaforo.RatioAmarilloMax,
            cuotasSinRecargoVal);

        return CreditoSimulacionVentaResultado.Valido(new CreditoSimulacionVentaJson
        {
            totalVenta            = totalVentaVal,
            anticipo              = anticipoVal,
            montoFinanciado       = plan.MontoFinanciado,
            cuotaEstimada         = plan.CuotaEstimada,
            tasaAplicada          = plan.TasaAplicada,
            interesTotal          = plan.InteresTotal,
            totalAPagar           = plan.TotalAPagar,
            gastosAdministrativos = plan.GastosAdministrativos,
            totalPlan             = plan.TotalPlan,
            fechaPrimerPago       = plan.FechaPrimerPago.ToString("yyyy-MM-dd"),
            fuentePorcentaje      = FuentePorcentajePlan,
            cuotas                = plan.Cuotas.Select(c => new CreditoSimulacionCuotaJson
            {
                numeroCuota = c.NumeroCuota,
                capital     = c.Capital,
                interes     = c.Interes,
                total       = c.Total
            }).ToArray(),
            // CSR-ML6: metadata del plan (qué cuotas se configuraron sin recargo), no inferida del
            // vector. Viaja tal cual la resolvió el plan de cuotas más arriba (cuotasSinRecargoVal).
            cuotasSinRecargo      = cuotasSinRecargoVal,
            semaforoEstado        = plan.SemaforoEstado,
            semaforoMensaje       = plan.SemaforoMensaje,
            mostrarMsgIngreso     = plan.MostrarMsgIngreso,
            mostrarMsgAntiguedad  = plan.MostrarMsgAntiguedad
        });
    }

    /// <summary>
    /// ML2.1 — Contrato congelado: resuelve el porcentaje financiero desde el plan de cuotas
    /// (unica autoridad). Sin tabla de planes en absoluto (legado, solo dobles de test) usa la
    /// tasa unica global. Con tabla de planes, un porcentaje null en el plan es configuracion
    /// invalida: nunca cae a la tasa global.
    /// </summary>
    private async Task<(decimal? Tasa, string? Error)> ResolverTasaDelPlanOTasaGlobalAsync(
        PlanesCreditoPersonalResultado planesVenta,
        int cuotas)
    {
        if (planesVenta.RigeConfiguracionUnicaGlobal)
        {
            var tasaGlobal = await _configuracionPagoService!.ObtenerTasaInteresMensualCreditoPersonalAsync();
            return tasaGlobal is null ? (null, TasaGlobalNoConfigurada) : (tasaGlobal.Value, null);
        }

        var tasaPlan = planesVenta.BuscarPlan(cuotas)?.TasaMensual;
        return tasaPlan.HasValue
            ? (tasaPlan.Value, null)
            : (null, $"El plan de cuotas para {cuotas} cuotas no tiene un porcentaje financiero configurado.");
    }
}
