using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Documentos;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>Usuario fijo para los tests del motor documental.</summary>
internal sealed class DocUserStub : ICurrentUserService
{
    public string GetUsername() => "tester";
    public string GetUserId() => "tester-id";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "tester@test.local";
    public bool IsInRole(string role) => true;
    public bool HasPermission(string modulo, string accion) => true;
    public string? GetIpAddress() => "127.0.0.1";
}

internal sealed class DocAuditoriaStub : ISeguridadAuditoriaService
{
    public List<(string Modulo, string Accion, string Entidad, string? Detalle)> Eventos { get; } = new();

    public Task RegistrarEventoAsync(string modulo, string accion, string entidad, string? detalle = null)
    {
        Eventos.Add((modulo, accion, entidad, detalle));
        return Task.CompletedTask;
    }

    public Task<AuditoriaQueryResult> ConsultarEventosAsync(string? usuario = null, string? modulo = null, string? accion = null,
        DateOnly? desde = null, DateOnly? hasta = null, int skip = 0, int? take = null)
        => Task.FromResult(new AuditoriaQueryResult());
}

/// <summary>Base con SQLite en memoria, los servicios documentales reales y datos de venta/pago de prueba.</summary>
public abstract class DocumentoTestBase : IDisposable
{
    protected readonly SqliteConnection Connection;
    protected readonly AppDbContext Context;
    internal readonly DocAuditoriaStub Auditoria = new();
    protected readonly DocumentoService Motor;
    protected readonly DocumentoConfiguracionService Config;

    protected DocumentoTestBase()
    {
        Connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        Connection.Open();

        Context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);
        Context.Database.EnsureCreated();

