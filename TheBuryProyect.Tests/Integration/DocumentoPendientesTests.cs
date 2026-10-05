using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Documentos;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>Datos de empresa propios, presupuesto emitido por el motor, firma manuscrita y constancia de entrega.</summary>
public class DocumentoEmpresaTests : DocumentoTestBase
{
    private static EmpresaInput EmpresaValida() => new()
    {
        Nombre = "Empresa Nueva SA", Cuit = "30-99999999-1", Domicilio = "Calle Nueva 1", Ciudad = "Cordoba",
        Jurisdiccion = "Cordoba", InteresMoraDiarioPorcentaje = 0.3m
    };

    [Fact]
    public async Task AlIniciar_LaEmpresaSeCopiaDeLaPlantillaDeContratoVigente()
    {
        await SembrarConfiguracionInicialAsync();

        var empresa = await Config.ObtenerEmpresaAsync();

        Assert.NotNull(empresa);
        Assert.Equal("The Bury SA", empresa!.Nombre);
        Assert.Equal("30-12345678-9", empresa.Cuit);
        Assert.Equal("Rosario", empresa.Ciudad);
        Assert.Equal(0.5m, empresa.InteresMoraDiarioPorcentaje);
    }

    [Fact]
    public async Task LosDocumentosUsanLaEmpresaConfigurada_NoLaPlantillaDeContrato()
    {
        await SembrarConfiguracionInicialAsync();
        await Config.GuardarEmpresaAsync(EmpresaValida());
        var venta = await SembrarVentaAsync();

        var r = await EmitirVentaAsync(venta.Id);

        var contrato = r.Generados.First(d => d.TipoDocumentoId == Context.TiposDocumento.First(t => t.Codigo == "CONTRATO").Id);
        Assert.Contains("Empresa Nueva SA", contrato.ContenidoRenderizado);
        Assert.DoesNotContain("The Bury SA", contrato.ContenidoRenderizado);
    }

    [Fact]
    public async Task GuardarEmpresa_ReplicaLosDatosEnLaPlantillaVigente_ParaElContratoAnterior()
    {
        await SembrarConfiguracionInicialAsync();
        // El interés por mora es único: sale de Configuración → Mora, no de lo que se envíe en el formulario de empresa.
        Context.ConfiguracionesMora.Add(new TheBuryProject.Models.Entities.ConfiguracionMora { TasaMoraBase = 0.3m });
        await Context.SaveChangesAsync();

        await Config.GuardarEmpresaAsync(EmpresaValida());

        Context.ChangeTracker.Clear();
        var legada = await Context.PlantillasContratoCredito.FirstAsync(p => p.Activa);
        Assert.Equal("Empresa Nueva SA", legada.NombreVendedor);
        Assert.Equal("Calle Nueva 1", legada.DomicilioVendedor);
        Assert.Equal("Cordoba", legada.CiudadFirma);
        Assert.Equal(0.3m, legada.InteresMoraDiarioPorcentaje);
        Assert.Equal(1, await Context.EmpresasConfiguracion.CountAsync());
        Assert.Contains(Auditoria.Eventos, e => e.Accion == "modificar-empresa");
    }

    [Fact]
    public async Task ElEditorDeContratoAnterior_ActualizaLaEmpresa()
    {
        await SembrarConfiguracionInicialAsync();
        var servicio = new PlantillaContratoCreditoService(Context, NullLogger<PlantillaContratoCreditoService>.Instance);
        var modelo = await servicio.ObtenerParaEdicionAsync();
        modelo.NombreVendedor = "Desde el editor legado";
        modelo.CiudadFirma = "Mendoza";

        await servicio.GuardarAsync(modelo);

        Context.ChangeTracker.Clear();
        var empresa = await Context.EmpresasConfiguracion.AsNoTracking().FirstAsync();
        Assert.Equal("Desde el editor legado", empresa.Nombre);
        Assert.Equal("Mendoza", empresa.Ciudad);
    }

