using AutoMapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Helpers;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Documentos;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

file sealed class DocWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "TheBuryProyect.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = Path.GetTempPath();
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), $"doc-flujos-{Guid.NewGuid():N}");
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

file sealed class DocCreditoDisponibleStub : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(int puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

/// <summary>Contrato legado (gate de crédito personal) + motor documental: una sola emisión, una sola numeración.</summary>
public class DocumentoContratoLegadoIntegracionTests : DocumentoTestBase
{
    private ContratoVentaCreditoService CrearServicioLegado(bool conMotor = true)
        => new(
            Context, new FinancialCalculationService(), new StubConfiguracionPagoServiceVenta(), new DocWebHostEnvironment(),
            NullLogger<ContratoVentaCreditoService>.Instance,
            conMotor ? Motor : null,
            conMotor ? new DocumentoNumeracionService(Context, NullLogger<DocumentoNumeracionService>.Instance) : null);

    [Fact]
    public async Task GenerarAsync_EmiteContratoYPagareDocumentales_ConLosMismosNumerosQueElContratoLegado()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var servicio = CrearServicioLegado();

        var legado = await servicio.GenerarAsync(venta.Id, "tester");

        var docs = await Context.DocumentosGenerados.Include(d => d.TipoDocumento).Where(d => d.VentaId == venta.Id).ToListAsync();
        Assert.Equal(2, docs.Count);
        Assert.Equal(legado.NumeroContrato, docs.Single(d => d.TipoDocumento.Codigo == "CONTRATO").Numero);
        Assert.Equal(legado.NumeroPagare, docs.Single(d => d.TipoDocumento.Codigo == "PAGARE").Numero);
        Assert.All(docs, d => Assert.Equal(EventosDocumentales.ContratoCreditoSolicitado, d.EventoOrigen));
        Assert.All(docs, d => Assert.Null(d.ContratoLegadoId));
    }

    [Fact]
    public async Task GenerarAsync_EsIdempotente_NoDuplicaNiContratoNiDocumentos()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var servicio = CrearServicioLegado();

        var primero = await servicio.GenerarAsync(venta.Id, "tester");
        var segundo = await servicio.GenerarAsync(venta.Id, "tester");

        Assert.Equal(primero.Id, segundo.Id);
        Assert.Equal(1, await Context.ContratosVentaCredito.CountAsync());
        Assert.Equal(2, await Context.DocumentosGenerados.CountAsync());
    }

    [Fact]
    public async Task GenerarAsync_ConDocumentoObligatorioFallido_NoDejaContratoNiDocumentos()
    {
        await SembrarConfiguracionInicialAsync();
        await DesactivarReglasSembradasAsync(EventosDocumentales.ContratoCreditoSolicitado);

        var tipo = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "CONTRATO");
        var plantilla = await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            TipoDocumentoId = tipo.Id, Codigo = "CONTRATO_REQ", Nombre = "Requiere fiador",
            Contenido = "{{fiador.nombreCompleto}}", VariablesRequeridas = "fiador.nombreCompleto"
        });
        await CrearReglaAsync("Obligatoria", EventosDocumentales.ContratoCreditoSolicitado, CondCredito, plantilla, obligatoria: true);

        var venta = await SembrarVentaAsync(conFiador: false);
        var servicio = CrearServicioLegado();

        var ex = await Assert.ThrowsAsync<ContratoVentaCreditoValidacionException>(() => servicio.GenerarAsync(venta.Id, "tester"));

        Assert.Contains("fiador.nombreCompleto", ex.Message);
        Context.ChangeTracker.Clear();
        Assert.Empty(await Context.ContratosVentaCredito.ToListAsync());
        Assert.Empty(await Context.DocumentosGenerados.ToListAsync());
    }

    [Fact]
    public async Task GenerarPdfAsync_ImprimeElContenidoDelDocumento_ElGateDeConfirmacionSigueFuncionando()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var servicio = CrearServicioLegado();

        Assert.False(await servicio.ExisteContratoGeneradoAsync(venta.Id));
        var contrato = await servicio.GenerarPdfAsync(venta.Id, "tester");

        Assert.True(await servicio.ExisteContratoGeneradoAsync(venta.Id));
        var pdf = await servicio.ObtenerPdfAsync(venta.Id);
        Assert.NotNull(pdf);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf!.Contenido, 0, 4));
        Assert.Equal(contrato.NumeroContrato, (await Context.DocumentosGenerados.Include(d => d.TipoDocumento)
            .FirstAsync(d => d.VentaId == venta.Id && d.TipoDocumento.Codigo == "CONTRATO")).Numero);
    }

    [Fact]
    public async Task ReiniciarLaApp_NoDuplicaNiChocaConLosContratosYaEmitidosPorElMotor()
    {
        // Cada arranque corre la migración de contratos legados: un contrato emitido por el motor (que
        // reutiliza su numeración) no debe volver a migrarse ni romper el índice único (Tipo, Número).
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        await CrearServicioLegado().GenerarAsync(venta.Id, "tester");
        var antes = await Context.DocumentosGenerados.CountAsync();

        await DocumentoSeeder.EnsureAsync(Context, NullLogger.Instance);
        await DocumentoSeeder.EnsureAsync(Context, NullLogger.Instance);

        Assert.Equal(antes, await Context.DocumentosGenerados.CountAsync());
        Assert.Equal(2, antes);
    }

    [Fact]
    public async Task ConElSistemaDocumentalVacio_ElContratoAnteriorSigueGenerandoseYImprimiendose()
    {
        // Sistema limpiado: sin tipos, plantillas, reglas ni paquetes. El contrato legado y su gate funcionan igual,
        // con la numeración y el texto de su propia plantilla, y el motor no emite nada.
        Context.PlantillasContratoCredito.Add(new PlantillaContratoCredito
        {
            Nombre = "Plantilla legada", Activa = true, NombreVendedor = "The Bury", DomicilioVendedor = "X", CiudadFirma = "C",
            Jurisdiccion = "J", InteresMoraDiarioPorcentaje = 1m, TextoContrato = "contrato {{COMPRADOR_NOMBRE}}",
            TextoPagare = "pagare {{PRECIO_TOTAL}}", VigenteDesde = DateTime.UtcNow.Date.AddDays(-1)
        });
        await Context.SaveChangesAsync();
        var venta = await SembrarVentaAsync();
        var servicio = CrearServicioLegado();

        var contrato = await servicio.GenerarPdfAsync(venta.Id, "tester");

        Assert.StartsWith("CVC-", contrato.NumeroContrato);
        Assert.True(await servicio.ExisteContratoGeneradoAsync(venta.Id));
        Assert.Empty(await Context.DocumentosGenerados.ToListAsync());
        Assert.Empty(await Context.TiposDocumento.ToListAsync());
    }

    [Fact]
    public async Task SinMotorDocumental_ElFlujoLegadoSigueIgual()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var servicio = CrearServicioLegado(conMotor: false);

        var contrato = await servicio.GenerarAsync(venta.Id, "tester");

        Assert.StartsWith("CVC-", contrato.NumeroContrato);
        Assert.Empty(await Context.DocumentosGenerados.ToListAsync());
    }

    [Fact]
    public async Task EditarLaPlantillaLegada_RegistraUnaVersionNuevaEnLaPlantillaEstandar()
    {
        await SembrarConfiguracionInicialAsync();
        var legada = await Context.PlantillasContratoCredito.FirstAsync();
        var venta = await SembrarVentaAsync();
        var servicio = CrearServicioLegado();
        var antes = (await servicio.GenerarAsync(venta.Id, "tester"));

        var servicioPlantilla = new PlantillaContratoCreditoService(Context, NullLogger<PlantillaContratoCreditoService>.Instance);
        var modelo = await servicioPlantilla.ObtenerParaEdicionAsync();
        modelo.TextoPagare = "PAGARE ACTUALIZADO {{documento.numero}}";
        await servicioPlantilla.GuardarAsync(modelo);

        var pagare = await PlantillaAsync(DocumentoSeeder.CodPagareEstandar);
        Assert.Equal(2, pagare.VersionActual);
        var v1 = await Context.PlantillasDocumentoVersion.FirstAsync(v => v.PlantillaDocumentoId == pagare.Id && v.Numero == 1);
        Assert.DoesNotContain("ACTUALIZADO", v1.Contenido);                    // la versión anterior no se toca
        Assert.NotNull(antes);
        _ = legada;
    }
}

