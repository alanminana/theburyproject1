using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para CreditoConfiguracionHelper.ResolverTasaYGastos.
///
/// Documenta el contrato real del bloque de resolución de tasa extraído de
/// ConfigurarVenta POST. Todos los casos se verificaron contra el bloque inline
/// original antes de la extracción.
///
/// El caso Manual no está cubierto aquí porque implica un early return del
/// controller (ModelState + View) que no es responsabilidad de este método.
///
/// No requiere DB ni infraestructura — todos los datos se pasan como parámetros.
/// </summary>
public class CreditoControllerTasaTests
{
    private const decimal TasaGlobal = 5.0m;
    private const decimal TasaCliente = 3.5m;
    private const decimal GastosCliente = 200m;

    private static Cliente ClienteConTasaYGastos() => new()
    {
        Nombre = "Test", Apellido = "Cliente",
        TipoDocumento = "DNI", NumeroDocumento = "30000001",
        TasaInteresMensualPersonalizada = TasaCliente,
        GastosAdministrativosPersonalizados = GastosCliente,
        NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
        RowVersion = new byte[8]
    };

    private static Cliente ClienteSinTasaPersonalizada() => new()
    {
        Nombre = "Test", Apellido = "SinTasa",
        TipoDocumento = "DNI", NumeroDocumento = "30000002",
        TasaInteresMensualPersonalizada = null,
        GastosAdministrativosPersonalizados = GastosCliente,
        NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
        RowVersion = new byte[8]
    };

    // ---------------------------------------------------------------------------
    // PorCliente — cliente con tasa personalizada
    // ---------------------------------------------------------------------------

    [Fact]
    public void PorCliente_ClienteConTasaPersonalizada_UsaTasaDelCliente()
    {
        var (tasa, _) = CreditoConfiguracionHelper.ResolverTasaYGastos(
            FuenteConfiguracionCredito.PorCliente,
            tasaMensualDelModelo: null,
            gastosDelModelo: null,
            tasaGlobal: TasaGlobal,
            cliente: ClienteConTasaYGastos());

        Assert.Equal(TasaCliente, tasa);
    }

    // ---------------------------------------------------------------------------
    // PorCliente — cliente sin tasa personalizada → fallback global
    // ---------------------------------------------------------------------------

    [Fact]
    public void PorCliente_ClienteSinTasaPersonalizada_UsaTasaGlobal()
    {
        var (tasa, _) = CreditoConfiguracionHelper.ResolverTasaYGastos(
            FuenteConfiguracionCredito.PorCliente,
            tasaMensualDelModelo: null,
            gastosDelModelo: null,
            tasaGlobal: TasaGlobal,
            cliente: ClienteSinTasaPersonalizada());

        Assert.Equal(TasaGlobal, tasa);
    }

    // ---------------------------------------------------------------------------
    // PorCliente — cliente null (no encontrado) → fallback global
    // ---------------------------------------------------------------------------

    [Fact]
    public void PorCliente_ClienteNull_UsaTasaGlobal()
    {
        var (tasa, _) = CreditoConfiguracionHelper.ResolverTasaYGastos(
            FuenteConfiguracionCredito.PorCliente,
            tasaMensualDelModelo: null,
            gastosDelModelo: null,
            tasaGlobal: TasaGlobal,
            cliente: null);

        Assert.Equal(TasaGlobal, tasa);
    }

    // ---------------------------------------------------------------------------
    // PorCliente — gastosDelModelo sin valor → toma gastos personalizados del cliente
    // ---------------------------------------------------------------------------

    [Fact]
    public void PorCliente_GastosDelModeloNull_TomGastosDelCliente()
    {
        var (_, gastos) = CreditoConfiguracionHelper.ResolverTasaYGastos(
            FuenteConfiguracionCredito.PorCliente,
            tasaMensualDelModelo: null,
            gastosDelModelo: null,       // modelo no trajo gastos explícitos
            tasaGlobal: TasaGlobal,
            cliente: ClienteConTasaYGastos());

        Assert.Equal(GastosCliente, gastos);
    }

    // ---------------------------------------------------------------------------
    // PorCliente — gastosDelModelo con valor → NO sobreescribe con gastos del cliente
    // ---------------------------------------------------------------------------

    [Fact]
    public void PorCliente_GastosDelModeloConValor_MantieneGastosDelModelo()
    {
        const decimal gastosDelModelo = 500m;

        var (_, gastos) = CreditoConfiguracionHelper.ResolverTasaYGastos(
            FuenteConfiguracionCredito.PorCliente,
            tasaMensualDelModelo: null,
            gastosDelModelo: gastosDelModelo,
            tasaGlobal: TasaGlobal,
            cliente: ClienteConTasaYGastos());

        Assert.Equal(gastosDelModelo, gastos);
    }

