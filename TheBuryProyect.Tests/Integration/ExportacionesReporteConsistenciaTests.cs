using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// QA de exportaciones (Excel/PDF): cada archivo se abre y se contrasta contra un oráculo SQL independiente del
/// servicio (consultas crudas sobre las tablas). Un único escenario sembrado cubre ventas facturadas/anuladas,
/// devoluciones parciales/totales, cliente dado de baja, descuento/recargo, crédito personal, límites de fecha y
/// cambio de mes. Corre sobre SQLite en memoria; con <c>QA_EXPORT_SQL</c> (catálogo que empiece con "qa_export")
/// corre sobre SQL Server real, y con <c>QA_EXPORT_OUT</c> deja los archivos generados para contraste externo.
/// </summary>
public class ExportacionesReporteConsistenciaTests : IDisposable
{
    private static readonly DateTime Hoy = new(2026, 11, 20);

    private static readonly object SqlLock = new();
    private static bool SqlSembrada;

    private readonly SqliteConnection? _sqlite;
    private readonly AppDbContext _context;
    private readonly ReporteService _service;
    private readonly string? _outDir = Environment.GetEnvironmentVariable("QA_EXPORT_OUT");

    private Cliente _clienteActivo = null!;
    private Cliente _clienteBaja = null!;
    private Producto _producto = null!;

