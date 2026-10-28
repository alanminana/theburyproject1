using System.ComponentModel.DataAnnotations;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Mora;

namespace TheBuryProject.Tests.Unit;

/// <summary>VALID-L5: montos y fechas de promesas/acuerdos de Mora y límites de los planes de pago globales.</summary>
public class MoraYPlanesCamposValidacionTests
{
    private static bool Falla(object vm, string propiedad)
    {
        var resultados = new List<ValidationResult>();
        Validator.TryValidateObject(vm, new ValidationContext(vm), resultados, validateAllProperties: true);
        return resultados.Any(r => r.MemberNames.Contains(propiedad));
    }

    private static RegistrarPromesaViewModel Promesa(DateTime fecha, decimal monto = 1000m) => new()
    {
        AlertaId = 1, ClienteId = 1, FechaPromesa = fecha, MontoPromesa = monto,
    };

    private static CrearAcuerdoViewModel Acuerdo(DateTime primeraCuota, decimal entrega = 0m) => new()
    {
        AlertaId = 1, ClienteId = 1, CreditoId = 1, CantidadCuotas = 3, FechaPrimeraCuota = primeraCuota, MontoEntregaInicial = entrega,
    };

    // ---- Promesa ----

    [Theory]
    [InlineData(0, false)]      // hoy
    [InlineData(-1, false)]     // tolerancia por huso horario (UTC vs hora local de Argentina)
    [InlineData(30, false)]
    [InlineData(365, false)]
    [InlineData(-2, true)]
    [InlineData(-400, true)]
    [InlineData(400, true)]
    public void Promesa_FechaDePago_Ventana(int dias, bool invalido)
    {
        var vm = Promesa(DateTime.UtcNow.Date.AddDays(dias));
        Assert.Equal(invalido, Falla(vm, nameof(vm.FechaPromesa)));
    }

    [Theory]
    [InlineData(0.01, false)]
    [InlineData(20_000_000, false)]
    [InlineData(0, true)]
    [InlineData(-5, true)]
    [InlineData(20_000_001, true)]
    [InlineData(100.555, true)]   // más de 2 decimales
    public void Promesa_Monto_Rango(double monto, bool invalido)
    {
        var vm = Promesa(DateTime.UtcNow.Date, (decimal)monto);
        Assert.Equal(invalido, Falla(vm, nameof(vm.MontoPromesa)));
    }

    // ---- Acuerdo ----

