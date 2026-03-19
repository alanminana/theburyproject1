namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Consulta la Central de Deudores del BCRA y cachea el resultado en la entidad Cliente.
    /// </summary>
    public interface ISituacionCrediticiaBcraService
    {
        /// <summary>
        /// Consulta (o devuelve del caché) la situación crediticia BCRA del cliente.
        /// Si el caché tiene menos de <paramref name="cacheDias"/> días, no vuelve a consultar.
        /// </summary>
        Task ConsultarYActualizarAsync(int clienteId, int cacheDias = 7);

        /// <summary>
        /// Fuerza una nueva consulta a la API BCRA ignorando el caché.
        /// </summary>
        Task ForzarActualizacionAsync(int clienteId);
    }
}
