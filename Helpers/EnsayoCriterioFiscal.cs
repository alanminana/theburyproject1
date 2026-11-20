namespace TheBuryProject.Helpers
{
    /// <summary>
    /// P-02: activación de ENSAYO de un criterio fiscal (p. ej. <see cref="CriterioFiscalV2"/>) para validar el ERP desplegado en una
    /// instancia aislada con datos sintéticos, SIN tocar la configuración productiva. Cerrada por defecto:
    /// <list type="bullet">
    /// <item>solo se admite en el entorno <c>Development</c>; en cualquier otro (Production, Staging, Testing…) la aplicación NO arranca si la
    /// clave <c>Ensayo:CriterioFiscalVersion</c> está definida (falla cerrada: nunca se ignora en silencio);</item>
    /// <item>solo acepta versiones registradas; la vigente de producción sigue siendo <see cref="CriterioFiscalRegistry.VersionInicial"/>;</item>
    /// <item>las operaciones ya emitidas conservan su versión: cambiar la vigente solo afecta a las nuevas.</item>
    /// </list>
    /// </summary>
    public static class EnsayoCriterioFiscal
    {
        public const string Clave = "Ensayo:CriterioFiscalVersion";

        /// <returns>La versión de ensayo pedida, o null si la clave no está definida.</returns>
        /// <exception cref="InvalidOperationException">La clave está definida fuera de Development o con una versión no registrada.</exception>
        public static int? Resolver(IHostEnvironment entorno, string? valor)
        {
            ArgumentNullException.ThrowIfNull(entorno);
            if (string.IsNullOrWhiteSpace(valor))
                return null;

            if (!entorno.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"{Clave} solo se admite en el entorno Development (instancia de ensayo aislada). Entorno actual: {entorno.EnvironmentName}. " +
                    "Quitá la clave de la configuración: no se activa ningún criterio fiscal de ensayo fuera de una instancia descartable.");
            }

            if (!int.TryParse(valor, out var version)
                || (version != CriterioFiscalRegistry.VersionInicial && version != CriterioFiscalRegistry.VersionDosRedondeoComercial))
            {
                throw new InvalidOperationException($"{Clave}={valor} no es una versión de criterio fiscal registrada (1 o 2).");
            }

            return version;
        }
    }
}
