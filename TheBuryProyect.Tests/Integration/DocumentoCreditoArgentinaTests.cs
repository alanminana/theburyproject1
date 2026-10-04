using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Documentos;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Configuración documental de la venta a crédito (presupuesto, pagaré, contrato y recibo de cobranza) sobre el motor existente:
/// casos A–J de la especificación, más la verificación de la configuración, la numeración, el vencimiento del pagaré y el PDF.
/// </summary>
public class DocumentoCreditoArgentinaTests : DocumentoTestBase
{
    private const string Dia = "dd/MM/yyyy";

    private async Task ConfigurarAsync()
    {
        // Datos del vendedor de ejemplo sin cargar: la configuración inicial los completa con los de los documentos de referencia.
        Context.PlantillasContratoCredito.Add(new PlantillaContratoCredito
        {
            Nombre = "Plantilla base", Activa = true, NombreVendedor = "Nombre del vendedor / comercio", DomicilioVendedor = "Domicilio del vendedor",
            CiudadFirma = "Ciudad", Jurisdiccion = "Provincia", InteresMoraDiarioPorcentaje = 0.05m,
            TextoContrato = "x", TextoPagare = "x", VigenteDesde = DateTime.UtcNow.Date.AddDays(-1)
        });
        await Context.SaveChangesAsync();
        await DocumentoConfiguracionCredito.ConfigurarAsync(Context, NullLogger.Instance);
    }

    /// <summary>Venta a crédito parametrizable: total, monto financiado (la diferencia es la entrega) y plan de cuotas.</summary>
    private async Task<Venta> VentaAsync(
        decimal total, decimal montoFinanciado, int cuotas, decimal importeCuota, bool fiador = false,
        string? numero = null, string dni = "7701419")
    {
        var cliente = new Cliente
        {
            Apellido = "Fucillo", Nombre = "Omar", TipoDocumento = "DNI", NumeroDocumento = dni,
            Domicilio = "9 DE JULIO SEC QUINTAS N°: S/N", Localidad = "ABBOTT", CodigoPostal = "7228", Telefono = "2227-441122"
        };
        var categoria = new Categoria { Nombre = $"Cat {Guid.NewGuid():N}", Codigo = $"C{Guid.NewGuid():N}"[..8] };
        var subcategoria = await Context.Categorias.FirstOrDefaultAsync(c => c.Nombre == "Lavarropas") ?? new Categoria { Nombre = "Lavarropas", Codigo = $"S{Guid.NewGuid():N}"[..8] };
        var marca = await Context.Marcas.FirstOrDefaultAsync(m => m.Nombre == "Drean") ?? new Marca { Nombre = "Drean", Codigo = $"M{Guid.NewGuid():N}"[..8] };
        var producto = new Producto
        {
            Codigo = Context.Productos.Any(p => p.Codigo == "LAV-0001") ? $"LAV-{Guid.NewGuid():N}"[..10] : "LAV-0001", Nombre = "Lavarropas Next 8.12", Categoria = categoria, Subcategoria = subcategoria, Marca = marca,
            PrecioCompra = 0, PrecioVenta = total, PorcentajeIVA = 21m
        };

        var venta = new Venta
        {
            Numero = numero ?? $"{Random.Shared.Next(100000, 999999):D12}", Cliente = cliente, TipoPago = TipoPago.CreditoPersonal,
            Estado = EstadoVenta.Presupuesto, FechaVenta = new DateTime(2026, 9, 2), Subtotal = total, Total = total
        };
        venta.Detalles.Add(new VentaDetalle { Producto = producto, Cantidad = 1, PrecioUnitario = total, Subtotal = total, SubtotalFinal = total });

        var credito = new Credito
        {
            Cliente = cliente, Numero = $"CRE-{Guid.NewGuid():N}"[..10], MontoSolicitado = montoFinanciado, MontoAprobado = montoFinanciado,
            SaldoPendiente = cuotas * importeCuota, TasaInteres = 0m, CantidadCuotas = cuotas, MontoCuota = importeCuota,
            TotalAPagar = cuotas * importeCuota, FechaPrimeraCuota = new DateTime(2026, 10, 2), RequiereGarante = fiador
        };
        for (var i = 1; i <= cuotas; i++)
            credito.Cuotas.Add(new Cuota
            {
                NumeroCuota = i, MontoCapital = importeCuota, MontoInteres = 0m, MontoTotal = importeCuota,
                FechaVencimiento = credito.FechaPrimeraCuota!.Value.AddMonths(i - 1)
            });
        if (fiador)
            credito.Garante = new Garante
            {
                Cliente = cliente, Nombre = "Maria", Apellido = "Gomez", TipoDocumento = "DNI", NumeroDocumento = "20999888",
                Domicilio = "Belgrano 456", Relacion = "Hermana"
            };
        venta.Credito = credito;

        Context.Ventas.Add(venta);
        await Context.SaveChangesAsync();
        return venta;
    }

    private async Task<PagoCuota> PagoAsync(Venta venta, int numeroCuota, decimal importe)
    {
        var cuota = await Context.Cuotas.FirstAsync(c => c.CreditoId == venta.CreditoId && c.NumeroCuota == numeroCuota);
        var pago = new PagoCuota
        {
            CuotaId = cuota.Id, FechaPagoComercial = new DateOnly(2026, 10, 2), ImporteTotal = importe, ImporteAplicadoCuota = importe,
            ImporteAplicadoPunitorio = 0m, MedioPago = "Efectivo", Origen = OrigenPagoCuota.RegistradoPorSistema,
            Estado = EstadoPagoCuota.Aplicado, HistorialCompleto = true
        };
        Context.PagosCuota.Add(pago);
        await Context.SaveChangesAsync();
        return pago;
    }

