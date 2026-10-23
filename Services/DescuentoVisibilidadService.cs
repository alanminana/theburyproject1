using System.Text.Json;
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
        => !PuedeVerDescuentos
            && (await VentasConDescuentos(v => v.Id == ventaId).AnyAsync()
                || await ContratoLegadoEmitidoConDescuentosAsync(ventaId)
                || await DocumentoEmitidoConDescuentosDeLaVentaAsync(ventaId));

    // El contrato legado emitido es inmutable: su snapshot conserva el descuento por línea tal como se emitió, aunque la
    // venta se haya editado después (VentaValidator permite editar en estados previos a la confirmación). Un snapshot
    // ilegible se trata cerrado: no se puede probar que no tenga descuentos.
    private async Task<bool> ContratoLegadoEmitidoConDescuentosAsync(int ventaId)
    {
        var snapshots = await _context.ContratosVentaCredito.AsNoTracking()
            .Where(c => c.VentaId == ventaId && !c.IsDeleted)
            .Select(c => c.DatosSnapshotJson)
            .ToListAsync();
        return snapshots.Any(SnapshotContratoTieneDescuentos);
    }

    // El PDF del contrato se arma desde los documentos del motor documental de la venta: si alguno se emitió con
    // descuentos (marca persistida), el contrato no se entrega sin ventas.discount aunque la venta ya no los tenga.
    private async Task<bool> DocumentoEmitidoConDescuentosDeLaVentaAsync(int ventaId)
    {
        var metadatas = await _context.DocumentosGenerados.AsNoTracking()
            .Where(d => d.VentaId == ventaId)
            .Select(d => d.MetadataJson)
            .ToListAsync();
        return metadatas.Any(DocumentoEmitidoConDescuentos);
    }

    internal static bool SnapshotContratoTieneDescuentos(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            foreach (var venta in doc.RootElement.EnumerateObject().Where(p => string.Equals(p.Name, "venta", StringComparison.OrdinalIgnoreCase)))
            {
                if (venta.Value.EnumerateObject().Any(p => string.Equals(p.Name, "conDescuentos", StringComparison.OrdinalIgnoreCase)
                        && p.Value.ValueKind == JsonValueKind.True))
                    return true;

                foreach (var productos in venta.Value.EnumerateObject().Where(p => string.Equals(p.Name, "productos", StringComparison.OrdinalIgnoreCase)))
                    foreach (var producto in productos.Value.EnumerateArray())
                        foreach (var d in producto.EnumerateObject().Where(p => string.Equals(p.Name, "descuento", StringComparison.OrdinalIgnoreCase)))
                            if (d.Value.ValueKind != JsonValueKind.Number || d.Value.GetDecimal() != 0m)
                                return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return true;
        }
    }

    // Marca persistida al emitir (MetadataJson.conDescuentos). Los documentos anteriores a la marca no la tienen y siguen
    // clasificándose por el estado actual de la venta (decisión funcional: no se restringe en bloque lo ya emitido).
    internal static bool DocumentoEmitidoConDescuentos(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.EnumerateObject().Any(p => string.Equals(p.Name, "conDescuentos", StringComparison.OrdinalIgnoreCase)
                    && p.Value.ValueKind == JsonValueKind.True);
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
            .Where(DescuentosComerciales.VentaConDescuento);

    private IQueryable<Cotizacion> CotizacionesConDescuentos(System.Linq.Expressions.Expression<Func<Cotizacion, bool>> filtro)
        => _context.Cotizaciones.AsNoTracking()
            .Where(filtro)
            .Where(DescuentosComerciales.CotizacionConDescuento);

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
            .Select(d => new { d.Id, d.VentaId, d.CotizacionId, d.CreditoId, d.MetadataJson })
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
            if (DocumentoEmitidoConDescuentos(a.MetadataJson)
                || (a.VentaId.HasValue && ventasCon.Contains(a.VentaId.Value))
                || (a.CreditoId.HasValue && creditosCon.Contains(a.CreditoId.Value))
                || (a.CotizacionId.HasValue && cotizacionesCon.Contains(a.CotizacionId.Value)))
                restringidos.Add(a.Id);
        }
        return restringidos;
    }
}
