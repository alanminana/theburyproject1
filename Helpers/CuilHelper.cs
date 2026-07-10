using System.Linq;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Arma y desarma el CUIL/CUIT del cliente en sus tres partes visibles: prefijo (2),
    /// DNI central (8, no editable) y dígito verificador (1). El DNI es la autoridad y
    /// proviene siempre del número de documento del cliente.
    /// Reglas de negocio acordadas: el CUIL se muestra parcialmente editable como XX-DNI-X;
    /// al guardar, el prefijo y el verificador vacíos se completan con 0. Si no hay un DNI
    /// de 8 dígitos, no se arma un CUIL (se devuelve null) para no persistir un valor inválido.
    /// </summary>
    public static class CuilHelper
    {
        private const char Cero = '0';

        private static string SoloDigitos(string? value) =>
            string.IsNullOrEmpty(value) ? string.Empty : new string(value.Where(char.IsDigit).ToArray());

        /// <summary>
        /// Arma el CUIL de 11 dígitos a partir de las partes editables y el DNI central.
        /// Prefijo/verificador vacíos (o parciales) se completan con 0. Devuelve null si no
        /// hay un DNI de 8 dígitos, para no generar un CUIL inválido.
        /// </summary>
        public static string? Componer(string? prefijo, string? numeroDocumento, string? verificador)
        {
            var dni = SoloDigitos(numeroDocumento);
            if (dni.Length != 8)
                return null;

            var pref = SoloDigitos(prefijo);
            pref = pref.Length >= 2 ? pref.Substring(0, 2) : pref.PadLeft(2, Cero);

            var ver = SoloDigitos(verificador);
            var verChar = ver.Length >= 1 ? ver[0] : Cero;

            return string.Concat(pref, dni, verChar);
        }

        /// <summary>
        /// Descompone un CUIL de 11 dígitos en (prefijo, verificador) para mostrar en el
        /// formulario. Si el valor no tiene 11 dígitos, devuelve (null, null): las partes se
        /// muestran vacías (placeholder) y el DNI se toma del cliente.
        /// </summary>
        public static (string? Prefijo, string? Verificador) Descomponer(string? cuilCuit)
        {
            var digits = SoloDigitos(cuilCuit);
            if (digits.Length != 11)
                return (null, null);

            return (digits.Substring(0, 2), digits.Substring(10, 1));
        }
    }
}
