namespace TheBuryProject.Services.Interfaces
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

        // Implementación default para no romper los stubs de test existentes que
        // implementan la interfaz explícitamente (no la necesitan para sus escenarios).
        string? GetUserAgent() => null;
    }
}
