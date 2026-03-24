using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Hubs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class NotificacionService : INotificacionService
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<NotificacionService> _logger;
        private readonly IHubContext<NotificacionesHub> _hubContext;

        public NotificacionService(
            AppDbContext context,
            UserManager<ApplicationUser> userManager,
            ILogger<NotificacionService> logger,
            IHubContext<NotificacionesHub> hubContext)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
            _hubContext = hubContext;
        }

        #region Crear Notificaciones

        public async Task<Notificacion> CrearNotificacionAsync(CrearNotificacionViewModel model)
        {
            var notificacion = new Notificacion
            {
                UsuarioDestino = model.UsuarioDestino,
                Tipo = model.Tipo,
                Prioridad = model.Prioridad,
                Titulo = model.Titulo,
                Mensaje = model.Mensaje,
                Url = model.Url,
                IconoCss = ObtenerIconoPorTipo(model.Tipo),
                FechaNotificacion = DateTime.UtcNow,
                Leida = false,
                EntidadOrigen = model.EntidadOrigen,
                EntidadOrigenId = model.EntidadOrigenId
            };

            _context.Notificaciones.Add(notificacion);
            await _context.SaveChangesAsync();

            await NotificarActualizacionAsync(notificacion.UsuarioDestino);

            _logger.LogInformation("Notificación creada para {Usuario}: {Titulo}", model.UsuarioDestino, model.Titulo);

            return notificacion;
        }

        public async Task CrearNotificacionParaUsuarioAsync(
            string usuario,
            TipoNotificacion tipo,
            string titulo,
            string mensaje,
            string? url = null,
            PrioridadNotificacion prioridad = PrioridadNotificacion.Media)
        {
            var model = new CrearNotificacionViewModel
            {
                UsuarioDestino = usuario,
                Tipo = tipo,
                Prioridad = prioridad,
                Titulo = titulo,
                Mensaje = mensaje,
                Url = url
            };

            await CrearNotificacionAsync(model);
        }

        public async Task CrearNotificacionParaRolAsync(
            string rol,
            TipoNotificacion tipo,
            string titulo,
            string mensaje,
            string? url = null,
            PrioridadNotificacion prioridad = PrioridadNotificacion.Media)
        {
            // Obtener todos los usuarios con ese rol
            var usuariosEnRol = await _userManager.GetUsersInRoleAsync(rol);

            foreach (var usuario in usuariosEnRol)
            {
                await CrearNotificacionParaUsuarioAsync(
                    usuario.UserName ?? usuario.Email ?? "",
                    tipo,
                    titulo,
                    mensaje,
                    url,
                    prioridad);
            }

            _logger.LogInformation("Notificaciones creadas para rol {Rol}: {Count} usuarios", rol, usuariosEnRol.Count);
        }

        #endregion

        #region Obtener Notificaciones

        public async Task<List<NotificacionViewModel>> ObtenerNotificacionesUsuarioAsync(
            string usuario,
            bool soloNoLeidas = false,
            int limite = 50)
        {
            var query = _context.Notificaciones
                .Where(n => n.UsuarioDestino == usuario && !n.IsDeleted);

            if (soloNoLeidas)
            {
                query = query.Where(n => !n.Leida);
            }

            var notificaciones = await query
                .OrderByDescending(n => n.FechaNotificacion)
                .Take(limite)
                .ToListAsync();

            return notificaciones.Select(n => new NotificacionViewModel
            {
                Id = n.Id,
                RowVersion = n.RowVersion,
                Tipo = n.Tipo,
                Prioridad = n.Prioridad,
                Titulo = n.Titulo,
                Mensaje = n.Mensaje,
                Url = n.Url,
                IconoCss = n.IconoCss,
                Leida = n.Leida,
                FechaNotificacion = n.FechaNotificacion,
                TiempoTranscurrido = CalcularTiempoTranscurrido(n.FechaNotificacion)
            }).ToList();
        }

        public async Task<int> ObtenerCantidadNoLeidasAsync(string usuario)
        {
            return await _context.Notificaciones
                .CountAsync(n => n.UsuarioDestino == usuario && !n.Leida && !n.IsDeleted);
        }

        public async Task<Notificacion?> ObtenerNotificacionPorIdAsync(int id)
        {
            return await _context.Notificaciones
                .FirstOrDefaultAsync(n => n.Id == id && !n.IsDeleted);
        }

        public async Task<ListaNotificacionesViewModel> ObtenerResumenNotificacionesAsync(string usuario)
        {
            var notificaciones = await ObtenerNotificacionesUsuarioAsync(usuario, false, 100);
            var noLeidas = await ObtenerCantidadNoLeidasAsync(usuario);

            return new ListaNotificacionesViewModel
            {
                Notificaciones = notificaciones,
                TotalNoLeidas = noLeidas,
                TotalNotificaciones = notificaciones.Count
            };
        }

        #endregion

        #region Marcar como Leída

        public async Task MarcarComoLeidaAsync(int notificacionId, string usuario, byte[]? rowVersion = null)
        {
            var notificacion = await _context.Notificaciones
                .FirstOrDefaultAsync(n => n.Id == notificacionId &&
                                         n.UsuarioDestino == usuario &&
                                         !n.IsDeleted);

            if (notificacion != null && !notificacion.Leida)
            {
                if (rowVersion is null || rowVersion.Length == 0)
                    throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá las notificaciones e intentá nuevamente.");

                _context.Entry(notificacion).Property(n => n.RowVersion).OriginalValue = rowVersion;

                notificacion.Leida = true;
                notificacion.FechaLeida = DateTime.UtcNow;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException("La notificación fue modificada por otro proceso. Recargá los datos e intentá nuevamente.");
                }

                await NotificarActualizacionAsync(usuario);
            }
        }

        public async Task MarcarTodasComoLeidasAsync(string usuario)
        {
            var now = DateTime.UtcNow;

            var updated = await _context.Notificaciones
                .Where(n => n.UsuarioDestino == usuario && !n.Leida && !n.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(n => n.Leida, true)
                    .SetProperty(n => n.FechaLeida, now)
                    .SetProperty(n => n.UpdatedAt, now)
                    .SetProperty(n => n.UpdatedBy, usuario));

            if (updated > 0)
            {
                _logger.LogInformation("Marcadas {Count} notificaciones como leídas para {Usuario}", updated, usuario);
                await NotificarActualizacionAsync(usuario);
            }
        }

        #endregion

        #region Eliminar

        public async Task EliminarNotificacionAsync(int id, string usuario, byte[]? rowVersion = null)
        {
            var notificacion = await _context.Notificaciones
                .FirstOrDefaultAsync(n => n.Id == id &&
                                         n.UsuarioDestino == usuario &&
                                         !n.IsDeleted);

            if (notificacion != null)
            {
                if (rowVersion is null || rowVersion.Length == 0)
                    throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá las notificaciones e intentá nuevamente.");

                _context.Entry(notificacion).Property(n => n.RowVersion).OriginalValue = rowVersion;

                notificacion.IsDeleted = true;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException("La notificación fue modificada por otro proceso. Recargá los datos e intentá nuevamente.");
                }

                await NotificarActualizacionAsync(usuario);
            }
        }

        public async Task LimpiarNotificacionesAntiguasAsync(int diasAntiguedad = 30)
        {
            var fechaLimite = DateTime.UtcNow.AddDays(-diasAntiguedad);

            var now = DateTime.UtcNow;
            var updated = await _context.Notificaciones
                .Where(n => n.FechaNotificacion < fechaLimite && n.Leida && !n.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(n => n.IsDeleted, true)
                    .SetProperty(n => n.UpdatedAt, now));

            if (updated > 0)
            {
                _logger.LogInformation("Limpiadas {Count} notificaciones antiguas", updated);
            }
        }

        #endregion

        #region Métodos Auxiliares

        private Task NotificarActualizacionAsync(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return Task.CompletedTask;
            }

            return _hubContext.Clients.Group(usuario).SendAsync("NotificacionesActualizadas");
        }

        private string ObtenerIconoPorTipo(TipoNotificacion tipo)
        {
            return tipo switch
            {
                TipoNotificacion.StockBajo => "bi-box-seam text-warning",
                TipoNotificacion.StockAgotado => "bi-box-seam text-danger",
                TipoNotificacion.CuotaProximaVencer => "bi-calendar-event text-warning",
                TipoNotificacion.CuotaVencida => "bi-calendar-x text-danger",
                TipoNotificacion.CreditoAprobado => "bi-check-circle text-success",
                TipoNotificacion.CreditoRechazado => "bi-x-circle text-danger",
                TipoNotificacion.PagoRecibido => "bi-cash text-success",
                TipoNotificacion.AutorizacionPendiente => "bi-shield-lock text-warning",
                TipoNotificacion.AutorizacionAprobada => "bi-shield-check text-success",
                TipoNotificacion.AutorizacionRechazada => "bi-shield-x text-danger",
                TipoNotificacion.VentaCompletada => "bi-cart-check text-success",
                TipoNotificacion.DevolucionCreada => "bi-arrow-return-left text-info",
                TipoNotificacion.DevolucionAprobada => "bi-check2-all text-success",
                TipoNotificacion.NotaCreditoGenerada => "bi-receipt text-info",
                TipoNotificacion.RMAPendiente => "bi-hourglass-split text-warning",
                TipoNotificacion.RMAAprobado => "bi-check-circle text-success",
                TipoNotificacion.RMARechazado => "bi-x-circle text-danger",
                TipoNotificacion.GarantiaProximaVencer => "bi-shield-exclamation text-warning",
                TipoNotificacion.CajaAbierta => "bi-unlock text-success",
                TipoNotificacion.CajaCerrada => "bi-lock text-info",
                TipoNotificacion.CierreConDiferencia => "bi-exclamation-triangle text-warning",
                TipoNotificacion.ChequeProximoVencer => "bi-cash-coin text-warning",
                TipoNotificacion.ChequeVencido => "bi-cash-coin text-danger",
                TipoNotificacion.SistemaError => "bi-bug text-danger",
                TipoNotificacion.SistemaMantenimiento => "bi-tools text-info",
                _ => "bi-bell text-primary"
            };
        }

        private string CalcularTiempoTranscurrido(DateTime fecha)
        {
            var diferencia = DateTime.UtcNow - fecha;

            if (diferencia.TotalMinutes < 1)
                return "Ahora";
            if (diferencia.TotalMinutes < 60)
                return $"Hace {(int)diferencia.TotalMinutes} min";
            if (diferencia.TotalHours < 24)
                return $"Hace {(int)diferencia.TotalHours}h";
            if (diferencia.TotalDays < 7)
                return $"Hace {(int)diferencia.TotalDays}d";
            if (diferencia.TotalDays < 30)
                return $"Hace {(int)(diferencia.TotalDays / 7)} sem";

            return fecha.ToString("dd/MM/yyyy");
        }

        #endregion
    }
}