    [Theory]
    [InlineData(30, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(-2, true)]
    [InlineData(400, true)]
    public void Acuerdo_FechaPrimeraCuota_Ventana(int dias, bool invalido)
    {
        var vm = Acuerdo(DateTime.UtcNow.Date.AddDays(dias));
        Assert.Equal(invalido, Falla(vm, nameof(vm.FechaPrimeraCuota)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(5000, false)]
    [InlineData(-1, true)]
    [InlineData(20_000_001, true)]
    public void Acuerdo_EntregaInicial_Rango(double entrega, bool invalido)
    {
        var vm = Acuerdo(DateTime.UtcNow.Date.AddDays(30), (decimal)entrega);
        Assert.Equal(invalido, Falla(vm, nameof(vm.MontoEntregaInicial)));
    }

    // ---- Planes de pago globales ----

    private static PlanPagoGlobalCommandViewModel Plan(int cuotas, decimal ajuste) => new()
    {
        ConfiguracionPagoId = 1, CantidadCuotas = cuotas, AjustePorcentaje = ajuste,
    };

    [Theory]
    [InlineData(1, false)]
    [InlineData(12, false)]
    [InlineData(60, false)]
    [InlineData(0, true)]
    [InlineData(61, true)]
    [InlineData(int.MaxValue, true)]
    public void Plan_CantidadDeCuotas_Rango(int cuotas, bool invalido)
    {
        var vm = Plan(cuotas, 5m);
        Assert.Equal(invalido, Falla(vm, nameof(vm.CantidadCuotas)));
    }

    [Theory]
    [InlineData(-5, false)]
    [InlineData(45, false)]
    [InlineData(0, false)]
    [InlineData(-99.9999, false)]
    [InlineData(-100, true)]      // dejaría el total en cero
    [InlineData(-150, true)]
    [InlineData(1000, true)]
    public void Plan_AjustePorcentaje_Rango(double ajuste, bool invalido)
    {
        var vm = Plan(3, (decimal)ajuste);
        Assert.Equal(invalido, Falla(vm, nameof(vm.AjustePorcentaje)));
    }

    // ---- Plantilla de contrato de crédito ----

    private static PlantillaContratoCreditoViewModel Plantilla(DateTime desde, DateTime? hasta, string? cuit = "20-37898524-3") => new()
    {
        Nombre = "Plantilla", NombreVendedor = "Vendedor", DomicilioVendedor = "Calle 1", CiudadFirma = "CABA", Jurisdiccion = "CABA",
        InteresMoraDiarioPorcentaje = 0.5m, TextoContrato = "x", TextoPagare = "y", VigenteDesde = desde, VigenteHasta = hasta, CuitVendedor = cuit,
    };

    [Theory]
    [InlineData("20-37898524-3", false)]
    [InlineData("20378985243", false)]
    [InlineData("123", true)]
    [InlineData("20-1234-5", true)]
    public void Plantilla_CuitDelVendedor_Forma(string cuit, bool invalido)
    {
        var vm = Plantilla(new DateTime(2025, 1, 1), null, cuit);
        Assert.Equal(invalido, Falla(vm, nameof(vm.CuitVendedor)));
    }

    [Fact]
    public void Plantilla_VigenteHastaAnteriorADesde_SeRechaza()
    {
        var vm = Plantilla(new DateTime(2026, 6, 1), new DateTime(2026, 5, 1));
        Assert.True(Falla(vm, nameof(vm.VigenteHasta)));
    }

    [Fact]
    public void Plantilla_VigenciaOrdenada_SeAcepta()
    {
        var vm = Plantilla(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
        Assert.False(Falla(vm, nameof(vm.VigenteHasta)));
        Assert.False(Falla(vm, nameof(vm.VigenteDesde)));
    }

    [Fact]
    public void Plantilla_DniSoloDigitos_SeAcepta_YConPuntosSeRechaza()
    {
        var vm = Plantilla(new DateTime(2026, 1, 1), null);
        vm.DniVendedor = "37898524";
        Assert.False(Falla(vm, nameof(vm.DniVendedor)));

        // Regla vigente (Dni_Invalido): 7 u 8 dígitos numéricos. Por eso la empresa guarda su DNI sin puntos.
        vm.DniVendedor = "37.898.524";
        Assert.True(Falla(vm, nameof(vm.DniVendedor)));
    }

    // ---- Configuración de mora ----

    [Theory]
    [InlineData(3, 7, 15, false)]
    [InlineData(3, 3, 3, false)]
    [InlineData(null, 7, null, false)]
    [InlineData(10, 5, 15, true)]    // alta < media
    [InlineData(3, 15, 7, true)]     // crítica < alta
    public void ConfigMora_DiasDePrioridad_Escalan(int? media, int? alta, int? critica, bool invalido)
    {
        var vm = new ConfiguracionMoraExpandidaViewModel
        {
            DiasParaPrioridadMedia = media, DiasParaPrioridadAlta = alta, DiasParaPrioridadCritica = critica,
        };
        Assert.Equal(invalido, Falla(vm, nameof(vm.DiasParaPrioridadAlta)) || Falla(vm, nameof(vm.DiasParaPrioridadCritica)));
    }

    [Theory]
    [InlineData(1000.0, 5000.0, 20000.0, false)]
    [InlineData(5000.0, 1000.0, null, true)]
    [InlineData(1000.0, 5000.0, 2000.0, true)]
    public void ConfigMora_MontosDePrioridad_Escalan(double media, double alta, double? critica, bool invalido)
    {
        var vm = new ConfiguracionMoraExpandidaViewModel
        {
            MontoParaPrioridadMedia = (decimal)media, MontoParaPrioridadAlta = (decimal)alta,
            MontoParaPrioridadCritica = critica.HasValue ? (decimal)critica.Value : null,
        };
        Assert.Equal(invalido, Falla(vm, nameof(vm.MontoParaPrioridadAlta)) || Falla(vm, nameof(vm.MontoParaPrioridadCritica)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(20_000_000, false)]
    [InlineData(20_000_001, true)]
    [InlineData(-1, true)]
    public void ConfigMora_UmbralesDeMonto_Tope(double monto, bool invalido)
    {
        var vm = new ConfiguracionMoraExpandidaViewModel { MoraMinima = (decimal)monto, MontoParaPrioridadMedia = (decimal)monto };
        Assert.Equal(invalido, Falla(vm, nameof(vm.MoraMinima)) && Falla(vm, nameof(vm.MontoParaPrioridadMedia)));
    }
}
