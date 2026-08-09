using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services;

public sealed class CotizacionPagoCalculator : ICotizacionPagoCalculator
{
    private readonly IProductoService _productoService;
    private readonly IConfiguracionPagoGlobalQueryService _configuracionPagoGlobalQueryService;
    private readonly ICreditoSimulacionVentaService _creditoSimulacionVentaService;
    private readonly IProductoCreditoRestriccionService _productoCreditoRestriccionService;
    private readonly IConfiguracionPagoService _configuracionPagoService;

    public CotizacionPagoCalculator(
        IProductoService productoService,
        IConfiguracionPagoGlobalQueryService configuracionPagoGlobalQueryService,
        ICreditoSimulacionVentaService creditoSimulacionVentaService,
        IProductoCreditoRestriccionService productoCreditoRestriccionService,
        IConfiguracionPagoService configuracionPagoService)
    {
        _productoService = productoService;
        _configuracionPagoGlobalQueryService = configuracionPagoGlobalQueryService;
        _creditoSimulacionVentaService = creditoSimulacionVentaService;
        _productoCreditoRestriccionService = productoCreditoRestriccionService;
        _configuracionPagoService = configuracionPagoService;
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
        DateTime fechaCalculo,
        decimal totalBase,
        List<CotizacionMedioPagoResultado> opciones,
        List<string> advertencias,
        CancellationToken cancellationToken)
    {
        if (!request.IncluirCreditoPersonal)
            return;

        // Autoridad del anticipo: se valida acá (server-side, contra el totalBase ya resuelto de
        // precios reales) antes de tocar cliente/planes. Se aplica antes del recargo dentro de
        // FinancialCalculationService.SimularPlanCredito (via CreditoSimulacionVentaService):
        // este calculator nunca resta el anticipo por su cuenta.
        var anticipoVal = request.Anticipo ?? 0m;
        if (anticipoVal < 0m || anticipoVal > totalBase)
        {
            var motivoAnticipo = anticipoVal < 0m
                ? "El anticipo no puede ser negativo."
                : "El anticipo no puede superar el total cotizado.";
            advertencias.Add(motivoAnticipo);
            opciones.Add(new CotizacionMedioPagoResultado
            {
                MedioPago = CotizacionMedioPagoTipo.CreditoPersonal,
                NombreMedioPago = "Credito personal",
                Disponible = false,
                Estado = CotizacionOpcionPagoEstado.AnticipoInvalido,
                MotivoNoDisponible = motivoAnticipo
            });
            return;
        }

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

        var tasaGlobal = await _configuracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync();
        if (tasaGlobal == null)
        {
            const string motivo = "La tasa de interes de Credito personal no esta configurada.";
            advertencias.Add(motivo);
            opciones.Add(CrearCreditoRequiereEvaluacion(motivo));
            return;
        }

        var parametros = await _configuracionPagoService.ObtenerParametrosCreditoClienteAsync(
            request.ClienteId.Value,
            tasaGlobal.Value);

        var cuotasMax = restricciones.MaxCuotasCredito.HasValue
            ? Math.Min(parametros.CuotasMaximas, restricciones.MaxCuotasCredito.Value)
            : parametros.CuotasMaximas;

        var cuotasSolicitadas = request.CuotasSolicitadas?
            .Where(c => c > 0)
            .ToHashSet();

        // Disponibilidad de cantidades: SIEMPRE desde los planes activos (resolucion canonica unica).
        // Sin planes no hay cuotas: no existe fallback a un rango.
        var planesVenta = await _configuracionPagoService.ResolverPlanesCreditoPersonalAsync(
            request.Productos.Select(p => p.ProductoId));

        // Sin planes globales activos o interseccion vacia entre productos: no financiable. No se
        // simula con un rango, que ofreceria cuotas que ningun plan habilita.
        if (!planesVenta.EsValido)
        {
            advertencias.Add(planesVenta.MensajeRechazo!);
            opciones.Add(new CotizacionMedioPagoResultado
            {
                MedioPago = CotizacionMedioPagoTipo.CreditoPersonal,
                NombreMedioPago = "Credito personal",
                Disponible = false,
                Estado = CotizacionOpcionPagoEstado.BloqueadoPorProducto,
                MotivoNoDisponible = planesVenta.MensajeRechazo
            });
            return;
        }

        // El cliente/perfil/producto solo REDUCEN (cap maximo, ya consolidado en cuotasMax): nunca
        // crean cantidades. Las cantidades ofrecidas son un subconjunto de los planes activos.
        var cuotasBase = planesVenta.Planes
            .Select(c => c.CantidadCuotas)
            .Where(c => c <= cuotasMax)
            .ToList();

        var cuotasCandidatas = cuotasBase
            .Where(c => cuotasSolicitadas == null || cuotasSolicitadas.Contains(c))
            .ToList();

        if (cuotasCandidatas.Count == 0)
        {
            var motivo = restricciones.MaxCuotasCredito.HasValue
                ? $"No hay cuotas de credito personal disponibles dentro del limite por producto de {restricciones.MaxCuotasCredito.Value} cuotas."
                : "No hay cuotas de credito personal configuradas para simular.";
            advertencias.Add(motivo);
            opciones.Add(CrearCreditoRequiereEvaluacion(motivo));
            return;
        }

        var planes = new List<CotizacionPlanPagoResultado>();
        var fechaPrimeraCuota = fechaCalculo.AddMonths(1).ToString("yyyy-MM-dd");
        var productoIdsVenta = request.Productos.Select(p => p.ProductoId).ToList();

        foreach (var cuotas in cuotasCandidatas)
        {
            // Resolución de tasa/plan 100% delegada a CreditoSimulacionVentaService (misma
            // precedencia plan/cliente/global que usa Configurar Venta): este calculator no decide
            // ninguna tasa por su cuenta, solo pasa ProductoIds/ClienteId y la Fuente ya resuelta
            // para el cliente (mismo default que usa el GET de ConfigurarVenta).
            var simulacion = await _creditoSimulacionVentaService.SimularAsync(
                new CreditoSimulacionVentaRequest
                {
                    TotalVenta = totalBase,
                    Anticipo = anticipoVal,
                    Cuotas = cuotas,
                    ClienteId = request.ClienteId,
                    ProductoIds = productoIdsVenta,
                    MetodoCalculo = MetodoCalculoCredito.AutomaticoPorCliente,
                    FuenteConfiguracion = parametros.Fuente,
                    GastosAdministrativos = parametros.GastosAdministrativos,
                    FechaPrimeraCuota = fechaPrimeraCuota
                },
                cancellationToken);

            if (!simulacion.EsValido || simulacion.Plan is null)
            {
                advertencias.Add(simulacion.Error?.error ?? "No se pudo simular credito personal.");
                continue;
            }

            planes.Add(CrearPlanCreditoPersonal(cuotas, simulacion.Plan, restricciones));
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
            NombreMedioPago = "Credito personal",
            Disponible = true,
            Estado = CotizacionOpcionPagoEstado.Disponible,
            FuenteTasaDescripcion = ResolverFuenteTasaDescripcion(parametros),
            Planes = planes
        });
    }

    private static string ResolverFuenteTasaDescripcion(ParametrosCreditoCliente parametros)
    {
        if (parametros.TieneConfiguracionPersonalizada)
            return "Configuracion personalizada del cliente";

        if (parametros.PerfilPreferidoId.HasValue)
            return $"Perfil de credito: {parametros.PerfilPreferidoNombre}";

        return "Configuracion global";
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
        int cuotas,
        CreditoSimulacionVentaJson plan,
        ProductoCreditoRestriccionResultado restricciones)
    {
        var advertencias = new List<string>();
        if (restricciones.MaxCuotasCredito.HasValue)
        {
            advertencias.Add($"Limite por producto: hasta {restricciones.MaxCuotasCredito.Value} cuotas.");
        }

        var vector = plan.cuotas
            .Select(c => new CotizacionPlanCuotaResultado
            {
                NumeroCuota = c.numeroCuota,
                Capital = c.capital,
                Interes = c.interes,
                Total = c.total
            })
            .ToList();

        return new CotizacionPlanPagoResultado
        {
            Plan = $"{cuotas} cuota(s)",
            CantidadCuotas = cuotas,
            TasaMensual = plan.tasaAplicada,
            InteresPorcentaje = plan.tasaAplicada,
            CostoFinancieroTotal = plan.interesTotal,
            TipoCalculo = "CreditoPersonalReadOnly",
            // Total a pagar en cuotas (sin gastos administrativos, que no se financian en cuotas):
            // totalAPagar, no totalPlan. Debe cerrar exacto contra la suma del vector de cuotas.
            Total = RedondearMoneda(plan.totalAPagar),
            ValorCuota = RedondearMoneda(plan.cuotaEstimada),
            Anticipo = plan.anticipo,
            SaldoAFinanciar = plan.montoFinanciado,
            TotalFinanciado = plan.totalAPagar,
            UltimaCuota = vector.Count > 0 ? vector[^1].Total : null,
            FuentePorcentaje = plan.fuentePorcentaje,
            Cuotas = vector,
            CuotasSinRecargo = plan.cuotasSinRecargo,
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
