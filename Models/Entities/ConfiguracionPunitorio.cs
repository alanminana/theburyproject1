using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Configuración versionada y auditable del punitorio por mora (PUN-ML3). Fuente canónica
    /// única — no reutiliza <c>Credito.TasaInteres</c> (recargo del plan, concepto independiente)
    /// ni <c>ConfiguracionMora</c> (que conserva días/estados de mora, autorización, aptitud y
    /// alertas; nunca tasas ni fórmula de punitorio).
    ///
    /// Append-only: nunca se edita una fila existente. Cada cambio funcional inserta una versión
    /// nueva con <see cref="VigenteDesde"/> estrictamente posterior a la última ya creada (ver
    /// <c>ConfiguracionPunitorioService.CrearNuevaVersionAsync</c>). "Vigente" en una fecha dada
    /// es la versión con mayor <see cref="VigenteDesde"/> que sea &lt;= esa fecha.
    ///
    /// No persiste <c>VigenteHasta</c>/<c>EsVigente</c> (a diferencia de <see cref="ProductoPrecioLista"/>,
    /// el otro append-only del proyecto): esta tabla es un único stream global de pocas filas, no
    /// una serie por clave (producto×lista) que necesite un rango indexado para performance. El cierre
    /// de una versión es implícito: la vigencia de la fila N termina donde empieza la fila N+1.
    ///
    /// No persiste "RequiereConfirmacionManual": la regla "el cálculo puede mostrarse automáticamente,
    /// pero la aplicación persistida siempre requiere confirmación autorizada" es una invariante fija
    /// del sistema (PUN-ML5), no una decisión de negocio por versión — no existe ningún escenario en
    /// el que una versión permita aplicación automática sin confirmación. Persistirla como booleano
    /// "por si acaso" sería un campo configurable sin necesidad demostrada.
    /// </summary>
    public class ConfiguracionPunitorio : AuditableEntity
    {
        /// <summary>
        /// Porcentaje correspondiente a <see cref="PeriodoDias"/> (no tasa mensual, no anual, no
        /// recargo del crédito). Ej.: Porcentaje=10, PeriodoDias=20 → 10% cada 20 días. Permite 0%
        /// (cuota nunca en mora bajo esta versión); nunca negativo. Sin tope máximo: no se valida
        /// un porcentaje "razonable" — es una decisión de negocio sin límite impuesto por el sistema.
        /// </summary>
        public decimal Porcentaje { get; set; }

        /// <summary>Días a los que corresponde <see cref="Porcentaje"/>. Nunca aproximado a "mes".</summary>
        public int PeriodoDias { get; set; }

        /// <summary>
        /// Días de gracia antes de que empiece a devengar punitorio. Independiente de los umbrales
        /// de autorización de <c>ConfiguracionMora</c> (conceptos distintos que hoy coinciden en
        /// nombre pero no se leen entre sí).
        /// </summary>
        public int DiasGracia { get; set; }

        /// <summary>Si el importe se prorratea día a día (true) o se trunca a múltiplos de <see cref="PeriodoDias"/> (false).</summary>
        public bool ProrrateoDiario { get; set; } = true;

        /// <summary>
        /// Decisión de negocio: si esta configuración, una vez que PUN-ML4/PUN-ML5 implementen el
        /// cálculo real, debe interpretarse sobre cuotas ya vencidas al momento de crearla. En este
        /// lote solo se almacena y se expone — no se aplica ni se recalcula nada todavía.
        /// </summary>
        public bool AplicacionRetroactiva { get; set; }

        /// <summary>
        /// Fecha comercial (<c>IRelojComercial</c>, no UTC) desde la cual esta versión es la
        /// vigente. Debe ser estrictamente posterior a la <see cref="VigenteDesde"/> de cualquier
        /// versión previa — el servicio la valida antes de insertar.
        /// </summary>
        public DateOnly VigenteDesde { get; set; }

        /// <summary>
        /// Si esta versión, mientras es la vigente, debe considerarse activa. Una versión vigente
        /// pero inactiva es un estado distinto de "no hay configuración": alguien la desactivó
        /// deliberadamente (ver <c>ConfiguracionPunitorioService.ObtenerVigenteAsync</c>).
        /// </summary>
        public bool Activa { get; set; } = true;

        /// <summary>Motivo del cambio. Obligatorio cuando <see cref="VigenteDesde"/> es retroactiva (anterior a hoy comercial).</summary>
        public string? MotivoCambio { get; set; }
    }
}