    public ExportacionesReporteConsistenciaTests()
    {
        var sql = Environment.GetEnvironmentVariable("QA_EXPORT_SQL");
        DbContextOptions<AppDbContext> options;
        if (!string.IsNullOrWhiteSpace(sql))
        {
            var catalogo = new SqlConnectionStringBuilder(sql).InitialCatalog;
            if (!catalogo.StartsWith("qa_export", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Catálogo '{catalogo}' no empieza con 'qa_export' (protección de la BD real).");
            options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(sql).Options;
            _context = new AppDbContext(options);
            // SQL Server: la base se migra y se siembra UNA vez por corrida (las pruebas solo leen); los esquemas reales
            // se crean con migraciones (EnsureCreated no sirve: ciclos de cascada del modelo).
            lock (SqlLock)
            {
                if (!SqlSembrada)
                {
                    _context.Database.EnsureDeleted();
                    _context.Database.Migrate();
                    SembrarEscenarioAsync().GetAwaiter().GetResult();
                    SqlSembrada = true;
                }
            }
        }
        else
        {
            _sqlite = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            _sqlite.Open();
            options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_sqlite).Options;
            _context = new AppDbContext(options);
            _context.Database.EnsureCreated();
        }

        _service = new ReporteService(_context, NullLogger<ReporteService>.Instance, null,
            new RelojComercialFake(DateOnly.FromDateTime(Hoy)));
        if (_sqlite != null)
            SembrarEscenarioAsync().GetAwaiter().GetResult();
        else
            CargarReferenciasAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _context.Dispose();
        _sqlite?.Dispose();
    }

    // ------------------------------------------------------------------ escenario

    private async Task SembrarEscenarioAsync()
    {
        _clienteActivo = await CrearClienteAsync("Activo", "Perez", "00123456");
        _clienteBaja = await CrearClienteAsync("Baja", "Gomez", "20111222");
        _producto = await CrearProductoAsync();

        // Noviembre 2026 (período auditado) + vecinos de octubre/diciembre para los límites.
        await VentaAsync("V-FACT", _clienteActivo, 2, new DateTime(2026, 11, 10, 10, 0, 0), EstadoVenta.Facturada);          // 242
        await VentaAsync("V-CANC", _clienteActivo, 2, new DateTime(2026, 11, 11, 10, 0, 0), EstadoVenta.Cancelada);          // excluida
        await VentaAsync("V-BAJA", _clienteBaja, 1, new DateTime(2026, 11, 12, 10, 0, 0), EstadoVenta.Entregada);            // 121 (cliente luego dado de baja)
        await VentaAsync("V-LIM-FIN", _clienteActivo, 1, new DateTime(2026, 11, 30, 23, 59, 59).AddTicks(9999999 % 10_000_000), EstadoVenta.Confirmada); // último instante de noviembre
        await VentaAsync("V-LIM-DIC", _clienteActivo, 1, new DateTime(2026, 12, 1, 0, 0, 0), EstadoVenta.Facturada);         // diciembre
        await VentaAsync("V-LIM-INI", _clienteActivo, 1, new DateTime(2026, 11, 1, 0, 0, 0), EstadoVenta.Facturada);         // primer instante
        await VentaAsync("V-LIM-OCT", _clienteActivo, 1, new DateTime(2026, 10, 31, 23, 59, 59), EstadoVenta.Facturada);     // octubre
        await VentaAsync("V-PRES", _clienteActivo, 1, new DateTime(2026, 11, 13, 10, 0, 0), EstadoVenta.Presupuesto);        // no comercial
        await VentaAsync("V-ELIM", _clienteActivo, 1, new DateTime(2026, 11, 13, 11, 0, 0), EstadoVenta.Facturada, eliminada: true);

        // Débito con recargo (254,10) y venta con descuento comercial (Total 222 = 242 - 20 con IVA).
        var debito = await VentaAsync("V-DEB", _clienteActivo, 2, new DateTime(2026, 11, 14, 10, 0, 0), EstadoVenta.Facturada,
            tipoPago: TipoPago.TarjetaDebito, totalOverride: 254.10m, ivaOverride: 44.10m);
        _context.DatosTarjeta.Add(new DatosTarjeta
        {
            VentaId = debito.Id, NombreTarjeta = "Debito", TipoTarjeta = TipoTarjeta.Debito, RecargoAplicado = 12.10m
        });
        await _context.SaveChangesAsync();
        var desc = await VentaAsync("V-DESC", _clienteActivo, 2, new DateTime(2026, 11, 15, 10, 0, 0), EstadoVenta.Facturada,
            totalOverride: 222m, ivaOverride: 38.53m);
        desc.Descuento = 20m;
        await _context.SaveChangesAsync();

        // Crédito personal (363) con cuotas vencidas: cliente activo y cliente dado de baja.
        await VentaAsync("V-CRED", _clienteActivo, 3, new DateTime(2026, 11, 16, 10, 0, 0), EstadoVenta.Facturada, tipoPago: TipoPago.CreditoPersonal);

        // Relleno: 55 ventas de 100 (supera el tope de 50 filas que tenía el PDF).
        for (var i = 0; i < 55; i++)
            await VentaAsync($"V-REL-{i:D2}", _clienteActivo, 1, new DateTime(2026, 11, 5, 9, 0, 0).AddMinutes(i), EstadoVenta.Facturada, brutoUnitario: 100m);

        // Devoluciones: parcial (V-FACT, Completada), total (V-BAJA, Aprobada), de una venta de octubre (cuenta en noviembre),
        // rechazada (no resta), pendiente (no resta) y de venta cancelada (no resta).
        await DevolucionAsync("V-FACT", 1, 121m, EstadoDevolucion.Completada, new DateTime(2026, 11, 16, 12, 0, 0));
        await DevolucionAsync("V-BAJA", 1, 121m, EstadoDevolucion.Aprobada, new DateTime(2026, 11, 17, 12, 0, 0));
        await DevolucionAsync("V-LIM-OCT", 1, 121m, EstadoDevolucion.Completada, new DateTime(2026, 11, 18, 12, 0, 0));
        await DevolucionAsync("V-LIM-INI", 1, 121m, EstadoDevolucion.Rechazada, new DateTime(2026, 11, 18, 13, 0, 0));
        await DevolucionAsync("V-LIM-FIN", 1, 121m, EstadoDevolucion.Pendiente, new DateTime(2026, 11, 18, 14, 0, 0));
        await DevolucionAsync("V-CANC", 1, 121m, EstadoDevolucion.Completada, new DateTime(2026, 11, 18, 15, 0, 0));

        // Morosidad: cliente activo y cliente dado de baja, con y sin pago parcial.
        await CuotaVencidaAsync(_clienteActivo, 1_000m, 400m, 40);
        await CuotaVencidaAsync(_clienteBaja, 500m, 0m, 5);

        // Movimientos de stock valorizados (noviembre y uno de diciembre, fuera de período).
        var fechaMov = new DateTime(2026, 11, 10, 12, 0, 0);
        _context.MovimientosStock.AddRange(
            Mov(TipoMovimiento.Entrada, 10m, 600m, fechaMov),
            Mov(TipoMovimiento.Salida, 3m, 180m, fechaMov.AddMinutes(1)),
            Mov(TipoMovimiento.Ajuste, -2m, 120m, fechaMov.AddMinutes(2)),
            Mov(TipoMovimiento.Entrada, 1m, 60m, new DateTime(2026, 12, 1)));
        await _context.SaveChangesAsync();

        // Se da de baja al cliente DESPUÉS de operar: su histórico debe seguir contando.
        _clienteBaja.IsDeleted = true;
        await _context.SaveChangesAsync();
    }

    private readonly Dictionary<string, Venta> _ventas = new();
    private int _secuenciaDevolucion;

    private async Task CargarReferenciasAsync()
    {
        _clienteActivo = await _context.Clientes.SingleAsync(c => c.NumeroDocumento == "00123456");
        _clienteBaja = await _context.Clientes.IgnoreQueryFilters().SingleAsync(c => c.NumeroDocumento == "20111222");
        _producto = await _context.Productos.SingleAsync(p => p.Codigo == "00042");
    }

    private async Task<Cliente> CrearClienteAsync(string nombre, string apellido, string documento)
    {
        var cliente = new Cliente
        {
            Nombre = nombre, Apellido = apellido, TipoDocumento = "DNI", NumeroDocumento = documento,
            Telefono = "011-4444-0001", Email = $"{documento}@test.com", Activo = true
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Producto> CrearProductoAsync()
    {
        var cat = new Categoria { Codigo = "C1", Nombre = "Categoria 1", Activo = true };
        var marca = new Marca { Codigo = "M1", Nombre = "Marca 1", Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        var producto = new Producto
        {
            Codigo = "00042", Nombre = "Producto Uno", CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = 60m, PrecioVenta = 121m, PorcentajeIVA = 21m, StockActual = 50m, StockMinimo = 1m, Activo = true
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task<Venta> VentaAsync(
        string numero, Cliente cliente, int cantidad, DateTime fecha, EstadoVenta estado,
        TipoPago tipoPago = TipoPago.Efectivo, decimal brutoUnitario = 121m,
        decimal? totalOverride = null, decimal? ivaOverride = null, bool eliminada = false)
    {
        var bruto = cantidad * brutoUnitario;
        var neto = Math.Round(bruto / 1.21m, 2, MidpointRounding.AwayFromZero);
        var total = totalOverride ?? bruto;
        var iva = ivaOverride ?? bruto - neto;
        var venta = new Venta
        {
            Numero = numero,
            ClienteId = cliente.Id,
            Estado = estado,
            TipoPago = tipoPago,
            FechaVenta = fecha,
            Subtotal = total - iva,
            IVA = iva,
            Total = total,
            IsDeleted = eliminada,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = _producto.Id, Cantidad = cantidad, PrecioUnitario = brutoUnitario,
                    Subtotal = bruto, SubtotalFinal = bruto, PorcentajeIVA = 21m,
                    SubtotalNeto = neto, SubtotalFinalNeto = neto,
                    SubtotalIVA = bruto - neto, SubtotalFinalIVA = bruto - neto,
                    CostoUnitarioAlMomento = 60m, CostoTotalAlMomento = 60m * cantidad
                }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        _ventas[numero] = venta;
        return venta;
    }

    private async Task DevolucionAsync(string numeroVenta, int cantidad, decimal importe, EstadoDevolucion estado, DateTime fecha)
    {
        var venta = _ventas[numeroVenta];
        _context.Devoluciones.Add(new Devolucion
        {
            VentaId = venta.Id,
            ClienteId = venta.ClienteId!.Value,
            NumeroDevolucion = $"DV-{++_secuenciaDevolucion:D4}",
            FechaDevolucion = fecha,
            Motivo = MotivoDevolucion.DefectoFabrica,
            Descripcion = "qa",
            Estado = estado,
            TotalDevolucion = importe,
            Detalles = new List<DevolucionDetalle>
            {
                new() { ProductoId = _producto.Id, Cantidad = cantidad, PrecioUnitario = importe / cantidad, Subtotal = importe,
                        EstadoProducto = EstadoProductoDevuelto.Nuevo }
            }
        });
        await _context.SaveChangesAsync();
    }

    private async Task CuotaVencidaAsync(Cliente cliente, decimal total, decimal pagado, int diasVencida)
    {
        var credito = new Credito
        {
            Numero = "CR-" + cliente.NumeroDocumento, ClienteId = cliente.Id, Estado = EstadoCredito.Activo,
            MontoSolicitado = total, MontoAprobado = total, SaldoPendiente = total - pagado, TasaInteres = 0m,
            CantidadCuotas = 1, FechaSolicitud = Hoy.AddDays(-90)
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();
        _context.Cuotas.Add(new Cuota
        {
            CreditoId = credito.Id, NumeroCuota = 1, MontoCapital = total, MontoInteres = 0m, MontoTotal = total,
            MontoPagado = pagado, Estado = pagado > 0 ? EstadoCuota.Parcial : EstadoCuota.Pendiente,
            FechaVencimiento = Hoy.AddDays(-diasVencida)
        });
        await _context.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ oráculo SQL (independiente del servicio)

    private const string Comercial = "Estado IN (2,3,4) AND IsDeleted = 0";

    private async Task<decimal> SqlEscalarAsync(string sql, params object[] parametros)
    {
        var fila = await _context.Database.SqlQueryRaw<double>(sql, parametros).ToListAsync();
        return Math.Round((decimal)fila.Single(), 2);
    }

    private static DateTime[] Mes(int mes) =>
        new[] { new DateTime(2026, mes, 1), new DateTime(2026, mes, 1).AddMonths(1) };

    private Task<decimal> SqlTotalVentasAsync(DateTime desde, DateTime hastaExclusivo) =>
        SqlEscalarAsync($"SELECT CAST(COALESCE(SUM(Total),0) AS FLOAT) AS Value FROM Ventas WHERE {Comercial} AND FechaVenta >= {{0}} AND FechaVenta < {{1}}", desde, hastaExclusivo);

    private Task<decimal> SqlTotalIvaAsync(DateTime desde, DateTime hastaExclusivo) =>
        SqlEscalarAsync($"SELECT CAST(COALESCE(SUM(IVA),0) AS FLOAT) AS Value FROM Ventas WHERE {Comercial} AND FechaVenta >= {{0}} AND FechaVenta < {{1}}", desde, hastaExclusivo);

    private Task<decimal> SqlTotalSubtotalAsync(DateTime desde, DateTime hastaExclusivo) =>
        SqlEscalarAsync($"SELECT CAST(COALESCE(SUM(Subtotal),0) AS FLOAT) AS Value FROM Ventas WHERE {Comercial} AND FechaVenta >= {{0}} AND FechaVenta < {{1}}", desde, hastaExclusivo);

    private async Task<int> SqlCantidadVentasAsync(DateTime desde, DateTime hastaExclusivo) =>
        (int)await SqlEscalarAsync($"SELECT CAST(COUNT(*) AS FLOAT) AS Value FROM Ventas WHERE {Comercial} AND FechaVenta >= {{0}} AND FechaVenta < {{1}}", desde, hastaExclusivo);

    /// <summary>Devoluciones que restan (Aprobada/Completada) por fecha de devolución, de ventas comerciales vigentes.</summary>
    private Task<decimal> SqlTotalDevolucionesAsync(DateTime desde, DateTime hastaExclusivo) =>
        SqlEscalarAsync(
            $"SELECT CAST(COALESCE(SUM(d.TotalDevolucion),0) AS FLOAT) AS Value FROM Devoluciones d " +
            $"WHERE d.IsDeleted = 0 AND d.Estado IN ({(int)EstadoDevolucion.Aprobada},{(int)EstadoDevolucion.Completada}) " +
            $"AND d.FechaDevolucion >= {{0}} AND d.FechaDevolucion < {{1}} " +
            $"AND EXISTS (SELECT 1 FROM Ventas v WHERE v.Id = d.VentaId AND v.Estado IN (2,3,4) AND v.IsDeleted = 0)",
            desde, hastaExclusivo);

    // ------------------------------------------------------------------ lectura de archivos

    private static IXLWorksheet Hoja(byte[] bytes)
    {
        Assert.True(bytes.Length > 4 && bytes[0] == 'P' && bytes[1] == 'K', "El archivo no es un .xlsx (zip) válido");
        var wb = new XLWorkbook(new MemoryStream(bytes));
        return wb.Worksheet(1);
    }

    private static List<string> Encabezados(IXLWorksheet ws) =>
        ws.Row(1).CellsUsed().Select(c => c.GetString()).ToList();

    private static decimal Num(IXLCell c) => Math.Round((decimal)c.GetDouble(), 2);

    private static int ColumnaDe(IXLWorksheet ws, string encabezado) =>
        Encabezados(ws).IndexOf(encabezado) + 1;

    private void Guardar(string nombre, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(_outDir)) return;
        Directory.CreateDirectory(_outDir);
        File.WriteAllBytes(Path.Combine(_outDir, nombre), bytes);
    }

    private static ReporteVentasFiltroViewModel FiltroMes(int mes) =>
        new() { FechaDesde = new DateTime(2026, mes, 1), FechaHasta = new DateTime(2026, mes, 1).AddMonths(1).AddDays(-1) };

    // ------------------------------------------------------------------ Ventas Excel

    [Fact]
    public async Task VentasExcel_Noviembre_FilasYTotalesCoincidenConSql()
    {
        var bytes = await _service.ExportarVentasExcelAsync(FiltroMes(11), incluirDescuentos: true);
        Guardar("ventas-2026-11.xlsx", bytes);
        var ws = Hoja(bytes);
        var mes = Mes(11);

        var colNumero = ColumnaDe(ws, "Nº Venta");
        var colTotal = ColumnaDe(ws, "Total");
        var colIva = ColumnaDe(ws, "IVA");
        var colSubtotal = ColumnaDe(ws, "Subtotal Neto");
        Assert.True(colNumero > 0 && colTotal > 0 && colIva > 0 && colSubtotal > 0);

        var filas = ws.RowsUsed().Skip(1).Where(r => !string.IsNullOrEmpty(r.Cell(colNumero).GetString())).ToList();
        Assert.Equal(await SqlCantidadVentasAsync(mes[0], mes[1]), filas.Count);
        Assert.Equal(filas.Count, filas.Select(r => r.Cell(colNumero).GetString()).Distinct().Count()); // sin duplicados

        Assert.Equal(await SqlTotalVentasAsync(mes[0], mes[1]), filas.Sum(r => Num(r.Cell(colTotal))));
        Assert.Equal(await SqlTotalIvaAsync(mes[0], mes[1]), filas.Sum(r => Num(r.Cell(colIva))));
        Assert.Equal(await SqlTotalSubtotalAsync(mes[0], mes[1]), filas.Sum(r => Num(r.Cell(colSubtotal))));

        // Fila TOTALES y devoluciones/neto contra SQL.
        var filaTot = ws.RowsUsed().Single(r => r.Cell(5).GetString() == "TOTALES:");
        Assert.Equal(await SqlTotalVentasAsync(mes[0], mes[1]), Num(filaTot.Cell(colTotal)));
        var devoluciones = await SqlTotalDevolucionesAsync(mes[0], mes[1]);
        var filaDev = ws.RowsUsed().Single(r => r.Cell(5).GetString() == "Devoluciones del período:");
        Assert.Equal(-devoluciones, Num(filaDev.Cell(colTotal)));
        var filaNeto = ws.RowsUsed().Single(r => r.Cell(5).GetString().StartsWith("NETO"));
        Assert.Equal((await SqlTotalVentasAsync(mes[0], mes[1])) - devoluciones, Num(filaNeto.Cell(colTotal)));
    }

    [Fact]
    public async Task VentasExcel_Noviembre_ExcluyeCanceladasNoComercialesEliminadasYFueraDePeriodo_IncluyeLimitesYClienteBaja()
    {
        var ws = Hoja(await _service.ExportarVentasExcelAsync(FiltroMes(11), incluirDescuentos: true));
        var colNumero = ColumnaDe(ws, "Nº Venta");
        var numeros = ws.RowsUsed().Skip(1).Select(r => r.Cell(colNumero).GetString()).ToHashSet();

        Assert.Contains("V-FACT", numeros);
        Assert.Contains("V-BAJA", numeros);       // cliente dado de baja: el histórico se conserva
        Assert.Contains("V-LIM-INI", numeros);    // 01/11 00:00:00
        Assert.Contains("V-LIM-FIN", numeros);    // 30/11 23:59:59,9999999
        Assert.Contains("V-DEB", numeros);
        Assert.Contains("V-DESC", numeros);
        Assert.Contains("V-CRED", numeros);
        Assert.DoesNotContain("V-CANC", numeros);
        Assert.DoesNotContain("V-PRES", numeros);
        Assert.DoesNotContain("V-ELIM", numeros);
        Assert.DoesNotContain("V-LIM-DIC", numeros);
        Assert.DoesNotContain("V-LIM-OCT", numeros);

        // El cliente dado de baja conserva su nombre (no "Anónimo").
        var colCliente = ColumnaDe(ws, "Cliente");
        var filaBaja = ws.RowsUsed().Single(r => r.Cell(colNumero).GetString() == "V-BAJA");
        Assert.Equal("Gomez, Baja", filaBaja.Cell(colCliente).GetString());
    }

    [Fact]
    public async Task VentasExcel_Octubre_SoloLaVentaDelUltimoDiaDelMes_YDevolucionDeOctubreNoRestaAhi()
    {
        var bytes = await _service.ExportarVentasExcelAsync(FiltroMes(10), incluirDescuentos: true);
        Guardar("ventas-2026-10.xlsx", bytes);
        var ws = Hoja(bytes);
        var colNumero = ColumnaDe(ws, "Nº Venta");
        var numeros = ws.RowsUsed().Skip(1).Select(r => r.Cell(colNumero).GetString()).Where(n => n.StartsWith("V-")).ToList();
        Assert.Equal(new[] { "V-LIM-OCT" }, numeros);
        // La devolución de esa venta es de noviembre: no figura en octubre.
        var filaDev = ws.RowsUsed().Single(r => r.Cell(5).GetString() == "Devoluciones del período:");
        Assert.Equal(0m, Num(filaDev.Cell(ColumnaDe(ws, "Total"))));
    }

    [Fact]
    public async Task VentasExcel_PeriodoSinInformacion_TieneEncabezadosTotalesEnCeroYSigueAbriendo()
    {
        var filtro = new ReporteVentasFiltroViewModel { FechaDesde = new DateTime(2025, 1, 1), FechaHasta = new DateTime(2025, 1, 31) };
        var bytes = await _service.ExportarVentasExcelAsync(filtro, incluirDescuentos: true);
        Guardar("ventas-vacio.xlsx", bytes);
        var ws = Hoja(bytes);
        Assert.Contains("Nº Venta", Encabezados(ws));
        var filaTot = ws.RowsUsed().Single(r => r.Cell(5).GetString() == "TOTALES:");
        Assert.Equal(0m, Num(filaTot.Cell(ColumnaDe(ws, "Total"))));
        Assert.Empty(ws.RowsUsed().Skip(1).Where(r => r.Cell(1).GetString().StartsWith("V-")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VentasExcel_ColumnasAlineadasConSinDescuentos(bool incluirDescuentos)
    {
        var bytes = await _service.ExportarVentasExcelAsync(FiltroMes(11), incluirDescuentos);
        Guardar($"ventas-2026-11-desc{(incluirDescuentos ? "1" : "0")}.xlsx", bytes);
        var ws = Hoja(bytes);
        var enc = Encabezados(ws);
        Assert.Equal(incluirDescuentos, enc.Contains("Descuento ($)"));

        var filaDesc = ws.RowsUsed().Single(r => r.Cell(1).GetString() == "V-DESC");
        Assert.Equal(222m, Num(filaDesc.Cell(ColumnaDe(ws, "Total"))));
        Assert.Equal(38.53m, Num(filaDesc.Cell(ColumnaDe(ws, "IVA"))));
        if (incluirDescuentos)
            Assert.Equal(20m, Num(filaDesc.Cell(ColumnaDe(ws, "Descuento ($)"))));

        var filaDeb = ws.RowsUsed().Single(r => r.Cell(1).GetString() == "V-DEB");
        Assert.Equal(254.10m, Num(filaDeb.Cell(ColumnaDe(ws, "Total"))));
        Assert.Equal(12.10m, Num(filaDeb.Cell(ColumnaDe(ws, "Recargo débito"))));
    }

    [Fact]
    public async Task VentasExcel_FiltroCliente_YTipoPago_SoloDevuelvenLoSolicitado()
    {
        var filtro = FiltroMes(11);
        filtro.ClienteId = _clienteBaja.Id;
        var ws = Hoja(await _service.ExportarVentasExcelAsync(filtro, true));
        var ventas = ws.RowsUsed().Skip(1).Select(r => r.Cell(1).GetString()).Where(n => n.StartsWith("V-")).ToList();
        Assert.Equal(new[] { "V-BAJA" }, ventas);

        var filtroPago = FiltroMes(11);
        filtroPago.TipoPago = TipoPago.CreditoPersonal;
        var ws2 = Hoja(await _service.ExportarVentasExcelAsync(filtroPago, true));
        var ventas2 = ws2.RowsUsed().Skip(1).Select(r => r.Cell(1).GetString()).Where(n => n.StartsWith("V-")).ToList();
        Assert.Equal(new[] { "V-CRED" }, ventas2);
    }

    [Fact]
    public async Task VentasExcel_FechaYImportes_TienenFormatoEsperado()
    {
        var ws = Hoja(await _service.ExportarVentasExcelAsync(FiltroMes(11), true));
        var fila = ws.RowsUsed().Single(r => r.Cell(1).GetString() == "V-DEB");
        Assert.Equal(XLDataType.DateTime, fila.Cell(2).DataType);
        Assert.True(fila.Cell(2).Style.DateFormat.NumberFormatId is 14 or 22 || fila.Cell(2).Style.DateFormat.Format.ToLowerInvariant().Contains("yy"),
            $"formato de fecha inesperado: id={fila.Cell(2).Style.DateFormat.NumberFormatId} fmt={fila.Cell(2).Style.DateFormat.Format}");
        Assert.Equal("$#,##0.00", fila.Cell(ColumnaDe(ws, "Total")).Style.NumberFormat.Format);
        // Los importes se guardan como número (no como texto) con 2 decimales de precisión.
        Assert.Equal(XLDataType.Number, fila.Cell(ColumnaDe(ws, "Total")).DataType);
    }

    // ------------------------------------------------------------------ Ventas PDF

    [Fact]
    public async Task VentasPdf_SeGeneraYContieneTodasLasVentasDelPeriodo()
    {
        var bytes = await _service.GenerarVentasPdfAsync(FiltroMes(11));
        Guardar("ventas-2026-11.pdf", bytes);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));

        var reporte = await _service.GenerarReporteVentasAsync(FiltroMes(11));
        Assert.Equal(reporte.Ventas.Count, ReporteService.FilasDelPdfDeVentas(reporte).Count);
        Assert.True(reporte.Ventas.Count > 50, "el escenario debe superar el tope histórico de 50 filas");
    }

    [Fact]
    public async Task VentasPdf_PeriodoVacio_GeneraArchivoValido()
    {
        var filtro = new ReporteVentasFiltroViewModel { FechaDesde = new DateTime(2025, 1, 1), FechaHasta = new DateTime(2025, 1, 31) };
        var bytes = await _service.GenerarVentasPdfAsync(filtro);
        Guardar("ventas-vacio.pdf", bytes);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    // ------------------------------------------------------------------ Morosidad

    [Fact]
    public async Task MorosidadExcel_ContrastaConSql_YConservaDocumentoYTelefonoComoTexto()
    {
        var bytes = await _service.ExportarMorosidadExcelAsync();
        Guardar("morosidad.xlsx", bytes);
        var ws = Hoja(bytes);

        var filas = ws.RowsUsed().Skip(1).ToList();
        Assert.Equal(2, filas.Count);   // activo y dado de baja: la deuda no desaparece

        var colDeuda = ColumnaDe(ws, "Deuda Vencida");
        var sqlDeuda = await SqlEscalarAsync(
            "SELECT CAST(COALESCE(SUM(CAST(c.MontoTotal AS FLOAT) - CAST(c.MontoPagado AS FLOAT)),0) AS FLOAT) AS Value FROM Cuotas c " +
            "JOIN Creditos cr ON cr.Id = c.CreditoId WHERE c.IsDeleted = 0 AND cr.IsDeleted = 0 AND CAST(c.MontoPagado AS FLOAT) < CAST(c.MontoTotal AS FLOAT) AND c.FechaVencimiento < {0}",
            Hoy);
        Assert.Equal(sqlDeuda, filas.Sum(r => Num(r.Cell(colDeuda))));
        Assert.Equal(1_100m, sqlDeuda);   // 600 (1000 - 400 pagado) + 500

        var activa = filas.Single(r => r.Cell(1).GetString().StartsWith("Perez"));
        Assert.Equal("00123456", activa.Cell(ColumnaDe(ws, "Documento")).GetString());   // ceros a la izquierda
        Assert.Equal(XLDataType.Text, activa.Cell(ColumnaDe(ws, "Documento")).DataType);
        Assert.Equal("011-4444-0001", activa.Cell(ColumnaDe(ws, "Teléfono")).GetString());

        // Todas las columnas de importe llevan formato monetario.
        foreach (var enc in new[] { "Deuda Vencida", "Deuda Vigente", "Deuda en Mora", "Deuda en Gracia" })
            Assert.Equal("$#,##0.00", activa.Cell(ColumnaDe(ws, enc)).Style.NumberFormat.Format);
    }

    [Fact]
    public async Task MorosidadPdf_SeGeneraConClientesDeLaExcel()
    {
        var bytes = await _service.GenerarMorosidadPdfAsync();
        Guardar("morosidad.pdf", bytes);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    // ------------------------------------------------------------------ Márgenes y movimientos valorizados

    [Fact]
    public async Task MargenesExcel_FilaPorProducto_ConDocumentoCodigoComoTexto()
    {
        var bytes = await _service.ExportarMargenesExcelAsync();
        Guardar("margenes.xlsx", bytes);
        var ws = Hoja(bytes);
        var filas = ws.RowsUsed().Skip(1).ToList();
        var sqlProductos = (int)await SqlEscalarAsync("SELECT CAST(COUNT(*) AS FLOAT) AS Value FROM Productos WHERE IsDeleted = 0");
        Assert.Equal(sqlProductos, filas.Count);
        Assert.Equal("00042", filas[0].Cell(1).GetString());   // código con ceros a la izquierda
        Assert.Equal(XLDataType.Text, filas[0].Cell(1).DataType);
        // Precio neto = 121 / 1,21 = 100; ganancia = 100 - 60 = 40; margen = 40 %.
        Assert.Equal(40m, Num(filas[0].Cell(ColumnaDe(ws, "Ganancia"))));
        Assert.Equal(40m, Num(filas[0].Cell(ColumnaDe(ws, "Margen %"))));
    }

    [Fact]
    public async Task MovimientosValorizadosExcel_TotalesCoincidenConSql()
    {
        var filtro = new ReporteMovimientosValorizadosFiltroViewModel
        {
            FechaDesde = new DateTime(2026, 11, 1), FechaHasta = new DateTime(2026, 11, 30)
        };
        var bytes = await _service.ExportarMovimientosValorizadosExcelAsync(filtro);
        Guardar("movimientos-2026-11.xlsx", bytes);
        var ws = Hoja(bytes);

        var filas = ws.RowsUsed().Skip(1).Where(r => r.Cell(2).GetString() != string.Empty).ToList();
        Assert.Equal(3, filas.Count);
        var colCosto = ColumnaDe(ws, "Costo total");
        Assert.Equal(await SqlEscalarAsync(
            "SELECT CAST(COALESCE(SUM(CostoTotalAlMomento),0) AS FLOAT) AS Value FROM MovimientosStock WHERE IsDeleted = 0 AND CreatedAt >= {0} AND CreatedAt < {1}",
            new DateTime(2026, 11, 1), new DateTime(2026, 12, 1)), filas.Sum(r => Num(r.Cell(colCosto))));
        var filaTot = ws.RowsUsed().Single(r => r.Cell(4).GetString() == "TOTALES:");
        Assert.Equal(filas.Sum(r => Num(r.Cell(colCosto))), Num(filaTot.Cell(colCosto)));
    }

    // ------------------------------------------------------------------ Devoluciones CSV

    [Fact]
    public async Task DevolucionesCsv_UnaFilaPorDevolucion_ImportesCoincidenConSql_YClienteBajaConservado()
    {
        var servicio = new DevolucionService(_context, null!, null!, NullLogger<DevolucionService>.Instance, null);
        var todas = await servicio.ObtenerTodasDevolucionesAsync();
        var bytes = servicio.GenerarCsvDevoluciones(todas);
        Guardar("devoluciones.csv", bytes);

        var texto = new System.Text.UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        var lineas = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.TrimEnd('\r')).ToList();
        Assert.Equal((int)await SqlEscalarAsync("SELECT CAST(COUNT(*) AS FLOAT) AS Value FROM Devoluciones WHERE IsDeleted = 0"), lineas.Count);

        var montos = lineas.Sum(l => decimal.Parse(l.Split(';')[^1].Trim('"'), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(await SqlEscalarAsync("SELECT CAST(COALESCE(SUM(TotalDevolucion),0) AS FLOAT) AS Value FROM Devoluciones WHERE IsDeleted = 0"), montos);
        Assert.Contains(lineas, l => l.Contains("\"Gomez, Baja\";\"20111222\""));
    }

    // ------------------------------------------------------------------ Cotización PDF

    [Fact]
    public void CotizacionPdf_ConYSinPermisoDeDescuentos_SeGeneraValido()
    {
        var cotizacion = new TheBuryProject.Services.Models.CotizacionResultado
        {
            Numero = "COT-QA-0001", Fecha = new DateTime(2026, 11, 10), NombreClienteLibre = "Cliente Mostrador",
            Subtotal = 2500m, DescuentoTotal = 250m, ImporteArmados = 100m, TieneEnvio = true, ImporteEnvio = 300m,
            EnvioIncluidoEnTotal = true, TotalBase = 2650m, TotalACobrar = 2650m,
            Detalles = new[]
            {
                new TheBuryProject.Services.Models.CotizacionDetalleResultado
                {
                    CodigoProductoSnapshot = "A-1", NombreProductoSnapshot = "Producto A", Cantidad = 2m,
                    PrecioUnitarioSnapshot = 1000m, DescuentoPorcentajeSnapshot = 10m, Subtotal = 1800m
                },
                new TheBuryProject.Services.Models.CotizacionDetalleResultado
                {
                    CodigoProductoSnapshot = "B-1", NombreProductoSnapshot = "Producto B", Cantidad = 1m,
                    PrecioUnitarioSnapshot = 500m, Subtotal = 500m
                }
            }
        };
        var servicio = new CotizacionPdfService();
        var con = servicio.Generar(cotizacion, incluirDescuentos: true);
        var sin = servicio.Generar(cotizacion, incluirDescuentos: false);
        Guardar("cotizacion-con-descuentos.pdf", con);
        Guardar("cotizacion-sin-descuentos.pdf", sin);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(con, 0, 5));
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(sin, 0, 5));
    }

    private MovimientoStock Mov(TipoMovimiento tipo, decimal cantidad, decimal costoTotal, DateTime fecha) => new()
    {
        ProductoId = _producto.Id, Tipo = tipo, Cantidad = cantidad, StockAnterior = 0m, StockNuevo = cantidad,
        CostoUnitarioAlMomento = costoTotal / Math.Abs(cantidad), CostoTotalAlMomento = costoTotal, FuenteCosto = "Compra",
        Referencia = "REF", Motivo = "qa", CreatedAt = fecha
    };
}




