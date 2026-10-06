using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Configuración global del aviso periódico "actualizar datos del cliente".
    /// Cuando está activa y el cliente superó <see cref="DiasRevision"/> días sin que se editen sus datos,
    /// al buscarlo en Venta o abrirlo en Cliente se le pide al usuario revisarlos.
    /// Se espera una única fila (configuración global).
    /// </summary>
    public class ConfiguracionActualizacionDatosCliente : AuditableEntity
    {
        public const int DiasRevisionMinimo = 1;
        public const int DiasRevisionMaximo = 3650;

        /// <summary>Si el aviso está habilitado. Apagado por defecto: no cambia el comportamiento hasta que se configure.</summary>
        public bool Activa { get; set; } = false;

        /// <summary>Cada cuántos días debe revisarse la información de un cliente.</summary>
        public int DiasRevision { get; set; } = 180;

        public static ConfiguracionActualizacionDatosCliente CrearDefault() => new();

        /// <summary>
        /// Indica si los datos del cliente están vencidos. Sin fecha de última actualización
        /// se toma la fecha de alta; sin ninguna de las dos no se puede afirmar que estén vencidos.
        /// </summary>
        public bool RequiereActualizacion(DateTime? fechaUltimaActualizacion, DateTime fechaAlta, DateTime ahoraUtc, out int diasTranscurridos)
        {
            var referencia = fechaUltimaActualizacion ?? fechaAlta;
            diasTranscurridos = referencia == default ? 0 : Math.Max(0, (int)(ahoraUtc - referencia).TotalDays);

            return Activa && referencia != default && diasTranscurridos >= DiasRevision;
        }
    }
}
