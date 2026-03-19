using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    public interface IDashboardService
    {
        Task<DashboardViewModel> GetDashboardDataAsync();
    }
}