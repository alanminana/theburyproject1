using System.Security.Claims;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Helper para verificar permisos del usuario en las vistas
    /// Permite ocultar elementos de UI según los permisos asignados al rol del usuario
    /// </summary>
    public static class PermissionHelper
    {
        /// <summary>
        /// Verifica si el usuario tiene un permiso específico
        /// </summary>
        /// <param name="user">Usuario actual (ClaimsPrincipal)</param>
        /// <param name="modulo">Módulo requerido (ej: "ventas", "clientes")</param>
        /// <param name="accion">Acción requerida (ej: "create", "view", "delete")</param>
        /// <returns>True si tiene el permiso, False si no</returns>
        public static bool TienePermiso(this ClaimsPrincipal user, string modulo, string accion)
        {
            if (user == null || !user.Identity?.IsAuthenticated == true)
                return false;

            // SuperAdmin siempre tiene todos los permisos
            if (user.IsInRole("SuperAdmin"))
                return true;

            return PermissionAliasHelper.HasPermissionClaim(user, modulo, accion);
        }

        /// <summary>
        /// Verifica si el usuario tiene cualquiera de los permisos especificados
        /// </summary>
        public static bool TieneCualquierPermiso(this ClaimsPrincipal user, params (string modulo, string accion)[] permisos)
        {
            if (user == null || !user.Identity?.IsAuthenticated == true)
                return false;

            // SuperAdmin siempre tiene todos los permisos
            if (user.IsInRole("SuperAdmin"))
                return true;

            foreach (var (modulo, accion) in permisos)
            {
                if (user.TienePermiso(modulo, accion))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Verifica si el usuario tiene todos los permisos especificados
        /// </summary>
        public static bool TieneTodosLosPermisos(this ClaimsPrincipal user, params (string modulo, string accion)[] permisos)
        {
            if (user == null || !user.Identity?.IsAuthenticated == true)
                return false;

            // SuperAdmin siempre tiene todos los permisos
            if (user.IsInRole("SuperAdmin"))
                return true;

            foreach (var (modulo, accion) in permisos)
            {
                if (!user.TienePermiso(modulo, accion))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Obtiene todos los permisos del usuario actual
        /// </summary>
        public static IEnumerable<string> ObtenerPermisos(this ClaimsPrincipal user)
        {
            if (user == null || !user.Identity?.IsAuthenticated == true)
                return Enumerable.Empty<string>();

            return user.Claims
                .Where(c => c.Type == "Permission")
                .Select(c => c.Value)
                .Distinct()
                .OrderBy(p => p);
        }

        /// <summary>
        /// Verifica si el usuario es SuperAdmin
        /// </summary>
        public static bool EsSuperAdmin(this ClaimsPrincipal user)
        {
            return user?.IsInRole("SuperAdmin") == true;
        }
    }
}
