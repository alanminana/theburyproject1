// catalogo-index.js — tabs, selección de productos y delegación de acciones del índice
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
                selectionPriceApi.openWithSelection(ids);
            } else if (typeof selectionPriceApi.open === 'function') {
                selectionPriceApi.open();
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
                modalApi.open(productoId);
            }
            return;
        }

        if (typeof modalApi.open === 'function') {
            modalApi.open();
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

    function getTabValue(button) {
        return button.getAttribute('data-catalogo-tab');
    }

    function initTabs() {
        var tabContainer = document.getElementById('catalogo-tabs');
        if (!tabContainer) return;

        var buttons = tabContainer.querySelectorAll('[data-catalogo-tab]');
        var panels = {
            productos: document.getElementById('tab-productos'),
            categorias: document.getElementById('tab-categorias'),
            marcas: document.getElementById('tab-marcas')
        };

        var actionButtons = {
            productos: document.getElementById('btn-crear-producto'),
            categorias: document.getElementById('btn-crear-categoria'),
            marcas: document.getElementById('btn-crear-marca')
        };
        var btnAjusteMasivo = document.getElementById('btn-ajuste-masivo');

        function switchTab(target) {
            buttons.forEach(function (button) {
                button.classList.remove('border-primary', 'text-primary', 'bg-primary/10');
                button.classList.add('border-transparent', 'text-slate-500', 'dark:text-slate-400');
            });

            var activeBtn = tabContainer.querySelector('[data-catalogo-tab="' + target + '"]');
            if (activeBtn) {
                activeBtn.classList.add('border-primary', 'text-primary', 'bg-primary/10');
                activeBtn.classList.remove('border-transparent', 'text-slate-500', 'dark:text-slate-400');
            }

            Object.keys(panels).forEach(function (key) {
                if (panels[key]) {
                    panels[key].classList.toggle('hidden', key !== target);
                }
            });

            Object.keys(actionButtons).forEach(function (key) {
                if (!actionButtons[key]) return;
                if (key === target) {
                    actionButtons[key].classList.remove('hidden');
                    actionButtons[key].classList.add('flex');
                } else {
                    actionButtons[key].classList.add('hidden');
                    actionButtons[key].classList.remove('flex');
                }
            });

            if (btnAjusteMasivo) {
                btnAjusteMasivo.classList.toggle('hidden', target !== 'productos');
                if (target === 'productos') {
                    btnAjusteMasivo.classList.add('flex');
                } else {
                    btnAjusteMasivo.classList.remove('flex');
                }
            }

            var selectionApi = getProductSelectionApi();
            if (selectionApi && typeof selectionApi.refreshUi === 'function') {
                selectionApi.refreshUi();
            }

            window.requestAnimationFrame(function () {
                refreshScrollAffordances();
            });
        }

        buttons.forEach(function (button) {
            button.addEventListener('click', function () {
                switchTab(getTabValue(button));
            });
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

    initDelegatedActions();
    initTabs();
    initScrollAffordances();

    if (window.TheBury && typeof window.TheBury.autoDismissToasts === 'function') {
        window.TheBury.autoDismissToasts();
    }
})();

// ─── Product Selection (checkboxes + floating bar) ─────────────
var ProductSelection = (function () {
    'use strict';

    var selectAll = document.getElementById('chk-select-all');
    var selectionBar = document.getElementById('selection-bar');
    var countEl = document.getElementById('selection-count');
    var countBtnEl = document.getElementById('selection-count-btn');
    var actionButtonBadge = document.getElementById('btn-ajuste-masivo-badge');
    var actionButtonBadgeCount = document.getElementById('btn-ajuste-masivo-count');
    var selectionChip = document.getElementById('catalogo-selection-chip');
    var selectionChipCount = document.getElementById('catalogo-selection-chip-count');
    var productosTab = document.getElementById('tab-productos');

    if (!selectAll) {
        return {
            getIds: function () { return []; },
            clearAll: function () {},
            getCount: function () { return 0; },
            refreshUi: function () {}
        };
    }

    function getCheckboxes() {
        return document.querySelectorAll('.chk-producto');
    }

    function isProductosTabActive() {
        return productosTab && !productosTab.classList.contains('hidden');
    }

    function getIds() {
        var ids = [];
        getCheckboxes().forEach(function (checkbox) {
            if (checkbox.checked) {
                ids.push(parseInt(checkbox.value, 10));
            }
        });
        return ids;
    }

    function getCount() {
        return getIds().length;
    }

    function updateRowState(checkbox) {
        if (!checkbox) return;

        var row = checkbox.closest('tr');
        if (!row) return;

        row.classList.toggle('bg-primary/5', checkbox.checked);
        row.classList.toggle('dark:bg-primary/10', checkbox.checked);
    }

    function updateActionAffordances(count) {
        var canShow = count > 0 && isProductosTabActive();

        if (actionButtonBadge && actionButtonBadgeCount) {
            actionButtonBadgeCount.textContent = count;
            actionButtonBadge.classList.toggle('hidden', !canShow);
            actionButtonBadge.classList.toggle('inline-flex', canShow);
        }

        if (selectionChip && selectionChipCount) {
            selectionChipCount.textContent = count;
            selectionChip.classList.toggle('hidden', !canShow);
            selectionChip.classList.toggle('inline-flex', canShow);
        }
    }

    function updateBar() {
        var count = getCount();
        if (countEl) countEl.textContent = count;
        if (countBtnEl) countBtnEl.textContent = count;

        if (selectionBar) {
            if (count > 0 && isProductosTabActive()) {
                selectionBar.classList.remove('hidden');
                selectionBar.classList.add('flex');
            } else {
                selectionBar.classList.add('hidden');
                selectionBar.classList.remove('flex');
            }
        }

        updateActionAffordances(count);

        var boxes = getCheckboxes();
        var total = boxes.length;
        selectAll.checked = count > 0 && count === total;
        selectAll.indeterminate = count > 0 && count < total;
    }

    function clearAll() {
        selectAll.checked = false;
        selectAll.indeterminate = false;
        getCheckboxes().forEach(function (checkbox) {
            checkbox.checked = false;
            updateRowState(checkbox);
        });
        updateBar();
    }

    selectAll.addEventListener('change', function () {
        var checked = selectAll.checked;
        getCheckboxes().forEach(function (checkbox) {
            checkbox.checked = checked;
            updateRowState(checkbox);
        });
        updateBar();
    });

    document.addEventListener('change', function (event) {
        if (event.target.classList.contains('chk-producto')) {
            updateRowState(event.target);
            updateBar();
        }
    });

    getCheckboxes().forEach(updateRowState);
    updateBar();

    return {
        getIds: getIds,
        clearAll: clearAll,
        getCount: getCount,
        refreshUi: updateBar
    };
})();

if (typeof CatalogoModule !== 'undefined' && typeof CatalogoModule.registerProductSelectionApi === 'function') {
    CatalogoModule.registerProductSelectionApi(ProductSelection);
}
