namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Fuente única de la leyenda que deja claro que un impreso del ERP es un documento interno y NO una factura
    /// fiscal (el ERP no integra ARCA/CAE; decisión del propietario 2026-11-11). Todo impreso o PDF que un cliente
    /// o un tercero pueda ver y que lleve letra de factura, numeración o IVA debe mostrar este texto, tal cual.
    /// No agrega ninguna regla fiscal: es solo una advertencia de lectura.
    /// </summary>
    public static class ComprobanteInternoLeyenda
    {
        public const string Texto = "COMPROBANTE INTERNO — DOCUMENTO NO VÁLIDO COMO FACTURA";
    }
}
