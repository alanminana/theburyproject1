using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services;

public sealed class CotizacionPagoCalculator : ICotizacionPagoCalculator
{
    private readonly IProductoService _productoService;
    private readonly IConfiguracionPagoGlobalQueryService _configuracionPagoGlobalQueryService;
    private readonly ICreditoSimulacionVentaService _creditoSimulacionVentaService;
    private readonly IProductoCreditoRestriccionService _productoCreditoRestriccionService;

    public CotizacionPagoCalculator(
        IProductoService productoService,
        IConfiguracionPagoGlobalQueryService configuracionPagoGlobalQueryService,
        ICreditoSimulacionVentaService creditoSimulacionVentaService,
        IProductoCreditoRestriccionService productoCreditoRestriccionService)
    {
        _productoService = productoService;
        _configuracionPagoGlobalQueryService = configuracionPagoGlobalQueryService;
        _creditoSimulacionVentaService = creditoSimulacionVentaService;
        _productoCreditoRestriccionService = productoCreditoRestriccionService;
    }

    public async Task<CotizacionSimulacionResultado> SimularAsync(
        CotizacionSimulacionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var fechaCalculo = request.FechaCotizacion ?? DateTime.Today;

        if (request.Productos.Count == 0)
        {
            return new CotizacionSimulacionResultado
            {
                Exitoso = false,
                FechaCalculo = fechaCalculo,
                Errores = { "Debe agregar al menos un producto para cotizar." }
            };
        }

        var errores = new List<string>();
        var advertencias = new List<string>();
        var productosResultado = new List<CotizacionProductoResultado>();
        var subtotal = 0m;
        var descuentoTotal = 0m;

        foreach (var producto in request.Productos)
        {
            if (producto.ProductoId <= 0)
            {
                errores.Add("Todos los productos de la cotizacion deben tener un ProductoId valido.");
                continue;
            }

            if (producto.Cantidad <= 0)
            {
                errores.Add("Todos los productos de la cotizacion deben tener una cantidad mayor a cero.");
                continue;
            }

            var precio = await _productoService.ObtenerPrecioVigenteParaVentaAsync(producto.ProductoId);
            if (precio == null)
            {
                errores.Add($"El producto {producto.ProductoId} no tiene precio vigente para venta.");
                continue;
            }

            var precioUnitario = RedondearMoneda(precio.PrecioVenta);
            var subtotalProductoBruto = RedondearMoneda(precioUnitario * producto.Cantidad);
            var descuentoProducto = CalcularDescuentoProducto(producto, subtotalProductoBruto, errores);
            var subtotalProducto = RedondearMoneda(subtotalProductoBruto - descuentoProducto);

            if (producto.PrecioManual.HasValue)
            {
                advertencias.Add(
                    $"Precio manual para producto {producto.ProductoId} no soportado en Cotizacion V1B; se uso precio vigente.");
            }

            subtotal += subtotalProductoBruto;
            descuentoTotal += descuentoProducto;

            productosResultado.Add(new CotizacionProductoResultado
            {
                ProductoId = precio.ProductoId,
                Codigo = precio.Codigo,
                Nombre = precio.Nombre,
                Cantidad = producto.Cantidad,
                PrecioUnitario = precioUnitario,
                Subtotal = subtotalProducto
            });
        }

        var descuentoGeneral = CalcularDescuentoGeneral(request, subtotal - descuentoTotal, errores);
        descuentoTotal = RedondearMoneda(descuentoTotal + descuentoGeneral);
        var totalBase = RedondearMoneda(subtotal - descuentoTotal);

        var opciones = new List<CotizacionMedioPagoResultado>();
        ConfiguracionPagoGlobalResultado? configuracion = null;

        if (errores.Count == 0)
        {
            configuracion = await _configuracionPagoGlobalQueryService.ObtenerActivaParaVentaAsync(cancellationToken);
            AgregarOpcionesBasicas(request, configuracion, totalBase, opciones);
            AgregarOpcionesTarjeta(request, configuracion, totalBase, opciones);
            AgregarMercadoPago(request, configuracion, totalBase, opciones, advertencias);
            await AgregarCreditoPersonalAsync(
                request,
                configuracion,
                fechaCalculo,
                totalBase,
                opciones,
                advertencias,
                cancellationToken);
        }

        return new CotizacionSimulacionResultado
        {
            Exitoso = errores.Count == 0,
            FechaCalculo = fechaCalculo,
            Errores = errores,
            Advertencias = advertencias,
            Productos = productosResultado,
            OpcionesPago = opciones,
            Subtotal = RedondearMoneda(subtotal),
            DescuentoTotal = descuentoTotal,
            TotalBase = totalBase
        };
    }

