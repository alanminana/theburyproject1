using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TheBuryProject.Services;

namespace TheBuryProject.Middleware
{
    public class AuditMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AuditMiddleware> _logger;

        public AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var startedAt = DateTimeOffset.UtcNow;
            var method = context.Request.Method;
            var path = context.Request.Path.Value;

            // Resolver por request (evita problemas de lifetime si es scoped)
            var currentUserService = context.RequestServices.GetService(typeof(ICurrentUserService)) as ICurrentUserService;

            var isAuth = currentUserService?.IsAuthenticated() == true;
            var userName = isAuth ? currentUserService!.GetUsername() : "Anonymous";
            var userId = isAuth ? currentUserService!.GetUserId() : "anonymous";

            try
            {
                await _next(context);
            }
            finally
            {
                var statusCode = context.Response?.StatusCode;
                _logger.LogInformation(
                    "HTTP {Method} {Path} executed by {UserName} ({UserId}) at {StartedAt} => {StatusCode}",
                    method,
                    path,
                    userName,
                    userId,
                    startedAt,
                    statusCode);
            }
        }
    }
}
