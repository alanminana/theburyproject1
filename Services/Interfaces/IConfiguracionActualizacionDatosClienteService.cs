using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces
{
    public interface IConfiguracionActualizacionDatosClienteService
    {
        Task<ConfiguracionActualizacionDatosCliente> GetConfiguracionAsync();

        Task<ConfiguracionActualizacionDatosCliente> SaveConfiguracionAsync(bool activa, int diasRevision);

        /// <summary>
        /// Evalúa si los datos de un cliente están vencidos según la configuración vigente.
        /// Devuelve <c>false</c> si el cliente no existe, está dado de baja o el aviso está apagado.
        /// </summary>
        Task<ActualizacionDatosClienteEstado> EvaluarClienteAsync(int clienteId);
    }

    public sealed record ActualizacionDatosClienteEstado(bool Requiere, int DiasTranscurridos, int DiasRevision)
    {
        public static ActualizacionDatosClienteEstado NoRequiere { get; } = new(false, 0, 0);
    }
}
