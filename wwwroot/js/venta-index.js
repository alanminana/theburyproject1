/**
 * venta-index.js — Ventas Index page
 * Toast auto-dismiss + scroll affordances
 */
(() => {
    'use strict';

    const theBury = window.TheBury || {};
    const ventaModule = window.VentaModule || {};
    let scrollAffordances = [];

    if (typeof ventaModule.initSharedUi === 'function') {
        ventaModule.initSharedUi();
    } else if (typeof theBury.autoDismissToasts === 'function') {
        theBury.autoDismissToasts();
    }

    function initScrollAffordances(scope) {
        const roots = (scope || document).querySelectorAll('[data-oc-scroll]');

        roots.forEach(function (root) {
            if (root.dataset.ventaScrollBound === 'true') {
                return;
            }

            const instance = typeof ventaModule.initScrollAffordance === 'function'
                ? ventaModule.initScrollAffordance(root)
                : null;

            root.dataset.ventaScrollBound = 'true';

            if (instance) {
                scrollAffordances.push({ root, instance });
            }
        });

        refreshScrollAffordances();
    }

    function refreshScrollAffordances() {
        scrollAffordances = scrollAffordances.filter(function (entry) {
            if (!entry.root || !entry.root.isConnected) {
                return false;
            }

            if (typeof ventaModule.refreshScrollAffordance === 'function') {
                ventaModule.refreshScrollAffordance(entry.instance);
            } else if (entry.instance && typeof entry.instance.update === 'function') {
                entry.instance.update();
            }

            return true;
        });
    }

    function queueScrollRefresh() {
        window.requestAnimationFrame(function () {
            refreshScrollAffordances();
        });
    }

    initScrollAffordances(document);

    // ── Nueva Venta bloqueada: highlight panel caja cerrada ───
    function highlightSection(panelId, focusId, msgId, msgText) {
        const panel = document.getElementById(panelId);
        const focusEl = document.getElementById(focusId);
        const msgEl = document.getElementById(msgId);
        if (!panel) return;

        panel.scrollIntoView({ behavior: 'smooth', block: 'center' });

        panel.classList.remove('caja-highlight-active');
        void panel.offsetWidth;
        panel.classList.add('caja-highlight-active');

        if (focusEl) {
            setTimeout(() => focusEl.focus({ preventScroll: true }), 300);
        }

        if (msgEl && msgText) {
            msgEl.textContent = msgText;
            msgEl.classList.remove('sr-only');
            msgEl.classList.add('ml-2', 'text-xs', 'font-semibold', 'text-amber-400');
        }

        setTimeout(() => {
            panel.classList.remove('caja-highlight-active');
            if (msgEl) {
                msgEl.textContent = '';
                msgEl.classList.add('sr-only');
                msgEl.classList.remove('ml-2', 'text-xs', 'font-semibold', 'text-amber-400');
            }
        }, 2200);
    }

    const btnNuevaVentaBloqueada = document.querySelector('[data-action="nueva-venta-bloqueada"]');
    if (btnNuevaVentaBloqueada) {
        btnNuevaVentaBloqueada.addEventListener('click', function () {
            highlightSection(
                'panel-caja-cerrada',
                'btn-abrir-caja',
                'msg-caja-cerrada',
                'Primero tenés que abrir la caja para poder vender.'
            );
        });
    }

    // filter panel toggle — handled by module-index.js (shared)
})();