/// <summary>Cobranza real (CreditoService) generando el recibo dentro de la misma transacción del pago.</summary>
public class DocumentoCobranzaIntegracionTests : DocumentoTestBase
{
    private static readonly RelojComercialFake Reloj = new(new DateOnly(2026, 6, 15));

    private CreditoService CrearCreditoService()
    {
        Context.Cajas.Add(new Caja { Id = 1, Codigo = "C1", Nombre = "Caja test" });
        Context.AperturasCaja.Add(new AperturaCaja { Id = 1, CajaId = 1, MontoInicial = 0m, UsuarioApertura = "tester", Cerrada = false });
        Context.SaveChanges();

        return new CreditoService(
            Context,
            new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper(),
            NullLogger<CreditoService>.Instance,
            new FinancialCalculationService(),
            new StubCajaServicePagoSeguro(Context),
            new DocCreditoDisponibleStub(),
            new DocUserStub(),
            configuracionPagoService: new StubConfiguracionPagoAjuste(),
            reloj: Reloj,
            documentoService: Motor);
    }

    private async Task<(Credito Credito, Cuota Cuota)> SembrarCreditoConCuotaAsync(bool conVenta)
    {
        var cliente = new Cliente
        {
            Nombre = "Ana", Apellido = "Lopez", TipoDocumento = "DNI", NumeroDocumento = $"D{Guid.NewGuid():N}"[..9],
            Domicilio = "Calle 1", Email = "ana@test.local"
        };
        var credito = new Credito
        {
            Cliente = cliente, Numero = $"CRE-{Guid.NewGuid():N}"[..10], Estado = EstadoCredito.Activo,
            MontoSolicitado = 600m, MontoAprobado = 600m, SaldoPendiente = 600m, TasaInteres = 0m,
            CantidadCuotas = 2, FechaSolicitud = DateTime.UtcNow
        };
        var cuota = new Cuota
        {
            Credito = credito, NumeroCuota = 1, MontoCapital = 300m, MontoInteres = 0m, MontoTotal = 300m,
            Estado = EstadoCuota.Pendiente, FechaVencimiento = Reloj.HoyComercial.ToDateTime(TimeOnly.MinValue)
        };
        Context.Cuotas.Add(cuota);
        if (conVenta)
        {
            Context.Ventas.Add(new Venta
            {
                Numero = $"VTA-{Guid.NewGuid():N}"[..14], Cliente = cliente, Credito = credito, TipoPago = TipoPago.CreditoPersonal,
                Estado = EstadoVenta.Confirmada, Subtotal = 600m, Total = 600m
            });
        }
        await Context.SaveChangesAsync();
        return (credito, cuota);
    }

