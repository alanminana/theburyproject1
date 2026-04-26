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

    // ── Abrir / Cerrar ──────────────────────────────────────
    function open() {
        const modal = el('modal-nuevo-producto');
        modal.classList.remove('hidden');
        modal.classList.add('flex');
        document.body.style.overflow = 'hidden';
        updateCaracteristicasUi();
    }

    function close() {
        const modal = el('modal-nuevo-producto');
        modal.classList.add('hidden');
        modal.classList.remove('flex');
        document.body.style.overflow = '';
        resetForm();
    }

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
                    const resp = await fetch(`/Producto/GetSubcategorias?categoriaId=${encodeURIComponent(catId)}`);
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
                    const resp = await fetch(`/Producto/GetSubmarcas?marcaId=${encodeURIComponent(marcaId)}`);
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

    // ── Cálculo IVA ─────────────────────────────────────────
    function initPrecioCalc() {
        const precioVenta = el('modal-precioVenta');
        const ivaSelect = el('modal-porcentajeIVA');

        const calc = () => {
            const venta = parseFloat(precioVenta?.value) || 0;
            const iva = parseFloat(ivaSelect?.value) || 0;
            const final_ = venta * (1 + iva / 100);
            const precioFinal = el('modal-precioFinal');
            if (precioFinal) precioFinal.value = final_.toFixed(2);
        };

        if (precioVenta) precioVenta.addEventListener('input', calc);
        if (ivaSelect) ivaSelect.addEventListener('change', calc);
    }

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
            if (errors.length > 0) {
                showValidation(errors.join('. '));
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
                    document.dispatchEvent(new CustomEvent('catalogo:toast', {
                        detail: { message: 'Producto creado exitosamente. Aplicá o refrescá los filtros para verlo en la lista.', type: 'success' }
                    }));
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
        const errors = [];
        const fd = new FormData(form);

        if (!fd.get('Codigo')?.trim()) errors.push('El código es obligatorio');
        if (!fd.get('Nombre')?.trim()) errors.push('El nombre es obligatorio');
        if (!fd.get('CategoriaId')) errors.push('La categoría es obligatoria');
        if (!fd.get('MarcaId')) errors.push('La marca es obligatoria');

        const costo = parseFloat(fd.get('PrecioCompra'));
        if (isNaN(costo) || costo < 0) errors.push('El precio de costo es obligatorio');

        const venta = parseFloat(fd.get('PrecioVenta'));
        if (isNaN(venta) || venta < 0) errors.push('El precio de venta es obligatorio');

        return errors;
    }

    function handleServerErrors(errors) {
        const messages = [];
        for (const [field, msgs] of Object.entries(errors)) {
            msgs.forEach(msg => messages.push(msg));

            // Marcar campo con error
            if (field) {
                const span = document.querySelector(`[data-valmsg-for="${field}"]`);
                if (span) {
                    span.textContent = msgs[0];
                    span.classList.remove('hidden');
                }
                // Borde rojo en el input
                const input = document.querySelector(`[name="${field}"]`);
                if (input) input.classList.add('border-red-500');
            }
        }
        if (messages.length) showValidation(messages.join('. '));
    }

    // ── Validación visual ───────────────────────────────────
    function showValidation(text) {
        const box = el('modal-validation-summary');
        const msg = el('modal-validation-text');
        if (box && msg) {
            msg.textContent = text;
            box.classList.remove('hidden');
            box.classList.add('flex');
            box.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    }

    function hideValidation() {
        const box = el('modal-validation-summary');
        if (box) {
            box.classList.add('hidden');
            box.classList.remove('flex');
        }
    }

    function clearFieldErrors() {
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

    // ── Esc para cerrar ─────────────────────────────────────
    function initEscKey() {
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                const modal = el('modal-nuevo-producto');
                if (modal && !modal.classList.contains('hidden')) {
                    close();
                }
            }
        });
    }

    // ── Init ────────────────────────────────────────────────
    function init() {
        initCascadingDropdowns();
        initPrecioCalc();
        initSubmit();
        initEscKey();
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
