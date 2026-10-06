using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class ConfiguracionActualizacionDatosClienteService : IConfiguracionActualizacionDatosClienteService
    {
        private readonly AppDbContext _context;

        public ConfiguracionActualizacionDatosClienteService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<ConfiguracionActualizacionDatosCliente> GetConfiguracionAsync()
        {
            var config = await _context.ConfiguracionesActualizacionDatosCliente
                .AsNoTracking()
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync(c => !c.IsDeleted);

            return config ?? ConfiguracionActualizacionDatosCliente.CrearDefault();
        }

        public async Task<ConfiguracionActualizacionDatosCliente> SaveConfiguracionAsync(bool activa, int diasRevision)
        {
            if (diasRevision < ConfiguracionActualizacionDatosCliente.DiasRevisionMinimo
                || diasRevision > ConfiguracionActualizacionDatosCliente.DiasRevisionMaximo)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(diasRevision),
                    $"Los días deben estar entre {ConfiguracionActualizacionDatosCliente.DiasRevisionMinimo} y {ConfiguracionActualizacionDatosCliente.DiasRevisionMaximo}.");
            }

            var config = await _context.ConfiguracionesActualizacionDatosCliente
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync(c => !c.IsDeleted);

            if (config == null)
            {
                config = new ConfiguracionActualizacionDatosCliente { CreatedAt = DateTime.UtcNow };
                _context.ConfiguracionesActualizacionDatosCliente.Add(config);
            }

            config.Activa = activa;
            config.DiasRevision = diasRevision;
            config.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return config;
        }

        public async Task<ActualizacionDatosClienteEstado> EvaluarClienteAsync(int clienteId)
        {
            var config = await GetConfiguracionAsync();
            if (!config.Activa)
                return ActualizacionDatosClienteEstado.NoRequiere;

            var cliente = await _context.Clientes
                .AsNoTracking()
                .Where(c => c.Id == clienteId && !c.IsDeleted && c.Activo)
                .Select(c => new { c.FechaUltimaActualizacionDatos, c.CreatedAt })
                .FirstOrDefaultAsync();

            if (cliente == null)
                return ActualizacionDatosClienteEstado.NoRequiere;

            var requiere = config.RequiereActualizacion(
                cliente.FechaUltimaActualizacionDatos, cliente.CreatedAt, DateTime.UtcNow, out var dias);

            return new ActualizacionDatosClienteEstado(requiere, dias, config.DiasRevision);
        }
    }
}
