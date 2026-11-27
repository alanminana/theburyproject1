// catalogo-index.js — comportamiento común de las secciones de Inventario

// ── Acciones delegadas, avisos y desplazamiento horizontal (común a todas las secciones) ──
(function () {
    'use strict';

    var scrollAffordances = [];
    var catalogoModule = typeof CatalogoModule !== 'undefined' ? CatalogoModule : null;

    function getProductSelectionApi() {
        return catalogoModule && typeof catalogoModule.getProductSelectionApi === 'function'
            ? catalogoModule.getProductSelectionApi()
            : null;
    }

    function getModalApi(name) {
        return catalogoModule && typeof catalogoModule.getModalApi === 'function'
            ? catalogoModule.getModalApi(name)
            : null;
    }

    function openCatalogoModal(name, trigger) {
        if (name === 'precio-selection') {
            var selectionApi = getProductSelectionApi();
            var ids = selectionApi && typeof selectionApi.getIds === 'function'
                ? selectionApi.getIds()
                : [];
            var selectionPriceApi = getModalApi('precio');

            if (!selectionPriceApi) return;

            if (typeof selectionPriceApi.openWithSelection === 'function') {
                selectionPriceApi.openWithSelection(ids, trigger || null);
            } else if (typeof selectionPriceApi.open === 'function') {
                selectionPriceApi.open(trigger || null);
            }
            return;
        }

        var modalApi = getModalApi(name);
        if (!modalApi) return;

        if (name === 'historial-precio') {
            var productoId = trigger
                ? parseInt(trigger.getAttribute('data-catalogo-producto-id'), 10)
                : NaN;
            if (!isNaN(productoId) && typeof modalApi.open === 'function') {
                modalApi.open(productoId, trigger || null);
            }
            return;
        }

        if (typeof modalApi.open === 'function') {
            modalApi.open(trigger || null);
        }
    }

    function closeCatalogoModal(name) {
        var modalApi = getModalApi(name);
        if (modalApi && typeof modalApi.close === 'function') {
            modalApi.close();
        }
    }

    function runCatalogoAction(action) {
        var selectionApi = getProductSelectionApi();
        var precioApi = getModalApi('precio');
        var productoApi = getModalApi('producto');

        switch (action) {
            case 'clear-selection':
                if (selectionApi && typeof selectionApi.clearAll === 'function') {
                    selectionApi.clearAll();
                }
                break;
            case 'product-add-caracteristica':
                if (productoApi && typeof productoApi.addCaracteristica === 'function') {
                    productoApi.addCaracteristica();
                }
                break;
            case 'price-prev':
                if (precioApi && typeof precioApi.prevStep === 'function') {
                    precioApi.prevStep();
                }
                break;
            case 'price-next':
                if (precioApi && typeof precioApi.nextStep === 'function') {
                    precioApi.nextStep();
                }
                break;
            case 'price-apply':
                if (precioApi && typeof precioApi.apply === 'function') {
                    precioApi.apply();
                }
                break;
            default:
                break;
        }
    }

    function initDelegatedActions() {
        document.addEventListener('click', function (event) {
            var openTrigger = event.target.closest('[data-catalogo-modal-open]');
            if (openTrigger) {
                event.preventDefault();
                openCatalogoModal(openTrigger.getAttribute('data-catalogo-modal-open'), openTrigger);
                return;
            }

            var closeTrigger = event.target.closest('[data-catalogo-modal-close]');
            if (closeTrigger) {
                event.preventDefault();
                closeCatalogoModal(closeTrigger.getAttribute('data-catalogo-modal-close'));
                return;
            }

            var actionTrigger = event.target.closest('[data-catalogo-action]');
            if (actionTrigger) {
                event.preventDefault();
                runCatalogoAction(actionTrigger.getAttribute('data-catalogo-action'));
            }
        });
    }

    function initScrollAffordances() {
        if (!window.TheBury || typeof window.TheBury.initHorizontalScrollAffordance !== 'function') {
            return;
        }

        document.querySelectorAll('[data-oc-scroll]').forEach(function (root) {
            var instance = window.TheBury.initHorizontalScrollAffordance(root);
            if (instance) {
                scrollAffordances.push(instance);
            }
        });
    }

    function refreshScrollAffordances() {
        scrollAffordances.forEach(function (instance) {
            if (instance && typeof instance.update === 'function') {
                instance.update();
            }
        });
    }

    document.addEventListener(
        catalogoModule && catalogoModule.events ? catalogoModule.events.refreshScroll : 'catalogo:refresh-scroll',
        function () {
        window.requestAnimationFrame(function () {
            refreshScrollAffordances();
        });
    });

    document.addEventListener('catalogo:toast', function (e) {
        var detail  = e.detail || {};
        var message = detail.message || '';
        var type    = detail.type || 'success';

        var typeMap = {
            success: { cls: 'alert-erp-success', icon: 'check_circle', role: 'status' },
            error:   { cls: 'alert-erp-error',   icon: 'error',        role: 'alert'  },
            warning: { cls: 'alert-erp-warning', icon: 'warning',      role: 'alert'  }
        };
        var v = typeMap[type] || typeMap.success;

        var div = document.createElement('div');
        div.className = 'toast-msg alert-erp ' + v.cls + ' fixed bottom-4 right-4 z-[60] shadow-lg';
        div.setAttribute('role', v.role);
        var iconSpan = document.createElement('span');
        iconSpan.className = 'material-symbols-outlined';
        iconSpan.textContent = v.icon;
        div.appendChild(iconSpan);
        div.appendChild(document.createTextNode(message));
        document.body.appendChild(div);

        if (window.TheBury && typeof window.TheBury.autoDismissToasts === 'function') {
            window.TheBury.autoDismissToasts(4000);
        }
    });

    initDelegatedActions();
    initScrollAffordances();

    if (window.TheBury && typeof window.TheBury.autoDismissToasts === 'function') {
        window.TheBury.autoDismissToasts();
    }
})();
