namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Reduce un teléfono a una forma canónica para compararlo (duplicados, coincidencia con el del cliente).
    /// Quita todo lo que no sea dígito, el prefijo internacional (00), el código de país 54, el 9 de celular
    /// que lo sigue y el 0 de acceso troncal. No interpreta el «15» interno: no se puede distinguir de forma
    /// confiable del número de abonado sin conocer el código de área.
    /// </summary>
    public static class TelefonoNormalizador
    {
        public static string Normalizar(string? telefono)
        {
            if (string.IsNullOrWhiteSpace(telefono))
                return string.Empty;

            var digitos = new string(telefono.Where(char.IsDigit).ToArray());

            if (digitos.StartsWith("00", StringComparison.Ordinal))
                digitos = digitos[2..];

            if (digitos.StartsWith("54", StringComparison.Ordinal) && digitos.Length >= 11)
            {
                digitos = digitos[2..];
                if (digitos.StartsWith('9') && digitos.Length >= 11)
                    digitos = digitos[1..];
            }

            if (digitos.StartsWith('0'))
                digitos = digitos.TrimStart('0');

            return digitos;
        }
    }
}