    [Theory]
    [InlineData("", "D", "C", "J", "30-1", 0.1, "nombre")]
    [InlineData("N", "", "C", "J", "30-1", 0.1, "domicilio")]
    [InlineData("N", "D", "", "J", "30-1", 0.1, "ciudad")]
    [InlineData("N", "D", "C", "", "30-1", 0.1, "jurisdicci")]
    [InlineData("N", "D", "C", "J", "abc", 0.1, "CUIT")]
    public async Task GuardarEmpresa_ValidaLosDatos(string nombre, string dom, string ciudad, string juris, string cuit, double mora, string fragmento)
    {
        await SembrarConfiguracionInicialAsync();

        var ex = await Assert.ThrowsAsync<DocumentoException>(() => Config.GuardarEmpresaAsync(new EmpresaInput
        {
            Nombre = nombre, Domicilio = dom, Ciudad = ciudad, Jurisdiccion = juris, Cuit = cuit, InteresMoraDiarioPorcentaje = (decimal)mora
        }));

        Assert.Contains(fragmento, ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public class DocumentoPresupuestoTests : DocumentoTestBase
{
    private async Task<Cotizacion> SembrarCotizacionAsync()
    {
        var categoria = new Categoria { Nombre = $"Cat {Guid.NewGuid():N}", Codigo = $"C{Guid.NewGuid():N}"[..8] };
        var marca = new Marca { Nombre = $"Marca {Guid.NewGuid():N}", Codigo = $"M{Guid.NewGuid():N}"[..8] };
        var producto = new Producto
        {
            Codigo = $"P{Guid.NewGuid():N}"[..8], Nombre = "Lavarropas", Categoria = categoria, Marca = marca,
            PrecioCompra = 0, PrecioVenta = 1200m, PorcentajeIVA = 21m
        };
        var cot = new Cotizacion
        {
            Numero = $"COT-{Guid.NewGuid():N}"[..14], Fecha = DateTime.UtcNow, Estado = EstadoCotizacion.Emitida,
            NombreClienteLibre = "Marta Diaz", DniClienteLibre = "27111222", Subtotal = 1200m, TotalBase = 1200m
        };
        cot.Detalles.Add(new CotizacionDetalle
        {
            Producto = producto, CodigoProductoSnapshot = "LAV1", NombreProductoSnapshot = "Lavarropas", Cantidad = 1,
            PrecioUnitarioSnapshot = 1200m, Subtotal = 1200m
        });
        Context.Cotizaciones.Add(cot);
        await Context.SaveChangesAsync();
        return cot;
    }

    private Task<DocumentoEventoResultado> EmitirPresupuestoAsync(int cotizacionId)
        => Motor.ProcesarEventoAsync(new DocumentoEventoRequest
        {
            Evento = EventosDocumentales.PresupuestoGenerado, Origen = new DocumentoOrigen { CotizacionId = cotizacionId }
        });

    [Fact]
    public async Task Presupuesto_SeEmiteComoDocumentoNumerado_ConLosDatosDeLaCotizacion()
    {
        await SembrarConfiguracionInicialAsync();
        var cot = await SembrarCotizacionAsync();

        var r = await EmitirPresupuestoAsync(cot.Id);

        var doc = Assert.Single(r.Generados);
        Assert.StartsWith("PRE-", doc.Numero);
        Assert.Equal(cot.Id, doc.CotizacionId);
        Assert.Contains(cot.Numero, doc.ContenidoRenderizado);
        Assert.Contains("Lavarropas", doc.ContenidoRenderizado);
        Assert.Contains("Marta Diaz", doc.ContenidoRenderizado);
        Assert.Single(await Motor.ObtenerPorCotizacionAsync(cot.Id));
    }

    [Fact]
    public async Task Presupuesto_PedidoDosVeces_DevuelveElMismoDocumento()
    {
        await SembrarConfiguracionInicialAsync();
        var cot = await SembrarCotizacionAsync();

        var primero = await EmitirPresupuestoAsync(cot.Id);
        var segundo = await EmitirPresupuestoAsync(cot.Id);

        Assert.Empty(segundo.Generados);
        Assert.Equal(primero.Generados.Single().Id, segundo.YaExistentes.Single().Id);
        Assert.Equal(1, await Context.DocumentosGenerados.CountAsync());
    }

    [Fact]
    public async Task Presupuesto_SePuedeRegenerarTrasCambiarLaCotizacion_ElAnteriorQuedaReemplazado()
    {
        await SembrarConfiguracionInicialAsync();
        var cot = await SembrarCotizacionAsync();
        var original = (await EmitirPresupuestoAsync(cot.Id)).Generados.Single();

        var cotDb = await Context.Cotizaciones.FirstAsync(c => c.Id == cot.Id);
        cotDb.NombreClienteLibre = "Marta Diaz de Lopez";
        await Context.SaveChangesAsync();

        var nuevo = await Motor.RegenerarAsync(original.Id, "Cambió el nombre del cliente");

        Assert.Contains("Marta Diaz de Lopez", nuevo.ContenidoRenderizado);
        Assert.Equal(EstadoDocumentoGenerado.Reemplazado, (await Motor.ObtenerAsync(original.Id))!.Estado);
    }
}

public class DocumentoFirmaManuscritaTests : DocumentoTestBase
{
    // PNG válido de 1x1 píxel.
    private const string Png1x1 = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static string PngConDimensiones(int ancho, int alto)
    {
        var b = new byte[40];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        b[11] = 13;
        "IHDR"u8.ToArray().CopyTo(b, 12);
        b[16] = (byte)(ancho >> 24); b[17] = (byte)(ancho >> 16); b[18] = (byte)(ancho >> 8); b[19] = (byte)ancho;
        b[20] = (byte)(alto >> 24); b[21] = (byte)(alto >> 16); b[22] = (byte)(alto >> 8); b[23] = (byte)alto;
        return "data:image/png;base64," + Convert.ToBase64String(b);
    }

    private async Task<DocumentoGenerado> PagareAsync()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        return (await EmitirVentaAsync(venta.Id)).Generados
            .First(d => d.TipoDocumentoId == Context.TiposDocumento.First(t => t.Codigo == "PAGARE").Id);
    }

    [Fact]
    public async Task Firma_ConImagen_SeGuardaConSuHash_YApareceEnElPdf()
    {
        var pagare = await PagareAsync();

        await Motor.FirmarAsync(pagare.Id, "firmante", "Juan Perez", Png1x1);

        Context.ChangeTracker.Clear();
        var doc = await Context.DocumentosGenerados.AsNoTracking().Include(d => d.TipoDocumento).Include(d => d.PlantillaDocumento)
            .FirstAsync(d => d.Id == pagare.Id);
        var firma = Assert.Single(DocumentoPdfService.LeerFirmas(doc.FirmasJson));
        Assert.False(string.IsNullOrEmpty(firma.ImagenPng));
        Assert.Equal(64, firma.HashImagen!.Length);
        Assert.Equal(EstadoDocumentoGenerado.Firmado, doc.Estado);

        var pdf = new DocumentoPdfService().GenerarPdf(new[] { doc });
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Contenido, 0, 4));
        Assert.Contains(Auditoria.Eventos, e => e.Accion == "firmar" && e.Detalle!.Contains("firma-manuscrita"));
    }

