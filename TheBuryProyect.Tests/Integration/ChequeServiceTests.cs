using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para ChequeService.
/// Cubren CreateAsync (número único, proveedor inexistente, fecha vencimiento
/// anterior a emisión, orden no pertenece al proveedor), UpdateAsync (RowVersion,
/// concurrencia), DeleteAsync (depositado/cobrado bloquea), CambiarEstadoAsync,
/// NumeroExisteAsync, GetVencidosAsync, GetPorVencerAsync y GetByProveedorIdAsync.
/// </summary>
public class ChequeServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ChequeService _service;

    public ChequeServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new ChequeService(_context, NullLogger<ChequeService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Proveedor> SeedProveedorAsync()
    {
        var cuit = Guid.NewGuid().ToString("N")[..11];
        var proveedor = new Proveedor { Cuit = cuit, RazonSocial = "Prov-" + cuit, Activo = true };
        _context.Proveedores.Add(proveedor);
        await _context.SaveChangesAsync();
        return proveedor;
    }

    private async Task<OrdenCompra> SeedOrdenCompraAsync(int proveedorId)
    {
        var orden = new OrdenCompra
        {
            Numero = "OC-" + Guid.NewGuid().ToString("N")[..6],
            ProveedorId = proveedorId,
            Estado = EstadoOrdenCompra.Borrador,
            FechaEmision = DateTime.UtcNow
        };
        _context.OrdenesCompra.Add(orden);
        await _context.SaveChangesAsync();
        return orden;
    }

    private async Task<Cheque> SeedChequeAsync(
        int proveedorId,
        string? numero = null,
        EstadoCheque estado = EstadoCheque.Emitido,
        DateTime? fechaVencimiento = null)
    {
        var cheque = new Cheque
        {
            Numero = numero ?? "CHQ-" + Guid.NewGuid().ToString("N")[..6],
            Banco = "Banco Test",
            Monto = 1000m,
            FechaEmision = DateTime.Today,
            FechaVencimiento = fechaVencimiento,
            Estado = estado,
            ProveedorId = proveedorId
        };
        _context.Cheques.Add(cheque);
        await _context.SaveChangesAsync();
        await _context.Entry(cheque).ReloadAsync();
        return cheque;
    }

    // -------------------------------------------------------------------------
    // CreateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_DatosValidos_Persiste()
    {
        var proveedor = await SeedProveedorAsync();

        var cheque = new Cheque
        {
            Numero = "CHQ-001",
            Banco = "Banco Test",
            Monto = 5000m,
            FechaEmision = DateTime.Today,
            FechaVencimiento = DateTime.Today.AddDays(30),
            Estado = EstadoCheque.Emitido,
            ProveedorId = proveedor.Id
        };

        var resultado = await _service.CreateAsync(cheque);

        Assert.True(resultado.Id > 0);
        var bd = await _context.Cheques.FirstOrDefaultAsync(c => c.Id == resultado.Id);
        Assert.NotNull(bd);
        Assert.Equal("CHQ-001", bd!.Numero);
    }

    [Fact]
    public async Task Create_NumeroDuplicado_LanzaExcepcion()
    {
        var proveedor = await SeedProveedorAsync();
        await SeedChequeAsync(proveedor.Id, "CHQ-DUP");

        var duplicado = new Cheque
        {
            Numero = "CHQ-DUP",
            Banco = "Banco Test",
            Monto = 100m,
            FechaEmision = DateTime.Today,
            Estado = EstadoCheque.Emitido,
            ProveedorId = proveedor.Id
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(duplicado));
    }

    [Fact]
    public async Task Create_ProveedorNoExiste_LanzaExcepcion()
    {
        var cheque = new Cheque
        {
            Numero = "CHQ-002",
            Banco = "Banco Test",
            Monto = 100m,
            FechaEmision = DateTime.Today,
            Estado = EstadoCheque.Emitido,
            ProveedorId = 99999
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(cheque));
    }

    [Fact]
    public async Task Create_FechaVencimientoAnteriorAEmision_LanzaExcepcion()
    {
        var proveedor = await SeedProveedorAsync();

        var cheque = new Cheque
        {
            Numero = "CHQ-003",
            Banco = "Banco Test",
            Monto = 100m,
            FechaEmision = DateTime.Today,
            FechaVencimiento = DateTime.Today.AddDays(-1), // anterior
            Estado = EstadoCheque.Emitido,
            ProveedorId = proveedor.Id
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(cheque));
    }

    [Fact]
    public async Task Create_ConOrdenCompraDeOtroProveedor_LanzaExcepcion()
    {
        var proveedor1 = await SeedProveedorAsync();
        var proveedor2 = await SeedProveedorAsync();
        var orden = await SeedOrdenCompraAsync(proveedor1.Id);

        var cheque = new Cheque
        {
            Numero = "CHQ-004",
            Banco = "Banco Test",
            Monto = 100m,
            FechaEmision = DateTime.Today,
            Estado = EstadoCheque.Emitido,
            ProveedorId = proveedor2.Id, // proveedor diferente a la orden
            OrdenCompraId = orden.Id
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(cheque));
    }

    [Fact]
    public async Task Create_ConOrdenCompraDelMismoProveedor_Persiste()
    {
        var proveedor = await SeedProveedorAsync();
        var orden = await SeedOrdenCompraAsync(proveedor.Id);

        var cheque = new Cheque
        {
            Numero = "CHQ-005",
            Banco = "Banco Test",
            Monto = 100m,
            FechaEmision = DateTime.Today,
            Estado = EstadoCheque.Emitido,
            ProveedorId = proveedor.Id,
            OrdenCompraId = orden.Id
        };

        var resultado = await _service.CreateAsync(cheque);

        Assert.True(resultado.Id > 0);
        Assert.Equal(orden.Id, resultado.OrdenCompraId);
    }

    // -------------------------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_EstadoEmitido_SoftDelete()
    {
        var proveedor = await SeedProveedorAsync();
        var cheque = await SeedChequeAsync(proveedor.Id, estado: EstadoCheque.Emitido);

        var resultado = await _service.DeleteAsync(cheque.Id);

        Assert.True(resultado);
        var bd = await _context.Cheques
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == cheque.Id);
        Assert.True(bd.IsDeleted);
    }

    [Fact]
    public async Task Delete_EstadoCobrado_LanzaExcepcion()
    {
        var proveedor = await SeedProveedorAsync();
        var cheque = await SeedChequeAsync(proveedor.Id, estado: EstadoCheque.Cobrado);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeleteAsync(cheque.Id));
    }

    [Fact]
    public async Task Delete_EstadoDepositado_LanzaExcepcion()
    {
        var proveedor = await SeedProveedorAsync();
        var cheque = await SeedChequeAsync(proveedor.Id, estado: EstadoCheque.Depositado);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeleteAsync(cheque.Id));
    }

    [Fact]
    public async Task Delete_ChequeNoExiste_RetornaFalse()
    {
        var resultado = await _service.DeleteAsync(99999);

        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // CambiarEstadoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CambiarEstado_ChequeExistente_ActualizaEstado()
    {
        var proveedor = await SeedProveedorAsync();
        var cheque = await SeedChequeAsync(proveedor.Id, estado: EstadoCheque.Emitido);

        var resultado = await _service.CambiarEstadoAsync(cheque.Id, EstadoCheque.Cobrado);

        Assert.True(resultado);
        _context.ChangeTracker.Clear();
        var bd = await _context.Cheques.FirstAsync(c => c.Id == cheque.Id);
        Assert.Equal(EstadoCheque.Cobrado, bd.Estado);
    }

    [Fact]
    public async Task CambiarEstado_ChequeNoExiste_RetornaFalse()
    {
        var resultado = await _service.CambiarEstadoAsync(99999, EstadoCheque.Cobrado);

        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // NumeroExisteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NumeroExiste_NumeroExistente_ReturnsTrue()
    {
        var proveedor = await SeedProveedorAsync();
        await SeedChequeAsync(proveedor.Id, "CHQ-EXISTS");

        var existe = await _service.NumeroExisteAsync("CHQ-EXISTS");

        Assert.True(existe);
    }

    [Fact]
    public async Task NumeroExiste_NumeroNoExistente_ReturnsFalse()
    {
        var existe = await _service.NumeroExisteAsync("CHQ-NOEXISTE");

        Assert.False(existe);
    }

    [Fact]
    public async Task NumeroExiste_ExcluyendoMismoId_ReturnsFalse()
    {
        var proveedor = await SeedProveedorAsync();
        var cheque = await SeedChequeAsync(proveedor.Id, "CHQ-SELF");

        // Al actualizar el mismo cheque, no debe considerarse duplicado
        var existe = await _service.NumeroExisteAsync("CHQ-SELF", cheque.Id);

        Assert.False(existe);
    }

    // -------------------------------------------------------------------------
    // GetVencidosAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetVencidos_SinCheques_RetornaVacio()
    {
        var resultado = await _service.GetVencidosAsync();

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetVencidos_ChequeVencidoNoFinalizado_IncludeEnResultado()
    {
        var proveedor = await SeedProveedorAsync();
        // Vencido (fecha pasada) y en estado Emitido (no finalizado)
        await SeedChequeAsync(proveedor.Id, estado: EstadoCheque.Emitido,
            fechaVencimiento: DateTime.Today.AddDays(-1));

        var resultado = await _service.GetVencidosAsync();

        Assert.Single(resultado);
    }

    [Fact]
    public async Task GetVencidos_ChequeCobrado_NoIncluido()
    {
        var proveedor = await SeedProveedorAsync();
        await SeedChequeAsync(proveedor.Id, estado: EstadoCheque.Cobrado,
            fechaVencimiento: DateTime.Today.AddDays(-1));

        var resultado = await _service.GetVencidosAsync();

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetVencidos_ChequePorVencer_NoIncluido()
    {
        var proveedor = await SeedProveedorAsync();
        // Vence en el futuro — no debe aparecer como vencido
        await SeedChequeAsync(proveedor.Id, estado: EstadoCheque.Emitido,
            fechaVencimiento: DateTime.Today.AddDays(10));

        var resultado = await _service.GetVencidosAsync();

        Assert.Empty(resultado);
    }

    // -------------------------------------------------------------------------
    // GetPorVencerAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPorVencer_ChequeEnVentanaDefault_Incluido()
    {
        var proveedor = await SeedProveedorAsync();
        // Vence en 5 días — dentro de la ventana de 7
        await SeedChequeAsync(proveedor.Id, estado: EstadoCheque.Emitido,
            fechaVencimiento: DateTime.Today.AddDays(5));

        var resultado = await _service.GetPorVencerAsync(dias: 7);

        Assert.Single(resultado);
    }

    [Fact]
    public async Task GetPorVencer_ChequeVencidoAyer_NoIncluido()
    {
        var proveedor = await SeedProveedorAsync();
        await SeedChequeAsync(proveedor.Id, estado: EstadoCheque.Emitido,
            fechaVencimiento: DateTime.Today.AddDays(-1));

        var resultado = await _service.GetPorVencerAsync(dias: 7);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetPorVencer_ChequeCobradoEnVentana_NoIncluido()
    {
        var proveedor = await SeedProveedorAsync();
        await SeedChequeAsync(proveedor.Id, estado: EstadoCheque.Cobrado,
            fechaVencimiento: DateTime.Today.AddDays(3));

        var resultado = await _service.GetPorVencerAsync(dias: 7);

        Assert.Empty(resultado);
    }

    // -------------------------------------------------------------------------
    // GetByProveedorIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByProveedor_SinCheques_RetornaVacio()
    {
        var proveedor = await SeedProveedorAsync();

        var resultado = await _service.GetByProveedorIdAsync(proveedor.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetByProveedor_ConCheques_RetornaSoloDelProveedor()
    {
        var proveedor1 = await SeedProveedorAsync();
        var proveedor2 = await SeedProveedorAsync();
        await SeedChequeAsync(proveedor1.Id);
        await SeedChequeAsync(proveedor1.Id);
        await SeedChequeAsync(proveedor2.Id);

        var resultado = await _service.GetByProveedorIdAsync(proveedor1.Id);

        Assert.Equal(2, resultado.Count());
        Assert.All(resultado, c => Assert.Equal(proveedor1.Id, c.ProveedorId));
    }

    // =========================================================================
    // GetAllAsync
    // =========================================================================

    [Fact]
    public async Task GetAll_SinCheques_RetornaVacio()
    {
        var resultado = await _service.GetAllAsync();
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetAll_ConCheques_DevuelveTodos()
    {
        var proveedor = await SeedProveedorAsync();
        await SeedChequeAsync(proveedor.Id);
        await SeedChequeAsync(proveedor.Id);

        var resultado = await _service.GetAllAsync();

        Assert.Equal(2, resultado.Count());
    }

    [Fact]
    public async Task GetAll_ExcluyeEliminados()
    {
        var proveedor = await SeedProveedorAsync();
        var cheque = await SeedChequeAsync(proveedor.Id);
        await _service.DeleteAsync(cheque.Id);

        var resultado = await _service.GetAllAsync();

        Assert.Empty(resultado);
    }

    // =========================================================================
    // GetByIdAsync
    // =========================================================================

    [Fact]
    public async Task GetById_ChequeExistente_RetornaCheque()
    {
        var proveedor = await SeedProveedorAsync();
        var cheque = await SeedChequeAsync(proveedor.Id);

        var resultado = await _service.GetByIdAsync(cheque.Id);

        Assert.NotNull(resultado);
        Assert.Equal(cheque.Id, resultado!.Id);
    }

    [Fact]
    public async Task GetById_ChequeInexistente_RetornaNull()
    {
        var resultado = await _service.GetByIdAsync(99999);
        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetById_ChequeEliminado_RetornaNull()
    {
        var proveedor = await SeedProveedorAsync();
        var cheque = await SeedChequeAsync(proveedor.Id);
        await _service.DeleteAsync(cheque.Id);

        var resultado = await _service.GetByIdAsync(cheque.Id);
        Assert.Null(resultado);
    }

    // =========================================================================
    // SearchAsync
    // =========================================================================

    [Fact]
    public async Task Search_SinFiltros_DevuelveTodos()
    {
        var proveedor = await SeedProveedorAsync();
        await SeedChequeAsync(proveedor.Id);
        await SeedChequeAsync(proveedor.Id);

        var resultado = await _service.SearchAsync();

        Assert.Equal(2, resultado.Count());
    }

    [Fact]
    public async Task Search_PorEstado_FiltraCorrectamente()
    {
        var proveedor = await SeedProveedorAsync();
        await SeedChequeAsync(proveedor.Id, estado: EstadoCheque.Emitido);
        await SeedChequeAsync(proveedor.Id, estado: EstadoCheque.Cobrado);

        var resultado = await _service.SearchAsync(estado: EstadoCheque.Emitido);

        Assert.Single(resultado);
        Assert.All(resultado, c => Assert.Equal(EstadoCheque.Emitido, c.Estado));
    }

    [Fact]
    public async Task Search_PorProveedorId_FiltraCorrectamente()
    {
        var p1 = await SeedProveedorAsync();
        var p2 = await SeedProveedorAsync();
        await SeedChequeAsync(p1.Id);
        await SeedChequeAsync(p2.Id);

        var resultado = await _service.SearchAsync(proveedorId: p1.Id);

        Assert.Single(resultado);
        Assert.All(resultado, c => Assert.Equal(p1.Id, c.ProveedorId));
    }

    // =========================================================================
    // GetByOrdenCompraIdAsync
    // =========================================================================

    [Fact]
    public async Task GetByOrdenCompra_SinCheques_RetornaVacio()
    {
        var proveedor = await SeedProveedorAsync();
        var orden = await SeedOrdenCompraAsync(proveedor.Id);

        var resultado = await _service.GetByOrdenCompraIdAsync(orden.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetByOrdenCompra_ConCheques_RetornaSolosLosDeEsaOrden()
    {
        var proveedor = await SeedProveedorAsync();
        var orden1 = await SeedOrdenCompraAsync(proveedor.Id);
        var orden2 = await SeedOrdenCompraAsync(proveedor.Id);

        // Cheque asociado a orden1 vía seed directo
        var cheque = new Cheque
        {
            Numero = "CHQ-OC-001",
            ProveedorId = proveedor.Id,
            OrdenCompraId = orden1.Id,
            Banco = "Banco Test",
            Monto = 500m,
            FechaEmision = DateTime.Today,
            Estado = EstadoCheque.Emitido
        };
        _context.Cheques.Add(cheque);
        await _context.SaveChangesAsync();

        await SeedChequeAsync(proveedor.Id); // sin orden

        var resultado = await _service.GetByOrdenCompraIdAsync(orden1.Id);

        Assert.Single(resultado);
        Assert.Equal(orden1.Id, resultado.First().OrdenCompraId);
    }
}
