using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.Services.Validators;
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// D-02 (decisión del propietario): con menos referencias VERIFICADAS que el mínimo configurado, la venta a crédito personal
/// queda pendiente de la autorización de un SEGUNDO usuario con ventas.authorize (nunca el creador, ni teniendo el permiso),
/// vale solo para esa venta y nunca levanta mora, BCRA ni ningún otro bloqueo. Todo por los servicios reales
/// (ClienteAptitudService + ValidacionVentaService + VentaService); solo se simulan la caja, el BCRA externo y el usuario.
/// </summary>
public class VentaReferenciasCreditoServiceTests : IDisposable
{
    private static readonly TipoRazonAutorizacion[] Refs = { TipoRazonAutorizacion.ReferenciasInsuficientes };
    private const string Creador = "vendedor.uno";
    private const string Autorizador = "gerente.dos";

    private readonly SqliteConnection _conn;
    private readonly AppDbContext _ctx;
    private readonly UsuarioMutable _usuario = new();
    private readonly HttpContextAccessor _accessor = new();
    private readonly AuditoriaGrabadora _auditoria = new();
    private readonly VentaService _svc;
    private readonly ValidacionVentaService _validacionSvc;
    private Cliente _cliente = null!;
    private Producto _producto = null!;

    public VentaReferenciasCreditoServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _ctx = new AppDbContext(options, _accessor);
        _ctx.Database.EnsureCreated();

        var creditoDisponible = new CreditoDisponibleService(_ctx, NullLogger<CreditoDisponibleService>.Instance);
        var garante = new GaranteService(_ctx, NullLogger<GaranteService>.Instance);
        var aptitud = new ClienteAptitudService(_ctx, NullLogger<ClienteAptitudService>.Instance, creditoDisponible, garante);
        var validacion = new ValidacionVentaService(
            _ctx, aptitud, new FakeSituacionCrediticiaBcraService(), NullLogger<ValidacionVentaService>.Instance);

        _validacionSvc = validacion;
        var apertura = SeedCaja();
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

        _svc = new VentaService(
            _ctx, mapper, NullLogger<VentaService>.Instance,
            new StubAlertaStockD02(), new StubMovimientoStockD02(), new FinancialCalculationService(), new VentaValidator(),
            new VentaNumberGenerator(_ctx, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_ctx), _usuario, validacion,
            new StubCajaAperturaActivaFija(apertura), null!,
            new StubContratoVentaCreditoService(), new StubConfiguracionPagoServiceVenta(),
            auditoria: _auditoria);

