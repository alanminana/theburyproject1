using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Configuración operativa del canal Mercado Libre (fila única, GetOrCreate).
    /// Vive junto a las demás configuraciones de Ventas.
    /// Todos los flags de automatización nacen apagados y ModoSimulacion nace
    /// encendido: ninguna acción real contra ML ocurre sin decisión explícita.
    /// </summary>
    public class MercadoLibreConfiguracion : AuditableEntity
    {
        /// <summary>Cuenta ML usada para operar (única cuenta activa del MVP).</summary>
        public int? AccountId { get; set; }

        /// <summary>Lista de precios ERP que alimenta el precio ML.</summary>
        public int? ListaPrecioId { get; set; }

        /// <summary>Sucursal/depósito de referencia para stock ML (informativo: el stock del ERP es global).</summary>
        public int? SucursalId { get; set; }

        /// <summary>Cliente interno al que se imputan las ventas ML.</summary>
        public int? ClienteMercadoLibreId { get; set; }

        /// <summary>Ajuste porcentual de canal sobre el precio ERP (ej: 10 = +10%).</summary>
        public decimal AjusteCanalPorcentaje { get; set; }

        /// <summary>Comisión estimada de ML en % (fallback cuando no se consulta la API de listing_prices).</summary>
        public decimal ComisionEstimadaPorcentaje { get; set; } = 13m;

        /// <summary>Costo de envío estimado a cargo del vendedor ($), usado en neto estimado.</summary>
        public decimal CostoEnvioEstimado { get; set; }

        /// <summary>Margen mínimo aceptado en % sobre costo. Null = sin validación.</summary>
        public decimal? MargenMinimoPorcentaje { get; set; }

        /// <summary>Regla de redondeo del precio ML: ninguno | decena | centena | mil.</summary>
        [StringLength(20)]
        public string ReglaRedondeo { get; set; } = "ninguno";

        /// <summary>
        /// Origen global del stock publicado hacia ML. Cada publicación puede
        /// sobreescribirlo con MercadoLibreListing.OrigenStockOverride.
        /// </summary>
        public MercadoLibreOrigenStock OrigenStock { get; set; } = MercadoLibreOrigenStock.StockLogicoProducto;

        /// <summary>Permite que el procesamiento de webhooks de items refresque stock/precio ERP→ML automáticamente.</summary>
        public bool SyncAutomaticaStock { get; set; }

        /// <summary>Permite push automático de precio ERP→ML.</summary>
        public bool SyncAutomaticaPrecio { get; set; }

        /// <summary>Permite importar órdenes automáticamente al recibir webhooks orders_v2.</summary>
        public bool ImportacionAutomaticaOrdenes { get; set; } = true;

        /// <summary>Permite crear la Venta interna (con descuento de stock) automáticamente al importar una orden paga.</summary>
        public bool CrearVentaAutomatica { get; set; }

        /// <summary>Habilita el flujo de publicación desde el ERP (Fase F). Apagado en el MVP.</summary>
        public bool PermitirPublicacionDesdeErp { get; set; }

        /// <summary>
        /// Modo simulación global: cuando está activo, los push de stock/precio y
        /// cambios de publicación calculan y loguean lo que se enviaría, sin llamar a ML.
        /// </summary>
        public bool ModoSimulacion { get; set; } = true;

        /// <summary>Política inicial sugerida ante devoluciones (la decisión final es manual).</summary>
        public MercadoLibrePoliticaDevolucion PoliticaDevolucion { get; set; } = MercadoLibrePoliticaDevolucion.PendienteRevision;

        public virtual MercadoLibreAccount? Account { get; set; }
        public virtual ListaPrecio? ListaPrecio { get; set; }
        public virtual Sucursal? Sucursal { get; set; }
        public virtual Cliente? ClienteMercadoLibre { get; set; }
    }
}
