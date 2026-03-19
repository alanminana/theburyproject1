using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class ConfiguracionMoraService : IConfiguracionMoraService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<ConfiguracionMoraService> _logger;

        public ConfiguracionMoraService(
            AppDbContext context,
            IMapper mapper,
            ILogger<ConfiguracionMoraService> logger)
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<ConfiguracionMoraCompletaViewModel?> GetConfiguracionAsync()
        {
            // Obtener configuración principal (solo debe haber una)
            var config = await _context.ConfiguracionesMora
                .FirstOrDefaultAsync(c => !c.IsDeleted);

            // Obtener alertas
            var alertas = await _context.Set<AlertaMora>()
                .Where(a => !a.IsDeleted)
                .OrderBy(a => a.Orden)
                .ThenBy(a => a.DiasRelativoVencimiento)
                .ToListAsync();

            if (config == null)
            {
                // Crear configuración por defecto
                return new ConfiguracionMoraCompletaViewModel
                {
                    TasaMoraDiaria = 0.1m,
                    DiasGracia = 3,
                    ProcesoAutomaticoActivo = true,
                    HoraEjecucionDiaria = new TimeSpan(8, 0, 0),
                    Alertas = new List<AlertaMoraViewModel>
                    {
                        new AlertaMoraViewModel
                        {
                            DiasRelativoVencimiento = 0,
                            ColorAlerta = "#FF0000",
                            Descripcion = "Cuota vencida",
                            NivelPrioridad = 5,
                            Activa = true,
                            Orden = 1
                        }
                    }
                };
            }

            var viewModel = new ConfiguracionMoraCompletaViewModel
            {
                Id = config.Id,
                TasaMoraDiaria = config.TasaMoraBase ?? 0.1m,
                DiasGracia = config.DiasGracia ?? 3,
                ProcesoAutomaticoActivo = config.ProcesoAutomaticoActivo,
                HoraEjecucionDiaria = config.HoraEjecucionDiaria ?? new TimeSpan(8, 0, 0),
                Alertas = _mapper.Map<List<AlertaMoraViewModel>>(alertas)
            };

            return viewModel;
        }

        public async Task<ConfiguracionMoraCompletaViewModel> SaveConfiguracionAsync(ConfiguracionMoraCompletaViewModel viewModel)
        {
            try
            {
                // Obtener o crear configuración principal
                var config = await _context.ConfiguracionesMora
                    .FirstOrDefaultAsync(c => !c.IsDeleted);

                if (config == null)
                {
                    config = new ConfiguracionMora();
                    _context.ConfiguracionesMora.Add(config);
                }

                // Actualizar configuración principal
                config.TasaMoraBase = viewModel.TasaMoraDiaria;
                config.DiasGracia = viewModel.DiasGracia;
                config.ProcesoAutomaticoActivo = viewModel.ProcesoAutomaticoActivo;
                config.HoraEjecucionDiaria = viewModel.HoraEjecucionDiaria;

                // Eliminar alertas existentes
                var alertasExistentes = await _context.Set<AlertaMora>()
                    .Where(a => !a.IsDeleted)
                    .ToListAsync();

                foreach (var alerta in alertasExistentes)
                {
                    alerta.IsDeleted = true;
                }

                // Agregar nuevas alertas
                var orden = 1;
                foreach (var alertaVm in viewModel.Alertas.Where(a => a.Activa))
                {
                    var alerta = new AlertaMora
                    {
                        DiasRelativoVencimiento = alertaVm.DiasRelativoVencimiento,
                        ColorAlerta = alertaVm.ColorAlerta,
                        Descripcion = alertaVm.Descripcion,
                        NivelPrioridad = alertaVm.NivelPrioridad,
                        Activa = true,
                        Orden = orden++
                    };
                    _context.Set<AlertaMora>().Add(alerta);
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Configuración de mora guardada exitosamente");

                return await GetConfiguracionAsync() ?? viewModel;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar configuración de mora");
                throw;
            }
        }

        public async Task<decimal> CalcularInterePunitorioDiarioAsync(decimal capital, int diasAtraso)
        {
            var config = await _context.ConfiguracionesMora
                .FirstOrDefaultAsync(c => !c.IsDeleted);

            if (config == null || !config.TasaMoraBase.HasValue || config.TasaMoraBase <= 0)
                return 0;

            var diasGracia = config.DiasGracia ?? 0;
            var diasCalculables = Math.Max(0, diasAtraso - diasGracia);

            if (diasCalculables <= 0)
                return 0;

            var tasaDiaria = config.TasaMoraBase.Value / 100m; // Convertir porcentaje a decimal
            return capital * tasaDiaria * diasCalculables;
        }

        public async Task<List<AlertaMoraViewModel>> GetAlertasActivasAsync()
        {
            var alertas = await _context.Set<AlertaMora>()
                .Where(a => a.Activa && !a.IsDeleted)
                .OrderBy(a => a.Orden)
                .ThenBy(a => a.DiasRelativoVencimiento)
                .ToListAsync();

            return _mapper.Map<List<AlertaMoraViewModel>>(alertas);
        }
    }
}
