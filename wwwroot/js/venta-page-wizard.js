(function () {
    'use strict';

    const root = document.getElementById('venta-create-page') || document.getElementById('venta-edit-page');
    if (!root) return;

    const ventaInicialSeed = document.getElementById('venta-inicial-json');
    if (ventaInicialSeed?.value && !window.ventaInicial) {
        try {
            window.ventaInicial = JSON.parse(ventaInicialSeed.value);
        } catch {
            window.ventaInicial = null;
        }
    }

    const tabButtons = Array.from(root.querySelectorAll('[data-step]'));
    const panels = tabButtons
        .map((button) => {
            const step = button.getAttribute('data-step');
            return step ? document.getElementById(`step-panel-${step}`) : null;
        })
        .filter(Boolean);
    const pagoSelect = document.getElementById('select-tipo-pago');
    const ventaForm = document.getElementById('venta-form');
    let creditoValidado = ventaForm?.dataset.creditoConfigurado === 'true';

    // El cotizador embebido (paso Cotizar) trae sus propios nodos [data-side-total]:
    // los maneja cotizacion-simulador.js y el resumen de la venta no debe pisarlos.
    const COTIZADOR_SELECTOR = '[data-cotizacion-simulador]';

    function setText(selector, value) {
        root.querySelectorAll(selector).forEach((node) => {
            if (node.closest(COTIZADOR_SELECTOR)) return;
            if (node.textContent !== value) {
                node.textContent = value;
            }
        });
    }

    function getText(id, fallback) {
        const el = document.getElementById(id);
        const text = el?.textContent?.trim();
        return text || fallback;
    }

    function setActiveStep(step) {
        if (!step) return;

        // Hook de layout: con Cotizar activo el CSS oculta [data-venta-workspace]
        // (grilla de la venta + barra móvil) para que el simulador ocupe el ancho.
        root.dataset.pasoActivo = step;

        tabButtons.forEach((button) => {
            const active = button.getAttribute('data-step') === step;
            button.classList.toggle('vm-step-tab--active', active);
            button.classList.toggle('is-active', active);
            button.setAttribute('aria-selected', active ? 'true' : 'false');
            button.tabIndex = active ? 0 : -1;
        });

        panels.forEach((panel) => {
            const active = panel.id === `step-panel-${step}`;
            panel.classList.toggle('hidden', !active);
            panel.hidden = !active;
        });

        actualizarAccionPrincipal(step);
    }

    function requiereCredito() {
        return pagoSelect?.value === pagoSelect?.dataset.creditoPersonalValue;
    }

    function pasosVisibles() {
        return tabButtons.filter((button) => !button.hidden);
    }

    function actualizarNumeracionPasos() {
        const visibles = pasosVisibles();
        visibles.forEach((button, index) => {
            button.setAttribute('aria-posinset', String(index + 1));
            button.setAttribute('aria-setsize', String(visibles.length));
            const numero = button.querySelector('.vm-step-tab__num');
            if (numero) numero.textContent = String(index + 1);
        });
    }

    function actualizarAccionPrincipal(step) {
        const esRevision = step === 'revision';
        const esPago = step === 'pago';
        // En Cotizar la acción real no es la venta: el simulador tiene su propio
        // endpoint (/api/cotizacion/simular) y su propio botón. Delegamos en él en
        // vez de conectar el paso al submit de #venta-form.
        const esCotizar = step === 'cotizar';
        root.querySelectorAll('[data-wizard-primary]').forEach((button) => {
            button.dataset.wizardAction = esCotizar
                ? 'simular-cotizacion'
                : (esRevision
                    ? 'submit'
                    : (step === 'credito' && !creditoValidado ? 'verify-credit' : 'next'));
            const label = button.querySelector('[data-wizard-primary-label]');
            const texto = esCotizar
                ? 'Simular cotización'
                : (esRevision
                ? 'Confirmar operación'
                : (esPago ? (requiereCredito() ? 'Continuar a crédito' : 'Revisar operación')
                    : (step === 'credito' ? (creditoValidado ? 'Revisar operación' : 'Verificar crédito') : 'Siguiente')));
            if (label) {
                label.textContent = texto;
            } else {
                button.textContent = texto;
            }
        });
    }

    function actualizarPasoCredito() {
        const tab = document.getElementById('step-btn-credito');
        const panel = document.getElementById('step-panel-credito');
        const activo = requiereCredito();
        if (!tab || !panel) return;

        tab.hidden = !activo;
        panel.hidden = !activo;
        tab.classList.toggle('hidden', !activo);
        if (!activo) creditoValidado = false;
        actualizarNumeracionPasos();
    }

    // Pestañas grisadas hasta completar el paso previo: Cliente siempre habilitado,
    // Productos requiere cliente, y el resto (Pago/Crédito/Revisión) requiere
    // además al menos un producto cargado.
    // Nota: se lee del hidden de cliente y de las filas de la tabla de detalle
    // (no de #hero-cliente/#hero-detalles-count, que no existen en el DOM actual).
    function refreshStepGating() {
        const clienteListo = (parseInt(document.getElementById('hdn-cliente-id')?.value, 10) || 0) > 0;
        const productosListo = (document.getElementById('tbody-detalles')?.children.length || 0) > 0;

        actualizarPasoCredito();
        const requisitos = {
            // Cotizar es el punto de entrada de Create: nunca depende de la venta.
            cotizar: () => true,
            cliente: () => true,
            productos: () => clienteListo,
            pago: () => clienteListo && productosListo,
            credito: () => clienteListo && productosListo,
            revision: () => clienteListo && productosListo && (!requiereCredito() || creditoValidado)
        };

        tabButtons.forEach((button) => {
            const step = button.getAttribute('data-step');
            const habilitado = (requisitos[step] || (() => clienteListo && productosListo))();
            const disponible = habilitado && !button.hidden;
            button.disabled = !disponible;
            button.setAttribute('aria-disabled', String(!disponible));
            if (!disponible) button.tabIndex = -1;
        });

        const activo = tabButtons.find((button) => button.getAttribute('aria-selected') === 'true');
        if (activo?.disabled || activo?.hidden) {
            const disponible = [...tabButtons].reverse().find((button) => !button.hidden && !button.disabled);
            setActiveStep(disponible?.getAttribute('data-step') || tabButtons[0]?.getAttribute('data-step'));
            return;
        }

        actualizarAccionPrincipal(activo?.getAttribute('data-step'));
    }

    function refreshSummary() {
        const cliente = getText('hero-cliente', 'Sin seleccionar');
        const items = getText('hero-detalles-count', '0 productos');
        const pago = getText('hero-tipo-pago', 'Sin definir');
        const subtotal = getText('total-subtotal', '$0,00');
        const descuento = getText('total-descuento', '-$0,00');
        const iva = getText('total-iva', '$0,00');
        const total = getText('total-final', '$0,00');

        setText('[data-side-cliente], [data-rev-cliente]', cliente);
        setText('[data-side-items], [data-rev-items]', items);
        setText('[data-side-pago], [data-rev-pago], [data-pago-summary]', pago);
        setText('[data-side-subtotal], [data-rev-subtotal]', subtotal);
        setText('[data-side-descuento], [data-rev-descuento]', descuento);
        setText('[data-side-iva], [data-rev-iva]', iva);
        setText('[data-side-total], [data-rev-total], [data-mobile-total]', total);
        setText('[data-conf-cliente]', cliente === 'Sin seleccionar' ? 'Cliente sin seleccionar' : cliente);
        setText('[data-conf-items]', items);
        setText('[data-conf-pago]', pago === 'Sin definir' ? 'Pago sin definir' : pago);
        setText('[data-conf-total]', total);

        const estado = root.querySelector('#vm-estado-global');
        if (estado) {
            const completo = cliente !== 'Sin seleccionar' && !items.startsWith('0 ') && pago !== 'Sin definir';
            estado.textContent = completo ? 'Lista para revisar' : 'Incompleta';
            estado.classList.toggle('text-emerald-300', completo);
        }

        refreshStepGating();
    }

    tabButtons.forEach((button) => {
        button.addEventListener('click', () => {
            if (button.disabled) return;
            setActiveStep(button.getAttribute('data-step'));
        });
    });

    function avanzar() {
        const activos = pasosVisibles();
        const actual = activos.findIndex((button) => button.getAttribute('aria-selected') === 'true');
        const siguiente = activos[actual + 1];
        if (siguiente && !siguiente.disabled) {
            setActiveStep(siguiente.getAttribute('data-step'));
            siguiente.focus({ preventScroll: true });
            document.getElementById(`step-panel-${siguiente.getAttribute('data-step')}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
    }

    function mostrarRequisitoCredito(mensaje, selector) {
        const alerta = document.getElementById('wizard-feedback');
        if (alerta) {
            alerta.textContent = mensaje;
            alerta.hidden = false;
        }
        const requisito = selector ? document.querySelector(selector) : null;
        requisito?.focus({ preventScroll: true });
        requisito?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }

    function verificarCredito() {
        const clienteId = parseInt(document.getElementById('hdn-cliente-id')?.value, 10) || 0;
        const hayProductos = (document.getElementById('tbody-detalles')?.children.length || 0) > 0;
        if (!clienteId) {
            mostrarRequisitoCredito('Seleccioná un cliente antes de verificar el crédito.', '#input-buscar-cliente');
            return;
        }
        if (!hayProductos) {
            mostrarRequisitoCredito('Agregá al menos un producto antes de verificar el crédito.', '#input-buscar-producto');
            return;
        }

        document.dispatchEvent(new CustomEvent('venta:solicitar-verificacion-credito'));
    }

    ventaForm?.addEventListener('submit', (event) => {
        const activo = pasosVisibles().find((button) => button.getAttribute('aria-selected') === 'true');
        if (activo?.getAttribute('data-step') !== 'revision') {
            event.preventDefault();
            avanzar();
        }
    });

    root.querySelectorAll('[data-wizard-primary]').forEach((button) => {
        button.addEventListener('click', () => {
            if (button.dataset.wizardAction === 'submit') {
                document.getElementById('btn-confirmar')?.click();
                return;
            }
            if (button.dataset.wizardAction === 'verify-credit') {
                verificarCredito();
                return;
            }
            if (button.dataset.wizardAction === 'simular-cotizacion') {
                document.getElementById('cotizacion-simular')?.click();
                return;
            }
            avanzar();
        });

    });

    ventaForm?.addEventListener('keydown', (event) => {
        if (event.key !== 'Enter' || event.isComposing || event.target instanceof HTMLTextAreaElement) return;
        // Dentro del cotizador embebido, Enter pertenece a sus buscadores: no debe
        // avanzar el wizard ni tocar la venta.
        if (event.target instanceof Element && event.target.closest(COTIZADOR_SELECTOR)) return;
        const activo = pasosVisibles().find((button) => button.getAttribute('aria-selected') === 'true');
        if (activo?.getAttribute('data-step') === 'revision') return;
        event.preventDefault();
        avanzar();
    });

    tabButtons.forEach((button) => {
        button.addEventListener('keydown', (event) => {
            const visibles = pasosVisibles().filter((tab) => !tab.disabled);
            const index = visibles.indexOf(button);
            if (index < 0) return;

            let nextIndex = null;
            if (event.key === 'ArrowRight') nextIndex = (index + 1) % visibles.length;
            if (event.key === 'ArrowLeft') nextIndex = (index - 1 + visibles.length) % visibles.length;
            if (event.key === 'Home') nextIndex = 0;
            if (event.key === 'End') nextIndex = visibles.length - 1;
            if (nextIndex === null) return;

            event.preventDefault();
            const next = visibles[nextIndex];
            setActiveStep(next.getAttribute('data-step'));
            next.focus({ preventScroll: true });
            next.scrollIntoView({ block: 'nearest', inline: 'nearest' });
        });
    });

    pagoSelect?.addEventListener('change', () => {
        creditoValidado = false;
        refreshStepGating();
    });
    document.addEventListener('venta:credito-validado', (event) => {
        // La elegibilidad (o una excepción aplicada) permite abrir el configurador, pero
        // Revisión sólo se habilita cuando el configurador canónico embebido confirmó el
        // plan (configurado=true). "aprobado" sin "configurado" ya no alcanza: antes
        // desbloqueaba Revisión con la sola elegibilidad, sin que existiera todavía
        // ningún plan de cuotas configurado.
        //
        // Ojo: no todo evento habla de "configurado". La verificación automática de
        // elegibilidad (venta-create.js) dispara sólo {aprobado} en cada chequeo —
        // incluido el que corre solo al cargar la página—, sin opinar sobre si el
        // crédito ya está configurado. Tratar esa ausencia como "false" pisaba la
        // semilla real del servidor (Model.CreditoConfigurado) apenas cargaba la
        // página. Sólo los eventos que informan "configurado" explícitamente pueden
        // cambiar este estado; el resto no lo toca.
        if (requiereCredito()) {
            if (typeof event.detail?.configurado === 'boolean') {
                creditoValidado = event.detail.configurado;
            }
        } else {
            creditoValidado = Boolean(event.detail?.aprobado);
        }
        refreshStepGating();
    });

    const observer = new MutationObserver(refreshSummary);
    ['hero-cliente', 'hero-detalles-count', 'hero-tipo-pago', 'total-subtotal', 'total-descuento', 'total-iva', 'total-final']
        .map((id) => document.getElementById(id))
        .filter(Boolean)
        .forEach((node) => observer.observe(node, { childList: true, characterData: true, subtree: true }));

    const initialStep = tabButtons.find((button) => button.getAttribute('aria-selected') === 'true')?.getAttribute('data-step')
        || tabButtons[0]?.getAttribute('data-step');
    setActiveStep(initialStep);
    actualizarNumeracionPasos();
    refreshSummary();

    // API pública para que venta-create.js pueda navegar al paso de un campo con error
    // y refrescar el grisado de pestañas cuando cambian cliente/productos/pago.
    window.VentaWizard = window.VentaWizard || {};
    window.VentaWizard.setActiveStep = setActiveStep;
    window.VentaWizard.refreshStepGating = refreshStepGating;
})();
