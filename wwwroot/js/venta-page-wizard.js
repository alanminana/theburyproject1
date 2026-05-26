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

    function setText(selector, value) {
        root.querySelectorAll(selector).forEach((node) => {
            node.textContent = value;
        });
    }

    function getText(id, fallback) {
        const el = document.getElementById(id);
        const text = el?.textContent?.trim();
        return text || fallback;
    }

    function setActiveStep(step) {
        if (!step) return;

        tabButtons.forEach((button) => {
            const active = button.getAttribute('data-step') === step;
            button.classList.toggle('vm-step-tab--active', active);
            button.classList.toggle('is-active', active);
            button.setAttribute('aria-selected', active ? 'true' : 'false');
        });

        panels.forEach((panel) => {
            const active = panel.id === `step-panel-${step}`;
            panel.classList.toggle('hidden', !active);
            panel.hidden = !active;
        });
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
    }

    tabButtons.forEach((button) => {
        button.addEventListener('click', () => {
            setActiveStep(button.getAttribute('data-step'));
        });
    });

    root.querySelectorAll('[data-wizard-submit-proxy]').forEach((button) => {
        button.addEventListener('click', () => {
            const target = document.getElementById(button.getAttribute('data-wizard-submit-proxy'));
            target?.click();
        });
    });

    const observer = new MutationObserver(refreshSummary);
    ['hero-cliente', 'hero-detalles-count', 'hero-tipo-pago', 'total-subtotal', 'total-descuento', 'total-iva', 'total-final']
        .map((id) => document.getElementById(id))
        .filter(Boolean)
        .forEach((node) => observer.observe(node, { childList: true, characterData: true, subtree: true }));

    const initialStep = tabButtons.find((button) => button.getAttribute('aria-selected') === 'true')?.getAttribute('data-step')
        || tabButtons[0]?.getAttribute('data-step');
    setActiveStep(initialStep);
    refreshSummary();
})();
