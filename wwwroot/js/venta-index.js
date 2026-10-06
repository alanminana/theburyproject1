/**
 * venta-index.js — Ventas Index page
 * Toast auto-dismiss + scroll affordances + CTA "Nueva venta" bloqueada + tabs del Centro de Ventas
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

    // VENTA-UI-03.1: puede haber más de un CTA "Nueva Venta bloqueada" en el DOM
    // (cabecera + mobile-sticky-bar); querySelector solo ataba el primero y
    // dejaba el resto sin handler. querySelectorAll + forEach ata cada elemento
    // una sola vez y todos comparten el mismo highlightSection (mismo mensaje,
    // mismo scroll, sin duplicarlo).
    document.querySelectorAll('[data-action="nueva-venta-bloqueada"]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            highlightSection(
                'panel-caja-cerrada',
                'btn-abrir-caja',
                'msg-caja-cerrada',
                'Primero tenés que abrir la caja para poder vender.'
            );
        });
    });

    // ── Tabs del Centro de Ventas (roving tabindex + flechas/Home/End + deep-link #hash) ───
    function initTabs() {
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

        // Deep-link: Index#envios abre directamente esa pestaña (volver desde el detalle de un envío).
        const hashTarget = (window.location.hash || '').replace('#', '');
        if (hashTarget) {
            const hashTab = tabs.find((item) => item.dataset.ventaTab === hashTarget);
            if (hashTab) activateTab(hashTab);
        }
    }

    initTabs();

    // filter panel toggle — handled by module-index.js (shared)
})();
