namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Gestiona el padrón de vendedores asignados a cada caja (control/visibilidad, sin enforcement).
    /// </summary>
    public interface ICajaVendedorService
    {
        /// <summary>Usuarios con rol Vendedor disponibles para asignar.</summary>
        Task<List<UsuarioSelectItem>> ObtenerVendedoresDisponiblesAsync();

        /// <summary>Ids de vendedores actualmente asignados a la caja.</summary>
        Task<List<string>> ObtenerVendedorIdsAsignadosAsync(int cajaId);

        /// <summary>Vendedores asignados a la caja (Id + nombre legible).</summary>
        Task<List<UsuarioSelectItem>> ObtenerVendedoresAsignadosAsync(int cajaId);

        /// <summary>
        /// Reemplaza el padrón de la caja por la selección indicada (alta de nuevos, baja de los quitados).
        /// Valida que cada usuario tenga rol Vendedor.
        /// </summary>
        Task AsignarVendedoresAsync(int cajaId, IEnumerable<string> vendedorUserIds, string usuario);
    }
}
