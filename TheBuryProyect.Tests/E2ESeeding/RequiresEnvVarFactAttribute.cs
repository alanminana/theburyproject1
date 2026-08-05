using System;
using Xunit;

namespace TheBuryProject.Tests.E2ESeeding
{
    /// <summary>
    /// Fact que se auto-saltea (estado real "Skipped", nunca un pase vacío ni un fallo) cuando la
    /// variable de entorno indicada no está configurada. Usado exclusivamente por la infraestructura
    /// de seed E2E (<see cref="ClienteAptitudPunitorioE2ESeedRunner"/>) para que estas herramientas
    /// nunca se ejecuten por accidente dentro de "dotnet test completo" (Fase 9 del cierre PUN-ML10-G)
    /// ni contra un connection string implícito — sólo corren cuando alguien las invoca a propósito
    /// con el connection string de una LocalDB descartable explícita.
    ///
    /// No confundir con los <c>test.skip</c> del spec Playwright que este mismo cierre elimina: ese
    /// skip escondía escenarios sin datos reales sembrados. Este es distinto — protege que una
    /// herramienta de mantenimiento de datos no se dispare sola.
    /// </summary>
    public sealed class RequiresEnvVarFactAttribute : FactAttribute
    {
        public RequiresEnvVarFactAttribute(string envVarName)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(envVarName)))
            {
                Skip = $"Requiere la variable de entorno {envVarName} apuntando a una LocalDB descartable explícita.";
            }
        }
    }
}
