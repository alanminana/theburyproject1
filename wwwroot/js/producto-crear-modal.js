/**
 * producto-crear-modal.js
 * Lógica del modal "Nuevo Producto" en la vista Catálogo Index_tw.
 * Maneja: apertura/cierre, dropdowns cascada, cálculo IVA,
 * características dinámicas y envío AJAX.
 */
const ProductoModal = (() => {
    // DOM refs (lazy)
    const el = (id) => document.getElementById(id);

    let caracteristicaIndex = 0;

    // ── Abrir / Cerrar ──────────────────────────────────────
    function open() {
        const modal = el('modal-nuevo-producto');
        modal.classList.remove('hidden');
        modal.classList.add('flex');
        document.body.style.overflow = 'hidden';
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

        // Limpiar subcategorías / submarcas
        resetSelect(el('modal-subcategoriaId'), 'Seleccionar subcategoría');
        resetSelect(el('modal-submarcaId'), 'Seleccionar submarca');

        // Limpiar tabla de características
        const tbody = el('modal-caracteristicas-body');
        if (tbody) tbody.innerHTML = '';
        caracteristicaIndex = 0;

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

    // ── Dropdowns en cascada ────────────────────────────────
    function initCascadingDropdowns() {
        const catSelect = el('modal-categoriaId');
        const marcaSelect = el('modal-marcaId');

        if (catSelect) {
            catSelect.addEventListener('change', async () => {
                const catId = catSelect.value;
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
            });
        }

        if (marcaSelect) {
            marcaSelect.addEventListener('change', async () => {
                const marcaId = marcaSelect.value;
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
            });
        }
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
                <button type="button" onclick="ProductoModal.removeCaracteristica(this)"
                        class="text-red-500 hover:text-red-600 transition-colors">
                    <span class="material-symbols-outlined">delete</span>
                </button>
            </td>`;
        tbody.appendChild(tr);

        nombreInput.value = '';
        valorInput.value = '';
        nombreInput.focus();
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
                    window.location.reload();
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
    }

    document.addEventListener('DOMContentLoaded', init);

    return { open, close, addCaracteristica, removeCaracteristica };
})();
