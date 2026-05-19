namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Contratos HTML/JS del Layout global y sus parciales críticos.
/// Objetivo: detectar regresiones al modificar _Layout.cshtml, layout.js o shared-ui.js.
/// Creado en UI-4A — auditoría previa al rework visual del Layout (UI-4B/UI-4C).
/// </summary>
public class LayoutUiContractTests
{
    // ── _Layout.cshtml: estructura principal ──────────────────────────────

    [Fact]
    public void Layout_TieneContenedorPrincipalFlex()
    {
        var layout = ReadLayout();
        Assert.Contains("class=\"flex h-screen overflow-hidden\"", layout);
    }

    [Fact]
    public void Layout_TieneSidebarConId()
    {
        var layout = ReadLayout();
        Assert.Contains("id=\"sidebar\"", layout);
        Assert.Contains("<aside id=\"sidebar\"", layout);
    }

    [Fact]
    public void Layout_TieneSidebarOverlayConId()
    {
        var layout = ReadLayout();
        Assert.Contains("id=\"sidebarOverlay\"", layout);
    }

    [Fact]
    public void Layout_TieneToggleSidebarParaMobile()
    {
        var layout = ReadLayout();
        Assert.Contains("id=\"toggleSidebar\"", layout);
        Assert.Contains("aria-label=\"Abrir/cerrar menú\"", layout);
    }

    [Fact]
    public void Layout_TieneCollapseSidebarParaDesktop()
    {
        var layout = ReadLayout();
        Assert.Contains("id=\"collapseSidebar\"", layout);
        Assert.Contains("id=\"collapseIcon\"", layout);
        Assert.Contains("aria-label=\"Colapsar/expandir sidebar\"", layout);
    }

    [Fact]
    public void Layout_TieneHeader()
    {
        var layout = ReadLayout();
        Assert.Contains("<header ", layout);
        Assert.Contains("</header>", layout);
    }

    [Fact]
    public void Layout_TieneMainContenido()
    {
        var layout = ReadLayout();
        Assert.Contains("<main ", layout);
        Assert.Contains("</main>", layout);
    }

    [Fact]
    public void Layout_TieneRenderBody()
    {
        var layout = ReadLayout();
        Assert.Contains("@RenderBody()", layout);
    }

    [Fact]
    public void Layout_TieneRenderSectionScripts()
    {
        var layout = ReadLayout();
        Assert.Contains("RenderSectionAsync(\"Scripts\"", layout);
    }

    [Fact]
    public void Layout_TieneRenderSectionModals()
    {
        var layout = ReadLayout();
        Assert.Contains("RenderSectionAsync(\"Modals\"", layout);
    }

    [Fact]
    public void Layout_TieneRenderSectionStyles()
    {
        var layout = ReadLayout();
        Assert.Contains("RenderSection(\"Styles\"", layout);
    }

    [Fact]
    public void Layout_TieneAntiforgeryEnLogoutForm()
    {
        // El form de logout usa POST identity — el antiforgery lo provee ASP.NET Identity.
        // Si se elimina el form, scripts de logout dejarán de funcionar.
        var layout = ReadLayout();
        Assert.Contains("asp-area=\"Identity\"", layout);
        Assert.Contains("asp-page=\"/Account/Logout\"", layout);
        Assert.Contains("method=\"post\"", layout);
    }

    [Fact]
    public void Layout_TieneBtnOpenTicketPanel()
    {
        // ticket-panel.js escucha este ID para abrir el panel lateral.
        // Romper este ID desconecta el botón del header del panel.
        var layout = ReadLayout();
        Assert.Contains("id=\"btn-open-ticket-panel\"", layout);
    }

    [Fact]
    public void Layout_TieneDataOpenTicketModal()
    {
        // El botón flotante de reporte y _TicketPanel usan [data-open-ticket-modal].
        var layout = ReadLayout();
        Assert.Contains("data-open-ticket-modal", layout);
    }

