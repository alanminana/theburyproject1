using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Circuito del cheque RECIBIDO (Hito 10). Decisiones D1-A (acreditación manual asentada en la caja abierta vigente)
    /// y D2-A (rechazo manual con motivo; la venta no se cancela). Ver docs/cheques-circuito-propuesta.md.
    /// </summary>
    public partial class CajaService
    {
        /// <summary>Un cheque cuyo vencimiento cae dentro de estos días se marca "por vencer".</summary>
        internal const int DiasAvisoChequePorVencer = 7;

        internal const int MaxLongitudMotivoRechazo = 500;

        private sealed record OrigenCheque(
            int Id, int AperturaCajaId, bool AperturaCerrada, int? VentaId, ConceptoMovimientoCaja Concepto,
            decimal Monto, EstadoAcreditacionMovimientoCaja? Estado);

        /// <summary>Un ingreso es "cheque recibido" si es cobro de venta con cheque o cobro de cuota pagado con cheque.</summary>
        internal static bool EsIngresoChequeRecibido(MovimientoCaja m) =>
            m.Tipo == TipoMovimientoCaja.Ingreso
            && (m.Concepto == ConceptoMovimientoCaja.VentaCheque
                || (m.Concepto == ConceptoMovimientoCaja.CobroCuota && m.TipoPago == TipoPago.Cheque));

        private async Task<OrigenCheque> LeerOrigenChequeAsync(int movimientoId)
        {
            var m = await _context.MovimientosCaja
                .AsNoTracking()
                .Where(x => x.Id == movimientoId && !x.IsDeleted)
                .Select(x => new
                {
                    x.Id, x.AperturaCajaId, x.Tipo, x.Concepto, x.TipoPago, x.VentaId, x.Monto, x.EstadoAcreditacion,
                    Cerrada = x.AperturaCaja.Cerrada
                })
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("Movimiento no encontrado.");

            var esCheque = m.Tipo == TipoMovimientoCaja.Ingreso
                && (m.Concepto == ConceptoMovimientoCaja.VentaCheque
                    || (m.Concepto == ConceptoMovimientoCaja.CobroCuota && m.TipoPago == TipoPago.Cheque));
            if (!esCheque)
                throw new InvalidOperationException("El movimiento no es un cheque recibido.");

            return new OrigenCheque(m.Id, m.AperturaCajaId, m.Cerrada, m.VentaId, m.Concepto, m.Monto, m.EstadoAcreditacion);
        }

        private static string DescribirEstado(EstadoAcreditacionMovimientoCaja? estado) => estado switch
        {
            EstadoAcreditacionMovimientoCaja.Pendiente => "Pendiente",
            EstadoAcreditacionMovimientoCaja.Acreditado => "Acreditado",
            EstadoAcreditacionMovimientoCaja.Rechazado => "Rechazado",
            EstadoAcreditacionMovimientoCaja.Anulado => "Anulado (venta cancelada)",
            _ => estado?.ToString() ?? "sin estado"
        };

        private const string MensajeChequeYaProcesado =
            "El cheque ya fue procesado por otra operación. Recargá la pantalla para ver su estado actual.";

        /// <summary>
        /// Serializa sobre la apertura del ingreso original aunque esté cerrada (el cierre ya no compite): dos operaciones
        /// sobre el mismo cheque, de usuarios distintos y con cajas vigentes distintas, pasan de a una.
        /// </summary>
        private async Task<T> BajoLockDeOrigenChequeAsync<T>(int aperturaOrigenId, Func<Task<T>> operacion)
        {
            var propia = _context.Database.CurrentTransaction == null;
            await using var transaccion = propia ? await _context.Database.BeginTransactionAsync() : null;

            await AperturaCajaConcurrencia.BloquearAsync(_context, aperturaOrigenId);
            var resultado = await operacion();

            if (transaccion != null)
                await transaccion.CommitAsync();
            return resultado;
        }

        private const string IndiceUnicoAsientoCheque = "IX_MovimientosCaja_MovimientoOrigenId_Concepto";

        /// <summary>
        /// Conflicto REAL con otra operación sobre el mismo cheque: token de concurrencia vencido o violación del índice único
        /// (origen, concepto). Cualquier otro error de persistencia (FK, truncamiento, timeout) NO es un duplicado y se propaga.
        /// </summary>
        private static bool EsConflictoDeConcurrencia(Exception ex)
        {
            if (ex is DbUpdateConcurrencyException)
                return true;
            if (ex is not DbUpdateException)
                return false;

            for (Exception? actual = ex; actual != null; actual = actual.InnerException)
            {
                if (actual.Message.Contains(IndiceUnicoAsientoCheque, StringComparison.OrdinalIgnoreCase)
                    || actual.Message.Contains("UNIQUE constraint failed: MovimientosCaja.MovimientoOrigenId", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ acreditar

        /// <inheritdoc/>
        public async Task<ChequeAccionResultado> AcreditarChequeAsync(int movimientoId, string usuario)
        {
            try
            {
                var origen = await LeerOrigenChequeAsync(movimientoId);
                if (origen.Estado != EstadoAcreditacionMovimientoCaja.Pendiente)
                {
                    throw new InvalidOperationException(
                        $"Solo se puede acreditar un cheque en estado Pendiente. Estado actual: {DescribirEstado(origen.Estado)}.");
                }

                ChequeAccionResultado resultado;
                if (!origen.AperturaCerrada)
                {
                    try
                    {
                        resultado = await AcreditarEnSuCajaAsync(origen, usuario);
                        return resultado;
                    }
                    catch (InvalidOperationException ex) when (ex.Message == MensajeAcreditacionCajaCerrada)
                    {
                        // La caja se cerró entre la lectura y el lock: se sigue por la vía de caja cerrada.
                    }
                }

                resultado = await AcreditarEnCajaVigenteAsync(origen, usuario);
                return resultado;
            }
            catch (Exception ex) when (EsConflictoDeConcurrencia(ex))
            {
                _logger.LogWarning(ex, "Conflicto al acreditar el cheque del movimiento {MovimientoId}", movimientoId);
                throw new InvalidOperationException(MensajeChequeYaProcesado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al acreditar el cheque del movimiento {MovimientoId}", movimientoId);
                throw;
            }
        }

        private async Task<ChequeAccionResultado> AcreditarEnSuCajaAsync(OrigenCheque origen, string usuario)
        {
            return await BajoLockDeAperturaAsync(
                origen.AperturaCajaId,
                async () =>
                {
                    var movimiento = await CargarOrigenParaEscrituraAsync(origen.Id);
                    ExigirEstado(movimiento, EstadoAcreditacionMovimientoCaja.Pendiente, "acreditar");

                    movimiento.EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Acreditado;
                    movimiento.UpdatedAt = DateTime.UtcNow;
                    await MarcarAcreditadoEnDatosChequeAsync(movimiento.VentaId, usuario);

                    _auditoria?.AgregarEvento("Caja", "AcreditarCheque", $"MovimientoCaja:{movimiento.Id}",
                        $"Apertura={movimiento.AperturaCajaId}; Venta={movimiento.VentaId}; Monto={movimiento.Monto:0.00}; Usuario={usuario}");

                    await _context.SaveChangesAsync();
                    return new ChequeAccionResultado(movimiento.Id, null, movimiento.AperturaCajaId, EstadoAcreditacionMovimientoCaja.Acreditado);
                },
                validarVigencia: false,
                mensajeCerrada: MensajeAcreditacionCajaCerrada);
        }

        private async Task<ChequeAccionResultado> AcreditarEnCajaVigenteAsync(OrigenCheque origen, string usuario)
        {
            var destino = await ObtenerAperturaActivaParaUsuarioAsync(usuario)
                ?? throw new InvalidOperationException(
                    "La caja de este cheque ya cerró: la acreditación se asienta en tu caja abierta y no tenés una. Abrí una caja para acreditarlo.");

            return await BajoLockDeOrigenChequeAsync(origen.AperturaCajaId, () =>
                BajoLockDeAperturaAsync(
                    destino.Id,
                    async () =>
                    {
                        var movimiento = await CargarOrigenParaEscrituraAsync(origen.Id);
                        ExigirEstado(movimiento, EstadoAcreditacionMovimientoCaja.Pendiente, "acreditar");

                        if (await _context.MovimientosCaja.AnyAsync(m => m.MovimientoOrigenId == movimiento.Id
                                && m.Concepto == ConceptoMovimientoCaja.AcreditacionCheque && !m.IsDeleted))
                        {
                            throw new InvalidOperationException(MensajeChequeYaProcesado);
                        }

                        var ahora = DateTime.UtcNow;
                        movimiento.EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Acreditado;
                        movimiento.UpdatedAt = ahora;
                        await MarcarAcreditadoEnDatosChequeAsync(movimiento.VentaId, usuario);

                        // Ingreso bancario (TipoPago.Cheque nunca es físico): no entra en el efectivo esperado ni en el arqueo.
                        var asiento = new MovimientoCaja
                        {
                            AperturaCajaId = destino.Id,
                            FechaMovimiento = ahora,
                            Tipo = TipoMovimientoCaja.Ingreso,
                            Concepto = ConceptoMovimientoCaja.AcreditacionCheque,
                            EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Acreditado,
                            TipoPago = TipoPago.Cheque,
                            // Sin FK a Ventas a propósito: insertar con VentaId obliga a SQL Server a leer (S) la fila de la venta; con la Apertura ya
                            // bloqueada eso invierte el orden Venta → Apertura de la cancelación y la devolución (deadlock con READ COMMITTED).
                            // El enlace con la venta viaja en ReferenciaId (solo informativo) y el origen en MovimientoOrigenId.
                            VentaId = null,
                            Monto = movimiento.Monto,
                            Descripcion = $"Acreditación de cheque {movimiento.Referencia}".Trim(),
                            Referencia = movimiento.Referencia,
                            ReferenciaId = movimiento.VentaId,
                            MedioPagoDetalle = "Cheque",
                            Usuario = usuario,
                            Observaciones = $"Acredita el ingreso #{movimiento.Id} de la apertura {movimiento.AperturaCajaId} (caja cerrada).",
                            MovimientoOrigenId = movimiento.Id,
                            CreatedAt = ahora
                        };
                        _context.MovimientosCaja.Add(asiento);

                        _auditoria?.AgregarEvento("Caja", "AcreditarCheque", $"MovimientoCaja:{movimiento.Id}",
                            $"Apertura origen={movimiento.AperturaCajaId}; Apertura asiento={destino.Id}; Venta={movimiento.VentaId}; Monto={movimiento.Monto:0.00}; Usuario={usuario}");

                        await _context.SaveChangesAsync();
                        return new ChequeAccionResultado(movimiento.Id, asiento.Id, destino.Id, EstadoAcreditacionMovimientoCaja.Acreditado);
                    },
                    usuarioPropietario: usuario));
        }

        // ------------------------------------------------------------------ rechazar

        /// <inheritdoc/>
        public async Task<ChequeAccionResultado> RechazarChequeAsync(int movimientoId, string motivo, string usuario)
        {
            try
            {
                motivo = (motivo ?? string.Empty).Trim();
                if (motivo.Length == 0)
                    throw new InvalidOperationException("El motivo del rechazo es obligatorio.");
                if (motivo.Length > MaxLongitudMotivoRechazo)
                    throw new InvalidOperationException($"El motivo del rechazo no puede superar los {MaxLongitudMotivoRechazo} caracteres.");

                var origen = await LeerOrigenChequeAsync(movimientoId);
                if (origen.Estado is not (EstadoAcreditacionMovimientoCaja.Pendiente or EstadoAcreditacionMovimientoCaja.Acreditado))
                {
                    throw new InvalidOperationException(
                        $"Solo se puede rechazar un cheque Pendiente o Acreditado. Estado actual: {DescribirEstado(origen.Estado)}.");
                }

                // Acreditado => hace falta un egreso en una caja abierta; Pendiente => solo cambia de estado.
                if (origen.Estado == EstadoAcreditacionMovimientoCaja.Acreditado)
                    return await RechazarAcreditadoAsync(origen, motivo, usuario);

                return await RechazarPendienteAsync(origen, motivo, usuario);
            }
            catch (Exception ex) when (EsConflictoDeConcurrencia(ex))
            {
                _logger.LogWarning(ex, "Conflicto al rechazar el cheque del movimiento {MovimientoId}", movimientoId);
                throw new InvalidOperationException(MensajeChequeYaProcesado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al rechazar el cheque del movimiento {MovimientoId}", movimientoId);
                throw;
            }
        }

        private async Task<ChequeAccionResultado> RechazarPendienteAsync(OrigenCheque origen, string motivo, string usuario)
        {
            async Task<ChequeAccionResultado> Aplicar()
            {
                var movimiento = await CargarOrigenParaEscrituraAsync(origen.Id);
                ExigirEstado(movimiento, EstadoAcreditacionMovimientoCaja.Pendiente, "rechazar");
                await ExigirVentaNoCanceladaBajoLockAsync(movimiento.VentaId);

                var ahora = DateTime.UtcNow;
                movimiento.EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Rechazado;
                movimiento.UpdatedAt = ahora;
                await MarcarRechazadoEnDatosChequeAsync(movimiento, motivo, usuario, ahora);

                _auditoria?.AgregarEvento("Caja", "RechazarCheque", $"MovimientoCaja:{movimiento.Id}",
                    $"Apertura={movimiento.AperturaCajaId}; Venta={movimiento.VentaId}; Monto={movimiento.Monto:0.00}; Previo=Pendiente; Motivo={motivo}; Usuario={usuario}");

                await _context.SaveChangesAsync();
                return new ChequeAccionResultado(movimiento.Id, null, movimiento.AperturaCajaId, EstadoAcreditacionMovimientoCaja.Rechazado);
            }

            if (!origen.AperturaCerrada)
            {
                try
                {
                    return await BajoLockDeAperturaAsync(origen.AperturaCajaId, Aplicar,
                        validarVigencia: false, mensajeCerrada: MensajeAcreditacionCajaCerrada);
                }
                catch (InvalidOperationException ex) when (ex.Message == MensajeAcreditacionCajaCerrada)
                {
                    // Se cerró entre la lectura y el lock: se sigue por la vía de caja cerrada.
                }
            }

            return await BajoLockDeOrigenChequeAsync(origen.AperturaCajaId, Aplicar);
        }

        private async Task<ChequeAccionResultado> RechazarAcreditadoAsync(OrigenCheque origen, string motivo, string usuario)
        {
            async Task<ChequeAccionResultado> Aplicar(int destinoId)
            {
                var movimiento = await CargarOrigenParaEscrituraAsync(origen.Id);
                ExigirEstado(movimiento, EstadoAcreditacionMovimientoCaja.Acreditado, "rechazar");
                await ExigirVentaNoCanceladaBajoLockAsync(movimiento.VentaId);

                if (await _context.MovimientosCaja.AnyAsync(m => m.MovimientoOrigenId == movimiento.Id
                        && m.Concepto == ConceptoMovimientoCaja.RechazoCheque && !m.IsDeleted))
                {
                    throw new InvalidOperationException(MensajeChequeYaProcesado);
                }

                var ahora = DateTime.UtcNow;
                movimiento.UpdatedAt = ahora;
                var rechazado = await MarcarRechazadoEnDatosChequeAsync(movimiento, motivo, usuario, ahora);
                if (!rechazado)
                    throw new InvalidOperationException("El cheque ya fue rechazado.");

                // Contrapartida bancaria (no física) en la caja vigente: el ingreso acreditado queda donde estaba.
                var asiento = new MovimientoCaja
                {
                    AperturaCajaId = destinoId,
                    FechaMovimiento = ahora,
                    Tipo = TipoMovimientoCaja.Egreso,
                    Concepto = ConceptoMovimientoCaja.RechazoCheque,
                    EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.NoAplica,
                    TipoPago = TipoPago.Cheque,
                    // Sin FK a Ventas (ver el asiento de acreditación): evita invertir el orden de locks Venta → Apertura.
                    VentaId = null,
                    Monto = movimiento.Monto,
                    Descripcion = $"Rechazo de cheque {movimiento.Referencia}".Trim(),
                    Referencia = movimiento.Referencia,
                    ReferenciaId = movimiento.VentaId,
                    MedioPagoDetalle = "Cheque",
                    Usuario = usuario,
                    Observaciones = $"Contrapartida del ingreso acreditado #{movimiento.Id}. Motivo: {motivo}",
                    MovimientoOrigenId = movimiento.Id,
                    CreatedAt = ahora
                };
                _context.MovimientosCaja.Add(asiento);

                _auditoria?.AgregarEvento("Caja", "RechazarCheque", $"MovimientoCaja:{movimiento.Id}",
                    $"Apertura origen={movimiento.AperturaCajaId}; Apertura asiento={destinoId}; Venta={movimiento.VentaId}; Monto={movimiento.Monto:0.00}; Previo=Acreditado; Motivo={motivo}; Usuario={usuario}");

                await _context.SaveChangesAsync();
                return new ChequeAccionResultado(movimiento.Id, asiento.Id, destinoId, EstadoAcreditacionMovimientoCaja.Acreditado);
            }

            if (!origen.AperturaCerrada)
            {
                try
                {
                    return await BajoLockDeAperturaAsync(origen.AperturaCajaId, () => Aplicar(origen.AperturaCajaId),
                        validarVigencia: false, mensajeCerrada: MensajeAcreditacionCajaCerrada);
                }
                catch (InvalidOperationException ex) when (ex.Message == MensajeAcreditacionCajaCerrada)
                {
                    // Se cerró entre la lectura y el lock: se sigue por la vía de caja cerrada.
                }
            }

            var destino = await ObtenerAperturaActivaParaUsuarioAsync(usuario)
                ?? throw new InvalidOperationException(
                    "El cheque ya estaba acreditado en una caja cerrada: la contrapartida del rechazo se asienta en tu caja abierta y no tenés una. Abrí una caja para rechazarlo.");

            return await BajoLockDeOrigenChequeAsync(origen.AperturaCajaId, () =>
                BajoLockDeAperturaAsync(destino.Id, () => Aplicar(destino.Id), usuarioPropietario: usuario));
        }

        // ------------------------------------------------------------------ B13: cancelar una venta con cheque de caja cerrada

        /// <summary>
        /// B13. Contrapartida auditada de la cancelación de una venta pagada con cheque cuyo ingreso pertenece a una caja CERRADA.
        /// La caja cerrada no se reabre ni se le agregan movimientos (sus totales, efectivo esperado y arqueo quedan intactos):
        ///  - Pendiente: nunca se cobró, no hay dinero que devolver; el ingreso pasa a Anulado (sin egreso).
        ///  - Acreditado (en su caja o por un asiento AcreditacionCheque): egreso <c>ReversionVenta</c> bancario (no físico) en la caja
        ///    ABIERTA del usuario, enlazado al ingreso original con <c>MovimientoOrigenId</c> (índice único: una sola contrapartida).
        ///  - Rechazado (o rechazo asentado con RechazoCheque) o ya Anulado: no se agrega nada (la salida ya está asentada).
        /// Orden de locks (el de todo el circuito): Venta (la cancelación ya la tiene) → Apertura del cheque → Apertura vigente.
        /// Sin caja vigente válida se rechaza antes de escribir (la cancelación entera hace rollback).
        /// </summary>
        private async Task<MovimientoCaja?> ContramovimientoChequeCajaCerradaAsync(
            int movimientoOriginalId, int aperturaOrigenId, int ventaId, string ventaNumero, string motivo, string usuario)
        {
            // Candidata (se revalida bajo lock): resolverla antes evita tomar locks si no hay caja donde asentar.
            var destinoCandidato = await ObtenerAperturaActivaParaUsuarioAsync(usuario);

            return await BajoLockDeOrigenChequeAsync<MovimientoCaja?>(aperturaOrigenId, async () =>
            {
                var original = await CargarOrigenParaEscrituraAsync(movimientoOriginalId);

                // Idempotencia / rechazo previo: nada que asentar.
                if (await ChequeRechazadoAsync(original))
                {
                    _logger.LogInformation("Venta {VentaNumero}: el cheque ya estaba rechazado, no se agrega reversión (la salida ya está asentada).", ventaNumero);
                    return null;
                }

                if (await _context.MovimientosCaja.AsNoTracking().AnyAsync(m => !m.IsDeleted
                        && m.Concepto == ConceptoMovimientoCaja.ReversionVenta
                        && (m.MovimientoOrigenId == original.Id || m.VentaId == ventaId)))
                {
                    return null;
                }

                if (original.EstadoAcreditacion == EstadoAcreditacionMovimientoCaja.Anulado)
                    return null;

                if (original.EstadoAcreditacion == EstadoAcreditacionMovimientoCaja.Pendiente)
                {
                    var ahoraPendiente = DateTime.UtcNow;
                    original.EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Anulado;
                    original.UpdatedAt = ahoraPendiente;
                    _auditoria?.AgregarEvento("Caja", "AnularChequePendienteCajaCerrada", $"MovimientoCaja:{original.Id}",
                        $"Apertura origen={original.AperturaCajaId} (cerrada); Venta={ventaId}; Monto={original.Monto:0.00}; Usuario={usuario}; Motivo={Recortar(motivo, 300)}");
                    await _context.SaveChangesAsync();
                    return null;
                }

                if (original.EstadoAcreditacion != EstadoAcreditacionMovimientoCaja.Acreditado)
                {
                    throw new InvalidOperationException(
                        $"No se puede cancelar la venta {ventaNumero}: el cheque está en un estado que no admite reversión automática ({DescribirEstado(original.EstadoAcreditacion)}).");
                }

                var destino = destinoCandidato
                    ?? throw new InvalidOperationException(
                        $"No se puede cancelar la venta {ventaNumero}: el cheque ya fue cobrado en una caja cerrada y la devolución se asienta en tu caja abierta. Abrí una caja para cancelar la venta.");

                return await BajoLockDeAperturaAsync<MovimientoCaja?>(destino.Id, async () =>
                {
                    var ahora = DateTime.UtcNow;
                    var egreso = new MovimientoCaja
                    {
                        AperturaCajaId = destino.Id,
                        FechaMovimiento = ahora,
                        Tipo = TipoMovimientoCaja.Egreso,
                        Concepto = ConceptoMovimientoCaja.ReversionVenta,
                        EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Revertido,
                        // Bancario: el cheque ya se acreditó; nunca es efectivo físico (no toca esperado ni arqueo).
                        TipoPago = TipoPago.Cheque,
                        // Se hereda del flujo de cancelación, que ya tiene el lock de la fila de la venta (orden Venta → Apertura).
                        VentaId = ventaId,
                        Monto = original.Monto,
                        Descripcion = $"Reversión por cancelación de venta {ventaNumero}",
                        Referencia = ventaNumero,
                        ReferenciaId = ventaId,
                        MedioPagoDetalle = original.MedioPagoDetalle,
                        Usuario = usuario,
                        Observaciones = $"Contramovimiento del ingreso #{original.Id} de la apertura {original.AperturaCajaId} (caja cerrada). Motivo: {motivo}",
                        MovimientoOrigenId = original.Id,
                        CreatedAt = ahora
                    };
                    _context.MovimientosCaja.Add(egreso);

                    _auditoria?.AgregarEvento("Caja", "ReversionChequeCajaCerrada", $"MovimientoCaja:{original.Id}",
                        $"Apertura origen={original.AperturaCajaId} (cerrada); Apertura contrapartida={destino.Id}; Venta={ventaId}; Monto={original.Monto:0.00}; Usuario={usuario}; Motivo={Recortar(motivo, 300)}");

                    await _context.SaveChangesAsync();
                    return egreso;
                }, usuarioPropietario: usuario);
            });
        }

        // ------------------------------------------------------------------ helpers de escritura

        private async Task<MovimientoCaja> CargarOrigenParaEscrituraAsync(int movimientoId)
        {
            var movimiento = await _context.MovimientosCaja
                .FirstOrDefaultAsync(m => m.Id == movimientoId && !m.IsDeleted)
                ?? throw new InvalidOperationException("Movimiento no encontrado.");

            // Con el lock tomado, el estado definitivo sale de la base: la entidad pudo estar rastreada y obsoleta.
            await _context.Entry(movimiento).ReloadAsync();
            if (movimiento.IsDeleted)
                throw new InvalidOperationException("Movimiento no encontrado.");
            return movimiento;
        }

        private static void ExigirEstado(MovimientoCaja movimiento, EstadoAcreditacionMovimientoCaja esperado, string accion)
        {
            if (movimiento.EstadoAcreditacion != esperado)
            {
                throw new InvalidOperationException(
                    $"No se puede {accion} el cheque: su estado actual es {DescribirEstado(movimiento.EstadoAcreditacion)}.");
            }
        }

        private async Task<DatosCheque?> CargarDatosChequeAsync(int? ventaId) =>
            ventaId.HasValue
                ? await _context.DatosCheque.FirstOrDefaultAsync(d => d.VentaId == ventaId.Value && !d.IsDeleted)
                : null;

        private async Task MarcarAcreditadoEnDatosChequeAsync(int? ventaId, string usuario)
        {
            var datos = await CargarDatosChequeAsync(ventaId);
            if (datos == null) return;
            datos.AcreditadoAt = DateTime.UtcNow;
            datos.AcreditadoPor = Recortar(usuario, 50);
        }

        /// <summary>Marca el rechazo en el snapshot del cheque (venta) o, en cheques de cuota, en las observaciones del ingreso. Devuelve false si ya estaba rechazado.</summary>
        private async Task<bool> MarcarRechazadoEnDatosChequeAsync(MovimientoCaja movimiento, string motivo, string usuario, DateTime ahora)
        {
            var datos = await CargarDatosChequeAsync(movimiento.VentaId);
            if (datos != null)
            {
                if (datos.RechazadoAt.HasValue) return false;
                datos.RechazadoAt = ahora;
                datos.RechazadoPor = Recortar(usuario, 50);
                datos.MotivoRechazo = Recortar(motivo, MaxLongitudMotivoRechazo);
                return true;
            }

            var traza = $"Cheque rechazado el {ahora:yyyy-MM-dd} por {usuario}: {motivo}";
            movimiento.Observaciones = Recortar(string.IsNullOrWhiteSpace(movimiento.Observaciones)
                ? traza
                : $"{movimiento.Observaciones} | {traza}", 500);
            return true;
        }

        private static string Recortar(string valor, int max) => valor.Length <= max ? valor : valor[..max];

        // ------------------------------------------------------------------ integridad bajo lock

        /// <summary>Cheque ya rechazado (estado del ingreso, egreso de contrapartida o marca en el cheque). Leer bajo lock.</summary>
        private async Task<bool> ChequeRechazadoAsync(MovimientoCaja origen)
        {
            if (origen.EstadoAcreditacion == EstadoAcreditacionMovimientoCaja.Rechazado)
                return true;

            if (await _context.MovimientosCaja.AsNoTracking().AnyAsync(m => m.MovimientoOrigenId == origen.Id
                    && m.Concepto == ConceptoMovimientoCaja.RechazoCheque && !m.IsDeleted))
                return true;

            return origen.VentaId.HasValue
                && await _context.DatosCheque.AsNoTracking().AnyAsync(d => d.VentaId == origen.VentaId.Value
                    && !d.IsDeleted && d.RechazadoAt != null);
        }

        /// <summary>
        /// Revalida bajo el lock de apertura que la venta del cheque no esté cancelada ni reversada. ORDEN GLOBAL DE LOCKS:
        /// Crédito → Venta → Apertura del cheque → Apertura vigente (la cancelación y la devolución toman la Venta primero y recién
        /// después la Apertura). Por eso acá NO se lee la fila de <c>Ventas</c> reteniendo la Apertura (con READ COMMITTED sin
        /// snapshot esa lectura espera al lock de Venta de una cancelación que a su vez espera esta Apertura: deadlock). Basta con el
        /// libro de caja: la cancelación toma este mismo lock de Apertura, así que o ya asentó su reversión (se ve acá), o anuló el
        /// ingreso Pendiente (ya lo rechaza el control de estado), o espera a que este rechazo termine y entonces ve el cheque
        /// rechazado y no duplica la salida.
        /// </summary>
        private async Task ExigirVentaNoCanceladaBajoLockAsync(int? ventaId)
        {
            if (!ventaId.HasValue) return;

            var reversada = await _context.MovimientosCaja.AsNoTracking().AnyAsync(m => m.VentaId == ventaId.Value
                && m.Concepto == ConceptoMovimientoCaja.ReversionVenta && !m.IsDeleted);
            if (reversada)
                throw new InvalidOperationException("No se puede rechazar el cheque: la venta está cancelada.");
        }

        /// <summary>Apertura del ingreso del cheque de una venta (para serializar devolución y rechazo sobre el mismo lock), o null.</summary>
        private async Task<int?> ObtenerAperturaDelChequeDeVentaAsync(int ventaId) =>
            await _context.MovimientosCaja.AsNoTracking()
                .Where(m => m.VentaId == ventaId && !m.IsDeleted && m.Tipo == TipoMovimientoCaja.Ingreso
                         && m.Concepto == ConceptoMovimientoCaja.VentaCheque)
                .OrderBy(m => m.Id)
                .Select(m => (int?)m.AperturaCajaId)
                .FirstOrDefaultAsync();

        // ------------------------------------------------------------------ D4: devolución

        /// <summary>
        /// D4. Pendiente: bloquea el reembolso en efectivo (nada se cobró). Acreditado: permitido. Rechazado: sin reembolso.
        /// Ventas que no son de cheque, o sin ingreso de cheque, no se ven afectadas.
        /// </summary>
        private async Task ValidarReembolsoContraChequeAsync(int ventaId, string ventaNumero)
        {
            var cheque = await _context.MovimientosCaja.AsNoTracking()
                .Where(m => m.VentaId == ventaId && !m.IsDeleted
                         && m.Tipo == TipoMovimientoCaja.Ingreso
                         && m.Concepto == ConceptoMovimientoCaja.VentaCheque)
                .OrderBy(m => m.Id)
                .FirstOrDefaultAsync();
            if (cheque == null) return;

            if (await ChequeRechazadoAsync(cheque))
            {
                throw new InvalidOperationException(
                    $"No se puede reembolsar en efectivo la venta {ventaNumero}: el cheque fue rechazado por el banco y nunca se cobró.");
            }

            if (cheque.EstadoAcreditacion == EstadoAcreditacionMovimientoCaja.Pendiente)
            {
                throw new InvalidOperationException(
                    $"No se puede reembolsar en efectivo la venta {ventaNumero}: el cheque todavía no fue acreditado. Acreditalo antes de devolver el dinero.");
            }
        }

        // ------------------------------------------------------------------ consultas

        /// <summary>
        /// Ingresos de la apertura cuya acreditación se asentó en OTRA caja (movimiento AcreditacionCheque): el dinero real se
        /// cuenta allá, así que el saldo real histórico de esta caja no cambia.
        /// </summary>
        private async Task<HashSet<int>> ObtenerOrigenesAcreditadosEnOtraCajaAsync(int aperturaId, IEnumerable<MovimientoCaja> movimientos)
        {
            var candidatos = movimientos
                .Where(m => m.Tipo == TipoMovimientoCaja.Ingreso && m.Concepto is ConceptoMovimientoCaja.VentaCheque or ConceptoMovimientoCaja.CobroCuota)
                .Select(m => m.Id)
                .ToList();
            if (candidatos.Count == 0)
                return new HashSet<int>();

            var ids = await _context.MovimientosCaja
                .AsNoTracking()
                .Where(m => m.Concepto == ConceptoMovimientoCaja.AcreditacionCheque
                         && !m.IsDeleted
                         && m.AperturaCajaId != aperturaId
                         && m.MovimientoOrigenId != null
                         && candidatos.Contains(m.MovimientoOrigenId.Value))
                .Select(m => m.MovimientoOrigenId!.Value)
                .ToListAsync();
            return ids.ToHashSet();
        }

        /// <inheritdoc/>
        public async Task<List<ChequePendienteViewModel>> ObtenerChequesPendientesAsync()
        {
            var hoy = _relojComercial.HoyComercial;

            var filas = await _context.MovimientosCaja
                .AsNoTracking()
                .Where(m => !m.IsDeleted
                         && m.Tipo == TipoMovimientoCaja.Ingreso
                         && m.EstadoAcreditacion == EstadoAcreditacionMovimientoCaja.Pendiente
                         && (m.Concepto == ConceptoMovimientoCaja.VentaCheque
                             || (m.Concepto == ConceptoMovimientoCaja.CobroCuota && m.TipoPago == TipoPago.Cheque)))
                .Select(m => new
                {
                    m.Id, m.AperturaCajaId, Cerrada = m.AperturaCaja.Cerrada, m.VentaId, m.Referencia, m.Concepto, m.Monto,
                    m.MedioPagoDetalle, m.Observaciones, m.FechaMovimiento
                })
                .ToListAsync();

            var ventaIds = filas.Where(f => f.VentaId.HasValue).Select(f => f.VentaId!.Value).Distinct().ToList();
            var datos = ventaIds.Count == 0
                ? new Dictionary<int, DatosCheque>()
                : await _context.DatosCheque.AsNoTracking()
                    .Where(d => ventaIds.Contains(d.VentaId) && !d.IsDeleted)
                    .ToDictionaryAsync(d => d.VentaId);

            var lista = filas.Select(f =>
            {
                datos.TryGetValue(f.VentaId ?? 0, out var d);
                int? dias = d == null ? null : (DateOnly.FromDateTime(d.FechaVencimiento).DayNumber - hoy.DayNumber);
                return new ChequePendienteViewModel
                {
                    MovimientoId = f.Id,
                    AperturaId = f.AperturaCajaId,
                    AperturaCerrada = f.Cerrada,
                    VentaId = f.VentaId,
                    Referencia = f.Referencia,
                    Origen = f.Concepto == ConceptoMovimientoCaja.CobroCuota ? "Cuota" : "Venta",
                    Banco = d?.Banco,
                    NumeroCheque = d?.NumeroCheque,
                    Titular = d?.Titular,
                    FechaVencimiento = d?.FechaVencimiento,
                    Monto = f.Monto,
                    Detalle = d == null ? f.MedioPagoDetalle : null,
                    DiasParaVencer = dias,
                    Vencido = dias is < 0,
                    PorVencer = dias is >= 0 and <= DiasAvisoChequePorVencer
                };
            }).ToList();

            return lista
                .OrderBy(c => c.FechaVencimiento.HasValue ? 0 : 1)
                .ThenBy(c => c.FechaVencimiento)
                .ThenBy(c => c.MovimientoId)
                .ToList();
        }

        /// <inheritdoc/>
        public async Task<ChequeVentaEstadoViewModel?> ObtenerEstadoChequeVentaAsync(int ventaId)
        {
            var d = await _context.DatosCheque.AsNoTracking()
                .FirstOrDefaultAsync(x => x.VentaId == ventaId && !x.IsDeleted);
            if (d == null) return null;

            var mov = await _context.MovimientosCaja.AsNoTracking()
                .Where(IngresoVentaCaja.Directo(ventaId))
                .Where(m => m.Concepto == ConceptoMovimientoCaja.VentaCheque)
                .OrderBy(m => m.Id)
                .Select(m => new { m.Id, m.AperturaCajaId, m.EstadoAcreditacion })
                .FirstOrDefaultAsync();

            var vm = new ChequeVentaEstadoViewModel
            {
                NumeroCheque = d.NumeroCheque, Banco = d.Banco, Titular = d.Titular, CUIT = d.CUIT,
                FechaEmision = d.FechaEmision, FechaVencimiento = d.FechaVencimiento, Monto = d.Monto,
                Observaciones = d.Observaciones, MovimientoId = mov?.Id, AperturaId = mov?.AperturaCajaId,
                AcreditadoAt = d.AcreditadoAt, AcreditadoPor = d.AcreditadoPor,
                RechazadoAt = d.RechazadoAt, RechazadoPor = d.RechazadoPor, MotivoRechazo = d.MotivoRechazo
            };

            vm.Estado = d.RechazadoAt.HasValue
                ? "Rechazado"
                : mov == null
                    ? "Sin cobrar"
                    : mov.EstadoAcreditacion switch
                    {
                        EstadoAcreditacionMovimientoCaja.Pendiente => "Pendiente de acreditar",
                        EstadoAcreditacionMovimientoCaja.Acreditado => "Acreditado",
                        EstadoAcreditacionMovimientoCaja.Anulado => "Anulado",
                        EstadoAcreditacionMovimientoCaja.Rechazado => "Rechazado",
                        _ => "Sin cobrar"
                    };
            return vm;
        }
    }
}