    private Task<DocumentoEventoResultado> PresupuestoAsync(int ventaId) => EmitirVentaAsync(ventaId, EventosDocumentales.PresupuestoVentaGenerado);

    private Task<DocumentoEventoResultado> CobranzaAsync(params PagoCuota[] pagos)
        => Motor.ProcesarEventoAsync(new DocumentoEventoRequest
        {
            Evento = EventosDocumentales.PagoRegistrado,
            Origen = new DocumentoOrigen { PagoCuotaId = pagos.Min(p => p.Id), PagoCuotaIds = pagos.Select(p => p.Id).OrderBy(i => i).ToList() }
        });

    private async Task<DocumentoGenerado> TipoAsync(DocumentoEventoResultado r, string codigoTipo)
    {
        var tipo = await Context.TiposDocumento.AsNoTracking().FirstAsync(t => t.Codigo == codigoTipo);
        return r.Generados.Single(d => d.TipoDocumentoId == tipo.Id);
    }

    // ------------------------------------------------------------------ Configuración

    [Fact]
    public async Task LaConfiguracionInicial_CreaExactamenteTiposPlantillasPaqueteYReglasPedidos()
    {
        await ConfigurarAsync();

        Assert.Equal(new[] { "CONTRATO", "PAGARE", "PRESUPUESTO", "RECIBO" },
            (await Context.TiposDocumento.Select(t => t.Codigo).ToListAsync()).OrderBy(x => x));

        var plantillas = await Context.PlantillasDocumento.Include(p => p.Versiones).ToListAsync();
        Assert.Equal(new[] { "CONTRATO_VENTA_CREDITO", "PAGARE_CREDITO", "PRESUPUESTO_CREDITO", "RECIBO_COBRANZA" },
            plantillas.Select(p => p.Codigo).OrderBy(x => x));
        Assert.All(plantillas, p =>
        {
            Assert.True(p.Activa);
            Assert.Equal(1, p.VersionActual);
            Assert.Equal(1, p.Versiones.Count);
        });
        Assert.Equal("Presupuesto Crédito", plantillas.Single(p => p.Codigo == "PRESUPUESTO_CREDITO").Nombre);
        Assert.Equal("Contrato de Venta Crédito", plantillas.Single(p => p.Codigo == "CONTRATO_VENTA_CREDITO").Nombre);
        Assert.Equal("Pagaré Crédito", plantillas.Single(p => p.Codigo == "PAGARE_CREDITO").Nombre);
        Assert.Equal("Recibo de Cobranza", plantillas.Single(p => p.Codigo == "RECIBO_COBRANZA").Nombre);

        var paquete = await Context.PaquetesDocumentales.Include(p => p.Items).ThenInclude(i => i.PlantillaDocumento).SingleAsync();
        Assert.Equal("DOCUMENTACION_CREDITO", paquete.Codigo);
        Assert.Equal("Documentación de Crédito", paquete.Nombre);
        Assert.Equal(new[] { "PAGARE_CREDITO", "CONTRATO_VENTA_CREDITO" },
            paquete.Items.OrderBy(i => i.Orden).Select(i => i.PlantillaDocumento.Codigo));

        var reglas = await Context.ReglasDocumento.Include(r => r.PlantillaDocumento).ToListAsync();
        Assert.Equal(new[] { "Contrato Venta Crédito", "Pagaré Crédito", "Presupuesto Crédito", "Recibo Cobranza" }, reglas.Select(r => r.Nombre).OrderBy(x => x));
        Assert.All(reglas, r => Assert.True(r.Activa));
        Assert.Equal(EventosDocumentales.PresupuestoVentaGenerado, reglas.Single(r => r.Nombre == "Presupuesto Crédito").EventoCodigo);
        Assert.Equal(EventosDocumentales.ContratoCreditoSolicitado, reglas.Single(r => r.Nombre == "Contrato Venta Crédito").EventoCodigo);
        Assert.Equal(EventosDocumentales.ContratoCreditoSolicitado, reglas.Single(r => r.Nombre == "Pagaré Crédito").EventoCodigo);
        Assert.Equal(EventosDocumentales.PagoRegistrado, reglas.Single(r => r.Nombre == "Recibo Cobranza").EventoCodigo);
        // Todas las condiciones y plantillas son válidas para el motor.
        Assert.All(reglas, r => Assert.True(CondicionDocumentoParser.Parsear(r.CondicionJson).EsValida));
    }

    [Fact]
    public async Task LaConfiguracionInicial_EsIdempotente_YNoPisaLoExistente()
    {
        await ConfigurarAsync();
        var plantilla = await PlantillaAsync("PAGARE_CREDITO");
        plantilla.Nombre = "Mi pagaré";
        await Context.SaveChangesAsync();

        var segunda = await DocumentoConfiguracionCredito.ConfigurarAsync(Context, NullLogger.Instance);

        Assert.Empty(segunda.Creado);
        Assert.Equal(4, await Context.TiposDocumento.CountAsync());
        Assert.Equal(4, await Context.PlantillasDocumento.CountAsync());
        Assert.Equal(1, await Context.PaquetesDocumentales.CountAsync());
        Assert.Equal(4, await Context.ReglasDocumento.CountAsync());
        Assert.Equal("Mi pagaré", (await PlantillaAsync("PAGARE_CREDITO")).Nombre);
    }

