using System.Reflection;
using TheBuryProject.Tests.Concurrency;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// QA-FIN-RESIDUAL — guardián del REGISTRO de defectos conocidos. No toca ningún servicio ni base de datos:
/// solo lee por reflexión los metadatos de los tests de evidencia para que nadie pueda interpretar
/// "9 Hallazgo_ PASS" como "9 bugs corregidos" ni borrar un hallazgo abierto sin que CI lo note.
///
/// Los 6 tests <c>Hallazgo_</c> PASAN porque REPRODUCEN defectos financieros VIGENTES (estado ABIERTO, PR correctiva
/// PENDIENTE). Los 3 hallazgos H-DEV-1/2/3 (CR-3) fueron CORREGIDOS en FIN-DEV-P0 y sus pruebas son <c>Regresion_</c>
/// (exigen el comportamiento correcto; no se eliminó la evidencia histórica, ver docs/qa-fin-residual-registro-defectos.md).
/// Los 6 <c>Contrato_</c> fijan comportamiento correcto. Los 10 escenarios R-xx de SQL Server están
/// NO EJECUTADOS / BLOQUEADOS: omitirlos no equivale a PASS. Ver docs/qa-fin-residual-registro-defectos.md.
///
/// Al corregir un defecto, la PR correctiva debe: invertir la aserción del Hallazgo_, quitar su trait
/// DefectoConocido, renombrarlo a Regresion_* y actualizar las cifras esperadas de este registro.
/// </summary>
public sealed class FinResidualRegistroDefectosConocidosTests
{
    private const int HallazgosAbiertos = 6;
    private const int HallazgosCorregidos = 3;
    private const int CausasRaiz = 4;
    private const int Contratos = 6;
    private const int EscenariosSqlNoEjecutados = 10;

    private readonly ITestOutputHelper _out;

    public FinResidualRegistroDefectosConocidosTests(ITestOutputHelper output) => _out = output;

    private static IEnumerable<(MethodInfo Metodo, Dictionary<string, string> Traits, string DisplayName)> Metodos(Type tipo)
    {
        foreach (var m in tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var fact = m.GetCustomAttribute<FactAttribute>();
            if (fact == null) continue;

            var traits = m.GetCustomAttributesData()
                .Where(a => a.AttributeType == typeof(TraitAttribute))
                .ToDictionary(a => (string)a.ConstructorArguments[0].Value!, a => (string)a.ConstructorArguments[1].Value!);
            yield return (m, traits, fact.DisplayName ?? string.Empty);
        }
    }

