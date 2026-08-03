using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Models
{
    /// <summary>
    /// Estado de la resolución de <c>ConfiguracionPunitorioService.ObtenerVigenteAsync</c>. Distingue
    /// explícitamente los tres casos que no deben confundirse: no hay ninguna versión aplicable,
    /// hay una versión aplicable pero desactivada, y hay una versión aplicable y activa (incluido
    /// el caso válido de <c>Porcentaje == 0</c>, que es "activa con 0%", no "ausente").
    /// </summary>
    public enum EstadoConfiguracionPunitorio
    {
        /// <summary>Ninguna versión tiene <c>VigenteDesde</c> &lt;= la fecha consultada.</summary>
        Ausente = 0,

        /// <summary>La versión aplicable existe pero <c>Activa == false</c>.</summary>
        Inactiva = 1,

        /// <summary>La versión aplicable existe y <c>Activa == true</c> (incluye 0%).</summary>
        Activa = 2
    }

    /// <summary>
    /// Resultado de resolver qué <see cref="ConfiguracionPunitorio"/> está vigente en una fecha
    /// comercial dada. <see cref="Configuracion"/> es null únicamente cuando <see cref="Estado"/>
    /// es <see cref="EstadoConfiguracionPunitorio.Ausente"/>.
    /// </summary>
    public sealed record ConfiguracionPunitorioVigente(
        EstadoConfiguracionPunitorio Estado,
        ConfiguracionPunitorio? Configuracion)
    {
        public static readonly ConfiguracionPunitorioVigente SinConfiguracion =
            new(EstadoConfiguracionPunitorio.Ausente, null);
    }
}