    [Fact]
    public async Task TodasLasPlantillas_SonValidasParaElCatalogo()
    {
        foreach (var texto in new[]
                 {
                     DocumentoConfiguracionCredito.TextoPresupuesto, DocumentoConfiguracionCredito.TextoPagare,
                     DocumentoConfiguracionCredito.TextoContrato, DocumentoConfiguracionCredito.TextoRecibo
                 })
            Assert.Empty(PlantillaRenderer.Validar(texto));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LosDatosDeLaEmpresa_ReproducenLosDocumentosDeReferencia_SinPisarLoCargado()
    {
        await ConfigurarAsync();
        var empresa = await Context.EmpresasConfiguracion.SingleAsync();

        Assert.Equal("Javier Martinez", empresa.Nombre);
        Assert.Equal("37.898.524", empresa.Dni);
        Assert.Equal("20-37898524-3", empresa.Cuit);
        Assert.Equal("ELECTRONICA MARTINEZ", empresa.NombreComercial);
        Assert.Equal("Av. San Martín 443", empresa.Domicilio);
        Assert.Equal("Av. San Martín n° 443", empresa.DomicilioCompleto);
        Assert.Equal("Monte", empresa.Ciudad);
        Assert.Equal(0.20m, empresa.InteresMoraDiarioPorcentaje);
        Assert.Equal("Consumidor Final", empresa.CondicionFiscalClientePorDefecto);
        Assert.Null(empresa.PagareVencimientoModo);      // la regla del vencimiento NO se asume

        empresa.Nombre = "Otro Titular";
        await Context.SaveChangesAsync();
        await DocumentoConfiguracionCredito.ConfigurarAsync(Context, NullLogger.Instance);
        Assert.Equal("Otro Titular", (await Context.EmpresasConfiguracion.SingleAsync()).Nombre);
    }

    // ------------------------------------------------------------------ CASO A: entrega + 2 cuotas

    [Fact]
    public async Task CasoA_ConEntrega_PresupuestoMuestraEntregaYPlan_ContratoYPagareSoloElSaldoFinanciado()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(total: 90969m, montoFinanciado: 60646m, cuotas: 2, importeCuota: 30323m);

        var presupuesto = await TipoAsync(await PresupuestoAsync(venta.Id), "PRESUPUESTO");
        Assert.Contains("ELECTRONICA MARTINEZ", presupuesto.ContenidoRenderizado);
        Assert.Contains("Av. San Martín 443, MONTE", presupuesto.ContenidoRenderizado);
        Assert.Contains($"Presupuesto N°: {venta.Numero}", presupuesto.ContenidoRenderizado);
        Assert.Contains("Fecha: 02/09/2026", presupuesto.ContenidoRenderizado);
        Assert.Contains("Condición: Crédito", presupuesto.ContenidoRenderizado);
        Assert.Contains("Entrega: $ 30323,00", presupuesto.ContenidoRenderizado);
        Assert.Contains("1¦02/10/2026¦$ 30323,00", presupuesto.ContenidoRenderizado);
        Assert.Contains("2¦02/11/2026¦$ 30323,00", presupuesto.ContenidoRenderizado);

        var r = await EmitirVentaAsync(venta.Id);
        var contrato = await TipoAsync(r, "CONTRATO");
        var pagare = await TipoAsync(r, "PAGARE");

        Assert.Contains("la suma de Pesos: Sesenta Mil Seiscientos Cuarenta y Seis Pesos ($ 60646,00) pagaderos", contrato.ContenidoRenderizado);
        Assert.Contains("Pago: 01 Vencimiento: 02/10/2026 Forma: Cuotas $ 30323,00", contrato.ContenidoRenderizado);
        Assert.Contains("Pago: 02 Vencimiento: 02/11/2026 Forma: Cuotas $ 30323,00", contrato.ContenidoRenderizado);
        Assert.DoesNotContain("Pago: 03", contrato.ContenidoRenderizado);
        Assert.DoesNotContain("90969", contrato.ContenidoRenderizado);       // venta.total no es el saldo financiado
        Assert.DoesNotContain("Entrega", contrato.ContenidoRenderizado);      // la entrega ya pagada no figura como deuda

        Assert.Contains("Por $ 60646,00.-", pagare.ContenidoRenderizado);
        Assert.Contains("La cantidad de pesos: Sesenta Mil Seiscientos Cuarenta y Seis Pesos.-", pagare.ContenidoRenderizado);
        Assert.DoesNotContain("90969", pagare.ContenidoRenderizado);
    }

    // ------------------------------------------------------------------ CASO B: sin entrega, 6 cuotas

    [Fact]
    public async Task CasoB_SinEntrega_6Cuotas_ElSaldoEsLaSumaDeLasCuotas()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(total: 1130148m, montoFinanciado: 1130148m, cuotas: 6, importeCuota: 188358m);

        var presupuesto = await TipoAsync(await PresupuestoAsync(venta.Id), "PRESUPUESTO");
        Assert.Contains("Entrega: $ 0,00", presupuesto.ContenidoRenderizado);
        Assert.Equal(6, System.Text.RegularExpressions.Regex.Matches(presupuesto.ContenidoRenderizado, @"(?m)^\d¦\d\d/\d\d/\d{4}¦\$ 188358,00").Count);

        var r = await EmitirVentaAsync(venta.Id);
        var contrato = await TipoAsync(r, "CONTRATO");
        var pagare = await TipoAsync(r, "PAGARE");

