using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.ViewModels
{
    public class ConfiguracionActualizacionDatosClienteViewModel
    {
        [Display(Name = "Avisar para actualizar datos del cliente")]
        public bool Activa { get; set; }

        [Display(Name = "Cada cuántos días")]
        [Range(ConfiguracionActualizacionDatosCliente.DiasRevisionMinimo, ConfiguracionActualizacionDatosCliente.DiasRevisionMaximo,
            ErrorMessage = "Ingresá una cantidad de días entre {1} y {2}.")]
        public int DiasRevision { get; set; } = 180;
    }
}
