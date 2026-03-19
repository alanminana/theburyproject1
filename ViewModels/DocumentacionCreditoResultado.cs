namespace TheBuryProject.ViewModels
{
    public class DocumentacionCreditoResultado
    {
        public bool DocumentacionCompleta { get; set; }
        public string MensajeFaltantes { get; set; } = string.Empty;
        public int ClienteId { get; set; }
        public int VentaId { get; set; }
        public int? CreditoId { get; set; }
        public bool CreditoCreado { get; set; }
    }
}
