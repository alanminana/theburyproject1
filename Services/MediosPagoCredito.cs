using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Fuente única de los medios de pago aceptados para cobrar cuotas de crédito: nombre
    /// canónico (el que se persiste) y su <see cref="TipoPago"/>. Lo usan el cobro de cuotas
    /// (<c>CreditoService</c>) y la validación del cobro inmediato de la primera cuota
    /// (<c>CreditoConfiguracionVentaService</c>), para que ambos acepten exactamente los mismos valores.
    /// </summary>
    internal static class MediosPagoCredito
    {
        public static readonly IReadOnlyDictionary<string, TipoPago> TipoPorMedio =
            new Dictionary<string, TipoPago>(StringComparer.OrdinalIgnoreCase)
            {
                ["Efectivo"] = TipoPago.Efectivo,
                ["Transferencia"] = TipoPago.Transferencia,
                ["Tarjeta Débito"] = TipoPago.TarjetaDebito,
                ["Tarjeta Crédito"] = TipoPago.TarjetaCredito,
                ["Cheque"] = TipoPago.Cheque
            };

        /// <summary>Nombres canónicos, en el orden de declaración.</summary>
        public static IEnumerable<string> Nombres => TipoPorMedio.Keys;

        /// <summary>Devuelve el nombre canónico (acentos/mayúsculas esperados) o false si no es válido.</summary>
        public static bool TryNormalizar(string? medioPago, out string canonico)
        {
            canonico = string.Empty;
            var valor = medioPago?.Trim();
            if (string.IsNullOrWhiteSpace(valor))
                return false;

            foreach (var nombre in TipoPorMedio.Keys)
            {
                if (string.Equals(nombre, valor, StringComparison.OrdinalIgnoreCase))
                {
                    canonico = nombre;
                    return true;
                }
            }

            return false;
        }
    }
}
