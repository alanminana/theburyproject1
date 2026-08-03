using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Calculador puro del punitorio de una cuota vencida (PUN-ML4): interés simple sobre saldo
    /// impago, prorrateo diario, gracia y segmentación por cambios de saldo/configuración — ver el
    /// plan de punitorios §8-9 y los contratos congelados PUN-ML1..ML3.
    ///
    /// Sin efectos secundarios: no consulta la base, no depende de <c>HttpContext</c>, del navegador
    /// ni de la hora del sistema (nada de <c>DateTime.Now</c>/UTC — solo <see cref="DateOnly"/>
    /// comercial), no modifica ninguna entidad ni la propia entrada, y llamarlo dos veces con la
    /// misma entrada produce siempre el mismo resultado.
    ///
    /// El caller (PUN-ML5) es responsable de obtener la cuota real, los pagos (efectivos o no — el
    /// filtro se aplica adentro) y las configuraciones vigentes, y de decidir qué hacer con el
    /// resultado. Este servicio nunca aplica ni persiste un punitorio.
    /// </summary>
    public interface IPunitorioCalculator
    {
        /// <summary>
        /// Calcula el punitorio de una cuota. Nunca lanza excepciones por datos de negocio inválidos
        /// o inconsistentes (pago que supera el saldo, configuraciones contradictorias, etc.): esos
        /// casos se representan como <see cref="EstadoResultadoPunitorio.EntradaInvalida"/> en el
        /// resultado, para que el resultado siga siendo un valor puro e inspeccionable.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="entrada"/> es null.</exception>
        PunitorioCalculoResultado Calcular(PunitorioCalculoEntrada entrada);
    }
}