        ComoUsuario(Creador, autoriza: true);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _conn.Dispose();
    }

    // ---------------------------------------------------------------- escenario

    private AperturaCaja SeedCaja()
    {
        var caja = new Caja { Codigo = "C01", Nombre = "Caja D02", RowVersion = new byte[8] };
        _ctx.Cajas.Add(caja);
        _ctx.SaveChanges();
        var apertura = new AperturaCaja { CajaId = caja.Id, UsuarioApertura = "x", MontoInicial = 0m, Cerrada = false, RowVersion = new byte[8] };
        _ctx.AperturasCaja.Add(apertura);
        _ctx.SaveChanges();
        return apertura;
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

    /// <summary>Config + cliente apto salvo por lo que cada prueba pida (BCRA ok, sin docs ni cupo exigidos).</summary>
    private async Task SembrarAsync(int minimo = 1, bool mora = false, int bcra = 1, bool cupo = false, decimal limite = 1_000_000m, int? diasNoApto = null, bool docs = false)
    {
        _ctx.Set<ConfiguracionCredito>().Add(new ConfiguracionCredito
        {
            ReferenciasVerificadasMinimas = minimo,
            ValidarDocumentacion = docs,
            ValidarLimiteCredito = cupo,
            ValidarMora = mora,
            DiasParaNoApto = mora ? (diasNoApto ?? 1) : null
        });
        (await _ctx.PuntajesCreditoLimite.FindAsync(1))!.LimiteMonto = limite;

        _cliente = new Cliente
        {
            Nombre = "Ana", Apellido = "Referencias", TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8], Telefono = "3415551000",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal, PuntajeCliente = 1, Activo = true,
            CuilCuit = "20123456786", SituacionCrediticiaBcra = bcra, SituacionCrediticiaConsultaOk = true,
            SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow.AddDays(-1),
            SituacionCrediticiaBcraUltimoExito = bcra, SituacionCrediticiaUltimoExitoUtc = DateTime.UtcNow.AddDays(-1)
        };
        _ctx.Clientes.Add(_cliente);

        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var categoria = new Categoria { Codigo = $"C{sufijo}", Nombre = $"Cat {sufijo}", Activo = true };
        var marca = new Marca { Codigo = $"M{sufijo}", Nombre = $"Mar {sufijo}", Activo = true };
        _ctx.Categorias.Add(categoria);
        _ctx.Marcas.Add(marca);
        await _ctx.SaveChangesAsync();
        _producto = new Producto
        {
            Codigo = $"P{sufijo}", Nombre = $"Prod {sufijo}", CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 500m, PrecioVenta = 1_000m, StockActual = 10m, PorcentajeIVA = 21m, Activo = true
        };
        _ctx.Productos.Add(_producto);
        await _ctx.SaveChangesAsync();
    }

    private async Task AgregarReferenciaAsync(EstadoVerificacionReferencia estado, int tel = 1, bool baja = false)
    {
        var telefono = $"34144400{tel:00}";
        _ctx.ClienteReferencias.Add(new ClienteReferencia
        {
            ClienteId = _cliente.Id, Tipo = TipoReferenciaCliente.Personal, Nombre = $"Ref {tel}",
            Telefono = telefono, TelefonoNormalizado = telefono, EstadoVerificacion = estado, IsDeleted = baja
        });
        await _ctx.SaveChangesAsync();
    }

    private async Task SembrarMoraBloqueanteAsync()
    {
        var credito = new Credito
        {
            ClienteId = _cliente.Id, Numero = "MORA-D02", Estado = EstadoCredito.Activo, SaldoPendiente = 500m
        };
        _ctx.Creditos.Add(credito);
        await _ctx.SaveChangesAsync();
        _ctx.Cuotas.Add(new Cuota
        {
            CreditoId = credito.Id, NumeroCuota = 1, FechaVencimiento = DateTime.UtcNow.Date.AddDays(-90),
            MontoCapital = 500m, MontoTotal = 500m, MontoPagado = 0m, Estado = EstadoCuota.Vencida
        });
        await _ctx.SaveChangesAsync();
    }

    private VentaViewModel NuevaVenta(bool excepcion = false, string? motivo = null) => new()
    {
        ClienteId = _cliente.Id,
        FechaVenta = DateTime.UtcNow,
        Estado = EstadoVenta.Cotizacion,
        TipoPago = TipoPago.CreditoPersonal,
        AplicarExcepcionDocumental = excepcion,
        MotivoExcepcionDocumentalCreate = motivo,
        Detalles = new List<VentaDetalleViewModel>
        {
            new() { ProductoId = _producto.Id, Cantidad = 1, PrecioUnitario = 1_000m, Subtotal = 1_000m }
        }
    };

    // SQLite no genera RowVersion: se fija un valor conocido en la fila para que el chequeo de concurrencia de UpdateAsync tenga contra qué comparar.
    private async Task<byte[]> FijarRowVersionAsync(int id)
    {
        var valor = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await _ctx.Database.ExecuteSqlRawAsync("UPDATE Ventas SET RowVersion = X'0102030405060708' WHERE Id = {0}", id);
        _ctx.ChangeTracker.Clear();
        return valor;
    }

    private async Task<Venta> VentaAsync(int id) =>
        await _ctx.Ventas.AsNoTracking().FirstAsync(v => v.Id == id);

    // ------------------------------------------------------------ camino: Create

    [Fact]
    public async Task Create_SinReferenciasVerificadas_QuedaPendienteDeAutorizacionConLaRazonDeReferencias()
    {
        await SembrarAsync();

        var creada = await _svc.CreateAsync(NuevaVenta());

        var venta = await VentaAsync(creada.Id);
        Assert.Equal(EstadoVenta.PendienteFinanciacion, venta.Estado);
        Assert.True(venta.RequiereAutorizacion);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.True(VentaAutorizacionReferencias.ContieneReferencias(venta.RazonesAutorizacionJson));
        Assert.Contains("0 de 1", venta.RazonesAutorizacionJson);
        Assert.Null(venta.UsuarioAutoriza);
    }

    [Fact]
    public async Task Create_ConReferenciaVerificada_NoRequiereAutorizacion()
    {
        await SembrarAsync();
        await AgregarReferenciaAsync(EstadoVerificacionReferencia.Verificada);

        var creada = await _svc.CreateAsync(NuevaVenta());

        var venta = await VentaAsync(creada.Id);
        Assert.False(venta.RequiereAutorizacion);
        Assert.Equal(EstadoAutorizacionVenta.NoRequiere, venta.EstadoAutorizacion);
        Assert.False(VentaAutorizacionReferencias.ContieneReferencias(venta.RazonesAutorizacionJson));
    }

    [Fact]
    public async Task Create_ConMinimoCero_NoRequiereAutorizacionAunSinReferencias()
    {
        await SembrarAsync(minimo: 0);

        var creada = await _svc.CreateAsync(NuevaVenta());

        Assert.False((await VentaAsync(creada.Id)).RequiereAutorizacion);
    }

    [Theory]
    [InlineData(EstadoVerificacionReferencia.Pendiente)]
    [InlineData(EstadoVerificacionReferencia.NoLocalizada)]
    [InlineData(EstadoVerificacionReferencia.Rechazada)]
    public async Task Create_ConReferenciasNoVerificadas_RequiereAutorizacion(EstadoVerificacionReferencia estado)
    {
        await SembrarAsync();
        await AgregarReferenciaAsync(estado);

        var creada = await _svc.CreateAsync(NuevaVenta());

        Assert.True((await VentaAsync(creada.Id)).RequiereAutorizacion);
    }

    [Fact]
    public async Task Create_LaVentaQueNoEsCreditoPersonal_NoCambia()
    {
        await SembrarAsync();
        var vm = NuevaVenta();
        vm.TipoPago = TipoPago.Efectivo;

        var creada = await _svc.CreateAsync(vm);

        var venta = await VentaAsync(creada.Id);
        Assert.False(venta.RequiereAutorizacion);
        Assert.Equal(EstadoAutorizacionVenta.NoRequiere, venta.EstadoAutorizacion);
    }

    // --------------------------------------------- autorización: segundo usuario

    [Fact]
    public async Task Autorizar_ElCreadorConPermiso_NoPuedeAutorizarseASiMismo()
    {
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.AutorizarVentaAsync(creada.Id, Creador, "Lo autorizo yo", Refs));

        Assert.Contains("segundo usuario", ex.Message);
        var venta = await VentaAsync(creada.Id);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.Null(venta.UsuarioAutoriza);
        Assert.Empty(_auditoria.Eventos);
    }

    [Fact]
    public async Task Autorizar_UnSegundoUsuarioSinPermiso_NoPuede()
    {
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        ComoUsuario(Autorizador, autoriza: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Motivo", Refs));

        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, (await VentaAsync(creada.Id)).EstadoAutorizacion);
    }

    [Fact]
    public async Task Autorizar_SinMotivo_SeRechaza()
    {
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        ComoUsuario(Autorizador, autoriza: true);

        await Assert.ThrowsAsync<ArgumentException>(() => _svc.AutorizarVentaAsync(creada.Id, Autorizador, "   ", Refs));

        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, (await VentaAsync(creada.Id)).EstadoAutorizacion);
    }

    [Fact]
    public async Task Autorizar_UnSegundoUsuarioConPermiso_AutorizaConTrazaCompletaYSoloUnaVez()
    {
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        ComoUsuario(Autorizador, autoriza: true);

        var primera = await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Cliente conocido, referencias en trámite", Refs);
        // doble uso / reintento: ya no está pendiente, se rechaza y no se registra una segunda autorización
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Reintento", Refs));

        Assert.True(primera);
        var venta = await VentaAsync(creada.Id);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, venta.EstadoAutorizacion);
        Assert.Equal(Autorizador, venta.UsuarioAutoriza);
        Assert.NotNull(venta.FechaAutorizacion);
        Assert.Equal("Cliente conocido, referencias en trámite", venta.MotivoAutorizacion);
        Assert.Equal(Creador, venta.CreatedBy);
        var evento = Assert.Single(_auditoria.Eventos);
        Assert.Equal(("Ventas", "AutorizarExcepcionReferencias", $"Venta:{creada.Id}"), (evento.Modulo, evento.Accion, evento.Entidad));
        Assert.Contains(Autorizador, evento.Detalle);
        Assert.Contains(Creador, evento.Detalle);
        Assert.Contains("Cliente conocido", evento.Detalle);
    }

    [Fact]
    public async Task Autorizar_LaExcepcionNoEsGlobal_UnaSegundaVentaDelMismoClienteVuelveAPedirla()
    {
        await SembrarAsync();
        var primera = await _svc.CreateAsync(NuevaVenta());
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(primera.Id, Autorizador, "Excepción para esta venta", Refs);

        ComoUsuario(Creador, autoriza: true);
        var segunda = await _svc.CreateAsync(NuevaVenta());

        Assert.Equal(EstadoAutorizacionVenta.Autorizada, (await VentaAsync(primera.Id)).EstadoAutorizacion);
        var venta2 = await VentaAsync(segunda.Id);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta2.EstadoAutorizacion);
        Assert.Null(venta2.UsuarioAutoriza);
    }

    [Fact]
    public async Task Autorizar_QuienModificoLaVenta_NoPuedeSerElAutorizador()
    {
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        ComoUsuario(Autorizador, autoriza: true);
        var entidad = await _ctx.Ventas.FirstAsync(v => v.Id == creada.Id);
        entidad.UsuarioSolicita = null; // venta anterior al registro del editor de condiciones
        entidad.Observaciones = "Editada por el futuro autorizador"; // UpdatedBy = autorizador
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Motivo", Refs));
    }

    [Fact]
    public async Task Autorizar_UnaVentaAjenaOInexistente_NoHaceNada()
    {
        await SembrarAsync();
        ComoUsuario(Autorizador, autoriza: true);

        Assert.False(await _svc.AutorizarVentaAsync(987_654, Autorizador, "Motivo", Refs));
        Assert.Empty(_auditoria.Eventos);
    }

    // ------------------------------------- no evade ningún otro bloqueo de crédito

    [Fact]
    public async Task ConMoraBloqueante_LaExcepcionNoLevantaNada_AunConExcepcionDocumentalSolicitada()
    {
        await SembrarAsync(mora: true);
        await SembrarMoraBloqueanteAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.CreateAsync(NuevaVenta(excepcion: true, motivo: "Quiero igual")));

        Assert.Contains("No es posible crear la venta con crédito personal", ex.Message);
        Assert.Empty(await _ctx.Ventas.ToListAsync());
    }

    [Fact]
    public async Task ConMoraBloqueante_YReferenciasInsuficientes_SinExcepcion_TampocoSeCrea()
    {
        await SembrarAsync(mora: true);
        await SembrarMoraBloqueanteAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CreateAsync(NuevaVenta()));
        Assert.Empty(await _ctx.Ventas.ToListAsync());
    }

    [Fact]
    public async Task ConBcraDesfavorable_LaExcepcionNoLevantaElBloqueo()
    {
        await SembrarAsync(bcra: 4);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.CreateAsync(NuevaVenta(excepcion: true, motivo: "Quiero igual")));
        Assert.Empty(await _ctx.Ventas.ToListAsync());
    }

    [Fact]
    public async Task ConCupoExcedido_YSinReferencias_LaExcepcionDeCupoNoAutorizaLasReferencias()
    {
        // La excepción de cupo/documentación ya existente la aplica el propio creador con ventas.authorize; las referencias
        // conservan su autorización aparte: la venta NO queda Autorizada por esa vía.
        await SembrarAsync(cupo: true, limite: 500m);

        var creada = await _svc.CreateAsync(NuevaVenta(excepcion: true, motivo: "Cupo excedido, lo autorizo"));

        var venta = await VentaAsync(creada.Id);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.True(venta.RequiereAutorizacion);
        Assert.Null(venta.UsuarioAutoriza);
        Assert.True(VentaAutorizacionReferencias.ContieneReferencias(venta.RazonesAutorizacionJson));
        Assert.Contains("Cupo excedido, lo autorizo", venta.RazonesAutorizacionJson); // la excepción de cupo queda registrada en las razones
    }

    [Fact]
    public async Task ConCupoExcedido_YReferenciasCompletas_LaExcepcionDeCupoSigueFuncionandoComoAntes()
    {
        await SembrarAsync(cupo: true, limite: 500m);
        await AgregarReferenciaAsync(EstadoVerificacionReferencia.Verificada);

        var creada = await _svc.CreateAsync(NuevaVenta(excepcion: true, motivo: "Cupo excedido, lo autorizo"));

        var venta = await VentaAsync(creada.Id);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, venta.EstadoAutorizacion);
        Assert.False(VentaAutorizacionReferencias.ContieneReferencias(venta.RazonesAutorizacionJson));
    }

    // ------------------------------------------------ confirmación (gate propio)

    [Fact]
    public async Task Confirmar_ConReferenciasInsuficientesYSinAutorizacionQueLasCubra_SeRechaza()
    {
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        var venta = await VentaAsync(creada.Id);
        var validacion = new ValidacionVentaResult();
        validacion.RazonesAutorizacion.Add(new RazonAutorizacion
        {
            Tipo = TipoRazonAutorizacion.ReferenciasInsuficientes, Descripcion = "Referencias verificadas insuficientes: 0 de 1 requeridas"
        });

        // Pendiente
        Assert.Throws<InvalidOperationException>(() => VentaService.ExigirAutorizacionDeReferencias(venta, validacion));

        // Autorizada, pero por otra razón (sin la de referencias en lo autorizado)
        venta.EstadoAutorizacion = EstadoAutorizacionVenta.Autorizada;
        venta.RazonesAutorizacionJson = "[{\"Tipo\":2,\"Descripcion\":\"Mora\"}]";
        Assert.Throws<InvalidOperationException>(() => VentaService.ExigirAutorizacionDeReferencias(venta, validacion));

        // Autorizada y cubriendo las referencias: pasa.
        venta.RazonesAutorizacionJson = "[{\"Tipo\":7,\"Descripcion\":\"Referencias\",\"Autorizada\":true}]";
        VentaService.ExigirAutorizacionDeReferencias(venta, validacion);

        // Referencias en lo autorizado pero SIN la confirmación expresa por razón: no cubre.
        venta.RazonesAutorizacionJson = "[{\"Tipo\":7,\"Descripcion\":\"Referencias\"}]";
        Assert.Throws<InvalidOperationException>(() => VentaService.ExigirAutorizacionDeReferencias(venta, validacion));
        venta.RazonesAutorizacionJson = "[{\"Tipo\":7,\"Descripcion\":\"Referencias\",\"Autorizada\":true}]";

        // Si cubre pero fue Rechazada: no sirve.
        venta.EstadoAutorizacion = EstadoAutorizacionVenta.Rechazada;
        Assert.Throws<InvalidOperationException>(() => VentaService.ExigirAutorizacionDeReferencias(venta, validacion));
    }

    [Fact]
    public async Task Confirmar_SinFaltaDeReferencias_NoExigeNada()
    {
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        var venta = await VentaAsync(creada.Id);

        VentaService.ExigirAutorizacionDeReferencias(venta, new ValidacionVentaResult());
    }

    [Fact]
    public async Task AsegurarReferenciasAutorizadas_BloqueaHastaQueUnSegundoUsuarioLaAutorice()
    {
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AsegurarReferenciasAutorizadasAsync(creada.Id));
        Assert.Contains("Referencias verificadas insuficientes: 0 de 1", ex.Message);
        Assert.Contains("segundo usuario", ex.Message);

        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Excepción para esta venta", Refs);

        await _svc.AsegurarReferenciasAutorizadasAsync(creada.Id); // ahora cubre
        await _svc.AsegurarReferenciasAutorizadasAsync(987_654);   // inexistente: no hace nada
    }

    [Fact]
    public async Task AsegurarReferenciasAutorizadas_NoAplicaAVentasQueNoSonCreditoPersonal()
    {
        await SembrarAsync();
        var vm = NuevaVenta();
        vm.TipoPago = TipoPago.Efectivo;
        var creada = await _svc.CreateAsync(vm);

        await _svc.AsegurarReferenciasAutorizadasAsync(creada.Id);
    }

    [Fact]
    public async Task AsegurarReferenciasAutorizadas_UnaAutorizacionPorOtroMotivoNoAlcanza()
    {
        await SembrarAsync();
        await AgregarReferenciaAsync(EstadoVerificacionReferencia.Verificada);
        var creada = await _svc.CreateAsync(NuevaVenta());
        // Con la referencia vigente alcanza; si se da de baja después y la venta solo tenía una autorización por otro motivo, no.
        var entidad = await _ctx.Ventas.FirstAsync(v => v.Id == creada.Id);
        entidad.RequiereAutorizacion = true;
        entidad.EstadoAutorizacion = EstadoAutorizacionVenta.Autorizada;
        entidad.RazonesAutorizacionJson = "[{\"Tipo\":2,\"Descripcion\":\"Mora\"}]";
        (await _ctx.ClienteReferencias.FirstAsync()).IsDeleted = true;
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AsegurarReferenciasAutorizadasAsync(creada.Id));
    }

    // ----------------------------------------------------- Edit: invalidación

    [Fact]
    public async Task Editar_ConAutorizacionPorReferencias_ElCambioDeMontoLaInvalidaYVuelveAPedirla()
    {
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Excepción para esta venta", Refs);
        _auditoria.Eventos.Clear();

        ComoUsuario(Creador, autoriza: true);
        var vm = await _svc.GetByIdAsync(creada.Id);
        Assert.NotNull(vm);
        vm!.RowVersion = await FijarRowVersionAsync(creada.Id);
        vm.Detalles[0].Cantidad = 2;
        vm.Detalles[0].Subtotal = 2_000m;
        await _svc.UpdateAsync(creada.Id, vm);

        var venta = await VentaAsync(creada.Id);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.Null(venta.UsuarioAutoriza);
        Assert.Null(venta.MotivoAutorizacion);
        Assert.True(VentaAutorizacionReferencias.ContieneReferencias(venta.RazonesAutorizacionJson));
        Assert.Contains(_auditoria.Eventos, e => e.Accion == "InvalidarExcepcionReferencias");
    }

    [Fact]
    public async Task Editar_ConAutorizacionPorReferencias_SinCambiarElMonto_LaConserva()
    {
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Excepción para esta venta", Refs);
        _auditoria.Eventos.Clear();

        ComoUsuario(Creador, autoriza: true);
        var vm = await _svc.GetByIdAsync(creada.Id);
        vm!.RowVersion = await FijarRowVersionAsync(creada.Id);
        vm.Observaciones = "Solo cambia una observación";
        await _svc.UpdateAsync(creada.Id, vm);

        var venta = await VentaAsync(creada.Id);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, venta.EstadoAutorizacion);
        Assert.Equal(Autorizador, venta.UsuarioAutoriza);
        Assert.DoesNotContain(_auditoria.Eventos, e => e.Accion == "InvalidarExcepcionReferencias");
    }

    [Fact]
    public async Task Editar_SiLasReferenciasDejaronDeAlcanzar_UnaAutorizacionAnteriorQueNoLasCubriaSeInvalida()
    {
        await SembrarAsync();
        await AgregarReferenciaAsync(EstadoVerificacionReferencia.Verificada);
        var creada = await _svc.CreateAsync(NuevaVenta());
        Assert.Equal(EstadoAutorizacionVenta.NoRequiere, (await VentaAsync(creada.Id)).EstadoAutorizacion);

        // La venta se autorizó por otro motivo, y después la referencia verificada se dio de baja.
        var entidad = await _ctx.Ventas.FirstAsync(v => v.Id == creada.Id);
        entidad.RequiereAutorizacion = true;
        entidad.EstadoAutorizacion = EstadoAutorizacionVenta.Autorizada;
        entidad.UsuarioAutoriza = Autorizador;
        entidad.RazonesAutorizacionJson = "[{\"Tipo\":2,\"Descripcion\":\"Mora\"}]";
        var referencia = await _ctx.ClienteReferencias.FirstAsync();
        referencia.IsDeleted = true;
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        var vm = await _svc.GetByIdAsync(creada.Id);
        vm!.RowVersion = await FijarRowVersionAsync(creada.Id);
        vm.Observaciones = "Edición posterior a la baja de la referencia";
        await _svc.UpdateAsync(creada.Id, vm);

        var venta = await VentaAsync(creada.Id);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.True(VentaAutorizacionReferencias.ContieneReferencias(venta.RazonesAutorizacionJson));
    }

    [Fact]
    public async Task Editar_AutorizarCompletarReferenciasCambiarMontoYRetirarlas_ExigeUnaAutorizacionNuevaParaElMontoActual()
    {
        // Hallazgo de la revisión independiente: la excepción dada para un monto no puede sobrevivir a un cambio de monto
        // aunque, en ese momento, las referencias alcancen.
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Excepción para el monto original", Refs);

        // 1) Las referencias pasan a alcanzar.
        await AgregarReferenciaAsync(EstadoVerificacionReferencia.Verificada);

        // 2) Se cambia el monto mientras las referencias alcanzan.
        _auditoria.Eventos.Clear();
        ComoUsuario(Creador, autoriza: true);
        var vm = await _svc.GetByIdAsync(creada.Id);
        vm!.RowVersion = await FijarRowVersionAsync(creada.Id);
        vm.Detalles[0].Cantidad = 3;
        vm.Detalles[0].Subtotal = 3_000m;
        await _svc.UpdateAsync(creada.Id, vm);

        var tras = await VentaAsync(creada.Id);
        Assert.False(VentaAutorizacionReferencias.ContieneReferencias(tras.RazonesAutorizacionJson),
            "la excepción por referencias del monto anterior no debe seguir registrada como cubierta");
        Assert.Contains(_auditoria.Eventos, e => e.Accion == "InvalidarExcepcionReferencias");

        // 3) Las referencias vuelven a faltar y se edita otra vez sin tocar el monto.
        var referencia = await _ctx.ClienteReferencias.FirstAsync();
        referencia.IsDeleted = true;
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        vm = await _svc.GetByIdAsync(creada.Id);
        vm!.RowVersion = await FijarRowVersionAsync(creada.Id);
        vm.Observaciones = "Edición tras retirar la referencia";
        await _svc.UpdateAsync(creada.Id, vm);

        var venta = await VentaAsync(creada.Id);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.Null(venta.UsuarioAutoriza);
        Assert.True(VentaAutorizacionReferencias.ContieneReferencias(venta.RazonesAutorizacionJson));
    }

    [Fact]
    public async Task Editar_ConReferenciasSuficientes_YSinCambiarElMonto_NoRetiraLaRazonDeLoAutorizado()
    {
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Excepción para el monto original", Refs);
        await AgregarReferenciaAsync(EstadoVerificacionReferencia.Verificada);
        _auditoria.Eventos.Clear();

        ComoUsuario(Creador, autoriza: true);
        var vm = await _svc.GetByIdAsync(creada.Id);
        vm!.RowVersion = await FijarRowVersionAsync(creada.Id);
        vm.Observaciones = "Solo una observación";
        await _svc.UpdateAsync(creada.Id, vm);

        var venta = await VentaAsync(creada.Id);
        Assert.True(VentaAutorizacionReferencias.ContieneReferencias(venta.RazonesAutorizacionJson));
        Assert.DoesNotContain(_auditoria.Eventos, e => e.Accion == "InvalidarExcepcionReferencias");
    }

    // ------------------------------------------------------------------ dobles

    private sealed class UsuarioMutable : ICurrentUserService
    {
        public string Nombre { get; set; } = "x";
        public bool Autoriza { get; set; }
        public string GetUsername() => Nombre;
        public string GetUserId() => "system";
        public bool IsAuthenticated() => true;
        public string? GetEmail() => null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string modulo, string accion) =>
            modulo == "ventas" && ((accion == "authorize" && Autoriza) || accion == "create" || accion == "update");
        public string? GetIpAddress() => null;
    }

    private sealed class AuditoriaGrabadora : ISeguridadAuditoriaService
    {
        public List<(string Modulo, string Accion, string Entidad, string? Detalle)> Eventos { get; } = new();

        public Task RegistrarEventoAsync(string modulo, string accion, string entidad, string? detalle = null)
        {
            Eventos.Add((modulo, accion, entidad, detalle));
            return Task.CompletedTask;
        }

        public void AgregarEvento(string modulo, string accion, string entidad, string? detalle = null) =>
            Eventos.Add((modulo, accion, entidad, detalle));

        public Task<AuditoriaQueryResult> ConsultarEventosAsync(
            string? usuario = null, string? modulo = null, string? accion = null,
            DateOnly? desde = null, DateOnly? hasta = null, int skip = 0, int? take = null) =>
            Task.FromResult(new AuditoriaQueryResult());
    }

    // ============================================================================================
    // Excepción de referencias: alcance exacto. Cada razón se autoriza de forma expresa y por
    // separado; la excepción de referencias solo exime las referencias.
    // ============================================================================================

    private static readonly TipoRazonAutorizacion[] SoloMora = { TipoRazonAutorizacion.MoraActiva };
    private static readonly TipoRazonAutorizacion[] MoraYRefs = { TipoRazonAutorizacion.MoraActiva, TipoRazonAutorizacion.ReferenciasInsuficientes };

    private async Task SembrarMoraNoBloqueanteAsync()
    {
        var credito = new Credito
        {
            ClienteId = _cliente.Id, Numero = "MORA-NB-D02", Estado = EstadoCredito.Activo, SaldoPendiente = 500m
        };
        _ctx.Creditos.Add(credito);
        await _ctx.SaveChangesAsync();
        _ctx.Cuotas.Add(new Cuota
        {
            CreditoId = credito.Id, NumeroCuota = 1, FechaVencimiento = DateTime.UtcNow.Date.AddDays(-10),
            MontoCapital = 500m, MontoTotal = 500m, MontoPagado = 0m, Estado = EstadoCuota.Vencida
        });
        await _ctx.SaveChangesAsync();
    }

    private async Task<int> CrearVentaConMoraNoBloqueanteYSinReferenciasAsync()
    {
        await SembrarAsync(mora: true, diasNoApto: 365);
        await SembrarMoraNoBloqueanteAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        var razones = VentaRazonesAutorizacionJson.Leer((await VentaAsync(creada.Id)).RazonesAutorizacionJson);
        Assert.Contains(razones, r => r.Tipo == TipoRazonAutorizacion.MoraActiva);
        Assert.Contains(razones, r => r.Tipo == TipoRazonAutorizacion.ReferenciasInsuficientes);
        Assert.All(razones, r => Assert.False(r.Autorizada));
        return creada.Id;
    }

    private async Task<List<RazonAutorizacion>> RazonesAsync(int id) =>
        VentaRazonesAutorizacionJson.Leer((await VentaAsync(id)).RazonesAutorizacionJson);

    [Fact]
    public async Task Razones_MoraNoBloqueanteMasReferencias_AutorizarSoloReferenciasDejaLaVentaPendienteYElConfirmarBloqueado()
    {
        var id = await CrearVentaConMoraNoBloqueanteYSinReferenciasAsync();
        ComoUsuario(Autorizador, autoriza: true);

        Assert.True(await _svc.AutorizarVentaAsync(id, Autorizador, "Solo exceptúo las referencias", Refs));

        var venta = await VentaAsync(id);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.Null(venta.UsuarioAutoriza);
        var razones = await RazonesAsync(id);
        var refs = razones.Single(r => r.Tipo == TipoRazonAutorizacion.ReferenciasInsuficientes);
        Assert.True(refs.Autorizada);
        Assert.Equal(Autorizador, refs.AutorizadaPor);
        Assert.Equal("Solo exceptúo las referencias", refs.MotivoAutorizacion);
        Assert.False(razones.Single(r => r.Tipo == TipoRazonAutorizacion.MoraActiva).Autorizada);

        // Confirmar: el chequeo de referencias pasa (están autorizadas) pero la mora sigue sin autorizar y el mensaje la nombra.
        var validacion = new ValidacionVentaResult { RequiereAutorizacion = true };
        validacion.RazonesAutorizacion.Add(new RazonAutorizacion { Tipo = TipoRazonAutorizacion.ReferenciasInsuficientes, Descripcion = "Referencias verificadas insuficientes: 0 de 1 requeridas" });
        var entidad = await VentaAsync(id);
        VentaService.ExigirAutorizacionDeReferencias(entidad, validacion);
        var ex = Assert.Throws<InvalidOperationException>(() => VentaService.ExigirTodasLasRazonesAutorizadas(entidad, validacion));
        Assert.Contains("Falta autorizar", ex.Message);
        Assert.Contains("Mora activa", ex.Message);
        Assert.DoesNotContain("Referencias verificadas insuficientes", ex.Message.Split("Falta autorizar")[1].Split('.')[0]);
        // Y el flujo real de confirmación tampoco avanza.
        await _ctx.Database.ExecuteSqlRawAsync("UPDATE Ventas SET Estado = {0} WHERE Id = {1}", (int)EstadoVenta.Presupuesto, id);
        _ctx.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.ConfirmarVentaAsync(id));
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, (await VentaAsync(id)).EstadoAutorizacion);
    }

    [Fact]
    public async Task Razones_MoraNoBloqueanteMasReferencias_AutorizarSoloLaMoraSinReferenciasDejaLaVentaPendiente()
    {
        var id = await CrearVentaConMoraNoBloqueanteYSinReferenciasAsync();
        ComoUsuario(Autorizador, autoriza: true);

        Assert.True(await _svc.AutorizarVentaAsync(id, Autorizador, "Autorizo la mora", SoloMora));

        var venta = await VentaAsync(id);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        var razones = await RazonesAsync(id);
        Assert.True(razones.Single(r => r.Tipo == TipoRazonAutorizacion.MoraActiva).Autorizada);
        Assert.False(razones.Single(r => r.Tipo == TipoRazonAutorizacion.ReferenciasInsuficientes).Autorizada);
        // Las referencias siguen bloqueando la confirmación.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AsegurarReferenciasAutorizadasAsync(id));
        Assert.Contains("segundo usuario", ex.Message);
    }

    [Fact]
    public async Task Razones_MoraNoBloqueanteMasReferencias_AutorizarAmbasExpresamente_AutorizaLaVenta()
    {
        var id = await CrearVentaConMoraNoBloqueanteYSinReferenciasAsync();
        ComoUsuario(Autorizador, autoriza: true);

        // En dos actos. Autorizar una razón actualiza UpdatedBy, pero quien solo autorizó (no editó las condiciones) sigue
        // pudiendo ser el segundo usuario de las referencias.
        await _svc.AutorizarVentaAsync(id, Autorizador, "Primero la mora", SoloMora);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, (await VentaAsync(id)).EstadoAutorizacion);
        await _svc.AutorizarVentaAsync(id, Autorizador, "Ahora las referencias", Refs);

        var venta = await VentaAsync(id);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, venta.EstadoAutorizacion);
        Assert.Equal(Autorizador, venta.UsuarioAutoriza);
        Assert.Equal("Ahora las referencias", venta.MotivoAutorizacion);
        Assert.All(await RazonesAsync(id), r => Assert.True(r.Autorizada));
        await _svc.AsegurarReferenciasAutorizadasAsync(id);
    }

    [Fact]
    public async Task Razones_MoraNoBloqueanteMasReferencias_AmbasEnUnSoloActo_AutorizaLaVentaYAuditaCadaRazon()
    {
        var id = await CrearVentaConMoraNoBloqueanteYSinReferenciasAsync();
        ComoUsuario(Autorizador, autoriza: true);

        await _svc.AutorizarVentaAsync(id, Autorizador, "Ambas", MoraYRefs);

        Assert.Equal(EstadoAutorizacionVenta.Autorizada, (await VentaAsync(id)).EstadoAutorizacion);
        Assert.Equal(2, _auditoria.Eventos.Count);
        var refs = Assert.Single(_auditoria.Eventos, e => e.Accion == "AutorizarExcepcionReferencias");
        Assert.Contains("ReferenciasInsuficientes", refs.Detalle);
        var mora = Assert.Single(_auditoria.Eventos, e => e.Accion == "AutorizarRazonVenta");
        Assert.Contains("MoraActiva", mora.Detalle);
    }

    [Fact]
    public async Task Razones_VentaConReferenciasPendientes_SinListaExpresa_SeRechazaYNoCambiaNada()
    {
        var id = await CrearVentaConMoraNoBloqueanteYSinReferenciasAsync();
        ComoUsuario(Autorizador, autoriza: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(id, Autorizador, "Autorizo todo"));
        Assert.Contains("expresa", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(id, Autorizador, "Autorizo todo", null));

        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, (await VentaAsync(id)).EstadoAutorizacion);
        Assert.All(await RazonesAsync(id), r => Assert.False(r.Autorizada));
        Assert.Empty(_auditoria.Eventos);
    }

    [Theory]
    [InlineData("vacia")]
    [InlineData("repetida")]
    [InlineData("desconocida")]
    [InlineData("ajena")]
    public async Task Razones_ListaManipulada_SeRechazaSinAutorizarNada(string caso)
    {
        var id = await CrearVentaConMoraNoBloqueanteYSinReferenciasAsync();
        ComoUsuario(Autorizador, autoriza: true);
        TipoRazonAutorizacion[] lista = caso switch
        {
            "vacia" => Array.Empty<TipoRazonAutorizacion>(),
            "repetida" => new[] { TipoRazonAutorizacion.MoraActiva, TipoRazonAutorizacion.MoraActiva },
            "desconocida" => new[] { (TipoRazonAutorizacion)99, TipoRazonAutorizacion.ReferenciasInsuficientes },
            _ => new[] { TipoRazonAutorizacion.ExcedeCupo, TipoRazonAutorizacion.ReferenciasInsuficientes } // una razón que la venta no tiene
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(id, Autorizador, "Manipulada", lista));

        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, (await VentaAsync(id)).EstadoAutorizacion);
        Assert.All(await RazonesAsync(id), r => Assert.False(r.Autorizada));
        Assert.Empty(_auditoria.Eventos);
    }

    [Fact]
    public async Task Razones_YaAutorizadaNoSePuedeAutorizarDeNuevo()
    {
        var id = await CrearVentaConMoraNoBloqueanteYSinReferenciasAsync();
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(id, Autorizador, "Primero la mora", SoloMora);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(id, Autorizador, "Otra vez la mora", SoloMora));
    }

    [Fact]
    public async Task Razones_ElCreadorNoPuedeAutorizarLasReferencias_AunqueLasIncluyaJuntoConOtrasRazones()
    {
        var id = await CrearVentaConMoraNoBloqueanteYSinReferenciasAsync();
        ComoUsuario(Creador, autoriza: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(id, Creador, "Me autorizo", MoraYRefs));

        Assert.Contains("segundo usuario", ex.Message);
        Assert.All(await RazonesAsync(id), r => Assert.False(r.Autorizada));

        // Segundo usuario sin ventas.authorize: tampoco.
        ComoUsuario(Autorizador, autoriza: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(id, Autorizador, "Sin permiso", Refs));
        Assert.All(await RazonesAsync(id), r => Assert.False(r.Autorizada));
    }

    [Fact]
    public async Task Razones_CupoExceptuadoEnElAltaMasReferencias_AutorizarLasReferenciasCompletaLaVenta_YElCupoNoAutorizaLasReferencias()
    {
        await SembrarAsync(cupo: true, limite: 500m);
        var creada = await _svc.CreateAsync(NuevaVenta(excepcion: true, motivo: "Cupo excedido, lo autorizo"));

        var antes = await RazonesAsync(creada.Id);
        Assert.True(antes.Single(r => r.Tipo == TipoRazonAutorizacion.ExcedeCupo).Autorizada);
        Assert.False(antes.Single(r => r.Tipo == TipoRazonAutorizacion.ReferenciasInsuficientes).Autorizada);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, (await VentaAsync(creada.Id)).EstadoAutorizacion);

        ComoUsuario(Autorizador, autoriza: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Ya autorizada", new[] { TipoRazonAutorizacion.ExcedeCupo }));
        await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Referencias", Refs);

        Assert.Equal(EstadoAutorizacionVenta.Autorizada, (await VentaAsync(creada.Id)).EstadoAutorizacion);
    }

    [Fact]
    public async Task Razones_BcraConRequiereAutorizacionMasReferencias_NingunaCubreALaOtra()
    {
        await SembrarAsync(bcra: 2);
        var creada = await _svc.CreateAsync(NuevaVenta());
        var razones = await RazonesAsync(creada.Id);
        var otras = razones.Where(r => r.Tipo != TipoRazonAutorizacion.ReferenciasInsuficientes).Select(r => r.Tipo).ToArray();
        Assert.NotEmpty(otras); // BCRA situación 2 agrega su propia razón de autorización
        ComoUsuario(Autorizador, autoriza: true);

        await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Solo referencias", Refs);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, (await VentaAsync(creada.Id)).EstadoAutorizacion);

        await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "El resto", otras);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, (await VentaAsync(creada.Id)).EstadoAutorizacion);
    }

    [Fact]
    public async Task Razones_MoraBloqueanteSigueSiendoNoViable_AunQueSeQuieraExceptuarLasReferencias()
    {
        await SembrarAsync(mora: true);
        await SembrarMoraBloqueanteAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CreateAsync(NuevaVenta(excepcion: true, motivo: "Quiero igual")));

        Assert.Contains("No es posible crear la venta con crédito personal", ex.Message);
        Assert.Empty(await _ctx.Ventas.ToListAsync());
    }

    [Fact]
    public async Task Razones_SinReferenciasFaltantes_ElComportamientoAnteriorNoCambia()
    {
        await SembrarAsync(mora: true, diasNoApto: 365);
        await AgregarReferenciaAsync(EstadoVerificacionReferencia.Verificada);
        await SembrarMoraNoBloqueanteAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        var razones = await RazonesAsync(creada.Id);
        Assert.DoesNotContain(razones, r => r.Tipo == TipoRazonAutorizacion.ReferenciasInsuficientes);
        ComoUsuario(Autorizador, autoriza: true);

        // Sin parámetro: autoriza todas las razones presentes, como antes.
        Assert.True(await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Autorizo la mora"));

        var venta = await VentaAsync(creada.Id);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, venta.EstadoAutorizacion);
        Assert.Equal(Autorizador, venta.UsuarioAutoriza);
        Assert.Equal("Autorizo la mora", venta.MotivoAutorizacion);
        var evento = Assert.Single(_auditoria.Eventos);
        Assert.Equal("AutorizarRazonVenta", evento.Accion);
    }

    [Fact]
    public async Task Razones_SinReferenciasFaltantes_AutorizarLaMoraPorSuTipoTambienAutorizaLaVenta()
    {
        await SembrarAsync(mora: true, diasNoApto: 365);
        await AgregarReferenciaAsync(EstadoVerificacionReferencia.Verificada);
        await SembrarMoraNoBloqueanteAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        ComoUsuario(Autorizador, autoriza: true);

        await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Autorizo la mora", SoloMora);

        Assert.Equal(EstadoAutorizacionVenta.Autorizada, (await VentaAsync(creada.Id)).EstadoAutorizacion);
    }

    [Fact]
    public async Task Razones_ElCambioDeMontoConReferenciasYaSuficientes_RetiraSoloLaAutorizacionDeReferencias_NoLaDeLaMora()
    {
        var id = await CrearVentaConMoraNoBloqueanteYSinReferenciasAsync();
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(id, Autorizador, "Ambas", MoraYRefs);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, (await VentaAsync(id)).EstadoAutorizacion);
        await AgregarReferenciaAsync(EstadoVerificacionReferencia.Verificada); // ahora las referencias alcanzan
        _auditoria.Eventos.Clear();

        ComoUsuario(Creador, autoriza: true);
        var vm = await _svc.GetByIdAsync(id);
        vm!.RowVersion = await FijarRowVersionAsync(id);
        vm.Detalles[0].Cantidad = 2;
        vm.Detalles[0].Subtotal = 2_000m;
        await _svc.UpdateAsync(id, vm);

        var venta = await VentaAsync(id);
        var razones = VentaRazonesAutorizacionJson.Leer(venta.RazonesAutorizacionJson);
        Assert.DoesNotContain(razones, r => r.Tipo == TipoRazonAutorizacion.ReferenciasInsuficientes);
        var mora = razones.Single(r => r.Tipo == TipoRazonAutorizacion.MoraActiva);
        Assert.True(mora.Autorizada);
        Assert.Equal(Autorizador, mora.AutorizadaPor);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, venta.EstadoAutorizacion);
        Assert.Contains(_auditoria.Eventos, e => e.Accion == "InvalidarExcepcionReferencias");
    }

    [Fact]
    public async Task Razones_SoloReferenciasAutorizadas_YCambioDeMontoConReferenciasQueSiguenFaltando_ExigeAutorizarDeNuevo()
    {
        var id = await CrearVentaConMoraNoBloqueanteYSinReferenciasAsync();
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(id, Autorizador, "Solo referencias", Refs);

        ComoUsuario(Creador, autoriza: true);
        var vm = await _svc.GetByIdAsync(id);
        vm!.RowVersion = await FijarRowVersionAsync(id);
        vm.Detalles[0].Cantidad = 2;
        vm.Detalles[0].Subtotal = 2_000m;
        await _svc.UpdateAsync(id, vm);

        var venta = await VentaAsync(id);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.All(VentaRazonesAutorizacionJson.Leer(venta.RazonesAutorizacionJson), r => Assert.False(r.Autorizada));
    }

    // ============================================================================================
    // Revisión de Codex (H1-H3)
    // ============================================================================================

    [Fact]
    public async Task H2_AEditaCondiciones_BAutorizaLaMora_AUnaVezMas_NoPuedeAutorizarLasReferencias_PeroUnTerceroSi()
    {
        var id = await CrearVentaConMoraNoBloqueanteYSinReferenciasAsync(); // la crea Creador (A)

        // B edita las condiciones de la venta.
        const string B = "gerente.b", C = "gerente.c", D = "gerente.d";
        ComoUsuario(B, autoriza: true);
        var vm = await _svc.GetByIdAsync(id);
        vm!.RowVersion = await FijarRowVersionAsync(id);
        vm.Observaciones = "B cambió las condiciones";
        await _svc.UpdateAsync(id, vm);
        Assert.Equal(B, (await VentaAsync(id)).UsuarioSolicita);

        // C autoriza solo la mora: el guardado deja UpdatedBy = C.
        ComoUsuario(C, autoriza: true);
        await _svc.AutorizarVentaAsync(id, C, "Mora", SoloMora);
        Assert.Equal(C, (await VentaAsync(id)).UpdatedBy);

        // B (último editor de condiciones) no puede autorizar las referencias.
        ComoUsuario(B, autoriza: true);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(id, B, "Referencias", Refs));
        Assert.Contains("segundo usuario", ex.Message);
        Assert.False((await RazonesAsync(id)).Single(r => r.Tipo == TipoRazonAutorizacion.ReferenciasInsuficientes).Autorizada);

        // El creador tampoco, y un tercero sí.
        ComoUsuario(Creador, autoriza: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(id, Creador, "Referencias", Refs));
        ComoUsuario(D, autoriza: true);
        await _svc.AutorizarVentaAsync(id, D, "Referencias", Refs);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, (await VentaAsync(id)).EstadoAutorizacion);
    }

    [Fact]
    public async Task H2_ElEditorQueTambienAutorizoLaMora_NoPuedeLuegoAutorizarLasReferencias()
    {
        var id = await CrearVentaConMoraNoBloqueanteYSinReferenciasAsync();
        const string B = "gerente.b";
        ComoUsuario(B, autoriza: true);
        var vm = await _svc.GetByIdAsync(id);
        vm!.RowVersion = await FijarRowVersionAsync(id);
        vm.Observaciones = "B edita";
        await _svc.UpdateAsync(id, vm);
        await _svc.AutorizarVentaAsync(id, B, "Mora", SoloMora);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(id, B, "Referencias", Refs));
    }

    [Fact]
    public async Task H1_AutorizadaSoloPorReferencias_SiLuegoAparecePorMoraNoBloqueante_ElGateDeConfirmarNoProcede()
    {
        await SembrarAsync(mora: true, diasNoApto: 365);
        var creada = await _svc.CreateAsync(NuevaVenta()); // solo faltan referencias
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Referencias", Refs);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, (await VentaAsync(creada.Id)).EstadoAutorizacion);

        // Se configura el crédito de la venta y, después, otra cuota del cliente entra en mora no bloqueante.
        var credito = new Credito { ClienteId = _cliente.Id, Numero = "CRED-H1", Estado = EstadoCredito.Configurado, SaldoPendiente = 0m };
        _ctx.Creditos.Add(credito);
        await _ctx.SaveChangesAsync();
        await _ctx.Database.ExecuteSqlRawAsync("UPDATE Ventas SET CreditoId = {0} WHERE Id = {1}", credito.Id, creada.Id);
        _ctx.ChangeTracker.Clear();
        await SembrarMoraNoBloqueanteAsync();

        var vigente = await _validacionSvc.ValidarConfirmacionVentaAsync(creada.Id);
        Assert.False(vigente.NoViable);
        Assert.Contains(vigente.RazonesAutorizacion, r => r.Tipo == TipoRazonAutorizacion.MoraActiva);

        var venta = await VentaAsync(creada.Id);
        VentaService.ExigirAutorizacionDeReferencias(venta, vigente); // las referencias sí están cubiertas
        var ex = Assert.Throws<InvalidOperationException>(() => VentaService.ExigirTodasLasRazonesAutorizadas(venta, vigente));
        Assert.Contains("Falta autorizar", ex.Message);
        Assert.Contains("Mora activa", ex.Message);
        // Y por el camino de Configurar.
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AsegurarReferenciasAutorizadasAsync(creada.Id));
        Assert.Contains("Mora activa", ex2.Message);

    }

    [Fact]
    public async Task H1_MoraBloqueanteSigueSiendoNoViableEnLaRevalidacion()
    {
        await SembrarAsync(mora: true);
        var creada = await _svc.CreateAsync(NuevaVenta());
        var credito = new Credito { ClienteId = _cliente.Id, Numero = "CRED-H1B", Estado = EstadoCredito.Configurado, SaldoPendiente = 0m };
        _ctx.Creditos.Add(credito);
        await _ctx.SaveChangesAsync();
        await _ctx.Database.ExecuteSqlRawAsync("UPDATE Ventas SET CreditoId = {0} WHERE Id = {1}", credito.Id, creada.Id);
        _ctx.ChangeTracker.Clear();
        await SembrarMoraBloqueanteAsync();

        var vigente = await _validacionSvc.ValidarConfirmacionVentaAsync(creada.Id);

        Assert.True(vigente.NoViable);
    }

    [Fact]
    public async Task H3_ExcepcionDocumentalConMoraNoBloqueantePendiente_NoDejaLaVentaAutorizada()
    {
        await SembrarAsync(minimo: 0, mora: true, diasNoApto: 365, docs: true);
        await SembrarMoraNoBloqueanteAsync();

        var creada = await _svc.CreateAsync(NuevaVenta(excepcion: true, motivo: "Documentación en trámite"));

        var venta = await VentaAsync(creada.Id);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.True(venta.RequiereAutorizacion);
        Assert.Null(venta.UsuarioAutoriza);
        var razones = VentaRazonesAutorizacionJson.Leer(venta.RazonesAutorizacionJson);
        Assert.True(razones.Single(r => r.Tipo == TipoRazonAutorizacion.DocumentacionVencida).Autorizada);
        Assert.False(razones.Single(r => r.Tipo == TipoRazonAutorizacion.MoraActiva).Autorizada);

        // Autorizar la mora (única razón pendiente) completa la venta.
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Mora", SoloMora);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, (await VentaAsync(creada.Id)).EstadoAutorizacion);
    }

    [Fact]
    public async Task H3_ExcepcionDocumentalSinOtrasRazones_SigueAutorizandoComoAntes()
    {
        await SembrarAsync(minimo: 0, docs: true);

        var creada = await _svc.CreateAsync(NuevaVenta(excepcion: true, motivo: "Documentación en trámite"));

        var venta = await VentaAsync(creada.Id);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, venta.EstadoAutorizacion);
        Assert.All(VentaRazonesAutorizacionJson.Leer(venta.RazonesAutorizacionJson), r => Assert.True(r.Autorizada));
    }

    [Fact]
    public async Task Invariante_Edit_UnaVentaAutorizadaConUnaRazonNuevaSinAutorizarVuelveAPendiente()
    {
        await SembrarAsync(mora: true, diasNoApto: 365);
        await AgregarReferenciaAsync(EstadoVerificacionReferencia.Verificada);
        var creada = await _svc.CreateAsync(NuevaVenta()); // sin razones: NoRequiere
        var entidad = await _ctx.Ventas.FirstAsync(v => v.Id == creada.Id);
        entidad.RequiereAutorizacion = true;
        entidad.EstadoAutorizacion = EstadoAutorizacionVenta.Autorizada;
        entidad.UsuarioAutoriza = Autorizador;
        entidad.RazonesAutorizacionJson = "[{\"Tipo\":3,\"Descripcion\":\"Doc\",\"Autorizada\":true,\"AutorizadaPor\":\"gerente.dos\"}]";
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();
        await SembrarMoraNoBloqueanteAsync(); // aparece una mora no bloqueante

        var vm = await _svc.GetByIdAsync(creada.Id);
        vm!.RowVersion = await FijarRowVersionAsync(creada.Id);
        vm.Observaciones = "Edición posterior a la mora";
        await _svc.UpdateAsync(creada.Id, vm);

        var venta = await VentaAsync(creada.Id);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.Null(venta.UsuarioAutoriza);
        Assert.Contains(_auditoria.Eventos, e => e.Accion == "InvalidarAutorizacionRazonNueva");
    }

    // ============================================================================================
    // Segunda revisión de Codex (H4-H5)
    // ============================================================================================

    [Fact]
    public async Task H4_DocumentacionVencidaYMoraNuevaTrasAutorizarSoloReferencias_ConfirmarNoProcede()
    {
        await SembrarAsync(mora: true, diasNoApto: 365);
        var creada = await _svc.CreateAsync(NuevaVenta()); // solo faltan referencias
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Referencias", Refs);

        // Crédito configurado; después vence la documentación Y entra en mora no bloqueante otra cuota.
        var credito = new Credito { ClienteId = _cliente.Id, Numero = "CRED-H4", Estado = EstadoCredito.Configurado, SaldoPendiente = 0m };
        _ctx.Creditos.Add(credito);
        await _ctx.SaveChangesAsync();
        await _ctx.Database.ExecuteSqlRawAsync("UPDATE Ventas SET CreditoId = {0}, Estado = {1} WHERE Id = {2}", credito.Id, (int)EstadoVenta.Presupuesto, creada.Id);
        (await _ctx.Set<ConfiguracionCredito>().FirstAsync()).ValidarDocumentacion = true;
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();
        await SembrarMoraNoBloqueanteAsync();

        var vigente = await _validacionSvc.ValidarConfirmacionVentaAsync(creada.Id);
        Assert.True(vigente.NoViable); // el bloqueo de documentación coexiste...
        Assert.Contains(vigente.RazonesAutorizacion, r => r.Tipo == TipoRazonAutorizacion.MoraActiva); // ...y la mora vigente no se pierde

        var venta = await VentaAsync(creada.Id);
        VentaService.ExigirAutorizacionDeReferencias(venta, vigente);
        var ex = Assert.Throws<InvalidOperationException>(() => VentaService.ExigirTodasLasRazonesAutorizadas(venta, vigente));
        Assert.Contains("Mora activa", ex.Message);

    }

    [Fact]
    public async Task H5_VentaLegadaSinEditorRegistrado_QuienEditoYAutorizoLaMora_NoPuedeAutorizarLasReferencias()
    {
        var id = await CrearVentaConMoraNoBloqueanteYSinReferenciasAsync(); // CreatedBy = Creador (A)
        const string B = "gerente.b";
        // Venta legada: B editó las condiciones (UpdatedBy = B) pero no hay editor registrado.
        await _ctx.Database.ExecuteSqlRawAsync("UPDATE Ventas SET UsuarioSolicita = NULL, UpdatedBy = {0} WHERE Id = {1}", B, id);
        _ctx.ChangeTracker.Clear();

        ComoUsuario(B, autoriza: true);
        // B autoriza primero la mora (la guarda de creador/editor para referencias solo aplica a las referencias)...
        await _svc.AutorizarVentaAsync(id, B, "Mora", SoloMora);
        Assert.Equal(B, (await VentaAsync(id)).UpdatedBy);

        // ...y en la siguiente solicitud ya no puede autorizar las referencias: UpdatedBy se compara de forma estricta.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(id, B, "Referencias", Refs));
        Assert.Contains("segundo usuario", ex.Message);
        Assert.False((await RazonesAsync(id)).Single(r => r.Tipo == TipoRazonAutorizacion.ReferenciasInsuficientes).Autorizada);

        // Un tercero sí puede.
        ComoUsuario("gerente.d", autoriza: true);
        await _svc.AutorizarVentaAsync(id, "gerente.d", "Referencias", Refs);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, (await VentaAsync(id)).EstadoAutorizacion);
    }

    [Fact]
    public async Task H5_VentaLegadaSinEditorRegistrado_UnSoloActoConAmbasCasillas_SigueFuncionandoParaQuienNoEditoNiCreo()
    {
        var id = await CrearVentaConMoraNoBloqueanteYSinReferenciasAsync();
        await _ctx.Database.ExecuteSqlRawAsync("UPDATE Ventas SET UsuarioSolicita = NULL WHERE Id = {0}", id);
        _ctx.ChangeTracker.Clear();
        ComoUsuario(Autorizador, autoriza: true);

        await _svc.AutorizarVentaAsync(id, Autorizador, "Ambas", MoraYRefs);

        Assert.Equal(EstadoAutorizacionVenta.Autorizada, (await VentaAsync(id)).EstadoAutorizacion);
    }

    // ============================================================================================
    // Confirmación completa de crédito (ConfirmarVentaCreditoAsync) con los servicios reales de validación: cierra el hueco
    // "se probó el gate y la revalidación por separado". Solo se simulan caja, stock y contrato.
    // ============================================================================================

    /// <summary>Crea la venta por el servicio real y deja su crédito Configurado (sin anticipo: el monto aprobado es el total).</summary>
    private async Task<int> CrearVentaConCreditoConfiguradoAsync()
    {
        var creada = await _svc.CreateAsync(NuevaVenta());
        var total = (await VentaAsync(creada.Id)).Total;
        var credito = new Credito
        {
            ClienteId = _cliente.Id, Numero = $"CRED-{Guid.NewGuid():N}"[..14], Estado = EstadoCredito.Configurado, TasaInteres = 0m,
            MontoSolicitado = total, MontoAprobado = total, SaldoPendiente = total, CantidadCuotas = 6,
            FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1)
        };
        _ctx.Creditos.Add(credito);
        await _ctx.SaveChangesAsync();
        await _ctx.Database.ExecuteSqlRawAsync(
            "UPDATE Ventas SET CreditoId = {0}, FechaConfiguracionCredito = {1}, Estado = {2} WHERE Id = {3}",
            credito.Id, DateTime.UtcNow, (int)EstadoVenta.Presupuesto, creada.Id);
        _ctx.ChangeTracker.Clear();
        return creada.Id;
    }

    private async Task AfirmarSinCambiosAsync(int ventaId)
    {
        var venta = await VentaAsync(ventaId);
        Assert.Equal(EstadoVenta.Presupuesto, venta.Estado);
        var credito = await _ctx.Creditos.AsNoTracking().FirstAsync(c => c.Id == venta.CreditoId);
        Assert.Equal(EstadoCredito.Configurado, credito.Estado);
        Assert.Empty(await _ctx.Cuotas.AsNoTracking().Where(c => c.CreditoId == credito.Id).ToListAsync());
    }

    [Fact]
    public async Task ConfirmarCredito_ConReferenciasSuficientes_ConfirmaLaVentaYGeneraElCredito()
    {
        await SembrarAsync();
        await AgregarReferenciaAsync(EstadoVerificacionReferencia.Verificada);
        var id = await CrearVentaConCreditoConfiguradoAsync();

        Assert.True(await _svc.ConfirmarVentaCreditoAsync(id));

        var venta = await VentaAsync(id);
        Assert.Equal(EstadoVenta.Confirmada, venta.Estado);
        var credito = await _ctx.Creditos.AsNoTracking().FirstAsync(c => c.Id == venta.CreditoId);
        Assert.Equal(EstadoCredito.Generado, credito.Estado);
        Assert.Equal(6, await _ctx.Cuotas.AsNoTracking().CountAsync(c => c.CreditoId == credito.Id));
    }

    [Fact]
    public async Task ConfirmarCredito_SinReferencias_RechazaHastaQueUnSegundoUsuarioLaExceptua_YLuegoConfirma()
    {
        await SembrarAsync();
        var id = await CrearVentaConCreditoConfiguradoAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.ConfirmarVentaCreditoAsync(id));
        Assert.Contains("requiere autorización", ex.Message);
        await AfirmarSinCambiosAsync(id);

        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(id, Autorizador, "Excepción por referencias", Refs);

        Assert.True(await _svc.ConfirmarVentaCreditoAsync(id));
        var venta = await VentaAsync(id);
        Assert.Equal(EstadoVenta.Confirmada, venta.Estado);
        Assert.Equal(Autorizador, venta.UsuarioAutoriza);
    }

    [Fact]
    public async Task ConfirmarCredito_RazonNuevaEnVentaQueNuncaRequirioAutorizacion_SeRechazaYNoCambiaNada()
    {
        await SembrarAsync();
        await AgregarReferenciaAsync(EstadoVerificacionReferencia.Verificada);
        var id = await CrearVentaConCreditoConfiguradoAsync();
        Assert.Equal(EstadoAutorizacionVenta.NoRequiere, (await VentaAsync(id)).EstadoAutorizacion);

        // Después de configurar, la única referencia verificada se da de baja: aparece una razón que la venta no tenía.
        await _ctx.Database.ExecuteSqlRawAsync("UPDATE ClienteReferencias SET IsDeleted = 1 WHERE ClienteId = {0}", _cliente.Id);
        _ctx.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.ConfirmarVentaCreditoAsync(id));

        // El gate de autorización previo ya la frena; el mensaje puede ser el de autorización general o el de referencias.
        Assert.Contains("autorizaci", ex.Message);
        await AfirmarSinCambiosAsync(id);
    }

    [Fact]
    public async Task ConfirmarCredito_AutorizadaPorReferencias_SiLuegoApareceMoraNoBloqueante_SeRechazaYSeResuelveGuardandoYAutorizandoLaMora()
    {
        await SembrarAsync(mora: true, diasNoApto: 365);
        var id = await CrearVentaConCreditoConfiguradoAsync();
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(id, Autorizador, "Referencias", Refs);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, (await VentaAsync(id)).EstadoAutorizacion);

        await SembrarMoraNoBloqueanteAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.ConfirmarVentaCreditoAsync(id));
        Assert.Contains("Mora activa", ex.Message);
        await AfirmarSinCambiosAsync(id);

        // Camino de resolución documentado: guardar la venta de nuevo vuelve a pedir SOLO la razón nueva y conserva la anterior.
        ComoUsuario(Creador, autoriza: true);
        var vm = await _svc.GetByIdAsync(id);
        vm!.RowVersion = await FijarRowVersionAsync(id);
        vm.Observaciones = "Reconfirmar con la mora nueva";
        await _svc.UpdateAsync(id, vm);

        var pendiente = await VentaAsync(id);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, pendiente.EstadoAutorizacion);
        var razones = await RazonesAsync(id);
        Assert.True(razones.Single(r => r.Tipo == TipoRazonAutorizacion.ReferenciasInsuficientes).Autorizada);
        Assert.False(razones.Single(r => r.Tipo == TipoRazonAutorizacion.MoraActiva).Autorizada);

        ComoUsuario("gerente.tres", autoriza: true);
        await _svc.AutorizarVentaAsync(id, "gerente.tres", "Mora no bloqueante", SoloMora);

        ComoUsuario(Creador, autoriza: true);
        Assert.True(await _svc.ConfirmarVentaCreditoAsync(id));
        Assert.Equal(EstadoVenta.Confirmada, (await VentaAsync(id)).Estado);
    }

    [Fact]
    public async Task ConfirmarCredito_ConMoraBloqueanteAparecidaDespues_SeRechazaEnElServicio_AunConTodasLasRazonesAutorizadas()
    {
        await SembrarAsync(mora: true);
        var id = await CrearVentaConCreditoConfiguradoAsync();
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(id, Autorizador, "Referencias", Refs);
        await SembrarMoraBloqueanteAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.ConfirmarVentaCreditoAsync(id));

        Assert.Contains("No se puede confirmar la venta", ex.Message);
        await AfirmarSinCambiosAsync(id);
    }

    /// <summary>La documentación pasa a ser exigida DESPUÉS de crear y configurar la venta (así la venta ya no se podría crear).</summary>
    private async Task ExigirDocumentacionAhoraAsync()
    {
        await _ctx.Database.ExecuteSqlRawAsync("UPDATE ConfiguracionesCredito SET ValidarDocumentacion = 1");
        _ctx.ChangeTracker.Clear();
    }

    [Fact]
    public async Task ConfirmarCredito_ConDocumentacionFaltanteYExcepcionDocumentalRegistrada_SigueConfirmando()
    {
        // La excepción documental la resuelve el controller (la registra o reutiliza una ya registrada) y luego llama al
        // servicio, que revalida y vuelve a ver la documentación faltante: el guard de bloqueos NO debe rechazarla.
        await SembrarAsync(minimo: 0);
        var id = await CrearVentaConCreditoConfiguradoAsync();
        await ExigirDocumentacionAhoraAsync();
        var vigente = await _validacionSvc.ValidarConfirmacionVentaAsync(id);
        Assert.True(vigente.PendienteRequisitos);
        Assert.All(vigente.RequisitosPendientes, r => Assert.Equal(TipoRequisitoPendiente.DocumentacionFaltante, r.Tipo));

        ComoUsuario(Autorizador, autoriza: true);
        Assert.True(await _svc.RegistrarExcepcionDocumentalAsync(id, Autorizador, "Documentación en trámite"));

        Assert.True(await _svc.ConfirmarVentaCreditoAsync(id));
        Assert.Equal(EstadoVenta.Confirmada, (await VentaAsync(id)).Estado);
    }

    [Fact]
    public async Task ConfirmarCredito_ConDocumentacionFaltanteYMoraBloqueante_SeRechazaAunConExcepcionDocumentalRegistrada()
    {
        await SembrarAsync(minimo: 0, mora: true);
        var id = await CrearVentaConCreditoConfiguradoAsync();
        await ExigirDocumentacionAhoraAsync();
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.RegistrarExcepcionDocumentalAsync(id, Autorizador, "Documentación en trámite");
        await SembrarMoraBloqueanteAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.ConfirmarVentaCreditoAsync(id));

        Assert.Contains("No se puede confirmar la venta", ex.Message);
        await AfirmarSinCambiosAsync(id);
    }

    private sealed class StubMovimientoStockD02 : IMovimientoStockService
    {
        public Task<List<MovimientoStock>> RegistrarSalidasAsync(
            List<(int productoId, decimal cantidad, string? referencia)> salidas, string motivo, string? usuarioActual = null,
            IReadOnlyList<MovimientoStockCostoLinea>? costos = null) => Task.FromResult(new List<MovimientoStock>());
        public Task<List<MovimientoStock>> RegistrarEntradasAsync(
            List<(int productoId, decimal cantidad, string? referencia)> entradas, string motivo, string? usuarioActual = null,
            int? ordenCompraId = null, IReadOnlyList<MovimientoStockCostoLinea>? costos = null) => Task.FromResult(new List<MovimientoStock>());
        public Task<IEnumerable<MovimientoStock>> GetAllAsync() => throw new NotImplementedException();
        public Task<MovimientoStock?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoStock>> GetByProductoIdAsync(int productoId) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoStock>> GetByOrdenCompraIdAsync(int ordenCompraId) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoStock>> GetByTipoAsync(TipoMovimiento tipo) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoStock>> GetByFechaRangoAsync(DateTime fechaDesde, DateTime fechaHasta) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoStock>> SearchAsync(int? productoId = null, TipoMovimiento? tipo = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null, string? orderBy = null, string? orderDirection = "desc") => throw new NotImplementedException();
        public Task<(IEnumerable<MovimientoStock> Items, int Total, decimal TotalEntradas, decimal TotalSalidas, int TotalAjustes)> SearchPaginadoAsync(int? productoId = null, TipoMovimiento? tipo = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null, string? orderBy = null, string? orderDirection = "desc", int pageNumber = 1, int pageSize = 20) => throw new NotImplementedException();
        public Task<MovimientoStock> CreateAsync(MovimientoStock movimiento) => throw new NotImplementedException();
        public Task<MovimientoStock> RegistrarAjusteAsync(int productoId, TipoMovimiento tipo, decimal cantidad, string? referencia, string motivo, string? usuarioActual = null, int? ordenCompraId = null, Guid? requestId = null) => throw new NotImplementedException();
        public Task<bool> HayStockDisponibleAsync(int productoId, decimal cantidad) => throw new NotImplementedException();
        public Task<(bool Valido, string Mensaje)> ValidarCantidadAsync(decimal cantidad) => throw new NotImplementedException();
    }

    private sealed class StubAlertaStockD02 : IAlertaStockService
    {
        public Task<int> VerificarYGenerarAlertasAsync(IEnumerable<int> productoIds) => Task.FromResult(0);
        public Task<int> GenerarAlertasStockBajoAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<List<AlertaStock>> GetAlertasPendientesAsync() => throw new NotImplementedException();
        public Task<PaginatedResult<AlertaStockViewModel>> BuscarAsync(AlertaStockFiltroViewModel filtro) => throw new NotImplementedException();
        public Task<(int Pendientes, int Criticas)> ContarPorEstadoAsync(AlertaStockFiltroViewModel filtro) => throw new NotImplementedException();
        public Task<AlertaStockViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<bool> ResolverAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
        public Task<bool> IgnorarAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
        public Task<bool> MarcarEnProcesoAsync(int id, string usuario, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
        public Task<AlertaStockEstadisticasViewModel> GetEstadisticasAsync() => throw new NotImplementedException();
        public Task<List<AlertaStockViewModel>> GetAlertasByProductoIdAsync(int productoId) => throw new NotImplementedException();
        public Task<AlertaStock?> VerificarYGenerarAlertaAsync(int productoId) => throw new NotImplementedException();
        public Task<int> LimpiarAlertasAntiguasAsync(int diasAntiguedad = 30, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ProductoCriticoViewModel>> GetProductosCriticosAsync() => throw new NotImplementedException();
    }
}
