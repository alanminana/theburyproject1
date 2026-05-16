using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public sealed class CotizacionControllerPdfTests
{
    // ── Infraestructura de instanciación ──────────────────────────────────────

    private static CotizacionController CreateController(
        ICotizacionService? cotizacionService = null,
        ICotizacionPdfService? pdfService = null) =>
        new(
            cotizacionService ?? new StubCotizacionService(),
            pdfService ?? new StubPdfService(),
            new StubProductoService(),
            new StubClienteService(),
            NullLogger<CotizacionController>.Instance);

    // ── Seguridad y permisos ──────────────────────────────────────────────────

    [Fact]
    public void DescargarPdf_RequierePermisosCotizacionesView()
    {
        Assert.Contains(
            typeof(CotizacionController).GetCustomAttributes<AuthorizeAttribute>(),
            a => a.GetType() == typeof(AuthorizeAttribute));

        var permiso = typeof(CotizacionController).GetCustomAttribute<PermisoRequeridoAttribute>();
        Assert.NotNull(permiso);
        Assert.Equal("cotizaciones", permiso.Modulo);
        Assert.Equal("view", permiso.Accion);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DescargarPdf_CotizacionExistente_RetornaFilePdf()
    {
        var controller = CreateController();

        var result = await controller.DescargarPdf(42);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.NotEmpty(file.FileContents);
    }

    [Fact]
    public async Task DescargarPdf_CotizacionExistente_NombreArchivoContieneNumero()
    {
        var controller = CreateController();

        var result = await controller.DescargarPdf(42);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Contains("COT-TEST", file.FileDownloadName);
        Assert.EndsWith(".pdf", file.FileDownloadName);
    }

    // ── Not found ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DescargarPdf_CotizacionInexistente_RetornaNotFound()
    {
        var controller = CreateController(cotizacionService: new StubCotizacionServiceVacio());

        var result = await controller.DescargarPdf(999);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Read-only: no modifica estado ─────────────────────────────────────────

    [Fact]
    public async Task DescargarPdf_NoInvocaConversionNiCancelacion()
    {
        var cotizacionSpy = new SpyCotizacionService();
        var controller = CreateController(cotizacionService: cotizacionSpy);

        await controller.DescargarPdf(1);

        Assert.False(cotizacionSpy.CancelarInvocado, "DescargarPdf no debe cancelar la cotizacion.");
        Assert.False(cotizacionSpy.VencerInvocado, "DescargarPdf no debe vencer cotizaciones.");
    }

    [Fact]
    public async Task DescargarPdf_NoModificaEstado_SoloLlamaObtener()
    {
        var cotizacionSpy = new SpyCotizacionService();
        var controller = CreateController(cotizacionService: cotizacionSpy);

        await controller.DescargarPdf(7);

        Assert.True(cotizacionSpy.ObtenerInvocado);
        Assert.Equal(7, cotizacionSpy.ObtenerIdRecibido);
        Assert.False(cotizacionSpy.CrearInvocado);
        Assert.False(cotizacionSpy.CancelarInvocado);
    }

    // ── Constructor no depende de Venta/Caja/Stock ────────────────────────────

    [Fact]
    public void Controller_ConPdfService_NoDependeDeVentaCajaStock()
    {
        var constructor = Assert.Single(typeof(CotizacionController).GetConstructors());
        var paramTypes = constructor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Contains(typeof(ICotizacionPdfService), paramTypes);
        Assert.DoesNotContain(paramTypes, t => t.Name.Contains("Venta", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paramTypes, t => t.Name.Contains("Caja", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paramTypes, t => t.Name.Contains("Stock", StringComparison.OrdinalIgnoreCase));
    }

    // ── Vista Detalles expone enlace DescargarPdf ─────────────────────────────

    [Fact]
    public void DetallesView_ContieneEnlaceDescargarPdf()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Detalles_tw.cshtml"));

        Assert.Contains("DescargarPdf", view);
        Assert.Contains("Descargar PDF", view);
    }

    // ── Vista Imprimir ya no indica "Guardar como PDF" como única opción ───────

    [Fact]
    public void ImprimirView_DisclaimerMencionaDescargarPdf()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Imprimir_tw.cshtml"));

        Assert.Contains("Descargar PDF", view);
    }

    // ── Stubs y spies ─────────────────────────────────────────────────────────

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "TheBuryProyect.csproj")))
            dir = Directory.GetParent(dir)?.FullName;

        return dir ?? throw new DirectoryNotFoundException("No se encontro la raiz del repo.");
    }

    private sealed class StubPdfService : ICotizacionPdfService
    {
        public byte[] Generar(CotizacionResultado cotizacion) => [0x25, 0x50, 0x44, 0x46]; // "%PDF"
    }

    private sealed class StubCotizacionService : ICotizacionService
    {
        public Task<CotizacionResultado> CrearAsync(CotizacionCrearRequest r, string u, CancellationToken ct = default) =>
            Task.FromResult(new CotizacionResultado { Id = 1, Numero = "COT-TEST" });

        public Task<CotizacionResultado?> ObtenerAsync(int id, CancellationToken ct = default) =>
            Task.FromResult<CotizacionResultado?>(new CotizacionResultado { Id = id, Numero = "COT-TEST" });

        public Task<CotizacionListadoResultado> ListarAsync(CotizacionFiltros f, CancellationToken ct = default) =>
            Task.FromResult(new CotizacionListadoResultado());

        public Task<CotizacionCancelacionResultado> CancelarAsync(int id, CotizacionCancelacionRequest r, string u, CancellationToken ct = default) =>
            Task.FromResult(new CotizacionCancelacionResultado { Exitoso = true, CotizacionId = id });

        public Task<CotizacionVencimientoResultado> VencerEmitidasAsync(DateTime f, string u, CancellationToken ct = default) =>
            Task.FromResult(new CotizacionVencimientoResultado { Exitoso = true });
    }

    private sealed class StubCotizacionServiceVacio : ICotizacionService
    {
        public Task<CotizacionResultado> CrearAsync(CotizacionCrearRequest r, string u, CancellationToken ct = default) =>
            Task.FromResult(new CotizacionResultado());

        public Task<CotizacionResultado?> ObtenerAsync(int id, CancellationToken ct = default) =>
            Task.FromResult<CotizacionResultado?>(null);

        public Task<CotizacionListadoResultado> ListarAsync(CotizacionFiltros f, CancellationToken ct = default) =>
            Task.FromResult(new CotizacionListadoResultado());

        public Task<CotizacionCancelacionResultado> CancelarAsync(int id, CotizacionCancelacionRequest r, string u, CancellationToken ct = default) =>
            Task.FromResult(new CotizacionCancelacionResultado { CotizacionId = id });

        public Task<CotizacionVencimientoResultado> VencerEmitidasAsync(DateTime f, string u, CancellationToken ct = default) =>
            Task.FromResult(new CotizacionVencimientoResultado());
    }

    private sealed class SpyCotizacionService : ICotizacionService
    {
        public bool ObtenerInvocado { get; private set; }
        public int ObtenerIdRecibido { get; private set; }
        public bool CrearInvocado { get; private set; }
        public bool CancelarInvocado { get; private set; }
        public bool VencerInvocado { get; private set; }

        public Task<CotizacionResultado?> ObtenerAsync(int id, CancellationToken ct = default)
        {
            ObtenerInvocado = true;
            ObtenerIdRecibido = id;
            return Task.FromResult<CotizacionResultado?>(new CotizacionResultado { Id = id, Numero = $"COT-{id}" });
        }

        public Task<CotizacionResultado> CrearAsync(CotizacionCrearRequest r, string u, CancellationToken ct = default)
        {
            CrearInvocado = true;
            return Task.FromResult(new CotizacionResultado());
        }

        public Task<CotizacionListadoResultado> ListarAsync(CotizacionFiltros f, CancellationToken ct = default) =>
            Task.FromResult(new CotizacionListadoResultado());

        public Task<CotizacionCancelacionResultado> CancelarAsync(int id, CotizacionCancelacionRequest r, string u, CancellationToken ct = default)
        {
            CancelarInvocado = true;
            return Task.FromResult(new CotizacionCancelacionResultado { CotizacionId = id });
        }

        public Task<CotizacionVencimientoResultado> VencerEmitidasAsync(DateTime f, string u, CancellationToken ct = default)
        {
            VencerInvocado = true;
            return Task.FromResult(new CotizacionVencimientoResultado());
        }
    }

    private sealed class StubProductoService : IProductoService
    {
        public Task<IEnumerable<Producto>> GetAllAsync() => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<Producto?> GetByIdAsync(int id) => Task.FromResult<Producto?>(null);
        public Task<Producto?> GetByIdParaHistorialAsync(int id) => Task.FromResult<Producto?>(null);
        public Task<IEnumerable<Producto>> GetByCategoriaAsync(int categoriaId) => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<IEnumerable<Producto>> GetByMarcaAsync(int marcaId) => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<IEnumerable<Producto>> GetProductosConStockBajoAsync() => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<Producto> CreateAsync(Producto producto) => Task.FromResult(producto);
        public Task<Producto> UpdateAsync(Producto producto) => Task.FromResult(producto);
        public Task<bool> DeleteAsync(int id) => Task.FromResult(false);
        public Task PrepararPrecioVentaConIvaAsync(Producto producto) => Task.CompletedTask;
        public decimal ObtenerPrecioVentaSinIva(decimal precioVentaConIva, decimal porcentajeIVA) => precioVentaConIva;
        public Task<IEnumerable<Producto>> SearchAsync(string? searchTerm = null, int? categoriaId = null, int? marcaId = null, bool stockBajo = false, bool soloActivos = false, string? orderBy = null, string? orderDirection = "asc") => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<List<int>> SearchIdsAsync(string? searchTerm = null, int? categoriaId = null, int? marcaId = null, bool stockBajo = false, bool soloActivos = false) => Task.FromResult(new List<int>());
        public Task<IEnumerable<ProductoVentaDto>> BuscarParaVentaAsync(string term, int take = 20, int? categoriaId = null, int? marcaId = null, bool soloConStock = true, decimal? precioMin = null, decimal? precioMax = null) => Task.FromResult<IEnumerable<ProductoVentaDto>>(Array.Empty<ProductoVentaDto>());
        public Task<ProductoPrecioVentaResultado?> ObtenerPrecioVigenteParaVentaAsync(int productoId) => Task.FromResult<ProductoPrecioVentaResultado?>(null);
        public Task<Producto> ActualizarStockAsync(int id, decimal cantidad) => Task.FromResult(new Producto { Id = id });
        public Task<Producto> ActualizarComisionAsync(int id, decimal porcentaje) => Task.FromResult(new Producto { Id = id });
        public Task<bool> ToggleDestacadoAsync(int id) => Task.FromResult(false);
        public Task CambiarTrazabilidadIndividualAsync(int productoId, bool requiereTrazabilidad) => Task.CompletedTask;
        public Task<bool> ExistsCodigoAsync(string codigo, int? excludeId = null) => Task.FromResult(false);
    }

    private sealed class StubClienteService : IClienteService
    {
        public Task<IEnumerable<Cliente>> GetAllAsync() => Task.FromResult<IEnumerable<Cliente>>(Array.Empty<Cliente>());
        public Task<Cliente?> GetByIdAsync(int id) => Task.FromResult<Cliente?>(null);
        public Task<Cliente> CreateAsync(Cliente cliente) => Task.FromResult(cliente);
        public Task<Cliente> UpdateAsync(Cliente cliente) => Task.FromResult(cliente);
        public Task<bool> DeleteAsync(int id) => Task.FromResult(false);
        public Task<IEnumerable<Cliente>> SearchAsync(string? searchTerm = null, string? tipoDocumento = null, bool? soloActivos = null, bool? conCreditosActivos = null, decimal? puntajeMinimo = null, string? orderBy = null, string? orderDirection = null) => Task.FromResult<IEnumerable<Cliente>>(Array.Empty<Cliente>());
        public Task<bool> ExisteDocumentoAsync(string tipoDocumento, string numeroDocumento, int? excludeId = null) => Task.FromResult(false);
        public Task<Cliente?> GetByDocumentoAsync(string tipoDocumento, string numeroDocumento) => Task.FromResult<Cliente?>(null);
        public Task ActualizarPuntajeRiesgoAsync(int clienteId, decimal nuevoPuntaje, string motivo) => Task.CompletedTask;
    }
}
