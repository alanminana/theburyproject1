// catalogo-index.js — Tab switching + dynamic action buttons + product selection for Catalogo Index
(function () {
    'use strict';

    var tabContainer = document.getElementById('catalogo-tabs');
    if (!tabContainer) return;

    var buttons = tabContainer.querySelectorAll('[data-tab]');
    var panels = {
        productos: document.getElementById('tab-productos'),
        categorias: document.getElementById('tab-categorias'),
        marcas: document.getElementById('tab-marcas')
    };

    // Action buttons per tab
    var actionButtons = {
        productos: document.getElementById('btn-crear-producto'),
        categorias: document.getElementById('btn-crear-categoria'),
        marcas: document.getElementById('btn-crear-marca')
    };
    var btnAjusteMasivo = document.getElementById('btn-ajuste-masivo');

    function switchTab(target) {
        // Update tab buttons
        buttons.forEach(function (b) {
            b.classList.remove('border-primary', 'text-primary', 'bg-primary/10');
            b.classList.add('border-transparent', 'text-slate-500', 'dark:text-slate-400');
        });
        var activeBtn = tabContainer.querySelector('[data-tab="' + target + '"]');
        if (activeBtn) {
            activeBtn.classList.add('border-primary', 'text-primary', 'bg-primary/10');
            activeBtn.classList.remove('border-transparent', 'text-slate-500', 'dark:text-slate-400');
        }

        // Update panels
        Object.keys(panels).forEach(function (key) {
            if (panels[key]) {
                panels[key].classList.toggle('hidden', key !== target);
            }
        });

        // Update action buttons
        Object.keys(actionButtons).forEach(function (key) {
            if (actionButtons[key]) {
                if (key === target) {
                    actionButtons[key].classList.remove('hidden');
                    actionButtons[key].classList.add('flex');
                } else {
                    actionButtons[key].classList.add('hidden');
                    actionButtons[key].classList.remove('flex');
                }
            }
        });

        // Show "Ajuste Masivo" only on Productos tab
        if (btnAjusteMasivo) {
            btnAjusteMasivo.classList.toggle('hidden', target !== 'productos');
            if (target === 'productos') btnAjusteMasivo.classList.add('flex');
            else btnAjusteMasivo.classList.remove('flex');
        }

        if (window.ProductSelection && typeof window.ProductSelection.refreshUi === 'function') {
            window.ProductSelection.refreshUi();
        }
    }

    buttons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            switchTab(btn.getAttribute('data-tab'));
        });
    });
})();

// ─── Product Selection (checkboxes + floating bar) ─────────────
var ProductSelection = (function () {
    'use strict';

    var selectAll = document.getElementById('chk-select-all');
    var selectionBar = document.getElementById('selection-bar');
    var countEl = document.getElementById('selection-count');
    var countBtnEl = document.getElementById('selection-count-btn');
    var actionButton = document.getElementById('btn-ajuste-masivo');
    var actionButtonBadge = document.getElementById('btn-ajuste-masivo-badge');
    var actionButtonBadgeCount = document.getElementById('btn-ajuste-masivo-count');
    var selectionChip = document.getElementById('catalogo-selection-chip');
    var selectionChipCount = document.getElementById('catalogo-selection-chip-count');
    var productosTab = document.getElementById('tab-productos');

    if (!selectAll) return { getIds: function () { return []; }, clearAll: function () {}, getCount: function () { return 0; }, refreshUi: function () {} };

    function getCheckboxes() {
        return document.querySelectorAll('.chk-producto');
    }

    function isProductosTabActive() {
        return productosTab && !productosTab.classList.contains('hidden');
    }

    function getIds() {
        var ids = [];
        getCheckboxes().forEach(function (cb) {
            if (cb.checked) ids.push(parseInt(cb.value));
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

        // Update select-all indeterminate state
        var boxes = getCheckboxes();
        var total = boxes.length;
        selectAll.checked = count > 0 && count === total;
        selectAll.indeterminate = count > 0 && count < total;
    }

    function clearAll() {
        selectAll.checked = false;
        selectAll.indeterminate = false;
        getCheckboxes().forEach(function (cb) {
            cb.checked = false;
            updateRowState(cb);
        });
        updateBar();
    }

    // Select all toggle
    selectAll.addEventListener('change', function () {
        var checked = selectAll.checked;
        getCheckboxes().forEach(function (cb) {
            cb.checked = checked;
            updateRowState(cb);
        });
        updateBar();
    });

    // Individual checkboxes (delegated)
    document.addEventListener('change', function (e) {
        if (e.target.classList.contains('chk-producto')) {
            updateRowState(e.target);
            updateBar();
        }
    });

    getCheckboxes().forEach(updateRowState);
    updateBar();

    return { getIds: getIds, clearAll: clearAll, getCount: getCount, refreshUi: updateBar };
})();
