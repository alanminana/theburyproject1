using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels.Mora;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para MoraService.AplicarFiltros y MoraService.AplicarOrdenamiento.
///
/// Documenta el contrato de filtrado y ordenamiento extraído de
/// GetClientesEnMoraAsync. Todos los casos operan sobre listas en memoria.
///
/// No requiere DB ni infraestructura.
/// </summary>
public class MoraServiceFiltrosTests
{
    private static readonly DateTime Hoy = new DateTime(2026, 3, 21);

    // ---------------------------------------------------------------------------
    // Helpers de construcción de listas de prueba
    // ---------------------------------------------------------------------------

    private static ClienteMoraViewModel Cliente(
        int id = 1,
        string nombre = "Cliente Test",
        string documento = "12345678",
        PrioridadAlerta prioridad = PrioridadAlerta.Media,
        EstadoGestionCobranza estado = EstadoGestionCobranza.Pendiente,
        int diasAtraso = 30,
        decimal montoVencido = 1_000m,
        bool tienePromesa = false,
        bool tieneAcuerdo = false,
        DateTime? ultimoContacto = null) =>
        new()
        {
            ClienteId = id,
            Nombre = nombre,
            Documento = documento,
            PrioridadMaxima = prioridad,
            EstadoGestion = estado,
            DiasMaxAtraso = diasAtraso,
            MontoVencido = montoVencido,
            TienePromesaActiva = tienePromesa,
            TieneAcuerdoActivo = tieneAcuerdo,
            UltimoContacto = ultimoContacto
        };

    private static FiltrosBandejaClientes SinFiltros() => new();

    // ---------------------------------------------------------------------------
    // AplicarFiltros — sin filtros → lista inalterada
    // ---------------------------------------------------------------------------

    [Fact]
    public void SinFiltros_DevuelveListaCompleta()
    {
        var lista = new List<ClienteMoraViewModel> { Cliente(1), Cliente(2), Cliente(3) };

        var resultado = MoraService.AplicarFiltros(lista, SinFiltros(), Hoy);

        Assert.Equal(3, resultado.Count);
    }

    // ---------------------------------------------------------------------------
    // AplicarFiltros — filtro por Prioridad
    // ---------------------------------------------------------------------------

