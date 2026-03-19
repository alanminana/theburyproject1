using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Usuario de la aplicación extendiendo IdentityUser para agregar campos personalizados.
    /// </summary>
    public class ApplicationUser : IdentityUser
    {
        /// <summary>
        /// Indica si el usuario está activo en el sistema.
        /// Los usuarios inactivos no pueden iniciar sesión y no se muestran en listas.
        /// Se usa soft delete para mantener integridad referencial con Ventas, Auditorías, etc.
        /// </summary>
        public bool Activo { get; set; } = true;

        /// <summary>
        /// Fecha de creación del usuario
        /// </summary>
        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Fecha de desactivación del usuario (si está inactivo)
        /// </summary>
        public DateTime? FechaDesactivacion { get; set; }

        /// <summary>
        /// Usuario que desactivó este usuario
        /// </summary>
        public string? DesactivadoPor { get; set; }

        /// <summary>
        /// Motivo de la desactivación
        /// </summary>
        public string? MotivoDesactivacion { get; set; }

        /// <summary>
        /// Nombre del usuario
        /// </summary>
        public string? Nombre { get; set; }

        /// <summary>
        /// Apellido del usuario
        /// </summary>
        public string? Apellido { get; set; }

        /// <summary>
        /// Teléfono de contacto del usuario
        /// </summary>
        public string? Telefono { get; set; }

        /// <summary>
        /// Nombre completo calculado (Nombre + Apellido)
        /// </summary>
        [NotMapped]
        public string? NombreCompleto =>
            string.IsNullOrWhiteSpace(Nombre) && string.IsNullOrWhiteSpace(Apellido)
                ? null
                : $"{Nombre} {Apellido}".Trim();

        /// <summary>
        /// Sucursal asignada al usuario
        /// </summary>
        public string? Sucursal { get; set; }

        /// <summary>
        /// Relación normalizada a la sucursal del usuario.
        /// </summary>
        public int? SucursalId { get; set; }

        public virtual Sucursal? SucursalNavigation { get; set; }

        /// <summary>
        /// Fecha y hora del último acceso al sistema
        /// </summary>
        public DateTime? UltimoAcceso { get; set; }

        /// <summary>
        /// Token de concurrencia optimista para evitar ediciones perdidas.
        /// </summary>
        [Timestamp]
        public byte[] RowVersion { get; set; } = default!;
    }
}