    private static PagoCuotaIndividualComando Comando(Cuota c, decimal monto)
        => new(c.Id, monto, "Efectivo", "COMP", "obs", c.RowVersion.ToArray());

    [Fact]
    public async Task PagarCuota_GeneraReciboAsociadoAlPagoLaCuotaYElCredito()
    {
        await SembrarConfiguracionInicialAsync();
        var (credito, cuota) = await SembrarCreditoConCuotaAsync(conVenta: true);
        var servicio = CrearCreditoService();

        var resultado = await servicio.RegistrarPagoCuotaIndividualAsync(Comando(cuota, 300m));

        Assert.NotNull(resultado);
        var recibo = Assert.Single(await Context.DocumentosGenerados.Include(d => d.TipoDocumento).ToListAsync());
        Assert.Equal("RECIBO", recibo.TipoDocumento.Codigo);
        Assert.Equal(resultado!.PagoCuotaId, recibo.PagoCuotaId);
        Assert.Equal(cuota.Id, recibo.CuotaId);
        Assert.Equal(credito.Id, recibo.CreditoId);
        Assert.Contains("Lopez", recibo.ContenidoRenderizado);
        Assert.Contains("Trescientos pesos con 00/100", recibo.ContenidoRenderizado);
        // El pago no generó contrato ni pagaré.
        Assert.DoesNotContain(await Context.DocumentosGenerados.Include(d => d.TipoDocumento).ToListAsync(),
            d => d.TipoDocumento.Codigo is "CONTRATO" or "PAGARE");
    }

