using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Fuente canónica de la configuración versionada de punitorios (PUN-ML3). Único punto que
    /// resuelve "qué regla estaba vigente en una fecha comercial dada" — no distribuir esta
    /// selección entre controllers, otros servicios, Razor o JavaScript.
    /// No calcula todavía el punitorio monetario (eso es PUN-ML4/PUN-ML5).
    /// </summary>
    public interface IConfiguracionPunitorioService
    {
        /// <summary>
        /// Resuelve la versión vigente en <paramref name="fechaComercial"/>: la de mayor
        /// <c>VigenteDesde</c> que sea &lt;= esa fecha. Solo lectura — nunca persiste.
        /// </summary>
        Task<ConfiguracionPunitorioVigente> ObtenerVigenteAsync(
            DateOnly fechaComercial, CancellationToken cancellationToken = default);

        /// <summary>
        /// Crea una nueva versión append-only. Nunca edita una fila existente. Rechaza (ver
        /// <see cref="Exceptions.ConfiguracionPunitorioRechazadaException"/>) valores inválidos,
        /// vigencia igual o anterior a una ya existente, y vigencia retroactiva sin motivo o sin
        /// <see cref="ConfiguracionPunitorioComando.AutorizadoParaRetroactivo"/>.
        /// </summary>
        Task<ConfiguracionPunitorio> CrearNuevaVersionAsync(
            ConfiguracionPunitorioComando comando, CancellationToken cancellationToken = default);

        /// <summary>Todas las versiones no eliminadas, de más reciente a más antigua por <c>VigenteDesde</c>.</summary>
        Task<IReadOnlyList<ConfiguracionPunitorio>> ListarHistorialAsync(CancellationToken cancellationToken = default);
    }
}
