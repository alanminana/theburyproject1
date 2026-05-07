namespace TheBuryProject.ViewModels
{
    public class CreditoIndexViewModel
    {
        public CreditoFilterViewModel Filter { get; set; } = new();

        public List<CreditoClienteIndexViewModel> Clientes { get; set; } = new();
    }
}
