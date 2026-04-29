using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class ConfiguracionRentabilidadService : IConfiguracionRentabilidadService
    {
        private readonly AppDbContext _context;

        public ConfiguracionRentabilidadService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<ConfiguracionRentabilidad> GetConfiguracionAsync()
        {
            var config = await _context.ConfiguracionesRentabilidad
                .AsNoTracking()
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync(c => !c.IsDeleted);

            return config ?? new ConfiguracionRentabilidad();
        }

        public async Task<ConfiguracionRentabilidad> SaveConfiguracionAsync(decimal margenBajoMax, decimal margenAltoMin)
        {
            Validar(margenBajoMax, margenAltoMin);

            var config = await _context.ConfiguracionesRentabilidad
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync(c => !c.IsDeleted);

            if (config == null)
            {
                config = new ConfiguracionRentabilidad
                {
                    CreatedAt = DateTime.UtcNow
                };
                _context.ConfiguracionesRentabilidad.Add(config);
            }

            config.MargenBajoMax = margenBajoMax;
            config.MargenAltoMin = margenAltoMin;
            config.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return config;
        }

        private static void Validar(decimal margenBajoMax, decimal margenAltoMin)
        {
            if (margenBajoMax < 0 || margenBajoMax > 100)
                throw new ArgumentOutOfRangeException(nameof(margenBajoMax), "El margen bajo debe estar entre 0 y 100.");

            if (margenAltoMin < 0 || margenAltoMin > 100)
                throw new ArgumentOutOfRangeException(nameof(margenAltoMin), "El margen alto debe estar entre 0 y 100.");

            if (margenBajoMax >= margenAltoMin)
                throw new ArgumentException("El margen bajo debe ser menor al margen alto.");
        }
    }
}
