/**
 * producto-crear-modal.js
 * Lógica del modal "Nuevo Producto" en la vista Catálogo Index_tw.
 * Maneja: apertura/cierre, dropdowns cascada, cálculo IVA,
 * características dinámicas y envío AJAX.
 */
const ProductoModal = (() => {
    // DOM refs (lazy)
    const el = (id) => document.getElementById(id);
    const catalogoModule = typeof CatalogoModule !== 'undefined' ? CatalogoModule : null;

    let caracteristicaIndex = 0;
    let _tabs = null;

    function dispatchScrollRefresh() {
        if (catalogoModule && typeof catalogoModule.requestScrollRefresh === 'function') {
            catalogoModule.requestScrollRefresh();
            return;
        }

        document.dispatchEvent(new CustomEvent('catalogo:refresh-scroll'));
    }

    function updateCaracteristicasUi() {
        const count = el('modal-caracteristicas-count');
        const total = el('modal-caracteristicas-body')?.querySelectorAll('tr').length || 0;
        if (count) {
            count.textContent = `${total} cargadas`;
        }
        dispatchScrollRefresh();
    }

    // ── Solapas ───────────────────────────────────────────────
    function initTabs() {
        _tabs = window.ProductoModalTabs.wire({
            root: el('form-nuevo-producto'),
            tabAttr: 'data-prod-create-tab',
            panelAttr: 'data-prod-create-panel',
            idPrefix: 'prod-create',
            onActivate: function (name) {
                if (name === 'caracteristicas') dispatchScrollRefresh();
            }
        });
    }

    // ── Abrir / Cerrar ──────────────────────────────────────
    var _panel = null;
    function panel() {
        if (!_panel) {
            _panel = window.InvUI.panelFormulario(el('modal-nuevo-producto'), {
                cierres: '[data-catalogo-modal-close="producto"]',
                cuentaComoCambio: '[data-catalogo-action="product-add-caracteristica"], [data-catalogo-action="product-remove-caracteristica"]',
                alCerrar: resetForm
            });
        }
        return _panel;
    }

    function open() {
        resetForm();
        panel().abrir();
        if (_tabs) _tabs.activate('datos');
        updateCaracteristicasUi();
        cargarCreditoPersonalCandidatos();
        const firstInput = document.querySelector('#form-nuevo-producto input[name="Codigo"]');
        if (firstInput) firstInput.focus();
    }

    // ── Crédito personal ─────────────────────────────────────
    var CP_PREVIEW_URL = '/Producto/PreviewRecargoCreditoPersonal';

    function creditoPersonalElementos() {
        return {
            admiteHidden: document.querySelector('#form-nuevo-producto [data-cp-admite-hidden]'),
            cardsWrap: document.querySelector('#form-nuevo-producto [data-cp-cards-wrap]'),
            cardsCont: el('modal-credito-cards'),
            maxCuotasWrap: document.querySelector('#form-nuevo-producto [data-cp-maxcuotas-wrap]')
        };
    }

    function initCreditoPersonalModo() {
        var radios = document.querySelectorAll('#form-nuevo-producto [data-cp-modo]');
        if (radios.length && window.ProductoCreditoPersonalUI) window.ProductoCreditoPersonalUI.wireModo(radios, creditoPersonalElementos());
    }

    function resetCreditoPersonalModo() {
        var radios = document.querySelectorAll('#form-nuevo-producto [data-cp-modo]');
        radios.forEach(function (r) { r.checked = (r.value === 'HeredaGlobal'); });
        if (window.ProductoCreditoPersonalUI) window.ProductoCreditoPersonalUI.aplicarModo('HeredaGlobal', creditoPersonalElementos());
    }

    function cargarCreditoPersonalCandidatos() {
        var cont = el('modal-credito-cards');
        if (!cont || !window.ProductoCreditoPersonalUI) return;
        resetCreditoPersonalModo();
        fetch('/Producto/CreditoPersonalCandidatosJson', { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) { return r.ok ? r.json() : { cuotas: [] }; })
            .then(function (data) {
                window.ProductoCreditoPersonalUI.renderCards(cont, data.cuotas || [], 'CreditoPersonal.Cuotas', CP_PREVIEW_URL);
            })
            .catch(function () { window.ProductoCreditoPersonalUI.renderCards(cont, [], 'CreditoPersonal.Cuotas', CP_PREVIEW_URL); });
    }

    // El navegador devuelve el foco al disparador al cerrar el <dialog>.
    function close() { panel().cerrar(); }

    function resetForm() {
        const form = el('form-nuevo-producto');
        if (form) form.reset();

        // Limpiar autocompletes
        const catInput = el('ac-categoria-input');
        const catClear = el('ac-categoria-clear');
        if (catInput) catInput.value = '';
        if (catClear) catClear.classList.add('hidden');

        const marcaInput = el('ac-marca-input');
        const marcaClear = el('ac-marca-clear');
        if (marcaInput) marcaInput.value = '';
        if (marcaClear) marcaClear.classList.add('hidden');

        // Limpiar subcategorías / submarcas
        resetSelect(el('modal-subcategoriaId'), 'Seleccionar subcategoría');
        resetSelect(el('modal-submarcaId'), 'Seleccionar submarca');

        // Limpiar tabla de características
        const tbody = el('modal-caracteristicas-body');
        if (tbody) tbody.innerHTML = '';
        caracteristicaIndex = 0;
        updateCaracteristicasUi();

        // Restablecer precio final
        const precioFinal = el('modal-precioFinal');
        if (precioFinal) precioFinal.value = '0.00';

        resetCreditoPersonalModo();

        if (_tabs) _tabs.activate('datos');

        // Ocultar errores
        hideValidation();
    }

    function resetSelect(selectEl, placeholder) {
        if (!selectEl) return;
        selectEl.innerHTML = `<option value="">${placeholder}</option>`;
    }

    // ── Autocomplete widget ─────────────────────────────────
    /**
     * Mounts a search-as-you-type autocomplete on a pre-built HTML structure.
     * @param {object} opts
     *   inputId       – text input id
     *   hiddenId      – hidden input id (carries the selected value for form submission)
     *   dropdownId    – dropdown container id
     *   clearBtnId    – clear/X button id
     *   items         – array of { id, nombre }
     *   onSelect      – callback(id) fired when a value is chosen or cleared
     */
    function initAutocomplete(opts) {
        const input    = el(opts.inputId);
        const hidden   = el(opts.hiddenId);
        const dropdown = el(opts.dropdownId);
        const clearBtn = el(opts.clearBtnId);
        if (!input || !hidden || !dropdown) return;

        let activeIndex = -1;
        let selectedLabel = '';

        function openDropdown(filtered) {
            dropdown.innerHTML = '';
            activeIndex = -1;

            if (!filtered.length) {
                const empty = document.createElement('div');
                empty.className = 'px-4 py-3 text-sm text-slate-400';
                empty.textContent = 'Sin resultados';
                dropdown.appendChild(empty);
            } else {
                filtered.forEach((item, i) => {
                    const row = document.createElement('button');
                    row.type = 'button';
                    row.className = 'w-full text-left px-4 py-2.5 text-sm text-slate-200 hover:bg-primary/20 hover:text-white transition-colors focus:outline-none focus:bg-primary/20';
                    row.dataset.acValue = item.id;
                    row.dataset.acLabel = item.nombre;
                    row.textContent = item.nombre;
                    row.addEventListener('mousedown', (e) => {
                        e.preventDefault(); // avoid blur before click
                        selectItem(item.id, item.nombre);
                    });
                    dropdown.appendChild(row);
                });
            }
            dropdown.classList.remove('hidden');
        }

        function closeDropdown() {
            dropdown.classList.add('hidden');
            activeIndex = -1;
        }

        function selectItem(id, label) {
            input.value = label;
            hidden.value = id;
            selectedLabel = label;
            if (clearBtn) clearBtn.classList.remove('hidden');
            closeDropdown();
            // fire change so cascading dropdowns react
            hidden.dispatchEvent(new Event('change', { bubbles: true }));
        }

        function clearSelection() {
            input.value = '';
            hidden.value = '';
            selectedLabel = '';
            if (clearBtn) clearBtn.classList.add('hidden');
            hidden.dispatchEvent(new Event('change', { bubbles: true }));
        }

        function filter(query) {
            if (!query.trim()) return opts.items.slice(0, 50);
            const q = query.toLowerCase();
            return opts.items.filter(i => i.nombre.toLowerCase().includes(q)).slice(0, 50);
        }

        input.addEventListener('input', () => {
            const q = input.value;
            if (!q) {
                hidden.value = '';
                if (clearBtn) clearBtn.classList.add('hidden');
            }
            openDropdown(filter(q));
        });

        input.addEventListener('focus', () => {
            openDropdown(filter(input.value));
        });

        input.addEventListener('blur', () => {
            // Small delay so mousedown on dropdown fires first
            setTimeout(() => {
                closeDropdown();
                if (hidden.value) {
                    // If the text was edited after selection without picking a new item, the
                    // hidden ID is stale — clear it so the form doesn't submit a wrong value.
                    if (input.value !== selectedLabel) {
                        clearSelection();
                    }
                } else {
                    // No active selection — clear any leftover text
                    input.value = '';
                    if (clearBtn) clearBtn.classList.add('hidden');
                }
            }, 150);
        });

        input.addEventListener('keydown', (e) => {
            const rows = dropdown.querySelectorAll('button[data-ac-value]');
            if (!rows.length) return;

            if (e.key === 'ArrowDown') {
                e.preventDefault();
                activeIndex = Math.min(activeIndex + 1, rows.length - 1);
                rows.forEach((r, i) => r.classList.toggle('bg-primary/20', i === activeIndex));
                rows[activeIndex]?.scrollIntoView({ block: 'nearest' });
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                activeIndex = Math.max(activeIndex - 1, 0);
                rows.forEach((r, i) => r.classList.toggle('bg-primary/20', i === activeIndex));
                rows[activeIndex]?.scrollIntoView({ block: 'nearest' });
            } else if (e.key === 'Enter') {
                e.preventDefault();
                if (activeIndex >= 0 && rows[activeIndex]) {
                    const r = rows[activeIndex];
                    selectItem(r.dataset.acValue, r.dataset.acLabel);
                }
            } else if (e.key === 'Escape') {
                // Cierra solo la lista de sugerencias, no el panel.
                e.preventDefault();
                closeDropdown();
            }
        });

        if (clearBtn) {
            clearBtn.addEventListener('click', clearSelection);
        }

        // Position dropdown relative to its container
        const wrapper = dropdown.closest('.autocomplete-erp');
        if (wrapper) wrapper.style.position = 'relative';

        if (opts.onSelect) {
            hidden.addEventListener('change', () => opts.onSelect(hidden.value));
        }
    }

    // ── Dropdowns en cascada ────────────────────────────────
    function initCascadingDropdowns() {
        const catData   = (window.CatalogoData && window.CatalogoData.categorias) || [];
        const marcaData = (window.CatalogoData && window.CatalogoData.marcas)     || [];

        initAutocomplete({
            inputId:    'ac-categoria-input',
            hiddenId:   'modal-categoriaId',
            dropdownId: 'ac-categoria-dropdown',
            clearBtnId: 'ac-categoria-clear',
            items:      catData,
            onSelect: async (catId) => {
                const subSelect = el('modal-subcategoriaId');
                resetSelect(subSelect, 'Seleccionar subcategoría');
                if (!catId) return;
                try {
                    const resp = await fetch(`/Producto/GetSubcategorias/${encodeURIComponent(catId)}`);
                    if (!resp.ok) return;
                    const items = await resp.json();
                    items.forEach(item => {
                        const opt = document.createElement('option');
                        opt.value = item.id;
                        opt.textContent = item.nombre;
                        subSelect.appendChild(opt);
                    });
                } catch { /* silent */ }
            }
        });

        initAutocomplete({
            inputId:    'ac-marca-input',
            hiddenId:   'modal-marcaId',
            dropdownId: 'ac-marca-dropdown',
            clearBtnId: 'ac-marca-clear',
            items:      marcaData,
            onSelect: async (marcaId) => {
                const subSelect = el('modal-submarcaId');
                resetSelect(subSelect, 'Seleccionar submarca');
                if (!marcaId) return;
                try {
                    const resp = await fetch(`/Producto/GetSubmarcas/${encodeURIComponent(marcaId)}`);
                    if (!resp.ok) return;
                    const items = await resp.json();
                    items.forEach(item => {
                        const opt = document.createElement('option');
                        opt.value = item.id;
                        opt.textContent = item.nombre;
                        subSelect.appendChild(opt);
                    });
                } catch { /* silent */ }
            }
        });
    }

    // El cálculo de precios (costo real + ganancia = precio final con IVA, desglose)
    // vive en producto-precio-calculo.js (bindPrecioProducto). Acá no se duplican
    // listeners de precio.

    // ── Características dinámicas ────────────────────────────
    function addCaracteristica() {
        const nombreInput = el('modal-caract-nombre');
        const valorInput = el('modal-caract-valor');
        const nombre = nombreInput?.value?.trim();
        const valor = valorInput?.value?.trim();

        if (!nombre || !valor) return;

        const tbody = el('modal-caracteristicas-body');
        const idx = caracteristicaIndex++;

        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td class="px-4 py-3">
                ${escapeHtml(nombre)}
                <input type="hidden" name="Caracteristicas[${idx}].Nombre" value="${escapeAttr(nombre)}" />
            </td>
            <td class="px-4 py-3">
                ${escapeHtml(valor)}
                <input type="hidden" name="Caracteristicas[${idx}].Valor" value="${escapeAttr(valor)}" />
            </td>
            <td class="px-4 py-3 text-center">
                <button type="button" data-catalogo-action="product-remove-caracteristica"
                        class="text-red-500 hover:text-red-600 transition-colors">
                    <span class="material-symbols-outlined">delete</span>
                </button>
            </td>`;
        tbody.appendChild(tr);

        nombreInput.value = '';
        valorInput.value = '';
        nombreInput.focus();
        updateCaracteristicasUi();
    }

    function removeCaracteristica(btn) {
        const tr = btn.closest('tr');
        if (tr) tr.remove();
        reindexCaracteristicas();
    }

    function reindexCaracteristicas() {
        const tbody = el('modal-caracteristicas-body');
        if (!tbody) return;
        const rows = tbody.querySelectorAll('tr');
        rows.forEach((row, i) => {
            const inputs = row.querySelectorAll('input[type="hidden"]');
            inputs.forEach(inp => {
                inp.name = inp.name.replace(/\[\d+\]/, `[${i}]`);
            });
        });
        caracteristicaIndex = rows.length;
        updateCaracteristicasUi();
    }

    // ── Inserción en DOM tras alta exitosa ──────────────────
    function mkEl(tag, cls) {
        var el = document.createElement(tag);
        if (cls) el.className = cls;
        return el;
    }

    // La fila se renderiza en el servidor: se vuelve a pedir el listado y se resalta el alta si quedó en la página.
    function onProductoCreado(entity) {
        if (!entity || !window.InvProductos) return;
        window.InvProductos.refrescar({ resaltar: entity.id });
    }

    // ── Envío AJAX ──────────────────────────────────────────
    function initSubmit() {
        const form = el('form-nuevo-producto');
        if (!form) return;

        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            hideValidation();
            clearFieldErrors();

            // Validación básica client-side
            const errors = validateForm(form);
            if (Object.keys(errors).length > 0) {
                handleServerErrors(errors);
                return;
            }

            const btn = el('btn-guardar-producto');
            const origText = btn.textContent;
            btn.disabled = true;
            btn.textContent = 'Guardando...';

            try {
                const formData = new FormData(form);
                const resp = await fetch(form.action, {
                    method: 'POST',
                    body: formData,
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });

                const result = await resp.json();

                if (result.success) {
                    close();
                    onProductoCreado(result.entity);
                    document.dispatchEvent(new CustomEvent('catalogo:toast', {
                        detail: { message: 'Producto creado exitosamente.', type: 'success' }
                    }));
                    // B8: avisos no bloqueantes del servidor (p. ej. precio de venta por debajo del costo).
                    (result.advertencias || []).forEach(function (msg) {
                        document.dispatchEvent(new CustomEvent('catalogo:toast', {
                            detail: { message: msg, type: 'warning' }
                        }));
                    });
                } else if (result.errors) {
                    handleServerErrors(result.errors);
                }
            } catch {
                showValidation('Error de conexión. Intente nuevamente.');
            } finally {
                btn.disabled = false;
                btn.textContent = origText;
            }
        });
    }

    function validateForm(form) {
        const errors = {};
        const fd = new FormData(form);

        if (!fd.get('Codigo')?.trim()) errors.Codigo = ['El código es obligatorio'];
        if (!fd.get('Nombre')?.trim()) errors.Nombre = ['El nombre es obligatorio'];
        if (!fd.get('CategoriaId')) errors.CategoriaId = ['La categoría es obligatoria'];
        if (!fd.get('MarcaId')) errors.MarcaId = ['La marca es obligatoria'];

        const costo = parseFloat(fd.get('PrecioCompra'));
        if (isNaN(costo) || costo < 0) errors.PrecioCompra = ['El precio de costo es obligatorio'];

        const venta = parseFloat(fd.get('PrecioVenta'));
        if (isNaN(venta) || venta < 0) errors.PrecioVenta = ['El precio de venta es obligatorio'];

        return errors;
    }

    function handleServerErrors(errors) {
        const primero = ProductoModalFormUtils.handleServerErrors(errors, showValidation, null);
        jumpToFirstErrorTab(errors);
        if (primero) primero.focus();
    }

    function jumpToFirstErrorTab(errors) {
        if (!_tabs) return;
        const fields = Object.keys(errors || {});
        for (const field of fields) {
            const tabName = _tabs.findTabForField(field);
            if (tabName) { _tabs.activate(tabName); return; }
        }
    }

    // ── Validación visual ───────────────────────────────────
    // Compartido con producto-editar-modal.js vía producto-modal-validacion.js;
    // acá se preserva el comportamiento previo: requireBoth (sólo actúa si box
    // y msg existen) + scroll al summary al mostrarlo.
    const _validacion = ProductoModalFormUtils.bindValidation('modal-validation-summary', 'modal-validation-text', {
        requireBoth: true,
        scrollIntoView: true
    });
    function showValidation(text) { _validacion.showValidation(text); }
    function hideValidation() { _validacion.hideValidation(); }

    function clearFieldErrors() {
        ProductoModalFormUtils.limpiarMarcas(el('form-nuevo-producto'));
        document.querySelectorAll('#form-nuevo-producto [data-valmsg-for]').forEach(span => {
            span.textContent = '';
            span.classList.add('hidden');
        });
        document.querySelectorAll('#form-nuevo-producto .border-red-500').forEach(input => {
            input.classList.remove('border-red-500');
        });
    }

    // ── Escape helpers ──────────────────────────────────────
    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    function escapeAttr(str) {
        return str.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    // ── Enter en características ────────────────────────────
    // Enter agrega la característica; nunca envía ni valida el formulario completo.
    function initEnterCaracteristicas() {
        ['modal-caract-nombre', 'modal-caract-valor'].forEach(function (id) {
            const campo = el(id);
            if (!campo) return;
            campo.addEventListener('keydown', function (e) {
                if (e.key !== 'Enter') return;
                e.preventDefault();
                addCaracteristica();
            });
        });
    }

    // ── Init ────────────────────────────────────────────────
    function init() {
        initTabs();
        initCascadingDropdowns();
        initSubmit();
        initEnterCaracteristicas();
        panel();
        initCreditoPersonalModo();
        updateCaracteristicasUi();
        const tbody = el('modal-caracteristicas-body');
        if (tbody) {
            tbody.addEventListener('click', (e) => {
                const removeBtn = e.target.closest('[data-catalogo-action="product-remove-caracteristica"]');
                if (!removeBtn) return;
                e.preventDefault();
                removeCaracteristica(removeBtn);
            });
        }
    }

    document.addEventListener('DOMContentLoaded', init);

    return { open, close, addCaracteristica, removeCaracteristica };
})();

if (typeof CatalogoModule !== 'undefined' && typeof CatalogoModule.registerModalApi === 'function') {
    CatalogoModule.registerModalApi('producto', ProductoModal);
}
