using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Services.Interfaces;
using RoleConstants = TheBuryProject.Models.Constants.Roles;

namespace TheBuryProject.Services;

public class GuardiaPrivilegiosService : IGuardiaPrivilegiosService
{
    private const string MensajeSuperAdmin = "Solo un SuperAdmin puede gestionar el rol o los usuarios SuperAdmin.";
    private const string MensajeRolSistema = "El rol SuperAdmin es del sistema: no se puede eliminar, renombrar ni desactivar.";
    private const string MensajeUltimoSuperAdmin = "La operación dejaría al sistema sin ningún SuperAdmin utilizable (activo, sin bloqueo y con el rol activo). No se aplicó ningún cambio.";
    private const string MensajeSinAssignRoles = "No tenés permiso para asignar roles.";

    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISeguridadAuditoriaService _auditoria;
    private readonly ILogger<GuardiaPrivilegiosService> _logger;

    public GuardiaPrivilegiosService(
        AppDbContext context,
        ICurrentUserService currentUser,
        IHttpContextAccessor httpContextAccessor,
        ISeguridadAuditoriaService auditoria,
        ILogger<GuardiaPrivilegiosService> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _auditoria = auditoria;
        _logger = logger;
    }

    private bool ActorEsSuperAdmin => _currentUser.IsInRole(RoleConstants.SuperAdmin);

    public async Task<GuardiaResultado> ValidarCambioRolesAsync(
        IReadOnlyCollection<string> rolesAAgregar, IReadOnlyCollection<string> rolesAQuitar)
    {
        if (rolesAAgregar.Count == 0 && rolesAQuitar.Count == 0)
            return GuardiaResultado.Ok;

        if (ActorEsSuperAdmin)
            return GuardiaResultado.Ok;

        if (!_currentUser.HasPermission("usuarios", "assignroles"))
            return GuardiaResultado.Denegado(MensajeSinAssignRoles);

        if (rolesAAgregar.Concat(rolesAQuitar).Any(r =>
                string.Equals(r, RoleConstants.SuperAdmin, StringComparison.OrdinalIgnoreCase)))
            return GuardiaResultado.Denegado(MensajeSuperAdmin);

        foreach (var nombre in rolesAAgregar)
        {
            var roleId = await _context.Roles.AsNoTracking()
                .Where(r => r.NormalizedName == nombre.ToUpperInvariant())
                .Select(r => r.Id)
                .FirstOrDefaultAsync();

            // Un rol inexistente lo rechaza la validación de existencia del controller.
            if (roleId == null)
                continue;

            var excedentes = await PermisosQueElActorNoTieneAsync(await ClaimsDeRolAsync(roleId));
            if (excedentes.Count > 0)
            {
                return GuardiaResultado.Denegado(
                    $"No podés asignar el rol '{nombre}': otorga permisos que no tenés ({Resumir(excedentes)}).");
            }
        }

        return GuardiaResultado.Ok;
    }

    public async Task<GuardiaResultado> ValidarGestionUsuarioAsync(string targetUserId)
    {
        if (ActorEsSuperAdmin || string.IsNullOrWhiteSpace(targetUserId))
            return GuardiaResultado.Ok;

        var rolesDelTarget = await (
            from ur in _context.UserRoles.AsNoTracking()
            join r in _context.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ur.UserId == targetUserId
            select new { r.Id, r.NormalizedName }).ToListAsync();

        if (rolesDelTarget.Any(r => r.NormalizedName == RoleConstants.SuperAdmin.ToUpperInvariant()))
            return GuardiaResultado.Denegado(MensajeSuperAdmin);

        // Modificar email/contraseña de un usuario con más privilegios que el actor equivale a adoptar su cuenta.
        var claimsDelTarget = new List<string>();
        foreach (var rol in rolesDelTarget)
            claimsDelTarget.AddRange(await ClaimsDeRolAsync(rol.Id));

        var fueraDeAlcance = await PermisosQueElActorNoTieneAsync(claimsDelTarget);
        return fueraDeAlcance.Count == 0
            ? GuardiaResultado.Ok
            : GuardiaResultado.Denegado(
                $"No podés gestionar a un usuario con permisos que no tenés ({Resumir(fueraDeAlcance)}).");
    }

