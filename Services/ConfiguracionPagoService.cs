using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class ConfiguracionPagoService : IConfiguracionPagoService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<ConfiguracionPagoService> _logger;

        public ConfiguracionPagoService(
            AppDbContext context,
            IMapper mapper,
            ILogger<ConfiguracionPagoService> logger)
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<List<ConfiguracionPagoViewModel>> GetAllAsync()
        {
            var configuraciones = await _context.ConfiguracionesPago
                .Include(c => c.ConfiguracionesTarjeta)
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.TipoPago)
                .ToListAsync();

            return _mapper.Map<List<ConfiguracionPagoViewModel>>(configuraciones);
        }

        public async Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id)
        {
            var configuracion = await _context.ConfiguracionesPago
                .Include(c => c.ConfiguracionesTarjeta)
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);

            return configuracion == null ? null : _mapper.Map<ConfiguracionPagoViewModel>(configuracion);
        }

        public async Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago)
        {
            var configuracion = await _context.ConfiguracionesPago
                .Include(c => c.ConfiguracionesTarjeta.Where(t => t.Activa && !t.IsDeleted))
                .FirstOrDefaultAsync(c => c.TipoPago == tipoPago && c.Activo && !c.IsDeleted);

            return configuracion == null ? null : _mapper.Map<ConfiguracionPagoViewModel>(configuracion);
        }

        public async Task<decimal> ObtenerTasaInteresMensualCreditoPersonalAsync()
        {
            var configuracion = await _context.ConfiguracionesPago
                .FirstOrDefaultAsync(c => c.TipoPago == TipoPago.CreditoPersonal && !c.IsDeleted);

            if (configuracion == null)
            {
                configuracion = new ConfiguracionPago
                {
                    TipoPago = TipoPago.CreditoPersonal,
                    Nombre = TipoPago.CreditoPersonal.ToString(),
                    Activo = true,
                    TasaInteresMensualCreditoPersonal = 0m
                };

                _context.ConfiguracionesPago.Add(configuracion);
                await _context.SaveChangesAsync();

                _logger.LogWarning(
                    "ConfiguracionPago CreditoPersonal no existia. Se creo con tasa 0.");
            }

            if (!configuracion.TasaInteresMensualCreditoPersonal.HasValue)
            {
                _logger.LogWarning(
                    "ConfiguracionPago CreditoPersonal sin tasa definida. Se usa 0.");
            }

            return configuracion.TasaInteresMensualCreditoPersonal ?? 0m;
        }

        public async Task<ConfiguracionPagoViewModel> CreateAsync(ConfiguracionPagoViewModel viewModel)
        {
            var configuracion = _mapper.Map<ConfiguracionPago>(viewModel);

            _context.ConfiguracionesPago.Add(configuracion);
            await _context.SaveChangesAsync();

            return _mapper.Map<ConfiguracionPagoViewModel>(configuracion);
        }

        public async Task<ConfiguracionPagoViewModel?> UpdateAsync(int id, ConfiguracionPagoViewModel viewModel)
        {
            var configuracion = await _context.ConfiguracionesPago
                .Include(c => c.ConfiguracionesTarjeta)
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);

            if (configuracion == null)
                return null;

            configuracion.Nombre = viewModel.Nombre;
            configuracion.Descripcion = viewModel.Descripcion;
            configuracion.Activo = viewModel.Activo;
            configuracion.PermiteDescuento = viewModel.PermiteDescuento;
            configuracion.PorcentajeDescuentoMaximo = viewModel.PorcentajeDescuentoMaximo;
            configuracion.TieneRecargo = viewModel.TieneRecargo;
            configuracion.PorcentajeRecargo = viewModel.PorcentajeRecargo;
            configuracion.TasaInteresMensualCreditoPersonal =
                viewModel.TipoPago == TipoPago.CreditoPersonal
                    ? viewModel.TasaInteresMensualCreditoPersonal
                    : null;
            configuracion.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return _mapper.Map<ConfiguracionPagoViewModel>(configuracion);
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var configuracion = await _context.ConfiguracionesPago
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
            if (configuracion == null)
                return false;

            configuracion.IsDeleted = true;
            configuracion.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<List<ConfiguracionTarjetaViewModel>> GetTarjetasActivasAsync()
        {
            var tarjetas = await _context.ConfiguracionesTarjeta
                .Where(t => t.Activa && !t.IsDeleted)
                .OrderBy(t => t.TipoTarjeta)
                .ThenBy(t => t.NombreTarjeta)
                .ToListAsync();

            return _mapper.Map<List<ConfiguracionTarjetaViewModel>>(tarjetas);
        }

        public async Task<ConfiguracionTarjetaViewModel?> GetTarjetaByIdAsync(int id)
        {
            var tarjeta = await _context.ConfiguracionesTarjeta
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

            return tarjeta == null ? null : _mapper.Map<ConfiguracionTarjetaViewModel>(tarjeta);
        }

        public async Task<bool> ValidarDescuento(TipoPago tipoPago, decimal descuento)
        {
            var config = await GetByTipoPagoAsync(tipoPago);

            if (config == null)
                return true; // Si no hay configuración, permitir

            if (!config.PermiteDescuento)
                return descuento == 0;

            if (config.PorcentajeDescuentoMaximo.HasValue)
                return descuento <= config.PorcentajeDescuentoMaximo.Value;

            return true;
        }

        public async Task<decimal> CalcularRecargo(TipoPago tipoPago, decimal monto)
        {
            var config = await GetByTipoPagoAsync(tipoPago);

            if (config == null || !config.TieneRecargo || !config.PorcentajeRecargo.HasValue)
                return 0;

            return monto * (config.PorcentajeRecargo.Value / 100);
        }
    }
}