    private static decimal CalcularDescuentoProducto(
        CotizacionProductoRequest producto,
        decimal subtotalProducto,
        List<string> errores)
    {
        var descuento = 0m;

        if (producto.DescuentoPorcentaje.HasValue)
        {
            if (producto.DescuentoPorcentaje < 0m || producto.DescuentoPorcentaje > 100m)
            {
                errores.Add($"El descuento porcentual del producto {producto.ProductoId} debe estar entre 0 y 100.");
            }
            else
            {
                descuento += RedondearMoneda(subtotalProducto * producto.DescuentoPorcentaje.Value / 100m);
            }
        }

        if (producto.DescuentoImporte.HasValue)
        {
            if (producto.DescuentoImporte < 0m)
            {
                errores.Add($"El descuento por importe del producto {producto.ProductoId} no puede ser negativo.");
            }
            else
            {
                descuento += RedondearMoneda(producto.DescuentoImporte.Value);
            }
        }

        if (descuento > subtotalProducto)
        {
            errores.Add($"El descuento del producto {producto.ProductoId} no puede superar su subtotal.");
        }

        return descuento;
    }

    private static decimal CalcularDescuentoGeneral(
        CotizacionSimulacionRequest request,
        decimal baseDescuento,
        List<string> errores)
    {
        var descuento = 0m;

        if (request.DescuentoGeneralPorcentaje.HasValue)
        {
            if (request.DescuentoGeneralPorcentaje < 0m || request.DescuentoGeneralPorcentaje > 100m)
            {
                errores.Add("El descuento general porcentual debe estar entre 0 y 100.");
            }
            else
            {
                descuento += RedondearMoneda(baseDescuento * request.DescuentoGeneralPorcentaje.Value / 100m);
            }
        }

        if (request.DescuentoGeneralImporte.HasValue)
        {
            if (request.DescuentoGeneralImporte < 0m)
            {
                errores.Add("El descuento general por importe no puede ser negativo.");
            }
            else
            {
                descuento += RedondearMoneda(request.DescuentoGeneralImporte.Value);
            }
        }

        if (descuento > baseDescuento)
        {
            errores.Add("El descuento general no puede superar el total de productos.");
        }

        return descuento;
    }

    private static void AgregarOpcionesBasicas(
        CotizacionSimulacionRequest request,
        ConfiguracionPagoGlobalResultado configuracion,
        decimal totalBase,
        List<CotizacionMedioPagoResultado> opciones)
    {
        if (request.IncluirEfectivo)
        {
            opciones.Add(CrearOpcionUnPago(
                CotizacionMedioPagoTipo.Efectivo,
                "Efectivo",
                BuscarMedio(configuracion, TipoPago.Efectivo),
                totalBase,
                mostrarDisponibleSinConfiguracion: true));
        }

        if (request.IncluirTransferencia)
        {
            opciones.Add(CrearOpcionUnPago(
                CotizacionMedioPagoTipo.Transferencia,
                "Transferencia",
                BuscarMedio(configuracion, TipoPago.Transferencia),
                totalBase,
                mostrarDisponibleSinConfiguracion: true));
        }
    }

    private static void AgregarOpcionesTarjeta(
        CotizacionSimulacionRequest request,
        ConfiguracionPagoGlobalResultado configuracion,
        decimal totalBase,
        List<CotizacionMedioPagoResultado> opciones)
    {
        if (request.IncluirTarjetaCredito)
        {
            opciones.Add(CrearOpcionTarjeta(
                request,
                BuscarMedio(configuracion, TipoPago.TarjetaCredito),
                TipoTarjeta.Credito,
                CotizacionMedioPagoTipo.TarjetaCredito,
                "Tarjeta credito",
                totalBase));
        }

        if (request.IncluirTarjetaDebito)
        {
            opciones.Add(CrearOpcionTarjeta(
                request,
                BuscarMedio(configuracion, TipoPago.TarjetaDebito),
                TipoTarjeta.Debito,
                CotizacionMedioPagoTipo.TarjetaDebito,
                "Tarjeta debito",
                totalBase));
        }
    }

