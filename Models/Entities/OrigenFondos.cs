using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// D-03 Tesorería administrativa: de dónde sale el dinero de un pago o anticipo a proveedor (efectivo administrativo,
    /// una cuenta bancaria, etc.). Catálogo configurable por el administrador; NO es una caja de ventas y no tiene saldo
    /// propio: solo identifica el origen de los egresos. Un origen con movimientos no se borra: se desactiva.
    /// </summary>
    public class OrigenFondos : AuditableEntity
    {
        /// <summary>Código corto, único sin distinguir mayúsculas ni espacios (se guarda recortado y en mayúsculas).</summary>
        [Required]
        [StringLength(30)]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        public TipoOrigenFondos Tipo { get; set; } = TipoOrigenFondos.Otro;

        [StringLength(500)]
        public string? Descripcion { get; set; }

        /// <summary>Un origen inactivo no se ofrece para nuevos pagos/anticipos pero conserva su historia.</summary>
        public bool Activo { get; set; } = true;

        public int Orden { get; set; }
    }
}
