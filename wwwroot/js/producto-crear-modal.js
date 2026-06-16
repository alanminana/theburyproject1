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
    let _openTrigger = null;

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
    function open(trigger) {
        _openTrigger = (trigger instanceof Element) ? trigger : null;
        const modal = el('modal-nuevo-producto');
        modal.classList.remove('hidden');
        modal.classList.add('flex');
        document.body.style.overflow = 'hidden';
        updateCaracteristicasUi();
        setTimeout(function () {
            var firstInput = document.querySelector('#form-nuevo-producto input[name="Codigo"]');
            if (firstInput) firstInput.focus();
        }, 50);
    }

    function close() {
        const trigger = _openTrigger;
        _openTrigger = null;
        const modal = el('modal-nuevo-producto');
        modal.classList.add('hidden');
        modal.classList.remove('flex');
        document.body.style.overflow = '';
        resetForm();
        if (trigger) trigger.focus();
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
        const precioVenta  = el('modal-precioVenta');
        const alicuotaSel  = el('modal-alicuotaIVAId');
        const ivaSelect    = el('modal-porcentajeIVA');

        const calc = () => {
            const venta = parseFloat(precioVenta?.value) || 0;
            const iva = parseFloat(ivaSelect?.value) || 0;
            const final_ = venta * (1 + iva / 100);
            const precioFinal = el('modal-precioFinal');
            if (precioFinal) precioFinal.value = final_.toFixed(2);
        };

        if (alicuotaSel) {
            alicuotaSel.addEventListener('change', function () {
                if (this.value) {
                    const opt = this.options[this.selectedIndex];
                    const pct = opt && opt.getAttribute('data-porcentaje');
                    if (pct !== null && pct !== '' && ivaSelect) ivaSelect.value = String(pct);
                }
                calc();
            });
        }

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

    // ── Inserción en DOM tras alta exitosa ──────────────────
    function mkEl(tag, cls) {
        var el = document.createElement(tag);
        if (cls) el.className = cls;
        return el;
    }

    function onProductoCreado(entity) {
        if (!entity) return;

        var tbody = document.getElementById('productos-tbody');
        if (!tbody) return;

        var emptyRow = tbody.querySelector('tr td[colspan]');
        if (emptyRow) emptyRow.closest('tr').remove();

        var precio = parseFloat(entity.precioVenta || 0)
            .toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

        var searchData = [
            entity.codigo || '', entity.nombre || '',
            entity.descripcion || '', entity.categoriaNombre || '', entity.marcaNombre || ''
        ].join(' ').toLowerCase().trim();

        var tr = document.createElement('tr');
        tr.className = 'hover:bg-white/5 transition-colors';
        tr.setAttribute('data-catalogo-producto-id', entity.id);
        tr.setAttribute('data-producto-codigo', entity.codigo || '');
        tr.setAttribute('data-producto-nombre', entity.nombre || '');
        tr.setAttribute('data-search', searchData);
        tr.setAttribute('data-sort-nombre', (entity.nombre || '').toLowerCase());
        tr.setAttribute('data-sort-precio', String(parseFloat(entity.precioVenta || 0)));
        tr.setAttribute('data-sort-comision', '0');

        // td: checkbox
        var tdChk = mkEl('td', 'w-10 px-3 py-4');
        var chk = document.createElement('input');
        chk.type = 'checkbox';
        chk.className = 'chk-producto rounded border-slate-600 text-primary focus:ring-primary cursor-pointer';
        chk.value = entity.id;
        chk.setAttribute('aria-label', 'Seleccionar ' + (entity.nombre || ''));
        tdChk.appendChild(chk);

        // td: destacado (estrella) — producto nuevo nunca destacado
        var tdStar = mkEl('td', 'w-10 px-3 py-4 text-center');
        var starBtn = mkEl('button', 'btn-star-destacado inline-flex items-center justify-center w-8 h-8 rounded transition-colors hover:bg-white/5');
        starBtn.type = 'button';
        starBtn.setAttribute('data-producto-id', entity.id);
        starBtn.setAttribute('data-es-destacado', 'false');
        starBtn.setAttribute('aria-label', 'Marcar como destacado ' + (entity.nombre || ''));
        starBtn.title = 'Marcar como destacado';
        var starIcon = mkEl('span', 'material-symbols-outlined text-xl leading-none text-slate-600');
        starIcon.style.fontVariationSettings = "'FILL' 0, 'wght' 400, 'GRAD' 0, 'opsz' 24";
        starIcon.textContent = 'star';
        starBtn.appendChild(starIcon);
        tdStar.appendChild(starBtn);

        // td: codigo
        var tdCodigo = mkEl('td', 'px-6 py-4 text-sm font-mono text-slate-400');
        tdCodigo.textContent = entity.codigo || '';

        // td: nombre + descripcion
        var tdNombre = mkEl('td', 'px-6 py-4');
        var divFlex = mkEl('div', 'flex items-center gap-3');
        var divIcon = mkEl('div', 'w-10 h-10 rounded bg-slate-800 flex items-center justify-center overflow-hidden flex-shrink-0');
        var iconSpan = mkEl('span', 'material-symbols-outlined text-slate-400 text-lg');
        iconSpan.textContent = 'package_2';
        divIcon.appendChild(iconSpan);
        var divText = mkEl('div');
        var spanNombre = mkEl('span', 'text-sm font-semibold text-white');
        spanNombre.textContent = entity.nombre || '';
        divText.appendChild(spanNombre);
        if (entity.descripcion) {
            var pDesc = mkEl('p', 'text-[10px] text-slate-400 truncate max-w-[200px]');
            pDesc.textContent = entity.descripcion;
            divText.appendChild(pDesc);
        }
        divFlex.appendChild(divIcon);
        divFlex.appendChild(divText);
        tdNombre.appendChild(divFlex);

        // td: categoria
        var tdCat = mkEl('td', 'px-6 py-4 text-sm text-slate-300');
        tdCat.textContent = entity.categoriaNombre || '—';

        // td: marca
        var tdMarca = mkEl('td', 'px-6 py-4 text-sm text-slate-300');
        tdMarca.textContent = entity.marcaNombre || '—';

        // td: stock (siempre Agotado para un producto nuevo)
        var tdStock = mkEl('td', 'px-6 py-4 text-center');
        var spanStock = mkEl('span', 'inline-flex items-center px-2 py-1 rounded-full text-[10px] font-bold uppercase tracking-wider bg-red-500/20 text-red-400 border border-red-500/30');
        spanStock.textContent = 'Agotado';
        tdStock.appendChild(spanStock);

        // td: precio
        var tdPrecio = mkEl('td', 'px-6 py-4');
        var pPrecio = mkEl('p', 'text-sm font-bold text-white');
        pPrecio.textContent = '$ ' + precio;
        var pPrecioLabel = mkEl('p', 'text-[10px] text-slate-400');
        pPrecioLabel.setAttribute('data-prod-precio-label', '');
        pPrecioLabel.textContent = 'Base/fallback';
        tdPrecio.appendChild(pPrecio);
        tdPrecio.appendChild(pPrecioLabel);

        // td: comisión (producto nuevo: sin comisión)
        var tdComision = mkEl('td', 'px-6 py-4 text-sm text-right');
        var spanComision = mkEl('span', 'text-slate-500');
        spanComision.setAttribute('data-producto-comision', '');
        spanComision.textContent = '—';
        tdComision.appendChild(spanComision);

        // td: acciones — paridad estructural con el row del servidor
        var tdAcc = mkEl('td', 'px-6 py-4 text-right');
        tdAcc.style.minWidth = '240px';
        var divBtns = mkEl('div', 'flex flex-wrap justify-end gap-2');

        // Crea un botón/enlace de acción. Los .row-action ocultan su label en desktop
        // via .row-action__label; Editar/Eliminar lo muestran siempre (plainLabel).
        function mkAction(opts) {
            var tag = opts.tag || 'button';
            var node = mkEl(tag, opts.cls);
            if (tag === 'button') node.type = 'button';
            if (opts.href) node.setAttribute('href', opts.href);
            if (opts.title) node.title = opts.title;
            var attrs = opts.attrs || {};
            for (var k in attrs) node.setAttribute(k, attrs[k]);
            var ico = mkEl('span', opts.iconCls || 'material-symbols-outlined');
            ico.textContent = opts.icon;
            node.appendChild(ico);
            if (opts.label) {
                var lbl = mkEl('span', opts.plainLabel ? null : 'row-action__label');
                lbl.textContent = opts.label;
                node.appendChild(lbl);
            }
            return node;
        }

        divBtns.appendChild(mkAction({
            cls: 'row-action row-action--primary', title: 'Historial de precio',
            attrs: { 'data-catalogo-modal-open': 'historial-precio', 'data-catalogo-producto-id': entity.id },
            icon: 'history', label: 'Historial'
        }));
        divBtns.appendChild(mkAction({
            cls: 'row-action row-action--primary', title: 'Comisión vendedor: 0.00%',
            attrs: { 'data-comision-producto-id': entity.id, 'data-comision-producto-nombre': entity.nombre || '', 'data-comision-porcentaje': '0' },
            icon: 'percent', label: 'Comisión'
        }));
        divBtns.appendChild(mkAction({
            cls: 'row-action row-action--primary', title: 'Historial de movimientos de stock',
            attrs: { 'data-movimientos-producto-id': entity.id, 'data-movimientos-producto-nombre': entity.nombre || '' },
            icon: 'swap_vert', label: 'Movimientos'
        }));
        divBtns.appendChild(mkAction({
            tag: 'a', cls: 'row-action row-action--primary', title: 'Ficha de inventario del producto',
            href: '/Producto/Inventario?productoId=' + encodeURIComponent(entity.id),
            icon: 'inventory_2', label: 'Ver inventario'
        }));
        divBtns.appendChild(mkAction({
            cls: 'inline-flex items-center gap-1.5 rounded-lg border border-slate-700 px-2.5 py-1.5 text-xs font-semibold text-slate-300 transition-colors hover:border-slate-600 hover:bg-slate-800 hover:text-white',
            title: 'Editar', attrs: { 'data-prod-edit-id': entity.id },
            icon: 'edit', iconCls: 'material-symbols-outlined text-base', label: 'Editar', plainLabel: true
        }));
        divBtns.appendChild(mkAction({
            cls: 'inline-flex items-center gap-1.5 rounded-lg border border-red-500/30 px-2.5 py-1.5 text-xs font-semibold text-red-400 transition-colors hover:bg-red-500/10 hover:text-red-400',
            title: 'Eliminar', attrs: { 'data-prod-delete-id': entity.id, 'data-prod-delete-nombre': entity.nombre || '' },
            icon: 'delete', iconCls: 'material-symbols-outlined text-base', label: 'Eliminar', plainLabel: true
        }));

        tdAcc.appendChild(divBtns);

        tr.appendChild(tdChk);
        tr.appendChild(tdStar);
        tr.appendChild(tdCodigo);
        tr.appendChild(tdNombre);
        tr.appendChild(tdCat);
        tr.appendChild(tdMarca);
        tr.appendChild(tdStock);
        tr.appendChild(tdPrecio);
        tr.appendChild(tdComision);
        tr.appendChild(tdAcc);
        tbody.appendChild(tr);

        var countBadge  = document.getElementById('productos-visible-count');
        var footerCount = document.getElementById('productos-footer-count');
        var footerTotal = document.getElementById('productos-footer-total');
        if (countBadge)  countBadge.textContent  = parseInt(countBadge.textContent  || '0', 10) + 1;
        if (footerCount) footerCount.textContent = parseInt(footerCount.textContent || '0', 10) + 1;
        if (footerTotal) footerTotal.textContent = parseInt(footerTotal.textContent || '0', 10) + 1;

        if (typeof CatalogoModule !== 'undefined') {
            var selApi = CatalogoModule.getProductSelectionApi();
            if (selApi && typeof selApi.refreshUi === 'function') selApi.refreshUi();
            CatalogoModule.requestScrollRefresh();
        }
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
                    onProductoCreado(result.entity);
                    document.dispatchEvent(new CustomEvent('catalogo:toast', {
                        detail: { message: 'Producto creado exitosamente.', type: 'success' }
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
            const modal = el('modal-nuevo-producto');
            if (!modal || modal.classList.contains('hidden')) return;
            if (e.key === 'Escape') { close(); return; }
            if (e.key === 'Tab' && window.CatalogoModule) window.CatalogoModule.trapFocus(modal, e);
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
