using Microsoft.EntityFrameworkCore;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Documentos;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// MORA-CONTRACTUAL-DIAG (2026-11-23) — pruebas de CARACTERIZACIÓN del comportamiento actual de
/// <c>ConfiguracionMora.TasaMoraBase</c> como fuente del interés contractual impreso en documentos.
///
/// No fijan una regla comercial ni una tasa: documentan lo que hoy hace el sistema para que, cuando el
/// propietario decida la alternativa A/B (ver docs/mora-contractual-diagnostico-20261123.md), el cambio
/// falle de forma deliberada en estas pruebas y se actualicen junto con la decisión. Datos 100 % sintéticos.
/// </summary>
public class MoraContractualCaracterizacionTests : DocumentoTestBase
{
    private async Task PonerTasaMoraAsync(decimal? tasa)
    {
        Context.ConfiguracionesMora.Add(new ConfiguracionMora { TasaMoraBase = tasa, DiasGracia = 3 });
        await Context.SaveChangesAsync();
    }

    /// <summary>Punitorio efectivo aprobado: 0 % cada 30 días, gracia 12 (única fuente de la deuda punitoria real).</summary>
    private async Task PonerPunitorioCeroAsync()
    {
        Context.ConfiguracionesPunitorio.Add(new ConfiguracionPunitorio
        {
            Porcentaje = 0m, PeriodoDias = 30, DiasGracia = 12, ProrrateoDiario = true, Activa = true,
            VigenteDesde = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();
    }

    /// <summary>
    /// El arnés siembra un contrato mínimo sin la cláusula de interés: se le agrega la misma variable que usa el texto
    /// de referencia (<c>{{contrato.interesMoraDiario}}</c>) para observar qué valor imprimiría.
    /// </summary>
    private async Task AgregarClausulaDeInteresAsync()
    {
        var versiones = await Context.PlantillasDocumentoVersion
            .Where(v => v.PlantillaDocumento.TipoDocumento.Codigo == "CONTRATO").ToListAsync();
        Assert.NotEmpty(versiones);
        foreach (var v in versiones)
            v.Contenido += "\n" + FraseInteres + "{{contrato.interesMoraDiario}} sobre las cuotas adeudadas.";
        await Context.SaveChangesAsync();
    }

    private async Task<string> RenderContratoAsync()
    {
        await AgregarClausulaDeInteresAsync();
        var venta = await SembrarVentaAsync();
        var r = await EmitirVentaAsync(venta.Id);
        var tipoContrato = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "CONTRATO");
        return r.Generados.First(d => d.TipoDocumentoId == tipoContrato.Id).ContenidoRenderizado;
    }

    private const string FraseInteres = "interés diario a favor del vendedor del ";

    [Fact]
    public async Task TasaMoraBase5_ImprimeCincoPorCientoDiarioEnElContrato_AunConPunitorioEfectivoCero()
    {
        // La inconsistencia del diagnóstico: el texto contractual dice «5 % diario» y la deuda punitoria real es $0.
        await SembrarConfiguracionInicialAsync();
        await PonerPunitorioCeroAsync();
        await PonerTasaMoraAsync(5m);

        var contenido = await RenderContratoAsync();

        Assert.Contains(FraseInteres + "5,00%", contenido);
        var punitorio = await Context.ConfiguracionesPunitorio.SingleAsync();
        Assert.Equal(0m, punitorio.Porcentaje); // el punitorio NO lo lee el contrato
    }

    [Theory]
    [InlineData(0)]
    [InlineData(null)]
    public async Task TasaMoraBaseCeroONula_NoEsSinInteres_CaeAlValorDeEmpresa(int? tasaEntera)
    {
        // `tasa > 0` falla y se usa el valor de empresa (semilla de la plantilla legada: 0,50 en este arnés).
        await SembrarConfiguracionInicialAsync();
        await PonerTasaMoraAsync(tasaEntera);

        var contenido = await RenderContratoAsync();

        Assert.Contains(FraseInteres + "0,50%", contenido);
    }

    [Fact]
    public async Task TasaMoraBaseCero_ConEmpresaEnCero_ElMotorNuevoImprimeCeroPorCiento_SinValidar()
    {
        // El motor documental nuevo no valida interés > 0: a diferencia del contrato anterior, emite «0,00%».
        await SembrarConfiguracionInicialAsync();
        var empresa = await Context.EmpresasConfiguracion.SingleAsync();
        empresa.InteresMoraDiarioPorcentaje = 0m;
        await PonerTasaMoraAsync(0m);

        var contenido = await RenderContratoAsync();

        Assert.Contains(FraseInteres + "0,00%", contenido);
    }

    [Fact]
    public async Task DocumentoYaEmitido_ConservaElTextoCongelado_AunqueCambieTasaMoraBase()
    {
        // Compatibilidad histórica: el contenido renderizado se persiste; cambiar la tasa no reescribe documentos emitidos.
        await SembrarConfiguracionInicialAsync();
        await PonerTasaMoraAsync(5m);
        await AgregarClausulaDeInteresAsync();
        var venta = await SembrarVentaAsync();
        var r = await EmitirVentaAsync(venta.Id);
        var emitidoId = r.Generados.First(d => d.ContenidoRenderizado.Contains(FraseInteres)).Id;

        var config = await Context.ConfiguracionesMora.SingleAsync();
        config.TasaMoraBase = 0m;
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var persistido = await Context.DocumentosGenerados.AsNoTracking().FirstAsync(d => d.Id == emitidoId);
        Assert.Contains(FraseInteres + "5,00%", persistido.ContenidoRenderizado);
    }
}