    [Fact]
    public async Task PagarCuota_ConReciboObligatorioQueFalla_RevierteElCobroCompleto()
    {
        await SembrarConfiguracionInicialAsync();
        await DesactivarReglasSembradasAsync(EventosDocumentales.PagoRegistrado);

        var tipo = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "RECIBO");
        var plantilla = await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            TipoDocumentoId = tipo.Id, Codigo = "RECIBO_EXIGENTE", Nombre = "Recibo exigente",
            Contenido = "Recibo {{venta.numero}}", VariablesRequeridas = "venta.numero"
        });
        await CrearReglaAsync("Recibo obligatorio", EventosDocumentales.PagoRegistrado, null, plantilla, obligatoria: true);

        var (_, cuota) = await SembrarCreditoConCuotaAsync(conVenta: false);   // sin venta => falta venta.numero
        var servicio = CrearCreditoService();

        await Assert.ThrowsAsync<DocumentoObligatorioFallidoException>(
            () => servicio.RegistrarPagoCuotaIndividualAsync(Comando(cuota, 300m)));

        Context.ChangeTracker.Clear();
        Assert.Empty(await Context.PagosCuota.ToListAsync());
        Assert.Empty(await Context.DocumentosGenerados.ToListAsync());
        var cuotaDb = await Context.Cuotas.AsNoTracking().FirstAsync(c => c.Id == cuota.Id);
        Assert.Equal(0m, cuotaDb.MontoPagado);
        Assert.Equal(EstadoCuota.Pendiente, cuotaDb.Estado);
    }

    [Fact]
    public async Task PagarCuota_ConReciboNoObligatorioQueFalla_ElCobroSeCompleta()
    {
        await SembrarConfiguracionInicialAsync();
        await DesactivarReglasSembradasAsync(EventosDocumentales.PagoRegistrado);

        var tipo = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "RECIBO");
        var plantilla = await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            TipoDocumentoId = tipo.Id, Codigo = "RECIBO_EXIGENTE", Nombre = "Recibo exigente",
            Contenido = "Recibo {{venta.numero}}", VariablesRequeridas = "venta.numero"
        });
        await CrearReglaAsync("Recibo opcional", EventosDocumentales.PagoRegistrado, null, plantilla, obligatoria: false);

        var (_, cuota) = await SembrarCreditoConCuotaAsync(conVenta: false);
        var servicio = CrearCreditoService();

        var resultado = await servicio.RegistrarPagoCuotaIndividualAsync(Comando(cuota, 300m));

        Assert.NotNull(resultado);
        Assert.Single(await Context.PagosCuota.ToListAsync());                 // el pago quedó
        Assert.Empty(await Context.DocumentosGenerados.ToListAsync());          // sin recibo: error recuperable
        // Se puede reintentar cuando los datos estén completos (idempotente).
        var reintento = await Motor.ReintentarEventoAsync(EventosDocumentales.PagoRegistrado, new DocumentoOrigen { PagoCuotaId = resultado!.PagoCuotaId });
        Assert.Single(reintento.Errores);
    }
}

public class DocumentoConfiguracionServiceTests : DocumentoTestBase
{
    private async Task<TipoDocumento> TipoAsync(string codigo = "RECIBO")
    {
        await SembrarConfiguracionInicialAsync();
        return await Context.TiposDocumento.FirstAsync(t => t.Codigo == codigo);
    }