        var builder = new DocumentoContextoBuilder(Context);
        Motor = new DocumentoService(
            Context, builder, new DocumentoNumeracionService(Context, NullLogger<DocumentoNumeracionService>.Instance),
            new DocumentoPdfService(), new DocUserStub(), Auditoria, RelojComercial.Sistema,
            NullLogger<DocumentoService>.Instance);
        Config = new DocumentoConfiguracionService(Context, builder, Auditoria, NullLogger<DocumentoConfiguracionService>.Instance);
    }

    public void Dispose()
    {
        Context.Dispose();
        Connection.Dispose();
    }

    /// <summary>Plantilla de contrato legada + configuración inicial (tipos, plantillas, paquete y reglas).</summary>
    protected async Task SembrarConfiguracionInicialAsync()
    {
        Context.PlantillasContratoCredito.Add(new PlantillaContratoCredito
        {
            Nombre = "Plantilla base",
            Activa = true,
            NombreVendedor = "The Bury SA",
            DomicilioVendedor = "Av. Siempre Viva 742",
            CuitVendedor = "30-12345678-9",
            CiudadFirma = "Rosario",
            Jurisdiccion = "Santa Fe",
            InteresMoraDiarioPorcentaje = 0.5m,
            TextoContrato = "CONTRATO {{documento.numero}} entre {{VENDEDOR_NOMBRE}} y {{COMPRADOR_NOMBRE}} ({{COMPRADOR_DNI}}). Cuotas: {{CANTIDAD_CUOTAS}}.\n{{#cuotas}}\nCuota {{numero}} vence {{vencimiento}} por {{importe}}\n{{/cuotas}}",
            TextoPagare = "PAGARE {{documento.numero}} por {{PRECIO_TOTAL}}. Contrato asociado {{documento.numeroContrato}}.",
            VigenteDesde = DateTime.UtcNow.Date.AddDays(-1)
        });
        await Context.SaveChangesAsync();
        await DocumentoSeeder.EnsureAsync(Context, NullLogger.Instance);
    }

    protected Task<PlantillaDocumento> PlantillaAsync(string codigo)
        => Context.PlantillasDocumento.Include(p => p.TipoDocumento).FirstAsync(p => p.Codigo == codigo);

    protected async Task DesactivarReglasSembradasAsync(string evento)
    {
        foreach (var r in await Context.ReglasDocumento.Where(r => r.EventoCodigo == evento).ToListAsync())
            r.Activa = false;
        await Context.SaveChangesAsync();
    }

    protected async Task<ReglaDocumento> CrearReglaAsync(
        string nombre, string evento, string? condicionJson, PlantillaDocumento? plantilla = null,
        PaqueteDocumental? paquete = null, int prioridad = 100, bool obligatoria = false, string? grupo = null)
    {
        return await Config.GuardarReglaAsync(new ReglaDocumentoInput
        {
            Nombre = nombre,
            EventoCodigo = evento,
            CondicionJson = condicionJson,
            PlantillaDocumentoId = plantilla?.Id,
            PaqueteDocumentalId = paquete?.Id,
            Prioridad = prioridad,
            Obligatoria = obligatoria,
            GrupoExclusion = grupo
        });
    }

    protected const string CondCredito = """{"op":"todas","condiciones":[{"campo":"venta.tipoPago","operador":"igual","valor":"CreditoPersonal"}]}""";
    protected const string CondCreditoSinFiador = """{"op":"todas","condiciones":[{"campo":"venta.tipoPago","operador":"igual","valor":"CreditoPersonal"},{"campo":"credito.requiereFiador","operador":"falso"}]}""";
    protected const string CondCreditoConFiador = """{"op":"todas","condiciones":[{"campo":"venta.tipoPago","operador":"igual","valor":"CreditoPersonal"},{"campo":"credito.requiereFiador","operador":"verdadero"}]}""";

    protected Task<DocumentoEventoResultado> EmitirVentaAsync(int ventaId, string evento = EventosDocumentales.ContratoCreditoSolicitado)
        => Motor.ProcesarEventoAsync(new DocumentoEventoRequest { Evento = evento, Origen = new DocumentoOrigen { VentaId = ventaId } });

    protected Task<DocumentoEventoResultado> EmitirPagoAsync(int pagoId)
        => Motor.ProcesarEventoAsync(new DocumentoEventoRequest
        {
            Evento = EventosDocumentales.PagoRegistrado,
            Origen = new DocumentoOrigen { PagoCuotaId = pagoId }
        });

    // ------------------------------------------------------------------ Datos de prueba

    protected async Task<Venta> SembrarVentaAsync(
        TipoPago tipoPago = TipoPago.CreditoPersonal, bool conFiador = false, int cuotas = 3, string dni = "30111222")
    {
        var cliente = new Cliente
        {
            Nombre = "Juan", Apellido = "Perez", TipoDocumento = "DNI", NumeroDocumento = $"{dni}{Guid.NewGuid():N}"[..10],
            Domicilio = "Calle 123", Localidad = "Rosario", Telefono = "341555"
        };
        var categoria = new Categoria { Nombre = $"Cat {Guid.NewGuid():N}", Codigo = $"C{Guid.NewGuid():N}"[..8] };
        var marca = new Marca { Nombre = $"Marca {Guid.NewGuid():N}", Codigo = $"M{Guid.NewGuid():N}"[..8] };
        var producto = new Producto
        {
            Codigo = $"P{Guid.NewGuid():N}"[..8], Nombre = "Heladera", Categoria = categoria, Marca = marca,
            PrecioCompra = 0, PrecioVenta = 900m, PorcentajeIVA = 21m
        };

        var venta = new Venta
        {
            Numero = $"VTA-{Guid.NewGuid():N}"[..14],
            Cliente = cliente,
            TipoPago = tipoPago,
            Estado = EstadoVenta.Confirmada,
            FechaVenta = DateTime.UtcNow,
            Subtotal = 900m,
            Total = 900m
        };
        venta.Detalles.Add(new VentaDetalle
        {
            Producto = producto, Cantidad = 1, PrecioUnitario = 900m, Subtotal = 900m, SubtotalFinal = 900m
        });

        if (tipoPago == TipoPago.CreditoPersonal)
        {
            var credito = new Credito
            {
                Cliente = cliente, Numero = $"CRE-{Guid.NewGuid():N}"[..10],
                MontoSolicitado = 900m, MontoAprobado = 900m, SaldoPendiente = 900m, TasaInteres = 0m,
                CantidadCuotas = cuotas, MontoCuota = 300m, TotalAPagar = 900m,
                FechaPrimeraCuota = DateTime.UtcNow.Date.AddMonths(1), RequiereGarante = conFiador
            };
            for (var i = 1; i <= cuotas; i++)
                credito.Cuotas.Add(new Cuota
                {
                    NumeroCuota = i, MontoCapital = 300m, MontoInteres = 0m, MontoTotal = 300m,
                    FechaVencimiento = credito.FechaPrimeraCuota!.Value.AddMonths(i - 1)
                });

            if (conFiador)
                credito.Garante = new Garante
                {
                    Cliente = cliente, Nombre = "Maria", Apellido = "Gomez", TipoDocumento = "DNI",
                    NumeroDocumento = "20999888", Domicilio = "Otra 456", Relacion = "Hermana"
                };

            venta.Credito = credito;
        }

        Context.Ventas.Add(venta);
        await Context.SaveChangesAsync();
        return venta;
    }

    protected async Task<PagoCuota> SembrarPagoAsync(Venta venta, decimal importe = 300m)
    {
        var cuota = await Context.Cuotas.OrderBy(c => c.NumeroCuota).FirstAsync(c => c.CreditoId == venta.CreditoId);
        var pago = new PagoCuota
        {
            CuotaId = cuota.Id, FechaPagoComercial = DateOnly.FromDateTime(DateTime.Today), ImporteTotal = importe,
            ImporteAplicadoCuota = importe, ImporteAplicadoPunitorio = 0m, MedioPago = "Efectivo",
            Origen = OrigenPagoCuota.RegistradoPorSistema, Estado = EstadoPagoCuota.Aplicado, HistorialCompleto = true
        };
        Context.PagosCuota.Add(pago);
        await Context.SaveChangesAsync();
        return pago;
    }
}

public class DocumentoMotorTests : DocumentoTestBase
{
    // CASO 1
    [Fact]
    public async Task VentaContado_NoGeneraContratoDeCredito()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync(TipoPago.Efectivo);

        var r = await EmitirVentaAsync(venta.Id);

