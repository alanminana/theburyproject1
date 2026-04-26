using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    public interface IPlantillaContratoCreditoService
    {
        Task<PlantillaContratoCreditoViewModel> ObtenerParaEdicionAsync();
        Task<PlantillaContratoCreditoViewModel> GuardarAsync(PlantillaContratoCreditoViewModel model);
    }
}
