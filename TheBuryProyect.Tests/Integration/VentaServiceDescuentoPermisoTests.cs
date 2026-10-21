using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Validators;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// DESCUENTO-PERMISO-01 (PROMPT 76): otorgar, subir o modificar un descuento comercial manual exige el
/// permiso independiente ventas.discount. Ni ventas.create ni ventas.update lo implican, y los campos
/// ocultos del formulario no son una vía de autorización: decide el servidor con el usuario autenticado.
///
/// Producto: 1.000 (IVA 0 %), 2 unidades → subtotal elegible 2.000.
/// </summary>
public class VentaServiceDescuentoPermisoTests : IDisposable
{
    private const decimal PrecioProducto = 1_000m;
    private const int Cantidad = 2;

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;
    private readonly PermisosCurrentUserService _usuario = new("ventas.create", "ventas.update");
    private readonly AperturaCaja _apertura;
    private readonly Cliente _cliente;
    private readonly Producto _producto;
    private readonly Producto _otroProducto;

    public VentaServiceDescuentoPermisoTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        var caja = new Caja { Codigo = "DPM1", Nombre = "Caja Permiso", IsDeleted = false, RowVersion = new byte[8] };
        var categoria = new Categoria { Codigo = "CAT-DPM", Nombre = "Cat DPM", IsDeleted = false, RowVersion = new byte[8] };
        var marca = new Marca { Codigo = "MAR-DPM", Nombre = "Marca DPM", IsDeleted = false, RowVersion = new byte[8] };
        _context.AddRange(caja, categoria, marca);
        _context.SaveChanges();

