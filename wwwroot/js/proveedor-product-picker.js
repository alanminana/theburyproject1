/**
 * proveedor-product-picker.js
 *
 * Autocomplete + chip picker para asociar productos a un proveedor.
 *
 * Arquitectura:
 *  - El dropdown se mueve al <body> como portal (escapa del overflow: auto del modal)
 *  - Posicionamiento via position:fixed calculado desde getBoundingClientRect
 *  - Model binding via hidden inputs name="ProductosSeleccionados"
 *  - API pública por instancia en containerEl._picker: { preload(ids), reset() }
 *
 * Requiere en el HTML:
 *  <script type="application/json" id="productos-picker-data">[...]</script>
 *  con objetos { id, codigo, nombre, marca, categoria, marcaId, categoriaId }
 *
 *  <div class="proveedor-product-picker" data-form-name="ProductosSeleccionados">
 *    <div class="relative">
 *      <input class="picker-search-input" ...>
 *      <div class="picker-dropdown" hidden></div>   ← JS lo mueve al body
 *    </div>
 *    <div class="picker-chips-container"></div>
 *    <p>...<span class="picker-count-number">0</span>...</p>
 *    <div class="picker-hidden-inputs"></div>
 *  </div>
 *
 * Expone: window.ProveedorProductPicker = { init }
 */
