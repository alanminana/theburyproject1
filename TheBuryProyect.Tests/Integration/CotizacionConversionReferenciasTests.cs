using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.Services.Validators;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// D-02: conversión Cotización -> Venta a crédito personal con la regla REAL de referencias verificadas (aptitud +
/// validación + VentaService reales; solo el BCRA externo es un doble). La conversión no tiene una regla propia: reutiliza
/// la validación unificada, así que el faltante de referencias deja la venta pendiente de un segundo usuario y nunca levanta
/// mora ni ningún otro bloqueo, ni siquiera con la excepción documental solicitada.
/// </summary>
public sealed class CotizacionConversionReferenciasTests : IDisposable
{
    private static readonly TheBuryProject.ViewModels.TipoRazonAutorizacion[] Refs = { TheBuryProject.ViewModels.TipoRazonAutorizacion.ReferenciasInsuficientes };
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _ctx;
    private readonly HttpContextAccessor _accessor = new();
    private readonly UsuarioConversion _usuario = new();
    private readonly CotizacionConversionService _servicio;
    private readonly VentaService _ventaService;
    private readonly Producto _producto;
    private readonly Cliente _cliente;

    public CotizacionConversionReferenciasTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options, _accessor);
        _ctx.Database.EnsureCreated();

        var categoria = new Categoria { Codigo = "CAT-D02", Nombre = "Categoria D02" };
        var marca = new Marca { Codigo = "MAR-D02", Nombre = "Marca D02" };
        _ctx.Categorias.Add(categoria);
        _ctx.Marcas.Add(marca);
        _ctx.SaveChanges();
        _producto = new Producto
        {
            Codigo = "P-D02", Nombre = "Producto D02", CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 50m, PrecioVenta = 100m, StockActual = 10m, StockMinimo = 1m, Activo = true
        };
        _cliente = new Cliente
        {
            TipoDocumento = "DNI", NumeroDocumento = "45678901", Apellido = "Conversion", Nombre = "Referencias",
            Telefono = "3415551000", Domicilio = "Calle 2", NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            PuntajeCliente = 1, Activo = true, CuilCuit = "20123456786", SituacionCrediticiaBcra = 1,
            SituacionCrediticiaConsultaOk = true, SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow.AddDays(-1),
            SituacionCrediticiaBcraUltimoExito = 1, SituacionCrediticiaUltimoExitoUtc = DateTime.UtcNow.AddDays(-1)
        };
        _ctx.Productos.Add(_producto);
        _ctx.Clientes.Add(_cliente);
        _ctx.SaveChanges();

        var creditoDisponible = new CreditoDisponibleService(_ctx, NullLogger<CreditoDisponibleService>.Instance);
        var garante = new GaranteService(_ctx, NullLogger<GaranteService>.Instance);
        var aptitud = new ClienteAptitudService(_ctx, NullLogger<ClienteAptitudService>.Instance, creditoDisponible, garante);
        var validacion = new ValidacionVentaService(
            _ctx, aptitud, new FakeSituacionCrediticiaBcraService(), NullLogger<ValidacionVentaService>.Instance);

        var numeros = new VentaNumberGenerator(_ctx, NullLogger<VentaNumberGenerator>.Instance);
        _ventaService = new VentaService(
            _ctx, null!, NullLogger<VentaService>.Instance, null!, null!, null!, new VentaValidator(), numeros, null!,
            _usuario, validacion, null!, null!, null!, null!);

        _servicio = new CotizacionConversionService(
            _ctx, numeros, new PrecioVigenteResolver(_ctx), validacion, _ventaService, _usuario,
            null!, null!, null!, null!, NullLogger<CotizacionConversionService>.Instance);

        ComoUsuario("carlos.creador", autoriza: true);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _conn.Dispose();
    }

    private void ComoUsuario(string nombre, bool autoriza)
    {
        _usuario.Nombre = nombre;
        _usuario.Autoriza = autoriza;
        _accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, nombre) }, "test"))
        };
    }

    private async Task ConfigurarAsync(int minimo = 1, bool mora = false)
    {
        _ctx.Set<ConfiguracionCredito>().Add(new ConfiguracionCredito
        {
            ReferenciasVerificadasMinimas = minimo, ValidarDocumentacion = false, ValidarLimiteCredito = false,
            ValidarMora = mora, DiasParaNoApto = mora ? 1 : null
        });
        (await _ctx.PuntajesCreditoLimite.FindAsync(1))!.LimiteMonto = 1_000_000m;
        await _ctx.SaveChangesAsync();
    }

    private async Task<Cotizacion> CotizacionAsync()
    {
        var cotizacion = new Cotizacion
        {
            Numero = $"COT-D02-{Guid.NewGuid():N}"[..20],
            Fecha = DateTime.UtcNow,
            Estado = EstadoCotizacion.Emitida,
            ClienteId = _cliente.Id,
            Subtotal = 200m,
            TotalBase = 200m,
            MedioPagoSeleccionado = CotizacionMedioPagoTipo.CreditoPersonal,
            Detalles =
            {
                new CotizacionDetalle
                {
                    ProductoId = _producto.Id, CodigoProductoSnapshot = _producto.Codigo, NombreProductoSnapshot = _producto.Nombre,
                    Cantidad = 2, PrecioUnitarioSnapshot = 100m, Subtotal = 200m
                }
            }
        };
        _ctx.Cotizaciones.Add(cotizacion);
        await _ctx.SaveChangesAsync();
        return cotizacion;
    }

    private async Task VerificarReferenciaAsync()
    {
        _ctx.ClienteReferencias.Add(new ClienteReferencia
        {
            ClienteId = _cliente.Id, Tipo = TipoReferenciaCliente.Personal, Nombre = "Ref", Telefono = "3414440001",
            TelefonoNormalizado = "3414440001", EstadoVerificacion = EstadoVerificacionReferencia.Verificada
        });
        await _ctx.SaveChangesAsync();
    }

    private static CotizacionConversionRequest Request(bool excepcion = false) => new()
    {
        UsarPrecioCotizado = true,
        ConfirmarAdvertencias = false,
        AplicarExcepcionDocumental = excepcion,
        MotivoExcepcionDocumental = excepcion ? "Excepción solicitada al convertir" : null
    };

    [Fact]
    public async Task Convertir_SinReferenciasVerificadas_CreaLaVentaPendienteDeAutorizacionSinCredito()
    {
        await ConfigurarAsync();
        var cotizacion = await CotizacionAsync();

        var resultado = await _servicio.ConvertirAVentaAsync(cotizacion.Id, Request(), "carlos.creador");

        Assert.True(resultado.Exitoso, string.Join(" | ", resultado.Errores));
        var venta = await _ctx.Ventas.AsNoTracking().SingleAsync(v => v.Id == resultado.VentaId);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.True(venta.RequiereAutorizacion);
        Assert.Null(venta.CreditoId);
        Assert.True(VentaAutorizacionReferencias.ContieneReferencias(venta.RazonesAutorizacionJson));
        Assert.Equal("carlos.creador", venta.CreatedBy);
    }

    [Fact]
    public async Task Convertir_ConReferenciaVerificada_NoPideAutorizacionYCreaElCredito()
    {
        await ConfigurarAsync();
        await VerificarReferenciaAsync();
        var cotizacion = await CotizacionAsync();

        var resultado = await _servicio.ConvertirAVentaAsync(cotizacion.Id, Request(), "carlos.creador");

        Assert.True(resultado.Exitoso, string.Join(" | ", resultado.Errores));
        var venta = await _ctx.Ventas.AsNoTracking().SingleAsync(v => v.Id == resultado.VentaId);
        Assert.Equal(EstadoAutorizacionVenta.NoRequiere, venta.EstadoAutorizacion);
        Assert.NotNull(venta.CreditoId);
    }

    [Fact]
    public async Task Convertir_ConMinimoCero_NoPideAutorizacion()
    {
        await ConfigurarAsync(minimo: 0);
        var cotizacion = await CotizacionAsync();

        var resultado = await _servicio.ConvertirAVentaAsync(cotizacion.Id, Request(), "carlos.creador");

        Assert.True(resultado.Exitoso);
        Assert.Equal(EstadoAutorizacionVenta.NoRequiere,
            (await _ctx.Ventas.AsNoTracking().SingleAsync(v => v.Id == resultado.VentaId)).EstadoAutorizacion);
    }

    [Fact]
    public async Task Convertir_ConMoraBloqueante_YSinReferencias_LaExcepcionSolicitadaNoLevantaLaMora()
    {
        await ConfigurarAsync(mora: true);
        var credito = new Credito { ClienteId = _cliente.Id, Numero = "MORA-CONV-D02", Estado = EstadoCredito.Activo, SaldoPendiente = 500m };
        _ctx.Creditos.Add(credito);
        await _ctx.SaveChangesAsync();
        _ctx.Cuotas.Add(new Cuota
        {
            CreditoId = credito.Id, NumeroCuota = 1, FechaVencimiento = DateTime.UtcNow.Date.AddDays(-90),
            MontoCapital = 500m, MontoTotal = 500m, MontoPagado = 0m, Estado = EstadoCuota.Vencida
        });
        await _ctx.SaveChangesAsync();
        var cotizacion = await CotizacionAsync();
        var ventasAntes = await _ctx.Ventas.CountAsync();

        var resultado = await _servicio.ConvertirAVentaAsync(cotizacion.Id, Request(excepcion: true), "carlos.creador");

        Assert.False(resultado.Exitoso);
        Assert.Equal(ventasAntes, await _ctx.Ventas.CountAsync());
        Assert.Equal(EstadoCotizacion.Emitida, (await _ctx.Cotizaciones.AsNoTracking().SingleAsync(c => c.Id == cotizacion.Id)).Estado);
    }

    [Fact]
    public async Task VentaConvertida_ElCreadorNoPuedeAutorizarla_UnSegundoUsuarioSi()
    {
        await ConfigurarAsync();
        var cotizacion = await CotizacionAsync();
        var resultado = await _servicio.ConvertirAVentaAsync(cotizacion.Id, Request(), "carlos.creador");
        Assert.True(resultado.Exitoso);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _ventaService.AutorizarVentaAsync(resultado.VentaId!.Value, "carlos.creador", "Me autorizo", Refs));
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion,
            (await _ctx.Ventas.AsNoTracking().SingleAsync(v => v.Id == resultado.VentaId)).EstadoAutorizacion);

        ComoUsuario("marta.gerente", autoriza: true);
        Assert.True(await _ventaService.AutorizarVentaAsync(resultado.VentaId!.Value, "marta.gerente", "Excepción de referencias", Refs));
        var venta = await _ctx.Ventas.AsNoTracking().SingleAsync(v => v.Id == resultado.VentaId);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, venta.EstadoAutorizacion);
        Assert.Equal("marta.gerente", venta.UsuarioAutoriza);
    }

    private sealed class UsuarioConversion : ICurrentUserService
    {
        public string Nombre { get; set; } = "x";
        public bool Autoriza { get; set; }
        public string GetUsername() => Nombre;
        public string GetUserId() => "system";
        public bool IsAuthenticated() => true;
        public string? GetEmail() => null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string modulo, string accion) =>
            modulo == "ventas" && accion == "authorize" && Autoriza;
        public string? GetIpAddress() => null;
    }
}