    // ---------------------------------------------------------------------------
    // PorCliente — cliente sin gastos personalizados → gastos = 0
    // ---------------------------------------------------------------------------

    [Fact]
    public void PorCliente_ClienteSinGastosPersonalizados_DevuelveGastosCero()
    {
        var clienteSinGastos = new Cliente
        {
            Nombre = "Test", Apellido = "SinGastos",
            TipoDocumento = "DNI", NumeroDocumento = "30000003",
            TasaInteresMensualPersonalizada = TasaCliente,
            GastosAdministrativosPersonalizados = null,
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            RowVersion = new byte[8]
        };

        var (_, gastos) = CreditoConfiguracionHelper.ResolverTasaYGastos(
            FuenteConfiguracionCredito.PorCliente,
            tasaMensualDelModelo: null,
            gastosDelModelo: null,
            tasaGlobal: TasaGlobal,
            cliente: clienteSinGastos);

        Assert.Equal(0m, gastos);
    }

    // ---------------------------------------------------------------------------
    // Global → usa tasa global, gastos del modelo (o 0)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Global_SinImportarCliente_UsaTasaGlobal()
    {
        var (tasa, _) = CreditoConfiguracionHelper.ResolverTasaYGastos(
            FuenteConfiguracionCredito.Global,
            tasaMensualDelModelo: null,
            gastosDelModelo: null,
            tasaGlobal: TasaGlobal,
            cliente: null);

        Assert.Equal(TasaGlobal, tasa);
    }

    [Fact]
    public void Global_ConGastosEnModelo_MantieneGastosDelModelo()
    {
        const decimal gastosDelModelo = 150m;

        var (_, gastos) = CreditoConfiguracionHelper.ResolverTasaYGastos(
            FuenteConfiguracionCredito.Global,
            tasaMensualDelModelo: null,
            gastosDelModelo: gastosDelModelo,
            tasaGlobal: TasaGlobal,
            cliente: null);

        Assert.Equal(gastosDelModelo, gastos);
    }

    // ---------------------------------------------------------------------------
    // Manual con tasa válida → el método no se llama en producción (early return previo),
    // pero si se llama, respeta la misma lógica Global (tasa global).
    // Documenta que la rama Manual en el controller NO delega en este método.
    // ---------------------------------------------------------------------------

    [Fact]
    public void ManualConTasaValida_EsEquivalenteAGlobal_DesdeElPuntoDeVistaDeLaFuncion()
    {
        // En producción, FuenteConfiguracion = Manual con TasaMensual > 0 entra al else
        // del controller y nunca llega a ResolverTasaYGastos.
        // Este test documenta que si por algún motivo se llamara, la lógica trataría
        // Manual como Global (no hay rama para Manual en este método).
        var (tasa, _) = CreditoConfiguracionHelper.ResolverTasaYGastos(
            FuenteConfiguracionCredito.Manual,
            tasaMensualDelModelo: 7m,
            gastosDelModelo: null,
            tasaGlobal: TasaGlobal,
            cliente: null);

        Assert.Equal(TasaGlobal, tasa);
    }

    // ---------------------------------------------------------------------------
    // Caso edge: Manual + TasaMensual null → en el controller cae en el if exterior
    // (!tasaMensual.HasValue = true) y llama a ResolverTasaYGastos con fuente=Manual.
    // El método lo trata como Global → devuelve tasaGlobal silenciosamente.
    // Este test documenta el bug latente: el usuario eligió Manual pero no ingresó tasa,
    // y el sistema asigna tasa global sin error.
    // ---------------------------------------------------------------------------

    [Fact]
    public void ManualConTasaNull_DevuelveTasaGlobalSilenciosamente_BugLatente()
    {
        // El controller llega acá cuando:
        //   modelo.TasaMensual = null  (usuario no ingresó tasa)
        //   modelo.FuenteConfiguracion = Manual
        // La condición !tasaMensual.HasValue || fuente != Manual → true (por el primer operando)
        // → entra al if, llama ResolverTasaYGastos con fuente=Manual, cliente=null
        // → cae en la rama else → devuelve tasaGlobal
        // → no se muestra error al usuario
        var (tasa, _) = CreditoConfiguracionHelper.ResolverTasaYGastos(
            FuenteConfiguracionCredito.Manual,
            tasaMensualDelModelo: null,
            gastosDelModelo: null,
            tasaGlobal: TasaGlobal,
            cliente: null);

        // Documenta el comportamiento actual, no el comportamiento correcto.
        // El comportamiento correcto sería mostrar error de validación.
        Assert.Equal(TasaGlobal, tasa);
    }
}
