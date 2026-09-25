(function () {
    'use strict';

    var MODAL_ID   = 'modal-editar-categoria';
    var FORM_ID    = 'form-editar-categoria';
    var VAL_BOX_ID = 'cat-edit-validation-summary';
    var VAL_TXT_ID = 'cat-edit-validation-text';
    // Tras editar o eliminar se vuelve al catálogo en la pestaña Categorías (el listado sale del servidor).
    var CATALOGO_CATEGORIAS_URL = '/Catalogo?tab=categorias';

    var _openTrigger = null;

    function el(id) { return document.getElementById(id); }

    function open(trigger) {
        _openTrigger = (trigger instanceof Element) ? trigger : null;
        var modal = el(MODAL_ID);
        modal.classList.remove('hidden');
        modal.classList.add('flex');
        document.body.style.overflow = 'hidden';
        setTimeout(function () {
            var firstInput = el('cat-edit-codigo');
            if (firstInput) firstInput.focus();
        }, 50);
    }

    function close() {
        var trigger = _openTrigger;
        _openTrigger = null;
        var modal = el(MODAL_ID);
        modal.classList.add('hidden');
        modal.classList.remove('flex');
        document.body.style.overflow = '';
        clearErrors();
        hideValidation();
        if (trigger) trigger.focus();
    }

    // Una categoría no puede ser su propio padre: se oculta esa opción. El servidor además
    // rechaza cualquier ciclo (p. ej. elegir una descendiente).
    function excludeSelfFromParentSelect(selfId) {
        var sel = el('cat-edit-parentId');
        if (!sel) return;
        Array.prototype.forEach.call(sel.options, function (opt) {
            var esPropia = opt.value !== '' && opt.value === String(selfId);
            opt.disabled = esPropia;
            opt.hidden = esPropia;
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

        excludeSelfFromParentSelect(data.id);
        var parentSel = el('cat-edit-parentId');
        if (parentSel) parentSel.value = data.parentId != null ? String(data.parentId) : '';

        var alicuotaSel = el('cat-edit-alicuotaIVAId');
        if (alicuotaSel) alicuotaSel.value = data.alicuotaIVAId != null ? String(data.alicuotaIVAId) : '';

        el(FORM_ID).action = '/Categoria/EditAjax/' + data.id;
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

    function clearFieldError(input) {
        input.classList.remove('border-red-500');
        input.removeAttribute('aria-invalid');
        var span = document.querySelector('#' + FORM_ID + ' [data-valmsg-for="' + input.name + '"]');
        if (span) { span.textContent = ''; span.classList.add('hidden'); }
    }

    function clearErrors() {
        document.querySelectorAll('#' + FORM_ID + ' [data-valmsg-for]').forEach(function (s) {
            s.textContent = ''; s.classList.add('hidden');
        });
        document.querySelectorAll('#' + FORM_ID + ' [aria-invalid]').forEach(function (i) {
            i.classList.remove('border-red-500');
            i.removeAttribute('aria-invalid');
        });
    }

    function validateForm(form) {
        var errors = {};
        var fd = new FormData(form);
        var codigo = fd.get('Codigo');
        var nombre = fd.get('Nombre');
        if (!codigo || !codigo.trim()) errors.Codigo = ['El código es obligatorio'];
        if (!nombre || !nombre.trim()) errors.Nombre = ['El nombre es obligatorio'];
        return errors;
    }

    function handleErrors(errors) {
        var messages = [];
        var firstInput = null;
        Object.keys(errors).forEach(function (field) {
            var msgs = errors[field];
            msgs.forEach(function (m) { messages.push(m); });
            if (field) {
                var span = document.querySelector('#' + FORM_ID + ' [data-valmsg-for="' + field + '"]');
                if (span) { span.textContent = msgs[0]; span.classList.remove('hidden'); }
                var input = document.querySelector('#' + FORM_ID + ' [name="' + field + '"]');
                if (input) {
                    input.classList.add('border-red-500');
                    input.setAttribute('aria-invalid', 'true');
                    if (!firstInput) firstInput = input;
                }
            }
        });
        if (messages.length) showValidation(messages.join('. '));
        if (firstInput) firstInput.focus();
    }

    function initSubmit() {
        var form = el(FORM_ID);
        if (!form) return;

        form.addEventListener('input', function (e) {
            if (e.target.name) clearFieldError(e.target);
        });

        form.addEventListener('submit', async function (e) {
            e.preventDefault();
            hideValidation();
            clearErrors();

            var errors = validateForm(form);
            if (Object.keys(errors).length > 0) {
                handleErrors(errors);
                return;
            }

            var btn = el('btn-guardar-cat-edit');
            var origHTML = btn.innerHTML;
            btn.disabled = true;
            btn.innerHTML = '<span class="material-symbols-outlined text-[18px] animate-spin" aria-hidden="true">progress_activity</span> Guardando...';

            var recargando = false;
            try {
                var resp = await fetch(form.action, {
                    method: 'POST',
                    body: new FormData(form),
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });

                // Sin permiso o sesión vencida el servidor responde con una redirección/HTML, no JSON.
                var esJson = (resp.headers.get('content-type') || '').indexOf('json') !== -1;
                if (!resp.ok || !esJson) {
                    showValidation('No se pudo guardar: no tenés permiso para editar categorías o tu sesión venció. Recargá la página e intentá de nuevo.');
                    return;
                }

                var result = await resp.json();
                if (result.success) {
                    recargando = true;
                    window.location.assign(CATALOGO_CATEGORIAS_URL);
                } else if (result.errors) {
                    handleErrors(result.errors);
                }
            } catch (_) {
                showValidation('Error de conexión. Intentá nuevamente.');
            } finally {
                // En éxito la página se recarga: el botón queda en "Guardando..." para evitar un doble envío.
                if (!recargando) {
                    btn.disabled = false;
                    btn.innerHTML = origHTML;
                }
            }
        });
    }

    function toast(message, type) {
        document.dispatchEvent(new CustomEvent('catalogo:toast', { detail: { message: message, type: type } }));
    }

    function initDelegatedEvents() {
        document.addEventListener('click', async function (e) {
            var editBtn = e.target.closest('[data-cat-edit-id]');
            if (editBtn) {
                var id = editBtn.getAttribute('data-cat-edit-id');
                try {
                    var resp = await fetch('/Categoria/GetJson/' + id, {
                        headers: { 'X-Requested-With': 'XMLHttpRequest' }
                    });
                    if (!resp.ok) throw new Error('Not found');
                    populate(await resp.json());
                    open(editBtn);
                } catch (_) {
                    toast('No se pudo cargar la categoría. Intentá nuevamente.', 'error');
                }
                return;
            }

            var deleteBtn = e.target.closest('[data-cat-delete-id]');
            if (deleteBtn) {
                var delId     = deleteBtn.getAttribute('data-cat-delete-id');
                var delNombre = deleteBtn.getAttribute('data-cat-delete-nombre') || 'esta categoría';
                window.TheBury.confirmAction(
                    '¿Eliminar la categoría "' + delNombre + '"? Esta acción no se puede deshacer.',
                    function () {
                        var form = el('form-delete-categoria');
                        form.action = '/Categoria/Delete/' + delId + '?returnUrl=' + encodeURIComponent(CATALOGO_CATEGORIAS_URL);
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
            var modal = el(MODAL_ID);
            if (!modal || modal.classList.contains('hidden')) return;
            if (e.key === 'Escape') { close(); return; }
            if (e.key === 'Tab' && window.CatalogoModule) window.CatalogoModule.trapFocus(modal, e);
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        initSubmit();
        initDelegatedEvents();
    });
})();
