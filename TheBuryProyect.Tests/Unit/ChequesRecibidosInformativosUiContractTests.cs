namespace TheBuryProject.Tests.Unit;

/// <summary>
/// V-02: el cheque recibido de un cliente es un pago pendiente informativo. Fija que la interfaz no vuelva a ofrecer Acreditar,
/// Rechazar ni la lista de acreditados sin una decisión expresa del propietario.
/// </summary>
public class ChequesRecibidosInformativosUiContractTests
{
    private static string Leer(params string[] partes)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(partes).ToArray()));
    }

    [Theory]
    [InlineData("Views", "Caja", "Cheques_tw.cshtml")]
    [InlineData("Views", "Caja", "_ConciliacionMovimientosTab.cshtml")]
    [InlineData("Views", "Caja", "Cerrar_tw.cshtml")]
    [InlineData("Views", "Caja", "Index_tw.cshtml")]
    [InlineData("Views", "Venta", "_ChequeCard.cshtml")]
    public void LasVistasDeCheques_NoOfrecenAcreditarNiRechazar(params string[] ruta)
    {
        var html = Leer(ruta);

        Assert.DoesNotContain("AcreditarCheque", html);
        Assert.DoesNotContain("RechazarCheque", html);
        Assert.DoesNotContain("ChequesAcreditados", html);
        Assert.DoesNotContain("_ChequeAcciones", html);
        Assert.DoesNotContain("Cheques pendientes", html);
    }

    [Fact]
    public void LosPartialsYVistasRetiradasNoExisten()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz != null && !File.Exists(Path.Combine(raiz.FullName, "TheBuryProyect.csproj")))
            raiz = raiz.Parent;
        Assert.NotNull(raiz);
        Assert.False(File.Exists(Path.Combine(raiz!.FullName, "Views", "Caja", "_ChequeAcciones.cshtml")));
        Assert.False(File.Exists(Path.Combine(raiz.FullName, "Views", "Caja", "ChequesAcreditados_tw.cshtml")));
    }

    [Fact]
    public void LaTarjetaDeLaVenta_ExplicaQueElChequeEsUnPagoPendienteInformativo()
    {
        var card = Leer("Views", "Venta", "_ChequeCard.cshtml");

        Assert.Contains("Pago pendiente", card);
        Assert.Contains("no registra su depósito, acreditación ni rechazo", card);
    }

    [Fact]
    public void LaConciliacion_InformaLosChequesRecibidosAparteDelCobrado()
    {
        Assert.Contains("data-cc-cheques-recibidos", Leer("Views", "Caja", "_ConciliacionCards.cshtml"));
        Assert.Contains("data-venta-cheque-recibido", Leer("Views", "Caja", "_ConciliacionVentasTab.cshtml"));
        Assert.Contains("data-medio-cheque-recibido", Leer("Views", "Caja", "_ConciliacionResumenTab.cshtml"));
    }
}