const ProveedorProductPicker = (() => {
    let allProducts = [];
    let pickerSeq = 0;

    // ─── Catálogo ───────────────────────────────────────────────────────────────
    function loadCatalog() {
        const el = document.getElementById('productos-picker-data');
        if (!el) return;
        try { allProducts = JSON.parse(el.textContent || '[]'); } catch { allProducts = []; }
    }

    const MAX_RESULTADOS = 25;

    function search(query) {
        const q = query.toLowerCase();
        return allProducts.filter(p =>
            (p.nombre || '').toLowerCase().includes(q) ||
            (p.codigo || '').toLowerCase().includes(q) ||
            (p.marca || '').toLowerCase().includes(q) ||
            (p.categoria || '').toLowerCase().includes(q)
        );
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────
    function escHtml(str) {
        const d = document.createElement('div');
        d.textContent = str || '';
        return d.innerHTML;
    }

    function productLabel(p) {
        return p.codigo ? `${p.codigo} — ${p.nombre}` : p.nombre;
    }

    // ─── Picker por instancia ────────────────────────────────────────────────────
    function initPicker(containerEl) {
        const formName = containerEl.dataset.formName || 'ProductosSeleccionados';
        const searchInput = containerEl.querySelector('.picker-search-input');
        const dropdownEl  = containerEl.querySelector('.picker-dropdown');
        const chipsEl     = containerEl.querySelector('.picker-chips-container');
        const hiddenEl    = containerEl.querySelector('.picker-hidden-inputs');
        const countEl     = containerEl.querySelector('.picker-count-number');

        if (!searchInput || !dropdownEl) return;

        // ── Portal: mover dropdown al body para escapar overflow:auto del modal ──
        // Construimos el contenido del dropdown dentro del elemento
        const resultsList = document.createElement('ul');
        resultsList.className = 'divide-y divide-slate-700/50 py-1 max-h-64 overflow-y-auto';
        dropdownEl.appendChild(resultsList);

        // Semántica de autocompletado (combobox + listbox) para lectores de pantalla y teclado.
        pickerSeq += 1;
        const listId = `picker-listbox-${pickerSeq}`;
        let activeIndex = -1;
        resultsList.id = listId;
        resultsList.setAttribute('role', 'listbox');
        searchInput.setAttribute('role', 'combobox');
        searchInput.setAttribute('aria-autocomplete', 'list');
        searchInput.setAttribute('aria-expanded', 'false');
        searchInput.setAttribute('aria-controls', listId);

        // Estilos base del portal (position:fixed, todo via style para ser explícitos)
        Object.assign(dropdownEl.style, {
            position: 'fixed',
            zIndex:   '9999',
            minWidth: '280px',
            background: 'var(--pal-neutral-925)',         // gray-900 — completamente opaco
            border: '1px solid color-mix(in srgb, var(--pal-info-500) 20%, transparent)',
            borderRadius: '0.75rem',
            boxShadow: '0 24px 64px color-mix(in srgb, var(--pal-black) 70%, transparent), 0 8px 24px color-mix(in srgb, var(--pal-black) 50%, transparent)',
            overflow: 'hidden',
            backdropFilter: 'none'           // no blur en portal — fondo es opaco
        });

        document.body.appendChild(dropdownEl);

        // ── Estado ──────────────────────────────────────────────────────────────
        let selectedIds = new Set();

        // ── Posicionamiento ──────────────────────────────────────────────────────
        function positionDropdown() {
            const rect = searchInput.getBoundingClientRect();
            dropdownEl.style.top   = (rect.bottom + 6) + 'px';
            dropdownEl.style.left  = rect.left + 'px';
            dropdownEl.style.width = rect.width + 'px';
        }

        // ── Dropdown open/close ──────────────────────────────────────────────────
        function openDropdown() {
            positionDropdown();
            dropdownEl.hidden = false;
            searchInput.setAttribute('aria-expanded', 'true');
        }

        function closeDropdown() {
            dropdownEl.hidden = true;
            resultsList.innerHTML = '';
            activeIndex = -1;
            searchInput.setAttribute('aria-expanded', 'false');
            searchInput.removeAttribute('aria-activedescendant');
        }

        // Al hacer scroll en el contenedor del drawer (el navegador lo hace solo para mostrar el input al
        // escribir) o cambiar el tamaño de la ventana, la lista sigue al input; se cierra solo si el input
        // quedó fuera de la vista. Cerrarla siempre cortaba la búsqueda a mitad de tipeo.
        function followInput() {
            if (dropdownEl.hidden) return;
            const rect = searchInput.getBoundingClientRect();
            const bounds = (modalScrollEl || document.documentElement).getBoundingClientRect();
            if (rect.bottom < bounds.top || rect.top > bounds.bottom) {
                closeDropdown();
                return;
            }
            positionDropdown();
        }

        const modalScrollEl = containerEl.closest('.overflow-y-auto');
        if (modalScrollEl) {
            modalScrollEl.addEventListener('scroll', followInput, { passive: true });
        }
        window.addEventListener('resize', followInput, { passive: true });

        // ── Sincronización de estado ─────────────────────────────────────────────
        function syncHiddenInputs() {
            hiddenEl.innerHTML = '';
            selectedIds.forEach(id => {
                const input = document.createElement('input');
                input.type  = 'hidden';
                input.name  = formName;
                input.value = id;
                hiddenEl.appendChild(input);
            });
            if (countEl) countEl.textContent = selectedIds.size;
            const labelEl = containerEl.querySelector('.picker-count-label');
            if (labelEl) labelEl.textContent = selectedIds.size === 1 ? 'producto seleccionado' : 'productos seleccionados';
        }

        function getSelectedProducts() {
            return allProducts.filter(p => selectedIds.has(p.id));
        }

        // ── Chips ────────────────────────────────────────────────────────────────
        function renderChips() {
            chipsEl.innerHTML = '';
            getSelectedProducts().forEach(p => {
                const chip = document.createElement('span');
                chip.className = 'picker-chip inline-flex items-center gap-1 rounded-md border border-slate-700 bg-slate-800/80 pl-2.5 pr-1.5 py-1 text-[11px] font-semibold text-slate-200';
                chip.innerHTML =
                    `<span class="truncate max-w-[18rem]">${escHtml(productLabel(p))}</span>` +
                    `<button type="button" class="picker-chip-remove shrink-0 flex items-center justify-center w-4 h-4 rounded-full text-slate-500 hover:text-white hover:bg-slate-700 transition-colors ml-0.5" data-id="${p.id}" aria-label="Quitar ${escHtml(p.nombre)}">` +
                    `<span class="material-symbols-outlined text-[12px]" style="font-size:12px">close</span></button>`;
                chipsEl.appendChild(chip);
            });

            chipsEl.querySelectorAll('.picker-chip-remove').forEach(btn => {
                btn.addEventListener('click', () => {
                    selectedIds.delete(parseInt(btn.dataset.id, 10));
                    renderChips();
                    syncHiddenInputs();
                });
            });
        }

        // ── Selección de producto ────────────────────────────────────────────────
        // Elegir un producto marca también su marca y su categoría (siguen siendo editables):
        // sin esto el proveedor queda con el producto "suelto" y sin esas asociaciones.
        function marcarAsociacionesDelProducto(product) {
            const form = containerEl.closest('form');
            if (!form) return;
            const agregadas = [];
            [['MarcasSeleccionadas', product.marcaId], ['CategoriasSeleccionadas', product.categoriaId]].forEach(([name, id]) => {
                if (!id) return;
                const checkbox = form.querySelector(`input[type="checkbox"][name="${name}"][value="${id}"]`);
                if (checkbox && !checkbox.checked) {
                    checkbox.checked = true;
                    checkbox.dispatchEvent(new Event('change', { bubbles: true }));
                    revelarEnLista(checkbox);
                    agregadas.push(name === 'MarcasSeleccionadas' ? `la marca ${product.marca}` : `la categoría ${product.categoria}`);
                }
            });
            mostrarAvisoAsociaciones(agregadas);
        }

        // La lista de marcas/categorías tiene scroll propio: lo recién tildado se acerca a la vista
        // (solo el scroll interno de la lista, sin mover el drawer).
        function revelarEnLista(checkbox) {
            const list = checkbox.closest('.prov-check-list');
            if (!list) return;
            const l = list.getBoundingClientRect();
            const c = checkbox.getBoundingClientRect();
            if (c.top < l.top || c.bottom > l.bottom) list.scrollTop += c.top - l.top - 8;
        }

        // Los tildes automáticos se anuncian: sin aviso el usuario no sabe que cambió algo fuera del picker.
        function mostrarAvisoAsociaciones(agregadas) {
            let note = containerEl.querySelector('.picker-auto-note');
            if (!agregadas.length) {
                if (note) note.textContent = '';
                return;
            }
            if (!note) {
                note = document.createElement('p');
                note.className = 'picker-auto-note mt-2 text-xs text-primary';
                note.setAttribute('role', 'status');
                note.setAttribute('aria-live', 'polite');
                chipsEl.insertAdjacentElement('afterend', note);
            }
            note.textContent = `También se marcó ${agregadas.join(' y ')} en las listas de abajo; podés destildar lo que no corresponda.`;
        }

        function selectProduct(product) {
            selectedIds.add(product.id);
            marcarAsociacionesDelProducto(product);
            renderChips();
            syncHiddenInputs();
            closeDropdown();
            searchInput.value = '';
            searchInput.focus();
        }

        // ── Renderizado de resultados ────────────────────────────────────────────
        function renderResults(todos) {
            resultsList.innerHTML = '';
            activeIndex = -1;
            searchInput.removeAttribute('aria-activedescendant');
            const results = todos.slice(0, MAX_RESULTADOS);

            if (results.length === 0) {
                const li = document.createElement('li');
                li.className = 'px-4 py-3 text-sm text-slate-400 italic text-center';
                li.setAttribute('role', 'presentation');
                li.textContent = 'Sin resultados para esta búsqueda';
                resultsList.appendChild(li);
                return;
            }

            results.forEach((p, idx) => {
                const isSelected = selectedIds.has(p.id);
                const li = document.createElement('li');
                li.id = `${listId}-opt-${idx}`;
                li.setAttribute('role', 'option');
                li.setAttribute('aria-selected', 'false');
                if (isSelected) li.setAttribute('aria-disabled', 'true');
                li.className = isSelected
                    ? 'flex items-center gap-3 px-3 py-2 cursor-default opacity-50'
                    : 'flex items-center gap-3 px-3 py-2 cursor-pointer hover:bg-indigo-500/10 transition-colors';

                const meta = [p.marca, p.categoria].filter(Boolean).join(' · ');

                li.innerHTML =
                    `<div class="flex-1 min-w-0">` +
                    `<p class="text-sm font-semibold ${isSelected ? 'text-slate-400' : 'text-white'} truncate">${escHtml(productLabel(p))}</p>` +
                    (meta ? `<p class="text-xs text-slate-500 truncate">${escHtml(meta)}</p>` : '') +
                    `</div>` +
                    (isSelected
                        ? `<span class="material-symbols-outlined text-primary/60 shrink-0" style="font-size:16px">check_circle</span>`
                        : `<span class="material-symbols-outlined text-slate-600 shrink-0" style="font-size:16px">add_circle</span>`);

                if (!isSelected) {
                    li.dataset.selectable = '1';
                    li._product = p;
                    li.addEventListener('click', () => selectProduct(p));
                }
                resultsList.appendChild(li);
            });

            // El corte tiene que ser visible: sin esto parece que el producto buscado no existe.
            if (todos.length > MAX_RESULTADOS) {
                const aviso = document.createElement('li');
                aviso.className = 'px-4 py-2 text-xs text-slate-400 text-center';
                aviso.setAttribute('role', 'presentation');
                aviso.textContent = `Mostrando ${MAX_RESULTADOS} de ${todos.length}. Escribí más para acotar la búsqueda.`;
                resultsList.appendChild(aviso);
            }
        }

        // ── Eventos del input ────────────────────────────────────────────────────
        searchInput.addEventListener('input', () => {
            const q = searchInput.value.trim();
            if (q.length === 0) { closeDropdown(); return; }
            renderResults(search(q));
            openDropdown();
        });

        function opcionesSeleccionables() {
            return Array.from(resultsList.querySelectorAll('li[data-selectable]'));
        }

        function setActive(i) {
            const items = opcionesSeleccionables();
            if (!items.length) return;
            activeIndex = (i + items.length) % items.length;
            items.forEach((li, idx) => {
                const on = idx === activeIndex;
                li.classList.toggle('bg-indigo-500/20', on);
                li.setAttribute('aria-selected', on ? 'true' : 'false');
            });
            searchInput.setAttribute('aria-activedescendant', items[activeIndex].id);
            items[activeIndex].scrollIntoView({ block: 'nearest' });
        }

        searchInput.addEventListener('keydown', e => {
            if (e.key === 'Escape') { closeDropdown(); searchInput.value = ''; return; }
            if (dropdownEl.hidden) return;
            if (e.key === 'ArrowDown') { e.preventDefault(); setActive(activeIndex + 1); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(activeIndex - 1); }
            else if (e.key === 'Enter') {
                // Con la lista abierta Enter elige (no envía el formulario del drawer).
                e.preventDefault();
                const items = opcionesSeleccionables();
                const target = items[activeIndex] || items[0];
                if (target && target._product) selectProduct(target._product);
            }
        });

        // Cerrar al click fuera
        document.addEventListener('click', e => {
            if (!containerEl.contains(e.target) && !dropdownEl.contains(e.target)) {
                closeDropdown();
            }
        });

        // ── API pública por instancia ────────────────────────────────────────────
        containerEl._picker = {
            preload(ids) {
                selectedIds = new Set((ids || []).map(Number).filter(n => !isNaN(n)));
                mostrarAvisoAsociaciones([]);
                renderChips();
                syncHiddenInputs();
            },
            reset() {
                selectedIds = new Set();
                mostrarAvisoAsociaciones([]);
                renderChips();
                syncHiddenInputs();
                searchInput.value = '';
                closeDropdown();
            }
        };

        // Estado inicial vacío
        renderChips();
        syncHiddenInputs();
    }

    // ─── Entrada pública ─────────────────────────────────────────────────────────
    function init() {
        loadCatalog();
        document.querySelectorAll('.proveedor-product-picker').forEach(initPicker);
    }

    window.ProveedorProductPicker = { init };
    return { init };
})();
