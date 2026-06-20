using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Pantalla de configuración del canal Mercado Libre (junto a las demás
    /// configuraciones de Ventas).
    /// </summary>
    public class MercadoLibreConfiguracionViewModel
    {
        public int? AccountId { get; set; }

        public int? ListaPrecioId { get; set; }

        public int? SucursalId { get; set; }

        public int? ClienteMercadoLibreId { get; set; }

        [Range(-99, 999, ErrorMessage = "El ajuste de canal debe estar entre -99% y 999%")]
        public decimal AjusteCanalPorcentaje { get; set; }

        [Range(0, 100, ErrorMessage = "La comisión estimada debe estar entre 0% y 100%")]
        public decimal ComisionEstimadaPorcentaje { get; set; } = 13m;

        [Range(0, 99999999, ErrorMessage = "El costo de envío estimado no puede ser negativo")]
        public decimal CostoEnvioEstimado { get; set; }

        [Range(0, 999, ErrorMessage = "El margen mínimo debe estar entre 0% y 999%")]
        public decimal? MargenMinimoPorcentaje { get; set; }

        public string ReglaRedondeo { get; set; } = "ninguno";

        /// <summary>Origen global del stock publicado hacia ML (override por publicación posible).</summary>
        public MercadoLibreOrigenStock OrigenStock { get; set; } = MercadoLibreOrigenStock.StockLogicoProducto;

        public bool SyncAutomaticaStock { get; set; }
        public bool SyncAutomaticaPrecio { get; set; }
        public bool ImportacionAutomaticaOrdenes { get; set; } = true;
        public bool CrearVentaAutomatica { get; set; }
        public bool PermitirPublicacionDesdeErp { get; set; }
        public bool ModoSimulacion { get; set; } = true;

        public MercadoLibrePoliticaDevolucion PoliticaDevolucion { get; set; }

        // Lookups para selects (solo lectura)
        public List<(int Id, string Nombre)> CuentasDisponibles { get; set; } = new();
        public List<(int Id, string Nombre)> ListasPreciosDisponibles { get; set; } = new();
        public List<(int Id, string Nombre)> SucursalesDisponibles { get; set; } = new();

        /// <summary>Cliente actualmente configurado, para mostrar en el picker.</summary>
        public string? ClienteMercadoLibreNombre { get; set; }
    }
}