        Assert.Empty(r.Generados);
        Assert.Empty(await Context.DocumentosGenerados.ToListAsync());
    }

    // CASO 2 + 5
    [Fact]
    public async Task VentaCreditoEstandar_GeneraContratoYPagareComoDocumentosIndependientes()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();

        var r = await EmitirVentaAsync(venta.Id);

        Assert.Equal(2, r.Generados.Count);
        var contrato = r.Generados.Single(d => d.TipoDocumentoId == (Context.TiposDocumento.First(t => t.Codigo == "CONTRATO").Id));
        var pagare = r.Generados.Single(d => d.TipoDocumentoId == (Context.TiposDocumento.First(t => t.Codigo == "PAGARE").Id));

        Assert.NotEqual(contrato.Id, pagare.Id);
        Assert.NotEqual(contrato.Numero, pagare.Numero);
        Assert.StartsWith("CVC-", contrato.Numero);
        Assert.StartsWith("PAG-", pagare.Numero);
        // Cada uno tiene su propia plantilla/versión y su propio ciclo de vida.
        Assert.NotEqual(contrato.PlantillaDocumentoId, pagare.PlantillaDocumentoId);
        Assert.Contains("The Bury SA", contrato.ContenidoRenderizado);        // alias legado {{VENDEDOR_NOMBRE}}
        Assert.Contains("Perez", contrato.ContenidoRenderizado);              // {{COMPRADOR_NOMBRE}}
        Assert.Contains(contrato.Numero, pagare.ContenidoRenderizado);        // {{documento.numeroContrato}}
        Assert.Contains("Cuota 3 vence", contrato.ContenidoRenderizado);      // sección {{#cuotas}}
        Assert.Equal(EstadoDocumentoGenerado.PendienteFirma, contrato.Estado);

        await Motor.CancelarAsync(pagare.Id, "Pagaré emitido por error");
        var contratoDb = await Context.DocumentosGenerados.AsNoTracking().FirstAsync(d => d.Id == contrato.Id);
        Assert.Equal(EstadoDocumentoGenerado.PendienteFirma, contratoDb.Estado);   // el contrato no se ve afectado
    }

    // CASO 3
    [Fact]
    public async Task VentaCreditoConFiador_GeneraContratoConFiador()
    {
        await SembrarConfiguracionInicialAsync();
        await DesactivarReglasSembradasAsync(EventosDocumentales.ContratoCreditoSolicitado);

        var tipoContrato = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "CONTRATO");
        var fiador = await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            TipoDocumentoId = tipoContrato.Id, Codigo = "CONTRATO_FIADOR", Nombre = "Contrato con fiador",
            Contenido = "CONTRATO CON FIADOR {{documento.numero}}. Fiador: {{fiador.nombreCompleto}} DNI {{fiador.dni}}.",
            VariablesRequeridas = "fiador.nombreCompleto", Activa = true
        });
        var estandar = await PlantillaAsync(DocumentoSeeder.CodContratoEstandar);

        await CrearReglaAsync("Estándar", EventosDocumentales.ContratoCreditoSolicitado, CondCreditoSinFiador, estandar);
        await CrearReglaAsync("Con fiador", EventosDocumentales.ContratoCreditoSolicitado, CondCreditoConFiador, fiador);

        var sinFiador = await SembrarVentaAsync(conFiador: false);
        var conFiador = await SembrarVentaAsync(conFiador: true);

        var r1 = await EmitirVentaAsync(sinFiador.Id);
        var r2 = await EmitirVentaAsync(conFiador.Id);

        Assert.Equal(DocumentoSeeder.CodContratoEstandar, (await PlantillaPorIdAsync(r1.Generados.Single().PlantillaDocumentoId)).Codigo);
        var doc2 = r2.Generados.Single();
        Assert.Equal("CONTRATO_FIADOR", (await PlantillaPorIdAsync(doc2.PlantillaDocumentoId)).Codigo);
        Assert.Contains("Gomez", doc2.ContenidoRenderizado);
    }

    private Task<PlantillaDocumento> PlantillaPorIdAsync(int id) => Context.PlantillasDocumento.FirstAsync(p => p.Id == id);

    // CASO 4
    [Fact]
    public async Task ReglaEspecifica_TienePrioridadSobreLaGeneral_DelMismoGrupo()
    {
        await SembrarConfiguracionInicialAsync();
        await DesactivarReglasSembradasAsync(EventosDocumentales.ContratoCreditoSolicitado);

        var tipoContrato = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "CONTRATO");
        var fiador = await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            TipoDocumentoId = tipoContrato.Id, Codigo = "CONTRATO_FIADOR", Nombre = "Contrato con fiador",
            Contenido = "FIADOR {{fiador.nombreCompleto}}", Activa = true
        });
        var estandar = await PlantillaAsync(DocumentoSeeder.CodContratoEstandar);

        // La general también coincide con una venta con fiador, pero comparte grupo con la específica.
        await CrearReglaAsync("General crédito", EventosDocumentales.ContratoCreditoSolicitado, CondCredito, estandar, prioridad: 100, grupo: "contrato");
        await CrearReglaAsync("Crédito con fiador", EventosDocumentales.ContratoCreditoSolicitado, CondCreditoConFiador, fiador, prioridad: 200, grupo: "contrato");

        var venta = await SembrarVentaAsync(conFiador: true);
        var r = await EmitirVentaAsync(venta.Id);

        var doc = Assert.Single(r.Generados);
        Assert.Equal("CONTRATO_FIADOR", (await PlantillaPorIdAsync(doc.PlantillaDocumentoId)).Codigo);
    }

    [Fact]
    public async Task SinGrupoDeExclusion_DosReglasDelMismoTipoNoDuplicanElDocumento()
    {
        // El tipo CONTRATO no admite múltiples: si dos reglas sin grupo coinciden, gana la de mayor prioridad.
        await SembrarConfiguracionInicialAsync();
        await DesactivarReglasSembradasAsync(EventosDocumentales.ContratoCreditoSolicitado);

        var tipoContrato = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "CONTRATO");
        var otra = await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            TipoDocumentoId = tipoContrato.Id, Codigo = "CONTRATO_ESPECIAL", Nombre = "Contrato especial", Contenido = "ESPECIAL", Activa = true
        });
        var estandar = await PlantillaAsync(DocumentoSeeder.CodContratoEstandar);
        await CrearReglaAsync("General", EventosDocumentales.ContratoCreditoSolicitado, CondCredito, estandar, prioridad: 10);
        await CrearReglaAsync("Especial", EventosDocumentales.ContratoCreditoSolicitado, CondCredito, otra, prioridad: 50);

        var venta = await SembrarVentaAsync();
        var r = await EmitirVentaAsync(venta.Id);

        var doc = Assert.Single(r.Generados);
        Assert.Equal("CONTRATO_ESPECIAL", (await PlantillaPorIdAsync(doc.PlantillaDocumentoId)).Codigo);
        Assert.Contains(r.Advertencias, a => a.Contains("Ya existe"));
    }

    // CASO 6
    [Fact]
    public async Task ContratoYPagare_ComparteGrupoDeImpresion_YSePuedenImprimirJuntos()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var r = await EmitirVentaAsync(venta.Id);

        var grupo = Assert.Single(r.Generados.Select(d => d.GrupoImpresionId).Distinct());
        Assert.NotNull(grupo);

        var juntos = await Motor.ObtenerPorGrupoAsync(grupo!.Value);
        Assert.Equal(2, juntos.Count);

        var pdf = await Motor.VerPdfAsync(juntos.Select(d => d.Id).ToList());
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Contenido, 0, 4));

        // Y por separado, cada uno es su propio PDF.
        var solo = await Motor.VerPdfAsync(new[] { juntos[0].Id });
        Assert.True(pdf.Contenido.Length > solo.Contenido.Length);
    }

    // CASO 7 + 8
    [Fact]
    public async Task PagoDeCuota_GeneraRecibo_AsociadoAPagoCuotaYCredito_YNoGeneraContrato()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var pago = await SembrarPagoAsync(venta, 300m);

        var r = await EmitirPagoAsync(pago.Id);

        var recibo = Assert.Single(r.Generados);
        var tipo = await Context.TiposDocumento.FirstAsync(t => t.Id == recibo.TipoDocumentoId);
        Assert.Equal("RECIBO", tipo.Codigo);
        Assert.StartsWith("REC-", recibo.Numero);
        Assert.Equal(pago.Id, recibo.PagoCuotaId);
        Assert.Equal(pago.CuotaId, recibo.CuotaId);
        Assert.Equal(venta.CreditoId, recibo.CreditoId);
        Assert.Equal(venta.Id, recibo.VentaId);
        Assert.Contains("trescientos pesos con 00/100", recibo.ContenidoRenderizado, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Perez", recibo.ContenidoRenderizado);

        // CASO 8: el pago nunca dispara contrato/pagaré.
        Assert.DoesNotContain(await Context.DocumentosGenerados.Include(d => d.TipoDocumento).ToListAsync(),
            d => d.TipoDocumento.Codigo is "CONTRATO" or "PAGARE");
    }

    [Fact]
    public async Task DosPagos_GeneranDosRecibosIndependientes()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var p1 = await SembrarPagoAsync(venta, 300m);
        var p2 = await SembrarPagoAsync(venta, 120m);

        var r1 = await EmitirPagoAsync(p1.Id);
        var r2 = await EmitirPagoAsync(p2.Id);

        Assert.NotEqual(r1.Generados.Single().Numero, r2.Generados.Single().Numero);
        Assert.Equal(2, (await Motor.ObtenerPorVentaAsync(venta.Id)).Count);
    }

    // CASO 9
    [Fact]
    public async Task ModificarPlantilla_NoCambiaElDocumentoHistorico()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var original = (await EmitirVentaAsync(venta.Id)).Generados
            .Single(d => d.TipoDocumentoId == Context.TiposDocumento.First(t => t.Codigo == "PAGARE").Id);
        var textoOriginal = original.ContenidoRenderizado;
        var versionOriginal = original.PlantillaDocumentoVersionId;

        var plantilla = await PlantillaAsync(DocumentoSeeder.CodPagareEstandar);
        await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            Id = plantilla.Id, TipoDocumentoId = plantilla.TipoDocumentoId, Codigo = plantilla.Codigo, Nombre = plantilla.Nombre,
            Activa = true, VigenteDesde = plantilla.VigenteDesde, RequiereFirma = true, FirmantesRequeridos = "firmante",
            Contenido = "PAGARE MODIFICADO {{documento.numero}}"
        });

        Context.ChangeTracker.Clear();
        var historico = await Context.DocumentosGenerados.AsNoTracking().Include(d => d.PlantillaDocumentoVersion).FirstAsync(d => d.Id == original.Id);
        Assert.Equal(textoOriginal, historico.ContenidoRenderizado);
        Assert.Equal(versionOriginal, historico.PlantillaDocumentoVersionId);
        Assert.Equal(1, historico.PlantillaDocumentoVersion.Numero);
        Assert.DoesNotContain("MODIFICADO", historico.ContenidoRenderizado);
    }

    // CASO 10
    [Fact]
    public async Task NuevaVenta_UsaLaNuevaVersionDeLaPlantilla()
    {
        await SembrarConfiguracionInicialAsync();
        var plantilla = await PlantillaAsync(DocumentoSeeder.CodPagareEstandar);
        await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            Id = plantilla.Id, TipoDocumentoId = plantilla.TipoDocumentoId, Codigo = plantilla.Codigo, Nombre = plantilla.Nombre,
            Activa = true, VigenteDesde = plantilla.VigenteDesde, RequiereFirma = true, FirmantesRequeridos = "firmante",
            Contenido = "PAGARE v2 {{documento.numero}}"
        });

        var venta = await SembrarVentaAsync();
        var r = await EmitirVentaAsync(venta.Id);

        var pagare = r.Generados.Single(d => d.TipoDocumentoId == Context.TiposDocumento.First(t => t.Codigo == "PAGARE").Id);
        Assert.Contains("PAGARE v2", pagare.ContenidoRenderizado);
        var version = await Context.PlantillasDocumentoVersion.FirstAsync(v => v.Id == pagare.PlantillaDocumentoVersionId);
        Assert.Equal(2, version.Numero);
    }

    // CASO 11
    [Fact]
    public async Task ReintentarElEvento_NoDuplicaDocumentos()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();

        var primero = await EmitirVentaAsync(venta.Id);
        var segundo = await EmitirVentaAsync(venta.Id);
        var tercero = await Motor.ReintentarEventoAsync(EventosDocumentales.ContratoCreditoSolicitado, new DocumentoOrigen { VentaId = venta.Id });

        Assert.Equal(2, primero.Generados.Count);
        Assert.Empty(segundo.Generados);
        Assert.Equal(2, segundo.YaExistentes.Count);
        Assert.Empty(tercero.Generados);
        Assert.Equal(2, await Context.DocumentosGenerados.CountAsync());
    }

    // CASO 13
    [Fact]
    public async Task VariableRequeridaAusente_NoObligatoria_DevuelveErrorControladoSinGenerar()
    {
        await SembrarConfiguracionInicialAsync();
        await DesactivarReglasSembradasAsync(EventosDocumentales.ContratoCreditoSolicitado);

        var tipo = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "CONTRATO");
        var plantilla = await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            TipoDocumentoId = tipo.Id, Codigo = "CONTRATO_REQ", Nombre = "Requiere fiador", Activa = true,
            Contenido = "FIADOR {{fiador.nombreCompleto}}", VariablesRequeridas = "fiador.nombreCompleto"
        });
        await CrearReglaAsync("Req", EventosDocumentales.ContratoCreditoSolicitado, CondCredito, plantilla);

        var venta = await SembrarVentaAsync(conFiador: false);
        var r = await EmitirVentaAsync(venta.Id);

        Assert.Empty(r.Generados);
        var error = Assert.Single(r.Errores);
        Assert.Equal(DocumentoErrores.ContextoIncompleto, error.Codigo);
        Assert.Empty(await Context.DocumentosGenerados.ToListAsync());
    }

    [Fact]
    public async Task VariableRequeridaAusente_Obligatoria_LanzaYNoPersisteNada()
    {
        await SembrarConfiguracionInicialAsync();
        await DesactivarReglasSembradasAsync(EventosDocumentales.ContratoCreditoSolicitado);

        var tipo = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "CONTRATO");
        var plantilla = await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            TipoDocumentoId = tipo.Id, Codigo = "CONTRATO_REQ", Nombre = "Requiere fiador", Activa = true,
            Contenido = "FIADOR {{fiador.nombreCompleto}}", VariablesRequeridas = "fiador.nombreCompleto"
        });
        await CrearReglaAsync("Req", EventosDocumentales.ContratoCreditoSolicitado, CondCredito, plantilla, obligatoria: true);

        var venta = await SembrarVentaAsync(conFiador: false);
        var ex = await Assert.ThrowsAsync<DocumentoObligatorioFallidoException>(() => EmitirVentaAsync(venta.Id));

        Assert.Equal(DocumentoErrores.ObligatorioFallido, ex.Codigo);
        Assert.Contains(ex.Errores, e => e.Codigo == DocumentoErrores.ContextoIncompleto);
        Assert.Empty(await Context.DocumentosGenerados.ToListAsync());
    }

    // CASO 14
    [Fact]
    public async Task PlantillaInactiva_NoSeUtiliza_EnUnaReglaNoObligatoria()
    {
        await SembrarConfiguracionInicialAsync();
        var regla = await Context.ReglasDocumento.FirstAsync(r => r.EventoCodigo == EventosDocumentales.ContratoCreditoSolicitado);
        regla.Obligatoria = false;
        await Context.SaveChangesAsync();
        var pagare = await PlantillaAsync(DocumentoSeeder.CodPagareEstandar);
        await Config.SetPlantillaActivaAsync(pagare.Id, false);
        var venta = await SembrarVentaAsync();

        var r = await EmitirVentaAsync(venta.Id);

        var doc = Assert.Single(r.Generados);   // solo el contrato
        Assert.Equal(DocumentoSeeder.CodContratoEstandar, (await PlantillaPorIdAsync(doc.PlantillaDocumentoId)).Codigo);
        Assert.Contains(r.Errores, e => e.Codigo == DocumentoErrores.PlantillaInactiva);
    }

    [Fact]
    public async Task PlantillaInactiva_EnUnaReglaObligatoria_ImpideElPasoYNoGeneraNada()
    {
        await SembrarConfiguracionInicialAsync();   // la regla de contrato + pagaré es obligatoria
        var pagare = await PlantillaAsync(DocumentoSeeder.CodPagareEstandar);
        await Config.SetPlantillaActivaAsync(pagare.Id, false);
        var venta = await SembrarVentaAsync();

        var ex = await Assert.ThrowsAsync<DocumentoObligatorioFallidoException>(() => EmitirVentaAsync(venta.Id));

        Assert.Contains(ex.Errores, e => e.Codigo == DocumentoErrores.PlantillaInactiva);
        Assert.Empty(await Context.DocumentosGenerados.ToListAsync());   // todo o nada: ni el contrato quedó emitido
    }

    // CASO 15
    [Fact]
    public async Task ReglaDesactivada_NoSeEjecuta()
    {
        await SembrarConfiguracionInicialAsync();
        await DesactivarReglasSembradasAsync(EventosDocumentales.ContratoCreditoSolicitado);
        var venta = await SembrarVentaAsync();

        var r = await EmitirVentaAsync(venta.Id);

        Assert.Empty(r.Generados);
        Assert.Empty(r.Errores);
    }

    // CASO 17
    [Fact]
    public async Task DocumentoCancelado_QuedaComoHistorico_ConMotivoYUsuario_YNoSeRegeneraSolo()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var contrato = (await EmitirVentaAsync(venta.Id)).Generados.First();

        await Assert.ThrowsAsync<DocumentoException>(() => Motor.CancelarAsync(contrato.Id, "no"));
        await Motor.CancelarAsync(contrato.Id, "Datos del cliente incorrectos");

        var db = await Context.DocumentosGenerados.AsNoTracking().FirstAsync(d => d.Id == contrato.Id);
        Assert.Equal(EstadoDocumentoGenerado.Cancelado, db.Estado);
        Assert.Equal("Datos del cliente incorrectos", db.MotivoCancelacion);
        Assert.Equal("tester", db.CanceladoPor);
        Assert.NotNull(db.FechaCancelacionUtc);
        Assert.False(string.IsNullOrWhiteSpace(db.ContenidoRenderizado));
        Assert.Contains(Auditoria.Eventos, e => e.Accion == "cancelar");

        // Un reintento del evento no resucita ni duplica el documento cancelado.
        var reintento = await EmitirVentaAsync(venta.Id);
        Assert.Empty(reintento.Generados);
        Assert.Equal(2, await Context.DocumentosGenerados.CountAsync());

        await Assert.ThrowsAsync<DocumentoException>(() => Motor.CancelarAsync(contrato.Id, "Otra vez cancelado"));
    }

    // CASO 18
    [Fact]
    public async Task Reimpresion_ConservaContenidoOriginal_YRegistraLaReimpresion()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var contrato = (await EmitirVentaAsync(venta.Id)).Generados.First();
        var texto = contrato.ContenidoRenderizado;

        // Cambian los datos vivos: dirección del cliente y la plantilla.
        var cliente = await Context.Clientes.FirstAsync(c => c.Id == venta.ClienteId);
        cliente.Nombre = "Cambiado";
        await Context.SaveChangesAsync();

        var antes = contrato.ContadorReimpresiones;
        var pdf = await Motor.ReimprimirAsync(new[] { contrato.Id });

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Contenido, 0, 4));
        Context.ChangeTracker.Clear();
        var db = await Context.DocumentosGenerados.AsNoTracking().FirstAsync(d => d.Id == contrato.Id);
        Assert.Equal(texto, db.ContenidoRenderizado);
        Assert.DoesNotContain("Cambiado", db.ContenidoRenderizado);
        Assert.Equal(antes + 1, db.ContadorReimpresiones);
        Assert.NotNull(db.UltimaReimpresionUtc);
        Assert.Contains(Auditoria.Eventos, e => e.Accion == "reimprimir");
    }

    // Regeneración explícita vs reimpresión
    [Fact]
    public async Task Regenerar_CreaDocumentoNuevo_YDejaElAnteriorComoReemplazado()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var original = (await EmitirVentaAsync(venta.Id)).Generados
            .First(d => d.TipoDocumentoId == Context.TiposDocumento.First(t => t.Codigo == "CONTRATO").Id);

        var cliente = await Context.Clientes.FirstAsync(c => c.Id == venta.ClienteId);
        cliente.Apellido = "Rodriguez";
        await Context.SaveChangesAsync();

        var nuevo = await Motor.RegenerarAsync(original.Id, "Corrección de apellido");

        Assert.NotEqual(original.Id, nuevo.Id);
        Assert.NotEqual(original.Numero, nuevo.Numero);
        Assert.Contains("Rodriguez", nuevo.ContenidoRenderizado);
        Assert.Equal(original.Id, nuevo.ReemplazaADocumentoId);

        Context.ChangeTracker.Clear();
        var viejo = await Context.DocumentosGenerados.AsNoTracking().FirstAsync(d => d.Id == original.Id);
        Assert.Equal(EstadoDocumentoGenerado.Reemplazado, viejo.Estado);
        Assert.Equal(nuevo.Id, viejo.ReemplazadoPorDocumentoId);
        Assert.Contains("Perez", viejo.ContenidoRenderizado);                // el original no se tocó
        Assert.DoesNotContain("Rodriguez", viejo.ContenidoRenderizado);

        await Assert.ThrowsAsync<DocumentoException>(() => Motor.RegenerarAsync(original.Id, "Otra vez"));
    }

    [Fact]
    public async Task Regenerar_SobreDocumentoFirmado_RequiereConfirmacionExplicita()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var pagare = (await EmitirVentaAsync(venta.Id)).Generados
            .First(d => d.TipoDocumentoId == Context.TiposDocumento.First(t => t.Codigo == "PAGARE").Id);
        await Motor.FirmarAsync(pagare.Id, "firmante", "Juan Perez");

        await Assert.ThrowsAsync<DocumentoException>(() => Motor.RegenerarAsync(pagare.Id, "Reemplazo"));
        var nuevo = await Motor.RegenerarAsync(pagare.Id, "Reemplazo", confirmarSobreFirmado: true);
        Assert.Equal(EstadoDocumentoGenerado.PendienteFirma, nuevo.Estado);
    }

    // Firmas
    [Fact]
    public async Task Firma_RequiereTodosLosFirmantes_YCambiaElEstado()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var contrato = (await EmitirVentaAsync(venta.Id)).Generados
            .First(d => d.TipoDocumentoId == Context.TiposDocumento.First(t => t.Codigo == "CONTRATO").Id);

        await Motor.FirmarAsync(contrato.Id, "vendedor", null);
        await Motor.FirmarAsync(contrato.Id, "comprador", "Juan Perez");
        Assert.Equal(EstadoDocumentoGenerado.PendienteFirma, (await Motor.ObtenerAsync(contrato.Id))!.Estado);

        await Assert.ThrowsAsync<DocumentoException>(() => Motor.FirmarAsync(contrato.Id, "comprador", null));   // ya firmó
        await Assert.ThrowsAsync<DocumentoException>(() => Motor.FirmarAsync(contrato.Id, "escribano", null));    // no es firmante

        await Motor.FirmarAsync(contrato.Id, "fiador", "Maria Gomez");
        Assert.Equal(EstadoDocumentoGenerado.Firmado, (await Motor.ObtenerAsync(contrato.Id))!.Estado);
    }

    [Fact]
    public async Task ReglaQueExigeFirma_BloqueaHastaQueSeFirme()
    {
        await SembrarConfiguracionInicialAsync();
        await DesactivarReglasSembradasAsync(EventosDocumentales.ContratoCreditoSolicitado);
        var contratoPl = await PlantillaAsync(DocumentoSeeder.CodContratoEstandar);
        await Config.GuardarReglaAsync(new ReglaDocumentoInput
        {
            Nombre = "Exige firma", EventoCodigo = EventosDocumentales.ContratoCreditoSolicitado, CondicionJson = CondCredito,
            PlantillaDocumentoId = contratoPl.Id, ExigeFirmaParaContinuar = true
        });
        var venta = await SembrarVentaAsync();
        var doc = (await EmitirVentaAsync(venta.Id)).Generados.Single();

        Assert.Single(await Motor.ObtenerBloqueantesDeFirmaAsync(venta.Id));
        foreach (var rol in new[] { "vendedor", "comprador", "fiador" })
            await Motor.FirmarAsync(doc.Id, rol, null);
        Assert.Empty(await Motor.ObtenerBloqueantesDeFirmaAsync(venta.Id));
    }

    // Numeración
    [Fact]
    public async Task Numeracion_EsSecuencialPorTipo_ConPrefijoPropio()
    {
        await SembrarConfiguracionInicialAsync();
        var tipo = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "RECIBO");
        var numeracion = new DocumentoNumeracionService(Context, NullLogger<DocumentoNumeracionService>.Instance);

        var a = await numeracion.SiguienteNumeroAsync(tipo.Id);
        var b = await numeracion.SiguienteNumeroAsync(tipo.Id);
        var otroTipo = await numeracion.SiguienteNumeroAsync((await Context.TiposDocumento.FirstAsync(t => t.Codigo == "PAGARE")).Id);

        Assert.EndsWith("000001", a);
        Assert.EndsWith("000002", b);
        Assert.StartsWith("REC-", a);
        Assert.StartsWith("PAG-", otroTipo);
        Assert.EndsWith("000001", otroTipo);
    }

    // CASO 12
    [Fact]
    public async Task DosProcesosSimultaneos_NoRecibenElMismoNumero()
    {
        var archivo = Path.Combine(Path.GetTempPath(), $"doc-num-{Guid.NewGuid():N}.db");
        var cadena = $"Data Source={archivo};Default Timeout=60";
        try
        {
            int tipoId;
            await using (var inicial = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(cadena).Options))
            {
                await inicial.Database.EnsureCreatedAsync();
                var tipo = new TipoDocumento { Codigo = "REC", Nombre = "Recibo", Prefijo = "REC", PermiteMultiples = true };
                inicial.TiposDocumento.Add(tipo);
                await inicial.SaveChangesAsync();
                tipoId = tipo.Id;
            }

            async Task<List<string>> Trabajador()
            {
                await using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(cadena).Options);
                var svc = new DocumentoNumeracionService(ctx, NullLogger<DocumentoNumeracionService>.Instance);
                var numeros = new List<string>();
                for (var i = 0; i < 15; i++)
                    numeros.Add(await svc.SiguienteNumeroAsync(tipoId));
                return numeros;
            }

            var resultados = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(Trabajador)));
            var todos = resultados.SelectMany(x => x).ToList();

            Assert.Equal(60, todos.Count);
            Assert.Equal(60, todos.Distinct().Count());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in new[] { archivo, archivo + "-wal", archivo + "-shm", archivo + "-journal" })
                if (File.Exists(f)) File.Delete(f);
        }
    }

    // Legado: migración de contratos ya emitidos
    [Fact]
    public async Task MigracionDeContratosLegados_ConservaTextoYNumeros_YEsIdempotente()
    {
        Context.PlantillasContratoCredito.Add(new PlantillaContratoCredito
        {
            Nombre = "Legacy", Activa = true, NombreVendedor = "The Bury", DomicilioVendedor = "X", CiudadFirma = "C", Jurisdiccion = "J",
            InteresMoraDiarioPorcentaje = 1m, TextoContrato = "viejo contrato", TextoPagare = "viejo pagare",
            VigenteDesde = DateTime.UtcNow.Date.AddDays(-1)
        });
        var venta = await SembrarVentaAsync();
        var plantillaLegada = await Context.PlantillasContratoCredito.FirstAsync();
        await Context.SaveChangesAsync();

        // Snapshot mínimo válido, como el que dejaba el generador anterior.
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
            UsuarioGeneracion = "u", TextoContratoSnapshot = "Contrato de {{COMPRADOR_NOMBRE}}", TextoPagareSnapshot = "Pagaré por {{PRECIO_TOTAL}}",
            DatosSnapshotJson = snapshot
        });
        await Context.SaveChangesAsync();

        await DocumentoSeeder.EnsureAsync(Context, NullLogger.Instance);
        await DocumentoSeeder.EnsureAsync(Context, NullLogger.Instance);   // segunda corrida: no duplica

        var docs = await Context.DocumentosGenerados.Include(d => d.TipoDocumento).Where(d => d.ContratoLegadoId != null).ToListAsync();
        Assert.Equal(2, docs.Count);
        var contrato = docs.Single(d => d.TipoDocumento.Codigo == "CONTRATO");
        var pagare = docs.Single(d => d.TipoDocumento.Codigo == "PAGARE");
        Assert.Equal("CVC-202601-000001", contrato.Numero);
        Assert.Equal("PAG-202601-000001", pagare.Numero);
        Assert.Contains("Perez, Juan", contrato.ContenidoRenderizado);
        Assert.Equal(venta.Id, contrato.VentaId);
        Assert.Single(await Context.ContratosVentaCredito.ToListAsync());   // el contrato legado sigue intacto

        // La numeración nueva continúa después de lo migrado (no pisa números históricos).
        var siguiente = await new DocumentoNumeracionService(Context, NullLogger<DocumentoNumeracionService>.Instance)
            .SiguienteNumeroAsync(contrato.TipoDocumentoId);
        Assert.EndsWith("000002", siguiente);
    }

    [Fact]
    public async Task ConfiguracionInicial_ReplicaElComportamientoAnterior_ConReglasConfigurables()
    {
        await SembrarConfiguracionInicialAsync();

        var reglas = await Context.ReglasDocumento.Include(r => r.PaqueteDocumental).ToListAsync();
        var credito = reglas.Single(r => r.EventoCodigo == EventosDocumentales.ContratoCreditoSolicitado);
        Assert.True(credito.Obligatoria);
        Assert.True(credito.Activa);
        Assert.NotNull(credito.PaqueteDocumentalId);
        Assert.Contains("CreditoPersonal", credito.CondicionJson);
        Assert.Equal(1, reglas.Count(r => r.EventoCodigo == EventosDocumentales.PagoRegistrado && r.Activa));
        // La constancia de entrega es opt-in: hoy el sistema no la emite.
        Assert.False(reglas.Single(r => r.EventoCodigo == EventosDocumentales.EntregaRealizada).Activa);
    }

    [Fact]
    public async Task EventoEntrega_ConReglaActivada_GeneraConstancia()
    {
        await SembrarConfiguracionInicialAsync();
        var regla = await Context.ReglasDocumento.FirstAsync(r => r.EventoCodigo == EventosDocumentales.EntregaRealizada);
        await Config.SetReglaActivaAsync(regla.Id, true);
        var venta = await SembrarVentaAsync(TipoPago.Efectivo);

        var r = await EmitirVentaAsync(venta.Id, EventosDocumentales.EntregaRealizada);

        var constancia = Assert.Single(r.Generados);
        Assert.StartsWith("CEN-", constancia.Numero);
        Assert.Contains("Heladera", constancia.ContenidoRenderizado);
    }

    [Fact]
    public async Task NuevoTipoDocumental_SeConfiguraSinTocarCodigo()
    {
        // "Cuando se entregue este tipo de producto necesito una constancia": tipo + plantilla + regla.
        await SembrarConfiguracionInicialAsync();
        var tipo = await Config.GuardarTipoAsync(new TipoDocumentoInput
        {
            Codigo = "GARANTIA_EXTENDIDA", Nombre = "Garantía extendida", Prefijo = "GAR", PermiteMultiples = false
        });
        var plantilla = await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            TipoDocumentoId = tipo.Id, Codigo = "GARANTIA_EXT_STD", Nombre = "Garantía estándar",
            Contenido = "Garantía extendida {{documento.numero}} para {{cliente.nombreCompleto}} — venta {{venta.numero}}"
        });
        await CrearReglaAsync("Garantía al confirmar", EventosDocumentales.VentaConfirmada,
            """{"campo":"venta.total","operador":"mayorIgual","valor":500}""", plantilla);

        var venta = await SembrarVentaAsync(TipoPago.Efectivo);
        var r = await EmitirVentaAsync(venta.Id, EventosDocumentales.VentaConfirmada);

        var doc = Assert.Single(r.Generados);
        Assert.StartsWith("GAR-", doc.Numero);
        Assert.Contains(venta.Numero, doc.ContenidoRenderizado);
    }

    [Fact]
    public async Task Auditoria_RegistraCreacionDePlantillaNuevaVersionYGeneracion()
    {
        await SembrarConfiguracionInicialAsync();
        var tipo = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "RECIBO");
        var p = await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            TipoDocumentoId = tipo.Id, Codigo = "RECIBO_X", Nombre = "Recibo X", Contenido = "A {{cliente.nombre}}"
        });
        await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            Id = p.Id, TipoDocumentoId = tipo.Id, Codigo = "RECIBO_X", Nombre = "Recibo X", VigenteDesde = p.VigenteDesde, Contenido = "B {{cliente.nombre}}"
        });
        await Config.SetPlantillaActivaAsync(p.Id, false);
        await Config.SetPlantillaActivaAsync(p.Id, true);
        var venta = await SembrarVentaAsync();
        await EmitirVentaAsync(venta.Id);

        var acciones = Auditoria.Eventos.Select(e => e.Accion).ToList();
        Assert.Contains("crear-plantilla", acciones);
        Assert.Contains("nueva-version-plantilla", acciones);
        Assert.Contains("desactivar-plantilla", acciones);
        Assert.Contains("activar-plantilla", acciones);
        Assert.Contains("generar", acciones);
    }
}