        Assert.Contains("Pesos: Un Millón Ciento Treinta Mil Ciento Cuarenta y Ocho Pesos ($ 1130148,00)", contrato.ContenidoRenderizado);
        Assert.Equal(6, contrato.ContenidoRenderizado.Split('\n').Count(l => l.StartsWith("Pago: ")));
        Assert.Contains("Pago: 06 Vencimiento: 02/03/2027 Forma: Cuotas $ 188358,00", contrato.ContenidoRenderizado);
        Assert.Contains("Por $ 1130148,00.-", pagare.ContenidoRenderizado);
        Assert.Contains("Un Millón Ciento Treinta Mil Ciento Cuarenta y Ocho Pesos.-", pagare.ContenidoRenderizado);
    }

    // ------------------------------------------------------------------ CASOS C y D: fiador

    [Fact]
    public async Task CasoC_ConFiador_LaClausulaQuintaSaleCompleta()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m, fiador: true);

        var contrato = await TipoAsync(await EmitirVentaAsync(venta.Id), "CONTRATO");

        Assert.Contains("El señor: Gomez Maria con domicilio Belgrano 456 y D.N.I. N° 20.999.888 se constituye en fiador solidario y principal pagador, con expresa renuncia a los beneficios de división y exclusión, por el cumplimiento de todas y cada una de las cláusulas del presente contrato de compraventa.",
            contrato.ContenidoRenderizado);
        Assert.DoesNotContain("....", contrato.ContenidoRenderizado.Split("** QUINTA")[1].Split("En prueba de conformidad")[0]);
    }

    [Fact]
    public async Task CasoD_SinFiador_MantieneElEspacioParaCompletar_SinTextoTecnico()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m, fiador: false);

        var r = await EmitirVentaAsync(venta.Id);
        var contrato = await TipoAsync(r, "CONTRATO");
        var pagare = await TipoAsync(r, "PAGARE");

        Assert.Contains("El señor: ............................................................ constituye en fiador solidario y principal pagador",
            contrato.ContenidoRenderizado);
        foreach (var texto in new[] { contrato.ContenidoRenderizado, pagare.ContenidoRenderizado })
        {
            Assert.DoesNotContain("undefined", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("null", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("N/A", texto);
            Assert.DoesNotContain("{{", texto);
        }
    }

    // ------------------------------------------------------------------ Texto contractual

    [Fact]
    public async Task ElContrato_MantieneLasClausulasDelModeloYTodosSusDatosSalenDelERP()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m);

        var r = await EmitirVentaAsync(venta.Id);
        var contrato = (await TipoAsync(r, "CONTRATO")).ContenidoRenderizado;
        var pagare = (await TipoAsync(r, "PAGARE")).ContenidoRenderizado;

        Assert.Contains($"Contrato de Venta N°: {venta.Numero}", contrato);
        Assert.Contains("Entre Javier Martinez con domicilio en Av. San Martín n° 443 de la ciudad de Monte, DNI 37.898.524 (CUIT 20-37898524-3) en adelante denominado EL VENDEDOR, por una parte y por la otra el señor: Fucillo Omar con domicilio 9 DE JULIO SEC QUINTAS N°: S/N - ABBOTT (7228) y D.N.I. N° 7.701.419 en adelante denominado EL COMPRADOR, se conviene a celebrar el presente contrato de compraventa de acuerdo a las siguientes cláusulas y condiciones.", contrato);
        Assert.Contains("(1) Drean - Lavarropas Next 8.12", contrato);
        Assert.Contains("El lugar de pago se fija en el domicilio del Vendedor. La falta de pago en término de una sola de las cuotas determinará la caducidad y vencimiento de todos los plazos por el presente contrato se establecen y dará derecho al vendedor a exigir la totalidad del saldo adeudado al comprador sin necesidad de intimación extrajudicial, pactándose en forma expresa la vía ejecutiva para perseguir dicho cobro.", contrato);
        Assert.Contains("se devengue un interés diario a favor del vendedor del 0,20% sobre las cuotas adeudadas y/o el saldo del precio adeudado.", contrato);
        Assert.Contains("se someten a la jurisdicción y competencia de los tribunales ordinarios de justicia de La Plata, con renuncia expresa a cualquier otro fuero o jurisdicción que pudiere corresponder.", contrato);
        Assert.Contains("En prueba de conformidad se firman dos ejemplares de un mismo tenor y a los mismos efectos en la ciudad de MONTE a los 2 días del mes de Septiembre del 2026.-", contrato);

        Assert.Contains("MONTE, 2 de Septiembre del 2026, pagaré sin protesto (art.50 D.Ley 5965/63) al Señor JAVIER MARTINEZ a su orden La cantidad de pesos: Novecientos Pesos.- por igual valor recibido en mercaderías a entera satisfacción pagadero en Av. San Martín 443 de la ciudad de MONTE.-", pagare);
        Assert.Contains("Firmante: (", pagare);
        Assert.Contains("FUCILLO OMAR", pagare);
        Assert.Contains("Localidad: ABBOTT - Telefono: 2227-441122", pagare);
        Assert.Contains("CLAUSULA SIN PROTESTO: Respecto del pagaré que luce precedentemente se pacta la cláusula 'sin protesto', de modo que el tomador y tenedores sucesivos quedan dispensados de formalizar el protesto por falta de pago (art. 50, dec. ley 5.965/63, ratificado por ley 16.478) y les confiere vía ejecutiva en caso de no ser pagado a su vencimiento", pagare);
    }

    [Fact]
    public async Task ElDatoDelCliente_ArmaLaIdentificacionComoLosComprobantesDeReferencia()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m);

        var presupuesto = (await TipoAsync(await PresupuestoAsync(venta.Id), "PRESUPUESTO")).ContenidoRenderizado;

        var cliente = await Context.Clientes.SingleAsync();
        Assert.Contains($"Cliente: ({cliente.Id:D5}) FUCILLO OMAR", presupuesto);
        Assert.Contains("Calle: 9 DE JULIO SEC QUINTAS N°: S/N - ABBOTT (7228)", presupuesto);
        Assert.Contains("Consumidor Final - Tipo Documento: D.N.I. N°: 7.701.419", presupuesto);
        Assert.Contains("¦Drean¦LAV-0001¦Lavarropas¦Lavarropas Next 8.12¦1", presupuesto);
    }

    // ------------------------------------------------------------------ Numeración y paquete

    [Fact]
    public async Task PresupuestoPagareYContrato_ComparteElNumeroDeOperacion_YElReciboTieneNumeracionPropia()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m, numero: "000000069381");

        var presupuesto = await TipoAsync(await PresupuestoAsync(venta.Id), "PRESUPUESTO");
        var r = await EmitirVentaAsync(venta.Id);
        var pagare = await TipoAsync(r, "PAGARE");
        var contrato = await TipoAsync(r, "CONTRATO");

        Assert.Contains("Presupuesto N°: 000000069381", presupuesto.ContenidoRenderizado);
        Assert.Contains("N° 000000069381", pagare.ContenidoRenderizado);
        Assert.Contains("Contrato de Venta N°: 000000069381", contrato.ContenidoRenderizado);
        // Cada GeneratedDocument conserva su identidad y su número interno propios.
        Assert.Equal(3, new[] { presupuesto.Id, pagare.Id, contrato.Id }.Distinct().Count());
        Assert.NotEqual(pagare.Numero, contrato.Numero);

        var recibo = await TipoAsync(await CobranzaAsync(await PagoAsync(venta, 1, 300m)), "RECIBO");
        Assert.StartsWith("REC-", recibo.Numero);
        Assert.Contains($"RECIBO N°: {recibo.Numero}", recibo.ContenidoRenderizado);
        Assert.DoesNotContain("000000069381\n", recibo.ContenidoRenderizado.Split("APLICADO A:")[0]);
    }

    [Fact]
    public async Task ElPaquete_OrdenaLaEmision_PagareAntesQueContrato_EnUnMismoGrupoDeImpresion()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m);

        var r = await EmitirVentaAsync(venta.Id);

        Assert.Equal(2, r.Generados.Count);
        Assert.Equal("PAGARE_CREDITO", (await PlantillaPorIdAsync(r.Generados[0].PlantillaDocumentoId)).Codigo);
        Assert.Equal("CONTRATO_VENTA_CREDITO", (await PlantillaPorIdAsync(r.Generados[1].PlantillaDocumentoId)).Codigo);
        Assert.True(r.Generados[0].Id < r.Generados[1].Id);
        Assert.Equal(r.Generados[0].GrupoImpresionId, r.Generados[1].GrupoImpresionId);
        Assert.Equal("Documentación de Crédito", (await Motor.ObtenerNombresDePaqueteAsync(r.Generados)).Single().Value);

        // Cambiar el orden del paquete cambia el orden en que se emite (la configuración manda).
        var paquete = await Context.PaquetesDocumentales.Include(p => p.Items).SingleAsync();
        foreach (var item in paquete.Items)
            item.Orden = item.Orden == 1 ? 2 : 1;
        await Context.SaveChangesAsync();

        var venta2 = await VentaAsync(900m, 900m, 3, 300m, dni: "7701420");
        var r2 = await EmitirVentaAsync(venta2.Id);
        Assert.Equal("CONTRATO_VENTA_CREDITO", (await PlantillaPorIdAsync(r2.Generados[0].PlantillaDocumentoId)).Codigo);
    }

    private Task<PlantillaDocumento> PlantillaPorIdAsync(int id) => Context.PlantillasDocumento.AsNoTracking().FirstAsync(p => p.Id == id);

    [Fact]
    public async Task LaImpresionCombinada_UneElPagareYElContratoEnUnSoloPdf_YCadaUnoSigueSiendoIndependiente()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m);
        var r = await EmitirVentaAsync(venta.Id);
        var ids = r.Generados.Select(d => d.Id).ToList();

        var archivo = await Motor.VerPdfAsync(ids);

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(archivo.Contenido, 0, 4));
        var docs = await Context.DocumentosGenerados.Include(d => d.PlantillaDocumento).Where(d => ids.Contains(d.Id)).ToListAsync();
        var paginas = CapturarPaginas(docs.OrderBy(d => ids.IndexOf(d.Id)).ToList());
        Assert.InRange(paginas.Count, 1, 2);      // sobrio y compacto: pagaré + contrato en una o dos páginas
        Assert.Equal(2, await Context.DocumentosGenerados.CountAsync(d => ids.Contains(d.Id)));
    }

    // ------------------------------------------------------------------ Vencimiento del pagaré

    [Fact]
    public async Task ElVencimientoDelPagare_NoSeAsume_SeCompletaAManoHastaQueSeConfigure()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m);

        var pagare = await TipoAsync(await EmitirVentaAsync(venta.Id), "PAGARE");

        Assert.Contains("Vence el: ..............................................", pagare.ContenidoRenderizado);
        Assert.DoesNotContain("02/10/2026", pagare.ContenidoRenderizado);
    }

    [Theory]
    [InlineData(PagareVencimientoModos.PrimeraCuota, null, "Vence el: 2 de Octubre del 2026")]
    [InlineData(PagareVencimientoModos.UltimaCuota, null, "Vence el: 2 de Diciembre del 2026")]
    [InlineData(PagareVencimientoModos.DiasDesdeOperacion, 45, "Vence el: 17 de Octubre del 2026")]
    public async Task ElVencimientoDelPagare_SigueLaEstrategiaConfigurada(string modo, int? dias, string esperado)
    {
        await ConfigurarAsync();
        var empresa = await Context.EmpresasConfiguracion.SingleAsync();
        empresa.PagareVencimientoModo = modo;
        empresa.PagareVencimientoDias = dias;
        await Context.SaveChangesAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m);

        var pagare = await TipoAsync(await EmitirVentaAsync(venta.Id), "PAGARE");

        Assert.Contains(esperado, pagare.ContenidoRenderizado);
    }

    // ------------------------------------------------------------------ Recibo

    [Fact]
    public async Task CasoE_PagoDeLaCuota5De6_ElReciboMuestraCuota5Sobre6_LaOperacionYElImporte()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(744234m, 744234m, 6, 124039m, numero: "000000069193");
        var pago = await PagoAsync(venta, 5, 124039m);

        var recibo = (await TipoAsync(await CobranzaAsync(pago), "RECIBO")).ContenidoRenderizado;

        Assert.Contains("02/10/2026¦Cuota: 5/6 Operac N°: 000000069193¦124039,00", recibo);
        Assert.Contains("Efectivo¦¦¦¦124039,00", recibo);
        Assert.Contains("Monto total: 124039,00", recibo);
        Assert.Contains("Son Pesos: $124039,00", recibo);
        Assert.Contains("Recibí conforme la suma de Ciento Veinticuatro Mil Treinta y Nueve Pesos", recibo);
        Assert.Contains("Cliente: (", recibo);
        Assert.Contains("Consumidor Final - Tipo Documento: D.N.I. N°: 7.701.419", recibo);
        Assert.Contains("Fecha: 02/10/2026", recibo);
    }

    [Fact]
    public async Task CasoF_PagoParcial_ElReciboMuestraLoRealmenteCobrado()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m);
        var pago = await PagoAsync(venta, 1, 100m);

        var recibo = (await TipoAsync(await CobranzaAsync(pago), "RECIBO")).ContenidoRenderizado;

        Assert.Contains("Cuota: 1/3 Operac N°:", recibo);
        Assert.Contains("¦100,00", recibo);
        Assert.Contains("Monto total: 100,00", recibo);
        Assert.Contains("Recibí conforme la suma de Cien Pesos", recibo);
        Assert.DoesNotContain("300,00", recibo);
    }

    [Fact]
    public async Task CasoG_UnaCobranzaAplicadaAVariasCuotas_ListaTodasLasImputacionesEnUnSoloRecibo()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m);
        var pago1 = await PagoAsync(venta, 1, 300m);
        var pago2 = await PagoAsync(venta, 2, 300m);

        var r = await CobranzaAsync(pago1, pago2);

        var recibo = Assert.Single(r.Generados);
        Assert.Contains("Cuota: 1/3 Operac N°:", recibo.ContenidoRenderizado);
        Assert.Contains("Cuota: 2/3 Operac N°:", recibo.ContenidoRenderizado);
        Assert.Contains("Monto total: 600,00", recibo.ContenidoRenderizado);
        Assert.Contains("Recibí conforme la suma de Seiscientos Pesos", recibo.ContenidoRenderizado);
        Assert.Equal(pago1.Id, recibo.PagoCuotaId);
        Assert.Contains("\"pagoCuotaIds\"", recibo.MetadataJson);
    }

    // ------------------------------------------------------------------ Idempotencia y separación de eventos

    [Fact]
    public async Task CasoH_ReintentarLaConfirmacion_NoDuplicaElContratoNiElPagare()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m);

        await EmitirVentaAsync(venta.Id);
        var segunda = await EmitirVentaAsync(venta.Id);

        Assert.Empty(segunda.Generados);
        var tipoContrato = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "CONTRATO");
        var tipoPagare = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "PAGARE");
        Assert.Equal(1, await Context.DocumentosGenerados.CountAsync(d => d.TipoDocumentoId == tipoContrato.Id));
        Assert.Equal(1, await Context.DocumentosGenerados.CountAsync(d => d.TipoDocumentoId == tipoPagare.Id));
    }

    [Fact]
    public async Task CasoI_ReintentarElPago_NoDuplicaElRecibo()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m);
        var pago1 = await PagoAsync(venta, 1, 300m);
        var pago2 = await PagoAsync(venta, 2, 300m);

        await CobranzaAsync(pago1, pago2);
        var otra = await CobranzaAsync(pago1, pago2);
        var soloAncla = await EmitirPagoAsync(pago1.Id);

        Assert.Empty(otra.Generados);
        Assert.Empty(soloAncla.Generados);
        var tipoRecibo = await Context.TiposDocumento.FirstAsync(t => t.Codigo == "RECIBO");
        Assert.Equal(1, await Context.DocumentosGenerados.CountAsync(d => d.TipoDocumentoId == tipoRecibo.Id));
    }

    [Fact]
    public async Task CadaEvento_GeneraSoloLoQueLeCorresponde()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m);

        // Confirmar la venta no emite recibo, ni contrato/pagaré (eso es del paso de contrato).
        Assert.Empty((await EmitirVentaAsync(venta.Id, EventosDocumentales.VentaConfirmada)).Generados);
        // El presupuesto no emite recibo, contrato ni pagaré.
        var p = await PresupuestoAsync(venta.Id);
        Assert.Equal("PRESUPUESTO", (await Context.TiposDocumento.FindAsync(Assert.Single(p.Generados).TipoDocumentoId))!.Codigo);
        // El contrato no emite recibo ni presupuesto.
        var c = await EmitirVentaAsync(venta.Id);
        Assert.Equal(new[] { "CONTRATO", "PAGARE" }, c.Generados.Select(d => Context.TiposDocumento.Find(d.TipoDocumentoId)!.Codigo).OrderBy(x => x));
        // Un pago solo emite el recibo.
        var pago = await PagoAsync(venta, 1, 300m);
        var cobro = await CobranzaAsync(pago);
        Assert.Equal("RECIBO", (await Context.TiposDocumento.FindAsync(Assert.Single(cobro.Generados).TipoDocumentoId))!.Codigo);
        Assert.Equal(4, await Context.DocumentosGenerados.CountAsync());
    }

    [Fact]
    public async Task UnaVentaAContado_NoGeneraNingunDocumentoDeCredito()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m);
        venta.TipoPago = TipoPago.Efectivo;
        await Context.SaveChangesAsync();

        Assert.Empty((await PresupuestoAsync(venta.Id)).Generados);
        Assert.Empty((await EmitirVentaAsync(venta.Id)).Generados);
    }

    // ------------------------------------------------------------------ CASO J e histórico

    [Fact]
    public async Task CasoJ_CambiarLaPlantilla_NoModificaLosDocumentosHistoricos_YLosNuevosUsanLaNuevaVersion()
    {
        await ConfigurarAsync();
        var venta1 = await VentaAsync(900m, 900m, 3, 300m);
        var original = await TipoAsync(await EmitirVentaAsync(venta1.Id), "PAGARE");
        var textoOriginal = original.ContenidoRenderizado;
        var hashOriginal = original.ContentHash;

        var plantilla = await Config.ObtenerPlantillaAsync((await PlantillaAsync("PAGARE_CREDITO")).Id);
        var version = plantilla!.Versiones.Single(v => v.Numero == plantilla.VersionActual);
        await Config.GuardarPlantillaAsync(new PlantillaDocumentoInput
        {
            Id = plantilla.Id, TipoDocumentoId = plantilla.TipoDocumentoId, Codigo = plantilla.Codigo, Nombre = plantilla.Nombre,
            Activa = true, VigenteDesde = plantilla.VigenteDesde, RequiereFirma = plantilla.RequiereFirma,
            FirmantesRequeridos = plantilla.FirmantesRequeridos, Copias = plantilla.Copias, VariablesRequeridas = version.VariablesRequeridas,
            Contenido = version.Contenido.Replace("CLAUSULA SIN PROTESTO", "CLÁUSULA SIN PROTESTO (versión 2)")
        });

        var venta2 = await VentaAsync(900m, 900m, 3, 300m, dni: "7701420");
        var nuevo = await TipoAsync(await EmitirVentaAsync(venta2.Id), "PAGARE");

        var historico = await Context.DocumentosGenerados.AsNoTracking().FirstAsync(d => d.Id == original.Id);
        Assert.Equal(textoOriginal, historico.ContenidoRenderizado);
        Assert.Equal(hashOriginal, historico.ContentHash);
        Assert.DoesNotContain("versión 2", historico.ContenidoRenderizado);
        Assert.Contains("versión 2", nuevo.ContenidoRenderizado);
        Assert.NotEqual(original.PlantillaDocumentoVersionId, nuevo.PlantillaDocumentoVersionId);
        Assert.Equal(2, (await PlantillaAsync("PAGARE_CREDITO")).VersionActual);
    }

    [Fact]
    public async Task LaReimpresion_UsaElSnapshotHistorico_SinReevaluarReglasNiCalcularNada()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m);
        var r = await EmitirVentaAsync(venta.Id);
        var contrato = await TipoAsync(r, "CONTRATO");
        var texto = contrato.ContenidoRenderizado;
        var numerosAntes = await Context.TiposDocumento.AsNoTracking().Select(t => t.UltimoNumero).ToListAsync();

        // Cambia todo lo vivo: cliente, plan y reglas.
        var cliente = await Context.Clientes.SingleAsync();
        cliente.Apellido = "Otro";
        foreach (var cuota in await Context.Cuotas.ToListAsync()) cuota.MontoTotal = 1m;
        foreach (var regla in await Context.ReglasDocumento.ToListAsync()) regla.Activa = false;
        await Context.SaveChangesAsync();

        var archivo = await Motor.ReimprimirAsync(new[] { contrato.Id });

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(archivo.Contenido, 0, 4));
        var despues = await Context.DocumentosGenerados.AsNoTracking().FirstAsync(d => d.Id == contrato.Id);
        Assert.Equal(texto, despues.ContenidoRenderizado);
        Assert.Equal(1, despues.ContadorReimpresiones);
        Assert.Equal(numerosAntes, await Context.TiposDocumento.AsNoTracking().Select(t => t.UltimoNumero).ToListAsync());   // no asignó números nuevos
        Assert.Equal(1, await Context.DocumentosGenerados.CountAsync(d => d.TipoDocumentoId == contrato.TipoDocumentoId));
    }

    // ------------------------------------------------------------------ Vista previa

    [Fact]
    public async Task LaVistaPrevia_FuncionaConUnaOperacionRealYConDatosDeEjemplo_ParaLosCuatroDocumentos()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(900m, 900m, 3, 300m);
        var pago = await PagoAsync(venta, 1, 300m);

        foreach (var (codigo, evento) in new[]
                 {
                     ("PRESUPUESTO_CREDITO", EventosDocumentales.PresupuestoVentaGenerado), ("PAGARE_CREDITO", EventosDocumentales.ContratoCreditoSolicitado),
                     ("CONTRATO_VENTA_CREDITO", EventosDocumentales.ContratoCreditoSolicitado), ("RECIBO_COBRANZA", EventosDocumentales.PagoRegistrado)
                 })
        {
            var plantilla = await Config.ObtenerPlantillaAsync((await PlantillaAsync(codigo)).Id);
            var contenido = plantilla!.Versiones.Single(v => v.Numero == plantilla.VersionActual).Contenido;
            var ejemplo = await Config.PrevisualizarAsync(contenido, null, evento);
            Assert.Empty(ejemplo.ErroresPlantilla);
            Assert.Empty(ejemplo.VariablesNoResueltas);
            Assert.False(ejemplo.UsaDatosReales);

            var real = await Config.PrevisualizarAsync(contenido, null, evento,
                ventaId: evento == EventosDocumentales.PagoRegistrado ? null : venta.Id,
                pagoCuotaId: evento == EventosDocumentales.PagoRegistrado ? pago.Id : null);
            Assert.True(real.UsaDatosReales);
            Assert.True(real.VariablesNoResueltas.Count == 0, $"real {codigo}: {string.Join(" | ", real.VariablesNoResueltas)}");
            Assert.Contains("FUCILLO OMAR", real.Texto, StringComparison.OrdinalIgnoreCase);
        }

        var operaciones = await Config.ListarOperacionesRecientesAsync();
        Assert.Single(operaciones.Ventas);
        Assert.Single(operaciones.Cobros);
    }

    // ------------------------------------------------------------------ Formato impreso

    /// <summary>Páginas en PNG de un conjunto de documentos (para verificar el diseño y, si se pide, guardarlas para revisarlas).</summary>
    private static List<byte[]> CapturarPaginas(IReadOnlyList<DocumentoGenerado> docs, string? nombre = null)
    {
        var paginas = DocumentoPdfService.CrearDocumento(docs).GenerateImages(new ImageGenerationSettings { RasterDpi = 110 }).ToList();
        var carpeta = Environment.GetEnvironmentVariable("DOC_DUMP_DIR");
        if (!string.IsNullOrWhiteSpace(carpeta) && nombre != null)
        {
            Directory.CreateDirectory(carpeta);
            for (var i = 0; i < paginas.Count; i++)
                File.WriteAllBytes(Path.Combine(carpeta, $"{nombre}-{i + 1}.png"), paginas[i]);
        }

        return paginas;
    }

    [Fact]
    public async Task LosCuatroDocumentos_SeImprimenEnFormatoAdministrativo_ConPaginasRazonables()
    {
        await ConfigurarAsync();
        var venta = await VentaAsync(total: 90969m, montoFinanciado: 60646m, cuotas: 2, importeCuota: 30323m, fiador: true, numero: "000000069381");
        var presupuesto = await TipoAsync(await PresupuestoAsync(venta.Id), "PRESUPUESTO");
        var emision = await EmitirVentaAsync(venta.Id);
        var pago = await PagoAsync(venta, 1, 30323m);
        var recibo = await TipoAsync(await CobranzaAsync(pago), "RECIBO");

        async Task<List<DocumentoGenerado>> CargarAsync(params int[] ids)
            => (await Context.DocumentosGenerados.Include(d => d.PlantillaDocumento).Include(d => d.TipoDocumento).Where(d => ids.Contains(d.Id)).ToListAsync())
                .OrderBy(d => Array.IndexOf(ids, d.Id)).ToList();

        Assert.Single(CapturarPaginas(await CargarAsync(presupuesto.Id), "presupuesto"));
        Assert.Single(CapturarPaginas(await CargarAsync(recibo.Id), "recibo"));
        var combinado = CapturarPaginas(await CargarAsync(emision.Generados.Select(d => d.Id).ToArray()), "documentacion-credito");
        Assert.InRange(combinado.Count, 1, 2);
        CapturarPaginas(await CargarAsync(emision.Generados[0].Id), "pagare");
        CapturarPaginas(await CargarAsync(emision.Generados[1].Id), "contrato");
    }
}