    [Fact]
    public async Task GuardarPlantilla_RechazaScriptsVariablesDesconocidasYSeccionesRotas()
    {
        var tipo = await TipoAsync();
        PlantillaDocumentoInput Base(string contenido, string? req = null) => new()
        {
            TipoDocumentoId = tipo.Id, Codigo = "TPL_X", Nombre = "X", Contenido = contenido, VariablesRequeridas = req
        };

        var ex1 = await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarPlantillaAsync(Base("<script>alert(1)</script>")));
        Assert.Contains("scripts", ex1.Message);
        var ex2 = await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarPlantillaAsync(Base("Hola {{no.existe}}")));
        Assert.Contains("desconocida", ex2.Message);
        var ex3 = await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarPlantillaAsync(Base("{{#cuotas}} sin cierre")));
        Assert.Contains("sin cerrar", ex3.Message);
        var ex4 = await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarPlantillaAsync(Base("ok", "variable.inventada")));
        Assert.Contains("no existe", ex4.Message);
        Assert.Empty(await Context.PlantillasDocumento.Where(p => p.Codigo == "TPL_X").ToListAsync());
    }

    [Fact]
    public async Task GuardarPlantilla_SoloCreaVersionNuevaCuandoCambiaElContenido()
    {
        var tipo = await TipoAsync();
        var p = await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            TipoDocumentoId = tipo.Id, Codigo = "TPL_V", Nombre = "V", Contenido = "uno {{cliente.nombre}}"
        });
        Assert.Equal(1, p.VersionActual);

        // Mismo contenido con otro nombre: no cambia la versión.
        p = await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            Id = p.Id, TipoDocumentoId = tipo.Id, Codigo = "TPL_V", Nombre = "V renombrada", VigenteDesde = p.VigenteDesde, Contenido = "uno {{cliente.nombre}}"
        });
        Assert.Equal(1, p.VersionActual);

        p = await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            Id = p.Id, TipoDocumentoId = tipo.Id, Codigo = "TPL_V", Nombre = "V renombrada", VigenteDesde = p.VigenteDesde, Contenido = "dos {{cliente.nombre}}"
        });
        Assert.Equal(2, p.VersionActual);

        var versiones = await Context.PlantillasDocumentoVersion.Where(v => v.PlantillaDocumentoId == p.Id).OrderBy(v => v.Numero).ToListAsync();
        Assert.Equal(new[] { "uno {{cliente.nombre}}", "dos {{cliente.nombre}}" }, versiones.Select(v => v.Contenido));

        var restaurada = await Config.RestaurarVersionAsync(p.Id, 1);
        Assert.Equal(3, restaurada.VersionActual);
        Assert.Equal("uno {{cliente.nombre}}", (await Config.ObtenerVersionVigenteAsync(p.Id))!.Contenido);
        Assert.Equal(3, await Context.PlantillasDocumentoVersion.CountAsync(v => v.PlantillaDocumentoId == p.Id));   // ninguna se borró
    }

    [Fact]
    public async Task GuardarPlantilla_RechazaCodigoDuplicadoYCopiasFueraDeRango()
    {
        var tipo = await TipoAsync();
        var input = new PlantillaDocumentoInput { TipoDocumentoId = tipo.Id, Codigo = "TPL_D", Nombre = "D", Contenido = "x" };
        await Config.GuardarPlantillaAsync(input);

        await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarPlantillaAsync(
            new PlantillaDocumentoInput { TipoDocumentoId = tipo.Id, Codigo = "tpl_d", Nombre = "D2", Contenido = "x" }));
        await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarPlantillaAsync(
            new PlantillaDocumentoInput { TipoDocumentoId = tipo.Id, Codigo = "TPL_E", Nombre = "E", Contenido = "x", Copias = 9 }));
    }

    [Fact]
    public async Task GuardarRegla_ValidaEventoCondicionesYDestino()
    {
        await SembrarConfiguracionInicialAsync();
        var plantilla = await PlantillaAsync(DocumentoSeeder.CodReciboEstandar);

        // Campo de pago en un evento de venta: no estaría disponible en el contexto.
        var ex1 = await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarReglaAsync(new ReglaDocumentoInput
        {
            Nombre = "mala", EventoCodigo = EventosDocumentales.VentaConfirmada,
            CondicionJson = """{"campo":"pago.importe","operador":"mayor","valor":0}""", PlantillaDocumentoId = plantilla.Id
        }));
        Assert.Contains("no está disponible", ex1.Message);

        await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarReglaAsync(new ReglaDocumentoInput
        {
            Nombre = "sin destino", EventoCodigo = EventosDocumentales.PagoRegistrado
        }));
        await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarReglaAsync(new ReglaDocumentoInput
        {
            Nombre = "evento falso", EventoCodigo = "NO_EXISTE", PlantillaDocumentoId = plantilla.Id
        }));
        await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarReglaAsync(new ReglaDocumentoInput
        {
            Nombre = "json roto", EventoCodigo = EventosDocumentales.PagoRegistrado, CondicionJson = "{no", PlantillaDocumentoId = plantilla.Id
        }));
    }

    [Fact]
    public async Task Paquete_AgrupaPlantillasEnOrden_YSeUsaDesdeUnaRegla()
    {
        await SembrarConfiguracionInicialAsync();
        var contrato = await PlantillaAsync(DocumentoSeeder.CodContratoEstandar);
        var pagare = await PlantillaAsync(DocumentoSeeder.CodPagareEstandar);

        var paquete = await Config.GuardarPaqueteAsync(new PaqueteDocumentalInput
        {
            Codigo = "CREDITO_X", Nombre = "Crédito X", PlantillaIds = new List<int> { pagare.Id, contrato.Id }
        });

        var cargado = await Config.ObtenerPaqueteAsync(paquete.Id);
        Assert.Equal(new[] { pagare.Id, contrato.Id }, cargado!.Items.OrderBy(i => i.Orden).Select(i => i.PlantillaDocumentoId));

        await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarPaqueteAsync(
            new PaqueteDocumentalInput { Codigo = "VACIO", Nombre = "Vacío" }));
    }

    [Fact]
    public async Task Tipos_ValidanCodigoPrefijoYUnicidad()
    {
        await SembrarConfiguracionInicialAsync();

        await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarTipoAsync(new TipoDocumentoInput { Codigo = "con espacios", Nombre = "X", Prefijo = "XX" }));
        await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarTipoAsync(new TipoDocumentoInput { Codigo = "RECIBO", Nombre = "Dup", Prefijo = "XX" }));
        await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarTipoAsync(new TipoDocumentoInput { Codigo = "NUEVO", Nombre = "N", Prefijo = "a-b" }));

        var ok = await Config.GuardarTipoAsync(new TipoDocumentoInput { Codigo = "nuevo", Nombre = "Nuevo", Prefijo = "nu" });
        Assert.Equal("NUEVO", ok.Codigo);
        Assert.Equal("NU", ok.Prefijo);
    }

    [Fact]
    public async Task Preview_ConDatosDeEjemplo_YConOperacionReal_InformaFaltantes()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync(conFiador: false);

        var ejemplo = await Config.PrevisualizarAsync("Hola {{cliente.nombreCompleto}} {{fiador.nombreCompleto}}", "fiador.nombreCompleto",
            EventosDocumentales.ContratoCreditoSolicitado);
        Assert.False(ejemplo.UsaDatosReales);
        Assert.True(ejemplo.EsGenerable);                                       // el ejemplo trae fiador

        var real = await Config.PrevisualizarAsync("Hola {{cliente.nombreCompleto}} {{fiador.nombreCompleto}}", "fiador.nombreCompleto",
            EventosDocumentales.ContratoCreditoSolicitado, ventaId: venta.Id);
        Assert.True(real.UsaDatosReales);
        Assert.Contains("Perez", real.Texto);
        Assert.Contains("fiador.nombreCompleto", real.RequeridasFaltantes);     // no permite emitir un definitivo
        Assert.False(real.EsGenerable);

        var invalida = await Config.PrevisualizarAsync("{{inventada}}", null, EventosDocumentales.PagoRegistrado);
        Assert.NotEmpty(invalida.ErroresPlantilla);
    }
}

