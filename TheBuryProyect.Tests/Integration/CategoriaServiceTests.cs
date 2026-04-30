using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para CategoriaService.
/// Cubren CreateAsync (persistencia, código vacío, nombre vacío, código duplicado, padre inexistente,
/// ciclo jerárquico), UpdateAsync (campos, código duplicado otro, no existe, eliminada),
/// DeleteAsync (soft-delete, con hijos bloquea, con productos bloquea), ExistsCodigoAsync,
/// GetByCodigoAsync, GetByIdAsync, SearchAsync y GetChildrenAsync.
/// </summary>
public class CategoriaServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CategoriaService _service;

    public CategoriaServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new CategoriaService(_context, NullLogger<CategoriaService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Categoria> SeedCategoriaAsync(string? codigo = null, int? parentId = null)
    {
        var code = codigo ?? Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria
        {
            Codigo = code,
            Nombre = "Cat-" + code,
            ParentId = parentId,
            Activo = true
        };
        _context.Categorias.Add(cat);
        await _context.SaveChangesAsync();
        await _context.Entry(cat).ReloadAsync();
        return cat;
    }

    private async Task<Marca> SeedMarcaAsync()
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var marca = new Marca { Codigo = code, Nombre = "Marca-" + code, Activo = true };
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        return marca;
    }

    private async Task<Producto> SeedProductoAsync(int categoriaId)
    {
        var marca = await SeedMarcaAsync();
        var code = Guid.NewGuid().ToString("N")[..8];
        var prod = new Producto
        {
            Codigo = code, Nombre = "Prod-" + code,
            CategoriaId = categoriaId, MarcaId = marca.Id,
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
        var cat = new Categoria { Codigo = "CAT-001", Nombre = "Electrónica", Activo = true };

        var resultado = await _service.CreateAsync(cat);

        Assert.True(resultado.Id > 0);
        var bd = await _context.Categorias.FirstOrDefaultAsync(c => c.Id == resultado.Id);
        Assert.NotNull(bd);
        Assert.Equal("CAT-001", bd!.Codigo);
    }

    [Fact]
    public async Task Create_CodigoVacio_LanzaExcepcion()
    {
        var cat = new Categoria { Codigo = "", Nombre = "Test", Activo = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(cat));
    }

    [Fact]
    public async Task Create_NombreVacio_LanzaExcepcion()
    {
        var cat = new Categoria { Codigo = "CAT-X", Nombre = "", Activo = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(cat));
    }

    [Fact]
    public async Task Create_CodigoDuplicado_LanzaExcepcion()
    {
        await SeedCategoriaAsync("CAT-DUP");

        var duplicado = new Categoria { Codigo = "CAT-DUP", Nombre = "Duplicado", Activo = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(duplicado));
    }

    [Fact]
    public async Task Create_PadreInexistente_LanzaExcepcion()
    {
        var cat = new Categoria { Codigo = "CAT-HIJO", Nombre = "Hijo", ParentId = 99999, Activo = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(cat));
    }

    [Fact]
    public async Task Create_ConPadreValido_AsignaParentId()
    {
        var padre = await SeedCategoriaAsync();

        var hijo = new Categoria
        {
            Codigo = "CAT-HIJO",
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
        var cat = await SeedCategoriaAsync();

        cat.Nombre = "Nombre Nuevo";
        cat.Descripcion = "Desc actualizada";

        var resultado = await _service.UpdateAsync(cat);

        Assert.Equal("Nombre Nuevo", resultado.Nombre);
        Assert.Equal("Desc actualizada", resultado.Descripcion);
    }

    [Fact]
    public async Task Update_NoExiste_LanzaExcepcion()
    {
        var cat = new Categoria { Id = 99999, Codigo = "X", Nombre = "X", Activo = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(cat));
    }

    [Fact]
    public async Task Update_CodigoDuplicadoDeOtra_LanzaExcepcion()
    {
        var cat1 = await SeedCategoriaAsync("CAT-A");
        var cat2 = await SeedCategoriaAsync("CAT-B");

        cat2.Codigo = cat1.Codigo; // intenta tomar el código de cat1

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(cat2));
    }

    [Fact]
    public async Task Update_MismoCodigoPropia_NoLanzaExcepcion()
    {
        var cat = await SeedCategoriaAsync("CAT-SELF");
        cat.Nombre = "Nombre modificado";

        var resultado = await _service.UpdateAsync(cat);

        Assert.Equal("Nombre modificado", resultado.Nombre);
    }

    [Fact]
    public async Task Update_Eliminada_LanzaExcepcion()
    {
        var cat = await SeedCategoriaAsync();
        cat.IsDeleted = true;
        await _context.SaveChangesAsync();

        cat.Nombre = "Modificado";

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(cat));
    }

    // -------------------------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_SinDependencias_SoftDelete()
    {
        var cat = await SeedCategoriaAsync();

        var resultado = await _service.DeleteAsync(cat.Id);

        Assert.True(resultado);
        var bd = await _context.Categorias.IgnoreQueryFilters().FirstAsync(c => c.Id == cat.Id);
        Assert.True(bd.IsDeleted);
    }

    [Fact]
    public async Task Delete_ConHijos_LanzaExcepcion()
    {
        var padre = await SeedCategoriaAsync();
        await SeedCategoriaAsync(parentId: padre.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(padre.Id));
    }

    [Fact]
    public async Task Delete_ConProductos_LanzaExcepcion()
    {
        var cat = await SeedCategoriaAsync();
        await SeedProductoAsync(cat.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(cat.Id));
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
        var cat = await SeedCategoriaAsync();
        await _service.DeleteAsync(cat.Id); // primer delete

        var resultado = await _service.DeleteAsync(cat.Id); // segundo delete

        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // ExistsCodigoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExistsCodigo_Existente_RetornaTrue()
    {
        await SeedCategoriaAsync("CAT-EXISTS");

        Assert.True(await _service.ExistsCodigoAsync("CAT-EXISTS"));
    }

    [Fact]
    public async Task ExistsCodigo_NoExistente_RetornaFalse()
    {
        Assert.False(await _service.ExistsCodigoAsync("NO-EXISTE"));
    }

    [Fact]
    public async Task ExistsCodigo_ExcluyendoMismoId_RetornaFalse()
    {
        var cat = await SeedCategoriaAsync("CAT-SELF2");

        Assert.False(await _service.ExistsCodigoAsync("CAT-SELF2", cat.Id));
    }

    // -------------------------------------------------------------------------
    // GetByCodigoAsync / GetByIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByCodigo_Existente_RetornaCategoria()
    {
        var cat = await SeedCategoriaAsync("CAT-GET");

        var resultado = await _service.GetByCodigoAsync("CAT-GET");

        Assert.NotNull(resultado);
        Assert.Equal(cat.Id, resultado!.Id);
    }

    [Fact]
    public async Task GetByCodigo_NoExistente_RetornaNull()
    {
        var resultado = await _service.GetByCodigoAsync("NO-EXISTE");

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetById_Existente_RetornaCategoria()
    {
        var cat = await SeedCategoriaAsync();

        var resultado = await _service.GetByIdAsync(cat.Id);

        Assert.NotNull(resultado);
        Assert.Equal(cat.Id, resultado!.Id);
    }

    [Fact]
    public async Task GetById_Eliminada_RetornaNull()
    {
        var cat = await SeedCategoriaAsync();
        await _service.DeleteAsync(cat.Id);

        var resultado = await _service.GetByIdAsync(cat.Id);

        Assert.Null(resultado);
    }

    // -------------------------------------------------------------------------
    // SearchAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Search_SinFiltros_RetornaTodas()
    {
        await SeedCategoriaAsync();
        await SeedCategoriaAsync();

        var resultado = await _service.SearchAsync();

        Assert.True(resultado.Count() >= 2);
    }

    [Fact]
    public async Task Search_SoloActivos_ExcluyeInactivos()
    {
        var activa = await SeedCategoriaAsync();
        var inactiva = await SeedCategoriaAsync();
        inactiva.Activo = false;
        await _context.SaveChangesAsync();

        var resultado = await _service.SearchAsync(soloActivos: true);

        Assert.DoesNotContain(resultado, c => c.Id == inactiva.Id);
    }

    // -------------------------------------------------------------------------
    // GetChildrenAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetChildren_ConHijos_RetornaHijos()
    {
        var padre = await SeedCategoriaAsync();
        var hijo1 = await SeedCategoriaAsync(parentId: padre.Id);
        var hijo2 = await SeedCategoriaAsync(parentId: padre.Id);

        var resultado = await _service.GetChildrenAsync(padre.Id);

        Assert.Equal(2, resultado.Count());
        Assert.All(resultado, c => Assert.Equal(padre.Id, c.ParentId));
    }

    [Fact]
    public async Task GetChildren_SinHijos_RetornaVacio()
    {
        var padre = await SeedCategoriaAsync();

        var resultado = await _service.GetChildrenAsync(padre.Id);

        Assert.Empty(resultado);
    }

    // -------------------------------------------------------------------------
    // Ciclo jerárquico
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Update_CicloHierarquico_LanzaExcepcion()
    {
        // A → B, intenta B.Parent = A y A.Parent = B → ciclo
        var padre = await SeedCategoriaAsync();
        var hijo = await SeedCategoriaAsync(parentId: padre.Id);

        // Intenta hacer que el padre sea hijo del hijo (crea ciclo)
        padre.ParentId = hijo.Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(padre));
    }

    // =========================================================================
    // AlicuotaIVA
    // =========================================================================

    private async Task<AlicuotaIVA> SeedAlicuotaAsync(bool activa = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var ali = new AlicuotaIVA
        {
            Codigo = "IVA_" + suffix,
            Nombre = "IVA-" + suffix,
            Porcentaje = 21m,
            Activa = activa,
            EsPredeterminada = false
        };
        _context.AlicuotasIVA.Add(ali);
        await _context.SaveChangesAsync();
        await _context.Entry(ali).ReloadAsync();
        return ali;
    }

    [Fact]
    public async Task Create_ConAlicuotaIVAActiva_PersistAlicuotaIVAId()
    {
        var alicuota = await SeedAlicuotaAsync();
        var cat = new Categoria { Codigo = "CAT-ALI1", Nombre = "Con Alícuota", AlicuotaIVAId = alicuota.Id, Activo = true };

        var resultado = await _service.CreateAsync(cat);

        _context.ChangeTracker.Clear();
        var bd = await _context.Categorias.FirstAsync(c => c.Id == resultado.Id);
        Assert.Equal(alicuota.Id, bd.AlicuotaIVAId);
    }

    [Fact]
    public async Task Update_AlicuotaIVAId_CambiaAlicuota()
    {
        var alicuota = await SeedAlicuotaAsync();
        var cat = await SeedCategoriaAsync();

        cat.AlicuotaIVAId = alicuota.Id;
        var resultado = await _service.UpdateAsync(cat);

        _context.ChangeTracker.Clear();
        var bd = await _context.Categorias.FirstAsync(c => c.Id == cat.Id);
        Assert.Equal(alicuota.Id, bd.AlicuotaIVAId);
    }

    [Fact]
    public async Task Update_AlicuotaIVANull_LimpiaPreviaAsignacion()
    {
        var alicuota = await SeedAlicuotaAsync();
        var cat = await SeedCategoriaAsync();
        cat.AlicuotaIVAId = alicuota.Id;
        _context.Categorias.Update(cat);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        cat = await _context.Categorias.FirstAsync(c => c.Id == cat.Id);

        cat.AlicuotaIVAId = null;
        await _service.UpdateAsync(cat);

        _context.ChangeTracker.Clear();
        var bd = await _context.Categorias.FirstAsync(c => c.Id == cat.Id);
        Assert.Null(bd.AlicuotaIVAId);
    }

    [Fact]
    public async Task Create_ConAlicuotaIVAInactiva_LanzaExcepcion()
    {
        var inactiva = await SeedAlicuotaAsync(activa: false);
        var cat = new Categoria { Codigo = "CAT-INV", Nombre = "Inválida", AlicuotaIVAId = inactiva.Id, Activo = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(cat));
    }

    // =========================================================================
    // GetAllAsync
    // =========================================================================

    [Fact]
    public async Task GetAll_DevuelveTodasLasCategoriasActivas()
    {
        var baseCount = (await _service.GetAllAsync()).Count();
        await SeedCategoriaAsync();
        await SeedCategoriaAsync();

        var resultado = await _service.GetAllAsync();

        Assert.Equal(baseCount + 2, resultado.Count());
    }

    [Fact]
    public async Task GetAll_ExcluyeEliminadas()
    {
        var baseCount = (await _service.GetAllAsync()).Count();
        var cat = await SeedCategoriaAsync();
        await _service.DeleteAsync(cat.Id);

        var resultado = await _service.GetAllAsync();

        Assert.Equal(baseCount, resultado.Count());
    }
}