    public async Task<GuardiaResultado> ValidarActivacionRolAsync(string roleId)
    {
        if (ActorEsSuperAdmin)
            return GuardiaResultado.Ok;

        // Reactivar un rol vuelve a hacer efectivos sus permisos para todos sus miembros (el actor incluido).
        var fueraDeAlcance = await PermisosQueElActorNoTieneAsync(await ClaimsDeRolAsync(roleId));
        return fueraDeAlcance.Count == 0
            ? GuardiaResultado.Ok
            : GuardiaResultado.Denegado(
                $"No podés activar un rol que otorga permisos que no tenés ({Resumir(fueraDeAlcance)}).");
    }

    public async Task<GuardiaResultado> ValidarGestionRolAsync(string roleId)
    {
        if (ActorEsSuperAdmin)
            return GuardiaResultado.Ok;

        return await EsRolSuperAdminAsync(roleId)
            ? GuardiaResultado.Denegado(MensajeSuperAdmin)
            : GuardiaResultado.Ok;
    }

    public async Task<GuardiaResultado> ValidarCambioPermisosRolAsync(string roleId, IReadOnlyCollection<int> accionIds)
    {
        if (ActorEsSuperAdmin)
            return GuardiaResultado.Ok;

        if (await EsRolSuperAdminAsync(roleId))
            return GuardiaResultado.Denegado(MensajeSuperAdmin);

        // Mismo filtro que SyncPermisosForRoleAsync: solo las acciones activas se aplican.
        var deseados = await _context.AccionesModulo.AsNoTracking()
            .Where(a => accionIds.Contains(a.Id) && !a.IsDeleted && a.Activa)
            .Select(a => a.Modulo.Clave + "." + a.Clave)
            .ToListAsync();

        var actuales = await ClaimsDeRolAsync(roleId);

        var cambian = deseados.Select(PermissionAliasHelper.Canon).ToHashSet(StringComparer.Ordinal);
        cambian.SymmetricExceptWith(actuales.Select(PermissionAliasHelper.Canon));

        var fueraDeAlcance = await PermisosQueElActorNoTieneAsync(cambian);
        return fueraDeAlcance.Count == 0
            ? GuardiaResultado.Ok
            : GuardiaResultado.Denegado(
                $"No podés modificar permisos que no tenés ({Resumir(fueraDeAlcance)}).");
    }

    public async Task<GuardiaResultado> ValidarCopiaPermisosAsync(string sourceRoleId, string targetRoleId)
    {
        if (ActorEsSuperAdmin)
            return GuardiaResultado.Ok;

        if (await EsRolSuperAdminAsync(targetRoleId))
            return GuardiaResultado.Denegado(MensajeSuperAdmin);

        // La copia reemplaza el set del destino: cambian los permisos de la diferencia simétrica.
        var origen = (await ClaimsDeRolAsync(sourceRoleId)).Select(PermissionAliasHelper.Canon).ToHashSet(StringComparer.Ordinal);
        var destino = (await ClaimsDeRolAsync(targetRoleId)).Select(PermissionAliasHelper.Canon);
        origen.SymmetricExceptWith(destino);

        var fueraDeAlcance = await PermisosQueElActorNoTieneAsync(origen);
        return fueraDeAlcance.Count == 0
            ? GuardiaResultado.Ok
            : GuardiaResultado.Denegado(
                $"No podés copiar permisos que no tenés ({Resumir(fueraDeAlcance)}).");
    }

    public async Task<GuardiaResultado> ValidarDuplicadoRolAsync(string sourceRoleId)
    {
        if (ActorEsSuperAdmin)
            return GuardiaResultado.Ok;

        var fueraDeAlcance = await PermisosQueElActorNoTieneAsync(await ClaimsDeRolAsync(sourceRoleId));
        return fueraDeAlcance.Count == 0
            ? GuardiaResultado.Ok
            : GuardiaResultado.Denegado(
                $"No podés duplicar un rol que otorga permisos que no tenés ({Resumir(fueraDeAlcance)}).");
    }