file sealed class DocContextoBuilderQueFalla : IDocumentoContextoBuilder
{
    public Task<DocumentoContexto> ConstruirAsync(string evento, DocumentoOrigen origen)
        => throw new InvalidOperationException("fallo inesperado de consulta");
}

public class DocumentoRobustezTests : DocumentoTestBase
{
    private DocumentoService MotorConBuilderRoto() => new(
        Context, new DocContextoBuilderQueFalla(),
        new DocumentoNumeracionService(Context, NullLogger<DocumentoNumeracionService>.Instance),
        new DocumentoPdfService(), new DocUserStub(), Auditoria, RelojComercial.Sistema,
        NullLogger<DocumentoService>.Instance);

    [Fact]
    public async Task ErrorInesperadoAlEvaluar_NoBloqueaElFlujo_SiNingunaReglaEsObligatoria()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var pago = await SembrarPagoAsync(venta);

        // PAGO_REGISTRADO: el recibo sembrado no es obligatorio.
        var r = await MotorConBuilderRoto().ProcesarEventoAsync(new DocumentoEventoRequest
        {
            Evento = EventosDocumentales.PagoRegistrado, Origen = new DocumentoOrigen { PagoCuotaId = pago.Id }
        });

        var error = Assert.Single(r.Errores);
        Assert.Equal("DOCUMENT_ENGINE_ERROR", error.Codigo);
        Assert.False(error.Obligatoria);
        Assert.Empty(r.Generados);
    }

    [Fact]
    public async Task ErrorInesperadoAlEvaluar_ImpideElPaso_SiHayUnaReglaObligatoria()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();

        // CONTRATO_CREDITO_SOLICITADO: la regla sembrada es obligatoria.
        var ex = await Assert.ThrowsAsync<DocumentoObligatorioFallidoException>(() =>
            MotorConBuilderRoto().ProcesarEventoAsync(new DocumentoEventoRequest
            {
                Evento = EventosDocumentales.ContratoCreditoSolicitado, Origen = new DocumentoOrigen { VentaId = venta.Id }
            }));

        Assert.Contains(ex.Errores, e => e.Codigo == "DOCUMENT_ENGINE_ERROR" && e.Obligatoria);
    }

    [Fact]
    public async Task ContextoDePago_IncluyeVentaCreditoCuotasYFiador_SinCiclosDeInclude()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync(conFiador: true, cuotas: 4);
        var pago = await SembrarPagoAsync(venta);

        var ctx = await new DocumentoContextoBuilder(Context).ConstruirAsync(
            EventosDocumentales.PagoRegistrado, new DocumentoOrigen { PagoCuotaId = pago.Id });

        Assert.Equal(venta.Numero, ctx.Valores["venta.numero"]);
        Assert.Equal("CreditoPersonal", ctx.Valores["venta.tipoPago"]);
        Assert.Equal(true, ctx.Valores["credito.requiereFiador"]);
        Assert.Equal(true, ctx.Valores["fiador.existe"]);
        Assert.Equal(4, ctx.Colecciones["cuotas"].Count);
        Assert.Single(ctx.Colecciones["productos"]);
        Assert.Equal(300m, ctx.Valores["pago.importe"]);
        Assert.Equal(1, ctx.Valores["cuota.numero"]);
    }

    [Fact]
    public async Task ReglaDeReciboConCondicionSobreLaVenta_FuncionaEnEventosDePago()
    {
        await SembrarConfiguracionInicialAsync();
        await DesactivarReglasSembradasAsync(EventosDocumentales.PagoRegistrado);
        var recibo = await PlantillaAsync(DocumentoSeeder.CodReciboEstandar);
        await CrearReglaAsync("Recibo solo ventas a crédito", EventosDocumentales.PagoRegistrado,
            """{"campo":"venta.tipoPago","operador":"igual","valor":"CreditoPersonal"}""", recibo);
        var venta = await SembrarVentaAsync();
        var pago = await SembrarPagoAsync(venta);

        var r = await EmitirPagoAsync(pago.Id);

        Assert.Single(r.Generados);
    }
}

