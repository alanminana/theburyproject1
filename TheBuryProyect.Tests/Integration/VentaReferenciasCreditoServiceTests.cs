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
    private static readonly TipoRazonAutorizacion[] Refs = { TipoRazonAutorizacion.ReferenciasInsuficientes };
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
    private async Task SembrarAsync(int minimo = 1, bool mora = false, int bcra = 1, bool cupo = false, decimal limite = 1_000_000m, int? diasNoApto = null)
    {
        _ctx.Set<ConfiguracionCredito>().Add(new ConfiguracionCredito
        {
            ReferenciasVerificadasMinimas = minimo,
            ValidarDocumentacion = false,
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

        // En dos actos. Autorizar una razón modifica el registro de la venta: quien ya la autorizó parcialmente no puede,
        // después, ser el segundo usuario de las referencias (la excepción exige alguien que no haya tocado la venta).
        await _svc.AutorizarVentaAsync(id, Autorizador, "Primero la mora", SoloMora);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, (await VentaAsync(id)).EstadoAutorizacion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AutorizarVentaAsync(id, Autorizador, "Ahora las referencias", Refs));

        const string OtroAutorizador = "gerente.tres";
        ComoUsuario(OtroAutorizador, autoriza: true);
        await _svc.AutorizarVentaAsync(id, OtroAutorizador, "Ahora las referencias", Refs);

        var venta = await VentaAsync(id);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, venta.EstadoAutorizacion);
        Assert.Equal(OtroAutorizador, venta.UsuarioAutoriza);
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
}
