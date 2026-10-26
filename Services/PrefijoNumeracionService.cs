using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class PrefijoNumeracionService : IPrefijoNumeracionService
    {
        private readonly AppDbContext _context;

        public PrefijoNumeracionService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<PrefijoNumeracionInfo>> ListarAsync()
        {
            var configurados = await PrefijosNumeracion.CargarAsync(_context);
            return Enum.GetValues<TipoPrefijoNumeracion>()
                .Select(t => new PrefijoNumeracionInfo(
                    t,
                    configurados.TryGetValue(t, out var p) ? p : t.PrefijoPorDefecto(),
                    t.PrefijoPorDefecto()))
                .ToList();
        }

        public async Task GuardarAsync(IEnumerable<PrefijoNumeracionComando> comandos)
        {
            var lista = comandos.ToList();

            foreach (var c in lista)
            {
                if (!Enum.IsDefined(c.Tipo))
                    throw new InvalidOperationException("Tipo de documento inválido.");
            }

            var duplicado = lista.GroupBy(c => c.Tipo).FirstOrDefault(g => g.Count() > 1);
            if (duplicado != null)
                throw new InvalidOperationException($"«{duplicado.Key.NombreVisible()}» está repetido.");

            var existentes = await _context.PrefijosNumeracion
                .Where(p => !p.IsDeleted)
                .ToDictionaryAsync(p => p.Tipo);

            foreach (var c in lista)
            {
                var prefijo = PrefijosNumeracion.Normalizar(c.Prefijo);
                if (prefijo.Length == 0)
                    prefijo = c.Tipo.PrefijoPorDefecto();
                else if (!PrefijosNumeracion.EsValido(prefijo, c.Tipo.LongitudMaxima()))
                    throw new InvalidOperationException(
                        $"El prefijo de «{c.Tipo.NombreVisible()}» no es válido: usá de 1 a {c.Tipo.LongitudMaxima()} letras o números, con guiones solo entre medio (ej. FA-A).");

                if (existentes.TryGetValue(c.Tipo, out var fila))
                {
                    fila.Prefijo = prefijo;
                }
                else if (prefijo != c.Tipo.PrefijoPorDefecto())
                {
                    _context.PrefijosNumeracion.Add(new PrefijoNumeracion { Tipo = c.Tipo, Prefijo = prefijo });
                }
            }

            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Lectura compartida de los prefijos: la usan la administración y todos los generadores de números.
    /// </summary>
    internal static class PrefijosNumeracion
    {
        private static readonly Regex Formato = new("^[A-Z0-9]+(-[A-Z0-9]+)*$", RegexOptions.Compiled);

        public static string Normalizar(string? prefijo) => (prefijo ?? string.Empty).Trim().ToUpperInvariant();

        public static bool EsValido(string prefijoNormalizado, int longitudMaxima) =>
            prefijoNormalizado.Length >= 1 && prefijoNormalizado.Length <= longitudMaxima && Formato.IsMatch(prefijoNormalizado);

        public static async Task<Dictionary<TipoPrefijoNumeracion, string>> CargarAsync(AppDbContext context) =>
            await context.PrefijosNumeracion
                .AsNoTracking()
                .Where(p => !p.IsDeleted)
                .ToDictionaryAsync(p => p.Tipo, p => p.Prefijo);

        public static async Task<string> ObtenerAsync(AppDbContext context, TipoPrefijoNumeracion tipo)
        {
            var configurado = await context.PrefijosNumeracion
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.Tipo == tipo)
                .Select(p => p.Prefijo)
                .FirstOrDefaultAsync();

            return string.IsNullOrWhiteSpace(configurado) ? tipo.PrefijoPorDefecto() : configurado;
        }
    }
}
