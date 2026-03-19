using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    public interface IDocumentacionService
    {
        Task<DocumentacionCreditoResultado> ProcesarDocumentacionVentaAsync(int ventaId, bool crearCreditoSiCompleta = true);
    }
}
