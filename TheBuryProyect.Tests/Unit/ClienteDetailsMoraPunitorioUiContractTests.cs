namespace TheBuryProject.Tests.Unit;

/// <summary>
/// PUN-ML10-F: contrato de UI de la ficha de cliente (Views/Cliente/Details_tw.cshtml) para
/// mora de capital vs. punitorio aplicado pendiente. Reglas congeladas cubiertas acá:
/// no mezclar ambos montos en una sola cifra; rotular el alias legacy claramente como capital;
/// no mostrar un bloque de punitorio vacío/confuso cuando no hay saldo pendiente; no leer
/// <c>Cuota.MontoPunitorio</c> desde la vista.
/// </summary>
public class ClienteDetailsMoraPunitorioUiContractTests
{
    [Fact]
    public void ClienteDetails_SeparaMoraCapitalYPunitorio()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "Details_tw.cshtml"));

        // Bloque de capital: rotulado explícitamente como capital (no genérico "Monto en mora"),
        // usando el alias legacy MontoTotalMora (PUN-ML10-C: capital-only).
        Assert.Contains("<dt>Capital en mora</dt>", view);
        Assert.Contains("apt.Mora.MontoTotalMora.ToString(\"C0\")", view);

        // Bloque de punitorio: propio, con monto real, cantidad de cuotas y estado de aptitud —
        // nunca mezclado en la misma cifra que el capital.
        Assert.Contains("Punitorio aplicado pendiente", view);
        Assert.Contains("apt.Mora.MontoPunitorioAplicadoPendiente.ToString(\"C0\")", view);
        Assert.Contains("apt.Mora.CuotasConPunitorioAplicadoPendiente", view);
        Assert.Contains("apt.Mora.TienePunitorioAplicadoPendiente", view);

        // Nunca debe leer el campo legacy congelado de cuota (PUN-ML9-E/PUN-ML10-C regla 9).
        Assert.DoesNotContain("c.MontoPunitorio", view);
        Assert.DoesNotContain("cuota.MontoPunitorio", view);
    }

    [Fact]
    public void ClienteDetails_NoMuestraPunitorioCuandoNoHayPendiente()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "Details_tw.cshtml")).Replace("\r\n", "\n");

        // El bloque de punitorio está envuelto en un @if que exige TienePunitorioAplicadoPendiente
        // (no un bloque vacío ni un $0,00 cuando no hay saldo pendiente): la condición inmediatamente
        // anterior a la aparición del título del bloque debe ser ese flag.
        var indiceTitulo = view.IndexOf("Punitorio aplicado pendiente</div>", StringComparison.Ordinal);
        Assert.True(indiceTitulo > 0, "El bloque de punitorio debe existir en la vista.");

        var indiceIf = view.LastIndexOf("@if", indiceTitulo, StringComparison.Ordinal);
        Assert.True(indiceIf > 0, "El bloque de punitorio debe estar condicionado por un @if.");

        var condicion = view.Substring(indiceIf, indiceTitulo - indiceIf);
        Assert.Contains("apt.Mora.TienePunitorioAplicadoPendiente", condicion);
    }

    [Fact]
    public void ClienteDetails_PunitorioReutilizaComponentesExistentesResponsive()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "Details_tw.cshtml"));

        // El bloque nuevo reutiliza las mismas clases responsive/existentes del resto de la
        // ficha (alert/kv/kv-row/chip) en vez de introducir CSS ajeno al sistema.
        var inicioBloque = view.IndexOf("Punitorio aplicado pendiente", StringComparison.Ordinal);
        Assert.True(inicioBloque > 0, "El bloque de punitorio debe existir en la vista.");
        var fragmento = view.Substring(Math.Max(0, inicioBloque - 400), 900);
        Assert.Contains("alert alert-warn", fragmento);
        Assert.Contains("class=\"kv", fragmento);
        Assert.Contains("kv-row", fragmento);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}
