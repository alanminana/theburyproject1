namespace TheBuryProject.Services.Exceptions
{
    /// <summary>
    /// Rechazo controlado de <c>ContratoVentaCreditoService.GenerarAsync</c> por datos
    /// contractuales faltantes (Cliente, Crédito, Garante o Plantilla) — la MISMA lista que ya
    /// devuelve <c>ValidarDatosParaGenerarAsync</c>. Hereda de <see cref="InvalidOperationException"/>
    /// para no romper callers/tests existentes que ya atrapan ese tipo, pero es distinguible por
    /// tipo de las demás <see cref="InvalidOperationException"/> que puede lanzar el mismo camino
    /// (ruta de archivo inválida, snapshot corrupto, contrato no encontrado tras generarlo) — esas
    /// sí son bugs inesperados y no deben tratarse como una validación de negocio conocida.
    /// </summary>
    public sealed class ContratoVentaCreditoValidacionException : InvalidOperationException
    {
        public ContratoVentaCreditoValidacionException(IReadOnlyList<string> errores)
            : base(string.Join(" ", errores))
        {
            Errores = errores;
        }

        public IReadOnlyList<string> Errores { get; }
    }
}
