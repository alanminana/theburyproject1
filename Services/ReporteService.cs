using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TheBuryProject.Data;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class ReporteService : IReporteService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ReporteService> _logger;

        public ReporteService(
            AppDbContext context,
            ILogger<ReporteService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ReporteVentasResultadoViewModel> GenerarReporteVentasAsync(ReporteVentasFiltroViewModel filtro)
        {
            try
            {
                var query = _context.Ventas
                    .Include(v => v.Cliente)
                    .Include(v => v.VendedorUser)
                    .Include(v => v.Detalles.Where(d =>
                        !d.IsDeleted &&
                        d.Producto != null &&
                        !d.Producto.IsDeleted))
                        .ThenInclude(d => d.Producto)
                    .Where(v =>
                        !v.IsDeleted &&
                        (v.Cliente == null || !v.Cliente.IsDeleted))
                    .AsQueryable();

                // Aplicar filtros
                if (filtro.FechaDesde.HasValue)
                    query = query.Where(v => v.FechaVenta >= filtro.FechaDesde.Value);

                if (filtro.FechaHasta.HasValue)
                    query = query.Where(v => v.FechaVenta <= filtro.FechaHasta.Value);

                if (filtro.ClienteId.HasValue)
                    query = query.Where(v => v.ClienteId == filtro.ClienteId.Value);

                if (filtro.TipoPago.HasValue)
                    query = query.Where(v => v.TipoPago == filtro.TipoPago.Value);

                if (!string.IsNullOrWhiteSpace(filtro.VendedorId))
                    query = query.Where(v => v.VendedorUserId == filtro.VendedorId);


                if (filtro.ProductoId.HasValue)
                    query = query.Where(v => v.Detalles.Any(d =>
                        !d.IsDeleted &&
                        d.ProductoId == filtro.ProductoId.Value &&
                        d.Producto != null &&
                        !d.Producto.IsDeleted));

                if (filtro.CategoriaId.HasValue)
                    query = query.Where(v => v.Detalles.Any(d =>
                        !d.IsDeleted &&
                        d.Producto != null &&
                        !d.Producto.IsDeleted &&
                        d.Producto.CategoriaId == filtro.CategoriaId.Value));

                if (filtro.MarcaId.HasValue)
                    query = query.Where(v => v.Detalles.Any(d =>
                        !d.IsDeleted &&
                        d.Producto != null &&
                        !d.Producto.IsDeleted &&
                        d.Producto.MarcaId == filtro.MarcaId.Value));

                var ventas = await query.OrderByDescending(v => v.FechaVenta).ToListAsync();

                // Mapear a ViewModels
                var ventasItems = ventas.Select(v =>
                {
                    var detallesValidos = v.Detalles
                        .Where(d => !d.IsDeleted && d.Producto != null && !d.Producto.IsDeleted)
                        .ToList();

                    var costo = detallesValidos.Sum(d => d.Cantidad * d.Producto!.PrecioCompra);
                    var ganancia = v.Total - costo;

                    return new VentaReporteItemViewModel
                    {
                        Id = v.Id,
                        NumeroVenta = v.Numero,
                        FechaVenta = v.FechaVenta,
                        ClienteNombre = v.Cliente?.NombreCompleto ?? "Anónimo",
                        VendedorNombre = !string.IsNullOrWhiteSpace(v.VendedorNombre)
                            ? v.VendedorNombre
                            : v.VendedorUser?.UserName ?? "Sin asignar",
                        TipoPago = v.TipoPago,
                        Subtotal = v.Subtotal,
                        Descuento = v.Descuento,
                        Total = v.Total,
                        Costo = costo,
                        Ganancia = ganancia,
                        MargenPorcentaje = v.Total > 0
                            ? (ganancia / v.Total) * 100
                            : 0,
                        CantidadProductos = detallesValidos.Sum(d => (int)d.Cantidad)
                    };
                }).ToList();

                // Calcular estadísticas
                var totalVentas = ventasItems.Sum(v => v.Total);
                var totalCosto = ventasItems.Sum(v => v.Costo);
                var totalGanancia = totalVentas - totalCosto;
                var margenPromedio = totalVentas > 0 ? (totalGanancia / totalVentas) * 100 : 0;

                // Ventas por tipo de pago
                var ventasPorTipoPago = ventasItems
                    .GroupBy(v => v.TipoPago)
                    .ToDictionary(
                        g => g.Key.ToString(),
                        g => g.Sum(v => v.Total)
                    );

                // Productos más vendidos
                var productosMasVendidos = await ObtenerProductosMasVendidosAsync(filtro);

                // Clientes top
                var clientesTop = await ObtenerClientesTopAsync(filtro);

                return new ReporteVentasResultadoViewModel
                {
                    Ventas = ventasItems,
                    TotalVentas = totalVentas,
                    TotalCosto = totalCosto,
                    TotalGanancia = totalGanancia,
                    MargenPromedio = margenPromedio,
                    CantidadVentas = ventasItems.Count,
                    CantidadProductosVendidos = ventasItems.Sum(v => v.CantidadProductos),
                    TicketPromedio = ventasItems.Any() ? totalVentas / ventasItems.Count : 0,
                    VentasPorTipoPago = ventasPorTipoPago,
                    ProductosMasVendidos = productosMasVendidos,
                    ClientesTop = clientesTop
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar reporte de ventas");
                throw;
            }
        }

        public async Task<ReporteMargenesViewModel> GenerarReporteMargenesAsync(int? categoriaId = null, int? marcaId = null)
        {
            try
            {
                var query = _context.Productos
                    .Include(p => p.Categoria)
                    .Include(p => p.Marca)
                    .AsNoTracking()
                    .Where(p => !p.IsDeleted)
                    .AsQueryable();

                if (categoriaId.HasValue)
                    query = query.Where(p => p.CategoriaId == categoriaId.Value);

                if (marcaId.HasValue)
                    query = query.Where(p => p.MarcaId == marcaId.Value);

                var productos = await query.ToListAsync();

                // Obtener ventas de los últimos 30 días para cada producto
                var hace30Dias = DateTime.UtcNow.AddDays(-30);
                var ventasRecientes = await _context.VentaDetalles
                    .Include(vd => vd.Venta)
                    .Where(vd => !vd.IsDeleted &&
                                 vd.Venta != null &&
                                 !vd.Venta.IsDeleted &&
                                 vd.Venta.FechaVenta >= hace30Dias)
                    .GroupBy(vd => vd.ProductoId)
                    .Select(g => new
                    {
                        ProductoId = g.Key,
                        CantidadVendida = g.Sum(vd => vd.Cantidad)
                    })
                    .ToListAsync();

                var ventasPorProducto = ventasRecientes.ToDictionary(v => v.ProductoId, v => (int)v.CantidadVendida);

                var productosMargen = productos.Select(p =>
                {
                    var ganancia = p.PrecioVenta - p.PrecioCompra;
                    var margenPorcentaje = p.PrecioVenta > 0
                        ? (ganancia / p.PrecioVenta) * 100
                        : 0;

                    var ventasUltimos30Dias = ventasPorProducto.GetValueOrDefault(p.Id, 0);
                    var rotacionMensual = p.StockActual > 0
                        ? (ventasUltimos30Dias / p.StockActual) * 100
                        : 0;

                    return new ProductoMargenViewModel
                    {
                        Id = p.Id,
                        Codigo = p.Codigo,
                        Nombre = p.Nombre,
                        CategoriaNombre = p.Categoria?.Nombre ?? "",
                        MarcaNombre = p.Marca?.Nombre,
                        PrecioCompra = p.PrecioCompra,
                        PrecioVenta = p.PrecioVenta,
                        Ganancia = ganancia,
                        MargenPorcentaje = margenPorcentaje,
                        StockActual = p.StockActual,
                        GananciaPotencial = ganancia * p.StockActual,
                        VentasUltimos30Dias = ventasUltimos30Dias,
                        RotacionMensual = rotacionMensual
                    };
                }).ToList();

                return new ReporteMargenesViewModel
                {
                    Productos = productosMargen.OrderByDescending(p => p.GananciaPotencial).ToList(),
                    MargenPromedioGeneral = productosMargen.Any()
                        ? productosMargen.Average(p => p.MargenPorcentaje)
                        : 0,
                    GananciaTotalPotencial = productosMargen.Sum(p => p.GananciaPotencial),
                    ProductosConMargenBajo = productosMargen.Count(p => p.MargenPorcentaje < 20),
                    ProductosConMargenAlto = productosMargen.Count(p => p.MargenPorcentaje >= 35)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar reporte de márgenes");
                throw;
            }
        }

        public async Task<ReporteMorosidadViewModel> GenerarReporteMorosidadAsync()
        {
            try
            {
                var hoy = DateTime.UtcNow.Date;

                // Obtener todas las cuotas vencidas con sus créditos y clientes
                var cuotasVencidas = await _context.Cuotas
                    .Include(c => c.Credito)
                        .ThenInclude(cr => cr.Cliente)
                    .Where(c => !c.IsDeleted
                             && !c.Credito.IsDeleted
                             && !c.Credito.Cliente.IsDeleted
                             && c.FechaVencimiento < hoy
                             && c.Estado == EstadoCuota.Pendiente)
                    .ToListAsync();

                // Agrupar por cliente
                var clientesMorosos = cuotasVencidas
                    .GroupBy(c => new
                    {
                        ClienteId = c.Credito.ClienteId,
                        ClienteNombre = c.Credito.Cliente.NombreCompleto ?? "Sin nombre",
                        ClienteDocumento = c.Credito.Cliente.NumeroDocumento,
                        ClienteTelefono = c.Credito.Cliente.Telefono
                    })
                    .Select(g =>
                    {
                        var cuotaMasAntigua = g.OrderBy(c => c.FechaVencimiento).First();
                        var diasMaxAtraso = (hoy - g.Min(c => c.FechaVencimiento)).Days;

                        // Obtener cuotas vigentes del cliente
                        var clienteId = g.Key.ClienteId;
                        var cuotasVigentes = _context.Cuotas
                            .Include(c => c.Credito)
                            .Where(c => !c.IsDeleted
                                     && !c.Credito.IsDeleted
                                     && !c.Credito.Cliente.IsDeleted
                                     && c.Credito.ClienteId == clienteId
                                     && c.FechaVencimiento >= hoy
                                     && c.Estado == EstadoCuota.Pendiente)
                            .Sum(c => c.MontoTotal);

                        return new ClienteMorosoViewModel
                        {
                            ClienteId = g.Key.ClienteId,
                            ClienteNombre = g.Key.ClienteNombre,
                            ClienteDocumento = g.Key.ClienteDocumento,
                            ClienteTelefono = g.Key.ClienteTelefono,
                            CantidadCreditosVencidos = g.Select(c => c.CreditoId).Distinct().Count(),
                            TotalDeudaVencida = g.Sum(c => c.MontoTotal),
                            TotalDeudaVigente = cuotasVigentes,
                            FechaPrimerVencimiento = g.Min(c => c.FechaVencimiento),
                            DiasMaximoAtraso = diasMaxAtraso,
                            MontoCuotaVencidaMasAntigua = cuotaMasAntigua.MontoTotal,
                            CreditoIdMasAntiguo = cuotaMasAntigua.CreditoId
                        };
                    })
                    .OrderByDescending(c => c.DiasMaximoAtraso)
                    .ThenByDescending(c => c.TotalDeudaVencida)
                    .ToList();

                var totalDeudaVencida = clientesMorosos.Sum(c => c.TotalDeudaVencida);
                var totalDeudaVigente = clientesMorosos.Sum(c => c.TotalDeudaVigente);

                return new ReporteMorosidadViewModel
                {
                    ClientesMorosos = clientesMorosos,
                    TotalDeudaVencida = totalDeudaVencida,
                    TotalDeudaVigente = totalDeudaVigente,
                    CantidadClientesMorosos = clientesMorosos.Count,
                    CantidadCreditosVencidos = clientesMorosos.Sum(c => c.CantidadCreditosVencidos),
                    PromedioDeudaPorCliente = clientesMorosos.Any()
                        ? totalDeudaVencida / clientesMorosos.Count
                        : 0,
                    DeudaMayor30Dias = clientesMorosos.Where(c => c.DiasMaximoAtraso >= 30).Sum(c => c.TotalDeudaVencida),
                    DeudaMayor60Dias = clientesMorosos.Where(c => c.DiasMaximoAtraso >= 60).Sum(c => c.TotalDeudaVencida),
                    DeudaMayor90Dias = clientesMorosos.Where(c => c.DiasMaximoAtraso >= 90).Sum(c => c.TotalDeudaVencida)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar reporte de morosidad");
                throw;
            }
        }

        public async Task<List<VentasAgrupadasViewModel>> ObtenerVentasAgrupadasAsync(
            DateTime fechaDesde,
            DateTime fechaHasta,
            string agruparPor)
        {
            try
            {
                var ventas = await _context.Ventas
                    .Include(v => v.Detalles.Where(d =>
                        !d.IsDeleted &&
                        d.Producto != null &&
                        !d.Producto.IsDeleted))
                        .ThenInclude(d => d.Producto)
                            .ThenInclude(p => p.Categoria)
                    .Where(v => !v.IsDeleted && v.FechaVenta >= fechaDesde && v.FechaVenta <= fechaHasta)
                    .ToListAsync();

                return agruparPor?.ToLower() switch
                {
                    "dia" => ventas
                        .GroupBy(v => v.FechaVenta.Date)
                        .Select(g => new VentasAgrupadasViewModel
                        {
                            Etiqueta = g.Key.ToString("dd/MM/yyyy"),
                            Monto = g.Sum(v => v.Total),
                            Cantidad = g.Count(),
                            Ganancia = g.Sum(v => v.Total - v.Detalles.Where(d => !d.IsDeleted && d.Producto != null && !d.Producto.IsDeleted).Sum(d => d.Cantidad * d.Producto!.PrecioCompra))
                        })
                        .OrderBy(v => v.Etiqueta)
                        .ToList(),

                    "mes" => ventas
                        .GroupBy(v => new { v.FechaVenta.Year, v.FechaVenta.Month })
                        .Select(g => new VentasAgrupadasViewModel
                        {
                            Etiqueta = $"{g.Key.Month:D2}/{g.Key.Year}",
                            Monto = g.Sum(v => v.Total),
                            Cantidad = g.Count(),
                            Ganancia = g.Sum(v => v.Total - v.Detalles.Where(d => !d.IsDeleted && d.Producto != null && !d.Producto.IsDeleted).Sum(d => d.Cantidad * d.Producto!.PrecioCompra))
                        })
                        .OrderBy(v => v.Etiqueta)
                        .ToList(),

                    "categoria" => ventas
                        .SelectMany(v => v.Detalles)
                        .Where(d => !d.IsDeleted && d.Producto != null && !d.Producto.IsDeleted)
                        .GroupBy(d => d.Producto.Categoria.Nombre)
                        .Select(g => new VentasAgrupadasViewModel
                        {
                            Etiqueta = g.Key,
                            Monto = g.Sum(d => d.PrecioUnitario * d.Cantidad),
                            Cantidad = (int)g.Sum(d => d.Cantidad),
                            Ganancia = g.Sum(d => (d.PrecioUnitario - d.Producto!.PrecioCompra) * d.Cantidad)
                        })
                        .OrderByDescending(v => v.Monto)
                        .ToList(),

                    _ => new List<VentasAgrupadasViewModel>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener ventas agrupadas");
                throw;
            }
        }

        // Métodos auxiliares privados
        private async Task<List<ProductoMasVendidoViewModel>> ObtenerProductosMasVendidosAsync(ReporteVentasFiltroViewModel filtro)
        {
            var query = _context.VentaDetalles
                .Include(vd => vd.Venta)
                .Include(vd => vd.Producto)
                    .ThenInclude(p => p.Categoria)
                .Where(vd =>
                    !vd.IsDeleted &&
                    vd.Venta != null &&
                    !vd.Venta.IsDeleted &&
                    vd.Producto != null &&
                    !vd.Producto.IsDeleted)
                .AsQueryable();

            if (filtro.FechaDesde.HasValue)
                query = query.Where(vd => vd.Venta.FechaVenta >= filtro.FechaDesde.Value);

            if (filtro.FechaHasta.HasValue)
                query = query.Where(vd => vd.Venta.FechaVenta <= filtro.FechaHasta.Value);

            var productos = await query
                .GroupBy(vd => new
                {
                    vd.ProductoId,
                    vd.Producto.Codigo,
                    vd.Producto.Nombre,
                    CategoriaNombre = vd.Producto.Categoria.Nombre,
                    vd.Producto.PrecioCompra
                })
                .Select(g => new ProductoMasVendidoViewModel
                {
                    ProductoId = g.Key.ProductoId,
                    ProductoCodigo = g.Key.Codigo,
                    ProductoNombre = g.Key.Nombre,
                    CategoriaNombre = g.Key.CategoriaNombre,
                    CantidadVendida = (int)g.Sum(vd => vd.Cantidad),
                    MontoTotal = g.Sum(vd => vd.PrecioUnitario * vd.Cantidad),
                    GananciaTotal = g.Sum(vd => (vd.PrecioUnitario - g.Key.PrecioCompra) * vd.Cantidad),
                    MargenPromedio = g.Average(vd => ((vd.PrecioUnitario - g.Key.PrecioCompra) / vd.PrecioUnitario) * 100)
                })
                .OrderByDescending(p => p.CantidadVendida)
                .Take(10)
                .ToListAsync();

            return productos;
        }

        private async Task<List<ClienteTopViewModel>> ObtenerClientesTopAsync(ReporteVentasFiltroViewModel filtro)
        {
            var query = _context.Ventas
                .Include(v => v.Cliente)
                .Where(v => !v.IsDeleted && v.Cliente != null && !v.Cliente.IsDeleted)
                .AsQueryable();

            if (filtro.FechaDesde.HasValue)
                query = query.Where(v => v.FechaVenta >= filtro.FechaDesde.Value);

            if (filtro.FechaHasta.HasValue)
                query = query.Where(v => v.FechaVenta <= filtro.FechaHasta.Value);

            var clientes = await query
                .GroupBy(v => new
                {
                    v.ClienteId,
                    ClienteNombre = v.Cliente!.NombreCompleto,
                    ClienteDocumento = v.Cliente.NumeroDocumento
                })
                .Select(g => new ClienteTopViewModel
                {
                    ClienteId = g.Key.ClienteId,
                    ClienteNombre = g.Key.ClienteNombre ?? "",
                    ClienteDocumento = g.Key.ClienteDocumento,
                    CantidadCompras = g.Count(),
                    MontoTotal = g.Sum(v => v.Total),
                    TicketPromedio = g.Average(v => v.Total),
                    UltimaCompra = g.Max(v => v.FechaVenta)
                })
                .OrderByDescending(c => c.MontoTotal)
                .Take(10)
                .ToListAsync();

            return clientes;
        }

        // Métodos de exportación a Excel
        public async Task<byte[]> ExportarVentasExcelAsync(ReporteVentasFiltroViewModel filtro)
        {
            try
            {
                var reporte = await GenerarReporteVentasAsync(filtro);

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Ventas");

                // Encabezados
                worksheet.Cell(1, 1).Value = "Nº Venta";
                worksheet.Cell(1, 2).Value = "Fecha";
                worksheet.Cell(1, 3).Value = "Cliente";
                worksheet.Cell(1, 4).Value = "Vendedor";
                worksheet.Cell(1, 5).Value = "Tipo Pago";
                worksheet.Cell(1, 6).Value = "Subtotal";
                worksheet.Cell(1, 7).Value = "Descuento";
                worksheet.Cell(1, 8).Value = "Total";
                worksheet.Cell(1, 9).Value = "Ganancia";
                worksheet.Cell(1, 10).Value = "Margen %";

                // Estilo de encabezados
                var headerRange = worksheet.Range("A1:J1");
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;

                // Datos
                int row = 2;
                foreach (var venta in reporte.Ventas)
                {
                    worksheet.Cell(row, 1).Value = venta.NumeroVenta;
                    worksheet.Cell(row, 2).Value = venta.FechaVenta;
                    worksheet.Cell(row, 3).Value = venta.ClienteNombre;
                    worksheet.Cell(row, 4).Value = venta.VendedorNombre;
                    worksheet.Cell(row, 5).Value = venta.TipoPagoDescripcion;
                    worksheet.Cell(row, 6).Value = venta.Subtotal;
                    worksheet.Cell(row, 7).Value = venta.Descuento;
                    worksheet.Cell(row, 8).Value = venta.Total;
                    worksheet.Cell(row, 9).Value = venta.Ganancia;
                    worksheet.Cell(row, 10).Value = venta.MargenPorcentaje;
                    row++;
                }

                // Totales
                worksheet.Cell(row, 5).Value = "TOTALES:";
                worksheet.Cell(row, 5).Style.Font.Bold = true;
                worksheet.Cell(row, 6).Value = reporte.Ventas.Sum(v => v.Subtotal);
                worksheet.Cell(row, 7).Value = reporte.Ventas.Sum(v => v.Descuento);
                worksheet.Cell(row, 8).Value = reporte.TotalVentas;
                worksheet.Cell(row, 9).Value = reporte.TotalGanancia;
                worksheet.Cell(row, 10).Value = reporte.MargenPromedio;

                // Formatear como moneda
                worksheet.Range($"F2:I{row}").Style.NumberFormat.Format = "$#,##0.00";
                worksheet.Range($"J2:J{row}").Style.NumberFormat.Format = "0.00";

                // Ajustar ancho de columnas
                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al exportar ventas a Excel");
                throw;
            }
        }

        public async Task<byte[]> ExportarMargenesExcelAsync(int? categoriaId = null, int? marcaId = null)
        {
            try
            {
                var reporte = await GenerarReporteMargenesAsync(categoriaId, marcaId);

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Margenes");

                // Encabezados
                worksheet.Cell(1, 1).Value = "Código";
                worksheet.Cell(1, 2).Value = "Producto";
                worksheet.Cell(1, 3).Value = "Categoría";
                worksheet.Cell(1, 4).Value = "Marca";
                worksheet.Cell(1, 5).Value = "P. Compra";
                worksheet.Cell(1, 6).Value = "P. Venta";
                worksheet.Cell(1, 7).Value = "Ganancia";
                worksheet.Cell(1, 8).Value = "Margen %";
                worksheet.Cell(1, 9).Value = "Stock";
                worksheet.Cell(1, 10).Value = "Ganancia Potencial";

                var headerRange = worksheet.Range("A1:J1");
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightGreen;

                int row = 2;
                foreach (var producto in reporte.Productos)
                {
                    worksheet.Cell(row, 1).Value = producto.Codigo;
                    worksheet.Cell(row, 2).Value = producto.Nombre;
                    worksheet.Cell(row, 3).Value = producto.CategoriaNombre;
                    worksheet.Cell(row, 4).Value = producto.MarcaNombre;
                    worksheet.Cell(row, 5).Value = producto.PrecioCompra;
                    worksheet.Cell(row, 6).Value = producto.PrecioVenta;
                    worksheet.Cell(row, 7).Value = producto.Ganancia;
                    worksheet.Cell(row, 8).Value = producto.MargenPorcentaje;
                    worksheet.Cell(row, 9).Value = producto.StockActual;
                    worksheet.Cell(row, 10).Value = producto.GananciaPotencial;
                    row++;
                }

                worksheet.Range($"E2:G{row}").Style.NumberFormat.Format = "$#,##0.00";
                worksheet.Range($"H2:H{row}").Style.NumberFormat.Format = "0.00";
                worksheet.Range($"J2:J{row}").Style.NumberFormat.Format = "$#,##0.00";
                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al exportar márgenes a Excel");
                throw;
            }
        }

        public async Task<byte[]> ExportarMorosidadExcelAsync()
        {
            try
            {
                var reporte = await GenerarReporteMorosidadAsync();

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Morosidad");

                // Encabezados
                worksheet.Cell(1, 1).Value = "Cliente";
                worksheet.Cell(1, 2).Value = "Documento";
                worksheet.Cell(1, 3).Value = "Teléfono";
                worksheet.Cell(1, 4).Value = "Créditos Vencidos";
                worksheet.Cell(1, 5).Value = "Deuda Vencida";
                worksheet.Cell(1, 6).Value = "Deuda Vigente";
                worksheet.Cell(1, 7).Value = "Días Atraso";
                worksheet.Cell(1, 8).Value = "Nivel Riesgo";

                var headerRange = worksheet.Range("A1:H1");
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightCoral;

                int row = 2;
                foreach (var cliente in reporte.ClientesMorosos)
                {
                    worksheet.Cell(row, 1).Value = cliente.ClienteNombre;
                    worksheet.Cell(row, 2).Value = cliente.ClienteDocumento;
                    worksheet.Cell(row, 3).Value = cliente.ClienteTelefono;
                    worksheet.Cell(row, 4).Value = cliente.CantidadCreditosVencidos;
                    worksheet.Cell(row, 5).Value = cliente.TotalDeudaVencida;
                    worksheet.Cell(row, 6).Value = cliente.TotalDeudaVigente;
                    worksheet.Cell(row, 7).Value = cliente.DiasMaximoAtraso;
                    worksheet.Cell(row, 8).Value = cliente.NivelRiesgo;
                    row++;
                }

                worksheet.Range($"E2:F{row}").Style.NumberFormat.Format = "$#,##0.00";
                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al exportar morosidad a Excel");
                throw;
            }
        }

        // Métodos de exportación a PDF
        public async Task<byte[]> GenerarVentasPdfAsync(ReporteVentasFiltroViewModel filtro)
        {
            try
            {
                QuestPDF.Settings.License = LicenseType.Community;
                var reporte = await GenerarReporteVentasAsync(filtro);

                var documento = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(2, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10));

                        page.Header().Text("Reporte de Ventas")
                            .SemiBold().FontSize(20).FontColor(Colors.Blue.Medium);

                        page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                        {
                            // Resumen
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"Total Ventas: ${reporte.TotalVentas:N2}").Bold();
                                row.RelativeItem().Text($"Ganancia: ${reporte.TotalGanancia:N2}").Bold();
                                row.RelativeItem().Text($"Margen: {reporte.MargenPromedio:N1}%").Bold();
                            });

                            col.Item().PaddingVertical(5);

                            // Tabla
                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(60);
                                    columns.ConstantColumn(70);
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(80);
                                    columns.ConstantColumn(70);
                                    columns.ConstantColumn(70);
                                    columns.ConstantColumn(70);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Nº Venta").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Fecha").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Cliente").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Tipo Pago").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Total").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Ganancia").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Margen %").Bold();
                                });

                                foreach (var venta in reporte.Ventas.Take(50)) // Limitar a 50 para PDF
                                {
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(venta.NumeroVenta);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(venta.FechaVenta.ToString("dd/MM/yy"));
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(venta.ClienteNombre);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(venta.TipoPagoDescripcion);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text($"${venta.Total:N2}");
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text($"${venta.Ganancia:N2}");
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text($"{venta.MargenPorcentaje:N1}%");
                                }
                            });
                        });

                        page.Footer().AlignCenter().Text(text =>
                        {
                            text.Span("Página ");
                            text.CurrentPageNumber();
                            text.Span(" de ");
                            text.TotalPages();
                        });
                    });
                });

                return documento.GeneratePdf();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar PDF de ventas");
                throw;
            }
        }

        public async Task<byte[]> GenerarMorosidadPdfAsync()
        {
            try
            {
                QuestPDF.Settings.License = LicenseType.Community;
                var reporte = await GenerarReporteMorosidadAsync();

                var documento = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(2, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10));

                        page.Header().Text("Reporte de Morosidad")
                            .SemiBold().FontSize(20).FontColor(Colors.Red.Medium);

                        page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                        {
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"Deuda Vencida: ${reporte.TotalDeudaVencida:N2}").Bold();
                                row.RelativeItem().Text($"Clientes: {reporte.CantidadClientesMorosos}").Bold();
                            });

                            col.Item().PaddingVertical(5);

                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(80);
                                    columns.ConstantColumn(70);
                                    columns.ConstantColumn(70);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Cliente").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Documento").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Deuda").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Días").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Riesgo").Bold();
                                });

                                foreach (var cliente in reporte.ClientesMorosos)
                                {
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(cliente.ClienteNombre);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(cliente.ClienteDocumento);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text($"${cliente.TotalDeudaVencida:N2}");
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(cliente.DiasMaximoAtraso.ToString());
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(cliente.NivelRiesgo);
                                }
                            });
                        });

                        page.Footer().AlignCenter().Text(text =>
                        {
                            text.Span("Generado el ");
                            text.Span(DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
                        });
                    });
                });

                return documento.GeneratePdf();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar PDF de morosidad");
                throw;
            }
        }
    }
}
