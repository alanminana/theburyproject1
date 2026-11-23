using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Documentos;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// MORA-CONTRACTUAL — el interés por mora impreso en contratos y pagarés sale de la configuración de punitorios vigente
/// (la misma que devenga la deuda real), admite 0 %, no usa <c>ConfiguracionMora.TasaMoraBase</c> ni valores semilla,
/// y no se confunde con el interés financiero del plan de cuotas. Datos 100 % sintéticos.
/// </summary>
public class MoraContractualMotorTests : DocumentoTestBase
{
    private const string Clausula = "respecto de la falta de pago en término del importe adeudado: ";

    private async Task PonerTasaMoraBaseAsync(decimal? tasa)
    {
        Context.ConfiguracionesMora.Add(new ConfiguracionMora { TasaMoraBase = tasa, DiasGracia = 3 });
        await Context.SaveChangesAsync();
    }

    /// <summary>El arnés siembra un contrato mínimo: se le agrega la cláusula con las variables que usa el texto real.</summary>
    private async Task AgregarClausulaAsync(string variables)
    {
        var versiones = await Context.PlantillasDocumentoVersion
            .Where(v => v.PlantillaDocumento.TipoDocumento.Codigo == "CONTRATO").ToListAsync();
        Assert.NotEmpty(versiones);
        foreach (var v in versiones)
            v.Contenido += "\n" + Clausula + variables;
        await Context.SaveChangesAsync();
    }

