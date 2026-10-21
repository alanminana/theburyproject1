using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services;

/// <inheritdoc />
public class DescuentoVisibilidadService : IDescuentoVisibilidadService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public DescuentoVisibilidadService(AppDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public bool PuedeVerDescuentos =>
        _currentUser.HasPermission(VentaConstants.PermisoDescuento.Modulo, VentaConstants.PermisoDescuento.Accion);

    public async Task<bool> DebeDenegarVentaAsync(int ventaId)
        => !PuedeVerDescuentos && await VentasConDescuentos(v => v.Id == ventaId).AnyAsync();

    public async Task<bool> DebeDenegarFacturaAsync(int facturaId)
    {
        if (PuedeVerDescuentos)
            return false;

        var ventaId = await _context.Facturas.AsNoTracking()
            .Where(f => f.Id == facturaId && !f.IsDeleted)
            .Select(f => (int?)f.VentaId)
            .FirstOrDefaultAsync();
        return ventaId.HasValue && await VentasConDescuentos(v => v.Id == ventaId.Value).AnyAsync();
    }

    public async Task<bool> DebeDenegarCotizacionAsync(int cotizacionId)
        => !PuedeVerDescuentos && await CotizacionesConDescuentos(c => c.Id == cotizacionId).AnyAsync();

    public async Task<bool> DebeDenegarDocumentosAsync(IReadOnlyCollection<int> documentoIds)
    {
        if (PuedeVerDescuentos || documentoIds.Count == 0)
            return false;

        var ids = documentoIds.Distinct().ToList();
        return await DocumentosConDescuentos(d => ids.Contains(d.Id));
    }

    public async Task<bool> DebeDenegarGrupoDocumentosAsync(Guid grupoImpresionId)
        => !PuedeVerDescuentos && await DocumentosConDescuentos(d => d.GrupoImpresionId == grupoImpresionId);

    public async Task<bool> DebeDenegarDocumentosDePagoAsync(int pagoId)
        => !PuedeVerDescuentos && await DocumentosConDescuentos(d => d.PagoCuotaId == pagoId);

    // Descuento comercial manual = descuento general, descuento (%) de línea o su prorrateo persistido. Los ajustes
    // automáticos del medio de pago (recargo/descuento por plan) NO cuentan: no los gobierna este permiso.
    private IQueryable<Venta> VentasConDescuentos(System.Linq.Expressions.Expression<Func<Venta, bool>> filtro)
        => _context.Ventas.AsNoTracking()
            .Where(filtro)
            .Where(v => v.Descuento != 0m
                || v.Detalles.Any(d => !d.IsDeleted && (d.Descuento != 0m || d.DescuentoGeneralProrrateado != 0m)));

    private IQueryable<Cotizacion> CotizacionesConDescuentos(System.Linq.Expressions.Expression<Func<Cotizacion, bool>> filtro)
        => _context.Cotizaciones.AsNoTracking()
            .Where(filtro)
            .Where(c => c.DescuentoTotal != 0m
                || c.Detalles.Any(d => (d.DescuentoImporteSnapshot ?? 0m) != 0m || (d.DescuentoPorcentajeSnapshot ?? 0m) != 0m));

    private async Task<bool> DocumentosConDescuentos(System.Linq.Expressions.Expression<Func<DocumentoGenerado, bool>> filtro)
        => (await RestringidosAsync(filtro)).Count > 0;

    public async Task<IReadOnlySet<int>> DocumentosRestringidosAsync(IReadOnlyCollection<int> documentoIds)
    {
        if (PuedeVerDescuentos || documentoIds.Count == 0)
            return new HashSet<int>();

        var ids = documentoIds.Distinct().ToList();
        return await RestringidosAsync(d => ids.Contains(d.Id));
    }

    // Ids de los documentos del filtro anclados a una venta, crédito (vía su venta) o cotización con descuentos manuales.
    private async Task<HashSet<int>> RestringidosAsync(System.Linq.Expressions.Expression<Func<DocumentoGenerado, bool>> filtro)
    {
        var anclas = await _context.DocumentosGenerados.AsNoTracking()
            .Where(filtro)
            .Select(d => new { d.Id, d.VentaId, d.CotizacionId, d.CreditoId })
            .ToListAsync();
        var restringidos = new HashSet<int>();
        if (anclas.Count == 0)
            return restringidos;

        var ventaIds = anclas.Where(a => a.VentaId.HasValue).Select(a => a.VentaId!.Value).Distinct().ToList();
        var creditoIds = anclas.Where(a => a.CreditoId.HasValue).Select(a => a.CreditoId!.Value).Distinct().ToList();
        var cotizacionIds = anclas.Where(a => a.CotizacionId.HasValue).Select(a => a.CotizacionId!.Value).Distinct().ToList();

        var ventasCon = ventaIds.Count == 0 ? new HashSet<int>()
            : (await VentasConDescuentos(v => ventaIds.Contains(v.Id)).Select(v => v.Id).ToListAsync()).ToHashSet();
        var creditosCon = creditoIds.Count == 0 ? new HashSet<int>()
            : (await VentasConDescuentos(v => v.CreditoId.HasValue && creditoIds.Contains(v.CreditoId.Value))
                .Select(v => v.CreditoId!.Value).ToListAsync()).ToHashSet();
        var cotizacionesCon = cotizacionIds.Count == 0 ? new HashSet<int>()
            : (await CotizacionesConDescuentos(c => cotizacionIds.Contains(c.Id)).Select(c => c.Id).ToListAsync()).ToHashSet();

        foreach (var a in anclas)
        {
            if ((a.VentaId.HasValue && ventasCon.Contains(a.VentaId.Value))
                || (a.CreditoId.HasValue && creditosCon.Contains(a.CreditoId.Value))
                || (a.CotizacionId.HasValue && cotizacionesCon.Contains(a.CotizacionId.Value)))
                restringidos.Add(a.Id);
        }
        return restringidos;
    }
}