    // ── _Layout.cshtml: scripts globales ─────────────────────────────────

    [Fact]
    public void Layout_CargaJquery()
    {
        var layout = ReadLayout();
        Assert.Contains("jquery.min.js", layout);
    }

    [Fact]
    public void Layout_CargaSharedUiJs()
    {
        var layout = ReadLayout();
        Assert.Contains("shared-ui.js", layout);
    }

    [Fact]
    public void Layout_CargaLayoutJs()
    {
        var layout = ReadLayout();
        Assert.Contains("layout.js", layout);
    }

    [Fact]
    public void Layout_CargaLayoutJsDespuesDeSharedUiJs()
    {
        // El orden importa: layout.js depende de que jQuery y shared-ui.js ya estén cargados.
        var layout = ReadLayout();
        var posSharedUi = layout.IndexOf("shared-ui.js", StringComparison.Ordinal);
        var posLayoutJs = layout.IndexOf("layout.js", StringComparison.Ordinal);
        Assert.True(posSharedUi < posLayoutJs, "shared-ui.js debe cargarse antes que layout.js");
    }

    // ── _Layout.cshtml: CSS global ────────────────────────────────────────

    [Fact]
    public void Layout_CargaTailwindCss()
    {
        var layout = ReadLayout();
        Assert.Contains("tailwind.css", layout);
    }

    [Fact]
    public void Layout_CargaLayoutCss()
    {
        var layout = ReadLayout();
        Assert.Contains("layout.css", layout);
    }

    [Fact]
    public void Layout_CargaSharedComponentsCss()
    {
        var layout = ReadLayout();
        Assert.Contains("shared-components.css", layout);
    }

    // ── _Layout.cshtml: permisos/roles ────────────────────────────────────

    [Fact]
    public void Layout_UsaHelperTienePermiso()
    {
        var layout = ReadLayout();
        Assert.Contains("User.TienePermiso(", layout);
    }

    [Fact]
    public void Layout_UsaHelperTieneCualquierPermiso()
    {
        var layout = ReadLayout();
        Assert.Contains("User.TieneCualquierPermiso(", layout);
    }

    // ── _ConfirmModal.cshtml: contratos críticos ──────────────────────────

    [Fact]
    public void ConfirmModal_TieneIdConfirmModal()
    {
        var modal = ReadShared("_ConfirmModal.cshtml");
        Assert.Contains("id=\"confirmModal\"", modal);
    }

    [Fact]
    public void ConfirmModal_TieneIdConfirmModalBody()
    {
        var modal = ReadShared("_ConfirmModal.cshtml");
        Assert.Contains("id=\"confirmModalBody\"", modal);
    }

    [Fact]
    public void ConfirmModal_TieneIdConfirmModalAction()
    {
        // shared-ui.js clona este botón para reemplazar listeners anteriores.
        var modal = ReadShared("_ConfirmModal.cshtml");
        Assert.Contains("id=\"confirmModalAction\"", modal);
    }

    [Fact]
    public void ConfirmModal_TieneDataConfirmModalClose()
    {
        // shared-ui.js delega cierres a [data-confirm-modal-close].
        var modal = ReadShared("_ConfirmModal.cshtml");
        Assert.Contains("data-confirm-modal-close", modal);
    }

    // ── shared-ui.js: contratos de API global ─────────────────────────────

    [Fact]
    public void SharedUiJs_ExponeWindowTheBury()
    {
        var js = ReadJs("shared-ui.js");
        Assert.Contains("window.TheBury", js);
    }

    [Fact]
    public void SharedUiJs_ExponeOpenConfirmModal()
    {
        var js = ReadJs("shared-ui.js");
        Assert.Contains("window.openConfirmModal", js);
    }

    [Fact]
    public void SharedUiJs_ExponeCloseConfirmModal()
    {
        var js = ReadJs("shared-ui.js");
        Assert.Contains("window.closeConfirmModal", js);
    }

