using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services;

/// <summary>Ver <see cref="INotaCreditoFiscalExternaService"/>. Requiere el permiso devoluciones.approve y deja auditoría.</summary>
public sealed partial class NotaCreditoFiscalExternaService : INotaCreditoFiscalExternaService
{
    private static readonly string[] TiposValidos = { "NCA", "NCB", "NCC", "NCM", "NCE" };
    private const decimal Tolerancia = 0.01m;

    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ISeguridadAuditoriaService _auditoria;
    private readonly ILogger<NotaCreditoFiscalExternaService> _logger;

    public NotaCreditoFiscalExternaService(
        AppDbContext context,
        ICurrentUserService currentUser,
        ISeguridadAuditoriaService auditoria,
        ILogger<NotaCreditoFiscalExternaService> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _auditoria = auditoria;
        _logger = logger;
    }

    [GeneratedRegex("^[0-9]{1,5}$")] private static partial Regex PuntoVentaRegex();
    [GeneratedRegex("^[0-9]{1,8}$")] private static partial Regex NumeroRegex();
    [GeneratedRegex("^[0-9]{14}$")] private static partial Regex CaeRegex();

    public async Task<NotaCreditoFiscalExternaResultado> RegistrarAsync(int notaCreditoId, NotaCreditoFiscalExternaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_currentUser.HasPermission("devoluciones", "approve"))
            return NotaCreditoFiscalExternaResultado.Fallo("No tenés permiso para vincular una nota de crédito fiscal externa.");

        var errores = new List<string>();
        var pv = (request.PuntoVenta ?? string.Empty).Trim();
        var numero = (request.Numero ?? string.Empty).Trim();
        var tipo = (request.Tipo ?? string.Empty).Trim().ToUpperInvariant();
        var cae = string.IsNullOrWhiteSpace(request.Cae) ? null : request.Cae.Trim();

        if (!PuntoVentaRegex().IsMatch(pv)) errores.Add("El punto de venta debe tener de 1 a 5 dígitos.");
        if (!NumeroRegex().IsMatch(numero)) errores.Add("El número debe tener de 1 a 8 dígitos.");
        if (!TiposValidos.Contains(tipo)) errores.Add("El tipo debe ser NCA, NCB, NCC, NCM o NCE.");
        if (cae != null && !CaeRegex().IsMatch(cae)) errores.Add("El CAE debe tener 14 dígitos.");
        if (request.Importe <= 0m) errores.Add("El importe de la nota de crédito fiscal debe ser mayor a cero.");
        if (errores.Count > 0)
            return NotaCreditoFiscalExternaResultado.Fallo(errores.ToArray());

        pv = pv.PadLeft(5, '0');
        numero = numero.PadLeft(8, '0');
        var fecha = request.Fecha.Date;

        var nc = await _context.NotasCredito
            .Include(n => n.Devolucion)
            .FirstOrDefaultAsync(n => n.Id == notaCreditoId && !n.IsDeleted);
        if (nc == null)
            return NotaCreditoFiscalExternaResultado.Fallo("La nota de crédito no existe.");
        if (nc.FiscalExternaNumero != null)
            return NotaCreditoFiscalExternaResultado.Fallo("La nota de crédito ya tiene un comprobante fiscal externo vinculado; no se modifica.");

        var venta = await _context.Ventas.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == nc.Devolucion.VentaId && !v.IsDeleted);
        if (venta == null)
            return NotaCreditoFiscalExternaResultado.Fallo("La venta de la devolución no existe.");

        var tieneFactura = await _context.Facturas.AsNoTracking()
            .AnyAsync(f => f.VentaId == venta.Id && !f.IsDeleted && !f.Anulada);
        if (!tieneFactura)
            return NotaCreditoFiscalExternaResultado.Fallo("La venta no tiene una factura vigente: no hay comprobante fiscal que corregir con una nota de crédito.");

        // Fecha: no futura (día comercial) y no anterior a la venta.
        var hoy = FechaComercial.DiaComercial(DateTime.UtcNow);
        if (fecha > hoy)
            errores.Add("La fecha de la nota de crédito fiscal no puede ser futura.");
        if (fecha < FechaComercial.DiaComercial(venta.FechaVenta))
            errores.Add("La nota de crédito fiscal no puede ser anterior a la venta.");

        // Importe: no mayor a la venta ni a lo ya acreditado fiscalmente sobre ella (evita acreditar dos veces).
        if (request.Importe > venta.Total + Tolerancia)
            errores.Add("El importe no puede superar el total de la venta.");
        var yaAcreditado = await _context.NotasCredito.AsNoTracking()
            .Where(n => !n.IsDeleted && n.Devolucion.VentaId == venta.Id && n.FiscalExternaImporte != null)
            .Select(n => n.FiscalExternaImporte!.Value)
            .ToListAsync();
        if (yaAcreditado.Sum() + request.Importe > venta.Total + Tolerancia)
            errores.Add("Las notas de crédito fiscales vinculadas a esta venta superarían su total.");
        if (request.Importe > nc.MontoTotal + Tolerancia)
            errores.Add("El importe de la nota de crédito fiscal no puede superar el de la nota de crédito interna.");

        // Duplicado: el mismo comprobante fiscal no puede apuntar a dos notas de crédito.
        var duplicado = await _context.NotasCredito.AsNoTracking().AnyAsync(n =>
            n.Id != nc.Id && n.FiscalExternaPuntoVenta == pv && n.FiscalExternaTipo == tipo && n.FiscalExternaNumero == numero);
        if (duplicado)
            errores.Add("Ese comprobante fiscal ya está vinculado a otra nota de crédito.");

        if (errores.Count > 0)
            return NotaCreditoFiscalExternaResultado.Fallo(errores.ToArray());

        nc.FiscalExternaPuntoVenta = pv;
        nc.FiscalExternaTipo = tipo;
        nc.FiscalExternaNumero = numero;
        nc.FiscalExternaFecha = fecha;
        nc.FiscalExternaImporte = request.Importe;
        nc.FiscalExternaCae = cae;
        nc.FiscalExternaRegistradaPor = _currentUser.GetUsername();
        nc.FiscalExternaRegistradaEn = DateTime.UtcNow;

        // La auditoría viaja en el MISMO guardado que el vínculo: no queda vínculo sin evento ni evento sin vínculo.
        _auditoria.AgregarEvento(
            "devoluciones", "vincular_nc_fiscal_externa", $"NotaCredito:{nc.Id}",
            $"{tipo} {pv}-{numero} fecha {fecha:yyyy-MM-dd} importe {request.Importe:0.00} venta {venta.Numero} (sin efecto fiscal: P3 no activo)");

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Carrera con otro registro del mismo comprobante: el índice único es la garantía de última línea.
            _logger.LogWarning(ex, "No se pudo vincular la NC fiscal externa {Tipo} {Pv}-{Numero} a la NC {NcId}", tipo, pv, numero, nc.Id);
            return NotaCreditoFiscalExternaResultado.Fallo("Ese comprobante fiscal ya está vinculado a otra nota de crédito.");
        }

        return NotaCreditoFiscalExternaResultado.Ok();
    }
}
