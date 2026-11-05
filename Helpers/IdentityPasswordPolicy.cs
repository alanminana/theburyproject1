namespace TheBuryProject.Helpers;

/// <summary>
/// Largo minimo de contrasena segun entorno. En Production se exige 10 (antes 6 en todos los entornos);
/// Development/Testing conservan 6 para no romper fixtures y cuentas de QA. Se puede ajustar con
/// <c>Identity:Password:RequiredLength</c> (nunca por debajo de 6).
/// </summary>
internal static class IdentityPasswordPolicy
{
    internal const int MinimoProduccion = 10;
    internal const int MinimoGeneral = 6;

    internal static int RequiredLength(bool production, string? configurado)
    {
        var porDefecto = production ? MinimoProduccion : MinimoGeneral;
        if (!int.TryParse(configurado, out var valor)) return porDefecto;
        if (valor < MinimoGeneral) return porDefecto;
        // En produccion la configuracion solo puede endurecer la regla, no relajarla.
        return production ? Math.Max(valor, MinimoProduccion) : valor;
    }
}
