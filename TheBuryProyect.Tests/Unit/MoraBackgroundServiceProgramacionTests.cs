using TheBuryProject.Tests.Helpers;
using TheBuryProject.Services;
using Xunit;

namespace TheBuryProject.Tests.Unit
{
    /// <summary>
    /// El job de mora interpreta la hora configurada como hora comercial (Argentina, UTC-3), no UTC,
    /// y no se repite el mismo dia comercial aunque la app se reinicie (ultima ejecucion persistida).
    /// </summary>
    public class MoraBackgroundServiceProgramacionTests
    {
        private static RelojComercial Reloj(DateTimeOffset utc) => RelojComercialFijo.EnUtc(utc);

        [Fact]
        public void HoraConfigurada_08_00_corre_a_las_11_UTC_y_no_a_las_08_UTC()
        {
            var r11 = Reloj(new DateTimeOffset(2026, 11, 6, 11, 0, 0, TimeSpan.Zero));
            var r08 = Reloj(new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero));
            var hora = new TimeSpan(8, 0, 0);

            Assert.True(MoraBackgroundService.DebeEjecutar(hora, r11.AhoraComercial, null, r11, DateTime.MinValue));
            Assert.False(MoraBackgroundService.DebeEjecutar(hora, r08.AhoraComercial, null, r08, DateTime.MinValue));
        }

        [Fact]
        public void Reinicio_el_mismo_dia_comercial_no_repite_si_la_base_registra_la_ejecucion()
        {
            var r = Reloj(new DateTimeOffset(2026, 11, 6, 11, 5, 0, TimeSpan.Zero));
            var ejecutoHoy = new DateTime(2026, 11, 6, 11, 0, 30, DateTimeKind.Utc);

            Assert.False(MoraBackgroundService.DebeEjecutar(new TimeSpan(8, 0, 0), r.AhoraComercial, ejecutoHoy, r, DateTime.MinValue));
        }

        [Fact]
        public void Ejecucion_de_ayer_no_bloquea_hoy()
        {
            var r = Reloj(new DateTimeOffset(2026, 11, 6, 11, 0, 0, TimeSpan.Zero));
            var ayer = new DateTime(2026, 11, 5, 11, 0, 0, DateTimeKind.Utc);

            Assert.True(MoraBackgroundService.DebeEjecutar(new TimeSpan(8, 0, 0), r.AhoraComercial, ayer, r, DateTime.MinValue));
        }

        [Fact]
        public void Ejecucion_a_las_01_UTC_es_del_dia_comercial_anterior()
        {
            // 01:00 UTC del 6/11 = 22:00 del 5/11 en Argentina: no cuenta como ejecutado el 6/11.
            var r = Reloj(new DateTimeOffset(2026, 11, 6, 11, 0, 0, TimeSpan.Zero));
            var tarde = new DateTime(2026, 11, 6, 1, 0, 0, DateTimeKind.Utc);

            Assert.True(MoraBackgroundService.DebeEjecutar(new TimeSpan(8, 0, 0), r.AhoraComercial, tarde, r, DateTime.MinValue));
        }

        [Fact]
        public void Ejecucion_en_memoria_del_mismo_dia_bloquea()
        {
            var r = Reloj(new DateTimeOffset(2026, 11, 6, 11, 5, 0, TimeSpan.Zero));
            Assert.False(MoraBackgroundService.DebeEjecutar(new TimeSpan(8, 0, 0), r.AhoraComercial, null, r, r.AhoraUtc.AddMinutes(-5)));
        }
    }
}

