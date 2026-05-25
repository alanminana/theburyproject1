/**
 * venta-modal-rework.js
 * KIRA-VENTAS-MODAL-REWORK-1C / 1E
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
 *   window.VentaModalRework.refreshState()          ← 1E
 *   window.VentaModalRework.goToFirstInvalidStep()  ← 1E
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

    // Tipos de pago que requieren verificación crediticia (coincide con TipoPago.cs enum)
    var TIPO_PAGO_CREDITO = ['5', '7']; // CreditoPersonal = 5, CuentaCorriente = 7


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
                    setTimeout(function () {
                        panel.classList.remove('vm-step-panel-active');
                    }, 300);
                } else {
                    panel.classList.add('hidden');
                    panel.classList.remove('vm-step-panel-active');
                }
            }
        });

        // Re-aplicar clases de estado a tabs inactivos tras el cambio de paso
        refreshState();
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
            // Diferir refreshState para que venta-create.js tenga tiempo de resetear
            // sus paneles de crédito/verificación antes de evaluar el estado
            setTimeout(refreshState, 50);
        });
    }


    // ── 1E: Evaluación de estados de pasos ────────────────────────────────────

    function evaluateStepStates() {
        var infoClienteEl   = document.getElementById('info-cliente');
        var tbody           = document.getElementById('tbody-detalles');
        var selectTP        = document.getElementById('select-tipo-pago');
        var panelCupoInsuf  = document.getElementById('panel-cupo-insuficiente');
        var panelMora       = document.getElementById('panel-alerta-mora');
        var panelDocFalt    = document.getElementById('panel-documentacion-faltante');
        var panelCupoSuf    = document.getElementById('panel-cupo-suficiente');

        // info-cliente visible === cliente seleccionado (venta-create.js lo muestra/oculta)
        var tieneCliente   = infoClienteEl && !infoClienteEl.classList.contains('hidden');
        var tieneProductos = tbody && tbody.children.length > 0;
        var tipoPago       = selectTP ? selectTP.value : '';
        var tienePago      = tipoPago !== '';

        var requiereCredito = TIPO_PAGO_CREDITO.indexOf(tipoPago) !== -1;
        var cupoInsuf       = panelCupoInsuf && !panelCupoInsuf.classList.contains('hidden');
        var moraActiva      = panelMora && !panelMora.classList.contains('hidden');
        var docFalt         = panelDocFalt && !panelDocFalt.classList.contains('hidden');
        var cupoSuf         = panelCupoSuf && !panelCupoSuf.classList.contains('hidden');

        // Paso Cliente
        var estadoCliente;
        if (tieneCliente) {
            estadoCliente = 'complete';
        } else if (requiereCredito) {
            // Crédito/Cuenta Corriente requieren cliente; marca urgente
            estadoCliente = 'warning';
        } else {
            estadoCliente = 'default';
        }

        // Paso Productos
        var estadoProductos = tieneProductos ? 'complete' : 'default';

        // Paso Pago (panel de detalle de cobro: tarjeta, cheque, planes)
        var estadoPago;
        if (!tienePago) {
            estadoPago = 'default';
        } else if (cupoInsuf) {
            estadoPago = 'warning';
        } else {
            estadoPago = 'complete';
        }

        // Paso Crédito
        var estadoCredito;
        if (!requiereCredito) {
            estadoCredito = 'complete'; // no aplica para este tipo de pago
        } else if (cupoInsuf || moraActiva) {
            estadoCredito = 'warning';
        } else if (docFalt) {
            estadoCredito = 'warning';
        } else if (cupoSuf) {
            estadoCredito = 'complete';
        } else {
            estadoCredito = 'default'; // requiere verificación pero aún no verificado
        }

        // Paso Revisión
        var hayAlertas = cupoInsuf || moraActiva || docFalt;
        var estadoRevision;
        if (tieneCliente && tieneProductos && tienePago && !hayAlertas) {
            estadoRevision = 'complete';
        } else if (hayAlertas) {
            estadoRevision = 'warning';
        } else {
            estadoRevision = 'default';
        }

        return {
            cliente:  estadoCliente,
            productos: estadoProductos,
            pago:     estadoPago,
            credito:  estadoCredito,
            revision: estadoRevision
        };
    }

    function refreshState() {
        var estados = evaluateStepStates();

        updateStepState('cliente',  estados.cliente);
        updateStepState('productos', estados.productos);
        updateStepState('pago',     estados.pago);
        updateStepState('credito',  estados.credito);
        updateStepState('revision', estados.revision);

        // Badge global de operación
        var hayAlertas  = estados.credito === 'warning' || estados.pago === 'warning' || estados.revision === 'warning';
        var todoCritico = estados.cliente === 'complete' && estados.productos === 'complete' && estados.pago === 'complete';

        if (todoCritico && !hayAlertas) {
            setOperationState('lista');
        } else if (hayAlertas) {
            setOperationState('alerta');
        } else {
            setOperationState('incompleta');
        }
    }


    // ── 1E: Navegación automática al paso inválido ────────────────────────────

    function safelyFocus(id) {
        var el = document.getElementById(id);
        if (!el || typeof el.focus !== 'function') return;
        // Diferir foco para que el panel esté visible antes de enfocar
        setTimeout(function () { el.focus(); }, 60);
    }

    function goToFirstInvalidStep() {
        var estados = evaluateStepStates();

        // Prioridad: cliente → productos → crédito → pago
        if (estados.cliente !== 'complete') {
            activateStep('cliente');
            safelyFocus('input-buscar-cliente');
            return 'cliente';
        }

        if (estados.productos !== 'complete') {
            activateStep('productos');
            safelyFocus('input-buscar-producto');
            return 'productos';
        }

        if (estados.credito === 'warning') {
            activateStep('credito');
            safelyFocus('btn-verificar-elegibilidad');
            return 'credito';
        }

        if (estados.pago === 'warning') {
            activateStep('pago');
            return 'pago';
        }

        return null; // todo OK, dejar proceder el submit
    }


    // ── 1E: Observers y listeners de estado ──────────────────────────────────

    function initStateObservers() {
        // Cliente: observar visibilidad de #info-cliente
        // venta-create.js hace show(infoCliente)/hide(infoCliente) al seleccionar/limpiar
        var infoCliente = document.getElementById('info-cliente');
        if (infoCliente) {
            new MutationObserver(refreshState)
                .observe(infoCliente, { attributes: true, attributeFilter: ['class'] });
        }

        // Productos: observar hijos de #tbody-detalles (renderDetalles() los reemplaza)
        var tbody = document.getElementById('tbody-detalles');
        if (tbody) {
            new MutationObserver(refreshState)
                .observe(tbody, { childList: true });
        }

        // Tipo de pago (change determina si se requiere crédito y qué paneles se muestran)
        var selectTP = document.getElementById('select-tipo-pago');
        if (selectTP) {
            selectTP.addEventListener('change', refreshState);
        }

        // Paneles crediticios: observar clase hidden (venta-create.js los muestra/oculta)
        var creditPanelIds = [
            'panel-cupo-insuficiente',
            'panel-alerta-mora',
            'panel-documentacion-faltante',
            'panel-cupo-suficiente'
        ];
        creditPanelIds.forEach(function (id) {
            var el = document.getElementById(id);
            if (el) {
                new MutationObserver(refreshState)
                    .observe(el, { attributes: true, attributeFilter: ['class'] });
            }
        });

        // Vendedor (si el selector delegado existe en la vista)
        var vendedor = document.getElementById('VendedorUserId');
        if (vendedor) {
            vendedor.addEventListener('change', refreshState);
        }
    }


    // ── 1E: Navegación al intentar confirmar ─────────────────────────────────

    function initSubmitNavigation() {
        // Fase de captura: se ejecuta antes de onclick="VentaCrearModal.submit()".
        // Si hay pasos inválidos, navegar al primero; el submit sigue su curso normal.
        // venta-create.js maneja su propia validación del formulario.
        document.addEventListener('click', function (e) {
            if (!e.target.closest('#btn-confirmar') && !e.target.closest('.vm-btn-confirm-sm')) return;
            goToFirstInvalidStep();
        }, true);
    }


    // ── Init ──────────────────────────────────────────────────────────────────

    function init() {
        initWizardTabs();
        initPagoItemSubmodal();
        initStickyTotalMirror();
        initModalOpenReset();
        initStateObservers();
        initSubmitNavigation();
        // Estado inicial luego de que venta-create.js ejecuta su init
        setTimeout(refreshState, 100);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }


    // ── API pública ───────────────────────────────────────────────────────────

    window.VentaModalRework = {
        activateStep:         activateStep,
        openSubmodal:         openSubmodal,
        closeSubmodal:        closeSubmodal,
        setOperationState:    setOperationState,
        updateStepState:      updateStepState,
        refreshState:         refreshState,
        goToFirstInvalidStep: goToFirstInvalidStep
    };

}());
