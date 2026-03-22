using System.Security.Claims;
using TheBuryProject.Helpers;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string GetUsername()
        {
            return _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "Sistema";
        }

        public string GetUserId()
        {
            var userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return userId ?? "system";
        }

        public bool IsAuthenticated()
        {
            return _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
        }

        public string? GetEmail()
        {
            return _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email);
        }

        public bool IsInRole(string role)
        {
            return _httpContextAccessor.HttpContext?.User?.IsInRole(role) ?? false;
        }

        public bool HasPermission(string modulo, string accion)
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user != null && user.TienePermiso(modulo, accion);
        }

        public string? GetIpAddress()
        {
            return _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        }
    }
}