    private async Task<string> RenderContratoAsync()
    {
        var venta = await SembrarVentaAsync();
        var r = await EmitirVentaAsync(venta.Id);
        var tipoContrato = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "CONTRATO");
        return r.Generados.First(d => d.TipoDocumentoId == tipoContrato.Id).ContenidoRenderizado;
    }

    private async Task<bool> ContratoEmitidoAsync()
    {
        var tipoContrato = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "CONTRATO");
        return await Context.DocumentosGenerados.AnyAsync(d => d.TipoDocumentoId == tipoContrato.Id);
    }

    [Fact]
    public async Task Punitorio0Porciento_ElContratoDiceQueNoSeDevengaInteres_AunConTasaMoraBase5()
    {
        await SembrarConfiguracionInicialAsync();
        await PonerTasaMoraBaseAsync(5m);
        await AgregarClausulaAsync("{{contrato.interesMoraTexto}}.");

        var contenido = await RenderContratoAsync();

        Assert.Contains(Clausula + "a la fecha de emisión de este documento no se devenga interés punitorio por la falta de pago en término (0 %)", contenido);
        Assert.Contains("vigente desde el 01/01/2020", contenido); // vigencia de la configuración al emitir
        Assert.DoesNotContain("5,00", contenido);                  // la tasa por defecto de Mora no se imprime
        Assert.DoesNotContain("0,50", contenido);                  // ni la semilla de la plantilla/empresa
    }

    [Fact]
    public async Task PlantillaAntigua_ConInteresDiario_Punitorio0_Imprime0YNoUnaSemilla()
    {
        await SembrarConfiguracionInicialAsync();
        await PonerTasaMoraBaseAsync(5m);
        await AgregarClausulaAsync("{{contrato.interesMoraDiario}}");

        var contenido = await RenderContratoAsync();

        Assert.Contains(Clausula + "0,00%", contenido);
    }

    [Fact]
    public async Task CambioDeTasa_ElContratoNuevoUsaLaVigente_YElEmitidoConservaSuTexto()
    {
        await SembrarConfiguracionInicialAsync();
        await AgregarClausulaAsync("{{contrato.interesMoraTexto}}.");
        var primero = await RenderContratoAsync();
        Assert.Contains("(0 %)", primero);

        // Nueva versión (append-only) con vigencia posterior: 3 % cada 30 días, gracia 5.
        await SembrarPunitorioAsync(porcentaje: 3m, periodoDias: 30, diasGracia: 5, vigenteDesde: new DateOnly(2021, 1, 1));
        var segundo = await RenderContratoAsync();

        Assert.Contains("un interés punitorio del 3,00 % cada 30 día(s)", segundo);
        Assert.Contains("5 día(s) de gracia", segundo);
        Assert.Contains("vigente desde el 01/01/2021", segundo);
        Assert.DoesNotContain("no se devenga", segundo);
        var tipo = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "CONTRATO");
        var viejo = await Context.DocumentosGenerados.AsNoTracking().OrderBy(d => d.Id).FirstAsync(d => d.TipoDocumentoId == tipo.Id);
        Assert.Contains("(0 %)", viejo.ContenidoRenderizado); // el documento histórico no se reescribe
    }

    [Fact]
    public async Task PlantillaAntigua_ConInteresDiario_ConPunitorioPositivo_NoSeEmite_PorqueElEquivalenteDiarioNoEsExacto()
    {
        await SembrarConfiguracionInicialAsync();
        await SembrarPunitorioAsync(porcentaje: 10m, periodoDias: 30, diasGracia: 0, vigenteDesde: new DateOnly(2021, 1, 1));
        await AgregarClausulaAsync("{{contrato.interesMoraDiario}}");
        var venta = await SembrarVentaAsync();

        var ex = await Record.ExceptionAsync(() => EmitirVentaAsync(venta.Id));

        Assert.False(await ContratoEmitidoAsync());
        Assert.NotNull(ex); // el contrato es obligatorio: la emisión falla en vez de imprimir una cláusula en blanco o inexacta
    }

    [Theory]
    [InlineData(false)] // ausente
    [InlineData(true)]  // inactiva
    public async Task SinPunitorioVigenteYActivo_ElContratoNoSeEmite_NuncaSeInventaUnaTasa(bool conConfiguracionInactiva)
    {
        // Contrato sembrado SIN configuración activa de punitorios (no se usa SembrarConfiguracionInicialAsync, que siembra una).
        Context.PlantillasContratoCredito.Add(new PlantillaContratoCredito
        {
            Nombre = "Plantilla base", Activa = true, NombreVendedor = "The Bury SA", DomicilioVendedor = "Av. Siempre Viva 742",
            CiudadFirma = "Rosario", Jurisdiccion = "Santa Fe", InteresMoraDiarioPorcentaje = 0.5m,
            TextoContrato = "CONTRATO {{documento.numero}}", TextoPagare = "PAGARE {{documento.numero}}",
            VigenteDesde = DateTime.UtcNow.Date.AddDays(-1)
        });
        await Context.SaveChangesAsync();
        if (conConfiguracionInactiva)
            await SembrarPunitorioAsync(activa: false);
        await DocumentoSeeder.EnsureAsync(Context, NullLogger.Instance);
        await PonerTasaMoraBaseAsync(5m);
        await AgregarClausulaAsync("{{contrato.interesMoraTexto}}.");
        var venta = await SembrarVentaAsync();

        var ex = await Record.ExceptionAsync(() => EmitirVentaAsync(venta.Id));

        Assert.False(await ContratoEmitidoAsync());
        Assert.NotNull(ex);
    }

    [Theory]
    [InlineData("{{ contrato.interesMoraDiario }}")]
    [InlineData("{{contrato.interesMoraDiario|upper}}")]
    [InlineData("{{ INTERES_MORA }}")]
    [InlineData("{{ contrato.interesMoraTexto | upper }}")]
    public async Task LaGuardaDeInteres_ReconoceEspaciosYFiltros_ConPunitorioPositivoOSinConfiguracion(string variable)
    {
        await SembrarConfiguracionInicialAsync();
        await SembrarPunitorioAsync(porcentaje: 10m, periodoDias: 30, diasGracia: 0, vigenteDesde: new DateOnly(2021, 1, 1));
        // Solo las variables diarias antiguas deben bloquearse con tasa positiva; la de texto nuevo sí tiene valor.
        var esDiaria = variable.Contains("Diario", StringComparison.OrdinalIgnoreCase) || variable.Contains("INTERES_MORA");
        await AgregarClausulaAsync(variable);
        var venta = await SembrarVentaAsync();

        var ex = await Record.ExceptionAsync(() => EmitirVentaAsync(venta.Id));

        if (esDiaria)
        {
            Assert.NotNull(ex);
            Assert.False(await ContratoEmitidoAsync());
        }
        else
        {
            Assert.Null(ex);
            Assert.True(await ContratoEmitidoAsync());
        }
    }

    [Fact]
    public async Task Emision_ConVariablesDesglosadasSinDeclarar_SinPunitorioActivo_NoSeEmite()
    {
        // Plantilla que usa solo porcentaje/período/gracia (sin VariablesRequeridas): sin configuración activa no puede quedar en blanco.
        await SembrarConfiguracionInicialAsync();
        var config = await Context.ConfiguracionesPunitorio.SingleAsync();
        config.Activa = false;
        await Context.SaveChangesAsync();
        await AgregarClausulaAsync("{{contrato.interesMoraPorcentaje}} cada {{contrato.interesMoraPeriodoDias}} días, gracia {{ contrato.interesMoraGraciaDias }}.");
        var venta = await SembrarVentaAsync();

        var ex = await Record.ExceptionAsync(() => EmitirVentaAsync(venta.Id));

        Assert.NotNull(ex);
        Assert.False(await ContratoEmitidoAsync());
    }

    [Fact]
    public async Task Regenerar_ConVariablesDesglosadas_UsaLasDelOriginal_YSiElOriginalNoLasTieneNoRegenera()
    {
        await SembrarConfiguracionInicialAsync();
        await AgregarClausulaAsync("Punitorio {{contrato.interesMoraPorcentaje}} cada {{contrato.interesMoraPeriodoDias}} días.");
        var venta = await SembrarVentaAsync();
        var r = await EmitirVentaAsync(venta.Id);
        var tipo = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "CONTRATO");
        var original = r.Generados.First(d => d.TipoDocumentoId == tipo.Id);
        Assert.Contains("Punitorio 0,00% cada 30 días.", original.ContenidoRenderizado);

        await SembrarPunitorioAsync(porcentaje: 3m, periodoDias: 15, diasGracia: 5, vigenteDesde: new DateOnly(2021, 1, 1));
        var nuevo = await Motor.RegenerarAsync(original.Id, "reimpresión");
        Assert.Contains("Punitorio 0,00% cada 30 días.", nuevo.ContenidoRenderizado);

        // Un original sin esas condiciones en su snapshot (emitido antes de este cambio) no se regenera con las de hoy.
        var sinCondiciones = await Context.DocumentosGenerados.FirstAsync(d => d.Id == nuevo.Id);
        sinCondiciones.DatosSnapshotJson = "{\"valores\":{},\"colecciones\":{}}";
        await Context.SaveChangesAsync();
        var ex = await Record.ExceptionAsync(() => Motor.RegenerarAsync(sinCondiciones.Id, "otra"));
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task Regenerar_ConservaLasCondicionesDeMoraDelOriginal_NoLasVigentesHoy()
    {
        await SembrarConfiguracionInicialAsync();
        await AgregarClausulaAsync("{{contrato.interesMoraTexto}}.");
        var venta = await SembrarVentaAsync();
        var r = await EmitirVentaAsync(venta.Id);
        var tipo = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "CONTRATO");
        var original = r.Generados.First(d => d.TipoDocumentoId == tipo.Id);
        Assert.Contains("(0 %)", original.ContenidoRenderizado);

        // Después de emitir se aprueba una tasa nueva.
        await SembrarPunitorioAsync(porcentaje: 3m, periodoDias: 30, diasGracia: 5, vigenteDesde: new DateOnly(2021, 1, 1));
        var nuevo = await Motor.RegenerarAsync(original.Id, "reimpresión con corrección de domicilio");

        Assert.Contains("no se devenga interés punitorio", nuevo.ContenidoRenderizado);
        Assert.DoesNotContain("3,00 %", nuevo.ContenidoRenderizado);
    }

    [Fact]
    public async Task ElTextoConPunitorioPositivo_NoPromete_Capital_PorqueElCalculadorUsaElTotalDeLaCuota()
    {
        await SembrarConfiguracionInicialAsync();
        await SembrarPunitorioAsync(porcentaje: 3m, periodoDias: 30, diasGracia: 5, vigenteDesde: new DateOnly(2021, 1, 1));
        await AgregarClausulaAsync("{{contrato.interesMoraTexto}}.");

        var contenido = await RenderContratoAsync();

        Assert.Contains("sobre el saldo impago de cada cuota vencida", contenido);
        var clausula = contenido[contenido.IndexOf(Clausula, StringComparison.Ordinal)..];
        Assert.DoesNotContain("capital", clausula, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InteresFinancieroDelPlan_NoSeConfundeConElInteresPorMora()
    {
        await SembrarConfiguracionInicialAsync();
        await AgregarClausulaAsync("{{contrato.interesMoraTexto}}.");
        var venta = await SembrarVentaAsync();
        var credito = await Context.Creditos.FirstAsync(c => c.Id == venta.CreditoId);
        credito.TasaInteres = 7.77m; // recargo financiero del plan: concepto independiente del punitorio
        await Context.SaveChangesAsync();

        var r = await EmitirVentaAsync(venta.Id);
        var tipo = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "CONTRATO");
        var contenido = r.Generados.First(d => d.TipoDocumentoId == tipo.Id).ContenidoRenderizado;

        Assert.DoesNotContain("7,77", contenido);
        Assert.Contains("no se devenga interés punitorio", contenido);
    }

    [Fact]
    public async Task GuardarEmpresa_NoCopiaTasaMoraBase_NiALaEmpresaNiALaPlantillaLegada()
    {
        await SembrarConfiguracionInicialAsync();
        await PonerTasaMoraBaseAsync(5m);
        var legadaAntes = (await Context.PlantillasContratoCredito.AsNoTracking().FirstAsync(p => p.Activa)).InteresMoraDiarioPorcentaje;

        await Config.GuardarEmpresaAsync(new EmpresaInput
        {
            Nombre = "Empresa Nueva SA", Cuit = "30-99999999-1", Domicilio = "Calle Nueva 1", Ciudad = "Cordoba", Jurisdiccion = "Cordoba"
        });

        Context.ChangeTracker.Clear();
        var legada = await Context.PlantillasContratoCredito.AsNoTracking().FirstAsync(p => p.Activa);
        Assert.Equal(legadaAntes, legada.InteresMoraDiarioPorcentaje);
        var empresa = await Config.ObtenerEmpresaAsync();
        Assert.NotEqual(5m, empresa!.InteresMoraDiarioPorcentaje);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(12, "12")]
    public void Texto_ExpresaElCalculoRealDelPunitorio(int gracia, string? graciaEsperada)
    {
        var interes = new InteresMoraContractual(EstadoConfiguracionPunitorio.Activa, 10m, 20, gracia, new DateOnly(2026, 3, 1));

        var texto = interes.Texto!;

        Assert.Contains("10,00 % cada 20 día(s)", texto);
        Assert.Contains("calculado día a día", texto);
        Assert.Contains("vigente desde el 01/03/2026", texto);
        if (graciaEsperada is null) Assert.Contains("a partir del día siguiente al vencimiento", texto);
        else Assert.Contains($"{graciaEsperada} día(s) de gracia", texto);
        Assert.Null(interes.EquivalenteDiarioExacto); // con tasa positiva no hay equivalente diario exacto
    }

    [Fact]
    public void SinConfiguracionActiva_NoHayTexto()
    {
        Assert.False(InteresMoraContractual.NoDisponible.Disponible);
        Assert.Null(InteresMoraContractual.NoDisponible.Texto);
        Assert.False(new InteresMoraContractual(EstadoConfiguracionPunitorio.Inactiva, 0m, 30, 12, new DateOnly(2026, 1, 1)).Disponible);
    }
}
