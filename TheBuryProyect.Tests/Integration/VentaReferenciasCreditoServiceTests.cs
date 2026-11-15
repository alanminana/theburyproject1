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
    private const string Creador = "vendedor.uno";
    private const string Autorizador = "gerente.dos";

    private readonly SqliteConnection _conn;
    private readonly AppDbContext _ctx;
    private readonly UsuarioMutable _usuario = new();
    private readonly HttpContextAccessor _accessor = new();
    private readonly AuditoriaGrabadora _auditoria = new();
    private readonly VentaService _svc;
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

        var apertura = SeedCaja();
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

        _svc = new VentaService(
            _ctx, mapper, NullLogger<VentaService>.Instance,
            null!, null!, new FinancialCalculationService(), new VentaValidator(),
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
    private async Task SembrarAsync(int minimo = 1, bool mora = false, int bcra = 1, bool cupo = false, decimal limite = 1_000_000m)
    {
        _ctx.Set<ConfiguracionCredito>().Add(new ConfiguracionCredito
        {
            ReferenciasVerificadasMinimas = minimo,
            ValidarDocumentacion = false,
            ValidarLimiteCredito = cupo,
            ValidarMora = mora,
            DiasParaNoApto = mora ? 1 : null
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
            _svc.AutorizarVentaAsync(creada.Id, Creador, "Lo autorizo yo"));

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

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Motivo"));

        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, (await VentaAsync(creada.Id)).EstadoAutorizacion);
    }

    [Fact]
    public async Task Autorizar_SinMotivo_SeRechaza()
    {
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        ComoUsuario(Autorizador, autoriza: true);

        await Assert.ThrowsAsync<ArgumentException>(() => _svc.AutorizarVentaAsync(creada.Id, Autorizador, "   "));

        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, (await VentaAsync(creada.Id)).EstadoAutorizacion);
    }

    [Fact]
    public async Task Autorizar_UnSegundoUsuarioConPermiso_AutorizaConTrazaCompletaYSoloUnaVez()
    {
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        ComoUsuario(Autorizador, autoriza: true);

        var primera = await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Cliente conocido, referencias en trámite");
        // doble uso / reintento: ya no está pendiente, se rechaza y no se registra una segunda autorización
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Reintento"));

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
        await _svc.AutorizarVentaAsync(primera.Id, Autorizador, "Excepción para esta venta");

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
        entidad.Observaciones = "Editada por el futuro autorizador"; // UpdatedBy = autorizador
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Motivo"));
    }

    [Fact]
    public async Task Autorizar_UnaVentaAjenaOInexistente_NoHaceNada()
    {
        await SembrarAsync();
        ComoUsuario(Autorizador, autoriza: true);

        Assert.False(await _svc.AutorizarVentaAsync(987_654, Autorizador, "Motivo"));
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
        venta.RazonesAutorizacionJson = "[{\"Tipo\":7,\"Descripcion\":\"Referencias\"}]";
        VentaService.ExigirAutorizacionDeReferencias(venta, validacion);

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

    // ----------------------------------------------------- Edit: invalidación

    [Fact]
    public async Task Editar_ConAutorizacionPorReferencias_ElCambioDeMontoLaInvalidaYVuelveAPedirla()
    {
        await SembrarAsync();
        var creada = await _svc.CreateAsync(NuevaVenta());
        ComoUsuario(Autorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Excepción para esta venta");
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
        await _svc.AutorizarVentaAsync(creada.Id, Autorizador, "Excepción para esta venta");
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
}
