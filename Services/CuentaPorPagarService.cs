using System.Data;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Serializa las operaciones del libro de cuentas por pagar POR PROVEEDOR: toma el lock de actualización de la
    /// fila de <c>Proveedores</c> al inicio de cada transacción. Todas las lecturas de saldo posteriores ocurren ya
    /// bajo ese lock, de modo que dos pagos simultáneos al mismo proveedor no ven el mismo saldo.
    /// SQL Server: <c>UPDLOCK, HOLDLOCK, ROWLOCK</c> sobre la PK; otros proveedores (SQLite de tests): no-op,
    /// porque un único escritor ya serializa todo (mismo criterio que <c>CreditoConcurrencia</c>).
    /// </summary>
    internal static class ProveedorConcurrencia
    {
        public static async Task BloquearAsync(AppDbContext context, int proveedorId, CancellationToken cancellationToken = default)
        {
            if (!context.Database.IsSqlServer())
                return;

            await context.Database
                .SqlQuery<int>($"SELECT [Id] AS [Value] FROM [Proveedores] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {proveedorId}")
                .ToListAsync(cancellationToken);
        }
    }

    /// <inheritdoc cref="ICuentaPorPagarService"/>
    public partial class CuentaPorPagarService : ICuentaPorPagarService
    {
        /// <summary>Tolerancia con la que Neto + IVA debe igualar al Total informado por el proveedor.</summary>
        public const decimal ToleranciaTotal = 0.01m;

        private const decimal ImporteMaximo = 9_999_999_999_999m;

        private readonly AppDbContext _context;
        private readonly IRelojComercial _reloj;
        private readonly ILogger<CuentaPorPagarService> _logger;
        private readonly ISeguridadAuditoriaService? _auditoria;

        public CuentaPorPagarService(
            AppDbContext context, IRelojComercial reloj, ILogger<CuentaPorPagarService> logger,
            ISeguridadAuditoriaService? auditoria = null)
        {
            _context = context;
            _reloj = reloj;
            _logger = logger;
            _auditoria = auditoria;
        }

        /// <summary>
        /// Auditoría de seguridad de cada operación que cambia el libro. Se agrega al contexto ANTES del SaveChanges de la operación,
        /// de modo que el evento y el cambio se confirman (o se revierten) juntos en la misma transacción.
        /// </summary>
        private void Auditar(string accion, string entidad, string detalle) =>
            _auditoria?.AgregarEvento("CuentasPorPagar", accion, entidad, detalle);

        // =====================================================================================
        // Escrituras
        // =====================================================================================

        public async Task<ResultadoCxp> RegistrarComprobanteAsync(ComprobanteProveedorRequest request, string usuario)
        {
            if (request is null)
                return ResultadoCxp.Falla("Datos inválidos.");

            var numero = (request.NumeroComprobante ?? string.Empty).Trim().ToUpperInvariant();
            if (numero.Length == 0)
                return ResultadoCxp.Falla("El número de comprobante es obligatorio.");
            if (numero.Length > 50)
                return ResultadoCxp.Falla("El número de comprobante no puede superar los 50 caracteres.");
            if (!Enum.IsDefined(request.Tipo))
                return ResultadoCxp.Falla("El tipo de comprobante no es válido.");
            if (request.FechaEmision is null || request.FechaEmision.Value.Year < 2000)
                return ResultadoCxp.Falla("La fecha de emisión es obligatoria.");

            var emision = request.FechaEmision.Value.Date;
            var vencimiento = request.FechaVencimiento?.Date;
            if (vencimiento.HasValue && vencimiento.Value < emision)
                return ResultadoCxp.Falla("La fecha de vencimiento no puede ser anterior a la de emisión.");

            foreach (var (valor, nombre) in new[]
                     {
                         (request.ImporteNeto, "El importe neto"),
                         (request.ImporteIva, "El importe de IVA"),
                         (request.ImporteTotal, "El importe total")
                     })
            {
                if (valor < 0)
                    return ResultadoCxp.Falla($"{nombre} no puede ser negativo.");
                if (!TieneMaximoDosDecimales(valor) || valor > ImporteMaximo)
                    return ResultadoCxp.Falla($"{nombre} no es válido (máximo 2 decimales).");
            }

            if (request.ImporteTotal <= 0)
                return ResultadoCxp.Falla("El importe total debe ser mayor que cero.");

            if (Math.Abs(request.ImporteNeto + request.ImporteIva - request.ImporteTotal) > ToleranciaTotal)
                return ResultadoCxp.Falla("El importe neto más el IVA no coincide con el total informado (tolerancia de 0,01).");

            var observaciones = RecortarOpcional(request.Observaciones, 1000);

            return await EnTransaccionAsync(request.ProveedorId, async () =>
            {
                if (!await _context.Proveedores.AnyAsync(p => p.Id == request.ProveedorId && !p.IsDeleted))
                    return ResultadoCxp.Falla("El proveedor no existe.");

                if (request.OrdenCompraId.HasValue)
                {
                    var ocValida = await _context.OrdenesCompra.AnyAsync(o =>
                        o.Id == request.OrdenCompraId.Value && !o.IsDeleted && o.ProveedorId == request.ProveedorId);
                    if (!ocValida)
                        return ResultadoCxp.Falla("La orden de compra no existe o pertenece a otro proveedor.");
                }

                var duplicado = await _context.ComprobantesProveedor.AnyAsync(c =>
                    c.ProveedorId == request.ProveedorId && c.Tipo == request.Tipo &&
                    c.NumeroComprobante == numero && c.Estado != EstadoComprobanteProveedor.Anulado);
                if (duplicado)
                    return ResultadoCxp.Falla("Ya existe un comprobante de ese tipo y número para el proveedor.");

                var comprobante = new ComprobanteProveedor
                {
                    ProveedorId = request.ProveedorId,
                    OrdenCompraId = request.OrdenCompraId,
                    Tipo = request.Tipo,
                    NumeroComprobante = numero,
                    FechaEmision = emision,
                    FechaVencimiento = vencimiento,
                    ImporteNeto = request.ImporteNeto,
                    ImporteIva = request.ImporteIva,
                    ImporteTotal = request.ImporteTotal,
                    SaldoPendiente = request.ImporteTotal,
                    Estado = EstadoComprobanteProveedor.Pendiente,
                    Observaciones = observaciones,
                    CreatedBy = usuario
                };
                _context.ComprobantesProveedor.Add(comprobante);
                Auditar("RegistrarComprobante", $"Proveedor:{comprobante.ProveedorId}",
                    $"{comprobante.Tipo} {comprobante.NumeroComprobante} por {comprobante.ImporteTotal:0.00} por {usuario}");
                await _context.SaveChangesAsync();

                _logger.LogInformation("Comprobante {Id} ({Tipo} {Numero}) registrado para proveedor {ProveedorId} por {Usuario}",
                    comprobante.Id, comprobante.Tipo, comprobante.NumeroComprobante, comprobante.ProveedorId, usuario);
                return ResultadoCxp.Exito(comprobante.Id);
            });
        }

        public async Task<ResultadoCxp> AnularComprobanteAsync(int comprobanteId, string? motivo, string usuario)
        {
            var motivoLimpio = RecortarOpcional(motivo, 500);
            if (motivoLimpio is null)
                return ResultadoCxp.Falla("El motivo de la anulación es obligatorio.");

            var proveedorId = await _context.ComprobantesProveedor.AsNoTracking()
                .Where(c => c.Id == comprobanteId).Select(c => (int?)c.ProveedorId).FirstOrDefaultAsync();
            if (proveedorId is null)
                return ResultadoCxp.Falla("El comprobante no existe.");

            return await EnTransaccionAsync(proveedorId.Value, async () =>
            {
                var comprobante = await _context.ComprobantesProveedor.FirstOrDefaultAsync(c => c.Id == comprobanteId);
                if (comprobante is null)
                    return ResultadoCxp.Falla("El comprobante no existe.");
                if (comprobante.Estado == EstadoComprobanteProveedor.Anulado)
                    return ResultadoCxp.Falla("El comprobante ya está anulado.");

                var aplicado = await CalcularAplicadoAsync(new[] { comprobanteId });
                if (aplicado.GetValueOrDefault(comprobanteId) > 0m)
                    return ResultadoCxp.Falla(
                        "No se puede anular un comprobante con pagos o notas de crédito aplicados. Anulá primero esas imputaciones.");

                Auditar("AnularComprobante", $"Comprobante:{comprobanteId}", $"{comprobante.Tipo} {comprobante.NumeroComprobante} anulado por {usuario}: {motivoLimpio}");
                comprobante.Estado = EstadoComprobanteProveedor.Anulado;
                comprobante.SaldoPendiente = 0m;
                comprobante.AnuladoPor = usuario;
                comprobante.FechaAnulacion = _reloj.AhoraUtc;
                comprobante.MotivoAnulacion = motivoLimpio;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Comprobante {Id} anulado por {Usuario}: {Motivo}", comprobanteId, usuario, motivoLimpio);
                return ResultadoCxp.Exito(comprobanteId);
            });
        }

        public async Task<ResultadoCxp> RegistrarPagoAsync(PagoProveedorRequest request, string usuario)
        {
            if (request is null)
                return ResultadoCxp.Falla("Datos inválidos.");
            if (!Enum.IsDefined(request.Medio))
                return ResultadoCxp.Falla("El medio de pago no es válido.");
            if (request.Fecha is null || request.Fecha.Value.Year < 2000)
                return ResultadoCxp.Falla("La fecha del pago es obligatoria.");
            if (request.Monto <= 0 || !TieneMaximoDosDecimales(request.Monto) || request.Monto > ImporteMaximo)
                return ResultadoCxp.Falla("El monto del pago debe ser mayor que cero (máximo 2 decimales).");
            if (request.ChequeId.HasValue && request.Medio != MedioPagoProveedor.Cheque)
                return ResultadoCxp.Falla("Solo un pago con medio Cheque puede referenciar un cheque.");
            if (request.OrigenFondosId is null or <= 0)
                return ResultadoCxp.Falla("Elegí el origen de los fondos del pago.");

            var (imputaciones, errorImputaciones) = NormalizarImputaciones(request.Imputaciones);
            if (errorImputaciones is not null)
                return ResultadoCxp.Falla(errorImputaciones);
            if (imputaciones.Count == 0)
                return ResultadoCxp.Falla("Imputá el pago a al menos un comprobante.");

            var suma = imputaciones.Sum(i => i.Monto);
            if (suma > request.Monto)
                return ResultadoCxp.Falla("Las imputaciones superan el monto del pago.");
            if (suma < request.Monto)
                return ResultadoCxp.Falla("El pago debe imputarse por completo: las imputaciones no alcanzan el monto del pago.");

            var clave = RecortarOpcional(request.ClaveIdempotencia, 64);
            var referencia = RecortarOpcional(request.Referencia, 100);
            var observaciones = RecortarOpcional(request.Observaciones, 1000);
            var fecha = request.Fecha.Value.Date;

            return await EnTransaccionAsync(request.ProveedorId, async () =>
            {
                if (clave is not null)
                {
                    var existente = await _context.PagosProveedor.AsNoTracking()
                        .Where(p => p.ProveedorId == request.ProveedorId && p.ClaveIdempotencia == clave)
                        .Select(p => (int?)p.Id).FirstOrDefaultAsync();
                    if (existente.HasValue)
                        return ResultadoCxp.Exito(existente, yaRegistrado: true);
                }

                if (!await _context.Proveedores.AnyAsync(p => p.Id == request.ProveedorId && !p.IsDeleted))
                    return ResultadoCxp.Falla("El proveedor no existe.");

                var errorOrigen = await ValidarOrigenFondosAsync(request.OrigenFondosId!.Value);
                if (errorOrigen is not null)
                    return ResultadoCxp.Falla(errorOrigen);

                if (request.ChequeId.HasValue)
                {
                    var errorCheque = await ValidarChequeParaEgresoAsync(request.ChequeId.Value, request.ProveedorId, request.Monto, "pago");
                    if (errorCheque is not null)
                        return ResultadoCxp.Falla(errorCheque);
                }

                var ids = imputaciones.Select(i => i.ComprobanteId).ToList();
                var comprobantes = await _context.ComprobantesProveedor.AsNoTracking()
                    .Where(c => ids.Contains(c.Id)).ToListAsync();
                if (comprobantes.Count != ids.Count)
                    return ResultadoCxp.Falla("Algún comprobante no existe.");

                var aplicado = await CalcularAplicadoAsync(ids);
                foreach (var imp in imputaciones)
                {
                    var c = comprobantes.Single(x => x.Id == imp.ComprobanteId);
                    if (c.ProveedorId != request.ProveedorId)
                        return ResultadoCxp.Falla($"El comprobante {c.NumeroComprobante} pertenece a otro proveedor.");
                    if (c.Tipo == TipoComprobanteProveedor.NotaCredito)
                        return ResultadoCxp.Falla($"El comprobante {c.NumeroComprobante} es una nota de crédito: no se paga, se aplica a facturas.");
                    if (c.Estado == EstadoComprobanteProveedor.Anulado)
                        return ResultadoCxp.Falla($"El comprobante {c.NumeroComprobante} está anulado.");

                    var saldo = c.ImporteTotal - aplicado.GetValueOrDefault(c.Id);
                    if (imp.Monto > saldo)
                        return ResultadoCxp.Falla(
                            $"La imputación al comprobante {c.NumeroComprobante} supera su saldo pendiente ({saldo:N2}).");
                }

                var pago = new PagoProveedor
                {
                    ProveedorId = request.ProveedorId,
                    Fecha = fecha,
                    Medio = request.Medio,
                    Monto = request.Monto,
                    Referencia = referencia,
                    ChequeId = request.ChequeId,
                    OrigenFondosId = request.OrigenFondosId,
                    Observaciones = observaciones,
                    ClaveIdempotencia = clave,
                    CreatedBy = usuario
                };
                foreach (var imp in imputaciones)
                {
                    pago.Imputaciones.Add(new PagoProveedorImputacion
                    {
                        ComprobanteProveedorId = imp.ComprobanteId,
                        Monto = imp.Monto,
                        CreatedBy = usuario
                    });
                }
                _context.PagosProveedor.Add(pago);
                Auditar("RegistrarPago", $"Proveedor:{pago.ProveedorId}",
                    $"Pago {pago.Medio} por {pago.Monto:0.00}; origen {pago.OrigenFondosId}; cheque {pago.ChequeId?.ToString() ?? "-"}; comprobantes {string.Join(",", ids)}; por {usuario}");
                await _context.SaveChangesAsync();

                await RecalcularAsync(ids);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Pago {Id} por {Monto} registrado para proveedor {ProveedorId} por {Usuario}",
                    pago.Id, pago.Monto, pago.ProveedorId, usuario);
                return ResultadoCxp.Exito(pago.Id);
            });
        }

        public async Task<ResultadoCxp> AnularPagoAsync(int pagoId, string? motivo, string usuario)
        {
            var motivoLimpio = RecortarOpcional(motivo, 500);
            if (motivoLimpio is null)
                return ResultadoCxp.Falla("El motivo de la anulación es obligatorio.");

            var proveedorId = await _context.PagosProveedor.AsNoTracking()
                .Where(p => p.Id == pagoId).Select(p => (int?)p.ProveedorId).FirstOrDefaultAsync();
            if (proveedorId is null)
                return ResultadoCxp.Falla("El pago no existe.");

            return await EnTransaccionAsync(proveedorId.Value, async () =>
            {
                if (!await _context.Proveedores.AnyAsync(p => p.Id == proveedorId.Value && !p.IsDeleted))
                    return ResultadoCxp.Falla("El proveedor está dado de baja: la anulación reabriría deuda o saldo a favor sobre un proveedor eliminado.");
                var pago = await _context.PagosProveedor.FirstOrDefaultAsync(p => p.Id == pagoId);
                if (pago is null)
                    return ResultadoCxp.Falla("El pago no existe.");
                if (pago.Anulado)
                    return ResultadoCxp.Falla("El pago ya está anulado.");

                pago.Anulado = true;
                pago.AnuladoPor = usuario;
                pago.FechaAnulacion = _reloj.AhoraUtc;
                pago.MotivoAnulacion = motivoLimpio;
                Auditar("AnularPago", $"Pago:{pagoId}", $"Pago por {pago.Monto:0.00} anulado por {usuario}: {motivoLimpio}");
                await _context.SaveChangesAsync();

                var ids = await _context.PagoProveedorImputaciones.AsNoTracking()
                    .Where(i => i.PagoProveedorId == pagoId).Select(i => i.ComprobanteProveedorId).ToListAsync();
                await RecalcularAsync(ids);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Pago {Id} anulado por {Usuario}: {Motivo}", pagoId, usuario, motivoLimpio);
                return ResultadoCxp.Exito(pagoId);
            });
        }

        public async Task<ResultadoCxp> ImputarNotaCreditoAsync(
            int notaCreditoId, IReadOnlyList<ImputacionRequest> imputacionesRequest, string? claveIdempotencia, string usuario)
        {
            var (imputaciones, errorImputaciones) = NormalizarImputaciones(imputacionesRequest);
            if (errorImputaciones is not null)
                return ResultadoCxp.Falla(errorImputaciones);
            if (imputaciones.Count == 0)
                return ResultadoCxp.Falla("Indicá a qué facturas se aplica la nota de crédito.");

            var clave = RecortarOpcional(claveIdempotencia, 64);

            var proveedorId = await _context.ComprobantesProveedor.AsNoTracking()
                .Where(c => c.Id == notaCreditoId).Select(c => (int?)c.ProveedorId).FirstOrDefaultAsync();
            if (proveedorId is null)
                return ResultadoCxp.Falla("La nota de crédito no existe.");

            return await EnTransaccionAsync(proveedorId.Value, async () =>
            {
                if (clave is not null)
                {
                    var repetida = await _context.ImputacionesNotaCredito.AsNoTracking()
                        .AnyAsync(i => i.NotaCreditoId == notaCreditoId && i.ClaveIdempotencia == clave);
                    if (repetida)
                        return ResultadoCxp.Exito(notaCreditoId, yaRegistrado: true);
                }

                var nc = await _context.ComprobantesProveedor.AsNoTracking().FirstOrDefaultAsync(c => c.Id == notaCreditoId);
                if (nc is null || nc.Tipo != TipoComprobanteProveedor.NotaCredito)
                    return ResultadoCxp.Falla("El comprobante indicado no es una nota de crédito.");
                if (nc.Estado == EstadoComprobanteProveedor.Anulado)
                    return ResultadoCxp.Falla("La nota de crédito está anulada.");

                var ids = imputaciones.Select(i => i.ComprobanteId).ToList();
                if (ids.Contains(notaCreditoId))
                    return ResultadoCxp.Falla("Una nota de crédito no se puede aplicar a sí misma.");

                var destinos = await _context.ComprobantesProveedor.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync();
                if (destinos.Count != ids.Count)
                    return ResultadoCxp.Falla("Alguna factura indicada no existe.");

                var aplicado = await CalcularAplicadoAsync(ids.Append(notaCreditoId).ToList());
                var saldoNc = nc.ImporteTotal - aplicado.GetValueOrDefault(notaCreditoId);

                foreach (var imp in imputaciones)
                {
                    var d = destinos.Single(x => x.Id == imp.ComprobanteId);
                    if (d.ProveedorId != nc.ProveedorId)
                        return ResultadoCxp.Falla($"El comprobante {d.NumeroComprobante} pertenece a otro proveedor.");
                    if (d.Tipo == TipoComprobanteProveedor.NotaCredito)
                        return ResultadoCxp.Falla($"El comprobante {d.NumeroComprobante} es una nota de crédito: solo se aplica a facturas o notas de débito.");
                    if (d.Estado == EstadoComprobanteProveedor.Anulado)
                        return ResultadoCxp.Falla($"El comprobante {d.NumeroComprobante} está anulado.");

                    var saldoDestino = d.ImporteTotal - aplicado.GetValueOrDefault(d.Id);
                    if (imp.Monto > saldoDestino)
                        return ResultadoCxp.Falla(
                            $"La aplicación al comprobante {d.NumeroComprobante} supera su saldo pendiente ({saldoDestino:N2}).");
                }

                var total = imputaciones.Sum(i => i.Monto);
                if (total > saldoNc)
                    return ResultadoCxp.Falla($"La aplicación supera el saldo de la nota de crédito ({saldoNc:N2}).");

                foreach (var imp in imputaciones)
                {
                    _context.ImputacionesNotaCredito.Add(new ImputacionNotaCredito
                    {
                        NotaCreditoId = notaCreditoId,
                        ComprobanteId = imp.ComprobanteId,
                        Monto = imp.Monto,
                        ClaveIdempotencia = clave,
                        CreatedBy = usuario
                    });
                }
                Auditar("AplicarNotaCredito", $"Comprobante:{notaCreditoId}",
                    $"Nota de crédito aplicada a {string.Join(",", ids)} por {total:0.00} por {usuario}");
                await _context.SaveChangesAsync();

                await RecalcularAsync(ids.Append(notaCreditoId).ToList());
                await _context.SaveChangesAsync();

                _logger.LogInformation("Nota de crédito {Id} aplicada a {Cantidad} comprobante(s) por {Usuario}",
                    notaCreditoId, imputaciones.Count, usuario);
                return ResultadoCxp.Exito(notaCreditoId);
            });
        }

        public async Task<ResultadoCxp> AnularImputacionNotaCreditoAsync(int imputacionId, string? motivo, string usuario)
        {
            var motivoLimpio = RecortarOpcional(motivo, 500);
            if (motivoLimpio is null)
                return ResultadoCxp.Falla("El motivo de la anulación es obligatorio.");

            var origen = await _context.ImputacionesNotaCredito.AsNoTracking()
                .Where(i => i.Id == imputacionId)
                .Select(i => new { i.NotaCredito.ProveedorId })
                .FirstOrDefaultAsync();
            if (origen is null)
                return ResultadoCxp.Falla("La aplicación de nota de crédito no existe.");

            return await EnTransaccionAsync(origen.ProveedorId, async () =>
            {
                if (!await _context.Proveedores.AnyAsync(p => p.Id == origen.ProveedorId && !p.IsDeleted))
                    return ResultadoCxp.Falla("El proveedor está dado de baja: la anulación reabriría deuda o saldo a favor sobre un proveedor eliminado.");
                var imp = await _context.ImputacionesNotaCredito.FirstOrDefaultAsync(i => i.Id == imputacionId);
                if (imp is null)
                    return ResultadoCxp.Falla("La aplicación de nota de crédito no existe.");
                if (imp.Anulada)
                    return ResultadoCxp.Falla("La aplicación ya está anulada.");

                imp.Anulada = true;
                imp.AnuladaPor = usuario;
                imp.FechaAnulacion = _reloj.AhoraUtc;
                imp.MotivoAnulacion = motivoLimpio;
                Auditar("AnularAplicacionNotaCredito", $"Comprobante:{imp.NotaCreditoId}", $"Aplicación {imputacionId} anulada por {usuario}: {motivoLimpio}");
                await _context.SaveChangesAsync();

                await RecalcularAsync(new[] { imp.NotaCreditoId, imp.ComprobanteId });
                await _context.SaveChangesAsync();

                _logger.LogInformation("Aplicación de nota de crédito {Id} anulada por {Usuario}: {Motivo}", imputacionId, usuario, motivoLimpio);
                return ResultadoCxp.Exito(imputacionId);
            });
        }

        public async Task<(int NotaCreditoId, int ComprobanteId)?> ObtenerComprobantesDeImputacionAsync(int imputacionId)
        {
            var fila = await _context.ImputacionesNotaCredito.AsNoTracking()
                .Where(i => i.Id == imputacionId)
                .Select(i => new { i.NotaCreditoId, i.ComprobanteId })
                .FirstOrDefaultAsync();
            return fila is null ? null : (fila.NotaCreditoId, fila.ComprobanteId);
        }

        // =====================================================================================
        // Consultas
        // =====================================================================================

        public async Task<ListadoComprobantesProveedor> ListarComprobantesAsync(FiltroComprobantesProveedor filtro)
        {
            filtro ??= new FiltroComprobantesProveedor();
            var inicioHoy = _reloj.InicioDiaComercial;
            var tamano = Math.Clamp(filtro.TamanoPagina <= 0 ? 25 : filtro.TamanoPagina, 1, 200);

            var query = _context.ComprobantesProveedor.AsNoTracking().AsQueryable();
            if (filtro.ProveedorId.HasValue)
                query = query.Where(c => c.ProveedorId == filtro.ProveedorId.Value);
            if (filtro.Estado.HasValue)
                query = query.Where(c => c.Estado == filtro.Estado.Value);
            if (filtro.Tipo.HasValue)
                query = query.Where(c => c.Tipo == filtro.Tipo.Value);
            if (filtro.SoloVencidos)
                query = query.Where(c => c.Tipo != TipoComprobanteProveedor.NotaCredito
                    && (c.Estado == EstadoComprobanteProveedor.Pendiente || c.Estado == EstadoComprobanteProveedor.ParcialmentePagado)
                    && c.FechaVencimiento != null && c.FechaVencimiento < inicioHoy);
            if (filtro.VencimientoDesde.HasValue)
            {
                var desde = filtro.VencimientoDesde.Value.Date;
                query = query.Where(c => c.FechaVencimiento != null && c.FechaVencimiento >= desde);
            }
            if (filtro.VencimientoHasta.HasValue)
            {
                var hasta = filtro.VencimientoHasta.Value.Date;
                query = query.Where(c => c.FechaVencimiento != null && c.FechaVencimiento <= hasta);
            }
            var busqueda = filtro.Busqueda?.Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(busqueda))
                query = query.Where(c => c.NumeroComprobante.Contains(busqueda));

            var total = await query.CountAsync();
            var saldos = await query
                .Where(c => c.Tipo != TipoComprobanteProveedor.NotaCredito && c.Estado != EstadoComprobanteProveedor.Anulado)
                .Select(c => c.SaldoPendiente).ToListAsync();

            var paginas = Math.Max(1, (int)Math.Ceiling(total / (double)tamano));
            var pagina = Math.Clamp(filtro.Pagina, 1, paginas);

            var filas = await query
                .OrderByDescending(c => c.FechaEmision).ThenByDescending(c => c.Id)
                .Skip((pagina - 1) * tamano).Take(tamano)
                .Select(c => new
                {
                    c.Id, c.ProveedorId, Proveedor = c.Proveedor.RazonSocial, c.Tipo, c.NumeroComprobante,
                    c.FechaEmision, c.FechaVencimiento, c.ImporteTotal, c.SaldoPendiente, c.Estado
                })
                .ToListAsync();

            return new ListadoComprobantesProveedor
            {
                Total = total,
                Pagina = pagina,
                TamanoPagina = tamano,
                SaldoDeuda = saldos.Sum(),
                Items = filas.Select(f => ToItem(f.Id, f.ProveedorId, f.Proveedor, f.Tipo, f.NumeroComprobante,
                    f.FechaEmision, f.FechaVencimiento, f.ImporteTotal, f.SaldoPendiente, f.Estado, inicioHoy)).ToList()
            };
        }

        public async Task<ComprobanteProveedorDetalle?> ObtenerComprobanteAsync(int comprobanteId)
        {
            var inicioHoy = _reloj.InicioDiaComercial;
            var c = await _context.ComprobantesProveedor.AsNoTracking()
                .Include(x => x.Proveedor).Include(x => x.OrdenCompra)
                .FirstOrDefaultAsync(x => x.Id == comprobanteId);
            if (c is null)
                return null;

            var detalle = new ComprobanteProveedorDetalle
            {
                Cabecera = ToItem(c.Id, c.ProveedorId, c.Proveedor.RazonSocial, c.Tipo, c.NumeroComprobante,
                    c.FechaEmision, c.FechaVencimiento, c.ImporteTotal, c.SaldoPendiente, c.Estado, inicioHoy),
                ImporteNeto = c.ImporteNeto,
                ImporteIva = c.ImporteIva,
                OrdenCompraId = c.OrdenCompraId,
                OrdenCompraNumero = c.OrdenCompra?.Numero,
                Observaciones = c.Observaciones,
                CreadoPor = c.CreatedBy,
                CreadoEn = c.CreatedAt,
                AnuladoPor = c.AnuladoPor,
                FechaAnulacion = c.FechaAnulacion,
                MotivoAnulacion = c.MotivoAnulacion
            };

            detalle.Pagos = await _context.PagoProveedorImputaciones.AsNoTracking()
                .Where(i => i.ComprobanteProveedorId == comprobanteId)
                .OrderBy(i => i.Pago.Fecha).ThenBy(i => i.Id)
                .Select(i => new ImputacionPagoItem
                {
                    PagoId = i.PagoProveedorId, ComprobanteId = i.ComprobanteProveedorId,
                    ComprobanteNumero = i.Comprobante.NumeroComprobante, FechaPago = i.Pago.Fecha,
                    Medio = i.Pago.Medio, Monto = i.Monto, PagoAnulado = i.Pago.Anulado
                }).ToListAsync();

            detalle.NotasCredito = await _context.ImputacionesNotaCredito.AsNoTracking()
                .Where(i => i.ComprobanteId == comprobanteId || i.NotaCreditoId == comprobanteId)
                .OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
                .Select(i => new ImputacionNotaCreditoItem
                {
                    Id = i.Id, NotaCreditoId = i.NotaCreditoId, NotaCreditoNumero = i.NotaCredito.NumeroComprobante,
                    ComprobanteId = i.ComprobanteId, ComprobanteNumero = i.Comprobante.NumeroComprobante,
                    Monto = i.Monto, Fecha = i.CreatedAt, Anulada = i.Anulada, MotivoAnulacion = i.MotivoAnulacion
                }).ToListAsync();

            return detalle;
        }

        public async Task<List<ComprobanteProveedorItem>> ObtenerPendientesAsync(int proveedorId)
        {
            var inicioHoy = _reloj.InicioDiaComercial;
            var filas = await _context.ComprobantesProveedor.AsNoTracking()
                .Where(c => c.ProveedorId == proveedorId && c.Tipo != TipoComprobanteProveedor.NotaCredito
                    && (c.Estado == EstadoComprobanteProveedor.Pendiente || c.Estado == EstadoComprobanteProveedor.ParcialmentePagado))
                .Select(c => new
                {
                    c.Id, c.ProveedorId, Proveedor = c.Proveedor.RazonSocial, c.Tipo, c.NumeroComprobante,
                    c.FechaEmision, c.FechaVencimiento, c.ImporteTotal, c.SaldoPendiente, c.Estado
                }).ToListAsync();

            return filas
                .Select(f => ToItem(f.Id, f.ProveedorId, f.Proveedor, f.Tipo, f.NumeroComprobante,
                    f.FechaEmision, f.FechaVencimiento, f.ImporteTotal, f.SaldoPendiente, f.Estado, inicioHoy))
                .OrderBy(i => i.FechaVencimiento ?? DateTime.MaxValue).ThenBy(i => i.FechaEmision).ThenBy(i => i.Id)
                .ToList();
        }

        public async Task<List<ComprobanteProveedorItem>> ObtenerNotasCreditoConSaldoAsync(int proveedorId)
        {
            var inicioHoy = _reloj.InicioDiaComercial;
            var filas = await _context.ComprobantesProveedor.AsNoTracking()
                .Where(c => c.ProveedorId == proveedorId && c.Tipo == TipoComprobanteProveedor.NotaCredito
                    && (c.Estado == EstadoComprobanteProveedor.Pendiente || c.Estado == EstadoComprobanteProveedor.ParcialmentePagado))
                .OrderBy(c => c.FechaEmision).ThenBy(c => c.Id)
                .Select(c => new
                {
                    c.Id, c.ProveedorId, Proveedor = c.Proveedor.RazonSocial, c.Tipo, c.NumeroComprobante,
                    c.FechaEmision, c.FechaVencimiento, c.ImporteTotal, c.SaldoPendiente, c.Estado
                }).ToListAsync();

            return filas.Select(f => ToItem(f.Id, f.ProveedorId, f.Proveedor, f.Tipo, f.NumeroComprobante,
                f.FechaEmision, f.FechaVencimiento, f.ImporteTotal, f.SaldoPendiente, f.Estado, inicioHoy)).ToList();
        }

        public async Task<PagoProveedorItem?> ObtenerPagoAsync(int pagoId)
        {
            var p = await _context.PagosProveedor.AsNoTracking()
                .Include(x => x.Proveedor).Include(x => x.Cheque).Include(x => x.OrigenFondos)
                .FirstOrDefaultAsync(x => x.Id == pagoId);
            return p is null ? null : await ToPagoItemAsync(p);
        }

        public async Task<List<PagoProveedorItem>> ListarPagosAsync(int proveedorId)
        {
            var pagos = await _context.PagosProveedor.AsNoTracking()
                .Include(x => x.Proveedor).Include(x => x.Cheque).Include(x => x.OrigenFondos)
                .Where(p => p.ProveedorId == proveedorId)
                .OrderByDescending(p => p.Fecha).ThenByDescending(p => p.Id)
                .ToListAsync();
            var lista = new List<PagoProveedorItem>();
            foreach (var p in pagos)
                lista.Add(await ToPagoItemAsync(p));
            return lista;
        }

        public async Task<EstadoCuentaProveedor?> ObtenerEstadoCuentaAsync(int proveedorId)
        {
            var proveedor = await _context.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.Id == proveedorId);
            if (proveedor is null)
                return null;

            var comprobantes = await _context.ComprobantesProveedor.AsNoTracking()
                .Where(c => c.ProveedorId == proveedorId).ToListAsync();
            var pagos = await _context.PagosProveedor.AsNoTracking()
                .Where(p => p.ProveedorId == proveedorId).ToListAsync();

            var anticipos = await _context.AnticiposProveedor.AsNoTracking()
                .Where(a => a.ProveedorId == proveedorId).ToListAsync();

            var movimientos = new List<MovimientoCuentaPorPagar>();
            foreach (var c in comprobantes)
            {
                var esNc = c.Tipo == TipoComprobanteProveedor.NotaCredito;
                movimientos.Add(new MovimientoCuentaPorPagar
                {
                    Fecha = c.FechaEmision,
                    Tipo = c.Tipo switch
                    {
                        TipoComprobanteProveedor.NotaCredito => TipoMovimientoCuentaPorPagar.NotaCredito,
                        TipoComprobanteProveedor.NotaDebito => TipoMovimientoCuentaPorPagar.NotaDebito,
                        _ => TipoMovimientoCuentaPorPagar.Factura
                    },
                    Descripcion = $"{NombreTipo(c.Tipo)} {c.NumeroComprobante}",
                    Debe = esNc ? 0m : c.ImporteTotal,
                    Haber = esNc ? c.ImporteTotal : 0m,
                    ComprobanteId = c.Id
                });
                if (c.Estado == EstadoComprobanteProveedor.Anulado)
                {
                    movimientos.Add(new MovimientoCuentaPorPagar
                    {
                        Fecha = (c.FechaAnulacion ?? c.FechaEmision).Date,
                        Tipo = TipoMovimientoCuentaPorPagar.AnulacionComprobante,
                        Descripcion = $"Anulación de {NombreTipo(c.Tipo).ToLowerInvariant()} {c.NumeroComprobante}",
                        Debe = esNc ? c.ImporteTotal : 0m,
                        Haber = esNc ? 0m : c.ImporteTotal,
                        ComprobanteId = c.Id
                    });
                }
            }
            foreach (var p in pagos)
            {
                movimientos.Add(new MovimientoCuentaPorPagar
                {
                    Fecha = p.Fecha,
                    Tipo = TipoMovimientoCuentaPorPagar.Pago,
                    Descripcion = $"Pago {NombreMedio(p.Medio)}" + (string.IsNullOrWhiteSpace(p.Referencia) ? "" : $" ({p.Referencia})"),
                    Haber = p.Monto,
                    PagoId = p.Id
                });
                if (p.Anulado)
                {
                    movimientos.Add(new MovimientoCuentaPorPagar
                    {
                        Fecha = (p.FechaAnulacion ?? p.Fecha).Date,
                        Tipo = TipoMovimientoCuentaPorPagar.AnulacionPago,
                        Descripcion = $"Anulación de pago {NombreMedio(p.Medio)}",
                        Debe = p.Monto,
                        PagoId = p.Id
                    });
                }
            }

            foreach (var a in anticipos)
            {
                movimientos.Add(new MovimientoCuentaPorPagar
                {
                    Fecha = a.Fecha,
                    Tipo = TipoMovimientoCuentaPorPagar.Anticipo,
                    Descripcion = $"Anticipo {NombreMedio(a.Medio)}" + (string.IsNullOrWhiteSpace(a.Referencia) ? "" : $" ({a.Referencia})"),
                    Haber = a.Monto,
                    AnticipoId = a.Id
                });
                if (a.Anulado)
                {
                    movimientos.Add(new MovimientoCuentaPorPagar
                    {
                        Fecha = (a.FechaAnulacion ?? a.Fecha).Date,
                        Tipo = TipoMovimientoCuentaPorPagar.AnulacionAnticipo,
                        Descripcion = $"Anulación de anticipo {NombreMedio(a.Medio)}",
                        Debe = a.Monto,
                        AnticipoId = a.Id
                    });
                }
            }

            // Orden cronológico estable; las anulaciones van después de lo que anulan en el mismo día.
            var ordenados = movimientos
                .OrderBy(m => m.Fecha)
                .ThenBy(m => m.Tipo is TipoMovimientoCuentaPorPagar.AnulacionPago or TipoMovimientoCuentaPorPagar.AnulacionComprobante or TipoMovimientoCuentaPorPagar.AnulacionAnticipo ? 1 : 0)
                .ThenBy(m => m.ComprobanteId ?? int.MaxValue).ThenBy(m => m.PagoId ?? int.MaxValue).ThenBy(m => m.AnticipoId ?? int.MaxValue)
                .ToList();
            var corrido = 0m;
            foreach (var m in ordenados)
            {
                corrido += m.Debe - m.Haber;
                m.SaldoCorrido = corrido;
            }

            var vigentes = comprobantes.Where(c => c.Estado != EstadoComprobanteProveedor.Anulado).ToList();
            return new EstadoCuentaProveedor
            {
                ProveedorId = proveedor.Id,
                ProveedorNombre = proveedor.RazonSocial,
                ProveedorCuit = proveedor.Cuit,
                Movimientos = ordenados,
                SaldoNeto = corrido,
                SaldoComprobantes = vigentes.Where(c => c.Tipo != TipoComprobanteProveedor.NotaCredito).Sum(c => c.SaldoPendiente),
                NotasCreditoSinAplicar = vigentes.Where(c => c.Tipo == TipoComprobanteProveedor.NotaCredito).Sum(c => c.SaldoPendiente),
                AnticiposSinAplicar = await CalcularAnticiposSinAplicarAsync(proveedorId)
            };
        }

        public async Task<AntiguedadDeuda> ObtenerAntiguedadAsync(int? proveedorId = null)
        {
            var inicioHoy = _reloj.InicioDiaComercial;
            var query = _context.ComprobantesProveedor.AsNoTracking()
                .Where(c => c.Tipo != TipoComprobanteProveedor.NotaCredito
                    && (c.Estado == EstadoComprobanteProveedor.Pendiente || c.Estado == EstadoComprobanteProveedor.ParcialmentePagado));
            if (proveedorId.HasValue)
                query = query.Where(c => c.ProveedorId == proveedorId.Value);

            var filas = await query
                .Select(c => new { c.ProveedorId, Proveedor = c.Proveedor.RazonSocial, c.FechaVencimiento, c.SaldoPendiente })
                .ToListAsync();

            var resultado = new AntiguedadDeuda { FechaCorte = inicioHoy };
            foreach (var grupo in filas.GroupBy(f => new { f.ProveedorId, f.Proveedor }).OrderBy(g => g.Key.Proveedor))
            {
                var linea = new AntiguedadDeudaProveedor { ProveedorId = grupo.Key.ProveedorId, ProveedorNombre = grupo.Key.Proveedor };
                foreach (var f in grupo)
                {
                    switch (CubetaAntiguedad(DiasDeAtraso(f.FechaVencimiento, inicioHoy)))
                    {
                        case 0: linea.PorVencer += f.SaldoPendiente; break;
                        case 1: linea.Vencido1a30 += f.SaldoPendiente; break;
                        case 2: linea.Vencido31a60 += f.SaldoPendiente; break;
                        default: linea.VencidoMas60 += f.SaldoPendiente; break;
                    }
                }
                resultado.Proveedores.Add(linea);
            }
            return resultado;
        }

        /// <summary>
        /// Días de atraso al día comercial de hoy: 0 si no vence antes de hoy o no tiene vencimiento
        /// (vence hoy = por vencer; el día siguiente al vencimiento ya cuenta 1 día).
        /// </summary>
        internal static int DiasDeAtraso(DateTime? fechaVencimiento, DateTime inicioHoy) =>
            fechaVencimiento.HasValue && fechaVencimiento.Value.Date < inicioHoy.Date
                ? (int)(inicioHoy.Date - fechaVencimiento.Value.Date).TotalDays
                : 0;

        /// <summary>0 = por vencer, 1 = vencido 1-30, 2 = vencido 31-60, 3 = vencido más de 60 días.</summary>
        internal static int CubetaAntiguedad(int diasAtraso) =>
            diasAtraso <= 0 ? 0 : diasAtraso <= 30 ? 1 : diasAtraso <= 60 ? 2 : 3;

        public async Task<List<ProveedorOpcion>> ObtenerProveedoresAsync() =>
            await _context.Proveedores.AsNoTracking().Where(p => !p.IsDeleted)
                .OrderBy(p => p.RazonSocial).Select(p => new ProveedorOpcion(p.Id, p.RazonSocial)).ToListAsync();

        public async Task<ProveedorOpcion?> ObtenerProveedorAsync(int proveedorId) =>
            await _context.Proveedores.AsNoTracking().Where(p => p.Id == proveedorId && !p.IsDeleted)
                .Select(p => new ProveedorOpcion(p.Id, p.RazonSocial)).FirstOrDefaultAsync();

        public async Task<List<OrdenCompraOpcion>> ObtenerOrdenesCompraAsync(int proveedorId) =>
            await _context.OrdenesCompra.AsNoTracking()
                .Where(o => o.ProveedorId == proveedorId && !o.IsDeleted && o.Estado != EstadoOrdenCompra.Cancelada)
                .OrderByDescending(o => o.FechaEmision).ThenByDescending(o => o.Id)
                .Select(o => new OrdenCompraOpcion(o.Id, o.Numero)).ToListAsync();

        public async Task<int?> ObtenerProveedorDeOrdenCompraAsync(int ordenCompraId) =>
            await _context.OrdenesCompra.AsNoTracking().Where(o => o.Id == ordenCompraId && !o.IsDeleted)
                .Select(o => (int?)o.ProveedorId).FirstOrDefaultAsync();

        public async Task<List<ChequeOpcion>> ObtenerChequesAsync(int proveedorId) =>
            await _context.Cheques.AsNoTracking()
                .Where(c => c.ProveedorId == proveedorId && !c.IsDeleted && c.Estado != EstadoCheque.Anulado && c.Estado != EstadoCheque.Rechazado
                    // Un cheque físico respalda un único pago o anticipo vigente: los ya usados no se ofrecen.
                    && !_context.PagosProveedor.Any(p => p.ChequeId == c.Id && !p.Anulado && !p.IsDeleted)
                    && !_context.AnticiposProveedor.Any(a => a.ChequeId == c.Id && !a.Anulado && !a.IsDeleted))
                .OrderByDescending(c => c.FechaEmision).ThenByDescending(c => c.Id)
                .Select(c => new ChequeOpcion(c.Id, c.Banco + " Nº " + c.Numero + " · $ " + c.Monto.ToString("N2"))).ToListAsync();

        // =====================================================================================
        // Internos
        // =====================================================================================

        /// <summary>
        /// Abre la transacción, toma el lock del proveedor y ejecuta la operación. Cualquier resultado distinto de
        /// éxito, o una excepción de persistencia, revierte todo: nunca queda un pago a medias ni saldos sin recalcular.
        /// </summary>
        private async Task<ResultadoCxp> EnTransaccionAsync(int proveedorId, Func<Task<ResultadoCxp>> operacion)
        {
            await using var transaccion = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                await ProveedorConcurrencia.BloquearAsync(_context, proveedorId);
                var resultado = await operacion();
                if (!resultado.Ok)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    _context.ChangeTracker.Clear();
                    return resultado;
                }

                await transaccion.CommitAsync();
                return resultado;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                _context.ChangeTracker.Clear();
                _logger.LogWarning(ex, "Conflicto de concurrencia en cuentas por pagar (proveedor {ProveedorId})", proveedorId);
                return ResultadoCxp.Falla("Otro usuario modificó estos datos al mismo tiempo. Recargá la pantalla e intentá nuevamente.");
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("Invariante violada", StringComparison.Ordinal))
            {
                // Invariante de saldo (p. ej. saldo negativo): nada se persiste y el detalle técnico queda solo en el log.
                await transaccion.RollbackAsync(CancellationToken.None);
                _context.ChangeTracker.Clear();
                _logger.LogError(ex, "Invariante de saldo violada en cuentas por pagar (proveedor {ProveedorId}); operación revertida", proveedorId);
                return ResultadoCxp.Falla("No se pudo completar la operación porque dejaría saldos inconsistentes. No se registró ningún cambio; recargá la pantalla y avisá a un administrador si persiste.");
            }
            catch (DbUpdateException ex)
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                _context.ChangeTracker.Clear();
                _logger.LogWarning(ex, "No se pudo guardar la operación de cuentas por pagar (proveedor {ProveedorId})", proveedorId);
                return ResultadoCxp.Falla("No se pudo guardar la operación. Puede que el registro ya exista o que otro usuario lo haya modificado; recargá e intentá nuevamente.");
            }
        }

        /// <summary>
        /// Importe aplicado de cada comprobante a partir de las imputaciones vigentes (leídas de la base, no del tracker).
        /// Factura/nota de débito: pagos no anulados + notas de crédito recibidas no anuladas + anticipos aplicados no anulados.
        /// Nota de crédito: sus aplicaciones no anuladas. Los montos se suman en memoria (SQLite no suma decimales).
        /// </summary>
        private async Task<Dictionary<int, decimal>> CalcularAplicadoAsync(IReadOnlyCollection<int> comprobanteIds)
        {
            var ids = comprobanteIds.Distinct().ToList();
            var resultado = ids.ToDictionary(i => i, _ => 0m);

            var pagos = await _context.PagoProveedorImputaciones.AsNoTracking()
                .Where(i => ids.Contains(i.ComprobanteProveedorId) && !i.Pago.Anulado)
                .Select(i => new { Id = i.ComprobanteProveedorId, i.Monto }).ToListAsync();
            foreach (var p in pagos)
                resultado[p.Id] += p.Monto;

            var recibidas = await _context.ImputacionesNotaCredito.AsNoTracking()
                .Where(i => ids.Contains(i.ComprobanteId) && !i.Anulada)
                .Select(i => new { Id = i.ComprobanteId, i.Monto }).ToListAsync();
            foreach (var r in recibidas)
                resultado[r.Id] += r.Monto;

            var emitidas = await _context.ImputacionesNotaCredito.AsNoTracking()
                .Where(i => ids.Contains(i.NotaCreditoId) && !i.Anulada)
                .Select(i => new { Id = i.NotaCreditoId, i.Monto }).ToListAsync();
            foreach (var e in emitidas)
                resultado[e.Id] += e.Monto;

            // Anticipos aplicados a la factura (solo de anticipos no anulados y aplicaciones no anuladas).
            var anticipos = await _context.AnticipoAplicaciones.AsNoTracking()
                .Where(a => ids.Contains(a.ComprobanteProveedorId) && !a.Anulada && !a.Anticipo.Anulado)
                .Select(a => new { Id = a.ComprobanteProveedorId, a.Monto }).ToListAsync();
            foreach (var a in anticipos)
                resultado[a.Id] += a.Monto;

            return resultado;
        }

        /// <summary>Recalcula y deja persistidos (sin guardar todavía) SaldoPendiente y Estado de los comprobantes indicados.</summary>
        private async Task RecalcularAsync(IReadOnlyCollection<int> comprobanteIds)
        {
            var ids = comprobanteIds.Distinct().ToList();
            var aplicado = await CalcularAplicadoAsync(ids);
            var comprobantes = await _context.ComprobantesProveedor.Where(c => ids.Contains(c.Id)).ToListAsync();

            foreach (var c in comprobantes)
            {
                if (c.Estado == EstadoComprobanteProveedor.Anulado)
                    continue;

                var usado = aplicado.GetValueOrDefault(c.Id);
                var saldo = c.ImporteTotal - usado;
                if (saldo < 0m)
                    throw new InvalidOperationException(
                        $"Invariante violada: el comprobante {c.Id} quedaría con saldo negativo ({saldo}).");

                c.SaldoPendiente = saldo;
                c.Estado = saldo == 0m
                    ? EstadoComprobanteProveedor.Pagado
                    : usado == 0m ? EstadoComprobanteProveedor.Pendiente : EstadoComprobanteProveedor.ParcialmentePagado;
            }
        }

        private sealed record ImpNorm(int ComprobanteId, decimal Monto);

        private static (List<ImpNorm> Items, string? Error) NormalizarImputaciones(IReadOnlyCollection<ImputacionRequest>? origen)
        {
            var lista = new List<ImpNorm>();
            if (origen is null)
                return (lista, null);

            foreach (var i in origen)
            {
                if (i is null || i.Monto is null || i.Monto.Value == 0m)
                    continue; // fila sin importe: no participa
                if (i.Monto.Value < 0m)
                    return (lista, "Los importes imputados deben ser mayores que cero.");
                if (!TieneMaximoDosDecimales(i.Monto.Value) || i.Monto.Value > ImporteMaximo)
                    return (lista, "Los importes imputados admiten hasta 2 decimales.");
                lista.Add(new ImpNorm(i.ComprobanteId, i.Monto.Value));
            }

            if (lista.GroupBy(i => i.ComprobanteId).Any(g => g.Count() > 1))
                return (lista, "No se puede imputar dos veces el mismo comprobante en una operación.");

            return (lista, null);
        }

        private async Task<PagoProveedorItem> ToPagoItemAsync(PagoProveedor p)
        {
            var imputaciones = await _context.PagoProveedorImputaciones.AsNoTracking()
                .Where(i => i.PagoProveedorId == p.Id)
                .OrderBy(i => i.Id)
                .Select(i => new ImputacionPagoItem
                {
                    PagoId = i.PagoProveedorId, ComprobanteId = i.ComprobanteProveedorId,
                    ComprobanteNumero = i.Comprobante.NumeroComprobante, FechaPago = p.Fecha,
                    Medio = p.Medio, Monto = i.Monto, PagoAnulado = p.Anulado
                }).ToListAsync();

            return new PagoProveedorItem
            {
                Id = p.Id, ProveedorId = p.ProveedorId, ProveedorNombre = p.Proveedor.RazonSocial, Fecha = p.Fecha,
                Medio = p.Medio, Monto = p.Monto, Referencia = p.Referencia, ChequeId = p.ChequeId,
                ChequeNumero = p.Cheque?.Numero, OrigenFondosId = p.OrigenFondosId, OrigenFondosNombre = p.OrigenFondos?.Nombre,
                Observaciones = p.Observaciones, Anulado = p.Anulado,
                AnuladoPor = p.AnuladoPor, FechaAnulacion = p.FechaAnulacion, MotivoAnulacion = p.MotivoAnulacion,
                CreadoPor = p.CreatedBy, Imputaciones = imputaciones
            };
        }

        private static ComprobanteProveedorItem ToItem(
            int id, int proveedorId, string proveedor, TipoComprobanteProveedor tipo, string numero, DateTime emision,
            DateTime? vencimiento, decimal total, decimal saldo, EstadoComprobanteProveedor estado, DateTime inicioHoy)
        {
            var conDeuda = tipo != TipoComprobanteProveedor.NotaCredito
                && estado is EstadoComprobanteProveedor.Pendiente or EstadoComprobanteProveedor.ParcialmentePagado;
            return new ComprobanteProveedorItem
            {
                Id = id, ProveedorId = proveedorId, ProveedorNombre = proveedor, Tipo = tipo, NumeroComprobante = numero,
                FechaEmision = emision, FechaVencimiento = vencimiento, ImporteTotal = total, SaldoPendiente = saldo,
                Estado = estado, DiasVencido = conDeuda ? DiasDeAtraso(vencimiento, inicioHoy) : 0
            };
        }

        private static string NombreTipo(TipoComprobanteProveedor tipo) => tipo switch
        {
            TipoComprobanteProveedor.Factura => "Factura",
            TipoComprobanteProveedor.NotaDebito => "Nota de débito",
            TipoComprobanteProveedor.NotaCredito => "Nota de crédito",
            _ => tipo.ToString()
        };

        private static string NombreMedio(MedioPagoProveedor medio) => medio switch
        {
            MedioPagoProveedor.Efectivo => "en efectivo",
            MedioPagoProveedor.Transferencia => "por transferencia",
            MedioPagoProveedor.Cheque => "con cheque",
            _ => "(otro medio)"
        };

        private static bool TieneMaximoDosDecimales(decimal valor) => decimal.Round(valor, 2) == valor;

        private static string? RecortarOpcional(string? texto, int maximo)
        {
            var t = texto?.Trim();
            if (string.IsNullOrEmpty(t))
                return null;
            return t.Length > maximo ? t[..maximo] : t;
        }
    }
}