    private static void AgregarMercadoPago(
        CotizacionSimulacionRequest request,
        ConfiguracionPagoGlobalResultado configuracion,
        decimal totalBase,
        List<CotizacionMedioPagoResultado> opciones,
        List<string> advertencias)
    {
        if (!request.IncluirMercadoPago)
            return;

        var medio = BuscarMedio(configuracion, TipoPago.MercadoPago);
        if (medio == null)
        {
            advertencias.Add("MercadoPago pendiente de mapeo en configuracion de pagos.");
            return;
        }

        opciones.Add(CrearOpcionUnPago(
            CotizacionMedioPagoTipo.MercadoPago,
            medio.NombreVisible,
            medio,
            totalBase,
            mostrarDisponibleSinConfiguracion: false));
    }

    private async Task AgregarCreditoPersonalAsync(
        CotizacionSimulacionRequest request,
        ConfiguracionPagoGlobalResultado configuracion,
        DateTime fechaCalculo,
        decimal totalBase,
        List<CotizacionMedioPagoResultado> opciones,
        List<string> advertencias,
        CancellationToken cancellationToken)
    {
        if (!request.IncluirCreditoPersonal)
            return;

        if (!request.ClienteId.HasValue)
        {
            const string advertenciaSinCliente = "Credito personal requiere cliente y evaluacion antes de confirmar.";
            advertencias.Add(advertenciaSinCliente);

            opciones.Add(new CotizacionMedioPagoResultado
            {
                MedioPago = CotizacionMedioPagoTipo.CreditoPersonal,
                NombreMedioPago = "Credito personal",
                Disponible = false,
                Estado = CotizacionOpcionPagoEstado.RequiereCliente,
                MotivoNoDisponible = "Credito personal requiere cliente para evaluacion."
            });
            return;
        }

        var restricciones = await _productoCreditoRestriccionService.ResolverAsync(
            request.Productos.Select(p => p.ProductoId),
            cancellationToken);

        if (!restricciones.Permitido)
        {
            var productos = string.Join(", ", restricciones.ProductoIdsBloqueantes.Select(id => $"#{id}"));
            var motivo = $"Credito personal bloqueado por producto(s): {productos}.";
            advertencias.Add(motivo);

            opciones.Add(new CotizacionMedioPagoResultado
            {
                MedioPago = CotizacionMedioPagoTipo.CreditoPersonal,
                NombreMedioPago = "Credito personal",
                Disponible = false,
                Estado = CotizacionOpcionPagoEstado.BloqueadoPorProducto,
                MotivoNoDisponible = motivo
            });
            return;
        }

        var medio = BuscarMedio(configuracion, TipoPago.CreditoPersonal);
        if (medio == null)
        {
            const string motivo = "No hay configuracion activa para credito personal.";
            advertencias.Add(motivo);
            opciones.Add(CrearCreditoRequiereEvaluacion(motivo));
            return;
        }

        var cuotasSolicitadas = request.CuotasSolicitadas?
            .Where(c => c > 0)
            .ToHashSet();

        var planesConfigurados = medio.Planes
            .Where(p => p.EsPlanGeneral)
            .Where(p => cuotasSolicitadas == null || cuotasSolicitadas.Contains(p.CantidadCuotas))
            .Where(p => !restricciones.MaxCuotasCredito.HasValue || p.CantidadCuotas <= restricciones.MaxCuotasCredito.Value)
            .OrderBy(p => p.Orden)
            .ThenBy(p => p.CantidadCuotas)
            .ToList();

        if (planesConfigurados.Count == 0)
        {
            var motivo = restricciones.MaxCuotasCredito.HasValue
                ? $"No hay planes activos de credito personal dentro del limite por producto de {restricciones.MaxCuotasCredito.Value} cuotas."
                : "No hay planes activos de credito personal para simular.";
            advertencias.Add(motivo);
            opciones.Add(CrearCreditoRequiereEvaluacion(motivo));
            return;
        }

        var planes = new List<CotizacionPlanPagoResultado>();
        var fechaPrimeraCuota = fechaCalculo.AddMonths(1).ToString("yyyy-MM-dd");

        foreach (var planConfigurado in planesConfigurados)
        {
            var simulacion = await _creditoSimulacionVentaService.SimularAsync(
                new CreditoSimulacionVentaRequest
                {
                    TotalVenta = totalBase,
                    Anticipo = 0m,
                    Cuotas = planConfigurado.CantidadCuotas,
                    GastosAdministrativos = 0m,
                    FechaPrimeraCuota = fechaPrimeraCuota
                },
                cancellationToken);

            if (!simulacion.EsValido || simulacion.Plan is null)
            {
                advertencias.Add(simulacion.Error?.error ?? "No se pudo simular credito personal.");
                continue;
            }

            planes.Add(CrearPlanCreditoPersonal(planConfigurado, simulacion.Plan, restricciones));
        }

        if (planes.Count == 0)
        {
            const string motivo = "Credito personal requiere evaluacion: no se pudo calcular un plan disponible.";
            opciones.Add(CrearCreditoRequiereEvaluacion(motivo));
            return;
        }

        if (restricciones.MaxCuotasCredito.HasValue)
        {
            advertencias.Add($"Credito personal limitado por producto hasta {restricciones.MaxCuotasCredito.Value} cuotas.");
        }

        opciones.Add(new CotizacionMedioPagoResultado
        {
            MedioPago = CotizacionMedioPagoTipo.CreditoPersonal,
            NombreMedioPago = medio.NombreVisible,
            Disponible = true,
            Estado = CotizacionOpcionPagoEstado.Disponible,
            Planes = planes
        });
    }

