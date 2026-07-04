using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    public interface IGaranteService
    {
        /// <summary>
        /// Valida si un cliente puede actuar como garante de otro.
        /// No escribe en DB — pura validación de reglas de negocio.
        /// </summary>
        Task<(bool Ok, List<string> Errores)> ValidarGaranteAsync(int clienteId, int garanteClienteId);

        /// <summary>
        /// Asigna un garante validado a un cliente. Si ya tenía uno, lo reemplaza con baja suave.
        /// </summary>
        Task<(bool Ok, string? Error)> AsignarGaranteAsync(
            int clienteId,
            int garanteClienteId,
            string? observacion,
            string usuario);

        /// <summary>
        /// Remueve el garante actual del cliente (baja suave: FechaBaja + MotivoBaja).
        /// </summary>
        Task<(bool Ok, string? Error)> RemoverGaranteAsync(int clienteId, string motivo, string usuario);

        /// <summary>
        /// Devuelve el estado actual del garante del cliente, con validez y motivos.
        /// Null si el cliente no tiene garante asignado.
        /// </summary>
        Task<GaranteInfoViewModel?> ObtenerInfoGaranteAsync(int clienteId);

        /// <summary>
        /// Busca clientes que podrían actuar como garante (filtro por nombre o documento).
        /// Devuelve candidatos sin pre-validar — la validación ocurre al asignar.
        /// </summary>
        Task<List<GaranteCandidatoDto>> BuscarCandidatosAsync(string query, int clienteIdExcluir, int maxResultados = 10);
    }

    /// <summary>
    /// Candidato a garante. <see cref="PuntajeCliente"/> es el puntaje EFECTIVO
    /// (manual si hay override, si no el automático), que es el que gobierna la elegibilidad.
    /// </summary>
    public record GaranteCandidatoDto(
        int ClienteId,
        string NombreCompleto,
        string NumeroDocumento,
        int PuntajeCliente,
        bool Activo,
        int CantidadCompras,
        int GarantiasActivas,
        string FuentePuntaje);
}
