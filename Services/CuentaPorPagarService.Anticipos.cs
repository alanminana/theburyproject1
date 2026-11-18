using Microsoft.EntityFrameworkCore;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    /// <summary>
    /// D-03 Tesorería administrativa: anticipos a proveedor (saldo a favor aplicable a una o varias facturas del MISMO proveedor),
    /// origen de fondos y regla de un cheque físico por pago. Mismas garantías que el resto del libro: una transacción por operación
    /// que serializa por proveedor, idempotencia por clave de formulario, contra-registros con motivo y auditoría en la misma transacción.
    /// </summary>
    public partial class CuentaPorPagarService
    {
        // =====================================================================================
        // Reglas compartidas por pagos y anticipos
        // =====================================================================================

        /// <summary>El origen debe existir y estar activo (un origen inactivo conserva su historia pero no recibe egresos nuevos).</summary>
        private async Task<string?> ValidarOrigenFondosAsync(int origenFondosId)
        {
            var origen = await _context.OrigenesFondos.AsNoTracking()
                .Where(o => o.Id == origenFondosId && !o.IsDeleted)
                .Select(o => new { o.Activo }).FirstOrDefaultAsync();
            if (origen is null)
                return "El origen de fondos no existe.";
            if (!origen.Activo)
                return "El origen de fondos está inactivo: elegí otro.";
            return null;
        }

        /// <summary>
        /// Un cheque físico respalda un ÚNICO pago o anticipo vigente (R-21): debe ser del proveedor, no estar anulado ni rechazado,
        /// su importe debe coincidir con el del egreso (un cheque no se reparte entre pagos) y no estar usado por otro pago o anticipo
        /// vigente. Anular el pago o el anticipo lo libera. Se invoca bajo el lock del proveedor; el índice único filtrado es la red de seguridad.
        /// </summary>
        private async Task<string?> ValidarChequeParaEgresoAsync(int chequeId, int proveedorId, decimal monto, string egreso)
        {
            var cheque = await _context.Cheques.AsNoTracking()
                .Where(c => c.Id == chequeId && !c.IsDeleted && c.ProveedorId == proveedorId)
                .Select(c => new { c.Estado, c.Monto, c.Numero }).FirstOrDefaultAsync();
            if (cheque is null)
                return "El cheque no existe o pertenece a otro proveedor.";
            if (cheque.Estado is EstadoCheque.Anulado or EstadoCheque.Rechazado)
                return $"El cheque está anulado o rechazado y no puede usarse para un {egreso}.";
            if (cheque.Monto != monto)
                return $"El importe del {egreso} ({monto:N2}) debe coincidir con el del cheque Nº {cheque.Numero} ({cheque.Monto:N2}): un cheque físico respalda un único {egreso}.";

            var usadoPorPago = await _context.PagosProveedor.AsNoTracking()
                .AnyAsync(p => p.ChequeId == chequeId && !p.Anulado && !p.IsDeleted);
            var usadoPorAnticipo = await _context.AnticiposProveedor.AsNoTracking()
                .AnyAsync(a => a.ChequeId == chequeId && !a.Anulado && !a.IsDeleted);
            if (usadoPorPago || usadoPorAnticipo)
                return $"El cheque Nº {cheque.Numero} ya respalda otro pago o anticipo vigente. Un cheque físico se asocia a un único pago (puede imputarse a varias facturas); anulá ese pago para liberarlo.";

            return null;
        }

        private async Task<decimal> CalcularAnticiposSinAplicarAsync(int proveedorId)
        {
            var anticipos = await _context.AnticiposProveedor.AsNoTracking()
                .Where(a => a.ProveedorId == proveedorId && !a.Anulado)
                .Select(a => new { a.Id, a.Monto }).ToListAsync();
            if (anticipos.Count == 0)
                return 0m;

            var saldos = await CalcularSaldosAnticiposAsync(anticipos.Select(a => a.Id).ToList());
            return anticipos.Sum(a => a.Monto - saldos.GetValueOrDefault(a.Id));
        }

        /// <summary>Importe aplicado (aplicaciones no anuladas) de cada anticipo; los montos se suman en memoria (SQLite no suma decimales).</summary>
        private async Task<Dictionary<int, decimal>> CalcularSaldosAnticiposAsync(IReadOnlyCollection<int> anticipoIds)
        {
            var ids = anticipoIds.Distinct().ToList();
            var resultado = ids.ToDictionary(i => i, _ => 0m);
            var aplicaciones = await _context.AnticipoAplicaciones.AsNoTracking()
                .Where(a => ids.Contains(a.AnticipoProveedorId) && !a.Anulada)
                .Select(a => new { a.AnticipoProveedorId, a.Monto }).ToListAsync();
            foreach (var a in aplicaciones)
                resultado[a.AnticipoProveedorId] += a.Monto;
            return resultado;
        }

        // =====================================================================================
        // Escrituras
        // =====================================================================================

        public async Task<ResultadoCxp> RegistrarAnticipoAsync(AnticipoProveedorRequest request, string usuario)
        {
            if (request is null)
                return ResultadoCxp.Falla("Datos inválidos.");
            if (!Enum.IsDefined(request.Medio))
                return ResultadoCxp.Falla("El medio de pago no es válido.");
            if (request.Fecha is null || request.Fecha.Value.Year < 2000)
                return ResultadoCxp.Falla("La fecha del anticipo es obligatoria.");
            if (request.Monto <= 0 || !TieneMaximoDosDecimales(request.Monto) || request.Monto > ImporteMaximo)
                return ResultadoCxp.Falla("El monto del anticipo debe ser mayor que cero (máximo 2 decimales).");
            if (request.ChequeId.HasValue && request.Medio != MedioPagoProveedor.Cheque)
                return ResultadoCxp.Falla("Solo un anticipo con medio Cheque puede referenciar un cheque.");
            if (request.OrigenFondosId is null or <= 0)
                return ResultadoCxp.Falla("Elegí el origen de los fondos del anticipo.");

            var clave = RecortarOpcional(request.ClaveIdempotencia, 64);
            var referencia = RecortarOpcional(request.Referencia, 100);
            var observaciones = RecortarOpcional(request.Observaciones, 1000);
            var fecha = request.Fecha.Value.Date;

            return await EnTransaccionAsync(request.ProveedorId, async () =>
            {
                if (clave is not null)
                {
                    var existente = await _context.AnticiposProveedor.AsNoTracking()
                        .Where(a => a.ProveedorId == request.ProveedorId && a.ClaveIdempotencia == clave)
                        .Select(a => (int?)a.Id).FirstOrDefaultAsync();
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
                    var errorCheque = await ValidarChequeParaEgresoAsync(request.ChequeId.Value, request.ProveedorId, request.Monto, "anticipo");
                    if (errorCheque is not null)
                        return ResultadoCxp.Falla(errorCheque);
                }

                var anticipo = new AnticipoProveedor
                {
                    ProveedorId = request.ProveedorId,
                    Fecha = fecha,
                    Monto = request.Monto,
                    OrigenFondosId = request.OrigenFondosId.Value,
                    Medio = request.Medio,
                    Referencia = referencia,
                    ChequeId = request.ChequeId,
                    Observaciones = observaciones,
                    ClaveIdempotencia = clave,
                    CreatedBy = usuario
                };
                _context.AnticiposProveedor.Add(anticipo);
                Auditar("RegistrarAnticipo", $"Proveedor:{anticipo.ProveedorId}",
                    $"Anticipo {anticipo.Medio} por {anticipo.Monto:0.00}; origen {anticipo.OrigenFondosId}; cheque {anticipo.ChequeId?.ToString() ?? "-"}; por {usuario}");
                await _context.SaveChangesAsync();

                _logger.LogInformation("Anticipo {Id} por {Monto} registrado para proveedor {ProveedorId} por {Usuario}",
                    anticipo.Id, anticipo.Monto, anticipo.ProveedorId, usuario);
                return ResultadoCxp.Exito(anticipo.Id);
            });
        }

        public async Task<ResultadoCxp> AnularAnticipoAsync(int anticipoId, string? motivo, string usuario)
        {
            var motivoLimpio = RecortarOpcional(motivo, 500);
            if (motivoLimpio is null)
                return ResultadoCxp.Falla("El motivo de la anulación es obligatorio.");

            var proveedorId = await _context.AnticiposProveedor.AsNoTracking()
                .Where(a => a.Id == anticipoId).Select(a => (int?)a.ProveedorId).FirstOrDefaultAsync();
            if (proveedorId is null)
                return ResultadoCxp.Falla("El anticipo no existe.");

            return await EnTransaccionAsync(proveedorId.Value, async () =>
            {
                var anticipo = await _context.AnticiposProveedor.FirstOrDefaultAsync(a => a.Id == anticipoId);
                if (anticipo is null)
                    return ResultadoCxp.Falla("El anticipo no existe.");
                if (anticipo.Anulado)
                    return ResultadoCxp.Falla("El anticipo ya está anulado.");

                var aplicado = (await CalcularSaldosAnticiposAsync(new[] { anticipoId })).GetValueOrDefault(anticipoId);
                if (aplicado > 0m)
                    return ResultadoCxp.Falla("No se puede anular un anticipo con aplicaciones vigentes. Anulá primero esas aplicaciones.");

                anticipo.Anulado = true;
                anticipo.AnuladoPor = usuario;
                anticipo.FechaAnulacion = _reloj.AhoraUtc;
                anticipo.MotivoAnulacion = motivoLimpio;
                Auditar("AnularAnticipo", $"Anticipo:{anticipoId}", $"Anticipo por {anticipo.Monto:0.00} anulado por {usuario}: {motivoLimpio}");
                await _context.SaveChangesAsync();

                _logger.LogInformation("Anticipo {Id} anulado por {Usuario}: {Motivo}", anticipoId, usuario, motivoLimpio);
                return ResultadoCxp.Exito(anticipoId);
            });
        }

        public async Task<ResultadoCxp> AplicarAnticipoAsync(
            int anticipoId, IReadOnlyList<ImputacionRequest> imputacionesRequest, string? claveIdempotencia, string usuario)
        {
            var (imputaciones, errorImputaciones) = NormalizarImputaciones(imputacionesRequest);
            if (errorImputaciones is not null)
                return ResultadoCxp.Falla(errorImputaciones);
            if (imputaciones.Count == 0)
                return ResultadoCxp.Falla("Indicá a qué facturas se aplica el anticipo.");

            var clave = RecortarOpcional(claveIdempotencia, 64);

            var proveedorId = await _context.AnticiposProveedor.AsNoTracking()
                .Where(a => a.Id == anticipoId).Select(a => (int?)a.ProveedorId).FirstOrDefaultAsync();
            if (proveedorId is null)
                return ResultadoCxp.Falla("El anticipo no existe.");

            return await EnTransaccionAsync(proveedorId.Value, async () =>
            {
                if (clave is not null)
                {
                    var repetida = await _context.AnticipoAplicaciones.AsNoTracking()
                        .AnyAsync(a => a.AnticipoProveedorId == anticipoId && a.ClaveIdempotencia == clave);
                    if (repetida)
                        return ResultadoCxp.Exito(anticipoId, yaRegistrado: true);
                }

                var anticipo = await _context.AnticiposProveedor.AsNoTracking().FirstOrDefaultAsync(a => a.Id == anticipoId);
                if (anticipo is null)
                    return ResultadoCxp.Falla("El anticipo no existe.");
                if (anticipo.Anulado)
                    return ResultadoCxp.Falla("El anticipo está anulado.");

                var ids = imputaciones.Select(i => i.ComprobanteId).ToList();
                var destinos = await _context.ComprobantesProveedor.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync();
                if (destinos.Count != ids.Count)
                    return ResultadoCxp.Falla("Alguna factura indicada no existe.");

                var aplicado = await CalcularAplicadoAsync(ids);
                var saldoAnticipo = anticipo.Monto - (await CalcularSaldosAnticiposAsync(new[] { anticipoId })).GetValueOrDefault(anticipoId);

                foreach (var imp in imputaciones)
                {
                    var d = destinos.Single(x => x.Id == imp.ComprobanteId);
                    if (d.ProveedorId != anticipo.ProveedorId)
                        return ResultadoCxp.Falla($"El comprobante {d.NumeroComprobante} pertenece a otro proveedor: un anticipo solo se aplica a facturas del mismo proveedor.");
                    if (d.Tipo == TipoComprobanteProveedor.NotaCredito)
                        return ResultadoCxp.Falla($"El comprobante {d.NumeroComprobante} es una nota de crédito: un anticipo solo se aplica a facturas o notas de débito.");
                    if (d.Estado == EstadoComprobanteProveedor.Anulado)
                        return ResultadoCxp.Falla($"El comprobante {d.NumeroComprobante} está anulado.");

                    var saldoDestino = d.ImporteTotal - aplicado.GetValueOrDefault(d.Id);
                    if (imp.Monto > saldoDestino)
                        return ResultadoCxp.Falla(
                            $"La aplicación al comprobante {d.NumeroComprobante} supera su saldo pendiente ({saldoDestino:N2}).");
                }

                var total = imputaciones.Sum(i => i.Monto);
                if (total > saldoAnticipo)
                    return ResultadoCxp.Falla($"La aplicación supera el saldo a favor del anticipo ({saldoAnticipo:N2}).");

                foreach (var imp in imputaciones)
                {
                    _context.AnticipoAplicaciones.Add(new AnticipoAplicacion
                    {
                        AnticipoProveedorId = anticipoId,
                        ComprobanteProveedorId = imp.ComprobanteId,
                        Monto = imp.Monto,
                        ClaveIdempotencia = clave,
                        CreatedBy = usuario
                    });
                }
                Auditar("AplicarAnticipo", $"Anticipo:{anticipoId}",
                    $"Anticipo aplicado a {string.Join(",", ids)} por {total:0.00} por {usuario}");
                await _context.SaveChangesAsync();

                await RecalcularAsync(ids);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Anticipo {Id} aplicado a {Cantidad} comprobante(s) por {Usuario}", anticipoId, imputaciones.Count, usuario);
                return ResultadoCxp.Exito(anticipoId);
            });
        }

        public async Task<ResultadoCxp> AnularAplicacionAnticipoAsync(int aplicacionId, string? motivo, string usuario)
        {
            var motivoLimpio = RecortarOpcional(motivo, 500);
            if (motivoLimpio is null)
                return ResultadoCxp.Falla("El motivo de la anulación es obligatorio.");

            var origen = await _context.AnticipoAplicaciones.AsNoTracking()
                .Where(a => a.Id == aplicacionId)
                .Select(a => new { a.Anticipo.ProveedorId }).FirstOrDefaultAsync();
            if (origen is null)
                return ResultadoCxp.Falla("La aplicación de anticipo no existe.");

            return await EnTransaccionAsync(origen.ProveedorId, async () =>
            {
                var aplicacion = await _context.AnticipoAplicaciones.FirstOrDefaultAsync(a => a.Id == aplicacionId);
                if (aplicacion is null)
                    return ResultadoCxp.Falla("La aplicación de anticipo no existe.");
                if (aplicacion.Anulada)
                    return ResultadoCxp.Falla("La aplicación ya está anulada.");

                aplicacion.Anulada = true;
                aplicacion.AnuladaPor = usuario;
                aplicacion.FechaAnulacion = _reloj.AhoraUtc;
                aplicacion.MotivoAnulacion = motivoLimpio;
                Auditar("AnularAplicacionAnticipo", $"Anticipo:{aplicacion.AnticipoProveedorId}", $"Aplicación {aplicacionId} anulada por {usuario}: {motivoLimpio}");
                await _context.SaveChangesAsync();

                await RecalcularAsync(new[] { aplicacion.ComprobanteProveedorId });
                await _context.SaveChangesAsync();

                _logger.LogInformation("Aplicación de anticipo {Id} anulada por {Usuario}: {Motivo}", aplicacionId, usuario, motivoLimpio);
                return ResultadoCxp.Exito(aplicacionId);
            });
        }

        // =====================================================================================
        // Consultas
        // =====================================================================================

        public async Task<AnticipoProveedorItem?> ObtenerAnticipoAsync(int anticipoId)
        {
            var a = await _context.AnticiposProveedor.AsNoTracking()
                .Include(x => x.Proveedor).Include(x => x.OrigenFondos).Include(x => x.Cheque)
                .FirstOrDefaultAsync(x => x.Id == anticipoId);
            return a is null ? null : (await ToAnticipoItemsAsync(new[] { a })).Single();
        }

        public async Task<List<AnticipoProveedorItem>> ListarAnticiposAsync(int proveedorId)
        {
            var anticipos = await _context.AnticiposProveedor.AsNoTracking()
                .Include(x => x.Proveedor).Include(x => x.OrigenFondos).Include(x => x.Cheque)
                .Where(a => a.ProveedorId == proveedorId)
                .OrderByDescending(a => a.Fecha).ThenByDescending(a => a.Id)
                .ToListAsync();
            return await ToAnticipoItemsAsync(anticipos);
        }

        /// <summary>Anticipos vigentes con saldo a favor sin aplicar (para ofrecerlos al aplicar a facturas).</summary>
        public async Task<List<AnticipoProveedorItem>> ObtenerAnticiposConSaldoAsync(int proveedorId) =>
            (await ListarAnticiposAsync(proveedorId)).Where(a => !a.Anulado && a.SaldoSinAplicar > 0m).ToList();

        private async Task<List<AnticipoProveedorItem>> ToAnticipoItemsAsync(IReadOnlyCollection<AnticipoProveedor> anticipos)
        {
            var ids = anticipos.Select(a => a.Id).ToList();
            var aplicaciones = await _context.AnticipoAplicaciones.AsNoTracking()
                .Where(x => ids.Contains(x.AnticipoProveedorId))
                .OrderBy(x => x.Id)
                .Select(x => new AnticipoAplicacionItem
                {
                    Id = x.Id, AnticipoId = x.AnticipoProveedorId, ComprobanteId = x.ComprobanteProveedorId,
                    ComprobanteNumero = x.Comprobante.NumeroComprobante, Monto = x.Monto, Fecha = x.CreatedAt,
                    Anulada = x.Anulada, MotivoAnulacion = x.MotivoAnulacion
                }).ToListAsync();

            return anticipos.Select(a =>
            {
                var propias = aplicaciones.Where(x => x.AnticipoId == a.Id).ToList();
                var aplicado = propias.Where(x => !x.Anulada).Sum(x => x.Monto);
                return new AnticipoProveedorItem
                {
                    Id = a.Id, ProveedorId = a.ProveedorId, ProveedorNombre = a.Proveedor.RazonSocial, Fecha = a.Fecha, Monto = a.Monto,
                    SaldoSinAplicar = a.Anulado ? 0m : a.Monto - aplicado,
                    OrigenFondosId = a.OrigenFondosId, OrigenFondosNombre = a.OrigenFondos.Nombre, Medio = a.Medio,
                    Referencia = a.Referencia, ChequeId = a.ChequeId, ChequeNumero = a.Cheque?.Numero, Observaciones = a.Observaciones,
                    Anulado = a.Anulado, AnuladoPor = a.AnuladoPor, FechaAnulacion = a.FechaAnulacion, MotivoAnulacion = a.MotivoAnulacion,
                    CreadoPor = a.CreatedBy, Aplicaciones = propias
                };
            }).ToList();
        }
    }
}
