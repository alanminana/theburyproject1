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

    public CreditoConfiguracionVentaService(
        IConfiguracionPagoService configuracionPagoService,
        ILogger<CreditoConfiguracionVentaService> logger,
        ICreditoRangoProductoService? creditoRangoProductoService = null)
    {
        _configuracionPagoService = configuracionPagoService;
        _logger = logger;
        _creditoRangoProductoService = creditoRangoProductoService;
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

        if (!tasaMensual.HasValue || modelo.FuenteConfiguracion != FuenteConfiguracionCredito.Manual)
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

                tasaMensual = parametrosCliente.TasaMensual;
                gastosAdministrativos = modelo.GastosAdministrativos ?? parametrosCliente.GastosAdministrativos;
                _logger.LogInformation(
                    "CrÃ©dito {CreditoId}: Usando configuraciÃ³n del cliente {ClienteId} - Tasa: {Tasa}%, Gastos: ${Gastos}",
                    modelo.CreditoId, modelo.ClienteId, tasaMensual, gastosAdministrativos);
            }
            else
            {
                tasaMensual = tasaGlobal.Value;
                gastosAdministrativos = modelo.GastosAdministrativos ?? 0m;
                _logger.LogInformation(
                    "CrÃ©dito {CreditoId}: Usando configuraciÃ³n global - Tasa: {Tasa}%",
                    modelo.CreditoId, tasaMensual);
            }
        }
        else
        {
            if (modelo.MetodoCalculo == MetodoCalculoCredito.Manual &&
                (!tasaMensual.HasValue || tasaMensual.Value <= 0))
            {
                return CreditoConfiguracionVentaResultado.Invalido(
                    nameof(modelo.TasaMensual),
                    "La tasa de interÃ©s debe ser mayor a 0% en modo Manual.");
            }

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
                rangoEfectivo);
        }

        cuotasMinPermitidas = rangoEfectivo.Min;
        cuotasMaxPermitidas = rangoEfectivo.Max;

        if (modelo.CantidadCuotas < cuotasMinPermitidas || modelo.CantidadCuotas > cuotasMaxPermitidas)
        {
            return CreditoConfiguracionVentaResultado.Invalido(
                nameof(modelo.CantidadCuotas),
                $"La cantidad de cuotas debe estar entre {cuotasMinPermitidas} y {cuotasMaxPermitidas} " +
                $"segÃºn el mÃ©todo '{descripcionMetodo}'.",
                rangoEfectivo);
        }

        var comando = new ConfiguracionCreditoComando
        {
            CreditoId                   = modelo.CreditoId,
            VentaId                     = modelo.VentaId,
            Monto                       = modelo.Monto,
            Anticipo                    = anticipo,
            CantidadCuotas              = modelo.CantidadCuotas,
            TasaMensual                 = tasaMensual ?? 0,
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
            MaxCuotasBaseSnap           = rangoEfectivo.MaxBase
        };

        return CreditoConfiguracionVentaResultado.Valido(comando, rangoEfectivo);
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