    [Fact]
    public void SharedUiJs_TieneFormatCurrency()
    {
        var js = ReadJs("shared-ui.js");
        Assert.Contains("TheBury.formatCurrency", js);
        Assert.Contains("es-AR", js);
        Assert.Contains("ARS", js);
    }

    [Fact]
    public void SharedUiJs_TieneAutoDismissToasts()
    {
        var js = ReadJs("shared-ui.js");
        Assert.Contains("TheBury.autoDismissToasts", js);
        Assert.Contains(".toast-msg", js);
        Assert.Contains("[id^=\"toast-\"]", js);
    }

    [Fact]
    public void SharedUiJs_TieneConfirmAction()
    {
        var js = ReadJs("shared-ui.js");
        Assert.Contains("TheBury.confirmAction", js);
        Assert.Contains("openConfirmModal", js);
    }

    [Fact]
    public void SharedUiJs_TieneNormalizeText()
    {
        var js = ReadJs("shared-ui.js");
        Assert.Contains("TheBury.normalizeText", js);
        Assert.Contains("normalize('NFD')", js);
    }

    // ── layout.js: contratos de IDs críticos ─────────────────────────────

    [Fact]
    public void LayoutJs_ReferenciaSidebarId()
    {
        var js = ReadJs("layout.js");
        Assert.Contains("getElementById('sidebar')", js);
    }

    [Fact]
    public void LayoutJs_ReferenciaSidebarOverlayId()
    {
        var js = ReadJs("layout.js");
        Assert.Contains("getElementById('sidebarOverlay')", js);
    }

    [Fact]
    public void LayoutJs_ReferenciaToggleSidebarId()
    {
        var js = ReadJs("layout.js");
        Assert.Contains("getElementById('toggleSidebar')", js);
    }

    [Fact]
    public void LayoutJs_ReferenciaCollapseSidebarId()
    {
        var js = ReadJs("layout.js");
        Assert.Contains("getElementById('collapseSidebar')", js);
    }

    [Fact]
    public void LayoutJs_UsaLocalStorageKeySidebarCollapsed()
    {
        var js = ReadJs("layout.js");
        Assert.Contains("sidebar-collapsed", js);
        Assert.Contains("localStorage", js);
    }

    [Fact]
    public void LayoutJs_AplicaClaseCollapsedAlSidebar()
    {
        var js = ReadJs("layout.js");
        Assert.Contains("classList.add('collapsed')", js);
        Assert.Contains("classList.remove('collapsed')", js);
    }

    [Fact]
    public void LayoutJs_AplicaClaseOpenAlSidebar()
    {
        var js = ReadJs("layout.js");
        Assert.Contains("classList.add('open')", js);
        Assert.Contains("classList.remove('open')", js);
    }

    [Fact]
    public void LayoutJs_AplicaClaseActiveAlOverlay()
    {
        var js = ReadJs("layout.js");
        Assert.Contains("classList.add('active')", js);
        Assert.Contains("classList.remove('active')", js);
    }

    // ── layout.css: clases críticas usadas por layout.js ─────────────────

    [Fact]
    public void LayoutCss_DefineSidebarId()
    {
        var css = ReadCss("layout.css");
        Assert.Contains("#sidebar", css);
    }

    [Fact]
    public void LayoutCss_DefineSidebarCollapsedClass()
    {
        var css = ReadCss("layout.css");
        Assert.Contains("#sidebar.collapsed", css);
    }

    [Fact]
    public void LayoutCss_DefineSidebarOpenClass()
    {
        var css = ReadCss("layout.css");
        Assert.Contains("#sidebar.open", css);
    }

    [Fact]
    public void LayoutCss_DefineSidebarOverlayActive()
    {
        var css = ReadCss("layout.css");
        Assert.Contains("#sidebarOverlay.active", css);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string ReadLayout() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Shared", "_Layout.cshtml"));

    private static string ReadShared(string filename) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Shared", filename));

    private static string ReadJs(string filename) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", filename));

    private static string ReadCss(string filename) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "css", filename));

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
