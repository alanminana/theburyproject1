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
        private const decimal TasaMoraDiariaDefault = 0.1m;
        private const int DiasGraciaDefault = 3;

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
                .AsNoTracking()
                .FirstOrDefaultAsync(c => !c.IsDeleted);

            // Obtener alertas
            var alertas = await _context.Set<AlertaMora>()
                .AsNoTracking()
                .Where(a => !a.IsDeleted)
                .OrderBy(a => a.Orden)
                .ThenBy(a => a.DiasRelativoVencimiento)
                .ToListAsync();

            if (config == null)
            {
                // Crear configuración por defecto
                return new ConfiguracionMoraCompletaViewModel
                {
                    TasaMoraDiaria = TasaMoraDiariaDefault,
                    DiasGracia = DiasGraciaDefault,
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
                TasaMoraDiaria = config.TasaMoraBase ?? TasaMoraDiariaDefault,
                DiasGracia = config.DiasGracia ?? DiasGraciaDefault,
                ProcesoAutomaticoActivo = config.ProcesoAutomaticoActivo,
                HoraEjecucionDiaria = config.HoraEjecucionDiaria ?? new TimeSpan(8, 0, 0),
                Alertas = _mapper.Map<List<AlertaMoraViewModel>>(alertas)
            };

            return viewModel;
        }

        public async Task AplicarAlertasMoraAsync(IList<CuotaViewModel> cuotas)
        {
            if (cuotas.Count == 0) return;

            var configuracion = await GetConfiguracionAsync();
            var alertas = (configuracion?.Alertas ?? new List<AlertaMoraViewModel>())
                .Where(a => a.Activa)
                .OrderBy(a => a.DiasRelativoVencimiento)
                .ToList();

            foreach (var cuota in cuotas)
            {
                var diasRelativo = (DateTime.Today - cuota.FechaVencimiento).Days;
                var alertaAplicable = alertas
                    .Where(a => diasRelativo >= a.DiasRelativoVencimiento)
                    .OrderByDescending(a => a.DiasRelativoVencimiento)
                    .FirstOrDefault();

                cuota.ColorAlerta = alertaAplicable?.ColorAlerta ?? "#FF0000";
                cuota.DescripcionAlerta = alertaAplicable?.Descripcion ?? "Cuota vencida";
                cuota.NivelPrioridad = alertaAplicable?.NivelPrioridad ?? 5;
            }
        }

    }
}
