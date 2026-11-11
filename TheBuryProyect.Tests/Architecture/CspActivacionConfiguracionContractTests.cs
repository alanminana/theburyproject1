namespace TheBuryProject.Tests.Architecture;

/// <summary>
/// D-05 (preparación de la activación de Enforce): contrato de configuración que hace operable el cambio de modo sin tocar código.
/// El procedimiento (docs/csp-enforce-activacion.md) depende de que (1) el despliegue Docker le pase <c>Csp__Mode</c> a <c>app</c> desde
/// <c>CSP_MODE</c> del <c>.env</c> con <c>Off</c> por defecto, (2) <c>.env.example</c> documente la variable apagada y (3) el preflight
/// rechace un valor inválido antes del deploy (un <c>Csp:Mode</c> inválido impide arrancar la app).
/// </summary>
public class CspActivacionConfiguracionContractTests
{
    [Fact]
    public void DockerCompose_PasaCspModeALaApp_ConOffPorDefecto()
    {
        var compose = File.ReadAllText(Path.Combine(FindRepoRoot(), "docker-compose.yml"));
        var iApp = compose.IndexOf("\n  app:", StringComparison.Ordinal);
        Assert.True(iApp > 0, "No se encontró el servicio app en docker-compose.yml.");
        var iSiguiente = compose.IndexOf("\n  caddy:", iApp, StringComparison.Ordinal);
        var bloqueApp = compose.Substring(iApp, iSiguiente - iApp);

        Assert.Contains("Csp__Mode: ${CSP_MODE:-Off}", bloqueApp, StringComparison.Ordinal);
        // Solo `app` recibe la variable: migrate/db-init no sirven HTTP.
        Assert.Equal(1, compose.Split("Csp__Mode:").Length - 1);
    }

    [Fact]
    public void EnvExample_DocumentaCspModeApagado()
    {
        var env = File.ReadAllText(Path.Combine(FindRepoRoot(), ".env.example"));

        Assert.Matches(@"(?m)^CSP_MODE=Off\r?$", env);
    }

    [Fact]
    public void Preflight_RechazaUnCspModeInvalido()
    {
        var preflight = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "deploy", "preflight.sh"));

        Assert.Contains("CSP_MODE=", preflight, StringComparison.Ordinal);
        Assert.Contains("\"$csp_low\" == \"reportonly\"", preflight, StringComparison.Ordinal);
        Assert.Contains("invalido: use Off, ReportOnly o Enforce", preflight, StringComparison.Ordinal);
    }

    [Fact]
    public void LanzadorLocal_SoloAceptaLosTresModosExactos()
    {
        var ps1 = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "ejecutar-erp-local.ps1"));

        Assert.Contains("[ValidateSet('', 'Off', 'ReportOnly', 'Enforce')][string]$CspMode", ps1, StringComparison.Ordinal);
        Assert.Contains("$env:Csp__Mode = $CspMode", ps1, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio a partir de AppContext.BaseDirectory.");
    }
}
