using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
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
                .AsNoTracking()
                .Include(c => c.ConfiguracionesTarjeta)
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.TipoPago)
                .ToListAsync();

            return _mapper.Map<List<ConfiguracionPagoViewModel>>(configuraciones);
        }

        public async Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id)
        {
            var configuracion = await _context.ConfiguracionesPago
                .AsNoTracking()
                .Include(c => c.ConfiguracionesTarjeta)
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);

            return configuracion == null ? null : _mapper.Map<ConfiguracionPagoViewModel>(configuracion);
        }

        public async Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago)
        {
            var configuracion = await _context.ConfiguracionesPago
                .AsNoTracking()
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

        public async Task GuardarConfiguracionesModalAsync(IReadOnlyList<ConfiguracionPagoViewModel> configuraciones)
        {
            if (configuraciones.Count == 0) return;

            var ahora = DateTime.UtcNow;
            var idsExistentes = configuraciones
                .Where(c => c.Id > 0)
                .Select(c => c.Id)
                .ToList();

            // Carga batch de las entidades a actualizar
            var existentes = idsExistentes.Count > 0
                ? await _context.ConfiguracionesPago
                    .Include(c => c.ConfiguracionesTarjeta)
                    .Where(c => idsExistentes.Contains(c.Id) && !c.IsDeleted)
                    .ToListAsync()
                : new List<ConfiguracionPago>();

            var existentesMap = existentes.ToDictionary(c => c.Id);

            foreach (var vm in configuraciones)
            {
                if (vm.Id > 0 && existentesMap.TryGetValue(vm.Id, out var entidad))
                {
                    entidad.Nombre = vm.Nombre;
                    entidad.Descripcion = vm.Descripcion;
                    entidad.Activo = vm.Activo;
                    entidad.PermiteDescuento = vm.PermiteDescuento;
                    entidad.PorcentajeDescuentoMaximo = vm.PorcentajeDescuentoMaximo;
                    entidad.TieneRecargo = vm.TieneRecargo;
                    entidad.PorcentajeRecargo = vm.PorcentajeRecargo;
                    entidad.TasaInteresMensualCreditoPersonal =
                        vm.TipoPago == TipoPago.CreditoPersonal
                            ? vm.TasaInteresMensualCreditoPersonal
                            : null;
                    entidad.UpdatedAt = ahora;

                    ActualizarConfiguracionesTarjeta(entidad, vm.ConfiguracionesTarjeta, ahora);
                }
                else if (vm.Id == 0)
                {
                    _context.ConfiguracionesPago.Add(_mapper.Map<ConfiguracionPago>(vm));
                }
            }

            await _context.SaveChangesAsync();
        }

        private static void ActualizarConfiguracionesTarjeta(
            ConfiguracionPago entidad,
            IReadOnlyList<ConfiguracionTarjetaViewModel>? tarjetasVm,
            DateTime ahora)
        {
            if (tarjetasVm == null || tarjetasVm.Count == 0)
                return;

            var tarjetasMap = entidad.ConfiguracionesTarjeta
                .Where(t => !t.IsDeleted)
                .ToDictionary(t => t.Id);

            foreach (var tarjetaVm in tarjetasVm)
            {
                if (tarjetaVm.Id <= 0 || !tarjetasMap.TryGetValue(tarjetaVm.Id, out var tarjeta))
                    continue;

                tarjeta.NombreTarjeta = tarjetaVm.NombreTarjeta;
                tarjeta.TipoTarjeta = tarjetaVm.TipoTarjeta;
                tarjeta.Activa = tarjetaVm.Activa;
                tarjeta.PermiteCuotas = tarjetaVm.PermiteCuotas;
                tarjeta.CantidadMaximaCuotas = tarjetaVm.PermiteCuotas
                    ? tarjetaVm.CantidadMaximaCuotas
                    : null;
                tarjeta.TipoCuota = tarjetaVm.PermiteCuotas
                    ? tarjetaVm.TipoCuota
                    : null;
                tarjeta.TasaInteresesMensual =
                    tarjetaVm.PermiteCuotas && tarjetaVm.TipoCuota == TipoCuotaTarjeta.ConInteres
                        ? tarjetaVm.TasaInteresesMensual
                        : null;
                tarjeta.TieneRecargoDebito = tarjetaVm.TipoTarjeta == TipoTarjeta.Debito &&
                                             tarjetaVm.TieneRecargoDebito;
                tarjeta.PorcentajeRecargoDebito = tarjeta.TieneRecargoDebito
                    ? tarjetaVm.PorcentajeRecargoDebito
                    : null;
                tarjeta.Observaciones = tarjetaVm.Observaciones;
                tarjeta.UpdatedAt = ahora;
            }
        }

        public async Task<List<ConfiguracionTarjetaViewModel>> GetTarjetasActivasAsync()
        {
            var tarjetas = await _context.ConfiguracionesTarjeta
                .AsNoTracking()
                .Where(t => t.Activa && !t.IsDeleted)
                .OrderBy(t => t.TipoTarjeta)
                .ThenBy(t => t.NombreTarjeta)
                .ToListAsync();

            return _mapper.Map<List<ConfiguracionTarjetaViewModel>>(tarjetas);
        }

        public async Task<ConfiguracionTarjetaViewModel?> GetTarjetaByIdAsync(int id)
        {
            var tarjeta = await _context.ConfiguracionesTarjeta
                .AsNoTracking()
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

        public async Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoAsync()
        {
            var perfiles = await _context.PerfilesCredito
                .AsNoTracking()
                .Where(p => !p.IsDeleted)
                .OrderBy(p => p.Orden)
                .ThenBy(p => p.Nombre)
                .ToListAsync();

            return _mapper.Map<List<PerfilCreditoViewModel>>(perfiles);
        }

        public async Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoActivosAsync()
        {
            var perfiles = await _context.PerfilesCredito
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.Activo)
                .OrderBy(p => p.Orden)
                .ThenBy(p => p.Nombre)
                .ToListAsync();

            return _mapper.Map<List<PerfilCreditoViewModel>>(perfiles);
        }

        public async Task GuardarCreditoPersonalAsync(CreditoPersonalConfigViewModel config)
        {
            // Actualizar defaults globales en ConfiguracionPago (TipoPago = CreditoPersonal)
            var configCreditoPersonal = await _context.ConfiguracionesPago
                .FirstOrDefaultAsync(c => c.TipoPago == TipoPago.CreditoPersonal);

            if (configCreditoPersonal == null && config.DefaultsGlobales != null)
            {
                configCreditoPersonal = new ConfiguracionPago
                {
                    TipoPago = TipoPago.CreditoPersonal,
                    Nombre = TipoPago.CreditoPersonal.ToString(),
                    Activo = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.ConfiguracionesPago.Add(configCreditoPersonal);
            }

            if (configCreditoPersonal != null && config.DefaultsGlobales != null)
            {
                configCreditoPersonal.TasaInteresMensualCreditoPersonal = config.DefaultsGlobales.TasaMensual;
                configCreditoPersonal.GastosAdministrativosDefaultCreditoPersonal = config.DefaultsGlobales.GastosAdministrativos;
                configCreditoPersonal.MinCuotasDefaultCreditoPersonal = config.DefaultsGlobales.MinCuotas;
                configCreditoPersonal.MaxCuotasDefaultCreditoPersonal = config.DefaultsGlobales.MaxCuotas;
                configCreditoPersonal.UpdatedAt = DateTime.UtcNow;
            }

            // Guardar perfiles de crédito
            if (config.Perfiles != null)
            {
                // Batch: cargar todos los perfiles existentes en una sola query
                var idsPerfiles = config.Perfiles
                    .Where(p => p.Id > 0)
                    .Select(p => p.Id)
                    .ToList();

                var perfilesExistentes = idsPerfiles.Count > 0
                    ? await _context.PerfilesCredito
                        .Where(p => idsPerfiles.Contains(p.Id))
                        .ToDictionaryAsync(p => p.Id)
                    : new Dictionary<int, PerfilCredito>();

                var ahora = DateTime.UtcNow;

                foreach (var perfilViewModel in config.Perfiles)
                {
                    if (perfilViewModel.Id > 0)
                    {
                        if (perfilesExistentes.TryGetValue(perfilViewModel.Id, out var perfil))
                        {
                            _mapper.Map(perfilViewModel, perfil);
                            perfil.UpdatedAt = ahora;
                        }
                    }
                    else
                    {
                        var nuevoPerfil = _mapper.Map<PerfilCredito>(perfilViewModel);
                        nuevoPerfil.CreatedAt = ahora;
                        _context.PerfilesCredito.Add(nuevoPerfil);
                    }
                }
            }

            await _context.SaveChangesAsync();
        }

        public async Task<ParametrosCreditoCliente> ObtenerParametrosCreditoClienteAsync(int clienteId, decimal tasaGlobal)
        {
            // Valores globales por defecto
            const int CuotasMaximasGlobal = 24;
            const int CuotasMinimas = 1;

            var cliente = await _context.Clientes
                .AsNoTracking()
                .Include(c => c.PerfilCreditoPreferido)
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

            var perfil = cliente?.PerfilCreditoPreferido;

            var tieneConfigPersonalizada = cliente != null &&
                (cliente.TasaInteresMensualPersonalizada.HasValue ||
                 cliente.GastosAdministrativosPersonalizados.HasValue ||
                 cliente.CuotasMaximasPersonalizadas.HasValue);

            var fuente = tieneConfigPersonalizada
                ? FuenteConfiguracionCredito.PorCliente
                : FuenteConfiguracionCredito.Global;

            // Cadena de prioridad: personalizado > perfil preferido > global
            var tasaMensual = tieneConfigPersonalizada
                ? (cliente!.TasaInteresMensualPersonalizada ?? perfil?.TasaMensual ?? tasaGlobal)
                : tasaGlobal;

            var gastos = tieneConfigPersonalizada
                ? (cliente!.GastosAdministrativosPersonalizados ?? perfil?.GastosAdministrativos ?? 0m)
                : 0m;

            var cuotasMaximas = tieneConfigPersonalizada
                ? (cliente!.CuotasMaximasPersonalizadas ?? perfil?.MaxCuotas ?? CuotasMaximasGlobal)
                : CuotasMaximasGlobal;

            var cuotasMinimas = perfil?.MinCuotas ?? CuotasMinimas;

            return new ParametrosCreditoCliente
            {
                Fuente = fuente,
                TasaMensual = tasaMensual,
                GastosAdministrativos = gastos,
                CuotasMaximas = cuotasMaximas,
                CuotasMinimas = cuotasMinimas,
                MontoMinimo = cliente?.MontoMinimoPersonalizado,
                MontoMaximo = cliente?.MontoMaximoPersonalizado,
                PerfilPreferidoId = perfil?.Id,
                PerfilPreferidoNombre = perfil?.Nombre,
                TieneConfiguracionPersonalizada = tieneConfigPersonalizada,
                TieneTasaPersonalizada = cliente?.TasaInteresMensualPersonalizada.HasValue ?? false,
                TasaPersonalizada = cliente?.TasaInteresMensualPersonalizada,
                GastosPersonalizados = cliente?.GastosAdministrativosPersonalizados
            };
        }

        public async Task<(int Min, int Max, string Descripcion, string? PerfilNombre)> ResolverRangoCuotasAsync(
            MetodoCalculoCredito metodo,
            int? perfilId,
            int? clienteId)
        {
            PerfilCredito? perfil = null;
            if (perfilId.HasValue &&
                (metodo == MetodoCalculoCredito.UsarPerfil ||
                 metodo == MetodoCalculoCredito.AutomaticoPorCliente))
            {
                perfil = await _context.PerfilesCredito
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == perfilId.Value && !p.IsDeleted);
            }

            Cliente? cliente = null;
            if (metodo == MetodoCalculoCredito.UsarCliente && clienteId.HasValue)
            {
                cliente = await _context.Clientes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == clienteId.Value && !c.IsDeleted);
            }

            var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(metodo, perfil, cliente);
            return (min, max, desc, perfil?.Nombre);
        }
    }
}
