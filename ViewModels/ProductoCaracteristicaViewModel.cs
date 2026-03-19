using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    public class ProductoCaracteristicaViewModel
    {
        public int Id { get; set; }

        [Display(Name = "Característica")]
        [StringLength(100, ErrorMessage = "El nombre no puede superar 100 caracteres")]
        public string Nombre { get; set; } = string.Empty;

        [Display(Name = "Valor")]
        [StringLength(300, ErrorMessage = "El valor no puede superar 300 caracteres")]
        public string Valor { get; set; } = string.Empty;
    }
}
