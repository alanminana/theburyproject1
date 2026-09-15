namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Contrato de UI del rediseño "ficha ejecutiva" de Cliente/Details (2026-09-14): ancho
/// fluido consistente con Cliente/Index, tope de créditos recientes mostrados, y
/// humanización de presentación de textos que antes filtraban el nombre técnico del enum.
/// </summary>
public class ClienteDetailsResumenEjecutivoUiContractTests
{
    [Fact]
    public void DetailsView_UsaAnchoFluidoComoIndex_NoElShellCapadoDeFormularios()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "Details_tw.cshtml"));

        Assert.Contains("class=\"shell cliente-details\"", view);

        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "css", "cliente-module.css"));
        Assert.Contains(".shell.cliente-details", css);
    }

    [Fact]
    public void DetailsView_LimitaCreditosRecientesYOfreceVerTodosConTotal()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "Details_tw.cshtml"));

        // La ficha no debe iterar la lista completa (puede tener decenas de créditos):
        // solo un resumen reciente, con el total real en el link "Ver todos".
        Assert.Contains("Model.CreditosActivos?.Take(5)", view);
        Assert.Contains("foreach (var credito in creditosRecientes)", view);
        Assert.Contains("Ver todos (@totalCreditos)", view);
    }

    [Fact]
    public void DetailsView_HumanizaEstadoCreditoPendienteConfiguracion()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "Details_tw.cshtml"));

        // EstadoCredito.PendienteConfiguracion se mostraba tal cual ("pegado"); la tabla
        // de créditos debe pasar por el helper de humanización, no interpolar el enum crudo.
        Assert.Contains("EstadoCredito.PendienteConfiguracion => \"Pendiente de configuracion\"", view);
        Assert.Contains("EstadoCreditoLabel(credito.Estado)", view);
        Assert.DoesNotContain(">@credito.Estado<", view);
    }

    [Fact]
    public void DetailsView_DocumentoFaltanteUsaEtiquetaFaltanteYNombreHumanizado()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "Details_tw.cshtml"));

        // Un documento nunca subido no es lo mismo que uno subido y pendiente de
        // verificación: antes ambos mostraban el mismo chip "Pendiente".
        Assert.Contains("<span class=\"chip chip-warn\">Faltante</span>", view);
        Assert.Contains("HumanizarTipoDocumento(faltante)", view);

        // Regresión: la comparación vieja (nombre humanizado contra nombre técnico crudo)
        // nunca coincidía para tipos de más de una palabra y duplicaba el documento.
        Assert.DoesNotContain("d.TipoDocumentoNombre == faltante", view);
        Assert.Contains("d.TipoDocumento.ToString() == faltante", view);
    }

    [Fact]
    public void DetailsView_BadgeDocumentacionCombinaPendientesYFaltantesConsistente()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "Details_tw.cshtml"));

        // El chip de cabecera del panel "Documentación" antes mostraba solo pendientes,
        // pudiendo leerse "0 pendientes" mientras documentos figuraban "Faltante" abajo.
        Assert.Contains("documentosAsuntoLabel", view);
        Assert.Contains("class=\"chip @documentosAsuntoChip\">@documentosAsuntoLabel</span>", view);
        Assert.DoesNotContain("chip @(documentosPendientes > 0 || documentosFaltantes > 0 ? \"chip-warn\" : \"chip-ok\")\">@documentosPendientes pendientes</span>", view);
    }

    [Fact]
    public void DetailsView_ZonaSensibleNoDiceEliminarCuandoEsBajaLogica()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "Details_tw.cshtml"));

        // ClienteService.DeleteAsync es soft-delete (IsDeleted = true); el propio texto de
        // ayuda ya lo aclara ("Dar de baja... Se conserva el historial") — el botón no debe
        // decir "Eliminar", que induce a pensar en un borrado físico.
        Assert.Contains("Dar de baja al cliente lo oculta del listado operativo. Se conserva el historial.", view);
        Assert.Contains(">Dar de baja cliente</a>", view);
        Assert.DoesNotContain(">Eliminar cliente</a>", view);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}
