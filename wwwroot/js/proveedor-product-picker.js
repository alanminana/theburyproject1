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
 *  con objetos { id, codigo, nombre, marca, categoria }
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

    // ─── Catálogo ───────────────────────────────────────────────────────────────
    function loadCatalog() {
        const el = document.getElementById('productos-picker-data');
        if (!el) return;
        try { allProducts = JSON.parse(el.textContent || '[]'); } catch { allProducts = []; }
    }

    function search(query) {
        const q = query.toLowerCase();
        return allProducts.filter(p =>
            (p.nombre || '').toLowerCase().includes(q) ||
            (p.codigo || '').toLowerCase().includes(q) ||
            (p.marca || '').toLowerCase().includes(q) ||
            (p.categoria || '').toLowerCase().includes(q)
        ).slice(0, 25);
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

        // Estilos base del portal (position:fixed, todo via style para ser explícitos)
        Object.assign(dropdownEl.style, {
            position: 'fixed',
            zIndex:   '9999',
            minWidth: '280px',
            background: '#111827',           // gray-900 — completamente opaco
            border: '1px solid rgba(99,102,241,0.2)',
            borderRadius: '0.75rem',
            boxShadow: '0 24px 64px rgba(0,0,0,0.7), 0 8px 24px rgba(0,0,0,0.5)',
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
        }

        function closeDropdown() {
            dropdownEl.hidden = true;
            resultsList.innerHTML = '';
        }

        // Cerrar al hacer scroll en el contenedor del modal
        const modalScrollEl = containerEl.closest('.overflow-y-auto');
        if (modalScrollEl) {
            modalScrollEl.addEventListener('scroll', closeDropdown, { passive: true });
        }

        // Cerrar y reposicionar si cambia el tamaño de ventana
        window.addEventListener('resize', () => { if (!dropdownEl.hidden) closeDropdown(); }, { passive: true });

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
        function selectProduct(product) {
            selectedIds.add(product.id);
            renderChips();
            syncHiddenInputs();
            closeDropdown();
            searchInput.value = '';
            searchInput.focus();
        }

        // ── Renderizado de resultados ────────────────────────────────────────────
        function renderResults(results) {
            resultsList.innerHTML = '';

            if (results.length === 0) {
                const li = document.createElement('li');
                li.className = 'px-4 py-3 text-sm text-slate-400 italic text-center';
                li.textContent = 'Sin resultados para esta búsqueda';
                resultsList.appendChild(li);
                return;
            }

            results.forEach(p => {
                const isSelected = selectedIds.has(p.id);
                const li = document.createElement('li');
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
                    li.addEventListener('click', () => selectProduct(p));
                }
                resultsList.appendChild(li);
            });
        }

        // ── Eventos del input ────────────────────────────────────────────────────
        searchInput.addEventListener('input', () => {
            const q = searchInput.value.trim();
            if (q.length === 0) { closeDropdown(); return; }
            renderResults(search(q));
            openDropdown();
        });

        searchInput.addEventListener('keydown', e => {
            if (e.key === 'Escape') { closeDropdown(); searchInput.value = ''; }
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
                renderChips();
                syncHiddenInputs();
            },
            reset() {
                selectedIds = new Set();
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