    [Fact]
    public void FiltrosPrioridad_DejasoloClientesConPrioridadIndicada()
    {
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, prioridad: PrioridadAlerta.Baja),
            Cliente(2, prioridad: PrioridadAlerta.Critica),
            Cliente(3, prioridad: PrioridadAlerta.Critica)
        };
        var filtros = new FiltrosBandejaClientes { Prioridad = PrioridadAlerta.Critica };

        var resultado = MoraService.AplicarFiltros(lista, filtros, Hoy);

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, c => Assert.Equal(PrioridadAlerta.Critica, c.PrioridadMaxima));
    }

    [Fact]
    public void FiltrosPrioridad_Null_NofiltRa()
    {
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, prioridad: PrioridadAlerta.Baja),
            Cliente(2, prioridad: PrioridadAlerta.Alta)
        };
        var filtros = new FiltrosBandejaClientes { Prioridad = null };

        var resultado = MoraService.AplicarFiltros(lista, filtros, Hoy);

        Assert.Equal(2, resultado.Count);
    }

    // ---------------------------------------------------------------------------
    // AplicarFiltros — filtro por días min/max
    // ---------------------------------------------------------------------------

    [Fact]
    public void FiltrosDiasMinAtraso_DejaClientesConDiasIgualOMayor()
    {
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, diasAtraso: 10),
            Cliente(2, diasAtraso: 30),
            Cliente(3, diasAtraso: 60)
        };
        var filtros = new FiltrosBandejaClientes { DiasMinAtraso = 30 };

        var resultado = MoraService.AplicarFiltros(lista, filtros, Hoy);

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, c => Assert.True(c.DiasMaxAtraso >= 30));
    }

    [Fact]
    public void FiltrosDiasMaxAtraso_DejaClientesConDiasIgualOMenor()
    {
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, diasAtraso: 10),
            Cliente(2, diasAtraso: 30),
            Cliente(3, diasAtraso: 90)
        };
        var filtros = new FiltrosBandejaClientes { DiasMaxAtraso = 30 };

        var resultado = MoraService.AplicarFiltros(lista, filtros, Hoy);

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, c => Assert.True(c.DiasMaxAtraso <= 30));
    }

    // ---------------------------------------------------------------------------
    // AplicarFiltros — filtro por monto min/max
    // ---------------------------------------------------------------------------

    [Fact]
    public void FiltrosMontoMin_DejaClientesConMontoIgualOMayor()
    {
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, montoVencido: 500m),
            Cliente(2, montoVencido: 1_000m),
            Cliente(3, montoVencido: 5_000m)
        };
        var filtros = new FiltrosBandejaClientes { MontoMinVencido = 1_000m };

        var resultado = MoraService.AplicarFiltros(lista, filtros, Hoy);

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, c => Assert.True(c.MontoVencido >= 1_000m));
    }

    [Fact]
    public void FiltrosMontoMax_DejaClientesConMontoIgualOMenor()
    {
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, montoVencido: 500m),
            Cliente(2, montoVencido: 1_000m),
            Cliente(3, montoVencido: 5_000m)
        };
        var filtros = new FiltrosBandejaClientes { MontoMaxVencido = 1_000m };

        var resultado = MoraService.AplicarFiltros(lista, filtros, Hoy);

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, c => Assert.True(c.MontoVencido <= 1_000m));
    }

    // ---------------------------------------------------------------------------
    // AplicarFiltros — búsqueda por texto (nombre o documento)
    // La búsqueda normaliza diacríticos (Normalize FormD) y es case-insensitive.
    // Nombre y Documento son string no-nullable (= string.Empty), no hay riesgo
    // de NullReferenceException en los campos del modelo.
    // ---------------------------------------------------------------------------

    [Fact]
    public void FiltrosBusqueda_EncuentraPorNombreSinDiacritico()
    {
        // "garcia" debe encontrar "García, Juan" y "García, Pedro"
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, nombre: "García, Juan"),
            Cliente(2, nombre: "López, María"),
            Cliente(3, nombre: "García, Pedro")
        };
        var filtros = new FiltrosBandejaClientes { Busqueda = "garcia" };

        var resultado = MoraService.AplicarFiltros(lista, filtros, Hoy);

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public void FiltrosBusqueda_EnMayusculasSinDiacritico_EncuentraConDiacritico()
    {
        // "GARCIA" debe encontrar "García, Juan"
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, nombre: "García, Juan"),
            Cliente(2, nombre: "López, María")
        };
        var filtros = new FiltrosBandejaClientes { Busqueda = "GARCIA" };

        var resultado = MoraService.AplicarFiltros(lista, filtros, Hoy);

        Assert.Single(resultado);
        Assert.Equal(1, resultado[0].ClienteId);
    }

    [Fact]
    public void FiltrosBusqueda_EncuentraNombreConEnye()
    {
        // "munoz" debe encontrar "Muñoz, Ana"
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, nombre: "Muñoz, Ana"),
            Cliente(2, nombre: "López, María")
        };
        var filtros = new FiltrosBandejaClientes { Busqueda = "munoz" };

        var resultado = MoraService.AplicarFiltros(lista, filtros, Hoy);

        Assert.Single(resultado);
        Assert.Equal(1, resultado[0].ClienteId);
    }

    [Fact]
    public void FiltrosBusqueda_EncuentraPorDocumentoNumerico()
    {
        // Documentos numéricos — sin diacríticos, el comportamiento no cambia
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, documento: "12345678"),
            Cliente(2, documento: "87654321"),
            Cliente(3, documento: "12349999")
        };
        var filtros = new FiltrosBandejaClientes { Busqueda = "1234" };

        var resultado = MoraService.AplicarFiltros(lista, filtros, Hoy);

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public void FiltrosBusqueda_EsCaseInsensitive()
    {
        // Sin diacríticos, case-insensitive sigue funcionando igual
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, nombre: "GARCIA, JUAN"),
            Cliente(2, nombre: "López, María")
        };
        var filtros = new FiltrosBandejaClientes { Busqueda = "garcia" };

        var resultado = MoraService.AplicarFiltros(lista, filtros, Hoy);

        Assert.Single(resultado);
    }

    // ---------------------------------------------------------------------------
    // AplicarFiltros — SinContactoReciente con fechaLimite
    // ---------------------------------------------------------------------------

    [Fact]
    public void FiltrosSinContactoReciente_DejaClientesSinContactoOContactoAntiguo()
    {
        // Hoy = 2026-03-21, DiasSinContacto = 10 → fechaLimite = 2026-03-11
        // UltimoContacto null → incluido
        // UltimoContacto = 2026-03-01 (< fechaLimite) → incluido
        // UltimoContacto = 2026-03-15 (>= fechaLimite) → excluido
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, ultimoContacto: null),
            Cliente(2, ultimoContacto: new DateTime(2026, 3, 1)),
            Cliente(3, ultimoContacto: new DateTime(2026, 3, 15))
        };
        var filtros = new FiltrosBandejaClientes
        {
            SinContactoReciente = true,
            DiasSinContacto = 10
        };

        var resultado = MoraService.AplicarFiltros(lista, filtros, Hoy);

        Assert.Equal(2, resultado.Count);
        Assert.DoesNotContain(resultado, c => c.ClienteId == 3);
    }

    [Fact]
    public void FiltrosSinContactoReciente_SinDiasSinContacto_NoAplicaFiltro()
    {
        // SinContactoReciente = true pero DiasSinContacto = null → condición no se cumple
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, ultimoContacto: null),
            Cliente(2, ultimoContacto: new DateTime(2026, 1, 1))
        };
        var filtros = new FiltrosBandejaClientes
        {
            SinContactoReciente = true,
            DiasSinContacto = null
        };

        var resultado = MoraService.AplicarFiltros(lista, filtros, Hoy);

        Assert.Equal(2, resultado.Count);
    }

    // ---------------------------------------------------------------------------
    // AplicarOrdenamiento — por prioridad descendente (default)
    // ---------------------------------------------------------------------------

    [Fact]
    public void OrdenamientoDefault_OrdenaPorPrioridadDescendente()
    {
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, prioridad: PrioridadAlerta.Baja),
            Cliente(2, prioridad: PrioridadAlerta.Critica),
            Cliente(3, prioridad: PrioridadAlerta.Media)
        };

        var resultado = MoraService.AplicarOrdenamiento(lista, ordenamiento: null);

        Assert.Equal(PrioridadAlerta.Critica, resultado[0].PrioridadMaxima);
        Assert.Equal(PrioridadAlerta.Media, resultado[1].PrioridadMaxima);
        Assert.Equal(PrioridadAlerta.Baja, resultado[2].PrioridadMaxima);
    }

    [Fact]
    public void OrdenamientoPrioridadDesc_EsEquivalenteAlDefault()
    {
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, prioridad: PrioridadAlerta.Baja),
            Cliente(2, prioridad: PrioridadAlerta.Alta)
        };

        var porDefault = MoraService.AplicarOrdenamiento(lista, ordenamiento: null);
        var porExplicito = MoraService.AplicarOrdenamiento(lista, ordenamiento: "PrioridadDesc");

        Assert.Equal(porDefault.Select(c => c.ClienteId), porExplicito.Select(c => c.ClienteId));
    }

    // ---------------------------------------------------------------------------
    // AplicarOrdenamiento — por días atraso
    // ---------------------------------------------------------------------------

    [Fact]
    public void OrdenamientoDiasAtrasoDesc_OrdenaDescendente()
    {
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, diasAtraso: 10),
            Cliente(2, diasAtraso: 90),
            Cliente(3, diasAtraso: 45)
        };

        var resultado = MoraService.AplicarOrdenamiento(lista, "DiasAtrasoDesc");

        Assert.Equal(90, resultado[0].DiasMaxAtraso);
        Assert.Equal(45, resultado[1].DiasMaxAtraso);
        Assert.Equal(10, resultado[2].DiasMaxAtraso);
    }

    [Fact]
    public void OrdenamientoDiasAtrasoAsc_OrdenaAscendente()
    {
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, diasAtraso: 90),
            Cliente(2, diasAtraso: 10),
            Cliente(3, diasAtraso: 45)
        };

        var resultado = MoraService.AplicarOrdenamiento(lista, "DiasAtrasoAsc");

        Assert.Equal(10, resultado[0].DiasMaxAtraso);
        Assert.Equal(45, resultado[1].DiasMaxAtraso);
        Assert.Equal(90, resultado[2].DiasMaxAtraso);
    }

    // ---------------------------------------------------------------------------
    // AplicarOrdenamiento — por monto (MontoTotal = MontoVencido + MontoMora)
    // ---------------------------------------------------------------------------

    [Fact]
    public void OrdenamientoMontoDesc_OrdenaPorMontoTotalDescendente()
    {
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, montoVencido: 500m),
            Cliente(2, montoVencido: 3_000m),
            Cliente(3, montoVencido: 1_000m)
        };

        var resultado = MoraService.AplicarOrdenamiento(lista, "MontoDesc");

        Assert.Equal(3_000m, resultado[0].MontoVencido);
        Assert.Equal(1_000m, resultado[1].MontoVencido);
        Assert.Equal(500m, resultado[2].MontoVencido);
    }

    // ---------------------------------------------------------------------------
    // AplicarOrdenamiento — por nombre
    // ---------------------------------------------------------------------------

    [Fact]
    public void OrdenamientoNombreAsc_OrdenaPorNombreAlfabetico()
    {
        var lista = new List<ClienteMoraViewModel>
        {
            Cliente(1, nombre: "Zeta, Juan"),
            Cliente(2, nombre: "Alpha, María"),
            Cliente(3, nombre: "Beta, Pedro")
        };

        var resultado = MoraService.AplicarOrdenamiento(lista, "NombreAsc");

        Assert.Equal("Alpha, María", resultado[0].Nombre);
        Assert.Equal("Beta, Pedro", resultado[1].Nombre);
        Assert.Equal("Zeta, Juan", resultado[2].Nombre);
    }
}
