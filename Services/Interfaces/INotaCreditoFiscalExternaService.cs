namespace TheBuryProject.Services.Interfaces
{
    /// <summary>Datos de la nota de crédito FISCAL emitida por el sistema externo (el ERP no emite CAE).</summary>
    public sealed class NotaCreditoFiscalExternaRequest
    {
        public string PuntoVenta { get; set; } = string.Empty;
        /// <summary>NCA, NCB, NCC, NCM o NCE.</summary>
        public string Tipo { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public decimal Importe { get; set; }
        public string? Cae { get; set; }
    }

    public sealed class NotaCreditoFiscalExternaResultado
    {
        public bool Exitoso => Errores.Count == 0;
        public List<string> Errores { get; } = new();
        public static NotaCreditoFiscalExternaResultado Ok() => new();
        public static NotaCreditoFiscalExternaResultado Fallo(params string[] errores)
        {
            var r = new NotaCreditoFiscalExternaResultado();
            r.Errores.AddRange(errores);
            return r;
        }
    }

    /// <summary>
    /// P-02 / P3 (preparado, SIN efecto fiscal): registra el vínculo entre una nota de crédito interna y la NC fiscal externa. No netea
    /// IVA ni modifica ningún importe: el Reporte de IVA no lo lee hasta que el propietario autorice P3 y el contador defina el período.
    /// </summary>
    public interface INotaCreditoFiscalExternaService
    {
        Task<NotaCreditoFiscalExternaResultado> RegistrarAsync(int notaCreditoId, NotaCreditoFiscalExternaRequest request);
    }
}
