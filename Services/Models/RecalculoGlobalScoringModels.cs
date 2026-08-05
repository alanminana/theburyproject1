using System.Collections.Generic;

namespace TheBuryProject.Services.Models
{
    /// <summary>
    /// PUN-ML10-G: política de continuidad ante fallos individuales durante un recálculo global.
    /// </summary>
    public enum PoliticaErroresRecalculoGlobal
    {
        /// <summary>Un cliente que falla se registra en <see cref="RecalculoGlobalScoringResultado.IdsFallidos"/> y el lote continúa con el siguiente.</summary>
        ContinuarTrasFallos = 0,

        /// <summary>El primer fallo detiene el lote completo; los clientes ya procesados quedan en el resumen parcial.</summary>
        DetenerEnPrimerFallo = 1
    }

    /// <summary>
    /// Opciones de <see cref="Interfaces.IClienteScoringService.RecalcularTodosAsync"/>.
    /// </summary>
    public sealed class RecalculoGlobalScoringOpciones
    {
        /// <summary>Cantidad de clientes procesados por lote antes de reevaluar cancelación. Mínimo 1.</summary>
        public int BatchSize { get; init; } = 50;

        /// <summary>
        /// Si es true, calcula el puntaje que resultaría sin persistir ningún cambio ni escribir
        /// historial — mismo <see cref="ClienteScoringCalculator"/>, sólo se omite el <c>SaveChangesAsync</c>.
        /// </summary>
        public bool Preview { get; init; }

        /// <summary>Origen auditado en <c>ClientePuntajeHistorial.Origen</c> (ignorado en modo preview).</summary>
        public string Origen { get; init; } = "RecalculoGlobal";

        /// <summary>Observación auditada (ignorada en modo preview).</summary>
        public string? Observacion { get; init; }

        /// <summary>Usuario que disparó el recálculo (ignorado en modo preview).</summary>
        public string? RegistradoPor { get; init; }

        /// <summary>Política de continuidad ante un fallo individual. Ver <see cref="PoliticaErroresRecalculoGlobal"/>.</summary>
        public PoliticaErroresRecalculoGlobal PoliticaErrores { get; init; } = PoliticaErroresRecalculoGlobal.ContinuarTrasFallos;
    }

    /// <summary>
    /// Resumen tipado de un recálculo global de scoring (PUN-ML10-G).
    /// </summary>
    public sealed class RecalculoGlobalScoringResultado
    {
        /// <summary>Cantidad de clientes elegibles considerados (activos, no eliminados).</summary>
        public int Examinados { get; init; }

        /// <summary>Cantidad de clientes cuyo puntaje cambió (o hubiera cambiado, en modo preview).</summary>
        public int Recalculados { get; init; }

        /// <summary>Cantidad de clientes examinados cuyo puntaje ya estaba al día.</summary>
        public int SinCambios { get; init; }

        /// <summary>Cantidad de clientes que fallaron durante el recálculo.</summary>
        public int Fallidos { get; init; }

        /// <summary>Ids de los clientes fallidos, en el mismo orden en que fallaron.</summary>
        public IReadOnlyList<int> IdsFallidos { get; init; } = System.Array.Empty<int>();

        /// <summary>True si este resumen corresponde a un modo preview (nada se persistió).</summary>
        public bool Preview { get; init; }

        /// <summary>True si el lote se interrumpió por cancelación o por <see cref="PoliticaErroresRecalculoGlobal.DetenerEnPrimerFallo"/> antes de examinar a todos los elegibles.</summary>
        public bool Interrumpido { get; init; }
    }
}
