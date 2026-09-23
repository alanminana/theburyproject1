using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

public class ContratoVentaCreditoServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly NoOpConfiguracionPagoService _configuracionPagoService;
    private readonly ContratoVentaCreditoService _service;

    public ContratoVentaCreditoServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _configuracionPagoService = new NoOpConfiguracionPagoService();
        _service = new ContratoVentaCreditoService(
            _context,
            new StubFinancialCalculationService(),
            _configuracionPagoService,
            new StubWebHostEnvironment(),
            NullLogger<ContratoVentaCreditoService>.Instance);
    }

    [Fact]
    public async Task GenerarAsync_SinDescuentoGeneral_ConservaSubtotalDeLinea()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_210m, subtotalFinal: 1_210m)
        }, total: 1_210m);

        var contrato = await _service.GenerarAsync(venta.Id, "tester");

        var productos = LeerProductosSnapshot(contrato);
        Assert.Single(productos);
        Assert.Equal(1_210m, productos[0].GetProperty("Subtotal").GetDecimal());
    }

    [Fact]
    public async Task GenerarAsync_ConDescuentoGeneral_UsaSubtotalFinal()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_210m, subtotalFinal: 1_089m)
        }, total: 1_089m, descuento: 121m);

        var contrato = await _service.GenerarAsync(venta.Id, "tester");

        var productos = LeerProductosSnapshot(contrato);
        Assert.Single(productos);
        Assert.Equal(1_089m, productos[0].GetProperty("Subtotal").GetDecimal());
    }

    [Fact]
    public async Task GenerarAsync_ProductosMixtosIva_ImportesVisiblesCierranConVentaTotal()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P21", "Producto IVA 21", subtotal: 1_210m, subtotalFinal: 1_089m),
            DetalleSeed("P105", "Producto IVA 10.5", subtotal: 110.50m, subtotalFinal: 100m),
            DetalleSeed("P0", "Producto IVA 0", subtotal: 100m, subtotalFinal: 80m)
        }, total: 1_269m, descuento: 151.50m);

        var contrato = await _service.GenerarAsync(venta.Id, "tester");

        var productos = LeerProductosSnapshot(contrato);
        Assert.Equal(3, productos.Count);
        Assert.Equal(venta.Total, productos.Sum(p => p.GetProperty("Subtotal").GetDecimal()));
    }

    [Fact]
    public async Task GenerarAsync_LegacySinSubtotalFinal_UsaSubtotal()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("LEG", "Producto legacy", subtotal: 500m, subtotalFinal: 0m)
        }, total: 500m);

        var contrato = await _service.GenerarAsync(venta.Id, "tester");

        var productos = LeerProductosSnapshot(contrato);
        Assert.Single(productos);
        Assert.Equal(500m, productos[0].GetProperty("Subtotal").GetDecimal());
    }

    [Fact]
    public async Task GenerarAsync_TasaCero_GeneraContrato()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_210m, subtotalFinal: 1_210m)
        }, total: 1_210m, tasaInteres: 0m);

        var contrato = await _service.GenerarAsync(venta.Id, "tester");

        Assert.NotNull(contrato);
        Assert.False(string.IsNullOrWhiteSpace(contrato.NumeroContrato));
    }

    [Fact]
    public async Task ValidarDatosParaGenerarAsync_TasaNegativa_DevuelveError()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_210m, subtotalFinal: 1_210m)
        }, total: 1_210m, tasaInteres: -1m);

        var resultado = await _service.ValidarDatosParaGenerarAsync(venta.Id);

        Assert.False(resultado.EsValido);
        Assert.Contains(resultado.Errores, e => e.Contains("negativa", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidarDatosParaGenerarAsync_CuotasEliminadas_ProyectaPlanVigente()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_210m, subtotalFinal: 1_210m)
        }, total: 1_210m);
        venta.Credito!.Cuotas.Single().IsDeleted = true;
        await _context.SaveChangesAsync();

        var resultado = await _service.ValidarDatosParaGenerarAsync(venta.Id);

        Assert.True(resultado.EsValido);
        Assert.DoesNotContain(resultado.Errores, e => e.Contains("plan de cuotas", StringComparison.OrdinalIgnoreCase));
    }

    // =========================================================================================
    // CSR-ML4 — Fase 4 (P5/P6/P7): snapshot con cuotas persistidas vs. re-simulación sin snapshot.
    // =========================================================================================

    [Fact]
    public async Task P5_CreditoConCuotasPersistidas_UsaSnapshotSinRecalcular()
    {
        var cuotasSinRecargoPersistidas = new[] { 2, 4 };
        var venta = await SeedVentaCreditoConPlanAsync(
            cantidadCuotas: 6, tasaInteres: 8m, total: 60_000m,
            cuotasSinRecargoPersistidas: cuotasSinRecargoPersistidas, persistirCuotas: true);

        // La configuración "vigente" al generar el contrato es OTRA (sin exclusiones): si el
        // contrato recalculara en vez de leer el snapshot, esta comparación fallaría.
        _configuracionPagoService.Planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(6, 8m, Array.Empty<int>(), true, Array.Empty<int>()) },
            OrigenPlanesCredito.Global);

        var contrato = await _service.GenerarAsync(venta.Id, "tester");
        var cuotas = LeerPlanCuotasSnapshot(contrato);

        Assert.Equal(6, cuotas.Count);
        foreach (var numero in cuotasSinRecargoPersistidas)
            Assert.Equal(0m, MontoInteresDeCuota(cuotas, numero));
    }

    [Fact]
    public async Task P6_SinCuotasPersistidas_ReSimulaUsandoCuotasSinRecargoDelPlanVigente()
    {
        var cuotasSinRecargo = new[] { 1, 3 };
        var venta = await SeedVentaCreditoConPlanAsync(
            cantidadCuotas: 5, tasaInteres: 10m, total: 100_000m,
            cuotasSinRecargoPersistidas: null, persistirCuotas: false);

        _configuracionPagoService.Planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(5, 10m, Array.Empty<int>(), true, cuotasSinRecargo) },
            OrigenPlanesCredito.Global);

        var contrato = await _service.GenerarAsync(venta.Id, "tester");
        var cuotas = LeerPlanCuotasSnapshot(contrato);

        Assert.Equal(5, cuotas.Count);
        foreach (var numero in cuotasSinRecargo)
            Assert.Equal(0m, MontoInteresDeCuota(cuotas, numero));

        Assert.True(cuotas
            .Where(c => !cuotasSinRecargo.Contains(c.GetProperty("NumeroCuota").GetInt32()))
            .All(c => c.GetProperty("MontoInteres").GetDecimal() > 0m));
    }

    [Fact]
    public async Task P7_CreditoConCuotasPersistidas_CambioPosteriorDeConfiguracionNoAlteraElContrato()
    {
        var cuotasOriginales = new[] { 1, 3, 5 };
        var venta = await SeedVentaCreditoConPlanAsync(
            cantidadCuotas: 10, tasaInteres: 10m, total: 100_000m,
            cuotasSinRecargoPersistidas: cuotasOriginales, persistirCuotas: true);

        // El plan global cambió DESPUÉS de confirmar (mismo espíritu que T13 en
        // CreditoPersonalCuotaSinRecargoHistoricoTests, ahora vía ContratoVentaCreditoService):
        // de 1,3,5 pasó a 2,4.
        _configuracionPagoService.Planes = PlanesCreditoPersonalResultado.Resuelto(
            new[] { new PlanCuotaCreditoPersonal(10, 10m, Array.Empty<int>(), true, new[] { 2, 4 }) },
            OrigenPlanesCredito.Global);

        var contrato = await _service.GenerarAsync(venta.Id, "tester");
        var cuotas = LeerPlanCuotasSnapshot(contrato);

        // Sigue el vector ORIGINAL (1,3,5 sin recargo) — no el vigente (2,4).
        foreach (var numero in cuotasOriginales)
            Assert.Equal(0m, MontoInteresDeCuota(cuotas, numero));

        Assert.True(MontoInteresDeCuota(cuotas, 2) > 0m);
        Assert.True(MontoInteresDeCuota(cuotas, 4) > 0m);
    }

    // =========================================================================================
    // PROBLEMA 1/2 (pedido 2026-09-17): Localidad del cliente + agregación de todos los
    // faltantes contractuales de una sola pasada + no bypass por excepción documental + no
    // duplicación ante una carrera real de dos requests concurrentes.
    // =========================================================================================

    [Fact]
    public async Task CasoA_ClienteCompleto_GeneraContratoCorrectamente()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_000m, subtotalFinal: 1_000m)
        }, total: 1_000m);

        var resultado = await _service.ValidarDatosParaGenerarAsync(venta.Id);
        Assert.True(resultado.EsValido);

        var contrato = await _service.GenerarAsync(venta.Id, "tester");
        Assert.NotNull(contrato);
    }

    /// <summary>
    /// Revalidación Linux pendiente del cierre anterior: el primer PDF generado en un proceso
    /// limpio debe poder ser un CONTRATO (no una cotización). A diferencia de <c>GenerarAsync</c>
    /// (los demás tests de esta clase), <see cref="ContratoVentaCreditoService.GenerarPdfAsync"/> es
    /// el único método que efectivamente invoca QuestPDF (<c>GenerarPdfBytes</c>/<c>GeneratePdf()</c>)
    /// y escribe el archivo en disco — se corre en aislamiento total vía
    /// <c>dotnet test --filter FullyQualifiedName~PrimerPdfDeUnProcesoLimpio</c> para que sea
    /// realmente el primer render QuestPDF de ese proceso, sin cotización previa.
    /// </summary>
    [Fact]
    public async Task GenerarPdfAsync_PrimerPdfDeUnProcesoLimpio_GeneraPdfValidoSinExcepcionDeFonts()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_000m, subtotalFinal: 1_000m)
        }, total: 1_000m);

        var contrato = await _service.GenerarPdfAsync(venta.Id, "tester");

        Assert.NotNull(contrato);
        Assert.False(string.IsNullOrWhiteSpace(contrato.RutaArchivo));

        var rutaCompleta = Path.Combine(Path.GetTempPath(), contrato.RutaArchivo!.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(rutaCompleta), $"No se encontró el PDF generado en {rutaCompleta}");

        var bytes = await File.ReadAllBytesAsync(rutaCompleta);
        Assert.True(bytes.Length > 1000, "El PDF generado es sospechosamente pequeño");

        var header = System.Text.Encoding.ASCII.GetString(bytes, 0, 5);
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public async Task CasoB_ClienteSinLocalidad_NoGenera_MensajeIdentificaLocalidad()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_000m, subtotalFinal: 1_000m)
        }, total: 1_000m, clienteLocalidad: null);

        var resultado = await _service.ValidarDatosParaGenerarAsync(venta.Id);

        Assert.False(resultado.EsValido);
        Assert.Contains(resultado.Errores, e => e.Contains("localidad", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(venta.ClienteId, resultado.ClienteId);

        var ex = await Assert.ThrowsAsync<ContratoVentaCreditoValidacionException>(() => _service.GenerarAsync(venta.Id, "tester"));
        Assert.Contains(ex.Errores, e => e.Contains("localidad", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CasoC_ClienteConLocalidadPeroSinTelefonoYSinGarante_ReportaTodosLosFaltantesJuntos()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_000m, subtotalFinal: 1_000m)
        }, total: 1_000m, clienteTelefono: string.Empty, requiereGaranteSinGarante: true);

        var resultado = await _service.ValidarDatosParaGenerarAsync(venta.Id);

        Assert.False(resultado.EsValido);
        // Ambos faltantes reportados en la MISMA pasada — nunca "falta X" → corregir → "ahora falta Y".
        Assert.Contains(resultado.Errores, e => e.Contains("teléfono", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(resultado.Errores, e => e.Contains("garante", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resultado.Errores, e => e.Contains("localidad", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CasoD_ExcepcionDocumentalConLocalidadFaltante_NoSaltaElRequisitoContractual()
    {
        // La excepción documental (RequiereAutorizacion=false/EstadoAutorizacion=Autorizada) es
        // una regla de VentaService/autorización, en un código completamente disjunto de
        // ValidarDatosParaGenerarAsync (que nunca lee RequiereAutorizacion/EstadoAutorizacion).
        // Este test prueba que esa disjunción es real: una venta "autorizada por excepción"
        // sigue exigiendo Localidad igual que cualquier otra.
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_000m, subtotalFinal: 1_000m)
        }, total: 1_000m, clienteLocalidad: null);

        venta.RequiereAutorizacion = false;
        venta.EstadoAutorizacion = EstadoAutorizacionVenta.Autorizada;
        venta.ExcepcionAlMomento = 1_000m;
        await _context.SaveChangesAsync();

        var resultado = await _service.ValidarDatosParaGenerarAsync(venta.Id);

        Assert.False(resultado.EsValido);
        Assert.Contains(resultado.Errores, e => e.Contains("localidad", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CasoE_SinEnvioClienteCompleto_GeneraContrato()
    {
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_000m, subtotalFinal: 1_000m)
        }, total: 1_000m);
        Assert.Null(venta.Envio);

        var resultado = await _service.ValidarDatosParaGenerarAsync(venta.Id);
        Assert.True(resultado.EsValido);
    }

    [Fact]
    public async Task CasoF_ConEnvioConLocalidadPeroClienteSinLocalidad_NoOculaElFaltanteDelCliente()
    {
        // VentaEnvio.Localidad y Cliente.Localidad son conceptos distintos (§2 del pedido):
        // que el envío SÍ tenga localidad completa no debe esconder que el Cliente no la tiene.
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_000m, subtotalFinal: 1_000m)
        }, total: 1_000m, clienteLocalidad: null);

        venta.Envio = new VentaEnvio
        {
            VentaId = venta.Id,
            Destinatario = "Juan Perez",
            Domicilio = "Otra calle 456",
            Localidad = "Localidad del envío",
            Estado = EstadoEnvio.Pendiente
        };
        await _context.SaveChangesAsync();

        var resultado = await _service.ValidarDatosParaGenerarAsync(venta.Id);

        Assert.False(resultado.EsValido);
        Assert.Contains(resultado.Errores, e => e.Contains("localidad", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CasoH_LlamadasRepetidasParaLaMismaVenta_NuncaDuplicanElContrato()
    {
        // §10/§11 del pedido: el backend debe seguir siendo seguro ante dos requests para la
        // misma venta, más allá de cualquier guard del lado del cliente (ver "existente" al
        // inicio de GenerarAsync). No es una carrera real dentro del mismo DbContext (EF Core
        // no soporta operaciones concurrentes sobre una misma instancia, y forzar dos conexiones
        // separadas sólo para simular el timing exacto de la carrera sería un test frágil que no
        // prueba nada más que sepa hacerlo el índice único de la BD — ver el test siguiente).
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_000m, subtotalFinal: 1_000m)
        }, total: 1_000m);

        var primero = await _service.GenerarAsync(venta.Id, "tester1");
        var segundo = await _service.GenerarAsync(venta.Id, "tester2");

        Assert.Equal(primero.Id, segundo.Id);

        var totalPersistidos = await _context.ContratosVentaCredito
            .IgnoreQueryFilters()
            .CountAsync(c => c.VentaId == venta.Id && !c.IsDeleted);
        Assert.Equal(1, totalPersistidos);
    }

    [Fact]
    public async Task CasoH2_IndiceUnicoDeVentaId_RechazaUnaSegundaFilaAunqueElServicioNoLaVea()
    {
        // Fundamento real de la idempotencia del catch(DbUpdateException) en GenerarAsync: si
        // dos requests concurrentes pasaran ambos el "existente == null" antes de que cualquiera
        // confirmara (la carrera real que un test síncrono de un solo DbContext no puede forzar
        // de forma fiable), la segunda inserción es la que este índice rechaza — no una
        // coincidencia de la lógica del servicio.
        var venta = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_000m, subtotalFinal: 1_000m)
        }, total: 1_000m);

        _context.ContratosVentaCredito.Add(new ContratoVentaCredito
        {
            VentaId = venta.Id,
            CreditoId = venta.CreditoId!.Value,
            ClienteId = venta.ClienteId,
            PlantillaContratoCreditoId = (await _context.PlantillasContratoCredito.FirstAsync()).Id,
            NumeroContrato = "CVC-DUP-1",
            NumeroPagare = "PAG-DUP-1",
            UsuarioGeneracion = "tester1",
            TextoContratoSnapshot = "x",
            TextoPagareSnapshot = "x",
            DatosSnapshotJson = "{}"
        });
        await _context.SaveChangesAsync();

        _context.ContratosVentaCredito.Add(new ContratoVentaCredito
        {
            VentaId = venta.Id,
            CreditoId = venta.CreditoId!.Value,
            ClienteId = venta.ClienteId,
            PlantillaContratoCreditoId = (await _context.PlantillasContratoCredito.FirstAsync()).Id,
            NumeroContrato = "CVC-DUP-2",
            NumeroPagare = "PAG-DUP-2",
            UsuarioGeneracion = "tester2",
            TextoContratoSnapshot = "x",
            TextoPagareSnapshot = "x",
            DatosSnapshotJson = "{}"
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());

        // Prueba, contra una DbUpdateException real de Sqlite (no un mensaje inventado a mano),
        // que el patrón que usa GenerarAsync para discriminar esta constraint puntual
        // efectivamente la reconoce.
        Assert.True(ContratoVentaCreditoService.EsViolacionIndiceUnicoVentaId(ex));
    }

    [Fact]
    public async Task CasoH3_DbUpdateExceptionDeOtraConstraint_NoEsTratadaComoIndiceUnicoDeVentaId()
    {
        // Contracara de CasoH2: una DbUpdateException real pero de una constraint distinta
        // (acá, NumeroContrato duplicado — también único en el modelo) NO debe discriminarse
        // como la carrera de VentaId. Si el catch de GenerarAsync fuera un catch(DbUpdateException)
        // genérico (como era antes de este ajuste), un error de este tipo también terminaría
        // devolviendo "el ganador" en vez de propagarse, escondiendo un bug real.
        var venta1 = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P1", "Producto 1", subtotal: 1_000m, subtotalFinal: 1_000m)
        }, total: 1_000m);
        var venta2 = await SeedVentaCreditoAsync(new[]
        {
            DetalleSeed("P2", "Producto 2", subtotal: 1_000m, subtotalFinal: 1_000m)
        }, total: 1_000m);

        var plantillaId = (await _context.PlantillasContratoCredito.FirstAsync()).Id;

        _context.ContratosVentaCredito.Add(new ContratoVentaCredito
        {
            VentaId = venta1.Id,
            CreditoId = venta1.CreditoId!.Value,
            ClienteId = venta1.ClienteId,
            PlantillaContratoCreditoId = plantillaId,
            NumeroContrato = "CVC-MISMO-NUMERO",
            NumeroPagare = "PAG-DUP-A",
            UsuarioGeneracion = "tester1",
            TextoContratoSnapshot = "x",
            TextoPagareSnapshot = "x",
            DatosSnapshotJson = "{}"
        });
        await _context.SaveChangesAsync();

        _context.ContratosVentaCredito.Add(new ContratoVentaCredito
        {
            VentaId = venta2.Id,
            CreditoId = venta2.CreditoId!.Value,
            ClienteId = venta2.ClienteId,
            PlantillaContratoCreditoId = plantillaId,
            NumeroContrato = "CVC-MISMO-NUMERO",
            NumeroPagare = "PAG-DUP-B",
            UsuarioGeneracion = "tester2",
            TextoContratoSnapshot = "x",
            TextoPagareSnapshot = "x",
            DatosSnapshotJson = "{}"
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());

        Assert.False(ContratoVentaCreditoService.EsViolacionIndiceUnicoVentaId(ex));
    }

    private async Task<Venta> SeedVentaCreditoAsync(
        IEnumerable<DetalleSeedData> detalles,
        decimal total,
        decimal descuento = 0m,
        decimal tasaInteres = 5m,
        string? clienteLocalidad = "Ciudad",
        string? clienteTelefono = "1122334455",
        bool requiereGaranteSinGarante = false)
    {
        var cliente = new Cliente
        {
            Nombre = "Juan",
            Apellido = "Perez",
            TipoDocumento = "DNI",
            NumeroDocumento = $"DNI{Guid.NewGuid():N}"[..12],
            Domicilio = "Calle 123",
            Localidad = clienteLocalidad,
            Telefono = clienteTelefono
        };

        var credito = new Credito
        {
            Cliente = cliente,
            Numero = $"CRE-{Guid.NewGuid():N}"[..10],
            MontoSolicitado = total,
            MontoAprobado = total,
            SaldoPendiente = total,
            TasaInteres = tasaInteres,
            CantidadCuotas = 1,
            MontoCuota = total,
            TotalAPagar = total,
            FechaPrimeraCuota = DateTime.UtcNow.Date.AddMonths(1),
            RequiereGarante = requiereGaranteSinGarante
        };

        credito.Cuotas.Add(new Cuota
        {
            NumeroCuota = 1,
            MontoCapital = total,
            MontoInteres = 0m,
            MontoTotal = total,
            FechaVencimiento = credito.FechaPrimeraCuota.Value
        });

        var categoria = new Categoria { Nombre = $"Categoria {Guid.NewGuid():N}", Codigo = $"CAT-{Guid.NewGuid():N}"[..10] };
        var marca = new Marca { Nombre = $"Marca {Guid.NewGuid():N}", Codigo = $"MAR-{Guid.NewGuid():N}"[..10] };

        var venta = new Venta
        {
            Numero = $"VTA-{Guid.NewGuid():N}",
            Cliente = cliente,
            Credito = credito,
            TipoPago = TipoPago.CreditoPersonal,
            Estado = EstadoVenta.Confirmada,
            FechaVenta = DateTime.UtcNow,
            Subtotal = total,
            IVA = 0m,
            Descuento = descuento,
            Total = total
        };

        foreach (var detalleSeed in detalles)
        {
            var producto = new Producto
            {
                Codigo = detalleSeed.Codigo,
                Nombre = detalleSeed.Nombre,
                Categoria = categoria,
                Marca = marca,
                PrecioCompra = 0m,
                PrecioVenta = detalleSeed.Subtotal,
                PorcentajeIVA = 21m
            };

            venta.Detalles.Add(new VentaDetalle
            {
                Producto = producto,
                Cantidad = 1,
                PrecioUnitario = detalleSeed.Subtotal,
                Subtotal = detalleSeed.Subtotal,
                SubtotalFinal = detalleSeed.SubtotalFinal
            });
        }

        _context.PlantillasContratoCredito.Add(new PlantillaContratoCredito
        {
            Nombre = $"Plantilla {Guid.NewGuid():N}",
            Activa = true,
            NombreVendedor = "The Bury",
            DomicilioVendedor = "Local 1",
            CiudadFirma = "Ciudad",
            Jurisdiccion = "Provincia",
            InteresMoraDiarioPorcentaje = 1m,
            TextoContrato = "{{Venta.Productos}} {{Venta.Total}}",
            TextoPagare = "{{Venta.Total}}",
            VigenteDesde = DateTime.UtcNow.Date.AddDays(-1)
        });

        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    /// <summary>
    /// CSR-ML4 — Fase 4 (P5/P6/P7): variante de <see cref="SeedVentaCreditoAsync"/> con un solo
    /// producto, cantidad/tasa de cuotas configurables, y control explícito de si el crédito ya
    /// tiene <c>Cuotas</c> persistidas. Cuando <paramref name="persistirCuotas"/> es true, las
    /// cuotas se construyen con el mismo cálculo canónico que usaría
    /// <c>VentaService.GenerarCuotasCreditoAsync</c> (<see cref="FinancialCalculationService.SimularPlanCredito"/>
    /// con <paramref name="cuotasSinRecargoPersistidas"/>), para que el snapshot persistido sea
    /// indistinguible de una confirmación real.
    /// </summary>
    private async Task<Venta> SeedVentaCreditoConPlanAsync(
        int cantidadCuotas,
        decimal tasaInteres,
        decimal total,
        IReadOnlyList<int>? cuotasSinRecargoPersistidas,
        bool persistirCuotas)
    {
        var cliente = new Cliente
        {
            Nombre = "Juan",
            Apellido = "Perez",
            TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            Domicilio = "Calle 123",
            Localidad = "Ciudad",
            Telefono = "1122334455"
        };

        var credito = new Credito
        {
            Cliente = cliente,
            Numero = $"CRE-{Guid.NewGuid():N}"[..10],
            MontoSolicitado = total,
            MontoAprobado = total,
            SaldoPendiente = total,
            TasaInteres = tasaInteres,
            CantidadCuotas = cantidadCuotas,
            MontoCuota = total / cantidadCuotas,
            TotalAPagar = total,
            FechaPrimeraCuota = DateTime.UtcNow.Date.AddMonths(1)
        };

        if (persistirCuotas)
        {
            var simulacion = new FinancialCalculationService().SimularPlanCredito(
                total, 0m, cantidadCuotas, tasaInteres, 0m, credito.FechaPrimeraCuota.Value,
                cuotasSinRecargo: cuotasSinRecargoPersistidas);

            foreach (var item in simulacion.Cuotas)
            {
                credito.Cuotas.Add(new Cuota
                {
                    NumeroCuota = item.NumeroCuota,
                    MontoCapital = item.Capital,
                    MontoInteres = item.Interes,
                    MontoTotal = item.Total,
                    FechaVencimiento = credito.FechaPrimeraCuota.Value
                });
            }
        }

        var categoria = new Categoria { Nombre = $"Categoria {Guid.NewGuid():N}", Codigo = $"CAT-{Guid.NewGuid():N}"[..10] };
        var marca = new Marca { Nombre = $"Marca {Guid.NewGuid():N}", Codigo = $"MAR-{Guid.NewGuid():N}"[..10] };
        var producto = new Producto
        {
            Codigo = $"P-{Guid.NewGuid():N}"[..10],
            Nombre = "Producto plan",
            Categoria = categoria,
            Marca = marca,
            PrecioCompra = 0m,
            PrecioVenta = total,
            PorcentajeIVA = 21m
        };

        var venta = new Venta
        {
            Numero = $"VTA-{Guid.NewGuid():N}",
            Cliente = cliente,
            Credito = credito,
            TipoPago = TipoPago.CreditoPersonal,
            Estado = EstadoVenta.Confirmada,
            FechaVenta = DateTime.UtcNow,
            Subtotal = total,
            IVA = 0m,
            Descuento = 0m,
            Total = total,
            Detalles =
            {
                new VentaDetalle
                {
                    Producto = producto,
                    Cantidad = 1,
                    PrecioUnitario = total,
                    Subtotal = total,
                    SubtotalFinal = total
                }
            }
        };

        _context.PlantillasContratoCredito.Add(new PlantillaContratoCredito
        {
            Nombre = $"Plantilla {Guid.NewGuid():N}",
            Activa = true,
            NombreVendedor = "The Bury",
            DomicilioVendedor = "Local 1",
            CiudadFirma = "Ciudad",
            Jurisdiccion = "Provincia",
            InteresMoraDiarioPorcentaje = 1m,
            TextoContrato = "{{Venta.Productos}} {{Venta.Total}}",
            TextoPagare = "{{Venta.Total}}",
            VigenteDesde = DateTime.UtcNow.Date.AddDays(-1)
        });

        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    private static List<JsonElement> LeerProductosSnapshot(ContratoVentaCredito contrato)
    {
        using var document = JsonDocument.Parse(contrato.DatosSnapshotJson);
        return document.RootElement
            .GetProperty("Venta")
            .GetProperty("Productos")
            .EnumerateArray()
            .Select(p => p.Clone())
            .ToList();
    }

    private static List<JsonElement> LeerPlanCuotasSnapshot(ContratoVentaCredito contrato)
    {
        using var document = JsonDocument.Parse(contrato.DatosSnapshotJson);
        return document.RootElement
            .GetProperty("Credito")
            .GetProperty("PlanCuotas")
            .EnumerateArray()
            .Select(c => c.Clone())
            .ToList();
    }

    private static decimal MontoInteresDeCuota(List<JsonElement> cuotas, int numeroCuota) =>
        cuotas.Single(c => c.GetProperty("NumeroCuota").GetInt32() == numeroCuota)
            .GetProperty("MontoInteres").GetDecimal();

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static DetalleSeedData DetalleSeed(string codigo, string nombre, decimal subtotal, decimal subtotalFinal)
        => new(codigo, nombre, subtotal, subtotalFinal);

    private sealed record DetalleSeedData(string Codigo, string Nombre, decimal Subtotal, decimal SubtotalFinal);

    /// <summary>
    /// ML3: <see cref="ContratoVentaCreditoService.ConstruirPlanCuotasAsync"/> ahora delega en
    /// <see cref="IFinancialCalculationService.SimularPlanCredito"/> para el plan aún no
    /// persistido (recargo total, no PMT). En vez de reimplementar la fórmula acá, se
    /// delega a la implementación real — sigue siendo un "stub" en el sentido de que los
    /// demás métodos (no usados por ContratoVentaCreditoService) permanecen simplificados.
    /// </summary>
    private sealed class StubFinancialCalculationService : IFinancialCalculationService
    {
        private readonly FinancialCalculationService _real = new();

        public decimal CalcularCuotaSistemaFrances(decimal monto, decimal tasaMensual, int cuotas)
            => cuotas > 0 ? Math.Round(monto / cuotas, 2, MidpointRounding.AwayFromZero) : 0m;

        public decimal CalcularTotalConInteres(decimal monto, decimal tasaMensual, int cuotas) => monto;

        public decimal CalcularCFTEA(decimal totalAPagar, decimal montoInicial, int cuotas) => 0m;

        public decimal CalcularInteresTotal(decimal monto, decimal tasaMensual, int cuotas) => 0m;

        public decimal ComputePmt(decimal tasaMensual, int cuotas, decimal monto)
            => cuotas > 0 ? Math.Round(monto / cuotas, 2, MidpointRounding.AwayFromZero) : 0m;

        public decimal ComputeFinancedAmount(decimal total, decimal anticipo) => total - anticipo;

        public decimal CalcularCFTEADesdeTasa(decimal tasaMensual) => 0m;

        public SimulacionPlanCreditoDto SimularPlanCredito(
            decimal totalVenta,
            decimal anticipo,
            int cuotas,
            decimal tasaMensual,
            decimal gastosAdministrativos,
            DateTime fechaPrimeraCuota,
            decimal semaforoRatioVerdeMax = 0.08m,
            decimal semaforoRatioAmarilloMax = 0.15m,
            IReadOnlyCollection<int>? cuotasSinRecargo = null)
            => _real.SimularPlanCredito(
                totalVenta, anticipo, cuotas, tasaMensual, gastosAdministrativos, fechaPrimeraCuota,
                semaforoRatioVerdeMax, semaforoRatioAmarilloMax, cuotasSinRecargo);
    }

    /// <summary>
    /// CSR-ML4: doble mínimo de <see cref="IConfiguracionPagoService"/> para el constructor de
    /// <see cref="ContratoVentaCreditoService"/>. <see cref="Planes"/> es <c>null</c> por defecto
    /// (los tests de paridad de subtotales de esta clase no ejercitan la rama de re-simulación:
    /// todos los créditos seedeados en <see cref="SeedVentaCreditoAsync"/> ya tienen <c>Cuotas</c>
    /// persistidas) — devuelve "sin tabla de planes" en ese caso. Los tests P5/P6/P7 (Fase 4) lo
    /// sobrescriben con un plan explícito, incluido <c>CuotasSinRecargo</c>.
    /// </summary>
    private sealed class NoOpConfiguracionPagoService : IConfiguracionPagoService
    {
        public PlanesCreditoPersonalResultado? Planes { get; set; }

        public Task<List<ConfiguracionPagoViewModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago) => throw new NotImplementedException();
        public Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync() => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel> CreateAsync(ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> UpdateAsync(int id, ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<List<ConfiguracionTarjetaViewModel>> GetTarjetasActivasAsync() => throw new NotImplementedException();
        public Task<List<TarjetaActivaVentaResultado>> GetTarjetasActivasParaVentaAsync() => throw new NotImplementedException();
        public Task<ConfiguracionTarjetaViewModel?> GetTarjetaByIdAsync(int id) => throw new NotImplementedException();
        public Task<bool> ValidarDescuento(TipoPago tipoPago, decimal descuento) => throw new NotImplementedException();
        public Task<decimal> CalcularRecargo(TipoPago tipoPago, decimal monto) => throw new NotImplementedException();
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoAsync() => throw new NotImplementedException();
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoActivosAsync() => throw new NotImplementedException();
        public Task GuardarCreditoPersonalAsync(CreditoPersonalConfigViewModel config) => throw new NotImplementedException();
        public Task<ParametrosCreditoCliente> ObtenerParametrosCreditoClienteAsync(int clienteId, decimal tasaGlobal) => throw new NotImplementedException();
        public Task<(int Min, int Max, string Descripcion, string? PerfilNombre)> ResolverRangoCuotasAsync(
            MetodoCalculoCredito metodo, int? perfilId, int? clienteId) => throw new NotImplementedException();
        public Task<MaxCuotasSinInteresResultado?> ObtenerMaxCuotasSinInteresEfectivoAsync(
            int tarjetaId, IEnumerable<int> productoIds) => throw new NotImplementedException();
        public Task<List<MontoPorPuntajeCreditoViewModel>> GetMontosPorPuntajeAsync() => throw new NotImplementedException();
        public Task<(bool Ok, List<string> Errores)> GuardarMontosPorPuntajeAsync(
            List<MontoPorPuntajeCreditoViewModel> items, string usuario) => throw new NotImplementedException();
        public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalAsync() => throw new NotImplementedException();
        public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalActivasAsync() => throw new NotImplementedException();
        public Task<PlanesCreditoPersonalResultado> ResolverPlanesCreditoPersonalAsync(IEnumerable<int> productoIds) =>
            Task.FromResult(Planes ?? PlanesCreditoPersonalResultado.SinTablaDePlanes());
        public Task<(bool Ok, List<string> Errores)> GuardarCuotasCreditoPersonalAsync(
            List<CuotaCreditoPersonalViewModel> items, string usuario) => throw new NotImplementedException();
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "TheBuryProject.Tests";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
