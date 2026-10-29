using System;
using Microsoft.Extensions.Configuration;

namespace TheBuryProject.Services
{
    /// <summary>
    /// <see cref="TimeProvider"/> para el stack E2E/CI: el tiempo avanza con normalidad pero nunca pasa del final del
    /// dia comercial (Argentina) en el que se creo. Asi un suite largo que cruza la medianoche comercial no convierte en
    /// "turno vencido" (<c>CajaService.EsAperturaVencida</c>) las aperturas sembradas antes del cruce.
    /// </summary>
    /// <remarks>
    /// No cambia ninguna regla de negocio: la regla de turno vencido sigue evaluandose igual contra <c>IRelojComercial</c>.
    /// Solo se activa con <c>E2E:AcotarDiaComercial=true</c> (variable <c>E2E__AcotarDiaComercial</c>), que unicamente define
    /// <c>docker-compose.ci.yml</c>; el compose de produccion no la define y sin la clave se usa <see cref="TimeProvider.System"/>.
    /// </remarks>
    public sealed class TimeProviderDiaComercialAcotado : TimeProvider
    {
        public const string ClaveConfiguracion = "E2E:AcotarDiaComercial";

        private readonly TimeProvider _interno;

        public TimeProviderDiaComercialAcotado(TimeProvider interno)
        {
            _interno = interno ?? throw new ArgumentNullException(nameof(interno));
            var reloj = new RelojComercial(_interno);
            var inicioDiaSiguiente = reloj.HoyComercial.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var inicioDiaSiguienteUtc = TimeZoneInfo.ConvertTimeToUtc(inicioDiaSiguiente, reloj.ZonaComercial);
            // Ultimo instante del dia comercial de arranque.
            LimiteUtc = new DateTimeOffset(DateTime.SpecifyKind(inicioDiaSiguienteUtc, DateTimeKind.Utc)).AddTicks(-1);
        }

        /// <summary>Ultimo instante (UTC) que este proveedor devuelve.</summary>
        public DateTimeOffset LimiteUtc { get; }

        public override DateTimeOffset GetUtcNow()
        {
            var ahora = _interno.GetUtcNow();
            return ahora > LimiteUtc ? LimiteUtc : ahora;
        }

        /// <summary>
        /// Devuelve <paramref name="sistema"/> salvo que <c>E2E:AcotarDiaComercial</c> sea <c>true</c>.
        /// </summary>
        public static TimeProvider Resolver(IConfiguration configuracion, TimeProvider sistema) =>
            configuracion.GetValue<bool>(ClaveConfiguracion)
                ? new TimeProviderDiaComercialAcotado(sistema)
                : sistema;
    }
}
