using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Aplicación autorizada y auditable de un punitorio calculado por <see cref="Interfaces.IPunitorioCalculator"/>
    /// (PUN-ML5). Fuente canónica de "punitorio aplicado" — distinta de "punitorio calculado" (efímero,
    /// <c>PunitorioConsultaResultado</c>, nunca persiste) y de "punitorio pagado" (PUN-ML6, todavía no
    /// existe ningún productor de <see cref="EstadoPunitorioAplicado.Pagado"/>).
    ///
    /// Modelo A (aplicación reemplazable): como máximo una fila con <see cref="Estado"/> =
    /// <see cref="EstadoPunitorioAplicado.Aplicado"/> por <see cref="CuotaId"/> — garantizado por un
    /// índice único filtrado (ver <c>AppDbContext</c>) además de la verificación previa del servicio.
    /// Aplicar con una fila activa ya existente se rechaza siempre (nunca se acumula ni se reemplaza
    /// implícitamente): quien quiera recalcular debe anular explícitamente primero. Esto evita el
    /// doble devengamiento de los mismos días sin necesidad de un modelo de intervalos Desde/Hasta.
    ///
    /// Append-only en la práctica: nunca se hace UPDATE de los campos de cálculo. Anular solo agrega
    /// <see cref="FechaAnulacion"/>/<see cref="UsuarioAnulacion"/>/<see cref="MotivoAnulacion"/> y
    /// cambia <see cref="Estado"/>; la fila nunca se borra.
    /// </summary>
    public class PunitorioAplicado : AuditableEntity
    {
        public int CuotaId { get; set; }

        /// <summary>
        /// Fecha comercial (<c>IRelojComercial.HoyComercial</c>) usada para el recálculo server-side
        /// inmediato a la aplicación. Nunca la que haya enviado el navegador.
        /// </summary>
        public DateOnly FechaCalculo { get; set; }

        /// <summary>Saldo impago de la cuota al momento de aplicar (<c>PunitorioCalculoResultado.SaldoFinal</c>).</summary>
        public decimal SaldoBase { get; set; }

        public int DiasComputados { get; set; }

        /// <summary>Importe exacto aplicado (<c>PunitorioCalculoResultado.PunitorioRedondeado</c>). Siempre mayor a cero.</summary>
        public decimal Importe { get; set; }

        public EstadoPunitorioAplicado Estado { get; set; } = EstadoPunitorioAplicado.Aplicado;

        /// <summary>
        /// Configuración dominante cuando todo el período usó una única versión de
        /// <see cref="ConfiguracionPunitorio"/>. <c>null</c> cuando el período atravesó varias
        /// configuraciones — en ese caso <see cref="DesgloseSnapshotJson"/> es la única fuente
        /// completa (una FK sola no alcanza para un cálculo por tramos).
        /// </summary>
        public int? ConfiguracionPunitorioId { get; set; }

        public decimal? Porcentaje { get; set; }
        public int? PeriodoDias { get; set; }
        public int? DiasGracia { get; set; }

        /// <summary>
        /// Snapshot JSON completo y auditable del resultado del calculador en el momento de aplicar
        /// (segmentos, configuraciones usadas con sus valores tal cual estaban ese día, saldo, días).
        /// No depende de que <see cref="ConfiguracionPunitorio"/> conserve esos valores en el futuro
        /// — aunque esa tabla es append-only, el snapshot es la fuente autosuficiente para reproducir
        /// qué se aprobó sin tener que reconstruirlo cruzando otras tablas.
        /// </summary>
        public string DesgloseSnapshotJson { get; set; } = string.Empty;

        public string MotivoAplicacion { get; set; } = string.Empty;

        /// <summary>Instante UTC de la aplicación (<c>IRelojComercial.AhoraUtc</c>), no <c>DateTime.UtcNow</c> directo.</summary>
        public DateTime FechaAplicacion { get; set; }

        /// <summary>
        /// Identidad resuelta y autorizada por el caller (controller, fuera de alcance en PUN-ML5)
        /// antes de invocar el servicio — mismo patrón de deferimiento que
        /// <c>ConfiguracionPunitorioComando.AutorizadoParaRetroactivo</c> (PUN-ML3).
        /// </summary>
        public string UsuarioAplicacion { get; set; } = string.Empty;

        public DateTime? FechaAnulacion { get; set; }
        public string? UsuarioAnulacion { get; set; }
        public string? MotivoAnulacion { get; set; }

        public virtual Cuota Cuota { get; set; } = null!;
        public virtual ConfiguracionPunitorio? ConfiguracionPunitorio { get; set; }
    }
}
