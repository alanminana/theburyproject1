namespace TheBuryProject.Tests.Unit;

/// <summary>
/// CLIENTE-WIZARD-ML1: contrato de UI del wizard real de alta en el drawer
/// (Views/Cliente/_ClienteFormPartial.cshtml + _ClienteFormCampos.cshtml +
/// wwwroot/js/cliente-modal.js). No renderiza Razor: como el resto de los
/// "UiContractTests" de este proyecto, verifica el contrato sobre el código
/// fuente real, con foco en dos cosas que no se pueden cubrir solo con QA
/// manual — el scoping estricto al drawer Create (no debe filtrarse a
/// drawer Edit, Create_tw ni Edit_tw) y que la navegación exista.
/// </summary>
public class ClienteWizardUiContractTests
{
    [Fact]
    public void ClienteFormPartial_ModoWizardSeDerivaDeIsEdit_NuncaSeActivaEnEdicion()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "_ClienteFormPartial.cshtml"));

        // isWizard nace de !isEdit: en edición (Model.Id > 0) siempre da false,
        // así que data-cliente-wizard="create" nunca se renderiza para editar.
        Assert.Contains("var isWizard = !isEdit;", view);
        Assert.Contains("data-cliente-wizard=\"@(isWizard ? \"create\" : null)\"", view);
    }

    [Fact]
    public void CreateTwYEditTw_NuncaFijanClienteWizardMode()
    {
        var createTw = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "Create_tw.cshtml"));
        var editTw = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "Edit_tw.cshtml"));

        // _ClienteFormCampos.cshtml (compartido) solo activa el modo wizard si
        // ViewData["ClienteWizardMode"] == "create". Las páginas full-page no
        // deben fijar esa clave nunca, así que el wizard no puede filtrarse ahí.
        Assert.DoesNotContain("ClienteWizardMode", createTw);
        Assert.DoesNotContain("ClienteWizardMode", editTw);
    }

    [Fact]
    public void ClienteFormPartial_FooterContieneAnteriorSiguienteYCrearSoloComoAccionesWizard()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "_ClienteFormPartial.cshtml"));

        Assert.Contains("id=\"cliente-wizard-prev\"", view);
        Assert.Contains("id=\"cliente-wizard-next\"", view);
        Assert.Contains("id=\"cliente-wizard-submit\"", view);
        Assert.Contains("id=\"cliente-wizard-cancel\"", view);
    }

    [Fact]
    public void ClienteFormPartial_CrearClienteNoEsLaAccionPrimariaDelPasoInicial()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "_ClienteFormPartial.cshtml"));

        // Estado inicial servido por el servidor: Anterior y Crear cliente
        // arrancan ocultos (clase "hidden"), Siguiente es la única acción
        // primaria visible en el paso 1. cliente-modal.js los muestra/oculta
        // según el paso, pero el primer paint (antes de que corra JS) ya debe
        // ser correcto.
        Assert.Contains("id=\"cliente-wizard-prev\" class=\"btn btn-ghost hidden\"", view);
        Assert.Contains("id=\"cliente-wizard-submit\" class=\"btn btn-primary hidden\"", view);
        Assert.DoesNotContain("id=\"cliente-wizard-next\" class=\"btn btn-primary hidden\"", view);
    }

    [Fact]
    public void ClienteFormCampos_TieneEstadosDePasoYSemanticaDeWizardScopeadaAIsWizard()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "_ClienteFormCampos.cshtml"));

        Assert.Contains("var isWizard = ViewData[\"ClienteWizardMode\"] as string == \"create\";", view);
        Assert.Contains("wizard-steps", view);
        Assert.Contains("data-step-state", view);
        Assert.Contains("aria-current", view);
        Assert.Contains("data-step-icon", view);
        Assert.Contains("data-step-status-text", view);

        // El role="tablist"/"tab" original de la edición se preserva intacto
        // (se omite solo cuando isWizard, nunca se reemplaza a secas).
        Assert.Contains("role=\"@(isWizard ? null : \"tablist\")\"", view);
        Assert.Contains("role=\"@(isWizard ? null : \"tab\")\"", view);
    }

    [Fact]
    public void ClienteModalJs_ExponeMaquinaDeEstadosDeWizardScopeadaAlAtributo()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cliente-modal.js"));

        Assert.Contains("data-cliente-wizard", script);
        Assert.Contains("function initWizard(", script);
        Assert.Contains("function goTo(", script);
        Assert.Contains("wizardValidateAll", script);

        // La cantidad de pasos se deriva del DOM real (steps.length), nunca de
        // un literal fijo — así el wizard no queda inconsistente cuando un
        // lote futuro oculte un paso (p. ej. Referencias).
        Assert.Contains("steps.length", script);
        Assert.DoesNotContain("i < 6", script);
        Assert.DoesNotContain("=== 5", script);
        Assert.DoesNotContain("== 5", script);
    }

    [Fact]
    public void ClienteModalJs_ValidaContraElModeloReal_NoDuplicaReglasCustomEnJs()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cliente-modal.js"));

        // Los campos obligatorios por paso se descubren desde los atributos
        // data-val-required que ya emite el tag helper asp-for a partir de
        // [Required] en ClienteViewModel — no hay una lista de nombres de
        // campo hardcodeada en JS que pueda desincronizarse del modelo.
        Assert.Contains("data-val-required", script);

        // Los validadores custom sin adaptador cliente (SoloLetras,
        // TelefonoArgentino, DocumentoArgentino, CodigoPostalArgentino) deben
        // seguir siendo autoridad exclusiva del servidor: no se reimplementan
        // acá.
        Assert.DoesNotContain("SoloLetras", script);
        Assert.DoesNotContain("TelefonoArgentino", script);
        Assert.DoesNotContain("DocumentoArgentino", script);
        Assert.DoesNotContain("CodigoPostalArgentino", script);
    }

    [Fact]
    public void ClienteViewModel_CamposObligatoriosDePersonalesYContactoSiguenSiendoLosMismos()
    {
        // Ancla el supuesto sobre el que se apoya la validación por paso del
        // wizard (Personales: TipoDocumento/NumeroDocumento/Apellido/Nombre;
        // Contacto: Telefono/Domicilio; el resto sin obligatorios): si alguien
        // cambia estos [Required] sin querer, este test avisa antes que un
        // usuario final.
        var viewModel = File.ReadAllText(Path.Combine(FindRepoRoot(), "ViewModels", "ClienteViewModel.cs"));

        Assert.Contains("[Required(ErrorMessage = \"El tipo de documento es requerido\")]", viewModel);
        Assert.Contains("[Required(ErrorMessage = \"El número de documento es requerido\")]", viewModel);
        Assert.Contains("[Required(ErrorMessage = \"El apellido es requerido\")]", viewModel);
        Assert.Contains("[Required(ErrorMessage = \"El nombre es requerido\")]", viewModel);
        Assert.Contains("[Required(ErrorMessage = \"El teléfono es requerido\")]", viewModel);
        Assert.Contains("[Required(ErrorMessage = \"El domicilio es requerido\")]", viewModel);
    }

    [Fact]
    public void ClienteFormCampos_ReferenciasQuedaFueraDeLosPasosNavegablesDelWizard()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "_ClienteFormCampos.cshtml"));

        // Referencias se saca del flujo navegable con el atributo hidden nativo
        // (no se borra la sección ni sus campos: solo deja de listarse como
        // paso). Ningún otro botón de paso lleva este atributo.
        Assert.Contains("data-cliente-tab=\"t-refs\" hidden=\"@isWizard\"", view);
        Assert.DoesNotContain("data-cliente-tab=\"t-personal\" hidden", view);
        Assert.DoesNotContain("data-cliente-tab=\"t-conyuge\" hidden", view);
        Assert.DoesNotContain("data-cliente-tab=\"t-contacto\" hidden", view);
        Assert.DoesNotContain("data-cliente-tab=\"t-laboral\" hidden", view);
        Assert.DoesNotContain("data-cliente-tab=\"t-credito\" hidden", view);

        // La sección y sus campos siguen intactos en el DOM (no se borran).
        Assert.Contains("id=\"t-refs\"", view);
        Assert.Contains("SIN CAMPOS EN EL MODELO ACTUAL", view);

        // "Paso X de N" para el primer paint sale de una única lista nombrada
        // (5 pasos: Referencias afuera), no de un literal repetido.
        Assert.Contains("var wizardStepIds = new[] { \"t-personal\", \"t-conyuge\", \"t-contacto\", \"t-laboral\", \"t-credito\" };", view);
        Assert.Contains("data-wizard-progress", view);
    }

    [Fact]
    public void ClienteFormCampos_ConyugeCondicionalUsaLosValoresRealesDeEstadosCiviles()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "_ClienteFormCampos.cshtml"));
        var dropdowns = File.ReadAllText(Path.Combine(FindRepoRoot(), "Helpers", "DropdownConstants.cs"));

        // El conjunto "sin cónyuge" del wizard debe ser un subconjunto literal
        // de los valores reales de DropdownConstants.EstadosCiviles, no un
        // enum ni una regla de negocio nueva.
        Assert.Contains("\"Soltero/a\", \"Casado/a\", \"Divorciado/a\", \"Viudo/a\", \"Unión de hecho\"", dropdowns);
        Assert.Contains("new HashSet<string> { \"Soltero/a\", \"Divorciado/a\", \"Viudo/a\" }", view);

        // Casado/a y Unión de hecho nunca deben quedar en el set "sin cónyuge".
        Assert.DoesNotContain("\"Casado/a\", \"Divorciado/a\"", view);
        Assert.DoesNotContain("\"Unión de hecho\", \"Divorciado/a\"", view);
    }

    [Fact]
    public void ClienteModalJs_ConyugeSeSalteaSinBorrarDatosYSinNuevaValidacionDeBackend()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cliente-modal.js"));

        Assert.Contains("function isConyugeApplicable(", script);
        Assert.Contains("function stepAfter(", script);
        Assert.Contains("['Soltero/a', 'Divorciado/a', 'Viudo/a']", script);

        // El índice de Cónyuge se busca en el DOM real (data-cliente-tab), no
        // se asume una posición fija — así no se rompe si el orden cambia.
        Assert.Contains("step.button.getAttribute('data-cliente-tab') === 't-conyuge'", script);

        // Cambiar Estado civil solo re-renderiza estados de paso: no limpia
        // ningún campo de Cónyuge ni agrega un fetch/validación de servidor.
        Assert.Contains("civilSelect.addEventListener('change'", script);
        Assert.DoesNotContain(".value = ''", script);

        // La cantidad de pasos sigue derivándose del DOM (Referencias afuera
        // baja steps.length a 5 solo), nunca de un literal nuevo.
        Assert.DoesNotContain("i < 5", script);
        Assert.DoesNotContain("=== 5", script);
        Assert.DoesNotContain("== 5", script);
    }

    [Fact]
    public void ClienteFormPartial_ResumenLateralDinamicoSoloExisteEnModoWizard()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "_ClienteFormPartial.cshtml"));

        // El bloque de Contacto del resumen (Lote 2, punto 3) se renderiza
        // únicamente dentro de "@if (isWizard)" y arranca oculto con el
        // atributo nativo hidden (nunca borra los kv-row de Estado
        // inicial/Aptitud/Límite que ya existían).
        Assert.Contains("if (isWizard)", view);
        Assert.Contains("id=\"pv-contact\" hidden", view);
        Assert.Contains("id=\"pv-contact-phone\" hidden", view);
        Assert.Contains("id=\"pv-contact-email\" hidden", view);
        Assert.Contains("Estado inicial", view);
        Assert.Contains("Sin evaluar", view);
        Assert.Contains("Por puntaje", view);

        // No se agrega score/riesgo/límite numérico ficticio al alta: los
        // kv-row de aptitud calculada (Score numérico) siguen existiendo
        // solo en la rama de edición.
        Assert.Contains("<div class=\"kv-row\"><dt>Score</dt><dd class=\"num\">@Model.PuntajeRiesgo</dd></div>", view);
    }

    [Fact]
    public void ClienteModalJs_ResumenDinamicoEsUnaFuncionCentralScopeadaAlWizard()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cliente-modal.js"));

        // Una única función central actualiza el resumen (no hay lógica
        // duplicada por campo); vive dentro de initWizard, así que solo
        // corre en modo wizard.
        Assert.Contains("function updateClientSummary(", script);
        Assert.Contains("function fieldValue(", script);
        Assert.Contains("function formatDocumentoNumero(", script);

        // Se conecta a los campos reales del modelo, con el evento correcto
        // según el tipo de control (input para texto, change para el select
        // de tipo de documento).
        Assert.Contains("['Nombre', 'Apellido', 'NumeroDocumento', 'Telefono', 'Email']", script);
        Assert.Contains("tipoDocumentoSelect.addEventListener('change', updateClientSummary)", script);

        // updatePreview() (la vista previa de edición) se preserva intacta y
        // no se registra en modo wizard, para no duplicar lógica sobre los
        // mismos nodos del resumen.
        Assert.Contains("function updatePreview()", script);
        Assert.Contains("if (!wizardReady) {", script);
    }

    [Fact]
    public void ClienteModalJs_ResumenDinamicoNuncaInventaAptitudNiBorraDatos()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cliente-modal.js"));

        // El resumen dinámico no calcula ni muestra score/riesgo/límite: esa
        // información sigue siendo estática ("Sin evaluar" / "Por puntaje"),
        // servida por el HTML y jamás tocada desde JS.
        Assert.DoesNotContain("PuntajeRiesgo", script);
        Assert.DoesNotContain("NivelRiesgo", script);
        Assert.DoesNotContain("pv-contact-phone').value = ''", script);
        Assert.DoesNotContain("pv-contact-email').value = ''", script);
    }

    [Fact]
    public void ClienteFormCampos_StepperCompactoDeMobileExisteSoloEnWizardYNoDuplicaElDato()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "_ClienteFormCampos.cshtml"));

        // Lote 3: el stepper compacto de mobile se suma ARRIBA de
        // .wizard-steps <=640px (ver cliente-module.css) — no la reemplaza,
        // se renderiza dentro del mismo "@if (isWizard)" que ya protege el
        // resto del wizard.
        Assert.Contains("data-wizard-compact", view);
        Assert.Contains("data-wizard-compact-icon", view);
        Assert.Contains("data-wizard-compact-label", view);
        Assert.Contains("data-wizard-compact-fill", view);

        // El texto de cada tab vive en su propio span ("tab-label"): en
        // mobile pasa a sr-only (icon-only, angosto) sin dejar de ser
        // clickeable — "abrir manualmente un paso permitido" no puede
        // depender de que la barra completa esté visible.
        Assert.Contains("class=\"tab-label\"", view);

        // Cada paso navegable expone su nombre corto vía data-step-label,
        // gateado a isWizard igual que el resto de los atributos propios del
        // wizard — la edición nunca recibe este atributo.
        Assert.Contains("data-step-label=\"@(isWizard ? \"Personales\" : null)\"", view);
        Assert.Contains("data-step-label=\"@(isWizard ? \"Cónyuge\" : null)\"", view);
        Assert.Contains("data-step-label=\"@(isWizard ? \"Contacto\" : null)\"", view);
        Assert.Contains("data-step-label=\"@(isWizard ? \"Laboral\" : null)\"", view);
        Assert.Contains("data-step-label=\"@(isWizard ? \"Crédito\" : null)\"", view);
    }

    [Fact]
    public void ClienteModalJs_PasoActivoSeMantieneVisibleEnLaBarraYAlimentaElStepperCompacto()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cliente-modal.js"));

        // Lote 3: en anchos donde la barra de 5 tabs no entra completa
        // (confirmado en vivo a 1024px), el tab del paso actual se mantiene
        // visible en cada render — sin esto "Crédito" podía quedar
        // totalmente fuera de vista al llegar al último paso con Siguiente.
        Assert.Contains("scrollIntoView({ block: 'nearest', inline: 'nearest' })", script);

        // El stepper compacto de mobile lee el ícono y el nombre del propio
        // tab activo (una sola fuente de verdad), nunca una copia aparte.
        Assert.Contains("function plainIconOf(", script);
        Assert.Contains("compactIconEl.textContent = plainIconOf(steps[current])", script);
        Assert.Contains("compactLabelEl.textContent = steps[current].button.getAttribute('data-step-label')", script);
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
