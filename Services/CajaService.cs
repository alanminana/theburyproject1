using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio centralizado para gestión de cajas y arqueos.
    /// </summary>
    public class CajaService : ICajaService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<CajaService> _logger;
        private readonly INotificacionService _notificacionService;

        private const decimal TOLERANCIA_DIFERENCIA = CajaConstants.TOLERANCIA_DIFERENCIA;

        public CajaService(
            AppDbContext context,
            IMapper mapper,
            ILogger<CajaService> logger,
            INotificacionService notificacionService)
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
            _notificacionService = notificacionService;
        }

        #region CRUD de Cajas

        public async Task<List<Caja>> ObtenerTodasCajasAsync()
        {
            try
            {
                return await _context.Cajas
                    .Where(c => !c.IsDeleted)
                    .OrderBy(c => c.Codigo)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener todas las cajas");
                throw;
            }
        }

        public async Task<Caja?> ObtenerCajaPorIdAsync(int id)
        {
            try
            {
                return await _context.Cajas
                    .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener caja {Id}", id);
                throw;
            }
        }

        public async Task<Caja> CrearCajaAsync(CajaViewModel model)
        {
            try
            {
                ValidarCaja(model);

                if (await ExisteCodigoCajaAsync(model.Codigo))
                {
                    throw new InvalidOperationException($"Ya existe una caja con el código '{model.Codigo}'");
                }

                var caja = _mapper.Map<Caja>(model);
                caja.Estado = EstadoCaja.Cerrada;
                caja.CreatedAt = DateTime.UtcNow;

                _context.Cajas.Add(caja);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Caja creada: {Codigo} - {Nombre}", caja.Codigo, caja.Nombre);

                return caja;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear caja");
                throw;
            }
        }

        public async Task<Caja> ActualizarCajaAsync(int id, CajaViewModel model)
        {
            try
            {
                var caja = await ObtenerCajaPorIdAsync(id);
                if (caja == null)
                {
                    throw new InvalidOperationException("Caja no encontrada");
                }

                // Concurrencia optimista
                if (model.RowVersion is null || model.RowVersion.Length == 0)
                {
                    throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la caja e intentá nuevamente.");
                }

                _context.Entry(caja).Property(c => c.RowVersion).OriginalValue = model.RowVersion;

                ValidarCaja(model);

                // Validar código único (excluyendo la caja actual)
                if (await ExisteCodigoCajaAsync(model.Codigo, id))
                {
                    throw new InvalidOperationException($"Ya existe otra caja con el código '{model.Codigo}'");
                }

                // No permitir desactivar si está abierta
                if (!model.Activa && caja.Estado == EstadoCaja.Abierta)
                {
                    throw new InvalidOperationException("No se puede desactivar una caja que está abierta");
                }

                _mapper.Map(model, caja);
                caja.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Caja actualizada: {Codigo}", caja.Codigo);

                return caja;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Conflicto de concurrencia al actualizar caja {Id}", id);
                throw new InvalidOperationException("La caja fue modificada por otro usuario. Por favor, recargue los datos.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar caja {Id}", id);
                throw;
            }
        }

        public async Task EliminarCajaAsync(int id, byte[]? rowVersion = null)
        {
            try
            {
                var caja = await ObtenerCajaPorIdAsync(id);
                if (caja == null)
                {
                    throw new InvalidOperationException("Caja no encontrada");
                }

                // Concurrencia optimista
                if (rowVersion is null || rowVersion.Length == 0)
                {
                    throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá la caja e intentá nuevamente.");
                }

                _context.Entry(caja).Property(c => c.RowVersion).OriginalValue = rowVersion;

                // No permitir eliminar si está abierta
                if (caja.Estado == EstadoCaja.Abierta)
                {
                    throw new InvalidOperationException("No se puede eliminar una caja que está abierta");
                }

                caja.IsDeleted = true;
                caja.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Caja eliminada: {Codigo}", caja.Codigo);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Conflicto de concurrencia al eliminar caja {Id}", id);
                throw new InvalidOperationException("La caja fue modificada por otro usuario. Por favor, recargue los datos.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar caja {Id}", id);
                throw;
            }
        }

        public async Task<bool> ExisteCodigoCajaAsync(string codigo, int? cajaIdExcluir = null)
        {
            try
            {
                var query = _context.Cajas.Where(c => c.Codigo == codigo && !c.IsDeleted);

                if (cajaIdExcluir.HasValue)
                {
                    query = query.Where(c => c.Id != cajaIdExcluir.Value);
                }

                return await query.AnyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar código de caja {Codigo}", codigo);
                throw;
            }
        }

        #endregion

        #region Apertura de Caja

        public async Task<AperturaCaja> AbrirCajaAsync(AbrirCajaViewModel model, string usuario)
        {
            try
            {
                var caja = await ObtenerCajaPorIdAsync(model.CajaId);
                if (caja == null)
                {
                    throw new InvalidOperationException("Caja no encontrada");
                }

                if (!caja.Activa)
                {
                    throw new InvalidOperationException("La caja no está activa");
                }

                if (await TieneCajaAbiertaAsync(model.CajaId))
                {
                    throw new InvalidOperationException("La caja ya tiene una apertura activa");
                }

                var apertura = new AperturaCaja
                {
                    CajaId = model.CajaId,
                    FechaApertura = DateTime.UtcNow,
                    MontoInicial = model.MontoInicial,
                    UsuarioApertura = usuario,
                    ObservacionesApertura = model.ObservacionesApertura,
                    Cerrada = false,
                    CreatedAt = DateTime.UtcNow
                };

                _context.AperturasCaja.Add(apertura);

                // Actualizar estado de la caja
                caja.Estado = EstadoCaja.Abierta;
                caja.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Caja abierta: {Codigo} por {Usuario} con monto inicial ${Monto:N2}",
                    caja.Codigo, usuario, model.MontoInicial);

                // Crear notificación (no debe bloquear la apertura)
                try
                {
                    await _notificacionService.CrearNotificacionParaRolAsync(
                        "Supervisor",
                        TipoNotificacion.CajaAbierta,
                        "Caja Abierta",
                        $"Caja {caja.Codigo} abierta por {usuario} con monto inicial ${model.MontoInicial:N2}",
                        $"/Caja/DetallesApertura/{apertura.Id}",
                        PrioridadNotificacion.Baja
                    );
                }
                catch (Exception exNoti)
                {
                    _logger.LogWarning(exNoti, "Error al crear notificación de apertura de caja");
                }

                return apertura;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al abrir caja");
                throw;
            }
        }

        public async Task<AperturaCaja?> ObtenerAperturaActivaAsync(int cajaId)
        {
            try
            {
                return await _context.AperturasCaja
                    .Include(a => a.Caja)
                    .Include(a => a.Movimientos)
                    .FirstOrDefaultAsync(a => a.CajaId == cajaId && !a.Cerrada && !a.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener apertura activa para caja {CajaId}", cajaId);
                throw;
            }
        }

        public async Task<AperturaCaja?> ObtenerAperturaPorIdAsync(int id)
        {
            try
            {
                return await _context.AperturasCaja
                    .Include(a => a.Caja)
                    .Include(a => a.Movimientos)
                    .Include(a => a.Cierre)
                    .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener apertura {Id}", id);
                throw;
            }
        }

        public async Task<List<AperturaCaja>> ObtenerAperturasAbiertasAsync()
        {
            try
            {
                return await _context.AperturasCaja
                    .Include(a => a.Caja)
                    .Where(a => !a.Cerrada && !a.IsDeleted)
                    .OrderByDescending(a => a.FechaApertura)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener aperturas abiertas");
                throw;
            }
        }

        public async Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return null;
            }

            try
            {
                return await _context.AperturasCaja
                    .Include(a => a.Caja)
                    .Where(a => !a.Cerrada && !a.IsDeleted && a.UsuarioApertura == usuario)
                    .OrderByDescending(a => a.FechaApertura)
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener apertura activa para usuario {Usuario}", usuario);
                throw;
            }
        }

        public async Task<bool> TieneCajaAbiertaAsync(int cajaId)
        {
            try
            {
                return await _context.AperturasCaja
                    .AnyAsync(a => a.CajaId == cajaId && !a.Cerrada && !a.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar si caja tiene apertura activa {CajaId}", cajaId);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> ExisteAlgunaCajaAbiertaAsync()
        {
            try
            {
                return await _context.AperturasCaja
                    .AnyAsync(a => !a.Cerrada && !a.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar si existe alguna caja abierta");
                throw;
            }
        }

        #endregion

        #region Movimientos de Caja

        public async Task<MovimientoCaja> RegistrarMovimientoAsync(MovimientoCajaViewModel model, string usuario)
        {
            try
            {
                var apertura = await ObtenerAperturaPorIdAsync(model.AperturaCajaId);
                if (apertura == null)
                {
                    throw new InvalidOperationException("Apertura de caja no encontrada");
                }

                if (apertura.Cerrada)
                {
                    throw new InvalidOperationException("No se pueden registrar movimientos en una caja cerrada");
                }

                var movimiento = new MovimientoCaja
                {
                    AperturaCajaId = model.AperturaCajaId,
                    FechaMovimiento = DateTime.UtcNow,
                    Tipo = model.Tipo,
                    Concepto = model.Concepto,
                    Monto = model.Monto,
                    Descripcion = model.Descripcion,
                    Referencia = model.Referencia,
                    Usuario = usuario,
                    Observaciones = model.Observaciones,
                    CreatedAt = DateTime.UtcNow
                };

                _context.MovimientosCaja.Add(movimiento);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Movimiento registrado: {Tipo} - ${Monto:N2} por {Usuario}",
                    model.Tipo, model.Monto, usuario);

                return movimiento;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar movimiento");
                throw;
            }
        }

        public async Task<List<MovimientoCaja>> ObtenerMovimientosDeAperturaAsync(int aperturaId)
        {
            try
            {
                return await _context.MovimientosCaja
                    .Where(m => m.AperturaCajaId == aperturaId && !m.IsDeleted)
                    .OrderBy(m => m.FechaMovimiento)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener movimientos de apertura {AperturaId}", aperturaId);
                throw;
            }
        }

        public async Task<decimal> CalcularSaldoActualAsync(int aperturaId)
        {
            try
            {
                var apertura = await ObtenerAperturaPorIdAsync(aperturaId);
                if (apertura == null)
                {
                    return 0;
                }

                var (ingresos, egresos) = await ObtenerTotalesMovimientosAsync(aperturaId);

                return apertura.MontoInicial + ingresos - egresos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al calcular saldo actual para apertura {AperturaId}", aperturaId);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync()
        {
            try
            {
                // Obtener la primera caja abierta (ordenada por Id para consistencia)
                return await _context.AperturasCaja
                    .Where(a => !a.Cerrada && !a.IsDeleted)
                    .OrderBy(a => a.Id)
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener apertura activa para venta");
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(
            int ventaId,
            string ventaNumero,
            decimal monto,
            TipoPago tipoPago,
            string usuario)
        {
            try
            {
                // Las ventas a crédito personal no generan ingreso inmediato en caja
                // El dinero ingresa cuando el cliente paga las cuotas
                if (tipoPago == TipoPago.CreditoPersonal || tipoPago == TipoPago.CuentaCorriente)
                {
                    _logger.LogDebug(
                        "Venta {VentaNumero} es a crédito/cuenta corriente, no se registra movimiento de caja inmediato",
                        ventaNumero);
                    return null;
                }

                AperturaCaja? apertura = null;
                int? aperturaId = null;
                string? vendedorUserId = null;

                if (ventaId > 0)
                {
                    var ventaData = await _context.Ventas
                        .Where(v => v.Id == ventaId && !v.IsDeleted)
                        .Select(v => new { v.AperturaCajaId, v.VendedorUserId })
                        .FirstOrDefaultAsync();

                    if (ventaData != null)
                    {
                        aperturaId = ventaData.AperturaCajaId;
                        vendedorUserId = ventaData.VendedorUserId;
                    }
                }

                if (aperturaId.HasValue)
                {
                    apertura = await _context.AperturasCaja
                        .FirstOrDefaultAsync(a => a.Id == aperturaId.Value && !a.IsDeleted);

                    if (apertura == null)
                    {
                        _logger.LogWarning(
                            "Apertura asociada a la venta {VentaNumero} no encontrada o eliminada. AperturaCajaId: {AperturaCajaId}",
                            ventaNumero,
                            aperturaId.Value);
                        throw new InvalidOperationException("Apertura asociada a la venta no encontrada o eliminada.");
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(vendedorUserId))
                    {
                        _logger.LogWarning(
                            "Venta {VentaNumero} no tiene AperturaCajaId y no hay soporte para resolver apertura activa por UserId.",
                            ventaNumero);
                        throw new InvalidOperationException("No hay apertura activa para el vendedor de la venta.");
                    }

                    _logger.LogWarning(
                        "Venta {VentaNumero} sin AperturaCajaId ni VendedorUserId. Movimiento no trazable.",
                        ventaNumero);
                    throw new InvalidOperationException("Venta no trazable: falta apertura y vendedor.");
                }

                // Determinar el concepto según el tipo de pago
                // Nota: CreditoPersonal y CuentaCorriente ya fueron filtrados arriba
                var concepto = tipoPago switch
                {
                    TipoPago.Efectivo => ConceptoMovimientoCaja.VentaEfectivo,
                    TipoPago.TarjetaDebito or TipoPago.TarjetaCredito or TipoPago.Tarjeta => ConceptoMovimientoCaja.VentaTarjeta,
                    TipoPago.Cheque => ConceptoMovimientoCaja.VentaCheque,
                    TipoPago.Transferencia or TipoPago.MercadoPago => ConceptoMovimientoCaja.VentaEfectivo, // Transferencias se registran como efectivo
                    TipoPago.CreditoPersonal or TipoPago.CuentaCorriente => throw new InvalidOperationException(
                        $"Los pagos a crédito no deben llegar aquí, fueron filtrados previamente: {tipoPago}"),
                    _ => throw new NotSupportedException($"Tipo de pago no soportado para movimiento de caja: {tipoPago}")
                };

                var movimiento = new MovimientoCaja
                {
                    AperturaCajaId = apertura.Id,
                    FechaMovimiento = DateTime.UtcNow,
                    Tipo = TipoMovimientoCaja.Ingreso,
                    Concepto = concepto,
                    Monto = monto,
                    Descripcion = $"Venta {ventaNumero}",
                    Referencia = ventaNumero,
                    ReferenciaId = ventaId,
                    Usuario = usuario,
                    Observaciones = $"Pago: {tipoPago}",
                    CreatedAt = DateTime.UtcNow
                };

                _context.MovimientosCaja.Add(movimiento);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Movimiento de caja registrado para venta {VentaNumero}: ${Monto:N2} ({TipoPago})",
                    ventaNumero, monto, tipoPago);

                return movimiento;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar movimiento de venta {VentaNumero}", ventaNumero);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(
            int cuotaId,
            string creditoNumero,
            int numeroCuota,
            decimal monto,
            string medioPago,
            string usuario)
        {
            try
            {
                // Obtener apertura de caja activa
                var apertura = await ObtenerAperturaActivaParaVentaAsync();
                if (apertura == null)
                {
                    _logger.LogWarning(
                        "No hay caja abierta para registrar cobro de cuota {CreditoNumero} #{NumeroCuota}",
                        creditoNumero, numeroCuota);
                    return null;
                }

                var movimiento = new MovimientoCaja
                {
                    AperturaCajaId = apertura.Id,
                    FechaMovimiento = DateTime.UtcNow,
                    Tipo = TipoMovimientoCaja.Ingreso,
                    Concepto = ConceptoMovimientoCaja.CobroCuota,
                    Monto = monto,
                    Descripcion = $"Cobro cuota #{numeroCuota} - Crédito {creditoNumero}",
                    Referencia = $"{creditoNumero}-C{numeroCuota}",
                    ReferenciaId = cuotaId,
                    Usuario = usuario,
                    Observaciones = $"Medio de pago: {medioPago}",
                    CreatedAt = DateTime.UtcNow
                };

                _context.MovimientosCaja.Add(movimiento);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Movimiento de caja registrado para cuota #{NumeroCuota} de crédito {CreditoNumero}: ${Monto:N2}",
                    numeroCuota, creditoNumero, monto);

                return movimiento;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error al registrar movimiento de cuota #{NumeroCuota} del crédito {CreditoNumero}", 
                    numeroCuota, creditoNumero);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(
            int creditoId,
            string creditoNumero,
            decimal montoAnticipo,
            string usuario)
        {
            try
            {
                // Si no hay anticipo, no registrar nada
                if (montoAnticipo <= 0)
                {
                    return null;
                }

                // Obtener apertura de caja activa
                var apertura = await ObtenerAperturaActivaParaVentaAsync();
                if (apertura == null)
                {
                    _logger.LogWarning(
                        "No hay caja abierta para registrar anticipo del crédito {CreditoNumero}",
                        creditoNumero);
                    return null;
                }

                var movimiento = new MovimientoCaja
                {
                    AperturaCajaId = apertura.Id,
                    FechaMovimiento = DateTime.UtcNow,
                    Tipo = TipoMovimientoCaja.Ingreso,
                    Concepto = ConceptoMovimientoCaja.AnticipoCredito,
                    Monto = montoAnticipo,
                    Descripcion = $"Anticipo de crédito {creditoNumero}",
                    Referencia = $"{creditoNumero}-ANT",
                    ReferenciaId = creditoId,
                    Usuario = usuario,
                    Observaciones = "Pago inicial que reduce el monto a financiar",
                    CreatedAt = DateTime.UtcNow
                };

                _context.MovimientosCaja.Add(movimiento);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Movimiento de caja registrado para anticipo de crédito {CreditoNumero}: ${Monto:N2}",
                    creditoNumero, montoAnticipo);

                return movimiento;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error al registrar movimiento de anticipo del crédito {CreditoNumero}", 
                    creditoNumero);
                throw;
            }
        }

        public async Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(
            int devolucionId,
            int ventaId,
            string ventaNumero,
            string devolucionNumero,
            decimal monto,
            string usuario)
        {
            try
            {
                if (monto <= 0)
                {
                    throw new InvalidOperationException("El monto del reembolso debe ser mayor a cero.");
                }

                var apertura = await ObtenerAperturaActivaParaUsuarioAsync(usuario)
                    ?? await ObtenerAperturaActivaParaVentaAsync();

                if (apertura == null)
                {
                    throw new InvalidOperationException("No hay una caja abierta para registrar el reembolso.");
                }

                var movimiento = new MovimientoCaja
                {
                    AperturaCajaId = apertura.Id,
                    FechaMovimiento = DateTime.UtcNow,
                    Tipo = TipoMovimientoCaja.Egreso,
                    Concepto = ConceptoMovimientoCaja.DevolucionCliente,
                    Monto = monto,
                    Descripcion = $"Reembolso devolución {devolucionNumero}",
                    Referencia = devolucionNumero,
                    ReferenciaId = devolucionId,
                    Usuario = usuario,
                    Observaciones = $"Venta origen: {ventaNumero} (ID {ventaId})",
                    CreatedAt = DateTime.UtcNow
                };

                _context.MovimientosCaja.Add(movimiento);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Movimiento de caja registrado para devolución {DevolucionNumero}: ${Monto:N2}",
                    devolucionNumero, monto);

                return movimiento;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar movimiento de devolución {DevolucionNumero}", devolucionNumero);
                throw;
            }
        }

        #endregion

        #region Cierre de Caja

        public async Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario)
        {
            try
            {
                var apertura = await ObtenerAperturaPorIdAsync(model.AperturaCajaId);
                if (apertura == null)
                {
                    throw new InvalidOperationException("Apertura de caja no encontrada");
                }

                if (apertura.Cerrada)
                {
                    throw new InvalidOperationException("La caja ya está cerrada");
                }

                var (ingresos, egresos) = await ObtenerTotalesMovimientosAsync(model.AperturaCajaId);
                var montoEsperado = apertura.MontoInicial + ingresos - egresos;
                var montoReal = model.EfectivoContado + model.ChequesContados + model.ValesContados;
                var diferencia = montoReal - montoEsperado;

                // Validar justificación si hay diferencia
                if (Math.Abs(diferencia) > TOLERANCIA_DIFERENCIA && string.IsNullOrWhiteSpace(model.JustificacionDiferencia))
                {
                    throw new InvalidOperationException("Debe proporcionar una justificación para la diferencia encontrada");
                }

                var cierre = new CierreCaja
                {
                    AperturaCajaId = model.AperturaCajaId,
                    FechaCierre = DateTime.UtcNow,
                    MontoInicialSistema = apertura.MontoInicial,
                    TotalIngresosSistema = ingresos,
                    TotalEgresosSistema = egresos,
                    MontoEsperadoSistema = montoEsperado,
                    EfectivoContado = model.EfectivoContado,
                    ChequesContados = model.ChequesContados,
                    ValesContados = model.ValesContados,
                    MontoTotalReal = montoReal,
                    Diferencia = diferencia,
                    TieneDiferencia = Math.Abs(diferencia) > TOLERANCIA_DIFERENCIA,
                    JustificacionDiferencia = model.JustificacionDiferencia,
                    UsuarioCierre = usuario,
                    ObservacionesCierre = model.ObservacionesCierre,
                    DetalleArqueo = model.DetalleArqueo,
                    CreatedAt = DateTime.UtcNow
                };

                _context.CierresCaja.Add(cierre);

                // Marcar apertura como cerrada
                apertura.Cerrada = true;
                apertura.UpdatedAt = DateTime.UtcNow;

                // Actualizar estado de la caja
                apertura.Caja.Estado = EstadoCaja.Cerrada;
                apertura.Caja.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Caja {Codigo} cerrada por {Usuario}. Diferencia: ${Diferencia:N2}",
                    apertura.Caja.Codigo, usuario, diferencia);

                // Crear notificaciones según resultado
                await CrearNotificacionesCierreAsync(cierre, apertura.Caja);

                return cierre;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cerrar caja");
                throw;
            }
        }

        public async Task<CierreCaja?> ObtenerCierrePorIdAsync(int id)
        {
            try
            {
                return await _context.CierresCaja
                    .Include(c => c.AperturaCaja)
                        .ThenInclude(a => a.Caja)
                    .Include(c => c.AperturaCaja)
                        .ThenInclude(a => a.Movimientos)
                    .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener cierre {Id}", id);
                throw;
            }
        }

        public async Task<List<CierreCaja>> ObtenerHistorialCierresAsync(
            int? cajaId = null,
            DateTime? fechaDesde = null,
            DateTime? fechaHasta = null)
        {
            try
            {
                var query = _context.CierresCaja
                    .Include(c => c.AperturaCaja)
                        .ThenInclude(a => a.Caja)
                    .Where(c => !c.IsDeleted);

                if (cajaId.HasValue)
                {
                    query = query.Where(c => c.AperturaCaja.CajaId == cajaId.Value);
                }

                if (fechaDesde.HasValue)
                {
                    query = query.Where(c => c.FechaCierre >= fechaDesde.Value);
                }

                if (fechaHasta.HasValue)
                {
                    var fechaHastaFin = fechaHasta.Value.Date.AddDays(1).AddSeconds(-1);
                    query = query.Where(c => c.FechaCierre <= fechaHastaFin);
                }

                return await query
                    .OrderByDescending(c => c.FechaCierre)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener historial de cierres");
                throw;
            }
        }

        #endregion

        #region Reportes y Estadísticas

        public async Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId)
        {
            try
            {
                var apertura = await ObtenerAperturaPorIdAsync(aperturaId);
                if (apertura == null)
                {
                    throw new InvalidOperationException("Apertura no encontrada");
                }

                var movimientos = await ObtenerMovimientosDeAperturaAsync(aperturaId);

                var (totalIngresos, totalEgresos) = CalcularTotalesMovimientos(movimientos);

                var saldoActual = apertura.MontoInicial + totalIngresos - totalEgresos;

                return new DetallesAperturaViewModel
                {
                    Apertura = apertura,
                    Movimientos = movimientos,
                    SaldoActual = saldoActual,
                    TotalIngresos = totalIngresos,
                    TotalEgresos = totalEgresos,
                    CantidadMovimientos = movimientos.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener detalles de apertura {AperturaId}", aperturaId);
                throw;
            }
        }

        public async Task<ReporteCajaViewModel> GenerarReporteCajaAsync(
            DateTime fechaDesde,
            DateTime fechaHasta,
            int? cajaId = null)
        {
            try
            {
                var query = _context.AperturasCaja
                    .Include(a => a.Caja)
                    .Include(a => a.Movimientos)
                    .Include(a => a.Cierre)
                    .Where(a => !a.IsDeleted &&
                               a.FechaApertura >= fechaDesde &&
                               a.FechaApertura <= fechaHasta);

                if (cajaId.HasValue)
                {
                    query = query.Where(a => a.CajaId == cajaId.Value);
                }

                var aperturas = await query.ToListAsync();

                var totalIngresos = aperturas
                    .SelectMany(a => a.Movimientos)
                    .Where(m => m.Tipo == TipoMovimientoCaja.Ingreso && !m.IsDeleted)
                    .Sum(m => m.Monto);

                var totalEgresos = aperturas
                    .SelectMany(a => a.Movimientos)
                    .Where(m => m.Tipo == TipoMovimientoCaja.Egreso && !m.IsDeleted)
                    .Sum(m => m.Monto);

                var totalDiferencias = aperturas
                    .Where(a => a.Cierre != null)
                    .Sum(a => a.Cierre!.Diferencia);

                return new ReporteCajaViewModel
                {
                    FechaDesde = fechaDesde,
                    FechaHasta = fechaHasta,
                    CajaId = cajaId,
                    Aperturas = aperturas,
                    TotalIngresos = totalIngresos,
                    TotalEgresos = totalEgresos,
                    TotalDiferencias = totalDiferencias,
                    TotalAperturas = aperturas.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar reporte de caja");
                throw;
            }
        }

        public async Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(
            int? cajaId = null,
            DateTime? fechaDesde = null,
            DateTime? fechaHasta = null)
        {
            try
            {
                var cierres = await ObtenerHistorialCierresAsync(cajaId, fechaDesde, fechaHasta);

                var totalCierres = cierres.Count;
                var cierresConDiferencia = cierres.Count(c => c.TieneDiferencia);
                var porcentajeCierresExactos = totalCierres > 0
                    ? ((totalCierres - cierresConDiferencia) / (decimal)totalCierres) * 100
                    : 0;

                var totalDiferenciasPositivas = cierres
                    .Where(c => c.Diferencia > 0)
                    .Sum(c => c.Diferencia);

                var totalDiferenciasNegativas = cierres
                    .Where(c => c.Diferencia < 0)
                    .Sum(c => c.Diferencia);

                return new HistorialCierresViewModel
                {
                    Cierres = cierres,
                    TotalDiferenciasPositivas = totalDiferenciasPositivas,
                    TotalDiferenciasNegativas = totalDiferenciasNegativas,
                    CierresConDiferencia = cierresConDiferencia,
                    TotalCierres = totalCierres,
                    PorcentajeCierresExactos = porcentajeCierresExactos
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener estadísticas de cierres");
                throw;
            }
        }

        #endregion

        #region Métodos Privados Helpers

        private async Task<(decimal Ingresos, decimal Egresos)> ObtenerTotalesMovimientosAsync(int aperturaId)
        {
            var movimientos = await ObtenerMovimientosDeAperturaAsync(aperturaId);
            return CalcularTotalesMovimientos(movimientos);
        }

        private static (decimal Ingresos, decimal Egresos) CalcularTotalesMovimientos(IEnumerable<MovimientoCaja> movimientos)
        {
            var ingresos = movimientos
                .Where(m => m.Tipo == TipoMovimientoCaja.Ingreso)
                .Sum(m => m.Monto);

            var egresos = movimientos
                .Where(m => m.Tipo == TipoMovimientoCaja.Egreso)
                .Sum(m => m.Monto);

            return (ingresos, egresos);
        }

        // Validación de negocio (las validaciones de formato están en el ViewModel con DataAnnotations)
        private static void ValidarCaja(CajaViewModel model)
        {
            // Las validaciones [Required] y [StringLength] ya se hacen en el controller via ModelState
            // Aquí solo agregamos validaciones de negocio adicionales si fueran necesarias
            if (string.IsNullOrWhiteSpace(model.Codigo) || string.IsNullOrWhiteSpace(model.Nombre))
            {
                throw new InvalidOperationException("El código y nombre de caja son obligatorios");
            }
        }

        private async Task CrearNotificacionesCierreAsync(CierreCaja cierre, Caja caja)
        {
            try
            {
                if (cierre.TieneDiferencia)
                {
                    var tipoDiferencia = cierre.Diferencia > 0 ? "sobrante" : "faltante";
                    await _notificacionService.CrearNotificacionParaRolAsync(
                        "Supervisor",
                        TipoNotificacion.CierreConDiferencia,
                        "Cierre de Caja con Diferencia",
                        $"Caja {caja.Codigo} cerrada con ${Math.Abs(cierre.Diferencia):N2} {tipoDiferencia}. Usuario: {cierre.UsuarioCierre}",
                        $"/Caja/DetallesCierre/{cierre.Id}",
                        PrioridadNotificacion.Alta
                    );
                }
                else
                {
                    await _notificacionService.CrearNotificacionParaRolAsync(
                        "Supervisor",
                        TipoNotificacion.CajaCerrada,
                        "Caja Cerrada",
                        $"Caja {caja.Codigo} cerrada sin diferencias por {cierre.UsuarioCierre}",
                        $"/Caja/DetallesCierre/{cierre.Id}",
                        PrioridadNotificacion.Baja
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al crear notificaciones de cierre de caja");
                // No re-lanzar: las notificaciones no deben bloquear el cierre
            }
        }

        #endregion
    }
}
