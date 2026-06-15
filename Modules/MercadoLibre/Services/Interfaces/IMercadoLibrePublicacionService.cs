using TheBuryProject.Modules.MercadoLibre.ViewModels;

namespace TheBuryProject.Modules.MercadoLibre.Services.Interfaces
{
    public sealed record MercadoLibreBorradorCrearResultado(
        int? BorradorId,
        int? ListingId,
        bool Existia,
        string Mensaje);

    /// <summary>
    /// Publicación de productos ERP en Mercado Libre vía BORRADORES (Fase F).
    /// Reglas duras:
    /// - Un borrador nace SIEMPRE desde (y vinculado a) un Producto interno.
    /// - Publicar exige: borrador validado, PermitirPublicacionDesdeErp activo,
    ///   modo simulación desactivado y confirmación explícita del operador.
    /// - En simulación se loguea el payload exacto y NO se llama a Mercado Libre.
    /// - La publicación real crea el MercadoLibreListing ya vinculado al Producto.
    /// </summary>
    public interface IMercadoLibrePublicacionService
    {
        /// <summary>Crea un borrador prellenado desde el producto (título, precio canal, stock según origen).</summary>
        Task<MercadoLibreBorradorCrearResultado> CrearBorradorAsync(
            int productoId, string usuario, CancellationToken ct = default);

        /// <summary>Actualiza un borrador editable; cualquier edición vuelve el estado a Borrador.</summary>
        Task ActualizarBorradorAsync(MercadoLibreBorradorEditViewModel viewModel, string usuario, CancellationToken ct = default);

        /// <summary>
        /// Valida el borrador contra las reglas de ML y del ERP (título, precio,
        /// categoría, condición, stock vs disponible). Sin errores → Validado.
        /// </summary>
        Task<(bool Ok, List<string> Errores, List<string> Advertencias)> ValidarAsync(
            int borradorId, string usuario, CancellationToken ct = default);

        /// <summary>
        /// Publica el borrador. Con ModoSimulacion activo: loguea el payload y
        /// no llama a ML. Real: exige confirmacion explicita + POST /items + listing vinculado.
        /// </summary>
        Task<(bool Ok, string Mensaje)> PublicarAsync(
            int borradorId, bool confirmarReal, string usuario, CancellationToken ct = default);

        /// <summary>Descarta un borrador no publicado.</summary>
        Task DescartarAsync(int borradorId, string usuario, CancellationToken ct = default);

        Task<List<MercadoLibreBorradorListViewModel>> GetBorradoresAsync(CancellationToken ct = default);

        Task<MercadoLibreBorradorEditViewModel?> GetBorradorAsync(int borradorId, CancellationToken ct = default);
    }
}
