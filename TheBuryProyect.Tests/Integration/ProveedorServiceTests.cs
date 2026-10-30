using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para ProveedorService.
/// Cubren CreateAsync (CUIT único, asociaciones), DeleteAsync (restricciones
/// por órdenes y cheques vigentes, soft-delete), ExistsCuitAsync y
/// GetProductosProveedorAsync.
/// </summary>
public class ProveedorServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ProveedorService _service;

    public ProveedorServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new ProveedorService(_context, NullLogger<ProveedorService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string UniqueCuit() => Guid.NewGuid().ToString("N")[..11];

    private async Task<Proveedor> SeedProveedorAsync(string? cuit = null)
    {
        cuit ??= UniqueCuit();
        var proveedor = new Proveedor
        {
            Cuit = cuit,
            RazonSocial = "Proveedor-" + cuit,
            Activo = true
        };
        _context.Set<Proveedor>().Add(proveedor);
        await _context.SaveChangesAsync();
        await _context.Entry(proveedor).ReloadAsync();
        return proveedor;
    }

    private async Task<Producto> SeedProductoAsync()
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var categoria = new Categoria { Codigo = codigo, Nombre = "Cat-" + codigo, Activo = true };
        _context.Set<Categoria>().Add(categoria);
        var marca = new Marca { Codigo = codigo, Nombre = "Marca-" + codigo, Activo = true };
        _context.Set<Marca>().Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo,
            Nombre = "Prod-" + codigo,
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = 10m,
            PrecioVenta = 15m,
            PorcentajeIVA = 21m,
            Activo = true
        };
        _context.Set<Producto>().Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    // -------------------------------------------------------------------------
    // CreateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_ProveedorNuevo_Persiste()
    {
        var cuit = UniqueCuit();
        var proveedor = new Proveedor
        {
            Cuit = cuit,
            RazonSocial = "Test SA",
            Activo = true,
            ProveedorProductos = new List<ProveedorProducto>(),
            ProveedorMarcas = new List<ProveedorMarca>(),
            ProveedorCategorias = new List<ProveedorCategoria>()
        };

        await _service.CreateAsync(proveedor);

        Assert.True(proveedor.Id > 0);
        var bd = await _context.Set<Proveedor>().FirstOrDefaultAsync(p => p.Id == proveedor.Id);
        Assert.NotNull(bd);
        Assert.Equal(cuit, bd!.Cuit);
    }

    [Fact]
    public async Task Create_CuitDuplicado_LanzaExcepcion()
    {
        var cuit = UniqueCuit();
        await SeedProveedorAsync(cuit);

        var duplicado = new Proveedor
        {
            Cuit = cuit,
            RazonSocial = "Otro SA",
            Activo = true,
            ProveedorProductos = new List<ProveedorProducto>(),
            ProveedorMarcas = new List<ProveedorMarca>(),
            ProveedorCategorias = new List<ProveedorCategoria>()
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(duplicado));
    }

    [Fact]
    public async Task Create_ConProductoAsociado_GuardaRelacion()
    {
        var producto = await SeedProductoAsync();
        var cuit = UniqueCuit();

        var proveedor = new Proveedor
        {
            Cuit = cuit,
            RazonSocial = "Proveedor con producto",
            Activo = true,
            ProveedorProductos = new List<ProveedorProducto>
            {
                new() { ProductoId = producto.Id }
            },
            ProveedorMarcas = new List<ProveedorMarca>(),
            ProveedorCategorias = new List<ProveedorCategoria>()
        };

        await _service.CreateAsync(proveedor);

        var relacion = await _context.Set<ProveedorProducto>()
            .FirstOrDefaultAsync(pp => pp.ProveedorId == proveedor.Id && pp.ProductoId == producto.Id);
        Assert.NotNull(relacion);
    }

    [Fact]
    public async Task Create_ProductoDuplicadoEnLista_DeduplicaAntesDePersistir()
    {
        var producto = await SeedProductoAsync();
        var cuit = UniqueCuit();

        // Mismo producto dos veces — PrepareAssociationsForCreate debe deduplicar
        var proveedor = new Proveedor
        {
            Cuit = cuit,
            RazonSocial = "Proveedor dup",
            Activo = true,
            ProveedorProductos = new List<ProveedorProducto>
            {
                new() { ProductoId = producto.Id },
                new() { ProductoId = producto.Id }
            },
            ProveedorMarcas = new List<ProveedorMarca>(),
            ProveedorCategorias = new List<ProveedorCategoria>()
        };

        await _service.CreateAsync(proveedor);

        var count = await _context.Set<ProveedorProducto>()
            .CountAsync(pp => pp.ProveedorId == proveedor.Id && pp.ProductoId == producto.Id);
        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // ExistsCuitAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExistsCuit_CuitExistente_ReturnsTrue()
    {
        var proveedor = await SeedProveedorAsync();
        var existe = await _service.ExistsCuitAsync(proveedor.Cuit);
        Assert.True(existe);
    }

    [Fact]
    public async Task ExistsCuit_CuitNoExistente_ReturnsFalse()
    {
        var existe = await _service.ExistsCuitAsync("99999999999");
        Assert.False(existe);
    }

    [Fact]
    public async Task ExistsCuit_ExcluyendoMismoId_ReturnsFalse()
    {
        var proveedor = await SeedProveedorAsync();
        // Al actualizar el mismo registro, no debe considerarse duplicado
        var existe = await _service.ExistsCuitAsync(proveedor.Cuit, proveedor.Id);
        Assert.False(existe);
    }

    // -------------------------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_ProveedorSinDependencias_SoftDelete()
    {
        var proveedor = await SeedProveedorAsync();

        var resultado = await _service.DeleteAsync(proveedor.Id);

        Assert.True(resultado);
        var bd = await _context.Set<Proveedor>()
            .IgnoreQueryFilters()
            .FirstAsync(p => p.Id == proveedor.Id);
        Assert.True(bd.IsDeleted);
    }

    [Fact]
    public async Task Delete_ProveedorNoExiste_RetornaFalse()
    {
        var resultado = await _service.DeleteAsync(99999);
        Assert.False(resultado);
    }

    [Fact]
    public async Task Delete_ConOrdenCompraAsociada_LanzaExcepcion()
    {
        var proveedor = await SeedProveedorAsync();

        _context.Set<OrdenCompra>().Add(new OrdenCompra
        {
            Numero = "OC-DEL-0001",
            ProveedorId = proveedor.Id,
            Estado = EstadoOrdenCompra.Borrador,
            FechaEmision = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeleteAsync(proveedor.Id));

        // El proveedor no debe haber sido eliminado
        var bd = await _context.Set<Proveedor>().FirstAsync(p => p.Id == proveedor.Id);
        Assert.False(bd.IsDeleted);
    }

    [Fact]
    public async Task Delete_ConChequeVigente_LanzaExcepcion()
    {
        var proveedor = await SeedProveedorAsync();

        _context.Set<Cheque>().Add(new Cheque
        {
            ProveedorId = proveedor.Id,
            Numero = "CHQ-0001",
            Banco = "Banco Test",
            Monto = 1000m,
            FechaEmision = DateTime.Today,
            FechaVencimiento = DateTime.Today.AddDays(30),
            Estado = EstadoCheque.Emitido // vigente (no Cobrado/Rechazado/Anulado)
        });
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeleteAsync(proveedor.Id));
    }

    [Fact]
    public async Task Delete_ConChequeCobrado_Permite()
    {
        var proveedor = await SeedProveedorAsync();

        _context.Set<Cheque>().Add(new Cheque
        {
            ProveedorId = proveedor.Id,
            Numero = "CHQ-0002",
            Banco = "Banco Test",
            Monto = 500m,
            FechaEmision = DateTime.Today,
            FechaVencimiento = DateTime.Today.AddDays(30),
            Estado = EstadoCheque.Cobrado // no vigente
        });
        await _context.SaveChangesAsync();

        var resultado = await _service.DeleteAsync(proveedor.Id);
        Assert.True(resultado);
    }

    // -------------------------------------------------------------------------
    // GetProductosProveedorAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetProductosProveedor_SinRelaciones_RetornaVacio()
    {
        var proveedor = await SeedProveedorAsync();

        var productos = await _service.GetProductosProveedorAsync(proveedor.Id);

        Assert.Empty(productos);
    }

    [Fact]
    public async Task GetProductosProveedor_ConRelaciones_RetornaProductos()
    {
        var producto = await SeedProductoAsync();
        var cuit = UniqueCuit();

        var proveedor = new Proveedor
        {
            Cuit = cuit,
            RazonSocial = "Con productos",
            Activo = true,
            ProveedorProductos = new List<ProveedorProducto>
            {
                new() { ProductoId = producto.Id }
            },
            ProveedorMarcas = new List<ProveedorMarca>(),
            ProveedorCategorias = new List<ProveedorCategoria>()
        };

        await _service.CreateAsync(proveedor);

        var productos = await _service.GetProductosProveedorAsync(proveedor.Id);

        Assert.Single(productos);
        Assert.Equal(producto.Id, productos[0].ProductoId);
    }

    // -------------------------------------------------------------------------
    // GetByIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetById_ProveedorExistente_RetornaProveedor()
    {
        var proveedor = await SeedProveedorAsync();

        var resultado = await _service.GetByIdAsync(proveedor.Id);

        Assert.NotNull(resultado);
        Assert.Equal(proveedor.Cuit, resultado!.Cuit);
    }

    [Fact]
    public async Task GetById_ProveedorNoExiste_RetornaNull()
    {
        var resultado = await _service.GetByIdAsync(99999);
        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetById_ProveedorEliminado_RetornaNull()
    {
        var proveedor = await SeedProveedorAsync();
        await _service.DeleteAsync(proveedor.Id);

        var resultado = await _service.GetByIdAsync(proveedor.Id);
        Assert.Null(resultado);
    }

    // =========================================================================
    // GetAllAsync
    // =========================================================================

    [Fact]
    public async Task GetAll_SinProveedores_RetornaVacio()
    {
        var resultado = await _service.GetAllAsync();
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetAll_ConProveedores_DevuelveTodos()
    {
        await SeedProveedorAsync();
        await SeedProveedorAsync();

        var resultado = await _service.GetAllAsync();

        Assert.Equal(2, resultado.Count());
    }

    [Fact]
    public async Task GetAll_ExcluyeEliminados()
    {
        var proveedor = await SeedProveedorAsync();
        await _service.DeleteAsync(proveedor.Id);

        var resultado = await _service.GetAllAsync();

        Assert.Empty(resultado);
    }

    // =========================================================================
    // UpdateAsync
    // =========================================================================

    [Fact]
    public async Task Update_SinRowVersion_LanzaExcepcion()
    {
        var proveedor = await SeedProveedorAsync();
        proveedor.RowVersion = null!;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(proveedor));
    }

    [Fact]
    public async Task Update_HappyPath_ActualizaRazonSocial()
    {
        var proveedor = await SeedProveedorAsync();
        proveedor.RazonSocial = "Nuevo Nombre S.A.";

        await _service.UpdateAsync(proveedor);

        _context.ChangeTracker.Clear();
        var actualizado = await _context.Set<Proveedor>().FirstAsync(p => p.Id == proveedor.Id);
        Assert.Equal("Nuevo Nombre S.A.", actualizado.RazonSocial);
    }

    // =========================================================================
    // Dígito verificador del CUIT: se exige al crear y cuando el CUIT cambia
    // =========================================================================

    private static string CuitValido(string primerosDiez)
    {
        int[] mult = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };
        var suma = 0;
        for (var i = 0; i < 10; i++) suma += (primerosDiez[i] - '0') * mult[i];
        var d = 11 - suma % 11;
        d = d == 11 ? 0 : d == 10 ? 9 : d;
        return primerosDiez + d;
    }

    private static string CuitConVerificadorIncorrecto(string primerosDiez)
        => primerosDiez + ((CuitValido(primerosDiez)[10] - '0' + 1) % 10);

    [Fact]
    public async Task Create_CuitConVerificadorIncorrecto_Lanza_IndicandoElCorrecto()
    {
        var invalido = CuitConVerificadorIncorrecto("3071442109");
        var correcto = CuitValido("3071442109")[10];

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(new Proveedor { Cuit = invalido, RazonSocial = "X", Activo = true }));

        Assert.Contains($"debería ser {correcto}", ex.Message);
        Assert.False(await _context.Set<Proveedor>().AnyAsync(p => p.Cuit == invalido));
    }

    [Fact]
    public async Task Create_CuitValido_Crea()
    {
        var cuit = CuitValido("3071442109");

        await _service.CreateAsync(new Proveedor { Cuit = cuit, RazonSocial = "X", Activo = true });

        Assert.True(await _context.Set<Proveedor>().AnyAsync(p => p.Cuit == cuit));
    }

    [Fact]
    public async Task Create_CuitDelExterior_PrefijoNoHabitual_SoloMiraElVerificador()
    {
        var cuit = CuitValido("5512345678"); // prefijo 55: CUIT país que AFIP emite a entidades del exterior

        await _service.CreateAsync(new Proveedor { Cuit = cuit, RazonSocial = "Exterior", Activo = true });

        Assert.True(await _context.Set<Proveedor>().AnyAsync(p => p.Cuit == cuit));
    }

    [Fact]
    public async Task Update_CuitAntiguoInvalido_SinCambiarElCuit_SigueEditable()
    {
        var antiguo = CuitConVerificadorIncorrecto("2222222222");
        var proveedor = await SeedProveedorAsync(antiguo);
        proveedor.RazonSocial = "Nombre corregido";

        await _service.UpdateAsync(proveedor);

        _context.ChangeTracker.Clear();
        Assert.Equal("Nombre corregido", (await _context.Set<Proveedor>().FirstAsync(p => p.Id == proveedor.Id)).RazonSocial);
    }

    [Fact]
    public async Task Update_CambiarElCuitAOtroInvalido_Lanza()
    {
        var proveedor = await SeedProveedorAsync(CuitConVerificadorIncorrecto("2222222222"));
        proveedor.Cuit = CuitConVerificadorIncorrecto("3071442109");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(proveedor));
    }

    [Fact]
    public async Task Update_CambiarElCuitAUnoValido_Permite()
    {
        var proveedor = await SeedProveedorAsync(CuitConVerificadorIncorrecto("2222222222"));
        var nuevo = CuitValido("3071442109");
        proveedor.Cuit = nuevo;

        await _service.UpdateAsync(proveedor);

        _context.ChangeTracker.Clear();
        Assert.Equal(nuevo, (await _context.Set<Proveedor>().FirstAsync(p => p.Id == proveedor.Id)).Cuit);
    }

    // =========================================================================
    // SearchAsync
    // =========================================================================

    [Fact]
    public async Task Search_SinFiltros_DevuelveTodosLosProveedores()
    {
        await SeedProveedorAsync();
        await SeedProveedorAsync();

        var resultado = await _service.SearchAsync();

        Assert.Equal(2, resultado.Count());
    }

    [Fact]
    public async Task Search_SoloActivos_ExcluyeInactivos()
    {
        var activo = await SeedProveedorAsync();
        var inactivo = await SeedProveedorAsync();
        inactivo.Activo = false;
        _context.Set<Proveedor>().Update(inactivo);
        await _context.SaveChangesAsync();

        var resultado = await _service.SearchAsync(soloActivos: true);

        Assert.All(resultado, p => Assert.True(p.Activo));
        Assert.DoesNotContain(resultado, p => p.Id == inactivo.Id);
    }

    [Fact]
    public async Task Search_PorRazonSocial_FiltraCorrectamente()
    {
        var p1 = await SeedProveedorAsync();
        await SeedProveedorAsync();

        var resultado = await _service.SearchAsync(searchTerm: p1.RazonSocial);

        Assert.Single(resultado);
        Assert.Equal(p1.Id, resultado.First().Id);
    }

    [Fact]
    public async Task Search_PorCuitFormateadoConGuiones_EncuentraElProveedor()
    {
        var p1 = await SeedProveedorAsync("30502680478");
        await SeedProveedorAsync();

        // El listado muestra el CUIT como 30-12345678-9: buscar tal cual se ve debe encontrarlo.
        var resultado = await _service.SearchAsync(searchTerm: " 30-50268047-8 ");

        Assert.Single(resultado);
        Assert.Equal(p1.Id, resultado.First().Id);
    }

    [Fact]
    public async Task SearchPaged_DevuelveSoloLaPaginaPeroTotalesDelFiltroCompleto()
    {
        for (var i = 0; i < 7; i++)
        {
            var p = await SeedProveedorAsync();
            if (i % 2 == 1)
            {
                p.Activo = false;
                _context.Set<Proveedor>().Update(p);
            }
        }
        await _context.SaveChangesAsync();

        var pagina2 = await _service.SearchPagedAsync(null, false, "razonsocial", "asc", pageNumber: 2, pageSize: 3);

        Assert.Equal(3, pagina2.Items.Count);
        Assert.Equal(7, pagina2.Total);
        Assert.Equal(4, pagina2.TotalActivos);
        Assert.Equal(2, pagina2.PageNumber);
    }

    [Fact]
    public async Task SearchPaged_PaginaFueraDeRango_VuelveALaUltima()
    {
        for (var i = 0; i < 5; i++) await SeedProveedorAsync();

        var resultado = await _service.SearchPagedAsync(null, false, null, "asc", pageNumber: 99, pageSize: 2);

        Assert.Equal(3, resultado.PageNumber);
        Assert.Single(resultado.Items);
    }

    [Fact]
    public async Task SearchPaged_PaginasConsecutivas_NoRepitenNiOmiten()
    {
        for (var i = 0; i < 6; i++) await SeedProveedorAsync();

        var ids = new List<int>();
        for (var n = 1; n <= 3; n++)
            ids.AddRange((await _service.SearchPagedAsync(null, false, "cuit", "desc", n, 2)).Items.Select(p => p.Id));

        Assert.Equal(6, ids.Distinct().Count());
    }
}