public class FormatoArgentinoTests
{
    [Theory]
    [InlineData(60646, "60646,00")]
    [InlineData(188358, "188358,00")]
    [InlineData(1130148.5, "1130148,50")]
    [InlineData(0, "0,00")]
    public void Importe_UsaComaDecimal_SinSeparadorDeMiles_NiFormatoNorteamericano(double valor, string esperado)
        => Assert.Equal(esperado, FormatoArgentino.Importe((decimal)valor));

    [Fact]
    public void Fechas_CortaYTextual()
    {
        Assert.Equal("02/10/2026", FormatoArgentino.Fecha(new DateTime(2026, 10, 2)));
        Assert.Equal("2 de Septiembre del 2026", FormatoArgentino.FechaTexto(new DateTime(2026, 9, 2)));
    }

    [Fact]
    public void Mayusculas_NoModificaElDatoOriginal()
    {
        var original = "Monte";
        Assert.Equal("MONTE", FormatoArgentino.Mayusculas(original));
        Assert.Equal("Monte", original);
    }

    [Fact]
    public void ElFormatoUpper_DeLaPlantilla_ConvierteSoloLaSalida()
    {
        var ctx = new DocumentoContexto();
        ctx.Set("empresa.localidad", "Monte");

        var render = PlantillaRenderer.Renderizar("{{empresa.localidad|upper}} y {{empresa.localidad}} y {{empresa.localidad|lower}}", ctx);

        Assert.Equal("MONTE y Monte y monte", render.Texto);
        Assert.Contains(PlantillaRenderer.Validar("{{empresa.localidad|titulo}}"), e => e.Contains("Formato desconocido"));
    }

    [Fact]
    public void ElLayoutAdministrativo_SeReconoceYSeParseaSinEjecutarNada()
    {
        Assert.True(DocumentoLayout.EsAdministrativo("\n@@admin\nHola"));
        Assert.False(DocumentoLayout.EsAdministrativo("Contrato común sin directivas"));

        var bloques = DocumentoLayout.Parsear("@@admin\n@@cols 60/40\nIzq\n@@col\n>> Der\n@@fincols\n@@linea\n@@tabla 1,2;I,D\nA¦B\n1¦2\n@@fintabla\n@@firmas");
        Assert.Collection(bloques,
            b => Assert.IsType<BloqueColumnas>(b),
            b => Assert.IsType<BloqueLinea>(b),
            b => Assert.IsType<BloqueTabla>(b),
            b => Assert.IsType<BloqueFirmas>(b));
        var columnas = (BloqueColumnas)bloques[0];
        Assert.Equal(2, columnas.Columnas.Count);
        Assert.Equal(AlineacionDocumento.Derecha, ((BloqueTexto)columnas.Columnas[1][0]).Alineacion);
    }
}