    public async Task<GuardiaResultado> ValidarInmutabilidadRolSuperAdminAsync(
        string roleId, string? nuevoNombre, bool? nuevoActivo, bool eliminar)
    {
        if (!await EsRolSuperAdminAsync(roleId))
            return GuardiaResultado.Ok;

        var renombra = nuevoNombre != null &&
                       !string.Equals(nuevoNombre.Trim(), RoleConstants.SuperAdmin, StringComparison.OrdinalIgnoreCase);

        return eliminar || renombra || nuevoActivo == false
            ? GuardiaResultado.Denegado(MensajeRolSistema)
            : GuardiaResultado.Ok;
    }

    public async Task<GuardiaResultado> ValidarGestionUsuariosAsync(IEnumerable<string> targetUserIds)
    {
        foreach (var id in targetUserIds.Distinct())
        {
            var resultado = await ValidarGestionUsuarioAsync(id);
            if (!resultado.Permitido)
                return resultado;
        }

        return GuardiaResultado.Ok;
    }

    public async Task<GuardiaResultado> EjecutarConservandoSuperAdminAsync(
        Func<Task<bool>> operacion, IEnumerable<string>? usuariosAGestionar = null)
    {
        // Si el caller ya abrió una transacción, se une a ella (no se puede anidar).
        var propia = _context.Database.CurrentTransaction == null;
        var transaction = propia ? await _context.Database.BeginTransactionAsync() : null;

        try
        {
            // Serializa a los administradores: quien llega segundo espera a que el primero confirme y
            // recién entonces cuenta los SuperAdmin, con el cambio del primero ya visible. Sin este lock,
            // dos pedidos que desactivan SuperAdmin distintos verían "hay dos" y quedaría ninguno.
            if (_context.Database.IsSqlServer())
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "SELECT TOP (1) [Id] FROM [AspNetRoles] WITH (UPDLOCK, HOLDLOCK) WHERE [NormalizedName] = {0}",
                    RoleConstants.SuperAdmin.ToUpperInvariant());
            }

            // Se revalida con el lock tomado: si un SuperAdmin promovió al objetivo después del chequeo
            // previo del controller, el actor no-SuperAdmin ya no puede modificarlo.
            if (usuariosAGestionar != null)
            {
                var gestion = await ValidarGestionUsuariosAsync(usuariosAGestionar);
                if (!gestion.Permitido)
                {
                    if (propia) await transaction!.RollbackAsync();
                    return gestion;
                }
            }

            // Solo se defiende la invariante si hoy se cumple: una instalación sin SuperAdmin utilizable
            // (p. ej. recién sembrada) no debe quedar con toda la administración de usuarios trabada.
            var habiaSuperAdmin = await ExisteSuperAdminUtilizableAsync();

            if (!await operacion())
            {
                if (propia) await transaction!.RollbackAsync();
                return GuardiaResultado.Ok;
            }

            if (habiaSuperAdmin && !await ExisteSuperAdminUtilizableAsync())
            {
                if (propia) await transaction!.RollbackAsync();
                // La operación pudo dejar entidades modificadas en el tracker: se descartan.
                _context.ChangeTracker.Clear();
                return GuardiaResultado.Denegado(MensajeUltimoSuperAdmin);
            }

