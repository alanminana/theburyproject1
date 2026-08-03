using System;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Helpers;

/// <summary>
/// Doble de <see cref="IRelojComercial"/> con fecha comercial fija y controlable. Permite escribir
/// tests de vencimiento/punitorio deterministas, independientes de la hora real del proceso.
/// Para probar la conversión de zona horaria en sí, usar <see cref="RelojComercialFijo"/> (que sí
/// ejerce la conversión real a partir de un instante UTC fijo).
/// </summary>
public sealed class RelojComercialFake : IRelojComercial
{
    public RelojComercialFake()
        : this(DateOnly.FromDateTime(DateTime.UtcNow))
    {
    }

    public RelojComercialFake(DateOnly hoyComercial)
    {
        HoyComercial = hoyComercial;
        AhoraUtc = hoyComercial.ToDateTime(new TimeOnly(12, 0));
        ZonaComercial = TimeZoneInfo.Utc;
    }

    public DateTime AhoraUtc { get; set; }
    public DateTime AhoraComercial => AhoraUtc;
    public DateOnly HoyComercial { get; set; }
    public TimeZoneInfo ZonaComercial { get; set; }
    public DateTime InicioDiaComercial => HoyComercial.ToDateTime(TimeOnly.MinValue);
}

/// <summary>
/// <see cref="TimeProvider"/> que devuelve un instante UTC fijo. Alimenta un
/// <see cref="RelojComercial"/> real para verificar la conversión a la fecha comercial de Argentina
/// (frontera UTC/Argentina) sin depender del reloj del sistema.
/// </summary>
public sealed class TimeProviderFijo : TimeProvider
{
    private readonly DateTimeOffset _utc;

    public TimeProviderFijo(DateTimeOffset utc) => _utc = utc;

    public override DateTimeOffset GetUtcNow() => _utc;
}

/// <summary>
/// Fábrica de un <see cref="RelojComercial"/> real anclado a un instante UTC fijo (ejerce la
/// conversión de zona horaria de verdad).
/// </summary>
public static class RelojComercialFijo
{
    public static RelojComercial EnUtc(DateTimeOffset instanteUtc) =>
        new RelojComercial(new TimeProviderFijo(instanteUtc));
}
