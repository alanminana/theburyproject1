using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Configuración global del scoring de comportamiento del cliente (PuntajeCliente).
    /// Cada factor se puede activar/desactivar y parametrizar (umbral + puntos),
    /// de modo que la regla sea editable desde una pantalla de configuración sin tocar código.
    /// Es independiente del riesgo crediticio (PuntajeRiesgo/NivelRiesgo) y de la mora.
    /// Se espera una única fila (configuración global).
    /// </summary>
    public class ConfiguracionScoringCliente : AuditableEntity
    {
        // ==========================
        // Puntaje base y límites
        // ==========================

        /// <summary>Puntaje con el que arranca todo cliente antes de aplicar factores.</summary>
        public int PuntajeBase { get; set; } = 1;

        /// <summary>Mínimo al que se acota el puntaje final.</summary>
        public int PuntajeMinimo { get; set; } = 1;

        /// <summary>Máximo al que se acota el puntaje final.</summary>
        public int PuntajeMaximo { get; set; } = 5;

        // ==========================
        // Factor: Antigüedad del cliente
        // ==========================

        public bool AntiguedadActiva { get; set; } = true;

        /// <summary>Meses de antigüedad requeridos para sumar los puntos del factor.</summary>
        public int AntiguedadMesesUmbral { get; set; } = 12;

        /// <summary>Puntos a sumar si el cliente supera el umbral de antigüedad.</summary>
        public int AntiguedadPuntos { get; set; } = 1;

        // ==========================
        // Factor: Actividad de compra (última venta)
        // ==========================

        public bool ActividadActiva { get; set; } = true;

        /// <summary>Meses hacia atrás dentro de los cuales la última venta cuenta como "activo".</summary>
        public int ActividadMesesUmbral { get; set; } = 6;

        public int ActividadPuntos { get; set; } = 1;

        // ==========================
        // Factor: Pago de créditos en término
        // ==========================

        public bool PagoEnTerminoActivo { get; set; } = true;

        /// <summary>Puntos si el cliente tiene historial de pago y ningún crédito con atraso (bonus buen pagador).</summary>
        public int PagoEnTerminoPuntos { get; set; } = 2;

        /// <summary>Puntos si el cliente tiene al menos un crédito con atraso (normalmente negativo).</summary>
        public int PagoConAtrasoPuntos { get; set; } = -2;

        // ==========================
        // Factor: Sueldo mínimo
        // ==========================

        public bool SueldoActivo { get; set; } = false;

        /// <summary>Sueldo mínimo (input configurable) a partir del cual se suman los puntos del factor.</summary>
        public decimal SueldoUmbral { get; set; } = 0m;

        public int SueldoPuntos { get; set; } = 1;

        /// <summary>
        /// Configuración por defecto (fórmula base): antigüedad ≥ 12 meses (+1),
        /// actividad ≤ 6 meses (+1), buen pagador (+2) / con atraso (-2), sueldo apagado.
        /// Se usa cuando todavía no existe una fila persistida.
        /// </summary>
        public static ConfiguracionScoringCliente CrearDefault() => new();
    }
}
