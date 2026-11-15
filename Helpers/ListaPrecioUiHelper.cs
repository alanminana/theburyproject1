using System.Globalization;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Helpers
{
    /// <summary>D-01: textos y formato (es-AR explícito) de las pantallas de listas de precios. Solo presentación.</summary>
    public static class ListaPrecioUiHelper
    {
        private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

        public const string InputClass =
            "w-full rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 text-sm text-white placeholder:text-slate-500 transition-colors focus:border-primary focus:ring-2 focus:ring-primary/50";

        public const string LabelClass = "mb-2 block text-xs font-bold uppercase tracking-wider text-slate-400";

        public static string Moneda(decimal valor) => valor.ToString("C2", Ar);

        public static string Porcentaje(decimal? valor) => valor.HasValue ? valor.Value.ToString("0.##", Ar) + "%" : "—";

        /// <summary>Valor para un input type=number (punto decimal, sin separador de miles).</summary>
        public static string Numero(decimal? valor) => valor.HasValue ? valor.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;

        public static string Fecha(DateTime? fecha) => fecha.HasValue ? fecha.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Ar) : "—";

        public static string NombreTipo(TipoListaPrecio tipo) => tipo switch
        {
            TipoListaPrecio.Contado => "Contado",
            TipoListaPrecio.Tarjeta => "Tarjeta",
            TipoListaPrecio.Mayorista => "Mayorista",
            TipoListaPrecio.Especial => "Especial",
            _ => tipo.ToString()
        };

        public static string NombreRedondeo(string? regla) => regla switch
        {
            "ninguno" => "Sin redondeo",
            "unidad" => "A la unidad",
            "decena" => "A la decena",
            "centena" => "A la centena",
            _ => "Centena (por defecto)"
        };
    }
}