    [Fact]
    public async Task Firma_SinImagen_SigueSiendoValida()
    {
        var pagare = await PagareAsync();

        await Motor.FirmarAsync(pagare.Id, "firmante", null);

        var firma = Assert.Single(DocumentoPdfService.LeerFirmas((await Motor.ObtenerAsync(pagare.Id))!.FirmasJson));
        Assert.Null(firma.ImagenPng);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/jpeg;base64,/9j/4AAQSkZJRg==")]
    [InlineData("data:image/png;base64,esto-no-es-base64!!")]
    [InlineData("data:image/png;base64,AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]   // base64 válido pero no es un PNG
    public async Task Firma_RechazaImagenesQueNoSonPngValidos(string imagen)
    {
        var pagare = await PagareAsync();

        await Assert.ThrowsAsync<DocumentoException>(() => Motor.FirmarAsync(pagare.Id, "firmante", null, imagen));

        Assert.Equal(EstadoDocumentoGenerado.PendienteFirma, (await Motor.ObtenerAsync(pagare.Id))!.Estado);   // no quedó firmado
    }

    [Fact]
    public async Task Firma_RechazaImagenesDemasiadoGrandes()
    {
        var pagare = await PagareAsync();

        await Assert.ThrowsAsync<DocumentoException>(() => Motor.FirmarAsync(pagare.Id, "firmante", null, PngConDimensiones(5000, 5000)));
        await Assert.ThrowsAsync<DocumentoException>(() => Motor.FirmarAsync(pagare.Id, "firmante", null, PngConDimensiones(0, 10)));
        await Assert.ThrowsAsync<DocumentoException>(() => Motor.FirmarAsync(pagare.Id, "firmante", null,
            "data:image/png;base64," + new string('A', 250_000)));
        await Motor.FirmarAsync(pagare.Id, "firmante", null, PngConDimensiones(480, 160));   // dentro del límite
    }
}

public class DocumentoConstanciaEntregaTests : DocumentoTestBase
{
    [Fact]
    public async Task Constancia_SeEmitePorDefectoAlMarcarLaEntrega()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync(TipoPago.Efectivo);

        var r = await EmitirVentaAsync(venta.Id, EventosDocumentales.EntregaRealizada);

        var constancia = Assert.Single(r.Generados);
        Assert.StartsWith("CEN-", constancia.Numero);
    }

    [Fact]
    public async Task BaseExistenteConLaReglaInactivaSinEditar_SeActivaUnaSolaVez()
    {
        await SembrarConfiguracionInicialAsync();
        // Estado de las bases sembradas antes de este cambio: regla inactiva y nunca editada.
        await Context.ReglasDocumento.Where(r => r.EventoCodigo == EventosDocumentales.EntregaRealizada)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Activa, false));
        Context.ChangeTracker.Clear();   // como en un arranque real: contexto nuevo, sin entidades en memoria

        await DocumentoSeeder.EnsureAsync(Context, NullLogger.Instance);

        Assert.True((await Context.ReglasDocumento.AsNoTracking().FirstAsync(r => r.EventoCodigo == EventosDocumentales.EntregaRealizada)).Activa);
    }

    [Fact]
    public async Task SiAlguienLaDesactivaDesdeLaConfiguracion_NoSeVuelveAActivar()
    {
        await SembrarConfiguracionInicialAsync();
        var regla = await Context.ReglasDocumento.FirstAsync(r => r.EventoCodigo == EventosDocumentales.EntregaRealizada);
        await Config.SetReglaActivaAsync(regla.Id, false);

        await DocumentoSeeder.EnsureAsync(Context, NullLogger.Instance);
        await DocumentoSeeder.EnsureAsync(Context, NullLogger.Instance);

        Context.ChangeTracker.Clear();
        Assert.False((await Context.ReglasDocumento.AsNoTracking().FirstAsync(r => r.Id == regla.Id)).Activa);
    }
}
