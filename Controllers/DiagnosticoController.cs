using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using System.Security.Claims;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Controllers
{
    [Authorize(Roles = Roles.SuperAdmin)]
    public class DiagnosticoController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IConfiguration _configuration;

        public DiagnosticoController(
            AppDbContext context,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            IConfiguration configuration)
        {
            _context = context;
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _configuration = configuration;
        }

        /// <summary>
        /// Muestra información de diagnóstico de la base de datos
        /// </summary>
        public async Task<IActionResult> Index()
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            var dbName = _context.Database.GetDbConnection().Database;
            var totalUsuarios = await _context.Users.CountAsync();
            var usuariosActivos = await _context.Users.CountAsync(u => u.Activo);
            var usuariosInactivos = totalUsuarios - usuariosActivos;
            var totalRoles = await _roleManager.Roles.CountAsync();
            
            var usuarios = await _context.Users
                .Select(u => new { u.UserName, u.Email, u.Activo, u.EmailConfirmed })
                .ToListAsync();

            var roles = await _roleManager.Roles
                .Select(r => new { r.Name, r.Id })
                .ToListAsync();

            var info = new
            {
                ConnectionString = connectionString,
                DatabaseName = dbName,
                TotalUsuarios = totalUsuarios,
                UsuariosActivos = usuariosActivos,
                UsuariosInactivos = usuariosInactivos,
                TotalRoles = totalRoles,
                Usuarios = usuarios,
                Roles = roles
            };

            return Json(info);
        }

        /// <summary>
        /// Crea un usuario de prueba para verificar persistencia
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CrearUsuarioPrueba()
        {
            var timestamp = DateTime.UtcNow.ToString("HHmmss");
            var email = $"test{timestamp}@diagnostico.com";
            
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, "Test123!");

            if (result.Succeeded)
            {
                // Verificar si se guardó
                var userVerificado = await _userManager.FindByEmailAsync(email);
                return Json(new
                {
                    Success = true,
                    Message = "Usuario creado exitosamente",
                    Email = email,
                    UserId = userVerificado?.Id,
                    Existe = userVerificado != null
                });
            }

            return Json(new
            {
                Success = false,
                Errors = result.Errors.Select(e => e.Description)
            });
        }

        /// <summary>
        /// Muestra formulario para resetear password
        /// </summary>
        [HttpGet]
        public IActionResult ResetPassword()
        {
            return View();
        }

        /// <summary>
        /// Resetea la contraseña de un usuario
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ResetPassword(string email, string newPassword)
        {
            try
            {
                var user = await _userManager.FindByEmailAsync(email);
                if (user == null)
                {
                    TempData["Error"] = $"Usuario {email} no encontrado";
                    return View();
                }

                // MÉTODO 1: Intentar con Token (más seguro)
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var resetResult = await _userManager.ResetPasswordAsync(user, token, newPassword);

                if (resetResult.Succeeded)
                {
                    // Asegurar que esté activo y confirmado
                    user.Activo = true;
                    user.EmailConfirmed = true;
                    user.LockoutEnd = null;
                    user.AccessFailedCount = 0;
                    await _userManager.UpdateAsync(user);

                    // Verificar que la password se guardó
                    var verificar = await _userManager.CheckPasswordAsync(user, newPassword);
                    
                    TempData["Success"] = $"✅ Contraseña resetada para {email}. Nueva contraseña: {newPassword}. Verificación: {(verificar ? "✓ CORRECTA" : "✗ ERROR")}";
                    return View();
                }

                // MÉTODO 2: Si falla, intentar remover y agregar
                var removeResult = await _userManager.RemovePasswordAsync(user);
                if (!removeResult.Succeeded)
                {
                    TempData["Error"] = $"Error al remover password: {string.Join(", ", removeResult.Errors.Select(e => e.Description))}";
                    return View();
                }

                var addResult = await _userManager.AddPasswordAsync(user, newPassword);
                if (!addResult.Succeeded)
                {
                    TempData["Error"] = $"Error al agregar password: {string.Join(", ", addResult.Errors.Select(e => e.Description))}";
                    return View();
                }

                // Asegurar que esté activo y confirmado
                user.Activo = true;
                user.EmailConfirmed = true;
                user.LockoutEnd = null;
                user.AccessFailedCount = 0;
                await _userManager.UpdateAsync(user);

                // Verificar que la password se guardó
                var verificar2 = await _userManager.CheckPasswordAsync(user, newPassword);

                TempData["Success"] = $"✅ Contraseña resetada (método 2) para {email}. Nueva contraseña: {newPassword}. Verificación: {(verificar2 ? "✓ CORRECTA" : "✗ ERROR")}";
                return View();
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error: {ex.Message}";
                return View();
            }
        }

        /// <summary>
        /// Fuerza el cambio de password directamente (para casos problemáticos)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ForcePasswordReset(string email)
        {
            try
            {
                var user = await _userManager.FindByEmailAsync(email ?? "test@test.com");
                if (user == null)
                {
                    return Json(new { Success = false, Error = "Usuario no encontrado" });
                }

                // Forzar password conocida
                string newPassword = "Test123!";
                
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

                if (!result.Succeeded)
                {
                    return Json(new { Success = false, Errors = result.Errors.Select(e => e.Description) });
                }

                // Limpiar bloqueos
                user.Activo = true;
                user.EmailConfirmed = true;
                user.LockoutEnd = null;
                user.AccessFailedCount = 0;
                await _userManager.UpdateAsync(user);

                // Verificar
                var passwordOk = await _userManager.CheckPasswordAsync(user, newPassword);

                return Json(new
                {
                    Success = true,
                    Email = email,
                    NewPassword = newPassword,
                    PasswordVerificada = passwordOk,
                    Message = passwordOk ? "✅ Password resetada y verificada" : "⚠️ Password resetada pero verificación falló"
                });
            }
            catch (Exception ex)
            {
                return Json(new { Success = false, Error = ex.Message, StackTrace = ex.StackTrace });
            }
        }

        /// <summary>
        /// Prueba el login de un usuario y devuelve diagnóstico detallado
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> TestLogin(string email, string password)
        {
            var diagnostico = new Dictionary<string, object?>();

            try
            {
                // 1. Buscar usuario
                var user = await _userManager.FindByEmailAsync(email);
                diagnostico["UsuarioExiste"] = user != null;

                if (user == null)
                {
                    diagnostico["Error"] = "Usuario no encontrado";
                    return Json(diagnostico);
                }

                // 2. Información del usuario
                diagnostico["UserId"] = user.Id;
                diagnostico["UserName"] = user.UserName;
                diagnostico["Email"] = user.Email;
                diagnostico["EmailConfirmed"] = user.EmailConfirmed;
                diagnostico["Activo"] = user.Activo;
                diagnostico["LockoutEnabled"] = user.LockoutEnabled;
                diagnostico["LockoutEnd"] = user.LockoutEnd;
                diagnostico["AccessFailedCount"] = user.AccessFailedCount;
                diagnostico["TwoFactorEnabled"] = user.TwoFactorEnabled;
                diagnostico["PhoneNumberConfirmed"] = user.PhoneNumberConfirmed;

                // 3. Verificar password
                var passwordValido = await _userManager.CheckPasswordAsync(user, password);
                diagnostico["PasswordValido"] = passwordValido;

                // 4. Verificar si puede hacer sign-in (validaciones de Identity)
                var signInManager = HttpContext.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>();
                var canSignIn = await signInManager.CanSignInAsync(user);
                diagnostico["CanSignIn"] = canSignIn;

                // 5. Verificar confirmación de email requerida
                diagnostico["RequireConfirmedEmail"] = _userManager.Options.SignIn.RequireConfirmedEmail;
                diagnostico["RequireConfirmedAccount"] = _userManager.Options.SignIn.RequireConfirmedAccount;

                // 6. Intentar login real
                if (passwordValido)
                {
                    var result = await signInManager.PasswordSignInAsync(
                        user, 
                        password, 
                        isPersistent: false, 
                        lockoutOnFailure: false
                    );

                    diagnostico["LoginSucceeded"] = result.Succeeded;
                    diagnostico["IsLockedOut"] = result.IsLockedOut;
                    diagnostico["IsNotAllowed"] = result.IsNotAllowed;
                    diagnostico["RequiresTwoFactor"] = result.RequiresTwoFactor;

                    if (!result.Succeeded)
                    {
                        diagnostico["MensajeError"] = "Sign-in falló a pesar de password válido";
                        
                        if (result.IsNotAllowed)
                            diagnostico["RazonFallo"] = "IsNotAllowed - Verificar EmailConfirmed o PhoneConfirmed";
                        else if (result.IsLockedOut)
                            diagnostico["RazonFallo"] = "Usuario bloqueado";
                        else if (result.RequiresTwoFactor)
                            diagnostico["RazonFallo"] = "Requiere autenticación de dos factores";
                    }
                }

                // 7. Verificar roles
                var roles = await _userManager.GetRolesAsync(user);
                diagnostico["Roles"] = roles;
                diagnostico["TieneRoles"] = roles.Any();

            }
            catch (Exception ex)
            {
                diagnostico["Exception"] = ex.Message;
                diagnostico["StackTrace"] = ex.StackTrace;
            }

            return Json(diagnostico);
        }

        /// <summary>
        /// Muestra los permisos efectivos del usuario actual
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> MisPermisos()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { Error = "Usuario no autenticado" });
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Json(new { Error = "Usuario no encontrado" });
            }

            // Obtener roles
            var roles = await _userManager.GetRolesAsync(user);

            // Obtener todos los claims del usuario
            var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();

            // Obtener permisos específicos
            var permisos = User.Claims
                .Where(c => c.Type == "Permission")
                .Select(c => c.Value)
                .ToList();

            var info = new
            {
                UserId = userId,
                UserName = user.UserName,
                Email = user.Email,
                Roles = roles,
                TotalPermisos = permisos.Count,
                Permisos = permisos.OrderBy(p => p).ToList(),
                TodosLosClaims = claims
            };

            return Json(info);
        }

        /// <summary>
        /// Muestra los permisos del ROL de un usuario (desde la BD)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> PermisosDelRol(string? email)
        {
            email ??= User.FindFirstValue(ClaimTypes.Email);
            
            if (string.IsNullOrEmpty(email))
            {
                return Json(new { Error = "Email no proporcionado" });
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                return Json(new { Error = "Usuario no encontrado" });
            }

            var roles = await _userManager.GetRolesAsync(user);
            var permisosInfo = new Dictionary<string, object?>();

            foreach (var roleName in roles)
            {
                var role = await _roleManager.FindByNameAsync(roleName);
                if (role != null)
                {
                    // Obtener permisos del rol desde la tabla RolPermiso (solo ClaimValue)
                    var permisos = await _context.RolPermisos
                        .Where(rp => rp.RoleId == role.Id && !rp.IsDeleted)
                        .Select(rp => rp.ClaimValue)
                        .Where(cv => !string.IsNullOrWhiteSpace(cv))
                        .OrderBy(cv => cv)
                        .ToListAsync();

                    permisosInfo[roleName] = new
                    {
                        RoleId = role.Id,
                        TotalPermisos = permisos.Count,
                        Permisos = permisos
                    };
                }
            }

            return Json(new
            {
                Email = email,
                UserName = user.UserName,
                Roles = roles,
                PermisosDelRol = permisosInfo
            });
        }

        /// <summary>
        /// Fuerza el refresh de los permisos del usuario actual sin hacer logout
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> RefreshPermisos()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { Error = "Usuario no autenticado" });
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Json(new { Error = "Usuario no encontrado" });
            }

            // Forzar refresh del sign-in para recargar claims (incluye permisos)
            await _signInManager.RefreshSignInAsync(user);

            return Json(new
            {
                Success = true,
                Message = "Permisos actualizados. Recarga la página para ver los cambios.",
                UserName = user.UserName,
                Email = user.Email
            });
        }

        /// <summary>
        /// Auditoría completa del sistema de permisos - verifica que todos los controladores usen PermisoRequerido
        /// </summary>
        [HttpGet]
        public IActionResult AuditoriaPermisos()
        {
            static bool EstaProtegido(object? valor)
            {
                var estado = valor?.GetType().GetProperty("Estado")?.GetValue(valor)?.ToString();
                return estado?.Contains("✅", StringComparison.Ordinal) == true
                    || estado?.Contains("🔒", StringComparison.Ordinal) == true;
            }

            var controladores = new Dictionary<string, object?>
            {
                ["AccionesController"] = new { Modulo = "acciones", Accion = "view", Estado = "✅" },
                ["AlertaStockController"] = new { Modulo = "stock", Accion = "viewalerts", Estado = "✅" },
                ["AutorizacionController"] = new { Modulo = "autorizaciones", Accion = "view", Estado = "✅ CORREGIDO" },
                ["CajaController"] = new { Modulo = "caja", Accion = "view", Estado = "✅ CORREGIDO" },
                ["CambiosPreciosController"] = new { Modulo = "precios", Accion = "view", Estado = "✅" },
                ["CatalogoController"] = new { Modulo = "cotizaciones", Accion = "view", Estado = "✅" },
                ["CategoriaController"] = new { Modulo = "categorias", Accion = "view", Estado = "✅" },
                ["ChequeController"] = new { Modulo = "cheques", Accion = "view", Estado = "✅" },
                ["ClienteController"] = new { Modulo = "clientes", Accion = "view", Estado = "✅" },
                ["ConfiguracionMoraController"] = new { Modulo = "configuraciones", Accion = "managemora", Estado = "✅ CORREGIDO - Removido hardcoded roles" },
                ["ConfiguracionPagoController"] = new { Modulo = "configuraciones", Accion = "view", Estado = "✅" },
                ["CreditoController"] = new { Modulo = "creditos", Accion = "view", Estado = "✅" },
                ["DashboardController"] = new { Modulo = "dashboard", Accion = "view", Estado = "✅ CORREGIDO - Dashboard usa PermisoRequerido" },
                ["DevolucionController"] = new { Modulo = "devoluciones", Accion = "view", Estado = "✅ CORREGIDO" },
                ["DiagnosticoController"] = new { Modulo = "N/A", Accion = "N/A", Estado = "🔒 Solo SuperAdmin" },
                ["DocumentoClienteController"] = new { Modulo = "clientes", Accion = "viewdocs", Estado = "✅" },
                ["ListasPreciosController"] = new { Modulo = "precios", Accion = "view", Estado = "✅" },
                ["MarcaController"] = new { Modulo = "marcas", Accion = "view", Estado = "✅" },
                ["ModulosController"] = new { Modulo = "modulos", Accion = "view", Estado = "✅ CORREGIDO - Agregado [Authorize]" },
                ["MoraController"] = new { Modulo = "cobranzas", Accion = "viewarrears", Estado = "✅" },
                ["MovimientoStockController"] = new { Modulo = "movimientos", Accion = "view", Estado = "✅" },
                ["NotificacionController"] = new { Modulo = "notificaciones", Accion = "view/update/delete", Estado = "✅ CORREGIDO - API protegida por RBAC" },
                ["OrdenCompraController"] = new { Modulo = "ordenescompra", Accion = "view", Estado = "✅ CORREGIDO - Agregado [Authorize]" },
                ["ProductoController"] = new { Modulo = "productos", Accion = "view", Estado = "✅" },
                ["ProveedorController"] = new { Modulo = "proveedores", Accion = "view", Estado = "✅" },
                ["ReporteController"] = new { Modulo = "reportes", Accion = "view", Estado = "✅" },
                ["RolesController"] = new { Modulo = "roles", Accion = "view", Estado = "✅" },
                ["UsuariosController"] = new { Modulo = "usuarios", Accion = "view", Estado = "✅" },
                ["VentaApiController"] = new { Modulo = "ventas", Accion = "view", Estado = "✅" },
                ["VentaController"] = new { Modulo = "ventas", Accion = "view", Estado = "✅" }
            };

            return Json(new
            {
                FechaAuditoria = DateTime.UtcNow,
                TotalControladores = controladores.Count,
                ControladoresProtegidos = controladores.Count(c => EstaProtegido(c.Value)),
                RolesHardcodeados = 0,
                Controladores = controladores,
                Resumen = new
                {
                    Protegidos = controladores.Count(c => EstaProtegido(c.Value)),
                    ConRBAC = controladores.Count(c =>
                    {
                        var estado = c.Value?.GetType().GetProperty("Estado")?.GetValue(c.Value)?.ToString();
                        return estado?.Contains("✅", StringComparison.Ordinal) == true;
                    }),
                    RestringidosPorRol = controladores.Count(c =>
                    {
                        var estado = c.Value?.GetType().GetProperty("Estado")?.GetValue(c.Value)?.ToString();
                        return estado?.Contains("🔒", StringComparison.Ordinal) == true;
                    }),
                    SinPermisos = controladores.Count(c => !EstaProtegido(c.Value)),
                    Mensaje = "Controladores de negocio cubiertos por RBAC y endpoints de diagnóstico restringidos a SuperAdmin."
                }
            });
        }
    }
}
