using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public sealed class VentaDetailsUiContractTests
{
    [Fact]
    public void DetailsView_MantieneAccionPrepararVentaParaCreditoPersonal()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("Model.PuedePrepararVenta", view);
        Assert.Contains("asp-action=\"PrepararVenta\"", view);
        Assert.Contains("Preparar venta", view);
    }

    [Fact]
    public void DetailsView_OfreceConfirmarYFacturarEnUnPaso()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("Model.PuedeConfirmarYFacturar", view);
        Assert.Contains("asp-action=\"ConfirmarYFacturar\"", view);
    }

    [Fact]
    public void VentaViewModel_CotizacionSinCreditoConfirmaDirectoSinPreparar()
    {
        var venta = new VentaViewModel { Estado = EstadoVenta.Cotizacion, TipoPago = TipoPago.Efectivo };

        Assert.False(venta.PuedePrepararVenta);
        Assert.True(venta.PuedeConfirmar);
        Assert.True(venta.PuedeConfirmarYFacturar);
        Assert.False(venta.PuedeFacturar);
    }

    [Fact]
    public void VentaViewModel_CotizacionCreditoPersonalConservaPrepararVenta()
    {
        var venta = new VentaViewModel { Estado = EstadoVenta.Cotizacion, TipoPago = TipoPago.CreditoPersonal };

        Assert.True(venta.PuedePrepararVenta);
        Assert.False(venta.PuedeConfirmar);
        Assert.False(venta.PuedeConfirmarYFacturar);
    }

    [Fact]
    public void VentaViewModel_PresupuestoPuedeConfirmarPeroNoPreparar()
    {
        var venta = new VentaViewModel { Estado = EstadoVenta.Presupuesto, TipoPago = TipoPago.Efectivo };

        Assert.False(venta.PuedePrepararVenta);
        Assert.True(venta.PuedeConfirmar);
        Assert.True(venta.PuedeConfirmarYFacturar);
        Assert.False(venta.PuedeFacturar);
    }

    [Fact]
    public void VentaViewModel_CreditoPersonalConCreditoConfiguradoOfreceConfirmarYFacturar()
    {
        // La acción combinada ya no excluye crédito personal: PuedeConfirmar exige
        // crédito configurado (y VentaController.Confirmar exige contrato generado antes
        // de llegar a REGLA 4), así que llegado este punto no queda ningún requisito
        // pendiente que impida facturar en el mismo paso que mostrador.
        var venta = new VentaViewModel
        {
            Estado = EstadoVenta.PendienteRequisitos,
            TipoPago = TipoPago.CreditoPersonal,
            CreditoId = 55,
            FechaConfiguracionCredito = DateTime.Today
        };

        Assert.True(venta.PuedeConfirmar);
        Assert.True(venta.PuedeConfirmarYFacturar);
    }

    [Fact]
    public void DetailsView_ModalAnularFactura_MarcaElTextareaMotivoComoFocoInicial()
    {
        // VENTA-DETAILS-01B (H3): bindModal resuelve el foco inicial vía
        // [data-modal-initial-focus] — el textarea Motivo debe llevarlo para que el
        // usuario aterrice ahí al abrir el modal, en vez del botón cerrar.
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("id=\"anular-factura-motivo\"", view);
        Assert.Contains("data-modal-initial-focus", view);
    }

    [Fact]
    public void DetailsView_ModalAnularFactura_NoDuplicaElFocoConUnAfterOpenLocal()
    {
        // Un afterOpen local que también hace foco (vía rAF) compite con el rAF
        // genérico de bindModal y pierde la carrera — ese era el bug original de H3.
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "details-venta.js"));

        Assert.DoesNotContain("inputMotivoAnulacion?.focus()", script);
    }

    [Fact]
    public void DetailsView_AccionesMuestraEmptyStateCuandoNoHayAccionesDisponibles()
    {
        // VENTA-DETAILS-01C (H6): la card "Acciones" no debe quedar vacía (solo
        // heading + divisor) cuando ninguna acción está disponible para el estado
        // actual — debe mostrar un mensaje explícito.
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("hayAccionesDisponibles", view);
        Assert.Contains("Sin acciones disponibles para esta venta", view);
    }

    [Fact]
    public void DetailsView_AccionesDestructivas_DividerSoloApareceSiHayAlgunaAccionDestructiva()
    {
        // El grupo de Cancelar/Anular Factura no debe dejar un <div> con borde
        // divisor vacío cuando ninguna de las dos acciones está disponible.
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("hayAccionesDestructivas", view);
        Assert.Contains("if (hayAccionesDestructivas)", view);
    }

    [Fact]
    public void DetailsView_AccionesDisponibles_ReutilizaLosMismosPredicadosQueLosBotones()
    {
        // La autoridad de "hay acciones" no debe inventar lógica de negocio nueva:
        // debe seguir leyendo los mismos Puede*/permisos que ya controlan cada botón.
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("Model.PuedeConfigurarCredito", view);
        Assert.Contains("Model.PuedeCancelar", view);
        Assert.Contains("Model.PuedeAutorizar", view);
    }

    [Fact]
    public void DetailsView_BotonContrato_UsaCopyCortoSinBarraDeImprimir()
    {
        // VENTA-DETAILS-01C (H4): "Ver / imprimir contrato" se partía en 3 líneas
        // en el sidebar de Acciones a ~1024px. El botón solo navega a la vista del
        // contrato (ContratoVentaCredito/Ver sirve el PDF inline); la impresión la
        // hace el usuario desde ahí, no el botón — el copy corto es más preciso y
        // elimina el wrap de raíz sin tocar el sistema de botones.
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("Ver contrato", view);
        Assert.DoesNotContain("Ver / imprimir contrato", view);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "TheBuryProyect.slnx")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir ?? throw new InvalidOperationException("No se encontro raiz del repo.");
    }
}
