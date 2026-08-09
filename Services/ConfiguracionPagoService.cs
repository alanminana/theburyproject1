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

        // Tope tecnico de cantidad de cuotas. NO es una fuente de disponibilidad: las cantidades
        // salen siempre de los planes activos. Solo evita caps abiertos cuando ni cliente ni perfil
        // fijan un maximo. Coincide con el rango de validacion de GuardarCuotasCreditoPersonalAsync.
        private const int MinCuotasTecnico = 1;
        private const int MaxCuotasTecnico = 120;

        private const string SinPlanesGlobalesMensaje =
            "No hay planes de Credito Personal globales activos, por lo que no puede financiarse con " +
            "este medio de pago. Configure al menos un plan activo en Administracion -> Credito Personal.";

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

        public async Task<IReadOnlyList<TarjetaGlobalAdminViewModel>> ListarTarjetasGlobalesAsync(int? configuracionPagoId = null)
        {
            var query = _context.ConfiguracionesTarjeta
                .AsNoTracking()
                .Where(t => !t.IsDeleted);

            if (configuracionPagoId.HasValue)
                query = query.Where(t => t.ConfiguracionPagoId == configuracionPagoId.Value);

            var tarjetas = await query
                .OrderBy(t => t.ConfiguracionPagoId)
                .ThenBy(t => t.TipoTarjeta)
                .ThenBy(t => t.NombreTarjeta)
                .ToListAsync();

            return tarjetas.Select(MapTarjetaGlobalAdmin).ToList();
        }

        public async Task<TarjetaGlobalAdminViewModel?> ObtenerTarjetaGlobalAsync(int id)
        {
            var tarjeta = await _context.ConfiguracionesTarjeta
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

            return tarjeta == null ? null : MapTarjetaGlobalAdmin(tarjeta);
        }

        public async Task<TarjetaGlobalAdminViewModel> CrearTarjetaGlobalAsync(TarjetaGlobalCommandViewModel command)
        {
            ArgumentNullException.ThrowIfNull(command);

            var medio = await ObtenerMedioPagoParaTarjetaAsync(command.ConfiguracionPagoId);

            var nombreNormalizado = NormalizarNombreRequerido(command.NombreTarjeta);
            var claveNombre = NormalizarClaveNombre(nombreNormalizado);

            var inactivas = await _context.ConfiguracionesTarjeta
                .Where(t => t.ConfiguracionPagoId == medio.Id
                            && t.TipoTarjeta == command.TipoTarjeta
                            && !t.Activa
                            && !t.IsDeleted)
                .ToListAsync();

            var existenteInactiva = inactivas.FirstOrDefault(t => NormalizarClaveNombre(t.NombreTarjeta) == claveNombre);

            if (existenteInactiva != null)
            {
                await ValidarTarjetaGlobalCommandAsync(command, medio, existenteInactiva.Id);
                existenteInactiva.TipoTarjeta = command.TipoTarjeta;
                existenteInactiva.Activa = command.Activa;
                existenteInactiva.Observaciones = NormalizarTexto(command.Observaciones);
                existenteInactiva.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return MapTarjetaGlobalAdmin(existenteInactiva);
            }

            await ValidarTarjetaGlobalCommandAsync(command, medio, tarjetaId: null);

            var ahora = DateTime.UtcNow;
            var tarjeta = new ConfiguracionTarjeta
            {
                ConfiguracionPagoId = medio.Id,
                NombreTarjeta = nombreNormalizado,
                TipoTarjeta = command.TipoTarjeta,
                Activa = command.Activa,
                Observaciones = NormalizarTexto(command.Observaciones),
                CreatedAt = ahora,
                UpdatedAt = ahora
            };

            _context.ConfiguracionesTarjeta.Add(tarjeta);
            await _context.SaveChangesAsync();

            return MapTarjetaGlobalAdmin(tarjeta);
        }

        public async Task<TarjetaGlobalAdminViewModel?> ActualizarTarjetaGlobalAsync(int id, TarjetaGlobalCommandViewModel command)
        {
            ArgumentNullException.ThrowIfNull(command);

            var tarjeta = await _context.ConfiguracionesTarjeta
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

            if (tarjeta == null)
                return null;

            var medio = await ObtenerMedioPagoParaTarjetaAsync(command.ConfiguracionPagoId);
            await ValidarTarjetaGlobalCommandAsync(command, medio, tarjeta.Id);

            tarjeta.ConfiguracionPagoId = medio.Id;
            tarjeta.NombreTarjeta = NormalizarNombreRequerido(command.NombreTarjeta);
            tarjeta.TipoTarjeta = command.TipoTarjeta;
            tarjeta.Activa = command.Activa;
            tarjeta.Observaciones = NormalizarTexto(command.Observaciones);
            tarjeta.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return MapTarjetaGlobalAdmin(tarjeta);
        }

        public async Task<bool> CambiarEstadoTarjetaGlobalAsync(int id, bool activa)
        {
            var tarjeta = await _context.ConfiguracionesTarjeta
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

            if (tarjeta == null)
                return false;

            if (activa)
            {
                var medio = await ObtenerMedioPagoParaTarjetaAsync(tarjeta.ConfiguracionPagoId);
                await ValidarTarjetaGlobalCommandAsync(
                    new TarjetaGlobalCommandViewModel
                    {
                        ConfiguracionPagoId = tarjeta.ConfiguracionPagoId,
                        NombreTarjeta = tarjeta.NombreTarjeta,
                        TipoTarjeta = tarjeta.TipoTarjeta,
                        Activa = true,
                        Observaciones = tarjeta.Observaciones
                    },
                    medio,
                    tarjeta.Id);
            }

            tarjeta.Activa = activa;
            tarjeta.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<PlanPagoGlobalAdminViewModel> CrearPlanGlobalAsync(PlanPagoGlobalCommandViewModel command)
        {
            ArgumentNullException.ThrowIfNull(command);

            var medio = await ObtenerMedioPagoParaPlanAsync(command.ConfiguracionPagoId);

            var existenteInactivo = await _context.ConfiguracionPagoPlanes
                .Include(p => p.ConfiguracionTarjeta)
                .FirstOrDefaultAsync(p => p.ConfiguracionPagoId == medio.Id
                                          && p.ConfiguracionTarjetaId == command.ConfiguracionTarjetaId
                                          && p.CantidadCuotas == command.CantidadCuotas
                                          && !p.Activo
                                          && !p.IsDeleted);

            if (existenteInactivo != null)
            {
                await ValidarPlanGlobalCommandAsync(command, medio, existenteInactivo.Id);
                if (!command.Activo)
                    await ValidarDuplicadoActivoPlanGlobalAsync(
                        medio.Id, medio.TipoPago, command.ConfiguracionTarjetaId,
                        command.CantidadCuotas, existenteInactivo.Id);

                existenteInactivo.Activo = true;
                existenteInactivo.AjustePorcentaje = command.AjustePorcentaje;
                existenteInactivo.TipoAjuste = command.TipoAjuste;
                existenteInactivo.Etiqueta = NormalizarTexto(command.Etiqueta);
                existenteInactivo.Orden = command.Orden;
                existenteInactivo.Observaciones = NormalizarTexto(command.Observaciones);
                existenteInactivo.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return MapPlanGlobalAdmin(existenteInactivo);
            }

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

        public async Task<bool> EliminarTarjetaGlobalAsync(int id)
        {
            var tarjeta = await _context.ConfiguracionesTarjeta
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

            if (tarjeta == null)
                return false;

            var ahora = DateTime.UtcNow;

            var planes = await _context.ConfiguracionPagoPlanes
                .Where(p => p.ConfiguracionTarjetaId == id && !p.IsDeleted)
                .ToListAsync();

            foreach (var plan in planes)
            {
                plan.IsDeleted = true;
                plan.Activo = false;
                plan.UpdatedAt = ahora;
            }

            tarjeta.IsDeleted = true;
            tarjeta.Activa = false;
            tarjeta.UpdatedAt = ahora;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> EliminarMedioPagoAsync(int id)
        {
            var medio = await _context.ConfiguracionesPago
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);

            if (medio == null)
                return false;

            var ahora = DateTime.UtcNow;

            var planes = await _context.ConfiguracionPagoPlanes
                .Where(p => p.ConfiguracionPagoId == id)
                .ToListAsync();

            foreach (var plan in planes)
            {
                plan.IsDeleted = true;
                plan.Activo = false;
                plan.UpdatedAt = ahora;
            }

            var tarjetas = await _context.ConfiguracionesTarjeta
                .Where(t => t.ConfiguracionPagoId == id)
                .ToListAsync();

            foreach (var tarjeta in tarjetas)
            {
                tarjeta.IsDeleted = true;
                tarjeta.Activa = false;
                tarjeta.UpdatedAt = ahora;
            }

            medio.IsDeleted = true;
            medio.Activo = false;
            medio.UpdatedAt = ahora;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> EditarMedioPagoAsync(int id, MedioPagoGlobalEditViewModel command)
        {
            var medio = await _context.ConfiguracionesPago
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);

            if (medio == null)
                return false;

            medio.Nombre = command.Nombre.Trim();
            medio.Descripcion = string.IsNullOrWhiteSpace(command.Descripcion) ? null : command.Descripcion.Trim();
            medio.Activo = command.Activo;
            medio.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> CambiarEstadoPlanGlobalAsync(int id, bool activo)
        {
            var plan = await _context.ConfiguracionPagoPlanes
                .Include(p => p.ConfiguracionPago)
                .Include(p => p.ConfiguracionTarjeta)
                .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

            if (plan == null)
                return false;

            if (activo)
            {
                ValidarTipoPagoPlanSoportado(plan.TipoPago);
                ValidarCoherenciaTarjetaMedio(plan.ConfiguracionPago, plan.ConfiguracionTarjeta);

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

            if (medio == null)
                throw new InvalidOperationException("El medio de pago global no existe.");

            ValidarTipoPagoPlanSoportado(medio.TipoPago);
            return medio;
        }

        private async Task<ConfiguracionPago> ObtenerMedioPagoParaTarjetaAsync(int configuracionPagoId)
        {
            var medio = await _context.ConfiguracionesPago
                .FirstOrDefaultAsync(c => c.Id == configuracionPagoId && !c.IsDeleted);

            if (medio == null)
                throw new InvalidOperationException("El medio de pago global no existe.");

            ValidarTipoPagoTarjetaNoPermitidoParaConfiguracionNueva(medio.TipoPago);
            return medio;
        }

        private async Task ValidarTarjetaGlobalCommandAsync(
            TarjetaGlobalCommandViewModel command,
            ConfiguracionPago medio,
            int? tarjetaId)
        {
            var nombre = NormalizarNombreRequerido(command.NombreTarjeta);
            if (!Enum.IsDefined(typeof(TipoTarjeta), command.TipoTarjeta))
                throw new InvalidOperationException("El tipo de tarjeta indicado no es valido.");

            if (command.TipoTarjeta == TipoTarjeta.Credito && medio.TipoPago != TipoPago.TarjetaCredito)
                throw new InvalidOperationException("Las tarjetas de credito deben pertenecer al medio Tarjeta Credito.");

            if (command.TipoTarjeta == TipoTarjeta.Debito && medio.TipoPago != TipoPago.TarjetaDebito)
                throw new InvalidOperationException("Las tarjetas de debito deben pertenecer al medio Tarjeta Debito.");

            if (command.Activa)
                await ValidarDuplicadoActivoTarjetaGlobalAsync(medio.Id, command.TipoTarjeta, nombre, tarjetaId);
        }

        private async Task ValidarDuplicadoActivoTarjetaGlobalAsync(
            int configuracionPagoId,
            TipoTarjeta tipoTarjeta,
            string nombreTarjeta,
            int? tarjetaId)
        {
            var nombreNormalizado = NormalizarClaveNombre(nombreTarjeta);
            var candidatas = await _context.ConfiguracionesTarjeta
                .AsNoTracking()
                .Where(t => t.ConfiguracionPagoId == configuracionPagoId
                            && t.TipoTarjeta == tipoTarjeta
                            && t.Activa
                            && !t.IsDeleted
                            && (!tarjetaId.HasValue || t.Id != tarjetaId.Value))
                .Select(t => t.NombreTarjeta)
                .ToListAsync();

            if (candidatas.Any(n => NormalizarClaveNombre(n) == nombreNormalizado))
                throw new InvalidOperationException("Ya existe una tarjeta activa con el mismo nombre y tipo.");
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
                var tarjeta = await _context.ConfiguracionesTarjeta
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == command.ConfiguracionTarjetaId.Value
                                              && t.ConfiguracionPagoId == medio.Id
                                              && !t.IsDeleted);

                if (tarjeta == null)
                    throw new InvalidOperationException("La tarjeta indicada no pertenece al medio de pago global.");

                ValidarCoherenciaTarjetaMedio(medio, tarjeta);
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

        private static TarjetaGlobalAdminViewModel MapTarjetaGlobalAdmin(ConfiguracionTarjeta tarjeta) =>
            new()
            {
                Id = tarjeta.Id,
                ConfiguracionPagoId = tarjeta.ConfiguracionPagoId,
                Nombre = tarjeta.NombreTarjeta,
                TipoTarjeta = tarjeta.TipoTarjeta,
                Activa = tarjeta.Activa,
                PermiteCuotas = tarjeta.PermiteCuotas,
                CantidadMaximaCuotas = tarjeta.CantidadMaximaCuotas,
                TipoCuota = tarjeta.TipoCuota,
                TasaInteresesMensual = tarjeta.TasaInteresesMensual,
                TieneRecargoDebito = tarjeta.TieneRecargoDebito,
                PorcentajeRecargoDebito = tarjeta.PorcentajeRecargoDebito,
                Observaciones = tarjeta.Observaciones
            };

        private static string NormalizarNombreRequerido(string? value)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                throw new InvalidOperationException("El nombre de la tarjeta es requerido.");

            return trimmed;
        }

        private static string NormalizarClaveNombre(string value) =>
            NormalizarNombreRequerido(value).ToUpperInvariant();

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

        /// <summary>
        /// Fuente canónica del porcentaje de recargo TOTAL único global de Crédito Personal
        /// (no es una tasa mensual ni compuesta: ver <see cref="ConfiguracionPago.TasaInteresMensualCreditoPersonal"/>).
        /// LEGADO (ML4) — SIN autoridad financiera sobre <see cref="ConfiguracionCreditoPersonalCuota"/>:
        /// un plan con <c>TasaMensual</c> propia <c>null</c> es configuración inválida y nunca hereda
        /// este valor (dejó de ser fallback desde ML2.1/ML2). Solo lo consumen los resolutores de
        /// venta como tasa única cuando no existe ninguna tabla de planes en absoluto (legado, solo
        /// dobles de test). Devuelve <c>null</c> únicamente cuando no existe configuración persistida
        /// o cuando el valor nunca fue definido — NUNCA cuando el valor configurado es exactamente 0:
        /// un recargo de 0 % es válido y debe distinguirse de "no configurado".
        /// </summary>
        public async Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync()
        {
            var configuracion = await _context.ConfiguracionesPago
                .FirstOrDefaultAsync(c => c.TipoPago == TipoPago.CreditoPersonal && !c.IsDeleted);

            if (configuracion == null)
            {
                _logger.LogWarning(
                    "No existe ConfiguracionPago para CreditoPersonal. " +
                    "Configure el recargo en Administración → Tipos de Pago.");
                return null;
            }

            if (!configuracion.TasaInteresMensualCreditoPersonal.HasValue)
            {
                _logger.LogWarning(
                    "ConfiguracionPago CreditoPersonal no tiene un recargo definido. " +
                    "Configure un valor (0 o mayor) en Administración → Tipos de Pago.");
                return null;
            }

            // 0 % es un recargo valido y explicito: no se reinterpreta como "no configurado".
            return configuracion.TasaInteresMensualCreditoPersonal.Value;
        }

        public async Task<ConfiguracionPagoViewModel> CreateAsync(ConfiguracionPagoViewModel viewModel)
        {
            ValidarTipoPagoTarjetaNoPermitidoParaConfiguracionNueva(viewModel.TipoPago);

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

        private static void ValidarTipoPagoTarjetaNoPermitidoParaConfiguracionNueva(TipoPago tipoPago)
        {
            if (tipoPago == TipoPago.Tarjeta)
            {
                throw new InvalidOperationException(
                    "TipoPago.Tarjeta es historico y ambiguo. Configure Tarjeta Credito o Tarjeta Debito.");
            }
        }

        private static void ValidarTipoPagoPlanSoportado(TipoPago tipoPago)
        {
            ValidarTipoPagoTarjetaNoPermitidoParaConfiguracionNueva(tipoPago);

            if (tipoPago == TipoPago.CreditoPersonal)
            {
                throw new InvalidOperationException(
                    "Credito Personal no usa ConfiguracionPagoPlan en esta fase. Configure sus perfiles y defaults desde Credito Personal.");
            }
        }

        private static void ValidarCoherenciaTarjetaMedio(
            ConfiguracionPago medio,
            ConfiguracionTarjeta? tarjeta)
        {
            if (tarjeta == null)
                return;

            if (tarjeta.TipoTarjeta == TipoTarjeta.Credito && medio.TipoPago != TipoPago.TarjetaCredito)
                throw new InvalidOperationException("Las tarjetas de credito deben pertenecer al medio Tarjeta Credito.");

            if (tarjeta.TipoTarjeta == TipoTarjeta.Debito && medio.TipoPago != TipoPago.TarjetaDebito)
                throw new InvalidOperationException("Las tarjetas de debito deben pertenecer al medio Tarjeta Debito.");
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

        public async Task<decimal> ObtenerPorcentajeAjusteUnPagoAsync(TipoPago tipoPago)
        {
            // Regla vigente del medio: plan general de 1 cuota (ajuste % positivo = recargo,
            // negativo = descuento). Fallback: recargo global legacy del medio.
            var ajustePlan = await _context.ConfiguracionPagoPlanes
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.Activo &&
                            p.TipoPago == tipoPago &&
                            p.ConfiguracionTarjetaId == null &&
                            p.CantidadCuotas == 1)
                .OrderBy(p => p.Orden)
                .Select(p => (decimal?)p.AjustePorcentaje)
                .FirstOrDefaultAsync();

            if (ajustePlan.HasValue)
                return ajustePlan.Value;

            var config = await GetByTipoPagoAsync(tipoPago);
            if (config?.TieneRecargo == true && config.PorcentajeRecargo.HasValue)
                return config.PorcentajeRecargo.Value;

            return 0m;
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
                // Min/MaxCuotasDefaultCreditoPersonal quedaron como columnas legacy inertes (Micro-lote 4):
                // la disponibilidad de cuotas sale solo de los planes activos. No se escriben.
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
            var defaults = await ObtenerDefaultsCreditoPersonalAsync();

            var cliente = await _context.Clientes
                .AsNoTracking()
                .Include(c => c.PerfilCreditoPreferido)
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

            var perfil = cliente?.PerfilCreditoPreferido?.Activo == true
                ? cliente.PerfilCreditoPreferido
                : null;

            var tieneConfigPersonalizada = cliente != null &&
                (cliente.TasaInteresMensualPersonalizada.HasValue ||
                 cliente.GastosAdministrativosPersonalizados.HasValue ||
                 cliente.CuotasMaximasPersonalizadas.HasValue);

            var fuente = tieneConfigPersonalizada
                ? FuenteConfiguracionCredito.PorCliente
                : FuenteConfiguracionCredito.Global;

            // ML2.1 — Fase 3 del contrato congelado: ni el perfil preferido ni la personalizacion
            // propia del cliente son autoridad del porcentaje financiero (el plan de cuotas global
            // resuelto por CreditoConfiguracionVentaService/CreditoSimulacionVentaService lo es
            // siempre). Este campo queda como dato informativo/legado — p. ej. para prellenar el
            // formulario antes de elegir plan — y por eso ya no arma una cascada personalizado >
            // perfil > global: siempre refleja la tasa global unica tal cual.
            var tasaMensual = tasaGlobal;

            var gastos = tieneConfigPersonalizada
                ? (cliente!.GastosAdministrativosPersonalizados ?? perfil?.GastosAdministrativos ?? defaults.GastosAdministrativos)
                : (perfil?.GastosAdministrativos ?? defaults.GastosAdministrativos);

            var cuotasMaximas = tieneConfigPersonalizada
                ? (cliente!.CuotasMaximasPersonalizadas ?? perfil?.MaxCuotas ?? defaults.MaxCuotas)
                : (perfil?.MaxCuotas ?? defaults.MaxCuotas);

            var cuotasMinimas = perfil?.MinCuotas ?? defaults.MinCuotas;

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
            var defaults = await ObtenerDefaultsCreditoPersonalAsync();
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

            var (min, max, desc) = CreditoConfiguracionHelper.ResolverRangoCuotasPermitidos(
                metodo,
                perfil,
                cliente,
                defaults.MinCuotas,
                defaults.MaxCuotas);

            // Consolidacion: cuando hay planes de cuotas activos, las cuotas del camino
            // global surgen de esos planes y no del rango min/max default (evita ofrecer
            // cantidades sin plan valido).
            if (desc == "Global")
            {
                var cuotasActivas = await GetCuotasCreditoPersonalActivasAsync();
                if (cuotasActivas.Count > 0)
                {
                    min = cuotasActivas.Min(c => c.CantidadCuotas);
                    max = cuotasActivas.Max(c => c.CantidadCuotas);
                    desc = "Planes de cuotas";
                }
            }

            return (min, max, desc, perfil?.Nombre);
        }

        private async Task<(decimal GastosAdministrativos, int MinCuotas, int MaxCuotas)> ObtenerDefaultsCreditoPersonalAsync()
        {
            var configuracion = await _context.ConfiguracionesPago
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.TipoPago == TipoPago.CreditoPersonal && !c.IsDeleted);

            // Las cantidades de cuotas NO salen del rango legacy Min/MaxCuotasDefaultCreditoPersonal
            // (columnas inertes): la unica fuente son los planes activos. Aqui solo se devuelve el
            // tope tecnico, que actua como cap abierto cuando ni cliente ni perfil fijan un maximo.
            var gastos = configuracion?.GastosAdministrativosDefaultCreditoPersonal ?? 0m;

            return (gastos, MinCuotasTecnico, MaxCuotasTecnico);
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

        public async Task<List<MontoPorPuntajeCreditoViewModel>> GetMontosPorPuntajeAsync()
        {
            var existentes = await _context.ConfiguracionCreditoMontosPorPuntaje
                .AsNoTracking()
                .OrderBy(x => x.Puntaje)
                .ToListAsync();

            var resultado = new List<MontoPorPuntajeCreditoViewModel>();

            for (var p = 0; p <= 10; p++)
            {
                var e = existentes.FirstOrDefault(x => x.Puntaje == p);
                resultado.Add(e != null
                    ? new MontoPorPuntajeCreditoViewModel
                    {
                        Id = e.Id,
                        Puntaje = e.Puntaje,
                        MontoMaximoFinanciable = e.MontoMaximoFinanciable,
                        RequiereAnalisis = e.RequiereAnalisis,
                        Activo = e.Activo,
                        Orden = e.Orden
                    }
                    : new MontoPorPuntajeCreditoViewModel
                    {
                        Puntaje = p,
                        MontoMaximoFinanciable = 0m,
                        RequiereAnalisis = false,
                        Activo = true,
                        Orden = p
                    });
            }

            return resultado;
        }

        public async Task<(bool Ok, List<string> Errores)> GuardarMontosPorPuntajeAsync(
            List<MontoPorPuntajeCreditoViewModel> items,
            string usuario)
        {
            var errores = new List<string>();

            if (items == null || items.Count == 0)
            {
                errores.Add("No se recibieron registros de monto por puntaje.");
                return (false, errores);
            }

            var puntajesRecibidos = items.Select(i => i.Puntaje).ToList();

            var repetidos = puntajesRecibidos.GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repetidos.Any())
                errores.Add($"Puntajes duplicados: {string.Join(", ", repetidos)}.");

            var fueraDeRango = puntajesRecibidos.Where(p => p < 0 || p > 10).ToList();
            if (fueraDeRango.Any())
                errores.Add($"Puntajes fuera de rango 0–10: {string.Join(", ", fueraDeRango)}.");

            if (items.Any(i => i.MontoMaximoFinanciable < 0))
                errores.Add("Los montos no pueden ser negativos.");

            if (errores.Any())
                return (false, errores);

            var puntajesIds = items.Where(i => i.Id > 0).Select(i => i.Id).ToList();
            var existentes = puntajesIds.Count > 0
                ? await _context.ConfiguracionCreditoMontosPorPuntaje
                    .Where(e => puntajesIds.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id)
                : new Dictionary<int, ConfiguracionCreditoMontoPorPuntaje>();

            var existentesPorPuntaje = await _context.ConfiguracionCreditoMontosPorPuntaje
                .Where(e => puntajesRecibidos.Contains(e.Puntaje))
                .ToDictionaryAsync(e => e.Puntaje);

            var fecha = DateTime.UtcNow;

            foreach (var item in items)
            {
                ConfiguracionCreditoMontoPorPuntaje? entidad = null;

                if (item.Id > 0 && existentes.TryGetValue(item.Id, out var porId))
                    entidad = porId;
                else if (existentesPorPuntaje.TryGetValue(item.Puntaje, out var porPuntaje))
                    entidad = porPuntaje;

                if (entidad != null)
                {
                    entidad.MontoMaximoFinanciable = item.MontoMaximoFinanciable;
                    entidad.RequiereAnalisis = item.RequiereAnalisis;
                    entidad.Activo = item.Activo;
                    entidad.Orden = item.Orden;
                    entidad.FechaActualizacion = fecha;
                    entidad.UsuarioActualizacion = usuario;
                }
                else
                {
                    _context.ConfiguracionCreditoMontosPorPuntaje.Add(new ConfiguracionCreditoMontoPorPuntaje
                    {
                        Puntaje = item.Puntaje,
                        MontoMaximoFinanciable = item.MontoMaximoFinanciable,
                        RequiereAnalisis = item.RequiereAnalisis,
                        Activo = item.Activo,
                        Orden = item.Orden,
                        FechaActualizacion = fecha,
                        UsuarioActualizacion = usuario
                    });
                }
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Montos por puntaje 0–10 guardados — {Count} registros — Usuario {Usuario}",
                items.Count, usuario);

            return (true, errores);
        }

        public async Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalAsync()
        {
            var existentes = await _context.ConfiguracionCreditoPersonalCuotas
                .AsNoTracking()
                .OrderBy(x => x.Orden)
                .ThenBy(x => x.CantidadCuotas)
                .ToListAsync();

            var vms = existentes.Select(e => new CuotaCreditoPersonalViewModel
            {
                Id = e.Id,
                CantidadCuotas = e.CantidadCuotas,
                TasaMensual = e.TasaMensual,
                Activo = e.Activo,
                Orden = e.Orden
            }).ToList();

            // CSR-ML5: cuotas sin recargo de TODOS los planes (activos e inactivos, la pantalla
            // admin edita ambos) en una unica query batch — reutiliza el mismo helper que ya usa
            // ResolverPlanesCreditoPersonalAsync (CSR-ML4), evita una query por plan.
            var cuotasSinRecargoPorPlan = await CargarCuotasSinRecargoPorPlanAsync(vms);
            foreach (var vm in vms)
            {
                vm.CuotasSinRecargo = cuotasSinRecargoPorPlan.TryGetValue(vm.Id, out var numeros)
                    ? numeros.ToList()
                    : new List<int>();
            }

            return vms;
        }

        public async Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalActivasAsync()
        {
            var activas = await _context.ConfiguracionCreditoPersonalCuotas
                .AsNoTracking()
                .Where(x => x.Activo)
                .OrderBy(x => x.CantidadCuotas)
                .ToListAsync();

            return activas.Select(e => new CuotaCreditoPersonalViewModel
            {
                Id = e.Id,
                CantidadCuotas = e.CantidadCuotas,
                TasaMensual = e.TasaMensual,
                Activo = e.Activo,
                Orden = e.Orden
            }).ToList();
        }

        public async Task<PlanesCreditoPersonalResultado> ResolverPlanesCreditoPersonalAsync(IEnumerable<int> productoIds)
        {
            var ids = productoIds?.Where(id => id > 0).Distinct().OrderBy(id => id).ToArray() ?? Array.Empty<int>();
            var globales = await GetCuotasCreditoPersonalActivasAsync();

            // CSR-ML4: cuotas sin recargo de TODOS los planes globales activos en una unica query
            // batch (evita N+1 — una consulta por plan resuelto). El plan resuelto la transporta en
            // PlanCuotaCreditoPersonal.CuotasSinRecargo: los call sites de SimularPlanCredito no
            // vuelven a consultar GetCuotasSinRecargoAsync por separado.
            var cuotasSinRecargoPorPlan = await CargarCuotasSinRecargoPorPlanAsync(globales);

            // Sin productos financiados solo puede regir la tabla global. Sin planes activos no hay
            // cuotas disponibles: rechazo explicito, nunca fallback a un rango.
            if (ids.Length == 0)
                return globales.Count == 0
                    ? PlanesCreditoPersonalResultado.SinPlanesGlobales(SinPlanesGlobalesMensaje)
                    : PlanesCreditoPersonalResultado.Resuelto(
                        ConstruirPlanesSoloGlobales(globales, cuotasSinRecargoPorPlan),
                        OrigenPlanesCredito.Global);

            var planesProducto = await _context.ProductoCreditoPersonalCuotas
                .AsNoTracking()
                .Where(p => ids.Contains(p.ProductoId) && p.Activo && !p.Producto.IsDeleted)
                .Select(p => new { p.ProductoId, p.CantidadCuotas, p.TasaMensual })
                .ToListAsync();

            var porProducto = planesProducto
                .GroupBy(p => p.ProductoId)
                .ToDictionary(g => g.Key, g => g.Select(p => p.CantidadCuotas).ToHashSet());

            var productosSinPlanPropio = ids.Where(id => !porProducto.ContainsKey(id)).ToArray();
            var hayProductoSinPlanes = productosSinPlanPropio.Length > 0;

            // Ningun producto tiene configuracion personalizada: rige la global tal cual. Sin planes
            // globales activos no hay cuotas disponibles: rechazo, nunca fallback a un rango.
            if (porProducto.Count == 0)
                return globales.Count == 0
                    ? PlanesCreditoPersonalResultado.SinPlanesGlobales(SinPlanesGlobalesMensaje)
                    : PlanesCreditoPersonalResultado.Resuelto(
                        ConstruirPlanesSoloGlobales(globales, cuotasSinRecargoPorPlan),
                        OrigenPlanesCredito.Global);

            // Cantidades efectivas: interseccion de los sets de cada producto con planes propios.
            // La configuracion personalizada REEMPLAZA a la global para ese producto: no incorpora
            // cantidades que solo existan globalmente.
            HashSet<int>? cantidades = null;
            foreach (var set in porProducto.Values)
            {
                cantidades = cantidades == null
                    ? new HashSet<int>(set)
                    : new HashSet<int>(cantidades.Intersect(set));
            }

            var cantidadesPorProducto = ConstruirCantidadesPorProducto(ids, porProducto, globales);

            // Un producto sin planes propios depende de la tabla global. Sin planes globales activos
            // ese producto no tiene cuotas disponibles: la venta no es financiable. No hay fallback
            // a un rango que ofreceria cantidades que ningun plan habilita.
            if (hayProductoSinPlanes && globales.Count == 0)
                return PlanesCreditoPersonalResultado.SinPlanesGlobales(
                    SinPlanesGlobalesMensaje, cantidadesPorProducto);

            // Producto sin planes propios hereda la global: interseca con las cantidades globales.
            if (hayProductoSinPlanes)
                cantidades!.IntersectWith(globales.Select(g => g.CantidadCuotas));

            // Interseccion vacia: la venta NO es financiable. No se cae al rango ni a la tasa
            // global; ese fallback permitia financiar combinaciones que ningun producto admite.
            if (cantidades!.Count == 0)
                return PlanesCreditoPersonalResultado.SinInterseccion(
                    ComponerMensajeSinInterseccion(cantidadesPorProducto),
                    cantidadesPorProducto);

            // ML2 — Contrato congelado: el plan de cuotas es la UNICA autoridad del porcentaje.
            // Cuando existe una cuota global (ConfiguracionCreditoPersonalCuota) para esta cantidad,
            // su TasaMensual es el porcentaje resuelto tal cual — incluido null, que significa
            // "plan activo sin porcentaje explicito" = configuracion invalida, nunca "heredar" (ni
            // de la tasa propia del producto, ni de la tasa unica global, que dejo de ser fallback).
            // La tasa propia de ProductoCreditoPersonalCuota queda como dato legacy: solo decide
            // que cantidades ofrece ese producto, no el porcentaje, y solo se usa cuando NINGUNA
            // cuota global cubre esa cantidad (unica fuente disponible en ese caso).
            var resultado = new List<PlanCuotaCreditoPersonal>();
            foreach (var cantidad in cantidades.OrderBy(c => c))
            {
                var entradaGlobal = globales.FirstOrDefault(g => g.CantidadCuotas == cantidad);

                // ML2.1 — Contrato congelado: el plan global de cuotas es la UNICA autoridad del
                // porcentaje. Sin cuota global para esta cantidad no hay porcentaje valido: null
                // (invalido), nunca la tasa propia del producto (que dejo de ser fuente de
                // porcentaje; solo sigue decidiendo que cantidades ofrece ese producto, arriba).
                decimal? tasaResuelta = entradaGlobal?.TasaMensual;

                // CSR-ML4: mismo origen que la tasa — solo el plan GLOBAL aporta CuotasSinRecargo.
                // Sin cuota global para esta cantidad no hay exclusiones (coherente con tasaResuelta
                // null: ya es un plan invalido, ProductoCreditoPersonalCuota no la sustituye).
                var cuotasSinRecargoResueltas = entradaGlobal == null
                    ? Array.Empty<int>()
                    : ObtenerCuotasSinRecargoValidadas(
                        entradaGlobal.Id, cantidad, tasaResuelta, cuotasSinRecargoPorPlan);

                resultado.Add(new PlanCuotaCreditoPersonal(
                    cantidad,
                    tasaResuelta,
                    planesProducto
                        .Where(p => p.CantidadCuotas == cantidad)
                        .Select(p => p.ProductoId)
                        .Distinct()
                        .OrderBy(id => id)
                        .ToArray(),
                    hayProductoSinPlanes,
                    cuotasSinRecargoResueltas));
            }

            return PlanesCreditoPersonalResultado.Resuelto(
                resultado,
                hayProductoSinPlanes ? OrigenPlanesCredito.Mixto : OrigenPlanesCredito.Producto,
                cantidadesPorProducto);
        }

        private static IReadOnlyList<PlanCuotaCreditoPersonal> ConstruirPlanesSoloGlobales(
            List<CuotaCreditoPersonalViewModel> globales,
            IReadOnlyDictionary<int, IReadOnlyList<int>> cuotasSinRecargoPorPlan)
        {
            // ML2: la tasa de cada cuota global es la resolucion final, sin fallback a la tasa
            // unica global. null = plan activo sin porcentaje explicito (configuracion invalida).
            return globales
                .OrderBy(g => g.CantidadCuotas)
                .Select(g => new PlanCuotaCreditoPersonal(
                    g.CantidadCuotas,
                    g.TasaMensual,
                    Array.Empty<int>(),
                    true,
                    ObtenerCuotasSinRecargoValidadas(g.Id, g.CantidadCuotas, g.TasaMensual, cuotasSinRecargoPorPlan)))
                .ToArray();
        }

        /// <summary>
        /// CSR-ML4 — Fase 1: carga en una unica query batch las cuotas sin recargo de todos los
        /// planes globales activos que se estan resolviendo (evita una query por plan). Devuelve un
        /// lookup por <c>ConfiguracionCreditoPersonalCuota.Id</c>, cada lista ya ordenada ascendente
        /// (misma garantia que <see cref="GetCuotasSinRecargoAsync"/>).
        /// </summary>
        private async Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> CargarCuotasSinRecargoPorPlanAsync(
            List<CuotaCreditoPersonalViewModel> globales)
        {
            if (globales.Count == 0)
                return new Dictionary<int, IReadOnlyList<int>>();

            var idsGlobales = globales.Select(g => g.Id).ToArray();

            var filas = await _context.ConfiguracionCreditoPersonalCuotasSinRecargo
                .AsNoTracking()
                .Where(c => idsGlobales.Contains(c.ConfiguracionCreditoPersonalCuotaId))
                .OrderBy(c => c.NumeroCuota)
                .Select(c => new { c.ConfiguracionCreditoPersonalCuotaId, c.NumeroCuota })
                .ToListAsync();

            return filas
                .GroupBy(f => f.ConfiguracionCreditoPersonalCuotaId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<int>)g.Select(f => f.NumeroCuota).ToArray());
        }

        /// <summary>
        /// CSR-ML4 — Fase 8: validacion defensiva de la seleccion persistida de cuotas sin recargo
        /// de un plan, contra la misma regla pura que ya usa la persistencia
        /// (<see cref="ConfiguracionCreditoPersonalCuotaSinRecargoRules.Validar"/>). La
        /// administracion ya valida al guardar, pero esto no confia ciegamente en el contenido de
        /// la tabla: numeros fuera de rango, duplicados, o todas las cuotas excluidas con un
        /// recargo &gt; 0 no se silencian ni se convierten en lista vacia — se rechaza resolviendo el
        /// plan (falla ruidosamente), igual que otras invariantes "no deberia pasar" de este service.
        /// </summary>
        private static IReadOnlyList<int> ObtenerCuotasSinRecargoValidadas(
            int planId,
            int cantidadCuotas,
            decimal? tasaMensual,
            IReadOnlyDictionary<int, IReadOnlyList<int>> cuotasSinRecargoPorPlan)
        {
            if (!cuotasSinRecargoPorPlan.TryGetValue(planId, out var numeros) || numeros.Count == 0)
                return Array.Empty<int>();

            var errores = ConfiguracionCreditoPersonalCuotaSinRecargoRules.Validar(cantidadCuotas, tasaMensual, numeros);
            if (errores.Count > 0)
                throw new InvalidOperationException(
                    $"Configuracion de cuotas sin recargo invalida en base de datos para el plan de Credito " +
                    $"Personal #{planId} ({cantidadCuotas} cuotas): {string.Join(" ", errores)}");

            return numeros;
        }

        private static Dictionary<int, IReadOnlyList<int>> ConstruirCantidadesPorProducto(
            IReadOnlyList<int> ids,
            IReadOnlyDictionary<int, HashSet<int>> porProducto,
            List<CuotaCreditoPersonalViewModel> globales)
        {
            var cantidadesGlobales = globales.Select(g => g.CantidadCuotas).OrderBy(c => c).ToArray();

            return ids.ToDictionary(
                id => id,
                id => porProducto.TryGetValue(id, out var propias)
                    ? (IReadOnlyList<int>)propias.OrderBy(c => c).ToArray()
                    : cantidadesGlobales);
        }

        private static string ComponerMensajeSinInterseccion(
            IReadOnlyDictionary<int, IReadOnlyList<int>> cantidadesPorProducto)
        {
            var detalle = string.Join("; ", cantidadesPorProducto
                .OrderBy(par => par.Key)
                .Select(par => par.Value.Count == 0
                    ? $"producto #{par.Key}: sin cuotas habilitadas"
                    : $"producto #{par.Key}: {string.Join(", ", par.Value)}"));

            return "Los productos de esta venta no comparten ninguna cantidad de cuotas de Credito " +
                   $"Personal, por lo que no puede financiarse con este medio de pago ({detalle}).";
        }

        public async Task<(bool Ok, List<string> Errores)> GuardarCuotasCreditoPersonalAsync(
            List<CuotaCreditoPersonalViewModel> items,
            string usuario)
        {
            var errores = new List<string>();

            items ??= new List<CuotaCreditoPersonalViewModel>();

            var cuotasRecibidas = items.Select(i => i.CantidadCuotas).ToList();

            var repetidas = cuotasRecibidas.GroupBy(c => c).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repetidas.Any())
                errores.Add($"Cantidades de cuotas duplicadas: {string.Join(", ", repetidas)}.");

            var fueraDeRango = cuotasRecibidas.Where(c => c < 1 || c > 120).ToList();
            if (fueraDeRango.Any())
                errores.Add($"Cantidades de cuotas fuera de rango 1–120: {string.Join(", ", fueraDeRango)}.");

            if (items.Any(i => i.TasaMensual < 0))
                errores.Add("Las tasas mensuales no pueden ser negativas.");

            // ML4 — Fase 6, contrato congelado: un plan activo requiere un recargo explicito.
            // 0 % es valido; null nunca se guarda para un plan activo (no hay fallback al
            // recargo global legacy, que dejo de tener autoridad desde ML2.1). Un plan inactivo
            // puede conservar un porcentaje historico null: solo se valida cuando Activo = true.
            var activasSinPorcentaje = items
                .Where(i => i.Activo && !i.TasaMensual.HasValue)
                .Select(i => i.CantidadCuotas)
                .ToList();
            if (activasSinPorcentaje.Any())
                errores.Add(
                    "Los planes activos deben tener un recargo total explicito (0 % es valido, nunca " +
                    $"hereda el recargo global): cantidad de cuotas {string.Join(", ", activasSinPorcentaje)}.");

            if (errores.Any())
                return (false, errores);

            var ids = items.Where(i => i.Id > 0).Select(i => i.Id).ToList();
            var existentesPorId = ids.Count > 0
                ? await _context.ConfiguracionCreditoPersonalCuotas
                    .Where(e => ids.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id)
                : new Dictionary<int, ConfiguracionCreditoPersonalCuota>();

            var existentesPorCuota = await _context.ConfiguracionCreditoPersonalCuotas
                .Where(e => cuotasRecibidas.Contains(e.CantidadCuotas))
                .ToDictionaryAsync(e => e.CantidadCuotas);

            var fecha = DateTime.UtcNow;

            foreach (var item in items)
            {
                ConfiguracionCreditoPersonalCuota? entidad = null;

                if (item.Id > 0 && existentesPorId.TryGetValue(item.Id, out var porId))
                    entidad = porId;
                else if (existentesPorCuota.TryGetValue(item.CantidadCuotas, out var porCuota))
                    entidad = porCuota;

                if (entidad != null)
                {
                    entidad.CantidadCuotas = item.CantidadCuotas;
                    entidad.TasaMensual = item.TasaMensual;
                    entidad.Activo = item.Activo;
                    entidad.Orden = item.Orden;
                    entidad.FechaActualizacion = fecha;
                    entidad.UsuarioActualizacion = usuario;
                }
                else
                {
                    _context.ConfiguracionCreditoPersonalCuotas.Add(new ConfiguracionCreditoPersonalCuota
                    {
                        CantidadCuotas = item.CantidadCuotas,
                        TasaMensual = item.TasaMensual,
                        Activo = item.Activo,
                        Orden = item.Orden,
                        FechaActualizacion = fecha,
                        UsuarioActualizacion = usuario
                    });
                }
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Cuotas de Credito Personal guardadas — {Count} registros — Usuario {Usuario}",
                items.Count, usuario);

            return (true, errores);
        }

        public async Task<IReadOnlyList<int>> GetCuotasSinRecargoAsync(int configuracionCreditoPersonalCuotaId)
        {
            return await _context.ConfiguracionCreditoPersonalCuotasSinRecargo
                .AsNoTracking()
                .Where(c => c.ConfiguracionCreditoPersonalCuotaId == configuracionCreditoPersonalCuotaId)
                .OrderBy(c => c.NumeroCuota)
                .Select(c => c.NumeroCuota)
                .ToListAsync();
        }

        public async Task<(bool Ok, List<string> Errores)> GuardarCuotasSinRecargoCreditoPersonalAsync(
            int configuracionCreditoPersonalCuotaId,
            IReadOnlyList<int> numerosCuota,
            string usuario)
        {
            var errores = new List<string>();

            numerosCuota ??= Array.Empty<int>();

            var plan = await _context.ConfiguracionCreditoPersonalCuotas
                .Include(p => p.CuotasSinRecargo)
                .FirstOrDefaultAsync(p => p.Id == configuracionCreditoPersonalCuotaId);

            if (plan == null)
            {
                errores.Add("El plan de Credito Personal indicado no existe.");
                return (false, errores);
            }

            errores.AddRange(ConfiguracionCreditoPersonalCuotaSinRecargoRules.Validar(
                plan.CantidadCuotas, plan.TasaMensual, numerosCuota));

            if (errores.Any())
                return (false, errores);

            _context.ConfiguracionCreditoPersonalCuotasSinRecargo.RemoveRange(plan.CuotasSinRecargo);

            var fecha = DateTime.UtcNow;
            foreach (var numero in numerosCuota.Distinct().OrderBy(n => n))
            {
                _context.ConfiguracionCreditoPersonalCuotasSinRecargo.Add(
                    new ConfiguracionCreditoPersonalCuotaSinRecargo
                    {
                        ConfiguracionCreditoPersonalCuotaId = plan.Id,
                        NumeroCuota = numero
                    });
            }

            plan.FechaActualizacion = fecha;
            plan.UsuarioActualizacion = usuario;

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Cuotas sin recargo guardadas para plan {PlanId} ({CantidadCuotas} cuotas) — " +
                "{Count} numeros — Usuario {Usuario}",
                plan.Id, plan.CantidadCuotas, numerosCuota.Distinct().Count(), usuario);

            return (true, errores);
        }
    }
}
