using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class PrefijosNumeracionAdminViewModel
    {
        public List<PrefijoNumeracionItemViewModel> Items { get; set; } = new();
    }

    public class PrefijoNumeracionItemViewModel
    {
        public TipoPrefijoNumeracion Tipo { get; set; }

        public string Nombre { get; set; } = string.Empty;

        public string Prefijo { get; set; } = string.Empty;

        public string PrefijoPorDefecto { get; set; } = string.Empty;

        public string Ejemplo { get; set; } = string.Empty;

        public int LongitudMaxima { get; set; }
    }
}