        _apertura = new AperturaCaja
        {
            CajaId = caja.Id, UsuarioApertura = "testuser", MontoInicial = 0m,
            Cerrada = false, IsDeleted = false, RowVersion = new byte[8]
        };
        _cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Permiso", TipoDocumento = "DNI", NumeroDocumento = "30000076",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal, IsDeleted = false, RowVersion = new byte[8]
        };
        _producto = NuevoProducto("P-DPM", categoria.Id, marca.Id);
        _otroProducto = NuevoProducto("P-DPM2", categoria.Id, marca.Id);
        _context.AddRange(_apertura, _cliente, _producto, _otroProducto);
        _context.SaveChanges();

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        _service = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            null!,
            null!,
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            _usuario,
            null!,
            new StubCajaAperturaActivaFija(_apertura),
            null!,
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static Producto NuevoProducto(string codigo, int categoriaId, int marcaId) => new()
    {
        Nombre = $"Producto {codigo}", Codigo = codigo, PrecioCompra = 600m, PrecioVenta = PrecioProducto,
        PorcentajeIVA = 0m, StockActual = 100, CategoriaId = categoriaId, MarcaId = marcaId,
        IsDeleted = false, RowVersion = new byte[8]
    };

    private void ConPermisoDescuento() => _usuario.Conceder("ventas.discount");
    private void SinPermisoDescuento() => _usuario.Retirar("ventas.discount");

    private VentaViewModel Vm(decimal descuentoGeneral = 0m, decimal descuentoLinea = 0m) => new()
    {
        ClienteId = _cliente.Id,
        FechaVenta = DateTime.UtcNow,
        Estado = EstadoVenta.Cotizacion,
        TipoPago = TipoPago.Efectivo,
        Descuento = descuentoGeneral,
        Detalles = new List<VentaDetalleViewModel>
        {
            new() { ProductoId = _producto.Id, Cantidad = Cantidad, PrecioUnitario = 0m, Descuento = descuentoLinea }
        }
    };

    private async Task<Venta> VentaPersistidaAsync(int id)
    {
        _context.ChangeTracker.Clear();
        return await _context.Ventas.AsNoTracking().Include(v => v.Detalles).SingleAsync(v => v.Id == id);
    }

    private async Task<int> ContarVentasAsync() => await _context.Ventas.IgnoreQueryFilters().CountAsync();

    /// <summary>Crea (con permiso) una venta con 10 % de línea y 500 de descuento general; deja al usuario SIN permiso.</summary>
    private async Task<Venta> VentaConDescuentosCreadaPorOtroAsync()
    {
        ConPermisoDescuento();
        var creada = await _service.CreateAsync(Vm(500m, 10m));
        // SQLite no genera RowVersion (en SQL Server lo hace la columna rowversion).
        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE Ventas SET RowVersion = X'0102030405060708' WHERE Id = {0}", creada.Id);
        SinPermisoDescuento();
        return await VentaPersistidaAsync(creada.Id);
    }

    private VentaViewModel VmEdicion(Venta venta, decimal descuentoGeneral, decimal descuentoLinea, int cantidad = Cantidad)
    {
        var vm = Vm(descuentoGeneral, descuentoLinea);
        vm.Id = venta.Id;
        vm.RowVersion = venta.RowVersion;
        vm.Observaciones = "Editado en test";
        vm.Detalles[0].Cantidad = cantidad;
        return vm;
    }

    // =====================================================================
    // Create
    // =====================================================================

    [Fact]
    public async Task Create_ConVentasCreateSinPermisoDescuento_SinDescuentos_SeCreaNormalmente()
    {
        var creada = await _service.CreateAsync(Vm());

        var venta = await VentaPersistidaAsync(creada.Id);
        Assert.Equal(0m, venta.Descuento);
        Assert.Equal(2_000m, venta.Total);
    }

    [Fact]
    public async Task Create_SinPermisoDescuento_DescuentoDeLinea_EsRechazadoYNoPersiste()
    {
        var ex = await Assert.ThrowsAsync<DescuentoNoAutorizadoException>(() => _service.CreateAsync(Vm(0m, 10m)));

        Assert.Equal(TheBuryProject.Models.Constants.VentaConstants.PermisoDescuento.MensajeSinPermiso, ex.Message);
        Assert.Equal(0, await ContarVentasAsync());
        Assert.Empty(await _context.VentaDetalles.IgnoreQueryFilters().ToListAsync());
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("500")]
    [InlineData("2000")]
    public async Task Create_SinPermisoDescuento_DescuentoGeneral_EsRechazadoYNoPersiste(string descuento)
    {
        await Assert.ThrowsAsync<DescuentoNoAutorizadoException>(() =>
            _service.CreateAsync(Vm(decimal.Parse(descuento, System.Globalization.CultureInfo.InvariantCulture))));

        Assert.Equal(0, await ContarVentasAsync());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("100.01")]
    public async Task Create_SinPermisoDescuento_ValoresFueraDeRango_TambienSeRechazanPorPermiso(string descuento)
    {
        // Sin permiso la regla de autorización corta primero: ni siquiera se evalúa el rango.
        await Assert.ThrowsAsync<DescuentoNoAutorizadoException>(() =>
            _service.CreateAsync(Vm(0m, decimal.Parse(descuento, System.Globalization.CultureInfo.InvariantCulture))));

        Assert.Equal(0, await ContarVentasAsync());
    }

    [Fact]
    public async Task Create_ConPermisoDescuento_AplicaDescuentoDeLineaYGeneral()
    {
        ConPermisoDescuento();

        var creada = await _service.CreateAsync(Vm(500m, 10m));

        var venta = await VentaPersistidaAsync(creada.Id);
        Assert.Equal(500m, venta.Descuento);
        Assert.Equal(10m, venta.Detalles.Single(d => !d.IsDeleted).Descuento);
        Assert.Equal(1_300m, venta.Total); // 2.000 - 10 % = 1.800 - 500
    }

    [Fact]
    public async Task Create_ConPermisoDescuento_100PorCiento_SigueSiendoPermitido()
    {
        ConPermisoDescuento();

        var creada = await _service.CreateAsync(Vm(0m, 100m));

        Assert.Equal(0m, (await VentaPersistidaAsync(creada.Id)).Total);
    }

    [Fact]
    public async Task Create_ConPermisoDescuento_MasDe100PorCiento_SeRechazaPorRango_NoPorPermiso()
    {
        ConPermisoDescuento();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(Vm(0m, 100.01m)));

        Assert.IsNotType<DescuentoNoAutorizadoException>(ex);
        Assert.Contains("entre 0 y 100", ex.Message);
        Assert.Equal(0, await ContarVentasAsync());
    }

    [Fact]
    public async Task Create_ConPermisoDescuento_GeneralMayorAlSubtotal_SeRechazaPorRango()
    {
        ConPermisoDescuento();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(Vm(2_000.01m)));

        Assert.IsNotType<DescuentoNoAutorizadoException>(ex);
        Assert.Contains("superar el subtotal", ex.Message);
    }

    [Fact]
    public async Task Create_PermisoRetiradoEnMitadDeSesion_DejaDeAceptarDescuentos()
    {
        ConPermisoDescuento();
        await _service.CreateAsync(Vm(0m, 10m)); // con permiso: OK

        SinPermisoDescuento();                   // el rol pierde el permiso (los claims se releen por request)
        await Assert.ThrowsAsync<DescuentoNoAutorizadoException>(() => _service.CreateAsync(Vm(0m, 10m)));

        Assert.Equal(1, await ContarVentasAsync());
    }

    // =====================================================================
    // Update — venta que ya tiene descuentos
    // =====================================================================

    [Fact]
    public async Task Update_SinPermiso_EdicionQueNoTocaDescuentos_ConservaLosPersistidos()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();
        Assert.Equal(1_300m, original.Total);

        // El formulario sin permiso no renderiza descuentos: llegan en 0 aunque la venta los tenga.
        var resultado = await _service.UpdateAsync(original.Id, VmEdicion(original, 0m, 0m));

        Assert.NotNull(resultado);
        var despues = await VentaPersistidaAsync(original.Id);
        Assert.Equal("Editado en test", despues.Observaciones);
        Assert.Equal(500m, despues.Descuento);
        Assert.Equal(10m, despues.Detalles.Single(d => !d.IsDeleted).Descuento);
        Assert.Equal(1_300m, despues.Total);
    }

    [Fact]
    public async Task Update_SinPermiso_DisminuyeCantidad_ConservaElPorcentajeDeLinea()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();

        await _service.UpdateAsync(original.Id, VmEdicion(original, 0m, 0m, cantidad: 1));

        var despues = await VentaPersistidaAsync(original.Id);
        Assert.Equal(10m, despues.Detalles.Single(d => !d.IsDeleted).Descuento);
        Assert.Equal(500m, despues.Descuento);
        Assert.Equal(400m, despues.Total); // 1.000 - 10 % = 900 - 500
    }

    [Fact]
    public async Task Update_SinPermiso_AumentaCantidadDeLineaConDescuento_EsRechazadoPorqueCrecheriaElImporteDescontado()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();

        var ex = await Assert.ThrowsAsync<DescuentoNoAutorizadoException>(() =>
            _service.UpdateAsync(original.Id, VmEdicion(original, 0m, 0m, cantidad: 3)));

        Assert.Contains("aumentar la cantidad", ex.Message);
        var despues = await VentaPersistidaAsync(original.Id);
        Assert.Equal(1_300m, despues.Total);
        Assert.Equal(2, despues.Detalles.Single(d => !d.IsDeleted).Cantidad);
    }

    [Fact]
    public async Task Update_ConPermiso_PuedeAumentarCantidadDeLineaConDescuento()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();
        ConPermisoDescuento();

        await _service.UpdateAsync(original.Id, VmEdicion(original, 500m, 10m, cantidad: 3));

        Assert.Equal(2_200m, (await VentaPersistidaAsync(original.Id)).Total); // 3.000 - 10 % = 2.700 - 500
    }

    // ── Productos repetidos: el descuento sigue a la LÍNEA (por Id), no a la posición ─────────────

    /// <summary>Venta con dos líneas del mismo producto: A×1 al 10 % y A×3 al 0 %. Deja al usuario SIN permiso.</summary>
    private async Task<(Venta Venta, int IdConDescuento, int IdSinDescuento)> VentaConProductoRepetidoAsync()
    {
        ConPermisoDescuento();
        var vm = Vm();
        vm.Detalles.Clear();
        vm.Detalles.Add(new VentaDetalleViewModel { ProductoId = _producto.Id, Cantidad = 1, PrecioUnitario = 0m, Descuento = 10m });
        vm.Detalles.Add(new VentaDetalleViewModel { ProductoId = _producto.Id, Cantidad = 3, PrecioUnitario = 0m, Descuento = 0m });
        var creada = await _service.CreateAsync(vm);
        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE Ventas SET RowVersion = X'0102030405060708' WHERE Id = {0}", creada.Id);
        SinPermisoDescuento();

        var venta = await VentaPersistidaAsync(creada.Id);
        var activas = venta.Detalles.Where(d => !d.IsDeleted).OrderBy(d => d.Id).ToList();
        return (venta, activas.Single(d => d.Descuento == 10m).Id, activas.Single(d => d.Descuento == 0m).Id);
    }

    private static VentaDetalleViewModel Linea(int id, int productoId, int cantidad, decimal descuento = 0m) => new()
    {
        Id = id, ProductoId = productoId, Cantidad = cantidad, PrecioUnitario = 0m, Descuento = descuento
    };

    private VentaViewModel VmConLineas(Venta venta, params VentaDetalleViewModel[] lineas)
    {
        var vm = Vm();
        vm.Id = venta.Id;
        vm.RowVersion = venta.RowVersion;
        vm.Detalles = lineas.ToList();
        return vm;
    }

    [Fact]
    public async Task Update_SinPermiso_ProductoRepetido_ReordenarLasLineas_NoMueveElDescuento()
    {
        var (venta, idDesc, idSin) = await VentaConProductoRepetidoAsync();
        Assert.Equal(3_900m, venta.Total); // A×1 al 10 % = 900 + A×3 al 0 % = 3.000

        // El operador reordena: primero la línea de 3 unidades, después la de 1.
        await _service.UpdateAsync(venta.Id, VmConLineas(venta, Linea(idSin, _producto.Id, 3), Linea(idDesc, _producto.Id, 1)));

        var activas = (await VentaPersistidaAsync(venta.Id)).Detalles.Where(d => !d.IsDeleted).ToList();
        Assert.Equal(10m, activas.Single(d => d.Cantidad == 1).Descuento);
        Assert.Equal(0m, activas.Single(d => d.Cantidad == 3).Descuento);
    }

    [Fact]
    public async Task Update_SinPermiso_ProductoRepetido_BorrarLaLineaConDescuento_NoTrasladaElDescuentoALaOtra()
    {
        var (venta, _, idSin) = await VentaConProductoRepetidoAsync();

        // Se borra la línea con 10 %; queda solo la de 3 unidades al 0 %.
        await _service.UpdateAsync(venta.Id, VmConLineas(venta, Linea(idSin, _producto.Id, 3)));

        var despues = await VentaPersistidaAsync(venta.Id);
        var unica = despues.Detalles.Single(d => !d.IsDeleted);
        Assert.Equal(0m, unica.Descuento);
        Assert.Equal(3_000m, despues.Total); // sin descuento: nunca 2.700
    }

    [Fact]
    public async Task Update_SinPermiso_ProductoRepetido_SinIds_EsAmbiguoYSeRechaza()
    {
        var (venta, _, _) = await VentaConProductoRepetidoAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(venta.Id, VmConLineas(venta, Linea(0, _producto.Id, 3), Linea(0, _producto.Id, 1))));

        Assert.IsNotType<DescuentoNoAutorizadoException>(ex);
        Assert.Contains("No se puede determinar a qué línea", ex.Message);
        var despues = await VentaPersistidaAsync(venta.Id);
        Assert.Equal(2, despues.Detalles.Count(d => !d.IsDeleted));
        Assert.DoesNotContain(despues.Detalles, d => d.IsDeleted);
    }

    [Fact]
    public async Task Update_SinPermiso_IdDeOtroProducto_NoHeredaElDescuentoDeEsaLinea()
    {
        var (venta, idDesc, _) = await VentaConProductoRepetidoAsync();

        // La línea con descuento (id) pasa a otro producto: es una línea distinta, sin descuento.
        await _service.UpdateAsync(venta.Id, VmConLineas(venta, Linea(idDesc, _otroProducto.Id, 1)));

        var unica = (await VentaPersistidaAsync(venta.Id)).Detalles.Single(d => !d.IsDeleted);
        Assert.Equal(_otroProducto.Id, unica.ProductoId);
        Assert.Equal(0m, unica.Descuento);
    }

    [Fact]
    public async Task Update_SinPermiso_MismoIdRepetidoEnDosLineas_SoloLaPrimeraConservaElDescuento()
    {
        var (venta, idDesc, _) = await VentaConProductoRepetidoAsync();

        // Forjar el mismo Id en dos líneas para duplicar el descuento: la segunda es "nueva" (0 %).
        await _service.UpdateAsync(venta.Id, VmConLineas(venta, Linea(idDesc, _producto.Id, 1), Linea(idDesc, _producto.Id, 1)));

        var activas = (await VentaPersistidaAsync(venta.Id)).Detalles.Where(d => !d.IsDeleted).ToList();
        Assert.Equal(2, activas.Count);
        Assert.Equal(1, activas.Count(d => d.Descuento == 10m));
        Assert.Equal(1, activas.Count(d => d.Descuento == 0m));
    }

    [Fact]
    public async Task Update_SinPermiso_UnidadesExtraComoLineaNueva_SeAgreganSinDescuento()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();
        var idExistente = original.Detalles.Single(d => !d.IsDeleted).Id;

        await _service.UpdateAsync(original.Id, VmConLineas(
            original, Linea(idExistente, _producto.Id, 2), Linea(0, _producto.Id, 1)));

        var activas = (await VentaPersistidaAsync(original.Id)).Detalles.Where(d => !d.IsDeleted).OrderBy(d => d.Id).ToList();
        Assert.Equal(2, activas.Count);
        Assert.Equal(10m, activas.Single(d => d.Cantidad == 2).Descuento);
        Assert.Equal(0m, activas.Single(d => d.Cantidad == 1).Descuento);
    }

    [Fact]
    public async Task Update_SinPermiso_GeneralConservadoMayorAlNuevoSubtotal_DaUnMensajeAccionable()
    {
        ConPermisoDescuento();
        var creada = await _service.CreateAsync(Vm(1_900m));
        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE Ventas SET RowVersion = X'0102030405060708' WHERE Id = {0}", creada.Id);
        SinPermisoDescuento();
        var original = await VentaPersistidaAsync(creada.Id);

        // Baja a 1 unidad (subtotal 1.000 < 1.900 de descuento general conservado).
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(original.Id, VmEdicion(original, 0m, 0m, cantidad: 1)));

        Assert.IsNotType<DescuentoNoAutorizadoException>(ex);
        Assert.Contains("Pedí a un usuario con permiso de descuentos", ex.Message);
        Assert.Equal(1_900m, (await VentaPersistidaAsync(original.Id)).Descuento);
    }

    [Fact]
    public async Task Update_ConPermiso_100PorCientoDeLinea_EsAceptado()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();
        ConPermisoDescuento();

        await _service.UpdateAsync(original.Id, VmEdicion(original, 0m, 100m));

        Assert.Equal(0m, (await VentaPersistidaAsync(original.Id)).Total);
    }

    [Fact]
    public async Task Update_ConPermiso_GeneralIgualAlSubtotal_EsAceptado()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();
        ConPermisoDescuento();

        await _service.UpdateAsync(original.Id, VmEdicion(original, 2_000m, 0m));

        Assert.Equal(0m, (await VentaPersistidaAsync(original.Id)).Total);
    }

    [Fact]
    public async Task Update_ConPermiso_GeneralMayorAlSubtotal_SeRechazaConElMensajeDeRango()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();
        ConPermisoDescuento();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(original.Id, VmEdicion(original, 2_000.01m, 0m)));

        Assert.Contains("El descuento general no puede superar el subtotal", ex.Message);
    }



    [Fact]
    public async Task Update_SinPermiso_EnviaElMismoDescuentoQueTiene_EsAceptadoSinCambios()
    {
        // Permiso retirado a mitad de sesión con el formulario ya renderizado (hidden con los valores vigentes).
        var original = await VentaConDescuentosCreadaPorOtroAsync();

        await _service.UpdateAsync(original.Id, VmEdicion(original, 500m, 10m));

        var despues = await VentaPersistidaAsync(original.Id);
        Assert.Equal(500m, despues.Descuento);
        Assert.Equal(10m, despues.Detalles.Single(d => !d.IsDeleted).Descuento);
    }

    [Theory]
    [InlineData("500", "50")]   // sube la línea
    [InlineData("500", "5")]    // baja la línea (también es modificar)
    [InlineData("1500", "10")]  // sube el general
    [InlineData("100", "10")]   // baja el general
    [InlineData("0", "100")]    // intento de regalar la línea
    public async Task Update_SinPermiso_ValorManipulado_EsRechazadoYNoModificaLaVenta(string general, string linea)
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();

        await Assert.ThrowsAsync<DescuentoNoAutorizadoException>(() =>
            _service.UpdateAsync(original.Id, VmEdicion(
                original,
                decimal.Parse(general, System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(linea, System.Globalization.CultureInfo.InvariantCulture))));

        var despues = await VentaPersistidaAsync(original.Id);
        Assert.NotEqual("Editado en test", despues.Observaciones);
        Assert.Equal(500m, despues.Descuento);
        Assert.Equal(10m, despues.Detalles.Single(d => !d.IsDeleted).Descuento);
        Assert.Equal(1_300m, despues.Total);
        Assert.DoesNotContain(despues.Detalles, d => d.IsDeleted);
    }

    [Fact]
    public async Task Update_SinPermiso_LineaNuevaConDescuento_EsRechazada()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();
        var vm = VmEdicion(original, 0m, 0m);
        vm.Detalles.Add(new VentaDetalleViewModel
        {
            ProductoId = _otroProducto.Id, Cantidad = 1, PrecioUnitario = 0m, Descuento = 25m
        });

        await Assert.ThrowsAsync<DescuentoNoAutorizadoException>(() => _service.UpdateAsync(original.Id, vm));

        Assert.Single((await VentaPersistidaAsync(original.Id)).Detalles.Where(d => !d.IsDeleted));
    }

    [Fact]
    public async Task Update_SinPermiso_LineaNuevaSinDescuento_SeAgregaSinDescuento()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();
        var vm = VmEdicion(original, 0m, 0m);
        vm.Detalles.Add(new VentaDetalleViewModel { ProductoId = _otroProducto.Id, Cantidad = 1, PrecioUnitario = 0m });

        await _service.UpdateAsync(original.Id, vm);

        var activos = (await VentaPersistidaAsync(original.Id)).Detalles.Where(d => !d.IsDeleted).OrderBy(d => d.Id).ToList();
        Assert.Equal(2, activos.Count);
        Assert.Equal(10m, activos.Single(d => d.ProductoId == _producto.Id).Descuento);
        Assert.Equal(0m, activos.Single(d => d.ProductoId == _otroProducto.Id).Descuento);
    }

    [Fact]
    public async Task Update_SinPermiso_VentaSinDescuentos_NoPuedeAgregarUno()
    {
        var creada = await _service.CreateAsync(Vm());
        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE Ventas SET RowVersion = X'0102030405060708' WHERE Id = {0}", creada.Id);
        var original = await VentaPersistidaAsync(creada.Id);

        await Assert.ThrowsAsync<DescuentoNoAutorizadoException>(() =>
            _service.UpdateAsync(original.Id, VmEdicion(original, 0m, 5m)));

        Assert.Equal(2_000m, (await VentaPersistidaAsync(original.Id)).Total);
    }

    [Fact]
    public async Task Update_ConPermiso_PuedeCambiarYQuitarDescuentos()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();
        ConPermisoDescuento();

        await _service.UpdateAsync(original.Id, VmEdicion(original, 0m, 20m));

        var despues = await VentaPersistidaAsync(original.Id);
        Assert.Equal(0m, despues.Descuento);
        Assert.Equal(20m, despues.Detalles.Single(d => !d.IsDeleted).Descuento);
        Assert.Equal(1_600m, despues.Total);
    }

    // =====================================================================
    // Preview
    // =====================================================================

    private static DetalleCalculoVentaRequest Linea(int productoId, decimal descuento = 0m) => new()
    {
        ProductoId = productoId, Cantidad = Cantidad, PrecioUnitario = PrecioProducto, Descuento = descuento
    };

    [Fact]
    public async Task Preview_SinPermiso_ConDescuentoDeLinea_EsRechazado()
    {
        await Assert.ThrowsAsync<DescuentoNoAutorizadoException>(() =>
            _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
            {
                Detalles = [Linea(_producto.Id, 10m)]
            }));
    }

    [Fact]
    public async Task Preview_SinPermiso_ConDescuentoGeneral_EsRechazado()
    {
        await Assert.ThrowsAsync<DescuentoNoAutorizadoException>(() =>
            _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
            {
                Detalles = [Linea(_producto.Id)],
                DescuentoGeneral = 10m,
                DescuentoEsPorcentaje = true
            }));
    }

    [Fact]
    public async Task Preview_SinPermiso_VentaNueva_DevuelveTotalesSinDescuento()
    {
        var respuesta = await _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
        {
            Detalles = [Linea(_producto.Id)]
        });

        Assert.Equal(2_000m, respuesta.Total);
        Assert.Equal(0m, respuesta.DescuentoGeneralAplicado);
    }

    [Fact]
    public async Task Preview_SinPermiso_VentaExistente_UsaLosDescuentosPersistidosSinExponerElDesglose()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();

        var respuesta = await _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
        {
            VentaId = original.Id,
            Detalles = [Linea(_producto.Id)]
        });

        // Importes finales reales (coinciden con lo que guardará Edit)...
        Assert.Equal(1_300m, respuesta.Total);
        Assert.Equal(1_800m, respuesta.Detalles.Single().Subtotal);
        // ...sin el desglose específico del descuento general.
        Assert.Equal(0m, respuesta.DescuentoGeneralAplicado);
        Assert.All(respuesta.Detalles, d => Assert.Equal(0m, d.DescuentoGeneralProrrateado));
    }

    [Fact]
    public async Task Preview_ConPermiso_IgnoraVentaIdYDevuelveElDesgloseDelDescuentoGeneral()
    {
        ConPermisoDescuento();

        var respuesta = await _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
        {
            Detalles = [Linea(_producto.Id, 10m)],
            DescuentoGeneral = 500m,
            DescuentoEsPorcentaje = false
        });

        Assert.Equal(500m, respuesta.DescuentoGeneralAplicado);
        Assert.Equal(1_300m, respuesta.Total);
    }

    [Fact]
    public async Task Preview_PermisoRetiradoEnMitadDeSesion_DejaDeAceptarDescuentos()
    {
        ConPermisoDescuento();
        var request = new CalcularTotalesVentaRequest { Detalles = [Linea(_producto.Id, 10m)] };
        Assert.Equal(1_800m, (await _service.CalcularTotalesPreviewConServiciosAsync(request)).Total);

        SinPermisoDescuento();

        await Assert.ThrowsAsync<DescuentoNoAutorizadoException>(() => _service.CalcularTotalesPreviewConServiciosAsync(request));
    }

    [Fact]
    public async Task Preview_SinPermiso_ProductoRepetido_UsaLineaIdYNoElOrden()
    {
        var (venta, idDesc, idSin) = await VentaConProductoRepetidoAsync();

        // Líneas en orden inverso al persistido, identificadas por LineaId.
        var respuesta = await _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
        {
            VentaId = venta.Id,
            Detalles =
            [
                new DetalleCalculoVentaRequest { LineaId = idSin, ProductoId = _producto.Id, Cantidad = 3, PrecioUnitario = PrecioProducto },
                new DetalleCalculoVentaRequest { LineaId = idDesc, ProductoId = _producto.Id, Cantidad = 1, PrecioUnitario = PrecioProducto }
            ]
        });

        Assert.Equal(3_000m, respuesta.Detalles[0].Subtotal); // A×3 sin descuento
        Assert.Equal(900m, respuesta.Detalles[1].Subtotal);   // A×1 con 10 %
        Assert.Equal(3_900m, respuesta.Total);
    }

    [Fact]
    public async Task Preview_SinPermiso_AumentoDeCantidadEnLineaConDescuento_EsRechazado()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();
        var idLinea = original.Detalles.Single(d => !d.IsDeleted).Id;

        await Assert.ThrowsAsync<DescuentoNoAutorizadoException>(() =>
            _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
            {
                VentaId = original.Id,
                Detalles = [new DetalleCalculoVentaRequest { LineaId = idLinea, ProductoId = _producto.Id, Cantidad = 5, PrecioUnitario = PrecioProducto }]
            }));
    }

    [Fact]
    public async Task Preview_SinPermiso_ConVentaId_GeneralPorcentaje_EsRechazado()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();

        await Assert.ThrowsAsync<DescuentoNoAutorizadoException>(() =>
            _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
            {
                VentaId = original.Id,
                Detalles = [Linea(_producto.Id)],
                DescuentoGeneral = 10m,
                DescuentoEsPorcentaje = true
            }));
    }

    [Fact]
    public async Task Preview_SinPermisoDeDescuento_NiDeEdicion_IgnoraElVentaId()
    {
        var original = await VentaConDescuentosCreadaPorOtroAsync();
        _usuario.Retirar("ventas.update"); // usuario de solo lectura: el ventaId no le sirve para inferir descuentos

        var respuesta = await _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
        {
            VentaId = original.Id,
            Detalles = [Linea(_producto.Id)]
        });

        Assert.Equal(2_000m, respuesta.Total); // sin los descuentos persistidos
    }

    [Fact]
    public async Task Preview_SinPermiso_GeneralConservadoMayorAlSubtotal_InformaComoUpdate()
    {
        ConPermisoDescuento();
        var creada = await _service.CreateAsync(Vm(1_900m));
        SinPermisoDescuento();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
            {
                VentaId = creada.Id,
                Detalles = [new DetalleCalculoVentaRequest { ProductoId = _producto.Id, Cantidad = 1, PrecioUnitario = PrecioProducto }]
            }));

        Assert.IsNotType<DescuentoNoAutorizadoException>(ex);
        Assert.Contains("Pedí a un usuario con permiso de descuentos", ex.Message);
    }

    // =====================================================================
    // Regresión PROMPT 70 / FIN-S-002
    // =====================================================================

    [Fact]
    public async Task Regresion_Prompt70_ConPermiso_DescuentoNegativoSigueRechazado()
    {
        ConPermisoDescuento();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(Vm(-1m)));

        Assert.Contains("negativo", ex.Message);
        Assert.Equal(0, await ContarVentasAsync());
    }
}
