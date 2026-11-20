using TheBuryProject.Models.Entities;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// P-02 / C13-C14 (contador, 2026-11-19): política COMERCIAL de devoluciones. Es solo informativa: no bloquea ni limita
    /// devoluciones, garantías, arrepentimiento ni reintegros que la ley obliga a aceptar (docs/p02-respuestas-contador-20261119.md §3.2);
    /// el sistema la evalúa para advertir al operador y dejar constancia auditable.
    /// <list type="bullet">
    /// <item>Venta presencial: 72 horas. Mercado Libre: 30 días.</item>
    /// <item>Reintegro en dinero: dentro de las primeras 24 horas (venta presencial); después corresponde nota de crédito.</item>
    /// <item>Motivos de garantía o defecto/entrega (defecto, daño, faltante, error de despacho, producto incorrecto, garantía) nunca
    /// quedan «fuera de política»: son derechos del consumidor.</item>
    /// </list>
    /// </summary>
    public static class DevolucionPolitica
    {
        public const int HorasVentaPresencial = 72;
        public const int DiasMercadoLibre = 30;
        public const int HorasReintegroEnDinero = 24;

        public readonly record struct Evaluacion(
            bool EsMercadoLibre,
            double HorasDesdeVenta,
            bool MotivoProtegido,
            bool FueraDePlazoComercial,
            bool ReintegroEnDineroFueraDePolitica,
            string? Aviso)
        {
            public bool FueraDePolitica => FueraDePlazoComercial || ReintegroEnDineroFueraDePolitica;
        }

        public static bool EsMotivoProtegido(MotivoDevolucion motivo) => motivo is
            MotivoDevolucion.DefectoFabrica or MotivoDevolucion.ProductoDanado or MotivoDevolucion.Garantia
            or MotivoDevolucion.ProductoIncorrecto or MotivoDevolucion.Faltante or MotivoDevolucion.ErrorDespacho;

        /// <param name="fechaVentaUtc">Instante UTC de la venta (<c>Venta.FechaVenta</c>).</param>
        /// <param name="reintegroEnDinero">La resolución elegida es devolver dinero (no nota de crédito ni cambio).</param>
        public static Evaluacion Evaluar(
            bool esMercadoLibre, DateTime fechaVentaUtc, DateTime ahoraUtc, MotivoDevolucion motivo, bool reintegroEnDinero)
        {
            var horas = Math.Max(0d, (ahoraUtc - fechaVentaUtc).TotalHours);
            var protegido = EsMotivoProtegido(motivo);
            var plazo = esMercadoLibre ? TimeSpan.FromDays(DiasMercadoLibre) : TimeSpan.FromHours(HorasVentaPresencial);
            var fueraPlazo = !protegido && horas > plazo.TotalHours;
            var dineroFuera = !protegido && !esMercadoLibre && reintegroEnDinero && horas > HorasReintegroEnDinero;

            string? aviso = null;
            if (fueraPlazo)
            {
                aviso = esMercadoLibre
                    ? $"Fuera del plazo comercial de {DiasMercadoLibre} días de Mercado Libre."
                    : $"Fuera del plazo comercial de {HorasVentaPresencial} horas de la venta presencial.";
            }
            else if (dineroFuera)
            {
                aviso = $"Reintegro en dinero fuera de las primeras {HorasReintegroEnDinero} horas: corresponde nota de crédito salvo derecho legal del cliente.";
            }

            return new Evaluacion(esMercadoLibre, horas, protegido, fueraPlazo, dineroFuera, aviso);
        }
    }
}
