/**
 * marca-crear-modal.js
 * Lógica del modal "Nueva Marca" en la vista Catálogo Index_tw.
 * Maneja: apertura/cierre, envío AJAX y validación.
 */
const MarcaModal = (() => {
    const el = (id) => document.getElementById(id);

    function open() {
        const modal = el('modal-nueva-marca');
        modal.classList.remove('hidden');
        modal.classList.add('flex');
        document.body.style.overflow = 'hidden';
    }

    function close() {
        const modal = el('modal-nueva-marca');
        modal.classList.add('hidden');
        modal.classList.remove('flex');
        document.body.style.overflow = '';
        resetForm();
    }

    function resetForm() {
        const form = el('form-nueva-marca');
        if (form) form.reset();
        hideValidation();
        clearFieldErrors();
    }

    // ── Actualización DOM tras alta exitosa ─────────────────
    function escHtml(str) {
        var d = document.createElement('div');
        d.textContent = str || '';
        return d.innerHTML;
    }

    function onMarcaCreada(entity, form) {
        if (!entity) return;

        var tbody = document.getElementById('marcas-tbody');
        if (tbody) {
            var emptyRow = tbody.querySelector('tr td[colspan]');
            if (emptyRow) emptyRow.closest('tr').remove();

            var parentSelect = form ? form.querySelector('[name="ParentId"]') : null;
            var parentNombre = parentSelect && parentSelect.value
                ? (parentSelect.options[parentSelect.selectedIndex] || {}).text || '—'
                : '—';

            var tr = document.createElement('tr');
            tr.className = 'hover:bg-slate-50 dark:hover:bg-white/5 transition-colors';
            tr.innerHTML =
                '<td class="px-6 py-4 text-sm font-mono text-slate-500 dark:text-slate-400">' + escHtml(entity.codigo) + '</td>' +
                '<td class="px-6 py-4">' +
                  '<div class="flex items-center gap-3">' +
                    '<div class="w-10 h-10 rounded bg-slate-100 dark:bg-slate-800 flex items-center justify-center flex-shrink-0">' +
                      '<span class="material-symbols-outlined text-slate-400 text-lg">branding_watermark</span>' +
                    '</div>' +
                    '<div><span class="text-sm font-semibold text-slate-900 dark:text-white">' + escHtml(entity.nombre) + '</span></div>' +
                  '</div>' +
                '</td>' +
                '<td class="px-6 py-4 text-sm text-slate-600 dark:text-slate-300">' + escHtml(parentNombre) + '</td>' +
                '<td class="px-6 py-4 text-sm text-slate-600 dark:text-slate-300">' + escHtml(entity.paisOrigen || '—') + '</td>' +
                '<td class="px-6 py-4 text-center">' +
                  '<span class="inline-flex items-center px-2 py-1 rounded-full text-[10px] font-bold uppercase tracking-wider bg-green-500/20 text-green-400 border border-green-500/30">Activo</span>' +
                '</td>' +
                '<td class="px-6 py-4 text-right">' +
                  '<div class="flex flex-wrap justify-end gap-2">' +
                    '<button type="button" data-marca-edit-id="' + entity.id + '" class="inline-flex items-center gap-1.5 rounded-lg border border-slate-700 px-2.5 py-1.5 text-xs font-semibold text-slate-300 transition-colors hover:border-slate-600 hover:bg-slate-800 hover:text-white">' +
                      '<span class="material-symbols-outlined text-base">edit</span><span>Editar</span></button>' +
                    '<button type="button" data-marca-delete-id="' + entity.id + '" data-marca-delete-nombre="' + escHtml(entity.nombre) + '" class="inline-flex items-center gap-1.5 rounded-lg border border-red-500/30 px-2.5 py-1.5 text-xs font-semibold text-red-400 transition-colors hover:bg-red-500/10 hover:text-red-400">' +
                      '<span class="material-symbols-outlined text-base">delete</span><span>Eliminar</span></button>' +
                  '</div>' +
                '</td>';
            tbody.appendChild(tr);
        }

        // Agregar la nueva marca al select del modal de producto
        var marcaSelect = document.getElementById('modal-marcaId');
        if (marcaSelect) {
            var opt = document.createElement('option');
            opt.value = entity.id;
            opt.textContent = entity.nombre;
            marcaSelect.appendChild(opt);
        }

        // Agregar la nueva marca al select ParentId del propio modal
        var parentSelect = document.getElementById('marca-modal-parentId');
        if (parentSelect) {
            var parentOpt = document.createElement('option');
            parentOpt.value = entity.id;
            parentOpt.textContent = entity.nombre;
            parentSelect.appendChild(parentOpt);
        }

        document.dispatchEvent(new CustomEvent('catalogo:toast', {
            detail: { message: 'Marca creada: ' + (entity.nombre || ''), type: 'success' }
        }));
    }

    function initSubmit() {
        const form = el('form-nueva-marca');
        if (!form) return;

        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            hideValidation();
            clearFieldErrors();

            const errors = validateForm(form);
            if (errors.length > 0) {
                showValidation(errors.join('. '));
                return;
            }

            const btn = el('btn-guardar-marca');
            const origHTML = btn.innerHTML;
            btn.disabled = true;
            btn.innerHTML = '<span class="material-symbols-outlined text-[18px] animate-spin">progress_activity</span> Guardando...';

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
                    onMarcaCreada(result.entity, form);
                } else if (result.errors) {
                    handleServerErrors(result.errors);
                }
            } catch {
                showValidation('Error de conexión. Intente nuevamente.');
            } finally {
                btn.disabled = false;
                btn.innerHTML = origHTML;
            }
        });
    }

    function validateForm(form) {
        const errors = [];
        const fd = new FormData(form);
        if (!fd.get('Codigo')?.trim()) errors.push('El código es obligatorio');
        if (!fd.get('Nombre')?.trim()) errors.push('El nombre es obligatorio');
        return errors;
    }

    function handleServerErrors(errors) {
        const messages = [];
        for (const [field, msgs] of Object.entries(errors)) {
            msgs.forEach(msg => messages.push(msg));
            if (field) {
                const span = document.querySelector(`#form-nueva-marca [data-valmsg-for="${field}"]`);
                if (span) { span.textContent = msgs[0]; span.classList.remove('hidden'); }
                const input = document.querySelector(`#form-nueva-marca [name="${field}"]`);
                if (input) input.classList.add('border-red-500');
            }
        }
        if (messages.length) showValidation(messages.join('. '));
    }

    function showValidation(text) {
        const box = el('marca-modal-validation-summary');
        const msg = el('marca-modal-validation-text');
        if (box && msg) {
            msg.textContent = text;
            box.classList.remove('hidden');
            box.classList.add('flex');
        }
    }

    function hideValidation() {
        const box = el('marca-modal-validation-summary');
        if (box) { box.classList.add('hidden'); box.classList.remove('flex'); }
    }

    function clearFieldErrors() {
        document.querySelectorAll('#form-nueva-marca [data-valmsg-for]').forEach(s => { s.textContent = ''; s.classList.add('hidden'); });
        document.querySelectorAll('#form-nueva-marca .border-red-500').forEach(i => i.classList.remove('border-red-500'));
    }

    function initEscKey() {
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                const modal = el('modal-nueva-marca');
                if (modal && !modal.classList.contains('hidden')) close();
            }
        });
    }

    function init() {
        initSubmit();
        initEscKey();
    }

    document.addEventListener('DOMContentLoaded', init);

    return { open, close };
})();

if (typeof CatalogoModule !== 'undefined' && typeof CatalogoModule.registerModalApi === 'function') {
    CatalogoModule.registerModalApi('marca', MarcaModal);
}
