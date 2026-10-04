using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
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
        private readonly IPlanCuotasProyector? _proyector;

        public DocumentoContextoBuilder(AppDbContext context, IPlanCuotasProyector? proyector = null)
        {
            _context = context;
            _proyector = proyector;
        }

        public async Task<DocumentoContexto> ConstruirAsync(string evento, DocumentoOrigen origen)
        {
            var info = EventosDocumentales.Obtener(evento)
                ?? throw new DocumentoException(DocumentoErrores.ContextoIncompleto, $"Evento documental desconocido: {evento}.");

            var ctx = new DocumentoContexto { Ancla = info.Ancla };
            var empresa = await AplicarEmpresaAsync(ctx);

            switch (info.Ancla)
            {
                case AnclaDocumento.Pago:
                    var ids = origen.PagoCuotaIds is { Count: > 0 } lista
                        ? lista
                        : origen.PagoCuotaId is int unico ? new[] { unico } : throw new DocumentoException(DocumentoErrores.ContextoIncompleto, "El evento requiere un pago de cuota.");
                    await CargarPagoAsync(ctx, ids, empresa);
                    break;
                case AnclaDocumento.Cotizacion:
                    if (origen.CotizacionId is not int cotId)
                        throw new DocumentoException(DocumentoErrores.ContextoIncompleto, "El evento requiere una cotización.");
                    await CargarCotizacionAsync(ctx, cotId, empresa);
                    break;
                default:
                    if (origen.VentaId is not int ventaId)
                        throw new DocumentoException(DocumentoErrores.ContextoIncompleto, "El evento requiere una venta.");
                    await CargarVentaAsync(ctx, ventaId, empresa);
                    break;
            }

            return ctx;
        }

        /// <summary>
        /// Datos de la empresa: salen de su configuración propia (Configuración → Documentos → Empresa). Si todavía no
        /// existe se usa la plantilla de contrato vigente, que era la única fuente antes.
        /// </summary>
        private async Task<EmpresaConfiguracion?> AplicarEmpresaAsync(DocumentoContexto ctx)
        {
            var empresa = await _context.EmpresasConfiguracion.AsNoTracking().OrderBy(e => e.Id).FirstOrDefaultAsync();
            if (empresa != null)
            {
                AplicarEmpresa(ctx, empresa, null);
                return empresa;
            }

            var hoy = DateTime.UtcNow.Date;
            var plantilla = await _context.PlantillasContratoCredito
                .AsNoTracking()
                .Where(p => p.Activa && p.VigenteDesde.Date <= hoy && (!p.VigenteHasta.HasValue || p.VigenteHasta.Value.Date >= hoy))
                .OrderByDescending(p => p.VigenteDesde).ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync();

            AplicarEmpresa(ctx, plantilla);
            return null;
        }

        public static void AplicarEmpresa(DocumentoContexto ctx, EmpresaConfiguracion? empresa, PlantillaContratoCredito? respaldo)
        {
            if (empresa == null)
            {
                AplicarEmpresa(ctx, respaldo);
                return;
            }

            ctx.Set("empresa.nombre", empresa.Nombre);
            ctx.Set("empresa.cuit", empresa.Cuit);
            ctx.Set("empresa.dni", empresa.Dni);
            ctx.Set("empresa.direccion", empresa.Domicilio);
            ctx.Set("empresa.ciudad", empresa.Ciudad);
            ctx.Set("empresa.jurisdiccion", empresa.Jurisdiccion);
            ctx.Set("empresa.interesMoraDiario", empresa.InteresMoraDiarioPorcentaje.ToString("F4", Cultura));
            ctx.Set("empresa.nombreComercial", string.IsNullOrWhiteSpace(empresa.NombreComercial) ? empresa.Nombre : empresa.NombreComercial);
            ctx.Set("empresa.titular", empresa.Nombre);
            ctx.Set("empresa.titularDni", empresa.Dni);
            ctx.Set("empresa.direccionCompleta", string.IsNullOrWhiteSpace(empresa.DomicilioCompleto) ? empresa.Domicilio : empresa.DomicilioCompleto);
            ctx.Set("empresa.localidad", empresa.Ciudad);
            ctx.Set("contrato.interesMoraDiario", PorcentajeTexto(empresa.InteresMoraDiarioPorcentaje));
            // Datos de empresa que no son variables de plantilla pero que el contexto necesita más adelante.
            ctx.Valores["_condicionFiscalCliente"] = empresa.CondicionFiscalClientePorDefecto;
            ctx.Valores["_pagareModo"] = empresa.PagareVencimientoModo;
            ctx.Valores["_pagareDias"] = empresa.PagareVencimientoDias;
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
            ctx.Set("empresa.nombreComercial", p?.NombreVendedor);
            ctx.Set("empresa.titular", p?.NombreVendedor);
            ctx.Set("empresa.titularDni", p?.DniVendedor);
            ctx.Set("empresa.direccionCompleta", p?.DomicilioVendedor);
            ctx.Set("empresa.localidad", p?.CiudadFirma);
            ctx.Set("contrato.interesMoraDiario", p == null ? null : PorcentajeTexto(p.InteresMoraDiarioPorcentaje));
        }

        /// <summary>0,20% — dos decimales como mínimo, más si el valor los tiene.</summary>
        public static string PorcentajeTexto(decimal porcentaje) => porcentaje.ToString("0.00##", Cultura) + "%";

        public static void AplicarCliente(DocumentoContexto ctx, Cliente? c)
        {
            ctx.ClienteId = c?.Id;
            var condicionFiscal = ctx.Valores.TryGetValue("_condicionFiscalCliente", out var cf) ? cf as string : null;

            ctx.Set("cliente.nombre", c?.Nombre);
            ctx.Set("cliente.apellido", c?.Apellido);
            ctx.Set("cliente.nombreCompleto", c == null ? null : NombreParaDocumento(c.Apellido, c.Nombre));
            ctx.Set("cliente.tipoDocumento", c == null ? null : TipoDocumentoTexto(c.TipoDocumento));
            ctx.Set("cliente.dni", c?.NumeroDocumento);
            ctx.Set("cliente.numeroDocumento", c == null ? null : NumeroDocumentoTexto(c.TipoDocumento, c.NumeroDocumento));
            ctx.Set("cliente.cuit", c?.CuilCuit);
            ctx.Set("cliente.direccion", c?.Domicilio);
            ctx.Set("cliente.direccionCompleta", c == null ? null : DireccionCompleta(c.Domicilio, c.Localidad, c.CodigoPostal));
            ctx.Set("cliente.localidad", c?.Localidad);
            ctx.Set("cliente.codigoPostal", c?.CodigoPostal);
            ctx.Set("cliente.telefono", c?.Telefono);
            ctx.Set("cliente.email", c?.Email);
            ctx.Set("cliente.codigo", c == null ? null : FormatoArgentino.Codigo(c.Id));
            ctx.Set("cliente.condicionFiscal", condicionFiscal);
        }

        /// <summary>"Apellido Nombre", como se imprime en los comprobantes (sin el documento que agrega ToDisplayName).</summary>
        public static string NombreParaDocumento(string? apellido, string? nombre)
            => string.Join(' ', new[] { apellido, nombre }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

        public static string TipoDocumentoTexto(string? tipo)
            => string.Equals(tipo?.Trim(), "DNI", StringComparison.OrdinalIgnoreCase) ? "D.N.I." : (tipo ?? string.Empty).Trim();

        /// <summary>Un DNI de solo dígitos se imprime con puntos (7.701.419); cualquier otro documento, tal cual.</summary>
        public static string NumeroDocumentoTexto(string? tipo, string? numero)
        {
            var n = (numero ?? string.Empty).Trim();
            if (string.Equals(tipo?.Trim(), "DNI", StringComparison.OrdinalIgnoreCase) && n.Length is >= 6 and <= 9 && n.All(char.IsDigit))
                return long.Parse(n, CultureInfo.InvariantCulture).ToString("#,##0", CultureInfo.GetCultureInfo("es-AR"));
            return n;
        }

        /// <summary>"9 DE JULIO SEC QUINTAS N°: S/N - ABBOTT (7228)": domicilio, localidad y código postal cuando existen.</summary>
        public static string DireccionCompleta(string? domicilio, string? localidad, string? codigoPostal)
        {
            var texto = (domicilio ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(localidad))
                texto = texto.Length == 0 ? localidad.Trim() : $"{texto} - {localidad.Trim()}";
            if (!string.IsNullOrWhiteSpace(codigoPostal))
                texto = $"{texto} ({codigoPostal.Trim()})".Trim();
            return texto;
        }

        public static void AplicarFiador(DocumentoContexto ctx, Garante? g)
        {
            var gc = g?.GaranteCliente;
            var tipo = gc?.TipoDocumento ?? g?.TipoDocumento;
            var numero = gc?.NumeroDocumento ?? g?.NumeroDocumento;
            var domicilio = gc?.Domicilio ?? g?.Domicilio;

            ctx.Set("fiador.existe", g != null);
            ctx.Set("fiador.nombreCompleto", g == null ? null : (gc != null ? NombreParaDocumento(gc.Apellido, gc.Nombre) : NombreParaDocumento(g.Apellido, g.Nombre)));
            ctx.Set("fiador.dni", g == null ? null : numero);
            ctx.Set("fiador.tipoDocumento", g == null ? null : TipoDocumentoTexto(tipo));
            ctx.Set("fiador.numeroDocumento", g == null ? null : NumeroDocumentoTexto(tipo, numero));
            ctx.Set("fiador.direccion", g == null ? null : domicilio);
            ctx.Set("fiador.direccionCompleta", g == null ? null : DireccionCompleta(domicilio, gc?.Localidad, gc?.CodigoPostal));
            ctx.Set("fiador.relacion", g?.Relacion);
        }

        public static void AplicarOperacion(DocumentoContexto ctx, string? numero, DateTime fecha)
        {
            ctx.Set("operacion.numero", numero);
            ctx.Set("operacion.fecha", fecha.Date);
            ctx.Set("operacion.fechaTexto", FormatoArgentino.FechaTexto(fecha));
            ctx.Set("operacion.dia", fecha.Day);
            ctx.Set("operacion.mesTexto", FormatoArgentino.MesTexto(fecha.Month));
            ctx.Set("operacion.anio", fecha.Year);
        }

        public static void AplicarProductos(DocumentoContexto ctx, IEnumerable<Dictionary<string, object?>> productos)
        {
            var lista = productos.ToList();
            ctx.Colecciones["productos"] = lista;
            ctx.Set("venta.cantidadProductos", lista.Count);
            ctx.Set("venta.productosDetalle", string.Join(Environment.NewLine,
                lista.Select(p => $"{p["cantidadTexto"]} x {p["descripcion"]} ({p["codigo"]}) - {PlantillaRenderer.Formatear(p["subtotal"])}")));
        }

        public static void AplicarCuotas(DocumentoContexto ctx, IEnumerable<Dictionary<string, object?>> cuotas)
        {
            var lista = cuotas.ToList();
            ctx.Colecciones["cuotas"] = lista;
            ctx.Set("credito.planCuotasDetalle", string.Join(Environment.NewLine,
                lista.Select(c => $"Cuota {c["numero"]}: vence {PlantillaRenderer.Formatear(c["vencimiento"])} - {PlantillaRenderer.Formatear(c["importe"])}")));
        }

        /// <summary>
        /// Saldo financiado, entrega inicial y vencimiento del pagaré a partir de las cuotas YA cargadas (llamar después de
        /// <see cref="AplicarCuotas"/>). El saldo financiado es la suma de las cuotas del crédito; la entrega es el anticipo
        /// (venta menos monto financiado) y nunca se cuenta como deuda ni se mezcla con las cuotas.
        /// </summary>
        public static void AplicarFinanciacion(DocumentoContexto ctx, decimal entrega, DateTime fechaOperacion)
        {
            var cuotas = ctx.Colecciones.TryGetValue("cuotas", out var c) ? c : new List<Dictionary<string, object?>>();
            var saldo = cuotas.Sum(x => (decimal)x["importe"]!);

            entrega = Math.Max(0m, entrega);
            ctx.Set("credito.entregaInicial", entrega);
            ctx.Set("credito.entregaInicialFormato", FormatoArgentino.Importe(entrega));
            ctx.Set("credito.tieneEntrega", entrega > 0m);

            ctx.Set("credito.saldoFinanciado", cuotas.Count == 0 ? null : saldo);
            ctx.Set("credito.saldoFinanciadoFormato", cuotas.Count == 0 ? null : FormatoArgentino.Importe(saldo));
            ctx.Set("credito.saldoFinanciadoLetras", cuotas.Count == 0 ? null : NumeroALetras.Importe(saldo));

            var modo = ctx.Valores.TryGetValue("_pagareModo", out var m) ? m as string : null;
            var dias = ctx.Valores.TryGetValue("_pagareDias", out var d) ? d as int? : null;
            var vence = ResolverVencimientoPagare(modo, dias, cuotas, fechaOperacion);
            ctx.Set("pagare.fechaDefinida", vence != null);
            ctx.Set("pagare.fechaVencimiento", vence);
            ctx.Set("pagare.fechaVencimientoTexto", vence == null ? null : FormatoArgentino.FechaTexto(vence.Value));
        }

        /// <summary>
        /// Vencimiento del pagaré según la estrategia configurada en la empresa. Sin estrategia definida devuelve null:
        /// nunca se supone primera cuota, última cuota ni un plazo fijo.
        /// </summary>
        public static DateTime? ResolverVencimientoPagare(string? modo, int? dias, IReadOnlyList<Dictionary<string, object?>> cuotas, DateTime fechaOperacion)
        {
            switch (modo)
            {
                case PagareVencimientoModos.PrimeraCuota when cuotas.Count > 0:
                    return cuotas.Min(x => (DateTime)x["vencimiento"]!).Date;
                case PagareVencimientoModos.UltimaCuota when cuotas.Count > 0:
                    return cuotas.Max(x => (DateTime)x["vencimiento"]!).Date;
                case PagareVencimientoModos.DiasDesdeOperacion when dias is > 0:
                    return fechaOperacion.Date.AddDays(dias.Value);
                default:
                    return null;
            }
        }

        public static Dictionary<string, object?> ItemProducto(
            string? codigo, string? marca, string descripcion, decimal cantidad, decimal precio, decimal subtotal,
            int? productoId = null, string? subRubro = null)
            => new(StringComparer.OrdinalIgnoreCase)
            {
                ["codigo"] = codigo ?? string.Empty,
                ["codigoNumerico"] = productoId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["codigoAlfa"] = codigo ?? string.Empty,
                ["marca"] = marca ?? string.Empty,
                ["subRubro"] = subRubro ?? string.Empty,
                ["descripcion"] = descripcion,
                ["detalle"] = string.IsNullOrWhiteSpace(marca) ? descripcion : $"{marca.Trim()} - {descripcion}",
                ["cantidad"] = cantidad,
                ["cantidadTexto"] = CantidadTexto(cantidad),
                ["precio"] = precio,
                ["subtotal"] = subtotal
            };

        /// <summary>2 (no "$ 2,00"): entero si es entero, con decimales solo si los tiene.</summary>
        public static string CantidadTexto(decimal cantidad)
            => cantidad == Math.Truncate(cantidad) ? ((long)cantidad).ToString(CultureInfo.InvariantCulture) : cantidad.ToString("0.##", Cultura);

        public static Dictionary<string, object?> ItemCuota(int numero, decimal importe, decimal capital, decimal interes, DateTime vencimiento, string estado)
            => new(StringComparer.OrdinalIgnoreCase)
            {
                ["numero"] = numero,
                ["numeroFormateado"] = FormatoArgentino.NumeroDosDigitos(numero),
                ["importe"] = importe,
                ["importeFormato"] = FormatoArgentino.Importe(importe),
                ["capital"] = capital,
                ["interes"] = interes,
                ["vencimiento"] = vencimiento.Date,
                ["estado"] = estado
            };

        /// <summary>Condición de pago en texto para comprobantes: "Crédito", "Efectivo", "Tarjeta Débito"…</summary>
        public static string CondicionPagoTexto(TipoPago tipo)
        {
            if (tipo == TipoPago.CreditoPersonal)
                return "Crédito";

            var display = typeof(TipoPago).GetField(tipo.ToString())?.GetCustomAttribute<DisplayAttribute>()?.Name;
            return display ?? tipo.ToString();
        }

        private IQueryable<Venta> VentaCompleta()
            => _context.Ventas
                .AsNoTracking()
                .Include(v => v.Cliente)
                .Include(v => v.Credito).ThenInclude(c => c!.Garante).ThenInclude(g => g!.GaranteCliente)
                .Include(v => v.Credito).ThenInclude(c => c!.Cuotas.Where(cu => !cu.IsDeleted).OrderBy(cu => cu.NumeroCuota))
                .Include(v => v.Detalles.Where(d => !d.IsDeleted)).ThenInclude(d => d.Producto).ThenInclude(p => p!.Marca)
                .Include(v => v.Detalles.Where(d => !d.IsDeleted)).ThenInclude(d => d.Producto).ThenInclude(p => p!.Subcategoria)
                .Include(v => v.Detalles.Where(d => !d.IsDeleted)).ThenInclude(d => d.Producto).ThenInclude(p => p!.Categoria)
                .Include(v => v.AperturaCaja).ThenInclude(a => a!.Caja)
                .Include(v => v.VendedorUser)
                .AsSplitQuery();

        private async Task CargarVentaAsync(DocumentoContexto ctx, int ventaId, EmpresaConfiguracion? empresa)
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
            AplicarOperacion(ctx, venta.Numero, venta.FechaVenta);

            ctx.Set("venta.numero", venta.Numero);
            ctx.Set("venta.fecha", venta.FechaVenta.Date);
            ctx.Set("venta.total", venta.Total);
            ctx.Set("venta.subtotal", venta.Subtotal);
            ctx.Set("venta.tipoPago", venta.TipoPago.ToString());
            ctx.Set("venta.condicionPago", CondicionPagoTexto(venta.TipoPago));
            ctx.Set("venta.sucursal", venta.VendedorUser?.Sucursal);
            ctx.Set("venta.caja", venta.AperturaCaja?.Caja?.Nombre);

            AplicarProductos(ctx, venta.Detalles.OrderBy(d => d.Id).Select(d => ItemProducto(
                VentaDetalleProductoSnapshot.ResolverCodigo(d),
                d.Producto?.Marca?.Nombre,
                VentaDetalleProductoSnapshot.ResolverNombre(d),
                d.Cantidad,
                d.PrecioUnitario,
                d.SubtotalFinal > 0m ? d.SubtotalFinal : d.Subtotal,
                d.ProductoId,
                d.Producto?.Subcategoria?.Nombre ?? d.Producto?.Categoria?.Nombre)));

            var envio = await _context.VentaEnvios.AsNoTracking().FirstOrDefaultAsync(e => e.VentaId == venta.Id && !e.IsDeleted);
            ctx.Set("venta.conEnvio", envio != null);
            ctx.Set("entrega.fecha", envio?.FechaEntregaReal?.Date);
            ctx.Set("entrega.direccion", envio == null ? null : string.Join(", ", new[] { envio.Domicilio, envio.Localidad, envio.Provincia }.Where(x => !string.IsNullOrWhiteSpace(x))));
            ctx.Set("entrega.destinatario", envio?.Destinatario);

            await AplicarCreditoAsync(ctx, venta);
        }

        private async Task AplicarCreditoAsync(DocumentoContexto ctx, Venta venta)
        {
            var credito = venta.Credito;
            var plan = credito == null ? Array.Empty<PlanCuotaProyectada>() as IReadOnlyList<PlanCuotaProyectada>
                : _proyector != null ? await _proyector.ObtenerPlanAsync(venta, credito)
                : credito.Cuotas.Where(c => !c.IsDeleted).OrderBy(c => c.NumeroCuota)
                    .Select(c => new PlanCuotaProyectada(c.NumeroCuota, c.MontoCapital, c.MontoInteres, c.MontoTotal, c.FechaVencimiento, c.Estado.ToString())).ToList();

            AplicarDatosCredito(ctx, credito, plan);
            AplicarFinanciacion(ctx, credito == null ? 0m : venta.Total - credito.MontoAprobado, venta.FechaVenta);
        }

        public static void AplicarDatosCredito(DocumentoContexto ctx, Credito? credito, IEnumerable<PlanCuotaProyectada> plan)
        {
            ctx.Set("credito.numero", credito?.Numero);
            ctx.Set("credito.total", credito?.TotalAPagar);
            ctx.Set("credito.saldo", credito?.SaldoPendiente);
            ctx.Set("credito.montoFinanciado", credito?.MontoAprobado);
            ctx.Set("credito.cantidadCuotas", credito?.CantidadCuotas);
            ctx.Set("credito.importeCuota", credito?.MontoCuota);
            ctx.Set("credito.fechaPrimeraCuota", credito?.FechaPrimeraCuota?.Date);
            ctx.Set("credito.requiereFiador", credito?.RequiereGarante ?? false);

            AplicarFiador(ctx, credito?.Garante);
            AplicarCuotas(ctx, plan.Select(c => ItemCuota(c.Numero, c.Total, c.Capital, c.Interes, c.Vencimiento, c.Estado)));
        }

        private async Task CargarPagoAsync(DocumentoContexto ctx, IReadOnlyList<int> pagoCuotaIds, EmpresaConfiguracion? empresa)
        {
            var ids = pagoCuotaIds.Distinct().OrderBy(i => i).ToList();
            var pagos = await _context.PagosCuota
                .AsNoTracking()
                .Where(p => ids.Contains(p.Id) && !p.IsDeleted)
                .OrderBy(p => p.Id)
                .ToListAsync();
            if (pagos.Count != ids.Count)
                throw new DocumentoException(DocumentoErrores.ContextoIncompleto, $"No existe el pago {string.Join(", ", ids.Except(pagos.Select(p => p.Id)))}.");

            var pago = pagos[0];   // el pago ancla da la identidad (idempotencia) y la operación principal del recibo

            var cuotaIds = pagos.Select(p => p.CuotaId).Distinct().ToList();
            var cuotas = await _context.Cuotas.AsNoTracking().Where(c => cuotaIds.Contains(c.Id)).ToListAsync();
            var cuota = cuotas.FirstOrDefault(c => c.Id == pago.CuotaId)
                ?? throw new DocumentoException(DocumentoErrores.ContextoIncompleto, $"El pago {pago.Id} no tiene cuota.");

            // El crédito se carga aparte: Cuota→Credito→Cuotas sería un ciclo (no admitido en consultas sin tracking).
            var creditoIds = cuotas.Select(c => c.CreditoId).Distinct().ToList();
            var creditos = await _context.Creditos
                .AsNoTracking()
                .Include(c => c.Cliente)
                .Include(c => c.Garante).ThenInclude(g => g!.GaranteCliente)
                .Include(c => c.Cuotas.Where(cu => !cu.IsDeleted).OrderBy(cu => cu.NumeroCuota))
                .Where(c => creditoIds.Contains(c.Id))
                .AsSplitQuery()
                .ToListAsync();
            var credito = creditos.FirstOrDefault(c => c.Id == cuota.CreditoId)
                ?? throw new DocumentoException(DocumentoErrores.ContextoIncompleto, $"La cuota {cuota.Id} no tiene crédito.");

            var numerosOperacion = await _context.Ventas.AsNoTracking()
                .Where(v => v.CreditoId != null && creditoIds.Contains(v.CreditoId.Value) && !v.IsDeleted)
                .Select(v => new { CreditoId = v.CreditoId!.Value, v.Numero })
                .ToListAsync();

            var venta = await VentaCompleta().FirstOrDefaultAsync(v => v.CreditoId == credito.Id && !v.IsDeleted);

            ctx.PagoCuotaId = pago.Id;
            ctx.PagoCuotaIds.AddRange(ids);
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
                AplicarDatosCredito(ctx, credito, credito.Cuotas.Where(c => !c.IsDeleted).OrderBy(c => c.NumeroCuota)
                    .Select(c => new PlanCuotaProyectada(c.NumeroCuota, c.MontoCapital, c.MontoInteres, c.MontoTotal, c.FechaVencimiento, c.Estado.ToString())));
                AplicarFinanciacion(ctx, 0m, credito.FechaSolicitud);
            }

            var total = pagos.Sum(p => p.ImporteTotal);
            ctx.Set("pago.numero", pago.Id.ToString(CultureInfo.InvariantCulture));
            ctx.Set("pago.fecha", pago.FechaPagoComercial.ToDateTime(TimeOnly.MinValue));
            ctx.Set("pago.confirmado", pagos.All(p => p.Estado == EstadoPagoCuota.Aplicado));
            ctx.Set("pago.importe", total);
            ctx.Set("pago.importeTotal", total);
            ctx.Set("pago.importeTotalFormato", FormatoArgentino.Importe(total));
            ctx.Set("pago.importeTotalLetras", NumeroALetras.Importe(total));
            ctx.Set("pago.importeEnLetras", NumeroALetras.Importe(total));
            ctx.Set("pago.medioPago", string.Join(", ", pagos.Select(p => p.MedioPago).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct()));
            ctx.Set("pago.importeCuota", pagos.Sum(p => p.ImporteAplicadoCuota ?? 0m));
            ctx.Set("pago.importePunitorio", pagos.Sum(p => p.ImporteAplicadoPunitorio ?? 0m));
            ctx.Set("cuota.numero", cuota.NumeroCuota);
            ctx.Set("cuota.vencimiento", cuota.FechaVencimiento.Date);
            ctx.Set("cuota.estado", cuota.Estado.ToString());

            // Valores recibidos: un renglón por medio de pago realmente registrado (el ERP no guarda datos de cheque).
            ctx.Colecciones["medios"] = pagos
                .GroupBy(p => string.IsNullOrWhiteSpace(p.MedioPago) ? "Sin especificar" : p.MedioPago!.Trim())
                .Select(g => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["descripcion"] = g.Key,
                    ["numeroCheque"] = string.Empty,
                    ["banco"] = string.Empty,
                    ["observaciones"] = string.Empty,
                    ["fechaVencimiento"] = string.Empty,
                    ["importeFormato"] = FormatoArgentino.Importe(g.Sum(p => p.ImporteTotal))
                })
                .ToList();

            // Aplicado a: una línea por cuota cobrada, con su número real sobre el total de su crédito.
            ctx.Colecciones["imputaciones"] = pagos.Select(p =>
            {
                var c = cuotas.First(x => x.Id == p.CuotaId);
                var cr = creditos.First(x => x.Id == c.CreditoId);
                return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fecha"] = p.FechaPagoComercial.ToDateTime(TimeOnly.MinValue),
                    ["cuotaNumero"] = c.NumeroCuota,
                    ["cuotasTotal"] = cr.CantidadCuotas,
                    ["operacionNumero"] = numerosOperacion.FirstOrDefault(n => n.CreditoId == cr.Id)?.Numero ?? cr.Numero,
                    ["importeFormato"] = FormatoArgentino.Importe(p.ImporteTotal)
                };
            }).ToList();
        }

        private async Task CargarCotizacionAsync(DocumentoContexto ctx, int cotizacionId, EmpresaConfiguracion? empresa)
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
                ctx.Set("cliente.numeroDocumento", cot.DniClienteLibre);
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
