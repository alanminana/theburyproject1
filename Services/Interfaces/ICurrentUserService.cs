using System.Security.Claims;

namespace TheBuryProject.Services
{
    public interface ICurrentUserService
    {
        string GetUsername();
        string GetUserId();
        bool IsAuthenticated();
        string? GetEmail();
    }
}
