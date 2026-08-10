(() => {
    'use strict';

    const root = document.getElementById('venta-index-rework');
    if (!root) return;

    const tabs = Array.from(root.querySelectorAll('[data-venta-tab]'));
    const panels = Array.from(root.querySelectorAll('[data-venta-tab-panel]'));

    function ensureTabVisible(tab) {
        const region = tab.closest('[data-oc-scroll-region]');
        if (!region) return;

        const containerRect = region.getBoundingClientRect();
        const tabRect = tab.getBoundingClientRect();

        const overflowLeft = containerRect.left - tabRect.left;
        const overflowRight = tabRect.right - containerRect.right;

        if (overflowLeft <= 0 && overflowRight <= 0) return;

        const delta = overflowLeft > 0 ? -overflowLeft : overflowRight;
        const targetLeft = region.scrollLeft + delta;

        if (typeof region.scrollTo === 'function') {
            region.scrollTo({ left: targetLeft, behavior: 'instant' });
        } else {
            region.scrollLeft = targetLeft;
        }
    }

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

        ensureTabVisible(tab);

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
})();
