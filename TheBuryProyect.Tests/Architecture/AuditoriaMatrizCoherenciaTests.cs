using System.Globalization;
using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Architecture;

/// <summary>
/// La matriz de auditoría (docs/auditoria-funcional-matriz.md) y su inventario (docs/auditoria-funcional-inventario.csv) no
/// pueden contradecirse: la cobertura que se informa es SIEMPRE la que sale del inventario. Evita que una cifra vieja quede en
/// la introducción o en las respuestas cuando se regenera la tabla.
/// </summary>
public class AuditoriaMatrizCoherenciaTests
{
    private static string Raiz()
    {
        var actual = new DirectoryInfo(AppContext.BaseDirectory);
        while (actual != null)
        {
            if (File.Exists(Path.Combine(actual.FullName, "TheBuryProyect.csproj")))
                return actual.FullName;
            actual = actual.Parent;
        }
        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }

    private static (int total, int verificadas, int incompletas) ContarInventario()
    {
        var lineas = File.ReadAllLines(Path.Combine(Raiz(), "docs", "auditoria-funcional-inventario.csv"));
        var cab = lineas[0].TrimStart('﻿').Split(';');
        int iModulo = Array.IndexOf(cab, "modulo"), iTipo = Array.IndexOf(cab, "tipo"), iEstado = Array.IndexOf(cab, "estado");
        Assert.True(iModulo >= 0 && iTipo >= 0 && iEstado >= 0, "cabecera inesperada del inventario");

        int total = 0, ver = 0, inc = 0;
        foreach (var linea in lineas.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var c = linea.Split(';');
            if (c[iTipo] != "funcionalidad" || c[iModulo].StartsWith("Fuera de alcance", StringComparison.Ordinal))
                continue;   // el denominador son las funcionalidades de usuario del alcance (no soporte ni ML/diagnóstico)
            total++;
            if (c[iEstado].StartsWith("Verificada", StringComparison.Ordinal)) ver++;
            else if (c[iEstado] == "Incompleta") inc++;
        }
        return (total, ver, inc);
    }

    [Fact]
    public void LaCoberturaDeLaMatriz_CoincideConElInventario_EnTodosLosLugaresDondeSeInforma()
    {
        var (total, ver, inc) = ContarInventario();
        var pct = (ver * 100.0 / total).ToString("0.0", CultureInfo.GetCultureInfo("es-AR"));   // 53,5
        var matriz = File.ReadAllText(Path.Combine(Raiz(), "docs", "auditoria-funcional-matriz.md"));

        // Fila de totales de la tabla (formato del generador: «**53.5 %**»).
        var totalFila = Regex.Match(matriz, @"\| \*\*Total\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \| \*\*\d+\*\* \| \*\*(\d+)\*\* \| \*\*\d+\*\* \| \*\*([\d.]+) %\*\*");
        Assert.True(totalFila.Success, "no se encontró la fila Total de la tabla de cobertura");
        Assert.Equal(total, int.Parse(totalFila.Groups[1].Value));
        Assert.Equal(ver, int.Parse(totalFila.Groups[2].Value) + int.Parse(totalFila.Groups[3].Value));
        Assert.Equal(inc, int.Parse(totalFila.Groups[4].Value));

        // Toda cifra de cobertura escrita en prosa («NN,N %» junto a «de 310» / «cobertura») debe ser la vigente.
        var cifraVigente = $"{pct} %";
        Assert.Contains($"{ver} de {total}", matriz);
        foreach (Match m in Regex.Matches(matriz, @"(?<!\d)(\d{1,3},\d) %\*\* \((\d+) de (\d+)"))
        {
            Assert.Equal(cifraVigente, m.Groups[1].Value + " %");
            Assert.Equal(ver.ToString(), m.Groups[2].Value);
        }
        Assert.DoesNotContain("24,2 %", matriz);
    }
}
