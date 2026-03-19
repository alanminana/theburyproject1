using System.Collections.Generic;
using System.Linq;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class DocumentacionClienteEstadoViewModel
    {
        public bool Completa { get; set; }

        public List<TipoDocumentoCliente> Faltantes { get; set; } = new();

        public string DescripcionFaltantes => Faltantes.Count == 0
            ? string.Empty
            : string.Join(", ", Faltantes.Select(f => f.ToString()));
    }
}
