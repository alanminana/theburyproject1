(function () {
    'use strict';

    var MODAL_ID   = 'modal-editar-categoria';
    var FORM_ID    = 'form-editar-categoria';
    var VAL_BOX_ID = 'cat-edit-validation-summary';
    var VAL_TXT_ID = 'cat-edit-validation-text';

    var currentRow = null;

    function el(id) { return document.getElementById(id); }

    function open(row) {
        currentRow = row;
        var modal = el(MODAL_ID);
        modal.classList.remove('hidden');
        modal.classList.add('flex');
        document.body.style.overflow = 'hidden';
    }

    function close() {
        var modal = el(MODAL_ID);
        modal.classList.add('hidden');
        modal.classList.remove('flex');
        document.body.style.overflow = '';
        currentRow = null;
        clearErrors();
        hideValidation();
    }

    function populateParentSelect(categorias, excludeId) {
        var sel = el('cat-edit-parentId');
        if (!sel) return;
        sel.innerHTML = '<option value="">Ninguna (Categoría Principal)</option>';
        (categorias || []).forEach(function (c) {
            if (String(c.id) !== String(excludeId)) {
                var opt = document.createElement('option');
                opt.value = c.id;
                opt.textContent = c.nombre;
                sel.appendChild(opt);
            }
        });
    }

    function populate(data) {
        el('cat-edit-id').value           = data.id;
        el('cat-edit-rowversion').value   = data.rowVersion || '';
        el('cat-edit-codigo').value       = data.codigo || '';
        el('cat-edit-nombre').value       = data.nombre || '';
        el('cat-edit-descripcion').value  = data.descripcion || '';
        el('cat-edit-controlSerie').checked = !!data.controlSerieDefault;
        el('cat-edit-activo').checked     = !!data.activo;

        var categorias = (window.CatalogoData && window.CatalogoData.categorias) || [];
        populateParentSelect(categorias, data.id);
        if (data.parentId != null) {
            var sel = el('cat-edit-parentId');
            if (sel) sel.value = data.parentId;
        }

        el(FORM_ID).action = '/Categoria/EditAjax/' + data.id;
    }

    function updateRow(entity) {
        if (!currentRow) return;
        var tds = currentRow.querySelectorAll('td');
        if (tds[0]) tds[0].textContent = entity.codigo;
        if (tds[1]) {
            var nameEl = tds[1].querySelector('.font-semibold');
            if (nameEl) nameEl.textContent = entity.nombre;
        }
        if (tds[3]) {
            tds[3].innerHTML = entity.activo
                ? '<span class="inline-flex items-center px-2 py-1 rounded-full text-[10px] font-bold uppercase tracking-wider bg-green-500/20 text-green-400 border border-green-500/30">Activo</span>'
                : '<span class="inline-flex items-center px-2 py-1 rounded-full text-[10px] font-bold uppercase tracking-wider bg-red-500/20 text-red-400 border border-red-500/30">Inactivo</span>';
        }
    }

    function showValidation(text) {
        var box = el(VAL_BOX_ID);
        var msg = el(VAL_TXT_ID);
        if (box) { box.classList.remove('hidden'); box.classList.add('flex'); }
        if (msg)   msg.textContent = text;
    }

    function hideValidation() {
        var box = el(VAL_BOX_ID);
        if (box) { box.classList.add('hidden'); box.classList.remove('flex'); }
    }

    function clearErrors() {
        document.querySelectorAll('#' + FORM_ID + ' [data-valmsg-for]').forEach(function (s) {
            s.textContent = ''; s.classList.add('hidden');
        });
        document.querySelectorAll('#' + FORM_ID + ' .border-red-500').forEach(function (i) {
            i.classList.remove('border-red-500');
        });
    }

    function handleServerErrors(errors) {
        var messages = [];
        Object.keys(errors).forEach(function (field) {
            var msgs = errors[field];
            msgs.forEach(function (m) { messages.push(m); });
            if (field) {
                var span = document.querySelector('#' + FORM_ID + ' [data-valmsg-for="' + field + '"]');
                if (span) { span.textContent = msgs[0]; span.classList.remove('hidden'); }
                var input = document.querySelector('#' + FORM_ID + ' [name="' + field + '"]');
                if (input) input.classList.add('border-red-500');
            }
        });
        if (messages.length) showValidation(messages.join('. '));
    }

    function initSubmit() {
        var form = el(FORM_ID);
        if (!form) return;

        form.addEventListener('submit', async function (e) {
            e.preventDefault();
            hideValidation();
            clearErrors();

            var btn = el('btn-guardar-cat-edit');
            var origHTML = btn.innerHTML;
            btn.disabled = true;
            btn.innerHTML = '<span class="material-symbols-outlined text-[18px] animate-spin">progress_activity</span> Guardando...';

            try {
                var resp = await fetch(form.action, {
                    method: 'POST',
                    body: new FormData(form),
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });
                var result = await resp.json();
                if (result.success) {
                    updateRow(result.entity);
                    close();
                    document.dispatchEvent(new CustomEvent('catalogo:toast', {
                        detail: { message: result.message || 'Categoría actualizada', type: 'success' }
                    }));
                } else if (result.errors) {
                    handleServerErrors(result.errors);
                }
            } catch (_) {
                showValidation('Error de conexión. Intentá nuevamente.');
            } finally {
                btn.disabled = false;
                btn.innerHTML = origHTML;
            }
        });
    }

    function initDelegatedEvents() {
        document.addEventListener('click', async function (e) {
            var editBtn = e.target.closest('[data-cat-edit-id]');
            if (editBtn) {
                var id = editBtn.getAttribute('data-cat-edit-id');
                var row = editBtn.closest('tr');
                try {
                    var resp = await fetch('/Categoria/GetJson/' + id, {
                        headers: { 'X-Requested-With': 'XMLHttpRequest' }
                    });
                    if (!resp.ok) throw new Error('Not found');
                    populate(await resp.json());
                    open(row);
                } catch (_) {
                    document.dispatchEvent(new CustomEvent('catalogo:toast', {
                        detail: { message: 'Error al cargar la categoría.', type: 'error' }
                    }));
                }
                return;
            }

            var deleteBtn = e.target.closest('[data-cat-delete-id]');
            if (deleteBtn) {
                var delId     = deleteBtn.getAttribute('data-cat-delete-id');
                var delNombre = deleteBtn.getAttribute('data-cat-delete-nombre') || 'esta categoría';
                window.TheBury.confirmAction(
                    '¿Eliminar "' + delNombre + '"? Esta acción no se puede deshacer.',
                    function () {
                        var form = el('form-delete-categoria');
                        form.action = '/Categoria/Delete/' + delId + '?returnUrl=/Catalogo';
                        form.submit();
                    }
                );
                return;
            }

            if (e.target.closest('[data-cat-edit-modal-close]')) {
                close();
            }
        });

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                var modal = el(MODAL_ID);
                if (modal && !modal.classList.contains('hidden')) close();
            }
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        initSubmit();
        initDelegatedEvents();
    });
})();
