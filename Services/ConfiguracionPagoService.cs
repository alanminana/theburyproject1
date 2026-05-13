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
    public class ConfiguracionPagoService : IConfiguracionPagoService, IConfiguracionPagoGlobalAdminService
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

        public async Task<ConfiguracionPagoGlobalAdminViewModel> ObtenerAdminGlobalAsync()
        {
            var configuraciones = await _context.ConfiguracionesPago
                .AsNoTracking()
                .Include(c => c.ConfiguracionesTarjeta)
                .Include(c => c.PlanesPago)
                    .ThenInclude(p => p.ConfiguracionTarjeta)
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.TipoPago)
                .ThenBy(c => c.Nombre)
                .ToListAsync();

            return new ConfiguracionPagoGlobalAdminViewModel
            {
                Medios = configuraciones.Select(c => new MedioPagoGlobalAdminViewModel
                {
                    Id = c.Id,
                    TipoPago = c.TipoPago,
                    Nombre = c.Nombre,
                    Descripcion = c.Descripcion,
                    Activo = c.Activo,
                    PermiteDescuento = c.PermiteDescuento,
                    PorcentajeDescuentoMaximo = c.PorcentajeDescuentoMaximo,
                    TieneRecargo = c.TieneRecargo,
                    PorcentajeRecargo = c.PorcentajeRecargo,
                    Tarjetas = c.ConfiguracionesTarjeta
                        .Where(t => !t.IsDeleted)
                        .OrderBy(t => t.TipoTarjeta)
                        .ThenBy(t => t.NombreTarjeta)
                        .Select(t => new TarjetaGlobalAdminViewModel
                        {
                            Id = t.Id,
                            ConfiguracionPagoId = t.ConfiguracionPagoId,
                            Nombre = t.NombreTarjeta,
                            TipoTarjeta = t.TipoTarjeta,
                            Activa = t.Activa,
                            PermiteCuotas = t.PermiteCuotas,
                            CantidadMaximaCuotas = t.CantidadMaximaCuotas,
                            TipoCuota = t.TipoCuota,
                            TasaInteresesMensual = t.TasaInteresesMensual,
                            TieneRecargoDebito = t.TieneRecargoDebito,
                            PorcentajeRecargoDebito = t.PorcentajeRecargoDebito,
                            Observaciones = t.Observaciones
                        })
                        .ToList(),
                    Planes = c.PlanesPago
                        .Where(p => !p.IsDeleted)
                        .OrderBy(p => p.Orden)
                        .ThenBy(p => p.CantidadCuotas)
                        .ThenBy(p => p.Id)
                        .Select(p => new PlanPagoGlobalAdminViewModel
                        {
                            Id = p.Id,
                            ConfiguracionPagoId = p.ConfiguracionPagoId,
                            ConfiguracionTarjetaId = p.ConfiguracionTarjetaId,
                            NombreTarjeta = p.ConfiguracionTarjeta?.NombreTarjeta,
                            TipoPago = p.TipoPago,
                            CantidadCuotas = p.CantidadCuotas,
                            Activo = p.Activo,
                            TipoAjuste = p.TipoAjuste,
                            AjustePorcentaje = p.AjustePorcentaje,
                            Etiqueta = p.Etiqueta,
                            Orden = p.Orden,
                            Observaciones = p.Observaciones
                        })
                        .ToList()
                }).ToList()
            };
        }

        public async Task<PlanPagoGlobalAdminViewModel> CrearPlanGlobalAsync(PlanPagoGlobalCommandViewModel command)
        {
            ArgumentNullException.ThrowIfNull(command);

            var medio = await ObtenerMedioPagoParaPlanAsync(command.ConfiguracionPagoId);
            await ValidarPlanGlobalCommandAsync(command, medio, planId: null);

            var ahora = DateTime.UtcNow;
            var plan = new ConfiguracionPagoPlan
            {
                ConfiguracionPagoId = medio.Id,
                ConfiguracionTarjetaId = command.ConfiguracionTarjetaId,
                TipoPago = medio.TipoPago,
                CantidadCuotas = command.CantidadCuotas,
                Activo = command.Activo,
                TipoAjuste = command.TipoAjuste,
                AjustePorcentaje = command.AjustePorcentaje,
                Etiqueta = NormalizarTexto(command.Etiqueta),
                Orden = command.Orden,
                Observaciones = NormalizarTexto(command.Observaciones),
                CreatedAt = ahora,
                UpdatedAt = ahora
            };

            _context.ConfiguracionPagoPlanes.Add(plan);
            await _context.SaveChangesAsync();

            await _context.Entry(plan).Reference(p => p.ConfiguracionTarjeta).LoadAsync();
            return MapPlanGlobalAdmin(plan);
        }

        public async Task<PlanPagoGlobalAdminViewModel?> ActualizarPlanGlobalAsync(int id, PlanPagoGlobalCommandViewModel command)
        {
            ArgumentNullException.ThrowIfNull(command);

            var plan = await _context.ConfiguracionPagoPlanes
                .Include(p => p.ConfiguracionPago)
                .Include(p => p.ConfiguracionTarjeta)
                .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

            if (plan == null)
                return null;

            var medio = await ObtenerMedioPagoParaPlanAsync(command.ConfiguracionPagoId);
            await ValidarPlanGlobalCommandAsync(command, medio, plan.Id);

            plan.ConfiguracionPagoId = medio.Id;
            plan.ConfiguracionTarjetaId = command.ConfiguracionTarjetaId;
            plan.TipoPago = medio.TipoPago;
            plan.CantidadCuotas = command.CantidadCuotas;
            plan.Activo = command.Activo;
            plan.TipoAjuste = command.TipoAjuste;
            plan.AjustePorcentaje = command.AjustePorcentaje;
            plan.Etiqueta = NormalizarTexto(command.Etiqueta);
            plan.Orden = command.Orden;
            plan.Observaciones = NormalizarTexto(command.Observaciones);
            plan.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _context.Entry(plan).Reference(p => p.ConfiguracionTarjeta).LoadAsync();
            return MapPlanGlobalAdmin(plan);
        }

        public async Task<bool> CambiarEstadoPlanGlobalAsync(int id, bool activo)
        {
            var plan = await _context.ConfiguracionPagoPlanes
                .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

            if (plan == null)
                return false;

            if (activo)
            {
                await ValidarDuplicadoActivoPlanGlobalAsync(
                    plan.ConfiguracionPagoId,
                    plan.TipoPago,
                    plan.ConfiguracionTarjetaId,
                    plan.CantidadCuotas,
                    plan.Id);
            }

            plan.Activo = activo;
            plan.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        private async Task<ConfiguracionPago> ObtenerMedioPagoParaPlanAsync(int configuracionPagoId)
        {
            var medio = await _context.ConfiguracionesPago
                .FirstOrDefaultAsync(c => c.Id == configuracionPagoId && !c.IsDeleted);

            return medio ?? throw new InvalidOperationException("El medio de pago global no existe.");
        }

        private async Task ValidarPlanGlobalCommandAsync(
            PlanPagoGlobalCommandViewModel command,
            ConfiguracionPago medio,
            int? planId)
        {
            if (command.CantidadCuotas < 1)
                throw new InvalidOperationException("La cantidad de cuotas debe ser al menos 1.");

            if (command.AjustePorcentaje < -100.0000m || command.AjustePorcentaje > 999.9999m)
                throw new InvalidOperationException("El porcentaje debe estar entre -100.0000 y 999.9999.");

            if (command.TipoAjuste != TipoAjustePagoPlan.Porcentaje)
                throw new InvalidOperationException("El tipo de ajuste global indicado no esta soportado.");

            if (command.ConfiguracionTarjetaId.HasValue)
            {
                var tarjetaValida = await _context.ConfiguracionesTarjeta
                    .AnyAsync(t => t.Id == command.ConfiguracionTarjetaId.Value
                                   && t.ConfiguracionPagoId == medio.Id
                                   && !t.IsDeleted);

                if (!tarjetaValida)
                    throw new InvalidOperationException("La tarjeta indicada no pertenece al medio de pago global.");
            }

            var validacionAjuste = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
            {
                BaseVenta = 100m,
                PorcentajeAjuste = command.AjustePorcentaje,
                CantidadCuotas = command.CantidadCuotas,
                MedioActivo = true,
                PlanActivo = command.Activo
            });

            if (validacionAjuste.Estado == EstadoValidacionPagoGlobal.DescuentoMayorAlTotal)
                throw new InvalidOperationException(validacionAjuste.Mensaje);

            if (command.Activo)
            {
                await ValidarDuplicadoActivoPlanGlobalAsync(
                    medio.Id,
                    medio.TipoPago,
                    command.ConfiguracionTarjetaId,
                    command.CantidadCuotas,
                    planId);
            }
        }

        private async Task ValidarDuplicadoActivoPlanGlobalAsync(
            int configuracionPagoId,
            TipoPago tipoPago,
            int? configuracionTarjetaId,
            int cantidadCuotas,
            int? planId)
        {
            var existeDuplicado = await _context.ConfiguracionPagoPlanes
                .AnyAsync(p => p.ConfiguracionPagoId == configuracionPagoId
                               && p.TipoPago == tipoPago
                               && p.ConfiguracionTarjetaId == configuracionTarjetaId
                               && p.CantidadCuotas == cantidadCuotas
                               && p.Activo
                               && !p.IsDeleted
                               && (!planId.HasValue || p.Id != planId.Value));

            if (existeDuplicado)
                throw new InvalidOperationException("Ya existe un plan activo para el mismo medio, tarjeta y cantidad de cuotas.");
        }

        private static PlanPagoGlobalAdminViewModel MapPlanGlobalAdmin(ConfiguracionPagoPlan plan) =>
            new()
            {
                Id = plan.Id,
                ConfiguracionPagoId = plan.ConfiguracionPagoId,
                ConfiguracionTarjetaId = plan.ConfiguracionTarjetaId,
                NombreTarjeta = plan.ConfiguracionTarjeta?.NombreTarjeta,
                TipoPago = plan.TipoPago,
                CantidadCuotas = plan.CantidadCuotas,
                Activo = plan.Activo,
                TipoAjuste = plan.TipoAjuste,
                AjustePorcentaje = plan.AjustePorcentaje,
                Etiqueta = plan.Etiqueta,
                Orden = plan.Orden,
                Observaciones = plan.Observaciones
            };

        private static string? NormalizarTexto(string? value)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
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

        public async Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync()
        {
            var configuracion = await _context.ConfiguracionesPago
                .FirstOrDefaultAsync(c => c.TipoPago == TipoPago.CreditoPersonal && !c.IsDeleted);

            if (configuracion == null)
            {
                _logger.LogWarning(
                    "No existe ConfiguracionPago para CreditoPersonal. " +
                    "Configure la tasa en Administración → Tipos de Pago.");
                return null;
            }

            if (!configuracion.TasaInteresMensualCreditoPersonal.HasValue ||
                configuracion.TasaInteresMensualCreditoPersonal.Value == 0m)
            {
                _logger.LogWarning(
                    "ConfiguracionPago CreditoPersonal tiene tasa {Tasa}. " +
                    "Configure un valor mayor a 0 en Administración → Tipos de Pago.",
                    configuracion.TasaInteresMensualCreditoPersonal);
                return null;
            }

            return configuracion.TasaInteresMensualCreditoPersonal.Value;
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

            ValidarConfiguracionesTarjetaModal(configuraciones);

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

        private static void ValidarConfiguracionesTarjetaModal(IReadOnlyList<ConfiguracionPagoViewModel> configuraciones)
        {
            foreach (var configuracion in configuraciones)
            {
                if (configuracion.ConfiguracionesTarjeta == null)
                    continue;

                foreach (var tarjeta in configuracion.ConfiguracionesTarjeta)
                {
                    if (tarjeta.PermiteCuotas &&
                        tarjeta.TipoCuota == TipoCuotaTarjeta.ConInteres &&
                        tarjeta.TasaInteresesMensual == null)
                    {
                        throw new InvalidOperationException("La tasa de interés mensual es requerida para tarjetas con cuotas con interés.");
                    }
                }
            }
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

        public async Task<List<TarjetaActivaVentaResultado>> GetTarjetasActivasParaVentaAsync()
        {
            var tarjetas = await GetTarjetasActivasAsync();

            return tarjetas.Select(t => new TarjetaActivaVentaResultado
            {
                Id = t.Id,
                Nombre = t.NombreTarjeta,
                Tipo = t.TipoTarjeta,
                PermiteCuotas = t.PermiteCuotas,
                CantidadMaximaCuotas = t.CantidadMaximaCuotas,
                TipoCuota = t.TipoCuota,
                TasaInteres = t.TasaInteresesMensual,
                TieneRecargo = t.TieneRecargoDebito,
                PorcentajeRecargo = t.PorcentajeRecargoDebito
            }).ToList();
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

        public async Task<MaxCuotasSinInteresResultado?> ObtenerMaxCuotasSinInteresEfectivoAsync(
            int tarjetaId,
            IEnumerable<int> productoIds)
        {
            var tarjeta = await _context.ConfiguracionesTarjeta
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tarjetaId && t.Activa && !t.IsDeleted);

            if (tarjeta is null)
                return null;

            if (tarjeta.TipoCuota != TipoCuotaTarjeta.SinInteres)
                return null;

            if (!tarjeta.CantidadMaximaCuotas.HasValue)
                return null;

            var maxTarjeta = tarjeta.CantidadMaximaCuotas.Value;

            var ids = productoIds.ToList();
            int? limiteProductos = null;

            if (ids.Count > 0)
            {
                var restricciones = await _context.Productos
                    .AsNoTracking()
                    .Where(p => ids.Contains(p.Id) && !p.IsDeleted && p.MaxCuotasSinInteresPermitidas.HasValue)
                    .Select(p => p.MaxCuotasSinInteresPermitidas!.Value)
                    .ToListAsync();

                if (restricciones.Count > 0)
                    limiteProductos = restricciones.Min();
            }

            int efectivo;
            bool limitadoPorProducto;

            if (limiteProductos.HasValue)
            {
                efectivo = Math.Min(maxTarjeta, limiteProductos.Value);
                limitadoPorProducto = limiteProductos.Value < maxTarjeta;
            }
            else
            {
                efectivo = maxTarjeta;
                limitadoPorProducto = false;
            }

            return new MaxCuotasSinInteresResultado
            {
                TarjetaId = tarjetaId,
                MaxCuotas = Math.Max(1, efectivo),
                LimitadoPorProducto = limitadoPorProducto
            };
        }
    }
}
