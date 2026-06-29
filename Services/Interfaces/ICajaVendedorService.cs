namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Gestiona el padrón de usuarios habilitados por caja (vendedores que venden en ella
    /// y cajeros que la operan). Soporta el enforcement de "cada usuario sobre su propia caja":
    /// quién puede operar la caja (cajero) y quién puede vender contra ella (vendedor).
    /// </summary>
    public interface ICajaVendedorService
    {
        /// <summary>Usuarios con rol Vendedor disponibles para asignar.</summary>
        Task<List<UsuarioSelectItem>> ObtenerVendedoresDisponiblesAsync();

        /// <summary>Usuarios con rol Cajero disponibles para asignar.</summary>
        Task<List<UsuarioSelectItem>> ObtenerCajerosDisponiblesAsync();

        /// <summary>Ids de usuarios actualmente asignados a la caja (vendedores y cajeros).</summary>
        Task<List<string>> ObtenerVendedorIdsAsignadosAsync(int cajaId);

        /// <summary>Usuarios asignados a la caja (Id + nombre legible).</summary>
        Task<List<UsuarioSelectItem>> ObtenerVendedoresAsignadosAsync(int cajaId);

        /// <summary>
        /// Reemplaza el padrón de la caja por la selección indicada (alta de nuevos, baja de los quitados).
        /// Valida que cada usuario tenga rol Vendedor o Cajero.
        /// </summary>
        Task AsignarVendedoresAsync(int cajaId, IEnumerable<string> vendedorUserIds, string usuario);

        /// <summary>
        /// Indica si el usuario está asignado al padrón de la caja.
        /// Base del enforcement: un usuario solo opera/vende en las cajas a las que pertenece.
        /// </summary>
        Task<bool> EsMiembroAsync(int cajaId, string userId);

        /// <summary>Ids de cajas a las que el usuario está asignado (para filtrar selects/listados).</summary>
        Task<List<int>> ObtenerCajaIdsDeUsuarioAsync(string userId);
    }
}
