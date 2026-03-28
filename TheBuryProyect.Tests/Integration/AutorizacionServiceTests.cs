using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para AutorizacionService.
/// Cubren CRUD de UmbralAutorizacion (crear, actualizar, eliminar, rol inválido,
/// duplicado), validación de umbrales (DescuentoVenta, MontoVenta, rol admin siempre
/// permitido, sin umbral→denegado, valor excede límite) y ciclo de vida de
/// SolicitudAutorizacion (crear, aprobar, rechazar, cancelar, estado no pendiente).
/// </summary>
public class AutorizacionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly AutorizacionService _service;

    public AutorizacionServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new AutorizacionService(_context, NullLogger<AutorizacionService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<UmbralAutorizacion> SeedUmbralAsync(
        string rol = Roles.Vendedor,
        TipoUmbral tipo = TipoUmbral.DescuentoVenta,
        decimal valorMaximo = 10m,
        bool activo = true)
    {
        var umbral = new UmbralAutorizacion
        {
            Rol = rol,
            TipoUmbral = tipo,
            ValorMaximo = valorMaximo,
            Activo = activo
        };
        _context.UmbralesAutorizacion.Add(umbral);
        await _context.SaveChangesAsync();
        return umbral;
    }

    private async Task<SolicitudAutorizacion> SeedSolicitudAsync(
        string usuario = "vendedor1",
        string rol = Roles.Vendedor,
        EstadoSolicitud estado = EstadoSolicitud.Pendiente)
    {
        var solicitud = new SolicitudAutorizacion
        {
            UsuarioSolicitante = usuario,
            RolSolicitante = rol,
            TipoUmbral = TipoUmbral.DescuentoVenta,
            ValorSolicitado = 15m,
            ValorPermitido = 10m,
            TipoOperacion = "Venta",
            Justificacion = "Cliente especial",
            Estado = estado
        };
        _context.SolicitudesAutorizacion.Add(solicitud);
        await _context.SaveChangesAsync();
        return solicitud;
    }

    // -------------------------------------------------------------------------
    // UmbralAutorizacion — CrearUmbralAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUmbral_RolValido_Persiste()
    {
        var umbral = new UmbralAutorizacion
        {
            Rol = Roles.Vendedor,
            TipoUmbral = TipoUmbral.DescuentoVenta,
            ValorMaximo = 10m,
            Activo = true
        };

        var resultado = await _service.CrearUmbralAsync(umbral);

        Assert.True(resultado.Id > 0);
        var bd = await _context.UmbralesAutorizacion.FirstOrDefaultAsync(u => u.Id == resultado.Id);
        Assert.NotNull(bd);
        Assert.Equal(10m, bd!.ValorMaximo);
    }

    [Fact]
    public async Task CrearUmbral_RolInvalido_LanzaExcepcion()
    {
        var umbral = new UmbralAutorizacion
        {
            Rol = "RolInventado",
            TipoUmbral = TipoUmbral.DescuentoVenta,
            ValorMaximo = 10m
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearUmbralAsync(umbral));
    }

    [Fact]
    public async Task CrearUmbral_DuplicadoRolTipo_LanzaExcepcion()
    {
        await SeedUmbralAsync(Roles.Vendedor, TipoUmbral.DescuentoVenta, 10m);

        var duplicado = new UmbralAutorizacion
        {
            Rol = Roles.Vendedor,
            TipoUmbral = TipoUmbral.DescuentoVenta,
            ValorMaximo = 20m
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearUmbralAsync(duplicado));
    }

    // -------------------------------------------------------------------------
    // UmbralAutorizacion — ActualizarUmbralAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ActualizarUmbral_Existente_ActualizaValores()
    {
        var umbral = await SeedUmbralAsync(valorMaximo: 10m);

        var actualizado = new UmbralAutorizacion
        {
            Id = umbral.Id,
            ValorMaximo = 25m,
            Descripcion = "Nueva descripcion",
            Activo = false
        };

        var resultado = await _service.ActualizarUmbralAsync(actualizado);

        Assert.Equal(25m, resultado.ValorMaximo);
        Assert.False(resultado.Activo);
        Assert.Equal("Nueva descripcion", resultado.Descripcion);
    }

    [Fact]
    public async Task ActualizarUmbral_NoExiste_LanzaKeyNotFoundException()
    {
        var inexistente = new UmbralAutorizacion { Id = 99999, ValorMaximo = 5m };

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.ActualizarUmbralAsync(inexistente));
    }

    // -------------------------------------------------------------------------
    // UmbralAutorizacion — EliminarUmbralAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EliminarUmbral_Existente_SoftDelete()
    {
        var umbral = await SeedUmbralAsync();

        await _service.EliminarUmbralAsync(umbral.Id);

        var bd = await _context.UmbralesAutorizacion
            .IgnoreQueryFilters()
            .FirstAsync(u => u.Id == umbral.Id);
        Assert.True(bd.IsDeleted);
    }

    [Fact]
    public async Task EliminarUmbral_NoExiste_LanzaKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.EliminarUmbralAsync(99999));
    }

    // -------------------------------------------------------------------------
    // Validación de umbrales
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidarDescuentoVenta_RolAdmin_SiemprePermitido()
    {
        // Admin no necesita umbral configurado — siempre pasa
        var (permitido, valor, _) = await _service.ValidarDescuentoVentaAsync(Roles.Administrador, 99m);

        Assert.True(permitido);
        Assert.Equal(decimal.MaxValue, valor);
    }

    [Fact]
    public async Task ValidarDescuentoVenta_SinUmbralConfigurado_Denegado()
    {
        // Vendedor sin umbral → denegado por defecto
        var (permitido, valor, mensaje) = await _service.ValidarDescuentoVentaAsync(Roles.Vendedor, 5m);

        Assert.False(permitido);
        Assert.Equal(0, valor);
        Assert.Contains("No hay umbral", mensaje);
    }

    [Fact]
    public async Task ValidarDescuentoVenta_ValorDentroDelUmbral_Permitido()
    {
        await SeedUmbralAsync(Roles.Vendedor, TipoUmbral.DescuentoVenta, valorMaximo: 10m);

        var (permitido, valorMax, _) = await _service.ValidarDescuentoVentaAsync(Roles.Vendedor, 8m);

        Assert.True(permitido);
        Assert.Equal(10m, valorMax);
    }

    [Fact]
    public async Task ValidarDescuentoVenta_ValorExcedeUmbral_Denegado()
    {
        await SeedUmbralAsync(Roles.Vendedor, TipoUmbral.DescuentoVenta, valorMaximo: 10m);

        var (permitido, valorMax, mensaje) = await _service.ValidarDescuentoVentaAsync(Roles.Vendedor, 15m);

        Assert.False(permitido);
        Assert.Equal(10m, valorMax);
        Assert.Contains("excede el límite", mensaje);
    }

    [Fact]
    public async Task ValidarDescuentoVenta_UmbralInactivo_Denegado()
    {
        // Umbral inactivo no aplica
        await SeedUmbralAsync(Roles.Vendedor, TipoUmbral.DescuentoVenta, valorMaximo: 10m, activo: false);

        var (permitido, _, _) = await _service.ValidarDescuentoVentaAsync(Roles.Vendedor, 5m);

        Assert.False(permitido);
    }

    [Fact]
    public async Task ValidarMontoVenta_ValorExactoUmbral_Permitido()
    {
        await SeedUmbralAsync(Roles.Vendedor, TipoUmbral.MontoTotalVenta, valorMaximo: 1000m);

        // Igual al máximo debe pasar (<=)
        var (permitido, _, _) = await _service.ValidarMontoVentaAsync(Roles.Vendedor, 1000m);

        Assert.True(permitido);
    }

    [Fact]
    public async Task ValidarDescuentoVenta_RolInvalido_Denegado()
    {
        var (permitido, _, mensaje) = await _service.ValidarDescuentoVentaAsync("RolInventado", 5m);

        Assert.False(permitido);
        Assert.Contains("no es válido", mensaje);
    }

    // -------------------------------------------------------------------------
    // SolicitudAutorizacion — CrearSolicitudAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearSolicitud_DatosValidos_EstadoPendiente()
    {
        var solicitud = new SolicitudAutorizacion
        {
            UsuarioSolicitante = "vendedor1",
            RolSolicitante = Roles.Vendedor,
            TipoUmbral = TipoUmbral.DescuentoVenta,
            ValorSolicitado = 15m,
            ValorPermitido = 10m,
            TipoOperacion = "Venta",
            Justificacion = "Descuento especial"
        };

        var resultado = await _service.CrearSolicitudAsync(solicitud);

        Assert.True(resultado.Id > 0);
        Assert.Equal(EstadoSolicitud.Pendiente, resultado.Estado);
        Assert.Null(resultado.UsuarioAutorizador);
        Assert.Null(resultado.FechaResolucion);
    }

    // -------------------------------------------------------------------------
    // SolicitudAutorizacion — AprobarSolicitudAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AprobarSolicitud_Pendiente_MarcaAprobada()
    {
        var solicitud = await SeedSolicitudAsync();

        var resultado = await _service.AprobarSolicitudAsync(solicitud.Id, "gerente1", "Excepción aprobada");

        Assert.Equal(EstadoSolicitud.Aprobada, resultado.Estado);
        Assert.Equal("gerente1", resultado.UsuarioAutorizador);
        Assert.NotNull(resultado.FechaResolucion);
        Assert.Equal("Excepción aprobada", resultado.ComentarioResolucion);
    }

    [Fact]
    public async Task AprobarSolicitud_NoExiste_LanzaKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.AprobarSolicitudAsync(99999, "gerente1"));
    }

    [Fact]
    public async Task AprobarSolicitud_YaAprobada_LanzaExcepcion()
    {
        var solicitud = await SeedSolicitudAsync(estado: EstadoSolicitud.Aprobada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AprobarSolicitudAsync(solicitud.Id, "gerente1"));
    }

    // -------------------------------------------------------------------------
    // SolicitudAutorizacion — RechazarSolicitudAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RechazarSolicitud_Pendiente_MarcaRechazada()
    {
        var solicitud = await SeedSolicitudAsync();

        var resultado = await _service.RechazarSolicitudAsync(
            solicitud.Id, "gerente1", "Descuento excesivo");

        Assert.Equal(EstadoSolicitud.Rechazada, resultado.Estado);
        Assert.Equal("gerente1", resultado.UsuarioAutorizador);
        Assert.Equal("Descuento excesivo", resultado.ComentarioResolucion);
    }

    [Fact]
    public async Task RechazarSolicitud_YaRechazada_LanzaExcepcion()
    {
        var solicitud = await SeedSolicitudAsync(estado: EstadoSolicitud.Rechazada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RechazarSolicitudAsync(solicitud.Id, "gerente1", "Motivo"));
    }

    [Fact]
    public async Task RechazarSolicitud_NoExiste_LanzaKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.RechazarSolicitudAsync(99999, "gerente1", "Motivo"));
    }

    // -------------------------------------------------------------------------
    // SolicitudAutorizacion — CancelarSolicitudAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelarSolicitud_Pendiente_MarcaCancelada()
    {
        var solicitud = await SeedSolicitudAsync();

        var resultado = await _service.CancelarSolicitudAsync(solicitud.Id);

        Assert.Equal(EstadoSolicitud.Cancelada, resultado.Estado);
        Assert.NotNull(resultado.FechaResolucion);
    }

    [Fact]
    public async Task CancelarSolicitud_YaAprobada_LanzaExcepcion()
    {
        var solicitud = await SeedSolicitudAsync(estado: EstadoSolicitud.Aprobada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CancelarSolicitudAsync(solicitud.Id));
    }

    [Fact]
    public async Task CancelarSolicitud_NoExiste_LanzaKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.CancelarSolicitudAsync(99999));
    }

    // -------------------------------------------------------------------------
    // ObtenerSolicitudesPendientesAsync / ObtenerSolicitudesPorUsuarioAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerSolicitudesPendientes_FiltraSoloPendientes()
    {
        await SeedSolicitudAsync(estado: EstadoSolicitud.Pendiente);
        await SeedSolicitudAsync(estado: EstadoSolicitud.Aprobada);

        var pendientes = await _service.ObtenerSolicitudesPendientesAsync();

        Assert.Single(pendientes);
        Assert.All(pendientes, s => Assert.Equal(EstadoSolicitud.Pendiente, s.Estado));
    }

    [Fact]
    public async Task ObtenerSolicitudesPorUsuario_FiltraPorUsuario()
    {
        await SeedSolicitudAsync("usuario_a");
        await SeedSolicitudAsync("usuario_b");

        var resultado = await _service.ObtenerSolicitudesPorUsuarioAsync("usuario_a");

        Assert.Single(resultado);
        Assert.All(resultado, s => Assert.Equal("usuario_a", s.UsuarioSolicitante));
    }
}
