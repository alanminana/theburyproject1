using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Modules.MercadoLibre.Entities
{
    /// <summary>
    /// Cuenta de vendedor de Mercado Libre conectada al ERP vía OAuth.
    /// Los tokens se persisten SIEMPRE cifrados con Data Protection;
    /// nunca se guardan ni loguean en texto plano.
    /// </summary>
    public class MercadoLibreAccount : AuditableEntity
    {
        /// <summary>
        /// user_id de Mercado Libre (dueño de la cuenta). Único.
        /// </summary>
        public long MeliUserId { get; set; }

        /// <summary>
        /// Nickname del vendedor en Mercado Libre.
        /// </summary>
        [StringLength(100)]
        public string Nickname { get; set; } = string.Empty;

        /// <summary>
        /// Site de operación (MLA = Argentina).
        /// </summary>
        [StringLength(10)]
        public string SiteId { get; set; } = "MLA";

        /// <summary>
        /// Access token cifrado con Data Protection.
        /// </summary>
        [Required]
        public string AccessTokenEncrypted { get; set; } = string.Empty;

        /// <summary>
        /// Refresh token cifrado con Data Protection.
        /// </summary>
        [Required]
        public string RefreshTokenEncrypted { get; set; } = string.Empty;

        /// <summary>
        /// Momento UTC en el que expira el access token actual.
        /// </summary>
        public DateTime AccessTokenExpiresAtUtc { get; set; }

        /// <summary>
        /// Scopes otorgados (ej: "offline_access read write").
        /// </summary>
        [StringLength(4000)]
        public string? Scope { get; set; }

        /// <summary>
        /// Cuenta habilitada para operar desde el ERP.
        /// </summary>
        public bool Activa { get; set; } = true;

        /// <summary>
        /// Última prueba de conexión (GET /users/me) en UTC.
        /// </summary>
        public DateTime? UltimaPruebaConexionUtc { get; set; }

        /// <summary>
        /// Resultado de la última prueba de conexión.
        /// </summary>
        public bool? UltimaPruebaConexionOk { get; set; }

        /// <summary>
        /// Última importación de publicaciones completada (UTC).
        /// </summary>
        public DateTime? UltimaImportacionListingsUtc { get; set; }

        public virtual ICollection<MercadoLibreListing> Listings { get; set; } = new List<MercadoLibreListing>();
        public virtual ICollection<MercadoLibreOrder> Orders { get; set; } = new List<MercadoLibreOrder>();
    }
}
