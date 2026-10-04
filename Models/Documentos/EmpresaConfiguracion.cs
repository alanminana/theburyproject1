using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Datos de la empresa que firma los documentos (variables <c>empresa.*</c>). Una sola fila vigente.
    /// Antes salían de la plantilla de contrato; ahora tienen su propia configuración y la plantilla
    /// legada se mantiene sincronizada para no romper el contrato anterior.
    /// </summary>
    public class EmpresaConfiguracion : AuditableEntity
    {
        [Required, StringLength(200)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(20)]
        public string? Cuit { get; set; }

        [StringLength(20)]
        public string? Dni { get; set; }

        [Required, StringLength(300)]
        public string Domicilio { get; set; } = string.Empty;

        [Required, StringLength(120)]
        public string Ciudad { get; set; } = string.Empty;

        [Required, StringLength(200)]
        public string Jurisdiccion { get; set; } = string.Empty;

        public decimal InteresMoraDiarioPorcentaje { get; set; }

        /// <summary>Nombre de fantasía que encabeza presupuestos y recibos (<c>empresa.nombreComercial</c>). Si está vacío se usa el nombre.</summary>
        [StringLength(200)]
        public string? NombreComercial { get; set; }

        /// <summary>Domicilio tal como se escribe en el contrato (<c>empresa.direccionCompleta</c>). Si está vacío se usa el domicilio.</summary>
        [StringLength(300)]
        public string? DomicilioCompleto { get; set; }

        /// <summary>
        /// El ERP no guarda la condición fiscal de cada cliente: este valor se muestra en los comprobantes
        /// (<c>cliente.condicionFiscal</c>) para todos los clientes. Ej.: "Consumidor Final".
        /// </summary>
        [StringLength(60)]
        public string? CondicionFiscalClientePorDefecto { get; set; }

        /// <summary>
        /// Cómo se determina el vencimiento del pagaré (<c>pagare.fechaVencimiento</c>). Vacío = sin definir: el pagaré
        /// sale con el espacio en blanco para completar a mano; el sistema nunca asume una regla. Ver <see cref="PagareVencimientoModos"/>.
        /// </summary>
        [StringLength(30)]
        public string? PagareVencimientoModo { get; set; }

        /// <summary>Días desde la fecha de la operación, solo con el modo <see cref="PagareVencimientoModos.DiasDesdeOperacion"/>.</summary>
        public int? PagareVencimientoDias { get; set; }
    }

    public static class PagareVencimientoModos
    {
        public const string PrimeraCuota = "PrimeraCuota";
        public const string UltimaCuota = "UltimaCuota";
        public const string DiasDesdeOperacion = "DiasDesdeOperacion";

        public static readonly string[] Todos = { PrimeraCuota, UltimaCuota, DiasDesdeOperacion };

        public static string Etiqueta(string? modo) => modo switch
        {
            PrimeraCuota => "Vencimiento de la primera cuota",
            UltimaCuota => "Vencimiento de la última cuota",
            DiasDesdeOperacion => "Días desde la fecha de la operación",
            _ => "Sin definir (se completa a mano)"
        };
    }
}
