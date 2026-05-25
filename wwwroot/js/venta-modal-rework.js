/**
 * venta-modal-rework.js
 * KIRA-VENTAS-MODAL-REWORK-1C
 *
 * Wizard UI para el modal fullscreen de Nueva Venta.
 * Complementa venta-crear-modal.js y venta-create.js sin reemplazarlos.
 *
 * Expone:
 *   window.VentaModalRework.activateStep(stepName)
 *   window.VentaModalRework.openSubmodal(id)
 *   window.VentaModalRework.closeSubmodal(id)
 *   window.VentaModalRework.setOperationState(state)
 *   window.VentaModalRework.updateStepState(stepName, state)
 *
 * Orden de carga requerido:
 *   horizontal-scroll-affordance.js → venta-module.js → venta-create.js
 *   → venta-crear-modal.js → venta-modal-rework.js
 */
(function () {
    'use strict';

    // Guard: ejecutar solo en páginas que contienen el wizard
    if (!document.getElementById('modal-crear-venta')) return;

    var STEPS = ['cliente', 'productos', 'pago', 'credito', 'revision'];


    // ── Wizard Steps ──────────────────────────────────────────────────────────

    function activateStep(stepName) {
        if (STEPS.indexOf(stepName) === -1) return;

        STEPS.forEach(function (s) {
            var tab   = document.getElementById('step-btn-' + s);
            var panel = document.getElementById('step-panel-' + s);
            var isActive = s === stepName;

            if (tab) {
                tab.setAttribute('aria-selected', isActive ? 'true' : 'false');
                tab.tabIndex = isActive ? 0 : -1;
                if (isActive) {
                    tab.classList.add('vm-step-tab--active');
                } else {
                    tab.classList.remove('vm-step-tab--active');
                }
            }

            if (panel) {
                if (isActive) {
                    panel.classList.remove('hidden');
                    panel.classList.add('vm-step-panel-active');
                    // Quitar clase de animación después de que termina
                    setTimeout(function () {
                        panel.classList.remove('vm-step-panel-active');
                    }, 300);
                } else {
                    panel.classList.add('hidden');
                    panel.classList.remove('vm-step-panel-active');
                }
            }
        });
    }

    function getCurrentStepIndex() {
        for (var i = 0; i < STEPS.length; i++) {
            var tab = document.getElementById('step-btn-' + STEPS[i]);
            if (tab && tab.getAttribute('aria-selected') === 'true') return i;
        }
        return 0;
    }

    function updateStepState(stepName, state) {
        // state: 'default' | 'complete' | 'warning'
        var tab = document.getElementById('step-btn-' + stepName);
        if (!tab) return;
        var isActive = tab.getAttribute('aria-selected') === 'true';

        tab.classList.remove('vm-step-tab--complete', 'vm-step-tab--warning');
        if (!isActive) {
            if (state === 'complete') tab.classList.add('vm-step-tab--complete');
            else if (state === 'warning') tab.classList.add('vm-step-tab--warning');
        }
    }


    // ── Tab: click y teclado ──────────────────────────────────────────────────

    function initWizardTabs() {
        var tablist = document.querySelector('#modal-crear-venta [role="tablist"]');
        if (!tablist) return;

        // Click sobre un tab
        tablist.addEventListener('click', function (e) {
            var tab = e.target.closest('[role="tab"]');
            if (!tab || !tab.dataset.step) return;
            activateStep(tab.dataset.step);
        });

        // Navegación por teclado: ArrowRight / ArrowLeft / Home / End
        tablist.addEventListener('keydown', function (e) {
            var idx    = getCurrentStepIndex();
            var newIdx = -1;

            if (e.key === 'ArrowRight') {
                newIdx = Math.min(idx + 1, STEPS.length - 1);
            } else if (e.key === 'ArrowLeft') {
                newIdx = Math.max(idx - 1, 0);
            } else if (e.key === 'Home') {
                newIdx = 0;
            } else if (e.key === 'End') {
                newIdx = STEPS.length - 1;
            }

            if (newIdx >= 0) {
                e.preventDefault();
                activateStep(STEPS[newIdx]);
                var nextTab = document.getElementById('step-btn-' + STEPS[newIdx]);
                if (nextTab) nextTab.focus();
            }
        });
    }


    // ── Estado global del wizard ──────────────────────────────────────────────

    function setOperationState(state) {
        // state: 'incompleta' | 'lista' | 'alerta' | 'error'
        var badge = document.getElementById('vm-estado-global');
        if (!badge) return;

        badge.classList.remove('vm-estado--listo', 'vm-estado--alerta', 'vm-estado--error');

        if (state === 'lista') {
            badge.classList.add('vm-estado--listo');
            badge.textContent = 'Lista';
        } else if (state === 'alerta') {
            badge.classList.add('vm-estado--alerta');
            badge.textContent = 'Atención';
        } else if (state === 'error') {
            badge.classList.add('vm-estado--error');
            badge.textContent = 'Error';
        } else {
            badge.textContent = 'Incompleta';
        }
    }


    // ── Submodales ────────────────────────────────────────────────────────────

    function openSubmodal(id) {
        var m = document.getElementById(id);
        if (!m) return;
        m.classList.remove('hidden');
        document.body.style.overflow = 'hidden';
    }

    function closeSubmodal(id) {
        var m = document.getElementById(id);
        if (!m) return;
        m.classList.add('hidden');
        // Restaurar overflow solo si ningún otro submodal sigue abierto
        var stillOpen = document.querySelector(
            '#modal-pago-item:not(.hidden), #modal-documentacion:not(.hidden)'
        );
        if (!stillOpen) {
            document.body.style.overflow = '';
        }
    }

    function initPagoItemSubmodal() {
        if (!document.getElementById('modal-pago-item')) return;

        // Cerrar con cualquier botón .btn-cerrar-pago-item (delegado)
        document.addEventListener('click', function (e) {
            if (e.target.closest('.btn-cerrar-pago-item')) {
                closeSubmodal('modal-pago-item');
            }
        });

        // Escape cierra el submodal superior (fase de captura, antes del handler
        // de venta-crear-modal.js que cierra el modal principal)
        document.addEventListener('keydown', function (e) {
            if (e.key !== 'Escape') return;
            var pagoItem = document.getElementById('modal-pago-item');
            if (pagoItem && !pagoItem.classList.contains('hidden')) {
                e.stopPropagation();
                closeSubmodal('modal-pago-item');
            }
        }, true);
    }


    // ── Sticky total mirror ───────────────────────────────────────────────────
    // Espeja #total-final → #vm-modal-sticky-total vía MutationObserver.
    // Reemplaza el inline <script> que existía en _VentaCrearModal.cshtml.

    function initStickyTotalMirror() {
        var src  = document.getElementById('total-final');
        var dest = document.getElementById('vm-modal-sticky-total');
        if (!src || !dest) return;
        dest.textContent = src.textContent;
        new MutationObserver(function () {
            dest.textContent = src.textContent;
        }).observe(src, { childList: true, characterData: true, subtree: true });
    }


    // ── Integración con VentaCrearModal ───────────────────────────────────────
    // Cuando el modal se abre, resetear siempre al paso 1 (Cliente).

    function initModalOpenReset() {
        document.addEventListener('venta-crear-modal:open', function () {
            activateStep(STEPS[0]);
            setOperationState('incompleta');
        });
    }


    // ── Init ──────────────────────────────────────────────────────────────────

    function init() {
        initWizardTabs();
        initPagoItemSubmodal();
        initStickyTotalMirror();
        initModalOpenReset();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }


    // ── API pública ───────────────────────────────────────────────────────────

    window.VentaModalRework = {
        activateStep:      activateStep,
        openSubmodal:      openSubmodal,
        closeSubmodal:     closeSubmodal,
        setOperationState: setOperationState,
        updateStepState:   updateStepState
    };

}());
