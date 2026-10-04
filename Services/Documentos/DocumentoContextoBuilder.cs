using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services.Documentos
{
    /// <summary>
    /// Traduce las entidades internas (venta, crédito, cuotas, pago, cotización) al contexto plano
    /// que consumen plantillas y reglas. Es el único lugar que conoce el modelo de dominio.
    /// </summary>
    public class DocumentoContextoBuilder : IDocumentoContextoBuilder
    {
        private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-AR");
        private readonly AppDbContext _context;

        public DocumentoContextoBuilder(AppDbContext context)
        {
            _context = context;
        }

        public async Task<DocumentoContexto> ConstruirAsync(string evento, DocumentoOrigen origen)
        {
            var info = EventosDocumentales.Obtener(evento)
                ?? throw new DocumentoException(DocumentoErrores.ContextoIncompleto, $"Evento documental desconocido: {evento}.");

            var ctx = new DocumentoContexto { Ancla = info.Ancla };
            await AplicarEmpresaAsync(ctx);

            switch (info.Ancla)
            {
                case AnclaDocumento.Pago:
                    if (origen.PagoCuotaId is not int pagoId)
                        throw new DocumentoException(DocumentoErrores.ContextoIncompleto, "El evento requiere un pago de cuota.");
                    await CargarPagoAsync(ctx, pagoId);
                    break;
                case AnclaDocumento.Cotizacion:
                    if (origen.CotizacionId is not int cotId)
                        throw new DocumentoException(DocumentoErrores.ContextoIncompleto, "El evento requiere una cotización.");
                    await CargarCotizacionAsync(ctx, cotId);
                    break;
                default:
                    if (origen.VentaId is not int ventaId)
                        throw new DocumentoException(DocumentoErrores.ContextoIncompleto, "El evento requiere una venta.");
                    await CargarVentaAsync(ctx, ventaId);
                    break;
            }

            return ctx;
        }

        /// <summary>Datos del vendedor/empresa: salen de la plantilla de contrato vigente (única fuente configurada hoy).</summary>
        private async Task AplicarEmpresaAsync(DocumentoContexto ctx)
        {
            var hoy = DateTime.UtcNow.Date;
            var plantilla = await _context.PlantillasContratoCredito
                .AsNoTracking()
                .Where(p => p.Activa && p.VigenteDesde.Date <= hoy && (!p.VigenteHasta.HasValue || p.VigenteHasta.Value.Date >= hoy))
                .OrderByDescending(p => p.VigenteDesde).ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync();

            AplicarEmpresa(ctx, plantilla);
        }

        public static void AplicarEmpresa(DocumentoContexto ctx, PlantillaContratoCredito? p)
        {
            ctx.Set("empresa.nombre", p?.NombreVendedor);
            ctx.Set("empresa.cuit", p?.CuitVendedor);
            ctx.Set("empresa.dni", p?.DniVendedor);
            ctx.Set("empresa.direccion", p?.DomicilioVendedor);
            ctx.Set("empresa.ciudad", p?.CiudadFirma);
            ctx.Set("empresa.jurisdiccion", p?.Jurisdiccion);
            ctx.Set("empresa.interesMoraDiario", p == null ? null : p.InteresMoraDiarioPorcentaje.ToString("F4", Cultura));
        }

        public static void AplicarCliente(DocumentoContexto ctx, Cliente? c)
        {
            ctx.ClienteId = c?.Id;
            ctx.Set("cliente.nombre", c?.Nombre);
            ctx.Set("cliente.apellido", c?.Apellido);
            ctx.Set("cliente.nombreCompleto", c?.ToDisplayName());
            ctx.Set("cliente.tipoDocumento", c?.TipoDocumento);
            ctx.Set("cliente.dni", c?.NumeroDocumento);
            ctx.Set("cliente.cuit", c?.CuilCuit);
            ctx.Set("cliente.direccion", c?.Domicilio);
            ctx.Set("cliente.localidad", c?.Localidad);
            ctx.Set("cliente.telefono", c?.Telefono);
            ctx.Set("cliente.email", c?.Email);
        }

        public static void AplicarFiador(DocumentoContexto ctx, Garante? g)
        {
            var gc = g?.GaranteCliente;
            ctx.Set("fiador.existe", g != null);
            ctx.Set("fiador.nombreCompleto", g == null ? null : (gc?.ToDisplayName() ?? g.ToDisplayName()));
            ctx.Set("fiador.dni", g == null ? null : (gc?.NumeroDocumento ?? g.NumeroDocumento));
            ctx.Set("fiador.direccion", g == null ? null : (gc?.Domicilio ?? g.Domicilio));
            ctx.Set("fiador.relacion", g?.Relacion);
        }

        public static void AplicarProductos(DocumentoContexto ctx, IEnumerable<Dictionary<string, object?>> productos)
        {
            var lista = productos.ToList();
            ctx.Colecciones["productos"] = lista;
            ctx.Set("venta.cantidadProductos", lista.Count);
            ctx.Set("venta.productosDetalle", string.Join(Environment.NewLine,
                lista.Select(p => $"{p["cantidad"]} x {p["descripcion"]} ({p["codigo"]}) - {PlantillaRenderer.Formatear(p["subtotal"])}")));
        }

        public static void AplicarCuotas(DocumentoContexto ctx, IEnumerable<Dictionary<string, object?>> cuotas)
        {
            var lista = cuotas.ToList();
            ctx.Colecciones["cuotas"] = lista;
            ctx.Set("credito.planCuotasDetalle", string.Join(Environment.NewLine,
                lista.Select(c => $"Cuota {c["numero"]}: vence {PlantillaRenderer.Formatear(c["vencimiento"])} - {PlantillaRenderer.Formatear(c["importe"])}")));
        }

        public static Dictionary<string, object?> ItemProducto(string? codigo, string? marca, string descripcion, decimal cantidad, decimal precio, decimal subtotal)
            => new(StringComparer.OrdinalIgnoreCase)
            {
                ["codigo"] = codigo ?? string.Empty, ["marca"] = marca ?? string.Empty, ["descripcion"] = descripcion,
                ["cantidad"] = cantidad, ["precio"] = precio, ["subtotal"] = subtotal
            };

        public static Dictionary<string, object?> ItemCuota(int numero, decimal importe, decimal capital, decimal interes, DateTime vencimiento, string estado)
            => new(StringComparer.OrdinalIgnoreCase)
            {
                ["numero"] = numero, ["importe"] = importe, ["capital"] = capital, ["interes"] = interes,
                ["vencimiento"] = vencimiento.Date, ["estado"] = estado
            };

        private IQueryable<Venta> VentaCompleta()
            => _context.Ventas
                .AsNoTracking()
                .Include(v => v.Cliente)
                .Include(v => v.Credito).ThenInclude(c => c!.Garante).ThenInclude(g => g!.GaranteCliente)
                .Include(v => v.Credito).ThenInclude(c => c!.Cuotas.Where(cu => !cu.IsDeleted).OrderBy(cu => cu.NumeroCuota))
                .Include(v => v.Detalles.Where(d => !d.IsDeleted)).ThenInclude(d => d.Producto).ThenInclude(p => p!.Marca)
                .Include(v => v.AperturaCaja).ThenInclude(a => a!.Caja)
                .Include(v => v.VendedorUser)
                .AsSplitQuery();

        private async Task CargarVentaAsync(DocumentoContexto ctx, int ventaId)
        {
            var venta = await VentaCompleta().FirstOrDefaultAsync(v => v.Id == ventaId && !v.IsDeleted)
                ?? throw new DocumentoException(DocumentoErrores.ContextoIncompleto, $"No existe la venta {ventaId}.");

            ctx.VentaId = venta.Id;
            ctx.ClaveAncla = $"venta:{venta.Id}";
            ctx.CreditoId = venta.CreditoId;

            await AplicarVentaAsync(ctx, venta);
        }

        /// <summary>Cliente, venta, productos, envío y crédito de una venta (también lo usa el contexto de un pago).</summary>
        private async Task AplicarVentaAsync(DocumentoContexto ctx, Venta venta)
        {
            AplicarCliente(ctx, venta.Cliente);

            ctx.Set("venta.numero", venta.Numero);
            ctx.Set("venta.fecha", venta.FechaVenta.Date);
            ctx.Set("venta.total", venta.Total);
            ctx.Set("venta.subtotal", venta.Subtotal);
            ctx.Set("venta.tipoPago", venta.TipoPago.ToString());
            ctx.Set("venta.sucursal", venta.VendedorUser?.Sucursal);
            ctx.Set("venta.caja", venta.AperturaCaja?.Caja?.Nombre);

            AplicarProductos(ctx, venta.Detalles.OrderBy(d => d.Id).Select(d => ItemProducto(
                VentaDetalleProductoSnapshot.ResolverCodigo(d),
                d.Producto?.Marca?.Nombre,
                VentaDetalleProductoSnapshot.ResolverNombre(d),
                d.Cantidad,
                d.PrecioUnitario,
                d.SubtotalFinal > 0m ? d.SubtotalFinal : d.Subtotal)));

            var envio = await _context.VentaEnvios.AsNoTracking().FirstOrDefaultAsync(e => e.VentaId == venta.Id && !e.IsDeleted);
            ctx.Set("venta.conEnvio", envio != null);
            ctx.Set("entrega.fecha", envio?.FechaEntregaReal?.Date);
            ctx.Set("entrega.direccion", envio == null ? null : string.Join(", ", new[] { envio.Domicilio, envio.Localidad, envio.Provincia }.Where(x => !string.IsNullOrWhiteSpace(x))));
            ctx.Set("entrega.destinatario", envio?.Destinatario);

            AplicarCredito(ctx, venta.Credito);
        }

        private static void AplicarCredito(DocumentoContexto ctx, Credito? credito)
        {
            ctx.Set("credito.numero", credito?.Numero);
            ctx.Set("credito.total", credito?.TotalAPagar);
            ctx.Set("credito.saldo", credito?.SaldoPendiente);
            ctx.Set("credito.montoFinanciado", credito?.MontoAprobado);
            ctx.Set("credito.saldoFinanciado", credito == null ? null : credito.MontoCuota * credito.CantidadCuotas);
            ctx.Set("credito.cantidadCuotas", credito?.CantidadCuotas);
            ctx.Set("credito.importeCuota", credito?.MontoCuota);
            ctx.Set("credito.fechaPrimeraCuota", credito?.FechaPrimeraCuota?.Date);
            ctx.Set("credito.requiereFiador", credito?.RequiereGarante ?? false);

            AplicarFiador(ctx, credito?.Garante);

            var cuotas = credito?.Cuotas.Where(c => !c.IsDeleted).OrderBy(c => c.NumeroCuota)
                .Select(c => ItemCuota(c.NumeroCuota, c.MontoTotal, c.MontoCapital, c.MontoInteres, c.FechaVencimiento, c.Estado.ToString()))
                ?? Enumerable.Empty<Dictionary<string, object?>>();
            AplicarCuotas(ctx, cuotas);
        }

        private async Task CargarPagoAsync(DocumentoContexto ctx, int pagoCuotaId)
        {
            var pago = await _context.PagosCuota
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == pagoCuotaId && !p.IsDeleted)
                ?? throw new DocumentoException(DocumentoErrores.ContextoIncompleto, $"No existe el pago {pagoCuotaId}.");

            var cuota = await _context.Cuotas
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == pago.CuotaId)
                ?? throw new DocumentoException(DocumentoErrores.ContextoIncompleto, $"El pago {pagoCuotaId} no tiene cuota.");

            // El crédito se carga aparte: Cuota→Credito→Cuotas sería un ciclo (no admitido en consultas sin tracking).
            var credito = await _context.Creditos
                .AsNoTracking()
                .Include(c => c.Cliente)
                .Include(c => c.Garante).ThenInclude(g => g!.GaranteCliente)
                .Include(c => c.Cuotas.Where(cu => !cu.IsDeleted).OrderBy(cu => cu.NumeroCuota))
                .AsSplitQuery()
                .FirstOrDefaultAsync(c => c.Id == cuota.CreditoId)
                ?? throw new DocumentoException(DocumentoErrores.ContextoIncompleto, $"La cuota {cuota.Id} no tiene crédito.");

            var venta = await VentaCompleta().FirstOrDefaultAsync(v => v.CreditoId == credito.Id && !v.IsDeleted);

            ctx.PagoCuotaId = pago.Id;
            ctx.CuotaId = cuota.Id;
            ctx.CreditoId = credito.Id;
            ctx.VentaId = venta?.Id;
            ctx.ClaveAncla = $"pago:{pago.Id}";

            // La operación completa está disponible para el recibo y para las condiciones (venta.*, credito.*, fiador.*, cuotas).
            if (venta != null)
            {
                await AplicarVentaAsync(ctx, venta);
            }
            else
            {
                AplicarCliente(ctx, credito.Cliente);
                AplicarCredito(ctx, credito);
            }

            ctx.Set("pago.numero", pago.Id.ToString(CultureInfo.InvariantCulture));
            ctx.Set("pago.fecha", pago.FechaPagoComercial.ToDateTime(TimeOnly.MinValue));
            ctx.Set("pago.importe", pago.ImporteTotal);
            ctx.Set("pago.importeEnLetras", NumeroALetras.Importe(pago.ImporteTotal));
            ctx.Set("pago.medioPago", pago.MedioPago);
            ctx.Set("pago.importeCuota", pago.ImporteAplicadoCuota);
            ctx.Set("pago.importePunitorio", pago.ImporteAplicadoPunitorio);
            ctx.Set("cuota.numero", cuota.NumeroCuota);
            ctx.Set("cuota.vencimiento", cuota.FechaVencimiento.Date);
            ctx.Set("cuota.estado", cuota.Estado.ToString());
        }

        private async Task CargarCotizacionAsync(DocumentoContexto ctx, int cotizacionId)
        {
            var cot = await _context.Cotizaciones
                .AsNoTracking()
                .Include(c => c.Cliente)
                .Include(c => c.Detalles.Where(d => !d.IsDeleted))
                .FirstOrDefaultAsync(c => c.Id == cotizacionId && !c.IsDeleted)
                ?? throw new DocumentoException(DocumentoErrores.ContextoIncompleto, $"No existe la cotización {cotizacionId}.");

            ctx.CotizacionId = cot.Id;
            ctx.ClaveAncla = $"cotizacion:{cot.Id}";

            if (cot.Cliente != null)
            {
                AplicarCliente(ctx, cot.Cliente);
            }
            else
            {
                ctx.Set("cliente.nombreCompleto", cot.NombreClienteLibre);
                ctx.Set("cliente.dni", cot.DniClienteLibre);
                ctx.Set("cliente.telefono", cot.TelefonoClienteLibre);
            }

            ctx.Set("cotizacion.numero", cot.Numero);
            ctx.Set("cotizacion.fecha", cot.Fecha.Date);
            ctx.Set("cotizacion.total", cot.TotalSeleccionado ?? cot.TotalBase);
            ctx.Set("cotizacion.vigencia", cot.FechaVencimiento?.Date);

            AplicarProductos(ctx, cot.Detalles.OrderBy(d => d.Id).Select(d => ItemProducto(
                d.CodigoProductoSnapshot, null, d.NombreProductoSnapshot, d.Cantidad, d.PrecioUnitarioSnapshot, d.Subtotal)));

            ctx.Set("credito.cantidadCuotas", cot.CantidadCuotasSeleccionada);
            ctx.Set("credito.importeCuota", cot.ValorCuotaSeleccionada);
        }
    }
}
