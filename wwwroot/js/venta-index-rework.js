(() => {
    'use strict';

    const root = document.getElementById('venta-index-rework');
    if (!root) return;

    const tabs = Array.from(root.querySelectorAll('[data-venta-tab]'));
    const panels = Array.from(root.querySelectorAll('[data-venta-tab-panel]'));

    function activateTab(tab) {
        const target = tab.dataset.ventaTab;
        if (!target) return;

        tabs.forEach((item) => {
            const active = item === tab;
            item.classList.toggle('is-active', active);
            item.setAttribute('aria-selected', active ? 'true' : 'false');
            item.tabIndex = active ? 0 : -1;
        });

        panels.forEach((panel) => {
            const active = panel.dataset.ventaTabPanel === target;
            panel.hidden = !active;
        });

        window.requestAnimationFrame(() => {
            if (window.VentaModule && typeof window.VentaModule.initScrollAffordance === 'function') {
                root.querySelectorAll('[data-oc-scroll]').forEach((scrollRoot) => {
                    window.VentaModule.initScrollAffordance(scrollRoot);
                });
            }
        });
    }

    function moveFocus(current, direction) {
        if (!tabs.length) return;

        const index = tabs.indexOf(current);
        if (index < 0) return;

        const nextIndex = (index + direction + tabs.length) % tabs.length;
        tabs[nextIndex].focus();
        activateTab(tabs[nextIndex]);
    }

    tabs.forEach((tab, index) => {
        tab.tabIndex = tab.classList.contains('is-active') ? 0 : -1;

        tab.addEventListener('click', () => activateTab(tab));
        tab.addEventListener('keydown', (event) => {
            if (event.key === 'ArrowRight') {
                event.preventDefault();
                moveFocus(tab, 1);
            } else if (event.key === 'ArrowLeft') {
                event.preventDefault();
                moveFocus(tab, -1);
            } else if (event.key === 'Home') {
                event.preventDefault();
                tabs[0].focus();
                activateTab(tabs[0]);
            } else if (event.key === 'End') {
                event.preventDefault();
                tabs[tabs.length - 1].focus();
                activateTab(tabs[tabs.length - 1]);
            }
        });

        if (index > 0 && tab.getAttribute('aria-selected') !== 'true') {
            tab.setAttribute('aria-selected', 'false');
        }
    });

    root.querySelectorAll('[data-quick-filter]').forEach((chip) => {
        chip.addEventListener('click', () => {
            const form = document.getElementById('form-filtros');
            const targetName = chip.dataset.quickFilter;
            const targetValue = chip.dataset.quickValue || '';
            const field = form ? form.elements[targetName] : null;

            if (!field) return;

            field.value = targetValue;
            form.requestSubmit();
        });
    });

    root.querySelectorAll('.toast-msg').forEach((toast) => {
        window.setTimeout(() => {
            toast.style.opacity = '0';
            toast.style.transform = 'translateY(-4px)';
            window.setTimeout(() => toast.remove(), 240);
        }, 5200);
    });
})();
