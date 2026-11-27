using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Ayuda «¿Cómo funciona?» de Listas de precios: el simulador y los ejemplos que muestra la ayuda deben coincidir con lo que
/// hace el ERP (cálculo sugerido, redondeo y margen mínimo al fijar un precio).
/// </summary>
public class ListaPrecioAyudaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly PrecioService _service;

    public ListaPrecioAyudaTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _service = new PrecioService(_context, NullLogger<PrecioService>.Instance, new Usuario(), new ConfigurationBuilder().Build());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void ElEjemploDeLaHeladera_SeArmaPasoAPaso()
    {
        var r = ListaPrecioSimulador.Simular(_service, 100000m, 30m, 10m, 20m, null, 110000m);

        Assert.Equal(30000m, r.MargenImporte);
        Assert.Equal(130000m, r.Subtotal);
        Assert.Equal(13000m, r.RecargoImporte);
        Assert.Equal(143000m, r.Sugerido);
        Assert.Equal(0m, r.AjusteRedondeo);
        Assert.Equal(43m, r.MargenSugerido);
        Assert.Equal(120000m, r.PrecioMinimo);
        Assert.Equal(10m, r.MargenPrueba);
        Assert.True(r.PruebaRechazada);
        Assert.False(r.SugeridoBajoMinimo);
    }

    // Los ejemplos del texto de ayuda sobre el redondeo: al más cercano y, en la mitad exacta, al múltiplo par.
    [Theory]
    [InlineData(143050, null, 143000)]
    [InlineData(143150, null, 143200)]
    [InlineData(119362.1, null, 119400)]
    [InlineData(143050, "centena", 143000)]
    [InlineData(143055, "decena", 143060)]
    [InlineData(143000.5, "unidad", 143000)]
    [InlineData(143000.55, "ninguno", 143000.55)]
    public void ElRedondeoEsElDelErp(double costo, string? regla, double esperado)
    {
        var r = ListaPrecioSimulador.Simular(_service, (decimal)costo, null, null, null, regla, null);

        Assert.Equal((decimal)esperado, r.Sugerido);
        Assert.Equal(r.Sugerido - (decimal)costo, r.AjusteRedondeo);
    }

    [Theory]
    [InlineData(0, 10, 30, 20)]
    [InlineData(100000, null, 10, null)]
    [InlineData(83470, 30, 10, "x")]
    public void ElSugeridoCoincideSiempreConElDelServicio(double costo, object? margen, int recargo, object? _)
    {
        decimal? m = margen is null ? null : Convert.ToDecimal(margen);
        var esperado = _service.CalcularPrecioSugerido(
            new ListaPrecio { MargenPorcentaje = m, RecargoPorcentaje = recargo }, (decimal)costo);

        Assert.Equal(esperado, ListaPrecioSimulador.Simular(_service, (decimal)costo, m, recargo, null, null, null).Sugerido);
    }

    [Theory]
    [InlineData(100000, 20, 110000)]
    [InlineData(100000, 20, 119999.99)]
    [InlineData(100000, 20, 120000)]
    [InlineData(33.33, 20, 39.99)]
    [InlineData(33.33, 20, 40)]
    [InlineData(0, 20, 1)]
    public async Task ElRechazoDelSimulador_CoincideConElDeFijarPrecio(double costo, int minimo, double precio)
    {
        var categoria = new Categoria { Codigo = "C", Nombre = "C", Activo = true };
        var marca = new Marca { Codigo = "M", Nombre = "M", Activo = true };
        var producto = new Producto
        {
            Codigo = "P", Nombre = "Heladera", Categoria = categoria, Marca = marca,
            PrecioCompra = (decimal)costo, PrecioVenta = 150000m, Activo = true
        };
        var lista = new ListaPrecio { Nombre = "Contado", Codigo = "CONTADO", Tipo = TipoListaPrecio.Contado, MargenMinimoPorcentaje = minimo, Activa = true };
        _context.AddRange(producto, lista);
        await _context.SaveChangesAsync();

        var simulado = ListaPrecioSimulador.Simular(_service, (decimal)costo, null, null, minimo, null, (decimal)precio);
        var error = await Record.ExceptionAsync(() => _service.SetPrecioManualAsync(producto.Id, lista.Id, (decimal)precio, (decimal)costo));

        Assert.Equal(simulado.PruebaRechazada, error is InvalidOperationException);
        if (simulado.PrecioMinimo.HasValue)
            Assert.Equal((decimal)precio < simulado.PrecioMinimo.Value, simulado.PruebaRechazada);
    }

    [Fact]
    public void CadaCampoPedidoTieneSuExplicacionCompleta()
    {
        var esperados = new[]
        {
            "Nombre", "Código", "Tipo de lista", "Margen sobre costo (%)", "Margen mínimo (%)", "Recargo (%)", "Cantidad de cuotas",
            "Redondeo", "Orden", "Descripción", "Notas", "Activa", "Lista predeterminada"
        };

        Assert.Equal(esperados, ListaPrecioAyudaCampos.Todos.Select(c => c.Nombre));
        Assert.All(ListaPrecioAyudaCampos.Todos, c => Assert.All(
            new[] { c.Clave, c.Breve, c.Significa, c.SirvePara, c.SeAplicaA, c.EfectoEnErp },
            texto => Assert.False(string.IsNullOrWhiteSpace(texto))));
        Assert.Equal(new[] { "tipo", "cuotas", "descripcion", "notas" },
            ListaPrecioAyudaCampos.Todos.Where(c => c.Efecto == EfectoCampoListaPrecio.Informativo).Select(c => c.Clave));
    }

    private sealed class Usuario : ICurrentUserService
    {
        public string GetUsername() => "ayuda-test";
        public string GetUserId() => "ayuda-test";
        public bool IsAuthenticated() => true;
        public string? GetEmail() => null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string modulo, string accion) => true;
        public string? GetIpAddress() => null;
    }
}
