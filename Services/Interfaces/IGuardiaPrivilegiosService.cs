namespace TheBuryProject.Services.Interfaces;

/// <summary>
/// Resultado de una verificación de privilegios. <see cref="Motivo"/> es apto para mostrar al usuario
/// y no revela datos sensibles; el detalle técnico se registra en el log/auditoría.
/// </summary>
public readonly record struct GuardiaResultado(bool Permitido, string? Motivo = null)
{
    public static GuardiaResultado Ok { get; } = new(true);

    public static GuardiaResultado Denegado(string motivo) => new(false, motivo);
}

/// <summary>
/// Evita la escalada de privilegios desde la administración de usuarios y roles: quien administra
/// no puede otorgar (a otros, a un rol o a sí mismo) más de lo que tiene, y solo un SuperAdmin
/// administra el rol/los usuarios SuperAdmin. Evalúa siempre contra el usuario autenticado del request.
/// </summary>
public interface IGuardiaPrivilegiosService
{
    /// <summary>
    /// Valida un cambio de roles de un usuario (alta con roles, edición, asignación masiva). Cualquier
    /// cambio exige <c>usuarios.assignroles</c>; cada rol agregado no puede otorgar más privilegios que
    /// los del actor; el rol SuperAdmin solo lo agrega o quita un SuperAdmin.
    /// </summary>
    Task<GuardiaResultado> ValidarCambioRolesAsync(
        IReadOnlyCollection<string> rolesAAgregar, IReadOnlyCollection<string> rolesAQuitar);

    /// <summary>
    /// Valida que el actor pueda modificar a un usuario existente (datos de acceso: email, contraseña,
    /// confirmación). Un SuperAdmin solo lo administra otro SuperAdmin, y un usuario con permisos que el
    /// actor no tiene tampoco: evita la toma de cuenta como vía de escalada.
    /// </summary>
    Task<GuardiaResultado> ValidarGestionUsuarioAsync(string targetUserId);

    /// <summary>
    /// Valida reactivar un rol: sus permisos vuelven a ser efectivos para sus miembros, por lo que
    /// no pueden exceder los del actor.
    /// </summary>
    Task<GuardiaResultado> ValidarActivacionRolAsync(string roleId);

    /// <summary>
    /// Valida editar/eliminar/renombrar un rol: el rol SuperAdmin solo lo gestiona un SuperAdmin.
    /// </summary>
    Task<GuardiaResultado> ValidarGestionRolAsync(string roleId);

    /// <summary>
    /// Valida reemplazar los permisos de un rol por <paramref name="accionIds"/>: cada permiso que cambia
    /// (se agrega o se quita) debe estar dentro de los permisos del actor.
    /// </summary>
    Task<GuardiaResultado> ValidarCambioPermisosRolAsync(string roleId, IReadOnlyCollection<int> accionIds);

    /// <summary>
    /// Valida copiar los permisos de un rol a otro (reemplaza los del destino).
    /// </summary>
    Task<GuardiaResultado> ValidarCopiaPermisosAsync(string sourceRoleId, string targetRoleId);

    /// <summary>
    /// Valida duplicar un rol: el duplicado hereda todos los permisos del origen.
    /// </summary>
    Task<GuardiaResultado> ValidarDuplicadoRolAsync(string sourceRoleId);

    /// <summary>
    /// Nombres reservados (SuperAdmin, Administrador, Gerente) otorgan autoridad implícita por IsInRole:
    /// solo un SuperAdmin puede crear un rol con ese nombre o usarlo como destino de un renombrado/duplicado.
    /// </summary>
    Task<GuardiaResultado> ValidarNombreRolAsync(string nombre);

    /// <summary>
    /// Renombrar, eliminar o cambiar el estado activo de un rol reservado existente: solo SuperAdmin.
    /// </summary>
    Task<GuardiaResultado> ValidarIdentidadRolReservadoAsync(
        string roleId, string? nuevoNombre, bool? nuevoActivo, bool eliminar);

    /// <summary>
    /// El rol SuperAdmin es del sistema: nadie (ni un SuperAdmin) puede eliminarlo, renombrarlo ni
    /// desactivarlo, porque dejaría al ERP sin acceso administrativo. Cualquier otro rol se ignora.
    /// </summary>
    Task<GuardiaResultado> ValidarInmutabilidadRolSuperAdminAsync(
        string roleId, string? nuevoNombre, bool? nuevoActivo, bool eliminar);

    /// <summary>
    /// Valida que el actor pueda administrar a TODOS los usuarios de una operación masiva
    /// (<see cref="ValidarGestionUsuarioAsync"/> por cada uno). Es todo o nada: si uno se rechaza,
    /// no debe aplicarse la operación sobre ninguno.
    /// </summary>
    Task<GuardiaResultado> ValidarGestionUsuariosAsync(IEnumerable<string> targetUserIds);

    /// <summary>
    /// Ejecuta <paramref name="operacion"/> (que debe escribir con el <c>AppDbContext</c> del request) en
    /// una transacción que serializa toda operación capaz de quitar acceso a un SuperAdmin y, al
    /// terminar, exige que siga existiendo al menos un SuperAdmin utilizable (activo, no bloqueado,
    /// con el rol SuperAdmin activo). Si no queda ninguno, revierte todo y devuelve el rechazo.
    /// <paramref name="operacion"/> devuelve <c>false</c> para pedir el rollback por un error propio
    /// (el resultado es <see cref="GuardiaResultado.Ok"/>: el caller informa su error).
    /// </summary>
    /// <param name="usuariosAGestionar">
    /// Usuarios objetivo: se revalida <see cref="ValidarGestionUsuariosAsync"/> ya con el lock tomado,
    /// para cerrar la ventana entre el chequeo del controller y la escritura.
    /// </param>
    Task<GuardiaResultado> EjecutarConservandoSuperAdminAsync(
        Func<Task<bool>> operacion, IEnumerable<string>? usuariosAGestionar = null);

    /// <summary>
    /// Registra en log y auditoría de seguridad un intento rechazado.
    /// </summary>
    Task RegistrarDenegacionAsync(string operacion, string entidad, string motivo);
}
