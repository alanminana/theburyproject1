using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para MarcaService.
/// Cubren CreateAsync (persistencia, código vacío, nombre vacío, código duplicado, padre inexistente,
/// ciclo jerárquico), UpdateAsync (campos, código duplicado otro, no existe, eliminada),
/// DeleteAsync (soft-delete, con hijos bloquea, con productos bloquea), ExistsCodigoAsync,
/// GetByCodigoAsync, GetByIdAsync, SearchAsync y GetChildrenAsync.
/// </summary>
public class MarcaServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly MarcaService _service;

    public MarcaServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new MarcaService(_context, NullLogger<MarcaService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Marca> SeedMarcaAsync(string? codigo = null, int? parentId = null)
    {
        var code = codigo ?? Guid.NewGuid().ToString("N")[..8];
        var marca = new Marca
        {
            Codigo = code,
            Nombre = "Marca-" + code,
            ParentId = parentId,
            Activo = true
        };
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        await _context.Entry(marca).ReloadAsync();
        return marca;
    }

    private async Task<Categoria> SeedCategoriaAsync()
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = code, Nombre = "Cat-" + code, Activo = true };
        _context.Categorias.Add(cat);
        await _context.SaveChangesAsync();
        return cat;
    }

    private async Task<Producto> SeedProductoAsync(int marcaId)
    {
        var cat = await SeedCategoriaAsync();
        var code = Guid.NewGuid().ToString("N")[..8];
        var prod = new Producto
        {
            Codigo = code, Nombre = "Prod-" + code,
            CategoriaId = cat.Id, MarcaId = marcaId,
            PrecioCompra = 10m, PrecioVenta = 20m,
            PorcentajeIVA = 21m, StockActual = 0m, Activo = true
        };
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        return prod;
    }

    // -------------------------------------------------------------------------
    // CreateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_DatosValidos_Persiste()
    {
        var marca = new Marca { Codigo = "MRC-001", Nombre = "Nike", Activo = true };

        var resultado = await _service.CreateAsync(marca);

        Assert.True(resultado.Id > 0);
        var bd = await _context.Marcas.FirstOrDefaultAsync(m => m.Id == resultado.Id);
        Assert.NotNull(bd);
        Assert.Equal("MRC-001", bd!.Codigo);
    }

    [Fact]
    public async Task Create_CodigoVacio_LanzaExcepcion()
    {
        var marca = new Marca { Codigo = "", Nombre = "Test", Activo = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(marca));
    }

    [Fact]
    public async Task Create_NombreVacio_LanzaExcepcion()
    {
        var marca = new Marca { Codigo = "MRC-X", Nombre = "", Activo = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(marca));
    }

    [Fact]
    public async Task Create_CodigoDuplicado_LanzaExcepcion()
    {
        await SeedMarcaAsync("MRC-DUP");

        var duplicado = new Marca { Codigo = "MRC-DUP", Nombre = "Duplicado", Activo = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(duplicado));
    }

    [Fact]
    public async Task Create_PadreInexistente_LanzaExcepcion()
    {
        var marca = new Marca { Codigo = "MRC-HIJO", Nombre = "Hijo", ParentId = 99999, Activo = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(marca));
    }

    [Fact]
    public async Task Create_ConPadreValido_AsignaParentId()
    {
        var padre = await SeedMarcaAsync();

        var hijo = new Marca
        {
            Codigo = "MRC-HIJO",
            Nombre = "Hijo",
            ParentId = padre.Id,
            Activo = true
        };

        var resultado = await _service.CreateAsync(hijo);

        Assert.Equal(padre.Id, resultado.ParentId);
    }

    // -------------------------------------------------------------------------
    // UpdateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Update_CamposValidos_Actualiza()
    {
        var marca = await SeedMarcaAsync();

        marca.Nombre = "Nombre Nuevo";
        marca.Descripcion = "Desc actualizada";
        marca.PaisOrigen = "AR";

        var resultado = await _service.UpdateAsync(marca);

        Assert.Equal("Nombre Nuevo", resultado.Nombre);
        Assert.Equal("Desc actualizada", resultado.Descripcion);
        Assert.Equal("AR", resultado.PaisOrigen);
    }

    [Fact]
    public async Task Update_NoExiste_LanzaExcepcion()
    {
        var marca = new Marca { Id = 99999, Codigo = "X", Nombre = "X", Activo = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(marca));
    }

    [Fact]
    public async Task Update_CodigoDuplicadoDeOtra_LanzaExcepcion()
    {
        var marca1 = await SeedMarcaAsync("MRC-A");
        var marca2 = await SeedMarcaAsync("MRC-B");

        marca2.Codigo = marca1.Codigo; // intenta tomar el código de marca1

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(marca2));
    }

    [Fact]
    public async Task Update_MismoCodigoPropia_NoLanzaExcepcion()
    {
        var marca = await SeedMarcaAsync("MRC-SELF");
        marca.Nombre = "Nombre modificado";

        var resultado = await _service.UpdateAsync(marca);

        Assert.Equal("Nombre modificado", resultado.Nombre);
    }

    [Fact]
    public async Task Update_Eliminada_LanzaExcepcion()
    {
        var marca = await SeedMarcaAsync();
        marca.IsDeleted = true;
        await _context.SaveChangesAsync();

        marca.Nombre = "Modificado";

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(marca));
    }

    // -------------------------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_SinDependencias_SoftDelete()
    {
        var marca = await SeedMarcaAsync();

        var resultado = await _service.DeleteAsync(marca.Id);

        Assert.True(resultado);
        var bd = await _context.Marcas.IgnoreQueryFilters().FirstAsync(m => m.Id == marca.Id);
        Assert.True(bd.IsDeleted);
    }

    [Fact]
    public async Task Delete_ConHijos_LanzaExcepcion()
    {
        var padre = await SeedMarcaAsync();
        await SeedMarcaAsync(parentId: padre.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(padre.Id));
    }

    [Fact]
    public async Task Delete_ConProductos_LanzaExcepcion()
    {
        var marca = await SeedMarcaAsync();
        await SeedProductoAsync(marca.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(marca.Id));
    }

    [Fact]
    public async Task Delete_NoExiste_RetornaFalse()
    {
        var resultado = await _service.DeleteAsync(99999);

        Assert.False(resultado);
    }

    [Fact]
    public async Task Delete_YaEliminada_RetornaFalse()
    {
        var marca = await SeedMarcaAsync();
        await _service.DeleteAsync(marca.Id); // primer delete

        var resultado = await _service.DeleteAsync(marca.Id); // segundo delete

        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // ExistsCodigoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExistsCodigo_Existente_RetornaTrue()
    {
        await SeedMarcaAsync("MRC-EXISTS");

        Assert.True(await _service.ExistsCodigoAsync("MRC-EXISTS"));
    }

    [Fact]
    public async Task ExistsCodigo_NoExistente_RetornaFalse()
    {
        Assert.False(await _service.ExistsCodigoAsync("NO-EXISTE"));
    }

    [Fact]
    public async Task ExistsCodigo_ExcluyendoMismoId_RetornaFalse()
    {
        var marca = await SeedMarcaAsync("MRC-SELF2");

        Assert.False(await _service.ExistsCodigoAsync("MRC-SELF2", marca.Id));
    }

    // -------------------------------------------------------------------------
    // GetByCodigoAsync / GetByIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByCodigo_Existente_RetornaMarca()
    {
        var marca = await SeedMarcaAsync("MRC-GET");

        var resultado = await _service.GetByCodigoAsync("MRC-GET");

        Assert.NotNull(resultado);
        Assert.Equal(marca.Id, resultado!.Id);
    }

    [Fact]
    public async Task GetByCodigo_NoExistente_RetornaNull()
    {
        var resultado = await _service.GetByCodigoAsync("NO-EXISTE");

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetById_Existente_RetornaMarca()
    {
        var marca = await SeedMarcaAsync();

        var resultado = await _service.GetByIdAsync(marca.Id);

        Assert.NotNull(resultado);
        Assert.Equal(marca.Id, resultado!.Id);
    }

    [Fact]
    public async Task GetById_Eliminada_RetornaNull()
    {
        var marca = await SeedMarcaAsync();
        await _service.DeleteAsync(marca.Id);

        var resultado = await _service.GetByIdAsync(marca.Id);

        Assert.Null(resultado);
    }

    // -------------------------------------------------------------------------
    // SearchAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Search_SinFiltros_RetornaTodas()
    {
        await SeedMarcaAsync();
        await SeedMarcaAsync();

        var resultado = await _service.SearchAsync();

        Assert.True(resultado.Count() >= 2);
    }

    [Fact]
    public async Task Search_SoloActivos_ExcluyeInactivos()
    {
        var activa = await SeedMarcaAsync();
        var inactiva = await SeedMarcaAsync();
        inactiva.Activo = false;
        await _context.SaveChangesAsync();

        var resultado = await _service.SearchAsync(soloActivos: true);

        Assert.DoesNotContain(resultado, m => m.Id == inactiva.Id);
    }

    // -------------------------------------------------------------------------
    // GetChildrenAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetChildren_ConHijos_RetornaHijos()
    {
        var padre = await SeedMarcaAsync();
        await SeedMarcaAsync(parentId: padre.Id);
        await SeedMarcaAsync(parentId: padre.Id);

        var resultado = await _service.GetChildrenAsync(padre.Id);

        Assert.Equal(2, resultado.Count());
        Assert.All(resultado, m => Assert.Equal(padre.Id, m.ParentId));
    }

    [Fact]
    public async Task GetChildren_SinHijos_RetornaVacio()
    {
        var padre = await SeedMarcaAsync();

        var resultado = await _service.GetChildrenAsync(padre.Id);

        Assert.Empty(resultado);
    }

    // -------------------------------------------------------------------------
    // Ciclo jerárquico
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Update_CicloHierarquico_LanzaExcepcion()
    {
        var padre = await SeedMarcaAsync();
        var hijo = await SeedMarcaAsync(parentId: padre.Id);

        // Intenta hacer que el padre sea hijo del hijo (crea ciclo)
        padre.ParentId = hijo.Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(padre));
    }

    // =========================================================================
    // GetAllAsync
    // =========================================================================

    [Fact]
    public async Task GetAll_DevuelveTodasLasMarcasActivas()
    {
        var baseCount = (await _service.GetAllAsync()).Count();
        await SeedMarcaAsync();
        await SeedMarcaAsync();

        var resultado = await _service.GetAllAsync();

        Assert.Equal(baseCount + 2, resultado.Count());
    }

    [Fact]
    public async Task GetAll_ExcluyeEliminadas()
    {
        var baseCount = (await _service.GetAllAsync()).Count();
        var marca = await SeedMarcaAsync();
        await _service.DeleteAsync(marca.Id);

        var resultado = await _service.GetAllAsync();

        Assert.Equal(baseCount, resultado.Count());
    }
}
