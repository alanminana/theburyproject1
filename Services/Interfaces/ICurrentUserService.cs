namespace TheBuryProject.Services
{
    public interface ICurrentUserService
    {
        string GetUsername();
        string GetUserId();
        bool IsAuthenticated();
        string? GetEmail();
        bool IsInRole(string role);
        bool HasPermission(string modulo, string accion);
        string? GetIpAddress();
    }
}
