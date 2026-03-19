using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel para crear y editar proveedores
    /// </summary>
    public class ProveedorViewModel
    {
        public int Id { get; set; }

        /// <summary>
        /// RowVersion para control de concurrencia optimista.
        /// Debe enviarse en POST para detectar conflictos.
        /// </summary>
        public byte[]? RowVersion { get; set; }

        [Required(ErrorMessage = "El CUIT es obligatorio")]
        [StringLength(11, MinimumLength = 11, ErrorMessage = "El CUIT debe tener 11 dígitos")]
        [Display(Name = "CUIT")]
        public string Cuit { get; set; } = string.Empty;

        [Required(ErrorMessage = "La razón social es obligatoria")]
        [StringLength(200, ErrorMessage = "La razón social no puede tener más de 200 caracteres")]
        [Display(Name = "Razón Social")]
        public string RazonSocial { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "El nombre de fantasía no puede tener más de 200 caracteres")]
        [Display(Name = "Nombre de Fantasía")]
        public string? NombreFantasia { get; set; }

        [EmailAddress(ErrorMessage = "El email no es válido")]
        [StringLength(100, ErrorMessage = "El email no puede tener más de 100 caracteres")]
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [StringLength(50, ErrorMessage = "El teléfono no puede tener más de 50 caracteres")]
        [Display(Name = "Teléfono")]
        public string? Telefono { get; set; }

        [StringLength(300, ErrorMessage = "La dirección no puede tener más de 300 caracteres")]
        [Display(Name = "Dirección")]
        public string? Direccion { get; set; }

        [StringLength(100, ErrorMessage = "La ciudad no puede tener más de 100 caracteres")]
        [Display(Name = "Ciudad")]
        public string? Ciudad { get; set; }

        [StringLength(100, ErrorMessage = "La provincia no puede tener más de 100 caracteres")]
        [Display(Name = "Provincia")]
        public string? Provincia { get; set; }

        [StringLength(10, ErrorMessage = "El código postal no puede tener más de 10 caracteres")]
        [Display(Name = "Código Postal")]
        public string? CodigoPostal { get; set; }

        [StringLength(200, ErrorMessage = "El contacto no puede tener más de 200 caracteres")]
        [Display(Name = "Contacto")]
        public string? Contacto { get; set; }

        [StringLength(2000, ErrorMessage = "Las aclaraciones no pueden tener más de 2000 caracteres")]
        [Display(Name = "Aclaraciones")]
        public string? Aclaraciones { get; set; }

        [Display(Name = "Activo")]
        public bool Activo { get; set; } = true;

        // Propiedades calculadas para el listado
        public int TotalOrdenesCompra { get; set; }
        public int ChequesVigentes { get; set; }
        public decimal TotalDeuda { get; set; }

        // Propiedades para las asociaciones con el catálogo
        [Display(Name = "Categorías")]
        public List<int> CategoriasSeleccionadas { get; set; } = new List<int>();

        [Display(Name = "Marcas")]
        public List<int> MarcasSeleccionadas { get; set; } = new List<int>();

        [Display(Name = "Productos")]
        public List<int> ProductosSeleccionados { get; set; } = new List<int>();

        // Propiedades para los dropdowns (UI)
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> CategoriasDisponibles { get; set; } = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> MarcasDisponibles { get; set; } = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> ProductosDisponibles { get; set; } = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();

        // Propiedades para mostrar en la vista de detalles
        public List<string> CategoriasAsociadas { get; set; } = new List<string>();
        public List<string> MarcasAsociadas { get; set; } = new List<string>();
        public List<string> ProductosAsociados { get; set; } = new List<string>();

    }
}