    private static CotizacionMedioPagoResultado CrearOpcionUnPago(
        CotizacionMedioPagoTipo tipo,
        string nombre,
        MedioPagoGlobalDto? medio,
        decimal totalBase,
        bool mostrarDisponibleSinConfiguracion)
    {
        if (medio == null && !mostrarDisponibleSinConfiguracion)
        {
            return CrearNoDisponible(tipo, nombre, "No hay configuracion activa para este medio de pago.");
        }

        var plan = medio?.Planes.FirstOrDefault(p => p.EsPlanGeneral && p.CantidadCuotas == 1)
            ?? medio?.Planes.FirstOrDefault(p => p.EsPlanGeneral);
        var ajuste = plan?.AjustePorcentaje ?? 0m;
        var calculo = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
        {
            BaseVenta = totalBase,
            PorcentajeAjuste = ajuste,
            CantidadCuotas = 1,
            MedioActivo = medio?.Activo ?? true,
            PlanActivo = plan?.Activo
        });

        if (!calculo.EsValido)
        {
            return CrearNoDisponible(tipo, nombre, calculo.Mensaje ?? "El medio de pago no esta disponible.");
        }

        return new CotizacionMedioPagoResultado
        {
            MedioPago = tipo,
            NombreMedioPago = medio?.NombreVisible ?? nombre,
            Disponible = true,
            Estado = CotizacionOpcionPagoEstado.Disponible,
            Planes =
            {
                CrearPlanResultado(plan?.Etiqueta ?? "1 pago", calculo)
            }
        };
    }

    private static CotizacionMedioPagoResultado CrearOpcionTarjeta(
        CotizacionSimulacionRequest request,
        MedioPagoGlobalDto? medio,
        TipoTarjeta tipoTarjeta,
        CotizacionMedioPagoTipo tipo,
        string nombre,
        decimal totalBase)
    {
        if (medio == null)
            return CrearNoDisponible(tipo, nombre, "No hay configuracion activa para este medio de pago.");

        var tarjetas = medio.Tarjetas
            .Where(t => t.TipoTarjeta == tipoTarjeta)
            .Where(t => !request.ConfiguracionTarjetaId.HasValue || t.Id == request.ConfiguracionTarjetaId.Value)
            .ToList();

        if (request.ConfiguracionTarjetaId.HasValue && tarjetas.Count == 0)
        {
            return CrearNoDisponible(tipo, nombre, "La tarjeta solicitada no existe, esta inactiva o no corresponde al medio de pago.");
        }

        var cuotasSolicitadas = request.CuotasSolicitadas?
            .Where(c => c > 0)
            .ToHashSet();

        var planes = new List<CotizacionPlanPagoResultado>();
        foreach (var tarjeta in tarjetas)
        {
            var planesTarjeta = medio.Planes
                .Where(p => p.ConfiguracionTarjetaId == tarjeta.Id || p.EsPlanGeneral)
                .Where(p => cuotasSolicitadas == null || cuotasSolicitadas.Contains(p.CantidadCuotas))
                .OrderBy(p => p.Orden)
                .ThenBy(p => p.CantidadCuotas)
                .ToList();

            foreach (var plan in planesTarjeta)
            {
                if (tarjeta.CantidadMaximaCuotas.HasValue && plan.CantidadCuotas > tarjeta.CantidadMaximaCuotas.Value)
                    continue;

                if (!tarjeta.PermiteCuotas && plan.CantidadCuotas > 1)
                    continue;

                var calculo = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
                {
                    BaseVenta = totalBase,
                    PorcentajeAjuste = plan.AjustePorcentaje,
                    CantidadCuotas = plan.CantidadCuotas,
                    MedioActivo = medio.Activo,
                    TarjetaActiva = tarjeta.Activa,
                    PlanActivo = plan.Activo
                });

                if (calculo.EsValido)
                {
                    var etiqueta = string.IsNullOrWhiteSpace(plan.Etiqueta)
                        ? $"{tarjeta.Nombre} - {plan.CantidadCuotas} pago(s)"
                        : $"{tarjeta.Nombre} - {plan.Etiqueta}";
                    planes.Add(CrearPlanResultado(etiqueta, calculo));
                }
            }
        }

        if (planes.Count == 0)
        {
            return CrearNoDisponible(tipo, medio.NombreVisible, "No hay planes activos disponibles para la tarjeta solicitada.");
        }

        return new CotizacionMedioPagoResultado
        {
            MedioPago = tipo,
            NombreMedioPago = medio.NombreVisible,
            Disponible = true,
            Estado = CotizacionOpcionPagoEstado.Disponible,
            Planes = planes
        };
    }

    private static CotizacionPlanPagoResultado CrearPlanResultado(
        string plan,
        AjustePagoGlobalResultado calculo) =>
        new()
        {
            Plan = plan,
            CantidadCuotas = calculo.CantidadCuotas,
            RecargoPorcentaje = calculo.PorcentajeAjuste > 0m ? calculo.PorcentajeAjuste : 0m,
            DescuentoPorcentaje = calculo.PorcentajeAjuste < 0m ? Math.Abs(calculo.PorcentajeAjuste) : 0m,
            InteresPorcentaje = calculo.PorcentajeAjuste > 0m ? calculo.PorcentajeAjuste : 0m,
            Total = calculo.TotalFinal,
            ValorCuota = calculo.ValorCuota,
            Recomendado = calculo.CantidadCuotas == 1 && calculo.PorcentajeAjuste <= 0m
        };

    private static CotizacionMedioPagoResultado CrearNoDisponible(
        CotizacionMedioPagoTipo tipo,
        string nombre,
        string motivo) =>
        new()
        {
            MedioPago = tipo,
            NombreMedioPago = nombre,
            Disponible = false,
            Estado = CotizacionOpcionPagoEstado.NoDisponible,
            MotivoNoDisponible = motivo
        };

    private static CotizacionMedioPagoResultado CrearCreditoRequiereEvaluacion(string motivo) =>
        new()
        {
            MedioPago = CotizacionMedioPagoTipo.CreditoPersonal,
            NombreMedioPago = "Credito personal",
            Disponible = false,
            Estado = CotizacionOpcionPagoEstado.RequiereEvaluacion,
            MotivoNoDisponible = motivo
        };

    private static CotizacionPlanPagoResultado CrearPlanCreditoPersonal(
        PlanPagoGlobalConfiguradoDto planConfigurado,
        CreditoSimulacionVentaJson plan,
        ProductoCreditoRestriccionResultado restricciones)
    {
        var advertencias = new List<string>();
        if (restricciones.MaxCuotasCredito.HasValue)
        {
            advertencias.Add($"Limite por producto: hasta {restricciones.MaxCuotasCredito.Value} cuotas.");
        }

        return new CotizacionPlanPagoResultado
        {
            Plan = string.IsNullOrWhiteSpace(planConfigurado.Etiqueta)
                ? $"{planConfigurado.CantidadCuotas} cuota(s)"
                : planConfigurado.Etiqueta!,
            CantidadCuotas = planConfigurado.CantidadCuotas,
            TasaMensual = plan.tasaAplicada,
            InteresPorcentaje = plan.tasaAplicada,
            CostoFinancieroTotal = plan.interesTotal,
            TipoCalculo = "CreditoPersonalReadOnly",
            Total = RedondearMoneda(plan.totalPlan),
            ValorCuota = RedondearMoneda(plan.cuotaEstimada),
            Advertencias = advertencias
        };
    }

    private static MedioPagoGlobalDto? BuscarMedio(
        ConfiguracionPagoGlobalResultado configuracion,
        TipoPago tipoPago) =>
        configuracion.Medios.FirstOrDefault(m => m.TipoPago == tipoPago);

    private static decimal RedondearMoneda(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
