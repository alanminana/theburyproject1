using System;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Implementación canónica de <see cref="IRelojComercial"/>. Toma el instante actual desde
    /// un <see cref="TimeProvider"/> inyectable (en producción <see cref="TimeProvider.System"/>;
    /// en tests, un proveedor fijo) y lo convierte a la fecha comercial de Argentina.
    /// </summary>
    public sealed class RelojComercial : IRelojComercial
    {
        // Identificadores de la zona horaria de Argentina. Se intenta primero el ID IANA
        // (válido en Linux y, desde .NET 6, también en Windows vía ICU) y luego el ID Windows.
        private static readonly string[] IdsZonaArgentina =
        {
            "America/Argentina/Buenos_Aires",
            "Argentina Standard Time"
        };

        private readonly TimeProvider _timeProvider;
        private readonly TimeZoneInfo _zona;

        public RelojComercial(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _zona = ResolverZonaArgentina();
        }

        /// <summary>Reloj por defecto basado en el reloj del sistema (fallback fuera de DI).</summary>
        public static RelojComercial Sistema { get; } = new RelojComercial(TimeProvider.System);

        public TimeZoneInfo ZonaComercial => _zona;

        public DateTime AhoraUtc => _timeProvider.GetUtcNow().UtcDateTime;

        public DateTime AhoraComercial =>
            TimeZoneInfo.ConvertTimeFromUtc(AhoraUtc, _zona);

        public DateOnly HoyComercial => DateOnly.FromDateTime(AhoraComercial);

        public DateTime InicioDiaComercial => HoyComercial.ToDateTime(TimeOnly.MinValue);

        private static TimeZoneInfo ResolverZonaArgentina()
        {
            foreach (var id in IdsZonaArgentina)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }

            // Último recurso portable: Argentina usa un offset fijo UTC−3 (sin horario de verano
            // vigente). No se hardcodea como comportamiento primario: solo si el SO no expone la
            // zona identificable, para que el reloj nunca falle al arrancar.
            return TimeZoneInfo.CreateCustomTimeZone(
                "Argentina UTC-3 (fallback)",
                TimeSpan.FromHours(-3),
                "Argentina (UTC-3)",
                "Argentina (UTC-3)");
        }
    }
}