            if (propia) await transaction!.CommitAsync();
            return GuardiaResultado.Ok;
        }
        catch
        {
            if (propia) await transaction!.RollbackAsync();
            throw;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
        }
    }

    /// <summary>
    /// Utilizable = puede iniciar sesión y conserva el rol: usuario activo, sin bloqueo vigente,
    /// con el rol SuperAdmin asignado y ese rol activo. Es lo que exige el login
    /// (<c>ActiveUserSignInManager</c> + lockout de Identity) y el filtro de roles activos.
    /// </summary>
    private async Task<bool> ExisteSuperAdminUtilizableAsync()
    {
        var ahora = DateTimeOffset.UtcNow;
        var normalizado = RoleConstants.SuperAdmin.ToUpperInvariant();

        // El bloqueo se evalúa en memoria: la comparación de DateTimeOffset no se traduce en todos los
        // proveedores (SQLite de los tests) y los SuperAdmin son un puñado de filas.
        var candidatos = await (
            from u in _context.Users.AsNoTracking()
            join ur in _context.UserRoles.AsNoTracking() on u.Id equals ur.UserId
            join r in _context.Roles.AsNoTracking() on ur.RoleId equals r.Id
            join m in _context.RolMetadatas.AsNoTracking() on r.Id equals m.RoleId into metadatos
            from m in metadatos.DefaultIfEmpty()
            where r.NormalizedName == normalizado
                  && u.Activo
                  && (m == null || m.Activo)
            select new { u.LockoutEnabled, u.LockoutEnd }).ToListAsync();

        return candidatos.Any(c => !c.LockoutEnabled || c.LockoutEnd == null || c.LockoutEnd <= ahora);
    }

    public async Task RegistrarDenegacionAsync(string operacion, string entidad, string motivo)
    {
        _logger.LogWarning(
            "Escalada de privilegios bloqueada: {Operacion} sobre {Entidad} por {Usuario} ({UserId}) desde {Ip}. Motivo: {Motivo}",
            operacion, entidad, _currentUser.GetUsername(), _currentUser.GetUserId(), _currentUser.GetIpAddress(), motivo);

        await _auditoria.RegistrarEventoAsync("Seguridad", "Denegado", entidad, $"{operacion}: {motivo}");
    }

    private async Task<bool> EsRolSuperAdminAsync(string roleId)
    {
        if (string.IsNullOrWhiteSpace(roleId))
            return false;

        return await _context.Roles.AsNoTracking()
            .AnyAsync(r => r.Id == roleId && r.NormalizedName == RoleConstants.SuperAdmin.ToUpperInvariant());
    }

    private async Task<List<string>> ClaimsDeRolAsync(string roleId)
    {
        if (string.IsNullOrWhiteSpace(roleId))
            return [];

        var claims = await _context.RolPermisos.AsNoTracking()
            .Where(rp => rp.RoleId == roleId && !rp.IsDeleted)
            .Select(rp => rp.ClaimValue)
            .ToListAsync();

        // El rol SuperAdmin tiene todos los permisos por su nombre (bypass), aunque su set persistido esté incompleto.
        if (await EsRolSuperAdminAsync(roleId))
        {
            var todos = await _context.AccionesModulo.AsNoTracking()
                .Where(a => !a.IsDeleted && a.Activa)
                .Select(a => a.Modulo.Clave + "." + a.Clave)
                .ToListAsync();
            claims.AddRange(todos);
        }

        return claims;
    }

    /// <summary>
    /// Permisos de <paramref name="claims"/> que el actor no posee (respetando los alias de acción
    /// del sistema, p. ej. update/edit). El SuperAdmin ya fue descartado antes por los callers.
    /// </summary>
    private Task<List<string>> PermisosQueElActorNoTieneAsync(IEnumerable<string> claims)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var resultado = new List<string>();

        foreach (var claim in claims.Select(PermissionAliasHelper.Canon).Where(c => c.Length > 0).Distinct())
        {
            var punto = claim.IndexOf('.');
            var tiene = punto > 0 && user != null &&
                        PermissionAliasHelper.HasPermissionClaim(user, claim[..punto], claim[(punto + 1)..]);
            if (!tiene)
                resultado.Add(claim);
        }

        return Task.FromResult(resultado);
    }

    private static string Resumir(IReadOnlyList<string> permisos) =>
        permisos.Count <= 3
            ? string.Join(", ", permisos)
            : $"{string.Join(", ", permisos.Take(3))} y {permisos.Count - 3} más";
}