public class DocumentoRegeneracionLegadoTests : DocumentoTestBase
{
    [Fact]
    public async Task Regenerar_UnDocumentoImportadoDelSistemaAnterior_UsaLaPlantillaActivaDeSuTipo()
    {
        var venta = await SembrarVentaAsync();
        Context.PlantillasContratoCredito.Add(new PlantillaContratoCredito
        {
            Nombre = "Legacy", Activa = true, NombreVendedor = "The Bury", DomicilioVendedor = "X", CiudadFirma = "C", Jurisdiccion = "J",
            InteresMoraDiarioPorcentaje = 1m, TextoContrato = "viejo contrato {{documento.numero}}", TextoPagare = "viejo pagare",
            VigenteDesde = DateTime.UtcNow.Date.AddDays(-1)
        });
        await Context.SaveChangesAsync();
        var plantillaLegada = await Context.PlantillasContratoCredito.FirstAsync();

        var snapshot = """
        {"Vendedor":{"Nombre":"The Bury","Domicilio":"X","CiudadFirma":"C","Jurisdiccion":"J","InteresMoraDiarioPorcentaje":1},
         "Comprador":{"NombreCompleto":"Perez, Juan","DNI":"1","Domicilio":"D","Localidad":"L","Telefono":"T"},
         "Venta":{"Numero":"V1","Fecha":"2026-01-01T00:00:00","Total":900,"Productos":[]},
         "Credito":{"Numero":"C1","CantidadCuotas":3,"MontoCuota":300,"TotalAPagar":900,"FechaPrimeraCuota":"2026-02-01T00:00:00","PlanCuotas":[]},
         "Contrato":{"Numero":"CVC-202601-000001","NumeroPagare":"PAG-202601-000001","FechaEmision":"2026-01-01T00:00:00"},
         "UsuarioGeneracion":"u"}
        """;
        Context.ContratosVentaCredito.Add(new ContratoVentaCredito
        {
            VentaId = venta.Id, CreditoId = venta.CreditoId!.Value, ClienteId = venta.ClienteId!.Value,
            PlantillaContratoCreditoId = plantillaLegada.Id, NumeroContrato = "CVC-202601-000001", NumeroPagare = "PAG-202601-000001",
            UsuarioGeneracion = "u", TextoContratoSnapshot = "Contrato de {{COMPRADOR_NOMBRE}}", TextoPagareSnapshot = "Pagaré",
            DatosSnapshotJson = snapshot
        });
        await Context.SaveChangesAsync();
        await DocumentoSeeder.EnsureAsync(Context, NullLogger.Instance);

        var importado = await Context.DocumentosGenerados.Include(d => d.TipoDocumento)
            .FirstAsync(d => d.ContratoLegadoId != null && d.TipoDocumento.Codigo == "CONTRATO");

        var nuevo = await Motor.RegenerarAsync(importado.Id, "Actualizar al modelo vigente");

        var plantilla = await Context.PlantillasDocumento.FirstAsync(p => p.Id == nuevo.PlantillaDocumentoId);
        Assert.Equal(DocumentoSeeder.CodContratoEstandar, plantilla.Codigo);
        Assert.Contains("viejo contrato " + nuevo.Numero, nuevo.ContenidoRenderizado);
        Context.ChangeTracker.Clear();
        Assert.Equal(EstadoDocumentoGenerado.Reemplazado, (await Context.DocumentosGenerados.FirstAsync(d => d.Id == importado.Id)).Estado);
        Assert.Contains("Perez, Juan", (await Context.DocumentosGenerados.FirstAsync(d => d.Id == importado.Id)).ContenidoRenderizado);
    }
}
