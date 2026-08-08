using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public sealed class CreditoConfiguracionVentaServiceTests
{
    [Fact]
    public async Task Resolver_RechazaTasaGlobalAusente()
    {
        var service = CrearService(ConfigService(tasaGlobal: null));

        var result = await service.ResolverAsync(Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global), venta: null);

        Assert.False(result.EsValido);
        Assert.Equal(string.Empty, result.ErrorKey);
        Assert.Contains("tasa de inter", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolver_RechazaMetodoCalculoAusente()
    {
        var service = CrearService(ConfigService(tasaGlobal: 5m));

        var result = await service.ResolverAsync(Modelo(FuenteConfiguracionCredito.Global, metodo: null), venta: null);

        Assert.False(result.EsValido);
        Assert.Equal(nameof(ConfiguracionCreditoVentaViewModel.MetodoCalculo), result.ErrorKey);
        Assert.Contains("Debe seleccionar", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolver_RechazaUsarClienteSinConfiguracionPersonalizada()
    {
        var service = CrearService(ConfigService(
            tasaGlobal: 5m,
            parametros: new ParametrosCreditoCliente
            {
                TieneConfiguracionPersonalizada = false,
                TasaMensual = 5m
            }));

        var result = await service.ResolverAsync(Modelo(FuenteConfiguracionCredito.PorCliente, MetodoCalculoCredito.UsarCliente), venta: null);

        Assert.False(result.EsValido);
        Assert.Equal(nameof(ConfiguracionCreditoVentaViewModel.MetodoCalculo), result.ErrorKey);
        Assert.Contains("no tiene configuraci", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolver_RechazaTasaManualNegativa()
    {
        var service = CrearService(ConfigService(tasaGlobal: 5m));
        var modelo = Modelo(FuenteConfiguracionCredito.Manual, MetodoCalculoCredito.Manual);
        modelo.TasaMensual = -1m;

        var result = await service.ResolverAsync(modelo, venta: null);

        Assert.False(result.EsValido);
        Assert.Equal(nameof(modelo.TasaMensual), result.ErrorKey);
        Assert.Contains("negativa", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    // ML2.1: 0% sigue siendo un porcentaje valido y explicito — pero la autoridad es siempre el
    // plan, tambien en modo Manual. Por eso el plan de la cantidad elegida declara 0% explicito:
    // el resultado coincide con lo que cargo el operador, no porque Manual "gane", sino porque el
    // plan tambien vale 0%.
    [Fact]
    public async Task Resolver_AceptaTasaManualCero()
    {
        var service = CrearService(ConfigService(
            tasaGlobal: 5m,
            rango: (1, 120, "Manual", null),
            cuotas: new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 0m, Activo = true } // plan explicito en 0%
            }));
        var modelo = Modelo(FuenteConfiguracionCredito.Manual, MetodoCalculoCredito.Manual);
        modelo.TasaMensual = 0m;

        var result = await service.ResolverAsync(modelo, venta: null);

        AssertComandoValido(result);
        Assert.Equal(0m, result.Comando!.TasaMensual);
    }

    [Fact]
    public async Task Resolver_RechazaCuotasFueraDeRangoEfectivo()
    {
        var service = CrearService(
            ConfigService(tasaGlobal: 5m, rango: (1, 24, "Global", null)),
            new StubCreditoRangoProductoService(new CreditoRangoProductoResultado(1, 6, 24, 6, 7, "Producto", "Limite", null)));
        var modelo = Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global, ventaId: 99);
        modelo.CantidadCuotas = 7;

        var result = await service.ResolverAsync(modelo, VentaConProducto());

        Assert.False(result.EsValido);
        Assert.Equal(nameof(modelo.CantidadCuotas), result.ErrorKey);
        Assert.Contains("entre 1 y 6", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(6, result.RangoEfectivo!.Max);
    }

    [Fact]
    public async Task Resolver_ArmaComandoValidoParaFuenteGlobal()
    {
        var service = CrearService(ConfigService(tasaGlobal: 5m, rango: (1, 24, "Global", null)));

        var result = await service.ResolverAsync(Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global), venta: null);

        AssertComandoValido(result);
        Assert.Equal(FuenteConfiguracionCredito.Global, result.Comando!.FuenteConfiguracion);
        Assert.Equal(5m, result.Comando.TasaMensual);
        Assert.Equal("Global", result.Comando.FuenteRestriccionCuotasSnap);
    }

    [Fact]
    public async Task Resolver_UsaTasaPorCantidadDeCuotasCuandoHayTablaConfigurada()
    {
        var service = CrearService(ConfigService(
            tasaGlobal: 5m,
            rango: (1, 24, "Global", null),
            cuotas: new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 1, TasaMensual = 1m, Activo = true },
                new() { CantidadCuotas = 6, TasaMensual = 10m, Activo = true }
            }));
        var modelo = Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global);
        modelo.CantidadCuotas = 6;

        var result = await service.ResolverAsync(modelo, venta: null);

        AssertComandoValido(result);
        Assert.Equal(10m, result.Comando!.TasaMensual);
    }

    // ML2.1 — Fase 5 del contrato congelado: un plan activo sin porcentaje explicito (TasaMensual
    // null) es configuracion invalida DE VERDAD, nunca "heredar la tasa global" ni "0% silencioso".
    // Corrige la expectativa de ML1 (aquel test exigia EsValido = true y solo verificaba que no se
    // heredara la global; esa expectativa contradice el requerimiento congelado de Fase 5).
    [Fact]
    public async Task Resolver_CuotaConTasaNull_EsConfiguracionInvalida()
    {
        var service = CrearService(ConfigService(
            tasaGlobal: 5m,
            rango: (1, 24, "Global", null),
            cuotas: new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 1, TasaMensual = 1m, Activo = true },
                new() { CantidadCuotas = 6, TasaMensual = null, Activo = true } // plan sin porcentaje propio
            }));
        var modelo = Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global);
        modelo.CantidadCuotas = 6;

        var result = await service.ResolverAsync(modelo, venta: null);

        Assert.False(result.EsValido);
        Assert.Equal(nameof(modelo.CantidadCuotas), result.ErrorKey);
        Assert.Contains("porcentaje", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    // ML2.1 — Fase 3 (cerrado): Cliente personalizado no altera el porcentaje del plan
    // seleccionado. Dos clientes con configuraciones "por cliente" distintas, pero pidiendo el
    // mismo plan (misma cantidad de cuotas, cuyo porcentaje canónico es 8 %), resuelven el mismo
    // porcentaje: PorCliente usa siempre `planesVenta.BuscarPlan(...)?.TasaMensual`, nunca
    // `parametrosCliente.TasaMensual`.
    [Fact]
    public async Task Resolver_MismoPlanConClientesPersonalizadosDistintos_DebeResolverElMismoPorcentaje()
    {
        var cuotasConPlanCanonico = new List<CuotaCreditoPersonalViewModel>
        {
            new() { CantidadCuotas = 6, TasaMensual = 8m, Activo = true } // porcentaje canónico del plan
        };

        var serviceClienteA = CrearService(ConfigService(
            tasaGlobal: 5m,
            parametros: new ParametrosCreditoCliente { TieneConfiguracionPersonalizada = true, TasaMensual = 15m },
            rango: (1, 24, "Cliente", null),
            cuotas: cuotasConPlanCanonico));

        var serviceClienteB = CrearService(ConfigService(
            tasaGlobal: 5m,
            parametros: new ParametrosCreditoCliente { TieneConfiguracionPersonalizada = true, TasaMensual = 2m },
            rango: (1, 24, "Cliente", null),
            cuotas: cuotasConPlanCanonico));

        var modelo = Modelo(FuenteConfiguracionCredito.PorCliente, MetodoCalculoCredito.UsarCliente);
        modelo.CantidadCuotas = 6;

        var resultA = await serviceClienteA.ResolverAsync(modelo, venta: null);
        var resultB = await serviceClienteB.ResolverAsync(modelo, venta: null);

        AssertComandoValido(resultA);
        AssertComandoValido(resultB);
        // Contrato nuevo: mismo plan (6 cuotas, 8 %) ⇒ mismo porcentaje, sin importar el cliente.
        Assert.Equal(8m, resultA.Comando!.TasaMensual);
        Assert.Equal(8m, resultB.Comando!.TasaMensual);
        Assert.Equal(resultA.Comando.TasaMensual, resultB.Comando.TasaMensual);
    }

    // ML2.1 — Fase 4 (cerrado): Manual/FuenteConfiguracion no altera el porcentaje de un plan
    // existente. La cantidad de cuotas elegida coincide con un plan que ya tiene porcentaje
    // canónico (8 %); el modo Manual no puede sobrescribirlo con el valor arbitrario del operador
    // (50 %): el plan es siempre la autoridad, tambien en Manual.
    [Fact]
    public async Task Resolver_FuenteManualConCantidadDePlanExistente_NoDeberiaSobrescribirElPorcentajeDelPlan()
    {
        var service = CrearService(ConfigService(
            tasaGlobal: 5m,
            rango: (1, 24, "Manual", null),
            cuotas: new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 8m, Activo = true } // porcentaje canónico del plan
            }));
        var modelo = Modelo(FuenteConfiguracionCredito.Manual, MetodoCalculoCredito.Manual);
        modelo.CantidadCuotas = 6;
        modelo.TasaMensual = 50m; // el operador intenta sobrescribir el 8 % del plan

        var result = await service.ResolverAsync(modelo, venta: null);

        AssertComandoValido(result);
        Assert.NotEqual(50m, result.Comando!.TasaMensual);
        Assert.Equal(8m, result.Comando.TasaMensual);
    }

    [Fact]
    public async Task Resolver_RechazaCuotaNoHabilitadaEnTablaPorCantidad()
    {
        var service = CrearService(ConfigService(
            tasaGlobal: 5m,
            rango: (1, 24, "Global", null),
            cuotas: new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 1, TasaMensual = 1m, Activo = true },
                new() { CantidadCuotas = 12, TasaMensual = 15m, Activo = true }
            }));
        var modelo = Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global);
        modelo.CantidadCuotas = 6;

        var result = await service.ResolverAsync(modelo, venta: null);

        Assert.False(result.EsValido);
        Assert.Equal(nameof(modelo.CantidadCuotas), result.ErrorKey);
        Assert.Contains("no est", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    // ML2.1: el plan (no el operador) es la autoridad del porcentaje tambien en modo Manual. El
    // plan de la cantidad elegida declara el mismo 7.25% que carga el operador para mantener el
    // caso "comando valido" enfocado en FuenteConfiguracion/GastosAdministrativos, no en la
    // precedencia plan-vs-manual (esa la cubre Resolver_FuenteManualConCantidadDePlanExistente_
    // NoDeberiaSobrescribirElPorcentajeDelPlan, donde plan y manual difieren a proposito).
    [Fact]
    public async Task Resolver_ArmaComandoValidoParaFuenteManual()
    {
        var service = CrearService(ConfigService(
            tasaGlobal: 5m,
            rango: (1, 120, "Manual", null),
            cuotas: new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 7.25m, Activo = true }
            }));
        var modelo = Modelo(FuenteConfiguracionCredito.Manual, MetodoCalculoCredito.Manual);
        modelo.TasaMensual = 7.25m;
        modelo.GastosAdministrativos = 150m;

        var result = await service.ResolverAsync(modelo, venta: null);

        AssertComandoValido(result);
        Assert.Equal(FuenteConfiguracionCredito.Manual, result.Comando!.FuenteConfiguracion);
        Assert.Equal(7.25m, result.Comando.TasaMensual); // proviene del plan (coincide con lo cargado)
        Assert.Equal(150m, result.Comando.GastosAdministrativos);
    }

    // ML2.1 — Fase 3: el cliente ya no es autoridad del porcentaje; parametros.TasaMensual queda
    // como dato legado sin efecto. El plan de la cantidad elegida declara el mismo 6.5% para
    // mantener el foco del caso en FuenteConfiguracion/CuotasMaxPermitidas (la equivalencia real
    // de porcentaje entre clientes distintos ya la cubre
    // Resolver_MismoPlanConClientesPersonalizadosDistintos_DebeResolverElMismoPorcentaje).
    [Fact]
    public async Task Resolver_ArmaComandoValidoParaFuentePorCliente()
    {
        var service = CrearService(ConfigService(
            tasaGlobal: 5m,
            parametros: new ParametrosCreditoCliente
            {
                TieneConfiguracionPersonalizada = true,
                TasaMensual = 6.5m, // ML2.1: legado, ya no es autoridad
                GastosAdministrativos = 250m
            },
            rango: (1, 18, "Cliente", null),
            cuotas: new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 6.5m, Activo = true } // autoridad real: el plan
            }));

        var result = await service.ResolverAsync(Modelo(FuenteConfiguracionCredito.PorCliente, MetodoCalculoCredito.UsarCliente), venta: null);

        AssertComandoValido(result);
        Assert.Equal(FuenteConfiguracionCredito.PorCliente, result.Comando!.FuenteConfiguracion);
        Assert.Equal(6.5m, result.Comando.TasaMensual); // proviene del plan (Fase 3)
        Assert.Equal(0m, result.Comando.GastosAdministrativos);
        Assert.Equal(18, result.Comando.CuotasMaxPermitidas);
    }

    [Fact]
    public async Task Resolver_ConservaSnapshotsDeRestriccionPorProducto()
    {
        var service = CrearService(
            ConfigService(tasaGlobal: 5m, rango: (1, 24, "Global", null)),
            new StubCreditoRangoProductoService(new CreditoRangoProductoResultado(1, 6, 24, 6, 7, "Producto", "Limite", null)));
        var modelo = Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global, ventaId: 99);
        modelo.CantidadCuotas = 6;

        var result = await service.ResolverAsync(modelo, VentaConProducto());

        AssertComandoValido(result);
        Assert.Equal("Producto", result.Comando!.FuenteRestriccionCuotasSnap);
        Assert.Equal(7, result.Comando.ProductoIdRestrictivoSnap);
        Assert.Equal(24, result.Comando.MaxCuotasBaseSnap);
        Assert.Equal(6, result.Comando.CuotasMaxPermitidas);
    }

    // ── ML4: autoridad del monto ────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolver_ConVenta_MontoManipuladoEnModelo_UsaTotalRealDeLaVenta()
    {
        // Tests obligatorios #1/#2: modelo.Monto llega desde un hidden manipulable; con venta
        // asociada el comando debe usar venta.Total, nunca el valor del formulario.
        var service = CrearService(ConfigService(tasaGlobal: 5m, rango: (1, 24, "Global", null)));
        var modelo = Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global, ventaId: 99);
        modelo.Monto = 999_999m;
        var venta = new VentaViewModel { Id = 99, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };

        var result = await service.ResolverAsync(modelo, venta);

        Assert.True(result.EsValido);
        Assert.Equal(10_000m, result.Comando!.Monto);
        Assert.NotEqual(modelo.Monto, result.Comando.Monto);
    }

    [Fact]
    public async Task Resolver_ConVenta_AnticipoSuperaTotalReal_Rechaza()
    {
        var service = CrearService(ConfigService(tasaGlobal: 5m, rango: (1, 24, "Global", null)));
        var modelo = Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global, ventaId: 99);
        modelo.Anticipo = 10_001m;
        var venta = new VentaViewModel { Id = 99, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };

        var result = await service.ResolverAsync(modelo, venta);

        Assert.False(result.EsValido);
        Assert.Equal(nameof(modelo.Anticipo), result.ErrorKey);
        Assert.Contains("anticipo no puede superar", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolver_ConVenta_AnticipoIgualAlTotalReal_GeneraComandoValidoConSaldoCero()
    {
        // Test obligatorio #6: la regla congelada (ver el equivalente en
        // CreditoSimulacionVentaServiceTests) es que anticipo == total no se rechaza acá tampoco;
        // el comando resultante financia $0 (saldo cero), no un error de validación.
        var service = CrearService(ConfigService(tasaGlobal: 5m, rango: (1, 24, "Global", null)));
        var modelo = Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global, ventaId: 99);
        modelo.Anticipo = 10_000m;
        var venta = new VentaViewModel { Id = 99, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };

        var result = await service.ResolverAsync(modelo, venta);

        Assert.True(result.EsValido);
        Assert.Equal(10_000m, result.Comando!.Monto);
        Assert.Equal(10_000m, result.Comando.Anticipo);
    }

    [Fact]
    public async Task Resolver_SinVenta_UsaMontoDelModelo()
    {
        // Escenario legítimo sin VentaId (crédito standalone): el monto lo define el formulario,
        // no hay total de venta contra el cual verificarlo.
        var service = CrearService(ConfigService(tasaGlobal: 5m, rango: (1, 24, "Global", null)));
        var modelo = Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global);
        modelo.Monto = 7_777m;

        var result = await service.ResolverAsync(modelo, venta: null);

        Assert.True(result.EsValido);
        Assert.Equal(7_777m, result.Comando!.Monto);
    }

    private static CreditoConfiguracionVentaService CrearService(
        StubConfiguracionPagoService configuracionPagoService,
        ICreditoRangoProductoService? rangoProductoService = null,
        IRelojComercial? reloj = null) =>
        new(
            configuracionPagoService,
            NullLogger<CreditoConfiguracionVentaService>.Instance,
            rangoProductoService,
            reloj);

    // ── F2 (Micro-lote 6): decisión de cobro de la 1ª cuota, server-authoritative ──────
    // El servidor solo persiste la intención de cobro cuando la 1ª cuota vence en la fecha
    // comercial actual y el medio es válido; el payload no puede forzarla en ningún otro caso.

    private static readonly DateOnly HoyFijo = new(2026, 6, 7);

    [Fact]
    public async Task Resolver_CobrarPrimeraCuota_VenceHoyMedioValido_PersisteDecision()
    {
        var service = CrearService(
            ConfigService(tasaGlobal: 5m, rango: (1, 120, "Manual", null)),
            reloj: new TheBuryProject.Tests.Helpers.RelojComercialFake(HoyFijo));
        var modelo = Modelo(FuenteConfiguracionCredito.Manual, MetodoCalculoCredito.Manual);
        modelo.FechaPrimeraCuota = HoyFijo.ToDateTime(TimeOnly.MinValue);
        modelo.CobrarPrimeraCuota = true;
        modelo.MedioPagoPrimeraCuota = "Transferencia";

        var result = await service.ResolverAsync(modelo, venta: null);

        AssertComandoValido(result);
        Assert.True(result.Comando!.CobrarPrimeraCuota);
        Assert.Equal("Transferencia", result.Comando.MedioPagoPrimeraCuota);
    }

    [Fact]
    public async Task Resolver_CobrarPrimeraCuota_VenceManana_NoPersisteAunConPayload()
    {
        var service = CrearService(
            ConfigService(tasaGlobal: 5m, rango: (1, 120, "Manual", null)),
            reloj: new TheBuryProject.Tests.Helpers.RelojComercialFake(HoyFijo));
        var modelo = Modelo(FuenteConfiguracionCredito.Manual, MetodoCalculoCredito.Manual);
        modelo.FechaPrimeraCuota = HoyFijo.AddDays(1).ToDateTime(TimeOnly.MinValue); // mañana
        modelo.CobrarPrimeraCuota = true;
        modelo.MedioPagoPrimeraCuota = "Efectivo";

        var result = await service.ResolverAsync(modelo, venta: null);

        AssertComandoValido(result);
        Assert.False(result.Comando!.CobrarPrimeraCuota);
        Assert.Null(result.Comando.MedioPagoPrimeraCuota);
    }

    [Fact]
    public async Task Resolver_CobrarPrimeraCuota_MedioInvalido_NoPersiste()
    {
        var service = CrearService(
            ConfigService(tasaGlobal: 5m, rango: (1, 120, "Manual", null)),
            reloj: new TheBuryProject.Tests.Helpers.RelojComercialFake(HoyFijo));
        var modelo = Modelo(FuenteConfiguracionCredito.Manual, MetodoCalculoCredito.Manual);
        modelo.FechaPrimeraCuota = HoyFijo.ToDateTime(TimeOnly.MinValue);
        modelo.CobrarPrimeraCuota = true;
        modelo.MedioPagoPrimeraCuota = "Bitcoin"; // no habilitado

        var result = await service.ResolverAsync(modelo, venta: null);

        AssertComandoValido(result);
        Assert.False(result.Comando!.CobrarPrimeraCuota);
        Assert.Null(result.Comando.MedioPagoPrimeraCuota);
    }

    [Fact]
    public async Task Resolver_CobrarPrimeraCuota_SinMarcar_NoPersiste()
    {
        var service = CrearService(
            ConfigService(tasaGlobal: 5m, rango: (1, 120, "Manual", null)),
            reloj: new TheBuryProject.Tests.Helpers.RelojComercialFake(HoyFijo));
        var modelo = Modelo(FuenteConfiguracionCredito.Manual, MetodoCalculoCredito.Manual);
        modelo.FechaPrimeraCuota = HoyFijo.ToDateTime(TimeOnly.MinValue);
        modelo.CobrarPrimeraCuota = false;
        modelo.MedioPagoPrimeraCuota = "Transferencia";

        var result = await service.ResolverAsync(modelo, venta: null);

        AssertComandoValido(result);
        Assert.False(result.Comando!.CobrarPrimeraCuota);
        Assert.Null(result.Comando.MedioPagoPrimeraCuota);
    }

    // Micro-lote 4 + ML2.1: las cantidades disponibles salen SOLO de los planes globales activos.
    // En produccion la configuracion siempre tiene planes; por eso el stub, cuando el test no
    // seedea cuotas propias, ofrece planes 1..24 para que los casos que ejercen tasa/rango/
    // snapshots tengan cuotas disponibles y no sean rechazados por "sin planes". ML2.1: cada plan
    // trae un porcentaje EXPLICITO (igual a tasaGlobal, solo como valor de conveniencia del
    // fixture) — un plan con TasaMensual null es "configuracion invalida" bajo el contrato nuevo
    // (Fase 5), no un caso por defecto neutro; los tests que necesitan ejercer ese caso puntual
    // pasan su propio `cuotas` con TasaMensual = null explicito.
    private static List<CuotaCreditoPersonalViewModel> PlanesGlobalesPorDefecto(decimal tasa) =>
        Enumerable.Range(1, 24)
            .Select(n => new CuotaCreditoPersonalViewModel { CantidadCuotas = n, TasaMensual = tasa, Activo = true })
            .ToList();

    private static StubConfiguracionPagoService ConfigService(
        decimal? tasaGlobal,
        ParametrosCreditoCliente? parametros = null,
        (int Min, int Max, string Descripcion, string? PerfilNombre)? rango = null,
        List<CuotaCreditoPersonalViewModel>? cuotas = null) =>
        new()
        {
            TasaGlobal = tasaGlobal,
            Parametros = parametros ?? new ParametrosCreditoCliente
            {
                Fuente = FuenteConfiguracionCredito.Global,
                TasaMensual = tasaGlobal ?? 0m,
                GastosAdministrativos = 0m
            },
            Rango = rango ?? (1, 120, "Manual", null),
            CuotasCreditoPersonal = cuotas ?? PlanesGlobalesPorDefecto(tasaGlobal ?? 0m)
        };

    private static ConfiguracionCreditoVentaViewModel Modelo(
        FuenteConfiguracionCredito fuente,
        MetodoCalculoCredito? metodo,
        int? ventaId = null) =>
        new()
        {
            CreditoId = 10,
            VentaId = ventaId,
            ClienteId = 20,
            Monto = 10_000m,
            Anticipo = 0m,
            CantidadCuotas = 6,
            TasaMensual = fuente == FuenteConfiguracionCredito.Manual ? 5m : null,
            GastosAdministrativos = 0m,
            FechaPrimeraCuota = new DateTime(2026, 6, 7),
            FuenteConfiguracion = fuente,
            MetodoCalculo = metodo
        };

    private static VentaViewModel VentaConProducto() =>
        new()
        {
            Id = 99,
            Total = 10_000m,
            Detalles = new List<VentaDetalleViewModel>
            {
                new() { ProductoId = 7, ProductoNombre = "Producto restrictivo" }
            }
        };

    private static void AssertComandoValido(CreditoConfiguracionVentaResultado result)
    {
        Assert.True(result.EsValido);
        Assert.NotNull(result.Comando);
        Assert.Equal(10, result.Comando!.CreditoId);
        Assert.Equal(10_000m, result.Comando.Monto);
        Assert.Equal(6, result.Comando.CantidadCuotas);
    }

    private sealed class StubCreditoRangoProductoService : ICreditoRangoProductoService
    {
        private readonly CreditoRangoProductoResultado _resultado;

        public StubCreditoRangoProductoService(CreditoRangoProductoResultado resultado)
        {
            _resultado = resultado;
        }

        public Task<CreditoRangoProductoResultado> ResolverAsync(
            VentaViewModel? venta,
            TipoPago tipoPago,
            int minBase,
            int maxBase,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_resultado);
    }

    private sealed class StubConfiguracionPagoService : IConfiguracionPagoService
    {
        public decimal? TasaGlobal { get; init; }
        public ParametrosCreditoCliente Parametros { get; init; } = new();
        public (int Min, int Max, string Descripcion, string? PerfilNombre) Rango { get; init; } = (1, 120, "Manual", null);
        public List<CuotaCreditoPersonalViewModel> CuotasCreditoPersonal { get; init; } = new();

        public Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync() => Task.FromResult(TasaGlobal);
        public Task<ParametrosCreditoCliente> ObtenerParametrosCreditoClienteAsync(int clienteId, decimal tasaGlobal) => Task.FromResult(Parametros);
        public Task<(int Min, int Max, string Descripcion, string? PerfilNombre)> ResolverRangoCuotasAsync(MetodoCalculoCredito metodo, int? perfilId, int? clienteId) => Task.FromResult(Rango);

        public Task<List<ConfiguracionPagoViewModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel> CreateAsync(ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> UpdateAsync(int id, ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<List<ConfiguracionTarjetaViewModel>> GetTarjetasActivasAsync() => throw new NotImplementedException();
        public Task<List<TarjetaActivaVentaResultado>> GetTarjetasActivasParaVentaAsync() => throw new NotImplementedException();
        public Task<ConfiguracionTarjetaViewModel?> GetTarjetaByIdAsync(int id) => throw new NotImplementedException();
        public Task<bool> ValidarDescuento(TipoPago tipoPago, decimal descuento) => throw new NotImplementedException();
        public Task<decimal> CalcularRecargo(TipoPago tipoPago, decimal monto) => throw new NotImplementedException();
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoAsync() => throw new NotImplementedException();
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoActivosAsync() => throw new NotImplementedException();
        public Task GuardarCreditoPersonalAsync(CreditoPersonalConfigViewModel config) => throw new NotImplementedException();
        public Task<MaxCuotasSinInteresResultado?> ObtenerMaxCuotasSinInteresEfectivoAsync(int tarjetaId, IEnumerable<int> productoIds) => throw new NotImplementedException();
        public Task<List<MontoPorPuntajeCreditoViewModel>> GetMontosPorPuntajeAsync() => Task.FromResult(new List<MontoPorPuntajeCreditoViewModel>());
        public Task<(bool Ok, List<string> Errores)> GuardarMontosPorPuntajeAsync(List<MontoPorPuntajeCreditoViewModel> items, string usuario) => Task.FromResult((true, new List<string>()));
        public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalAsync() => Task.FromResult(CuotasCreditoPersonal);
        public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalActivasAsync() => Task.FromResult(CuotasCreditoPersonal.Where(c => c.Activo).ToList());
        public async Task<PlanesCreditoPersonalResultado> ResolverPlanesCreditoPersonalAsync(IEnumerable<int> productoIds) => PlanesCreditoPersonalStub.DesdeGlobales(await GetCuotasCreditoPersonalActivasAsync());
        public Task<(bool Ok, List<string> Errores)> GuardarCuotasCreditoPersonalAsync(List<CuotaCreditoPersonalViewModel> items, string usuario) => Task.FromResult((true, new List<string>()));
    }
}
