using System.Globalization;
using System.Text.RegularExpressions;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// P-02 / P6 (autorizada por el propietario): activación PRODUCTIVA de <see cref="CriterioFiscalV2"/> desde la fecha real de puesta en
    /// marcha de una instalación, sin retroactividad. Clave <c>Fiscal:CriterioV2Desde</c> (env <c>Fiscal__CriterioV2Desde</c>, en Docker
    /// <c>FISCAL_CRITERIO_V2_DESDE</c> del <c>.env</c>): instante ISO-8601 con zona (p. ej. <c>2026-12-01T03:00:00Z</c> = 00:00 de Argentina).
    /// <list type="bullet">
    /// <item>Sin la clave (o vacía) nada cambia: rige la versión 1, como siempre. Una instalación con base histórica no se ve afectada.</item>
    /// <item>La versión se resuelve UNA vez al arrancar el proceso: V2 si el instante ya llegó, V1 si no (y entonces hasta el próximo
    /// reinicio). No cambia en caliente, así que ningún cálculo ni operación en curso puede mezclar versiones. Cada venta se estampa con
    /// la versión vigente al crearse y esa versión no se reescribe: lo anterior conserva V1. El despliegue de puesta en marcha reinicia
    /// la app, por lo que rige desde ese despliegue.</item>
    /// <item>Un valor inválido (sin zona, texto, fecha anterior a 2026) impide arrancar: nunca se ignora en silencio ni se «adivina» una fecha.</item>
    /// <item>No la reemplaza el override de ensayo (<see cref="EnsayoCriterioFiscal"/>, solo Development).</item>
    /// </list>
    /// Una vez que existen ventas V2 la clave NO debe quitarse ni adelantarse (volvería a estamparse V1): se conserva en el <c>.env</c> y en
    /// el registro de puesta en marcha (docs/p02-procedimiento-activacion.md).
    /// </summary>
    public static class ActivacionCriterioFiscal
    {
        public const string Clave = "Fiscal:CriterioV2Desde";

        /// <returns>El instante UTC desde el cual rige V2, o null si la clave no está definida.</returns>
        /// <exception cref="InvalidOperationException">El valor no es un instante ISO-8601 con zona.</exception>
        public static DateTimeOffset? Resolver(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return null;

            var texto = valor.Trim();
            // Exige zona explícita (Z o ±hh:mm): sin ella la fecha sería ambigua entre UTC y hora local del servidor.
            var tieneZona = texto.EndsWith('Z') || Regex.IsMatch(texto, @"[+-]\d{2}:\d{2}$");
            if (!tieneZona
                || !DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instante)
                || instante.Year < 2026)
            {
                throw new InvalidOperationException(
                    $"{Clave}='{valor}' no es un instante ISO-8601 con zona válido (ejemplo: 2026-12-01T03:00:00Z). " +
                    "La activación de V2 necesita la fecha real de puesta en marcha; no se adivina ni se ignora.");
            }

            return instante.ToUniversalTime();
        }
    }
}
