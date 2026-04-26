// precio-aumento-modal.js — Modal de Aumento de Precios (3 pasos: Alcance → Configuración → Vista previa)
const PrecioModal = (() => {
    'use strict';

    const catalogoModule = typeof CatalogoModule !== 'undefined' ? CatalogoModule : null;
    let currentStep = 1;
    let tipoCambio = 'porcentaje'; // 'porcentaje' | 'monto'
    let simulationResult = null;
    let selectedProductIds = []; // IDs pre-selected from table checkboxes
    let selectedScopeSnapshot = [];

    function dispatchScrollRefresh() {
        if (catalogoModule && typeof catalogoModule.requestScrollRefresh === 'function') {
            catalogoModule.requestScrollRefresh();
            return;
        }

        document.dispatchEvent(new CustomEvent('catalogo:refresh-scroll'));
    }

    // ─── DOM refs ──────────────────────────────────────────────
    const modal = () => document.getElementById('modal-aumento-precios');
    const stepPanels = () => [
        document.getElementById('precio-step-1'),
        document.getElementById('precio-step-2'),
        document.getElementById('precio-step-3')
    ];
    const stepIndicators = () => [
        document.getElementById('step-ind-1'),
        document.getElementById('step-ind-2'),
        document.getElementById('step-ind-3')
    ];

    const btnNext = () => document.getElementById('precio-btn-next');
    const btnBack = () => document.getElementById('precio-btn-back');
    const btnApply = () => document.getElementById('precio-btn-apply');

    const validationSummary = () => document.getElementById('precio-modal-validation-summary');
    const validationText = () => document.getElementById('precio-modal-validation-text');
    const selectedScopeCard = () => document.getElementById('precio-scope-seleccionados');
    const selectedScopeRadio = () => {
        var card = selectedScopeCard();
        return card ? card.querySelector('input[type="radio"]') : null;
    };
    const selectedScopeCount = () => document.getElementById('precio-scope-sel-count');
    const selectedScopeHelper = () => document.getElementById('precio-scope-sel-helper');
    const selectedScopeBadge = () => document.getElementById('precio-scope-sel-badge');
    const selectedSummary = () => document.getElementById('precio-selection-summary');
    const selectedSummaryCount = () => document.getElementById('precio-selection-summary-count');

    // ─── Open / Close ──────────────────────────────────────────
    function open() {
        reset();
        syncSelectionState(readCurrentSelection(), true);
        modal().classList.remove('hidden');
        document.body.style.overflow = 'hidden';
        dispatchScrollRefresh();
    }

    function openWithSelection(ids) {
        reset();
        syncSelectionState(ids, true);
        modal().classList.remove('hidden');
        document.body.style.overflow = 'hidden';
        dispatchScrollRefresh();
    }

    function close() {
        modal().classList.add('hidden');
        document.body.style.overflow = '';
    }

    function reset() {
        currentStep = 1;
        tipoCambio = 'porcentaje';
        simulationResult = null;
        selectedProductIds = [];
        selectedScopeSnapshot = [];
        hideError();

        // Reset scope to "todos"
        var radios = document.querySelectorAll('input[name="precio-scope"]');
        radios.forEach(function (r) { r.checked = r.value === 'todos'; });
        updateScopeCards();

        // Hide category/brand dropdowns
        document.getElementById('precio-cat-select-wrapper').classList.add('hidden');
        document.getElementById('precio-marca-select-wrapper').classList.add('hidden');
        document.getElementById('precio-categoria-id').value = '';
        document.getElementById('precio-marca-id').value = '';

        // Reset config
        document.getElementById('precio-motivo').selectedIndex = 0;
        setTipoCambio('porcentaje');
        var valorInput = document.getElementById('precio-valor');
        if (valorInput) valorInput.value = '';

        var redondeos = document.querySelectorAll('input[name="precio-redondeo"]');
        redondeos.forEach(function (r) { r.checked = r.value === 'none'; });

        // Reset preview
        document.getElementById('precio-preview-tbody').innerHTML = '';
        document.getElementById('precio-preview-loading').classList.add('hidden');

        var moreEl = document.getElementById('precio-preview-more');
        if (moreEl) moreEl.classList.add('hidden');

        showStep(1);
    }

    // ─── Step Navigation ───────────────────────────────────────
    function showStep(step) {
        currentStep = step;
        var panels = stepPanels();
        var indicators = stepIndicators();

        panels.forEach(function (p, i) {
            if (p) p.classList.toggle('hidden', i !== step - 1);
        });

        // Update step indicators
        indicators.forEach(function (ind, i) {
            if (!ind) return;
            var circle = ind.querySelector('span:first-child');
            var label = ind.querySelector('span:last-child');
            if (i < step - 1) {
                // Completed step
                circle.className = 'w-7 h-7 rounded-full bg-emerald-500 text-white text-xs font-bold flex items-center justify-center';
                circle.innerHTML = '<span class="material-symbols-outlined text-sm">check</span>';
                label.className = 'text-xs font-bold text-emerald-500';
            } else if (i === step - 1) {
                // Active step
                circle.className = 'w-7 h-7 rounded-full bg-primary text-white text-xs font-bold flex items-center justify-center';
                circle.textContent = String(i + 1);
                label.className = 'text-xs font-bold text-primary';
            } else {
                // Future step
                circle.className = 'w-7 h-7 rounded-full bg-slate-200 dark:bg-slate-700 text-slate-500 dark:text-slate-400 text-xs font-bold flex items-center justify-center';
                circle.textContent = String(i + 1);
                label.className = 'text-xs font-medium text-slate-400 dark:text-slate-500';
            }
        });

        // Footer buttons
        var back = btnBack();
        var next = btnNext();
        var apply = btnApply();

        if (step === 1) {
            back.classList.add('hidden');
            back.classList.remove('flex');
            next.classList.remove('hidden');
            apply.classList.add('hidden');
            apply.classList.remove('flex');
        } else if (step === 2) {
            back.classList.remove('hidden');
            back.classList.add('flex');
            next.classList.remove('hidden');
            apply.classList.add('hidden');
            apply.classList.remove('flex');
        } else {
            back.classList.remove('hidden');
            back.classList.add('flex');
            next.classList.add('hidden');
            apply.classList.remove('hidden');
            apply.classList.add('flex');
        }

        dispatchScrollRefresh();
    }

    function nextStep() {
        hideError();
        if (currentStep === 1) {
            if (!validateStep1()) return;
            showStep(2);
        } else if (currentStep === 2) {
            if (!validateStep2()) return;
            showStep(3);
            loadPreview();
        }
    }

    function prevStep() {
        hideError();
        if (currentStep === 2) showStep(1);
        else if (currentStep === 3) showStep(2);
    }

    // ─── Scope Cards ───────────────────────────────────────────
    function updateScopeCards() {
        var cards = document.querySelectorAll('.precio-scope-card');
        cards.forEach(function (card) {
            var radio = card.querySelector('input[type="radio"]');
            if (radio && radio.checked && !radio.disabled) {
                card.classList.add('selected', 'border-primary', 'bg-primary/5');
                card.classList.remove('border-slate-200', 'dark:border-slate-700', 'border-dashed', 'opacity-60', 'cursor-not-allowed');
            } else {
                card.classList.remove('selected', 'border-primary', 'bg-primary/5');
                if (radio && radio.disabled) {
                    card.classList.add('border-slate-200', 'dark:border-slate-700', 'border-dashed', 'opacity-60', 'cursor-not-allowed');
                } else {
                    card.classList.add('border-slate-200', 'dark:border-slate-700');
                    card.classList.remove('border-dashed', 'opacity-60', 'cursor-not-allowed');
                }
            }
        });

        // Toggle category/brand dropdowns
        var scope = getScope();
        document.getElementById('precio-cat-select-wrapper').classList.toggle('hidden', scope !== 'categoria');
        document.getElementById('precio-marca-select-wrapper').classList.toggle('hidden', scope !== 'marca');
    }

    function getScope() {
        var checked = document.querySelector('input[name="precio-scope"]:checked');
        return checked ? checked.value : 'todos';
    }

    function getProductSelectionApi() {
        return catalogoModule && typeof catalogoModule.getProductSelectionApi === 'function'
            ? catalogoModule.getProductSelectionApi()
            : null;
    }

    function readCurrentSelection() {
        var selectionApi = getProductSelectionApi();
        if (selectionApi && typeof selectionApi.getIds === 'function') {
            return selectionApi.getIds();
        }

        return [];
    }

    function syncSelectionState(ids, preferSelected) {
        selectedProductIds = Array.isArray(ids) ? ids.slice() : [];
        selectedScopeSnapshot = selectedProductIds.slice();

        var count = selectedProductIds.length;
        var selCard = selectedScopeCard();
        var selRadio = selectedScopeRadio();
        var selCount = selectedScopeCount();
        var selHelper = selectedScopeHelper();
        var selBadge = selectedScopeBadge();
        var summary = selectedSummary();
        var summaryCount = selectedSummaryCount();

        if (selCount) selCount.textContent = count;
        if (summaryCount) summaryCount.textContent = count;

        if (!selCard || !selRadio) {
            return;
        }

        if (count > 0) {
            selRadio.disabled = false;
            if (selHelper) {
                selHelper.textContent = 'Solo los productos que tildaste en la tabla.';
            }
            if (summary) {
                summary.classList.remove('hidden');
                summary.classList.add('flex');
            }
            if (selBadge) {
                selBadge.classList.toggle('hidden', !preferSelected);
                selBadge.classList.toggle('inline-flex', preferSelected);
            }

            if (preferSelected) {
                selRadio.checked = true;
            }
        } else {
            if (selHelper) {
                selHelper.textContent = 'Seleccioná productos en la tabla para habilitar esta opción.';
            }
            if (summary) {
                summary.classList.add('hidden');
                summary.classList.remove('flex');
            }
            if (selBadge) {
                selBadge.classList.add('hidden');
                selBadge.classList.remove('inline-flex');
            }

            if (selRadio.checked) {
                document.querySelectorAll('input[name="precio-scope"]').forEach(function (radio) {
                    radio.checked = radio.value === 'todos';
                });
            }

            selRadio.disabled = true;
            selRadio.checked = false;
        }

        updateScopeCards();
    }

    // ─── Tipo Cambio Toggle ────────────────────────────────────
    function setTipoCambio(tipo) {
        tipoCambio = tipo;
        var btnPct = document.getElementById('precio-tipo-porcentaje');
        var btnMonto = document.getElementById('precio-tipo-monto');
        var prefix = document.getElementById('precio-valor-prefix');

        if (tipo === 'porcentaje') {
            btnPct.className = 'flex-1 py-2.5 text-sm font-bold bg-primary text-white transition-colors';
            btnMonto.className = 'flex-1 py-2.5 text-sm font-bold bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 transition-colors';
            if (prefix) prefix.textContent = '%';
        } else {
            btnMonto.className = 'flex-1 py-2.5 text-sm font-bold bg-primary text-white transition-colors';
            btnPct.className = 'flex-1 py-2.5 text-sm font-bold bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 transition-colors';
            if (prefix) prefix.textContent = '$';
        }
    }

    // ─── Validation ────────────────────────────────────────────
    function validateStep1() {
        var scope = getScope();
        if (scope === 'seleccionados') {
            if (!selectedProductIds || selectedProductIds.length === 0) {
                showError('No hay productos seleccionados'); return false;
            }
        } else if (scope === 'categoria') {
            var catId = document.getElementById('precio-categoria-id').value;
            if (!catId) { showError('Seleccione una categoría'); return false; }
        } else if (scope === 'marca') {
            var marcaId = document.getElementById('precio-marca-id').value;
            if (!marcaId) { showError('Seleccione una marca'); return false; }
        }
        return true;
    }

    function validateStep2() {
        var valor = parseFloat(document.getElementById('precio-valor').value);
        if (!valor || valor <= 0) {
            showError('Ingrese un valor de ajuste mayor a 0');
            return false;
        }
        return true;
    }

    function showError(msg) {
        var summary = validationSummary();
        var text = validationText();
        if (summary && text) {
            text.textContent = msg;
            summary.classList.remove('hidden');
            summary.classList.add('flex');
        }
    }

    function hideError() {
        var summary = validationSummary();
        if (summary) {
            summary.classList.add('hidden');
            summary.classList.remove('flex');
        }
    }

    // ─── Preview (Simulation) ──────────────────────────────────
    function loadPreview() {
        var tbody = document.getElementById('precio-preview-tbody');
        var loadingEl = document.getElementById('precio-preview-loading');
        var moreEl = document.getElementById('precio-preview-more');

        tbody.innerHTML = '';
        loadingEl.classList.remove('hidden');
        if (moreEl) moreEl.classList.add('hidden');
        btnApply().disabled = true;

        var scope = getScope();
        var valor = parseFloat(document.getElementById('precio-valor').value);
        var tipoCambioStr = tipoCambio === 'porcentaje' ? 'Porcentaje' : 'MontoFijo';
        var motivo = document.getElementById('precio-motivo').value;

        var solicitud = {
            nombre: motivo + ' - ' + new Date().toLocaleDateString('es-AR'),
            tipoCambio: tipoCambioStr,
            valor: valor,
            listasIds: [],
            categoriasIds: [],
            marcasIds: [],
            productosIds: []
        };

        if (scope === 'seleccionados') {
            solicitud.productosIds = selectedProductIds.slice();
        } else if (scope === 'categoria') {
            solicitud.categoriasIds = [parseInt(document.getElementById('precio-categoria-id').value)];
        } else if (scope === 'marca') {
            solicitud.marcasIds = [parseInt(document.getElementById('precio-marca-id').value)];
        }
        // scope === 'todos' → empty arrays = all products

        var token = document.querySelector('input[name="__RequestVerificationToken"]');

        fetch('/Catalogo/SimularCambioPrecios', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token ? token.value : ''
            },
            body: JSON.stringify(solicitud)
        })
        .then(function (res) {
            if (!res.ok) return res.json().then(function (e) { throw new Error(e.error || 'Error en simulación'); });
            return res.json();
        })
        .then(function (data) {
            simulationResult = data;
            renderPreview(data);
            loadingEl.classList.add('hidden');
            btnApply().disabled = false;
        })
        .catch(function (err) {
            loadingEl.classList.add('hidden');
            showError(err.message || 'Error al simular el cambio de precios');
        });
    }

    function renderPreview(data) {
        var tbody = document.getElementById('precio-preview-tbody');
        var summaryEl = document.getElementById('precio-preview-summary');
        var detailEl = document.getElementById('precio-preview-detail');
        var moreEl = document.getElementById('precio-preview-more');
        var redondeo = document.querySelector('input[name="precio-redondeo"]:checked');
        var redondeoVal = redondeo ? redondeo.value : 'none';

        var filas = data.filas || [];

        // Summary info
        if (summaryEl) {
            summaryEl.textContent = filas.length + ' producto' + (filas.length !== 1 ? 's' : '') + ' serán afectados';
        }
        if (detailEl) {
            var tipoLabel = tipoCambio === 'porcentaje' ? data.valor + '%' : '$' + formatMoney(data.valor);
            detailEl.textContent = describeScope() + ' · ' + data.tipoCambio + ' · ' + tipoLabel + ' · ' + data.nombre;
        }

        // Render rows (max 50 in preview)
        var maxRows = 50;
        var visibleRows = filas.slice(0, maxRows);

        tbody.innerHTML = '';
        visibleRows.forEach(function (fila) {
            var precioNuevo = applyRounding(fila.precioNuevo, redondeoVal);
            var diferencia = precioNuevo - fila.precioActual;
            var isPositive = diferencia >= 0;

            var tr = document.createElement('tr');
            tr.className = 'border-b border-slate-100 dark:border-slate-800/50 hover:bg-slate-50 dark:hover:bg-white/[0.02]';
            tr.innerHTML =
                '<td class="px-4 py-3">' +
                    '<div class="font-semibold text-slate-900 dark:text-white text-sm">' + escapeHtml(fila.nombre) + '</div>' +
                    '<div class="text-xs text-slate-500 dark:text-slate-400">' + escapeHtml(fila.codigo) + '</div>' +
                '</td>' +
                '<td class="px-4 py-3 text-right text-sm text-slate-700 dark:text-slate-300 font-medium">$' + formatMoney(fila.precioActual) + '</td>' +
                '<td class="px-4 py-3 text-right">' +
                    '<span class="inline-flex items-center gap-1 text-xs font-bold ' +
                        (isPositive ? 'text-emerald-600 dark:text-emerald-400' : 'text-red-500') + '">' +
                        '<span class="material-symbols-outlined text-[14px]">' + (isPositive ? 'arrow_upward' : 'arrow_downward') + '</span>' +
                        (isPositive ? '+' : '') + '$' + formatMoney(Math.abs(diferencia)) +
                    '</span>' +
                '</td>' +
                '<td class="px-4 py-3 text-right text-sm font-bold text-slate-900 dark:text-white">$' + formatMoney(precioNuevo) + '</td>';
            tbody.appendChild(tr);
        });

        // Show "more" indicator
        if (filas.length > maxRows && moreEl) {
            moreEl.classList.remove('hidden');
            moreEl.querySelector('span').textContent = '...y ' + (filas.length - maxRows) + ' productos más';
        }

        dispatchScrollRefresh();
    }

    function applyRounding(price, mode) {
        if (mode === 'entero') return Math.round(price);
        if (mode === '99') return Math.floor(price) + 0.99;
        return price;
    }

    function describeScope() {
        var scope = getScope();

        if (scope === 'seleccionados') {
            var count = selectedScopeSnapshot.length || selectedProductIds.length;
            return 'Seleccionados (' + count + ')';
        }

        if (scope === 'categoria') {
            var categoriaSelect = document.getElementById('precio-categoria-id');
            if (categoriaSelect && categoriaSelect.selectedIndex >= 0) {
                return 'Categoría: ' + categoriaSelect.options[categoriaSelect.selectedIndex].text;
            }
        }

        if (scope === 'marca') {
            var marcaSelect = document.getElementById('precio-marca-id');
            if (marcaSelect && marcaSelect.selectedIndex >= 0) {
                return 'Marca: ' + marcaSelect.options[marcaSelect.selectedIndex].text;
            }
        }

        return 'Todos los productos';
    }

    // ─── Apply ─────────────────────────────────────────────────
    function apply() {
        if (!simulationResult || !simulationResult.batchId) {
            showError('No hay simulación disponible. Vuelva a generar la vista previa.');
            return;
        }

        hideError();
        var applyBtn = btnApply();
        applyBtn.disabled = true;
        applyBtn.innerHTML = '<div class="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin"></div> Aplicando...';

        var token = document.querySelector('input[name="__RequestVerificationToken"]');

        var solicitud = {
            batchId: simulationResult.batchId,
            rowVersion: simulationResult.rowVersion || ''
        };

        fetch('/Catalogo/AplicarCambioPrecios', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token ? token.value : ''
            },
            body: JSON.stringify(solicitud)
        })
        .then(function (res) {
            if (!res.ok) return res.json().then(function (e) { throw new Error(e.error || 'Error al aplicar'); });
            return res.json();
        })
        .then(function (data) {
            if (data.exitoso) {
                close();
                var successMsg = data.mensaje || (data.productosActualizados + ' productos actualizados. Aplicá o refrescá los filtros para ver los precios nuevos.');
                document.dispatchEvent(new CustomEvent('catalogo:toast', {
                    detail: { message: successMsg, type: 'success' }
                }));
            } else {
                showError(data.mensaje || 'Error al aplicar el cambio');
                applyBtn.disabled = false;
                applyBtn.innerHTML = '<span class="material-symbols-outlined text-[16px]">check</span> Aplicar Aumento';
            }
        })
        .catch(function (err) {
            showError(err.message || 'Error al aplicar el cambio de precios');
            applyBtn.disabled = false;
            applyBtn.innerHTML = '<span class="material-symbols-outlined text-[16px]">check</span> Aplicar Aumento';
        });
    }

    // ─── Helpers ───────────────────────────────────────────────
    function formatMoney(val) {
        return Number(val).toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function escapeHtml(str) {
        if (!str) return '';
        var div = document.createElement('div');
        div.appendChild(document.createTextNode(str));
        return div.innerHTML;
    }

    // ─── Event Listeners ───────────────────────────────────────
    document.addEventListener('DOMContentLoaded', function () {
        // Scope radio changes
        document.querySelectorAll('input[name="precio-scope"]').forEach(function (radio) {
            radio.addEventListener('change', updateScopeCards);
        });

        // Tipo cambio toggles
        var btnPct = document.getElementById('precio-tipo-porcentaje');
        var btnMonto = document.getElementById('precio-tipo-monto');
        if (btnPct) btnPct.addEventListener('click', function () { setTipoCambio('porcentaje'); });
        if (btnMonto) btnMonto.addEventListener('click', function () { setTipoCambio('monto'); });

        // ESC key
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && !modal().classList.contains('hidden')) {
                close();
            }
        });

        // Initialize scope card visuals
        updateScopeCards();
    });

    return { open: open, openWithSelection: openWithSelection, close: close, nextStep: nextStep, prevStep: prevStep, apply: apply };
})();

if (typeof CatalogoModule !== 'undefined' && typeof CatalogoModule.registerModalApi === 'function') {
    CatalogoModule.registerModalApi('precio', PrecioModal);
}
