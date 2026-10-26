using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Interfaces
{
    public sealed record PrefijoNumeracionInfo(TipoPrefijoNumeracion Tipo, string Prefijo, string PrefijoPorDefecto);

    public sealed record PrefijoNumeracionComando(TipoPrefijoNumeracion Tipo, string? Prefijo);

    /// <summary>Administración de los prefijos de numeración de los documentos (Configuración → Prefijos).</summary>
    public interface IPrefijoNumeracionService
    {
        /// <summary>Siempre devuelve todos los tipos (los que no tienen fila usan el prefijo por defecto).</summary>
        Task<IReadOnlyList<PrefijoNumeracionInfo>> ListarAsync();

        /// <summary>Upsert de los prefijos enviados; vacío vuelve al valor por defecto. Rechaza formatos inválidos.</summary>
        Task GuardarAsync(IEnumerable<PrefijoNumeracionComando> comandos);
    }
}