    [Fact(DisplayName = "REGISTRO: 6 hallazgos ABIERTOS en 4 causas raiz (pasar NO significa corregido)")]
    public void Registro_SeisHallazgosAbiertos_EnCuatroCausasRaiz()
    {
        var hallazgos = Metodos(typeof(FinResidualCajaDevolucionAnticipoTests))
            .Where(x => x.Metodo.Name.StartsWith("Hallazgo_", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(HallazgosAbiertos, hallazgos.Count);
        foreach (var h in hallazgos)
        {
            Assert.Equal("DefectoConocido", h.Traits.GetValueOrDefault("Categoria"));
            Assert.Equal("ABIERTO", h.Traits.GetValueOrDefault("Estado"));
            Assert.False(string.IsNullOrWhiteSpace(h.Traits.GetValueOrDefault("Hallazgo")), h.Metodo.Name);
            Assert.False(string.IsNullOrWhiteSpace(h.Traits.GetValueOrDefault("CausaRaiz")), h.Metodo.Name);
            Assert.StartsWith("DEFECTO CONOCIDO ABIERTO", h.DisplayName);
        }

        // Ningún hallazgo CORREGIDO puede seguir figurando como abierto, ni al revés.
        var abiertos = hallazgos.Select(h => h.Traits["Hallazgo"]).ToHashSet();
        Assert.DoesNotContain("H-DEV-1", abiertos);
        Assert.DoesNotContain("H-DEV-2", abiertos);
        Assert.DoesNotContain("H-DEV-3", abiertos);

        Assert.Equal(HallazgosAbiertos, hallazgos.Select(h => h.Traits["Hallazgo"]).Distinct().Count());
        Assert.Equal(CausasRaiz, hallazgos.Select(h => h.Traits["CausaRaiz"]).Distinct().Count());

        _out.WriteLine($"{HallazgosAbiertos} tests Hallazgo_ reproducen defectos financieros ABIERTOS (PR correctiva PENDIENTE). " +
                       "Que pasen NO significa que esten corregidos.");
        foreach (var h in hallazgos.OrderBy(h => h.Traits["Hallazgo"]))
            _out.WriteLine($"  {h.Traits["Hallazgo"],-9} {h.Traits["CausaRaiz"]}  ABIERTO  {h.Metodo.Name}");
    }

    [Fact(DisplayName = "REGISTRO: 3 hallazgos CORREGIDOS (H-DEV-1/2/3) con pruebas de regresion")]
    public void Registro_TresHallazgosCorregidos_ConRegresion()
    {
        var corregidos = Metodos(typeof(FinResidualCajaDevolucionAnticipoTests))
            .Where(x => x.Metodo.Name.StartsWith("Regresion_H_DEV_", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(HallazgosCorregidos, corregidos.Count);
        foreach (var c in corregidos)
        {
            Assert.Equal("Regresion", c.Traits.GetValueOrDefault("Categoria"));
            Assert.Equal("CORREGIDO", c.Traits.GetValueOrDefault("Estado"));
            Assert.Equal("CR-3", c.Traits.GetValueOrDefault("CausaRaiz"));
            Assert.StartsWith("REGRESION", c.DisplayName);
        }
        Assert.Equal(new[] { "H-DEV-1", "H-DEV-2", "H-DEV-3" }, corregidos.Select(c => c.Traits["Hallazgo"]).OrderBy(x => x).ToArray());

        // Los traits de "defecto abierto" no pueden sobrevivir en ningún test de la clase que ya no los reproduce.
        var todos = Metodos(typeof(FinResidualCajaDevolucionAnticipoTests)).ToList();
        Assert.DoesNotContain(todos, t => t.Metodo.Name.StartsWith("Regresion", StringComparison.Ordinal)
                                          && t.Traits.GetValueOrDefault("Categoria") == "DefectoConocido");

        _out.WriteLine("H-DEV-1, H-DEV-2 y H-DEV-3 (CR-3) CORREGIDOS en FIN-DEV-P0; el resto de hallazgos sigue ABIERTO.");
    }

    [Fact(DisplayName = "REGISTRO: 6 contratos validos (comportamiento correcto, no son bugs)")]
    public void Registro_SeisContratosValidos()
    {
        var contratos = Metodos(typeof(FinResidualCajaDevolucionAnticipoTests))
            .Where(x => x.Metodo.Name.StartsWith("Contrato_", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(Contratos, contratos.Count);
        foreach (var c in contratos)
        {
            Assert.Equal("ContratoValido", c.Traits.GetValueOrDefault("Categoria"));
            Assert.StartsWith("CONTRATO VALIDO", c.DisplayName);
        }
    }

    [Fact(DisplayName = "REGISTRO: 10 escenarios SQL Server R-01..R-10 NO EJECUTADOS / BLOQUEADOS (omitidos != PASS)")]
    public void Registro_DiezEscenariosSqlServer_NoEjecutados()
    {
        var escenarios = Metodos(typeof(FinResidualSqlServerTests))
            .Where(x => x.Traits.GetValueOrDefault("Categoria") == "SqlServerNoEjecutado")
            .ToList();

        Assert.Equal(EscenariosSqlNoEjecutados, escenarios.Count);
        foreach (var e in escenarios)
        {
            Assert.StartsWith("NO EJECUTADO / BLOQUEADO", e.DisplayName);
            Assert.False(string.IsNullOrWhiteSpace(e.Traits.GetValueOrDefault("ResultadoEsperadoInferido")), e.Metodo.Name);
        }

        _out.WriteLine($"{EscenariosSqlNoEjecutados} escenarios SQL Server (R-01..R-10): NO EJECUTADOS / BLOQUEADOS. " +
                       "Requieren un turno exclusivo con SQL Server efimero; omitirlos NO equivale a PASS.");
    }
}
