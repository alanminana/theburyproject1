using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services;

public sealed class CreditoConfiguracionVentaService : ICreditoConfiguracionVentaService
{
    private const string TasaGlobalNoConfigurada =
        "La tasa de interÃ©s de CrÃ©dito Personal no estÃ¡ configurada. " +
        "Configure el valor en AdministraciÃ³n â†’ Tipos de Pago.";

    private readonly IConfiguracionPagoService _configuracionPagoService;
    private readonly ICreditoRangoProductoService? _creditoRangoProductoService;
    private readonly ILogger<CreditoConfiguracionVentaService> _logger;
    private readonly IRelojComercial _reloj;

    // Medios de pago válidos para el cobro inmediato de la primera cuota (idénticos a los que
    // acepta CreditoService.NormalizarMedioPago). Se validan acá para no persistir un medio inválido.
    private static readonly HashSet<string> MediosPagoPrimeraCuota =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Efectivo", "Transferencia", "Tarjeta Débito", "Tarjeta Crédito", "Cheque"
        };

    public CreditoConfiguracionVentaService(
        IConfiguracionPagoService configuracionPagoService,
        ILogger<CreditoConfiguracionVentaService> logger,
        ICreditoRangoProductoService? creditoRangoProductoService = null,
        IRelojComercial? reloj = null)
    {
        _configuracionPagoService = configuracionPagoService;
        _logger = logger;
        _creditoRangoProductoService = creditoRangoProductoService;
        _reloj = reloj ?? RelojComercial.Sistema;
    }

    public async Task<CreditoConfiguracionVentaResultado> ResolverAsync(
        ConfiguracionCreditoVentaViewModel modelo,
        VentaViewModel? venta,
        CancellationToken cancellationToken = default)
    {
        if (!modelo.MetodoCalculo.HasValue)
        {
            return CreditoConfiguracionVentaResultado.Invalido(
                nameof(modelo.MetodoCalculo),
                "Debe seleccionar un mÃ©todo de cÃ¡lculo.");
        }

        decimal? tasaGlobal = null;
        ParametrosCreditoCliente? parametrosCliente = null;

        if (modelo.MetodoCalculo == MetodoCalculoCredito.UsarCliente ||
            !modelo.TasaMensual.HasValue ||
            modelo.FuenteConfiguracion != FuenteConfiguracionCredito.Manual)
        {
            tasaGlobal = await _configuracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync();
            if (tasaGlobal == null)
            {
                return CreditoConfiguracionVentaResultado.Invalido(string.Empty, TasaGlobalNoConfigurada);
            }
        }

        if (modelo.MetodoCalculo == MetodoCalculoCredito.UsarCliente)
        {
            parametrosCliente = await _configuracionPagoService.ObtenerParametrosCreditoClienteAsync(
                modelo.ClienteId,
                tasaGlobal!.Value);

            if (!parametrosCliente.TieneConfiguracionPersonalizada)
            {
                return CreditoConfiguracionVentaResultado.Invalido(
                    nameof(modelo.MetodoCalculo),
                    "El cliente no tiene configuraciÃ³n de crÃ©dito personal. " +
                    "Configure el cliente con valores personalizados o seleccione otro mÃ©todo.");
            }
        }

        var anticipo = modelo.Anticipo ?? 0m;
        var gastosAdministrativos = modelo.GastosAdministrativos ?? 0m;
        var tasaMensual = modelo.TasaMensual;

        // Autoridad del monto: con venta asociada, su total real (leído de BD por el controller)
        // manda siempre sobre modelo.Monto, que llega desde un hidden manipulable en el navegador.
        // Sin venta (crédito standalone) se preserva el único escenario legítimo demostrado hoy:
        // el monto lo define el formulario.
        var montoAutoritativo = venta is not null ? venta.Total : modelo.Monto;

        if (anticipo > montoAutoritativo)
        {
            return CreditoConfiguracionVentaResultado.Invalido(
                nameof(modelo.Anticipo),
                "El anticipo no puede superar el total real de la venta.");
        }

        // Planes de la venta: resolucion canonica unica (personalizada del producto reemplaza a
        // la global; con varios productos rige la interseccion). Se resuelve para TODOS los
        // metodos de calculo porque la restriccion es del producto, no del origen de la tasa.
        var planesVenta = await _configuracionPagoService.ResolverPlanesCreditoPersonalAsync(
            venta?.Detalles?.Select(d => d.ProductoId) ?? Enumerable.Empty<int>());

        if (!planesVenta.EsValido)
        {
            return CreditoConfiguracionVentaResultado.Invalido(
                nameof(modelo.CantidadCuotas),
                planesVenta.MensajeRechazo!,
                motivo: MotivoRechazoConfiguracionCredito.Conflicto);
        }

        // La fuente Manual solo es legitima con el metodo Manual: de lo contrario un POST podria
        // declarar Manual para que se acepte la tasa que envia el navegador.
        var fuenteManualValida = modelo.FuenteConfiguracion == FuenteConfiguracionCredito.Manual &&
                                 modelo.MetodoCalculo == MetodoCalculoCredito.Manual;

        if (!tasaMensual.HasValue || !fuenteManualValida)
        {
            if (tasaGlobal == null)
            {
                tasaGlobal = await _configuracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync();
                if (tasaGlobal == null)
                {
                    return CreditoConfiguracionVentaResultado.Invalido(string.Empty, TasaGlobalNoConfigurada);
                }
            }

            if (modelo.FuenteConfiguracion == FuenteConfiguracionCredito.PorCliente)
            {
                parametrosCliente ??= await _configuracionPagoService.ObtenerParametrosCreditoClienteAsync(
                    modelo.ClienteId,
                    tasaGlobal.Value);

                // ML2: el cliente ya no es autoridad del porcentaje (Fase 4 del contrato congelado).
                // Sigue aportando el resto de los parametros no financieros (gastos administrativos);
                // el porcentaje sale siempre del plan de cuotas resuelto, igual que en la rama global.
                tasaMensual = planesVenta.RigeConfiguracionUnicaGlobal
                    ? tasaGlobal.Value
                    : planesVenta.BuscarPlan(modelo.CantidadCuotas)?.TasaMensual;
                gastosAdministrativos = modelo.GastosAdministrativos ?? parametrosCliente.GastosAdministrativos;
                _logger.LogInformation(
                    "CrÃ©dito {CreditoId}: Usando configuraciÃ³n del cliente {ClienteId} - Tasa: {Tasa}%, Gastos: ${Gastos}",
                    modelo.CreditoId, modelo.ClienteId, tasaMensual, gastosAdministrativos);
            }
            else
            {
                // Sin tabla de planes rige la tasa unica global (compatibilidad con dobles de test;
                // el resolutor productivo no emite este caso). Con planes, la tasa la decide el
                // servidor a partir del plan resuelto: nunca el navegador.
                if (planesVenta.RigeConfiguracionUnicaGlobal)
                {
                    tasaMensual = tasaGlobal.Value;
                }
                else
                {
                    // ML2: tasa null en el plan (o plan inexistente para esta cantidad) ya no cae a
                    // la tasa global unica: "null" es configuracion invalida, nunca "heredar".
                    tasaMensual = planesVenta.BuscarPlan(modelo.CantidadCuotas)?.TasaMensual;
                }

                gastosAdministrativos = modelo.GastosAdministrativos ?? 0m;
                _logger.LogInformation(
                    "CrÃ©dito {CreditoId}: Usando configuraciÃ³n global - Tasa: {Tasa}%",
                    modelo.CreditoId, tasaMensual);
            }
        }
        else
        {
            if (modelo.MetodoCalculo == MetodoCalculoCredito.Manual &&
                (!tasaMensual.HasValue || tasaMensual.Value < 0))
            {
                return CreditoConfiguracionVentaResultado.Invalido(
                    nameof(modelo.TasaMensual),
                    "La tasa de interÃ©s no puede ser negativa en modo Manual.");
            }

            // ML2.1 — Contrato congelado (Fase 4): el plan de cuotas es la unica autoridad del
            // porcentaje, tambien en modo Manual. Sin tabla de planes en absoluto (legado, solo
            // dobles de test) se conserva la tasa que cargo el operador: no hay otra autoridad a
            // la que recurrir. Con tabla de planes, el porcentaje SIEMPRE sale del plan — incluido
            // cuando es null (plan sin porcentaje explicito = invalido): el valor manual nunca
            // actua como rescate, se descarta aunque el plan no fije nada.
            tasaMensual = planesVenta.RigeConfiguracionUnicaGlobal
                ? tasaMensual
                : planesVenta.BuscarPlan(modelo.CantidadCuotas)?.TasaMensual;

            _logger.LogInformation(
                "CrÃ©dito {CreditoId}: ConfiguraciÃ³n manual - Tasa: {Tasa}%, Gastos: ${Gastos}",
                modelo.CreditoId, tasaMensual, gastosAdministrativos);
        }

        var (cuotasMinPermitidas, cuotasMaxPermitidas, descripcionMetodo, perfilNombre) =
            await _configuracionPagoService.ResolverRangoCuotasAsync(
                modelo.MetodoCalculo.Value,
                modelo.PerfilCreditoSeleccionadoId,
                modelo.ClienteId);

        var rangoEfectivo = await ResolverRangoCreditoProductoAsync(
            venta,
            cuotasMinPermitidas,
            cuotasMaxPermitidas,
            cancellationToken);

        if (rangoEfectivo.Error is not null)
        {
            return CreditoConfiguracionVentaResultado.Invalido(
                nameof(modelo.CantidadCuotas),
                rangoEfectivo.Error,
                rangoEfectivo,
                MotivoRechazoConfiguracionCredito.Conflicto);
        }

        cuotasMinPermitidas = rangoEfectivo.Min;
        cuotasMaxPermitidas = rangoEfectivo.Max;

        // Gate de planes: la cantidad debe pertenecer a la interseccion real de los productos.
        // Se aplica a todos los metodos de calculo, incluido el Manual, y ANTES del rango min/max:
        // cuando rige una tabla de planes el conjunto discreto manda sobre el intervalo continuo,
        // y el rechazo es siempre "cuota no disponible para estos productos" (conflicto).
        if (!planesVenta.RigeConfiguracionUnicaGlobal &&
            planesVenta.BuscarPlan(modelo.CantidadCuotas) is null)
        {
            var habilitadas = string.Join(", ", planesVenta.Planes.Select(p => p.CantidadCuotas));
            return CreditoConfiguracionVentaResultado.Invalido(
                nameof(modelo.CantidadCuotas),
                $"La cantidad de cuotas {modelo.CantidadCuotas} no esta habilitada para Credito personal " +
                $"con los productos de esta venta. Cantidades disponibles: {habilitadas}.",
                rangoEfectivo,
                MotivoRechazoConfiguracionCredito.Conflicto);
        }

        // ML2.1 — Contrato congelado (Fase 5): un plan activo sin porcentaje explicito es
        // configuracion invalida, nunca "0% silencioso". Se valida el plan en si (no la variable
        // tasaMensual ya resuelta arriba) para cubrir los tres metodos de calculo por igual.
        if (!planesVenta.RigeConfiguracionUnicaGlobal &&
            planesVenta.BuscarPlan(modelo.CantidadCuotas)!.TasaMensual is null)
        {
            return CreditoConfiguracionVentaResultado.Invalido(
                nameof(modelo.CantidadCuotas),
                $"El plan de cuotas para {modelo.CantidadCuotas} cuotas no tiene un porcentaje financiero " +
                "configurado. Configure el porcentaje en Administracion -> Credito Personal antes de " +
                "financiar con esta cantidad.",
                rangoEfectivo,
                MotivoRechazoConfiguracionCredito.Conflicto);
        }

        if (modelo.CantidadCuotas < cuotasMinPermitidas || modelo.CantidadCuotas > cuotasMaxPermitidas)
        {
            // Si el tope lo impuso un producto es un conflicto con la venta; si es el rango propio
            // del metodo de calculo, es un dato mal elegido en el formulario.
            return CreditoConfiguracionVentaResultado.Invalido(
                nameof(modelo.CantidadCuotas),
                $"La cantidad de cuotas debe estar entre {cuotasMinPermitidas} y {cuotasMaxPermitidas} " +
                $"segÃºn el mÃ©todo '{descripcionMetodo}'.",
                rangoEfectivo,
                rangoEfectivo.ProductoIdRestrictivo.HasValue
                    ? MotivoRechazoConfiguracionCredito.Conflicto
                    : MotivoRechazoConfiguracionCredito.SolicitudInvalida);
        }

        // F2: la decisión de cobrar la primera cuota solo es legítima si la 1ª cuota vence en la
        // fecha comercial actual (Argentina) y el medio es válido. El servidor NO confía en el
        // payload: si no vence hoy o el medio es inválido, la opción se descarta (no se persiste).
        var (cobrarPrimeraCuota, medioPrimeraCuota) = ResolverDecisionPrimeraCuota(modelo);

        var comando = new ConfiguracionCreditoComando
        {
            CreditoId                   = modelo.CreditoId,
            VentaId                     = modelo.VentaId,
            Monto                       = montoAutoritativo,
            Anticipo                    = anticipo,
            CantidadCuotas              = modelo.CantidadCuotas,
            // ML2.1: a esta altura ya se rechazo toda combinacion con porcentaje invalido (Fase 5,
            // gate arriba). Nunca coalesce a 0 en silencio: si esta invariante se rompiera, el
            // comando debe fallar ruidosamente en vez de persistir un 0% no configurado.
            TasaMensual                 = tasaMensual ?? throw new InvalidOperationException(
                "TasaMensual no deberia ser null luego de validar el plan de cuotas."),
            GastosAdministrativos       = gastosAdministrativos,
            FechaPrimeraCuota           = modelo.FechaPrimeraCuota,
            MetodoCalculo               = modelo.MetodoCalculo.Value,
            FuenteConfiguracion         = modelo.FuenteConfiguracion,
            PerfilCreditoAplicadoId     = modelo.PerfilCreditoSeleccionadoId,
            PerfilCreditoAplicadoNombre = perfilNombre,
            CuotasMinPermitidas         = cuotasMinPermitidas,
            CuotasMaxPermitidas         = cuotasMaxPermitidas,
            FuenteRestriccionCuotasSnap = rangoEfectivo.ProductoIdRestrictivo.HasValue ? "Producto" : "Global",
            ProductoIdRestrictivoSnap   = rangoEfectivo.ProductoIdRestrictivo,
            MaxCuotasBaseSnap           = rangoEfectivo.MaxBase,
            CobrarPrimeraCuota          = cobrarPrimeraCuota,
            MedioPagoPrimeraCuota       = medioPrimeraCuota
        };

        return CreditoConfiguracionVentaResultado.Valido(comando, rangoEfectivo);
    }

    /// <summary>
    /// Valida la decisión de cobro inmediato de la primera cuota contra la fecha comercial actual
    /// (Argentina) y la lista de medios permitidos. Server-authoritative: el payload no puede forzar
    /// la opción si la primera cuota no vence hoy o el medio no es válido.
    /// </summary>
    private (bool Cobrar, string? Medio) ResolverDecisionPrimeraCuota(ConfiguracionCreditoVentaViewModel modelo)
    {
        if (!modelo.CobrarPrimeraCuota)
            return (false, null);

        if (!modelo.FechaPrimeraCuota.HasValue ||
            DateOnly.FromDateTime(modelo.FechaPrimeraCuota.Value) != _reloj.HoyComercial)
            return (false, null);

        var medio = modelo.MedioPagoPrimeraCuota?.Trim();
        if (string.IsNullOrWhiteSpace(medio) || !MediosPagoPrimeraCuota.Contains(medio))
            return (false, null);

        // Normaliza a la forma canónica del diccionario (respeta acentos/mayúsculas esperados).
        var canonico = MediosPagoPrimeraCuota.First(m => string.Equals(m, medio, StringComparison.OrdinalIgnoreCase));
        return (true, canonico);
    }

    private async Task<CreditoRangoProductoResultado> ResolverRangoCreditoProductoAsync(
        VentaViewModel? venta,
        int minBase,
        int maxBase,
        CancellationToken cancellationToken)
    {
        if (venta is null || _creditoRangoProductoService is null)
        {
            return new CreditoRangoProductoResultado(minBase, maxBase, maxBase, null, null, null, null, null);
        }

        return await _creditoRangoProductoService.ResolverAsync(
            venta,
            TipoPago.CreditoPersonal,
            minBase,
            maxBase,
            cancellationToken);
    }
}
