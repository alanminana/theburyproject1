/**
 * categoria-crear-modal.js
 * Lógica del modal "Nueva categoría" en la vista Catálogo Index_tw.
 * Maneja: apertura/cierre, validación por campo y envío AJAX.
 *
 * Tras un alta exitosa se recarga el catálogo en la pestaña Categorías: el listado, el contador de
 * la pestaña, los combos de "categoría padre" y los de producto salen todos del servidor (una sola
 * fuente de verdad). El aviso de éxito viaja por TempData desde CategoriaController.CreateAjax.
 */
const CategoriaModal = (() => {
    const FORM_ID = 'form-nueva-categoria';
    const CATALOGO_CATEGORIAS_URL = '/Catalogo?tab=categorias';
    const el = (id) => document.getElementById(id);
    let _openTrigger = null;

    // ── Abrir / Cerrar ──────────────────────────────────────
    function open(trigger) {
        _openTrigger = (trigger instanceof Element) ? trigger : null;
        const modal = el('modal-nueva-categoria');
        modal.classList.remove('hidden');
        modal.classList.add('flex');
        document.body.style.overflow = 'hidden';
        setTimeout(function () {
            var firstInput = document.querySelector('#' + FORM_ID + ' input[name="Codigo"]');
            if (firstInput) firstInput.focus();
        }, 50);
    }

    function close() {
        const trigger = _openTrigger;
        _openTrigger = null;
        const modal = el('modal-nueva-categoria');
        modal.classList.add('hidden');
        modal.classList.remove('flex');
        document.body.style.overflow = '';
        resetForm();
        if (trigger) trigger.focus();
    }

    function resetForm() {
        const form = el(FORM_ID);
        if (form) form.reset();
        hideValidation();
        clearFieldErrors();
    }

    // ── Errores por campo ───────────────────────────────────
    function fieldInput(field) {
        return document.querySelector('#' + FORM_ID + ' [name="' + field + '"]');
    }

    function markFieldError(field, message) {
        const span = document.querySelector('#' + FORM_ID + ' [data-valmsg-for="' + field + '"]');
        if (span) {
            span.textContent = message;
            span.classList.remove('hidden');
        }
        const input = fieldInput(field);
        if (input) {
            input.classList.add('border-red-500');
            input.setAttribute('aria-invalid', 'true');
        }
    }

    function clearFieldError(input) {
        input.classList.remove('border-red-500');
        input.removeAttribute('aria-invalid');
        const span = document.querySelector('#' + FORM_ID + ' [data-valmsg-for="' + input.name + '"]');
        if (span) {
            span.textContent = '';
            span.classList.add('hidden');
        }
    }

    function clearFieldErrors() {
        document.querySelectorAll('#' + FORM_ID + ' [data-valmsg-for]').forEach(span => {
            span.textContent = '';
            span.classList.add('hidden');
        });
        document.querySelectorAll('#' + FORM_ID + ' [aria-invalid]').forEach(input => {
            input.classList.remove('border-red-500');
            input.removeAttribute('aria-invalid');
        });
    }

    function validateForm(form) {
        const errors = {};
        const fd = new FormData(form);

        if (!fd.get('Codigo')?.trim()) errors.Codigo = ['El código es obligatorio'];
        if (!fd.get('Nombre')?.trim()) errors.Nombre = ['El nombre es obligatorio'];

        return errors;
    }

    function handleErrors(errors) {
        const messages = [];
        let firstField = null;
        for (const [field, msgs] of Object.entries(errors)) {
            msgs.forEach(msg => messages.push(msg));
            if (field) {
                markFieldError(field, msgs[0]);
                if (!firstField) firstField = field;
            }
        }
        if (messages.length) showValidation(messages.join('. '));
        const target = firstField && fieldInput(firstField);
        if (target) target.focus();
    }

    // ── Resumen de validación ───────────────────────────────
    function showValidation(text) {
        const box = el('cat-modal-validation-summary');
        const msg = el('cat-modal-validation-text');
        if (box && msg) {
            msg.textContent = text;
            box.classList.remove('hidden');
            box.classList.add('flex');
        }
    }

    function hideValidation() {
        const box = el('cat-modal-validation-summary');
        if (box) {
            box.classList.add('hidden');
            box.classList.remove('flex');
        }
    }

    // ── Envío AJAX ──────────────────────────────────────────
    function initSubmit() {
        const form = el(FORM_ID);
        if (!form) return;

        form.addEventListener('input', (e) => {
            if (e.target.name) clearFieldError(e.target);
        });

        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            hideValidation();
            clearFieldErrors();

            const errors = validateForm(form);
            if (Object.keys(errors).length > 0) {
                handleErrors(errors);
                return;
            }

            const btn = el('btn-guardar-categoria');
            const origHTML = btn.innerHTML;
            btn.disabled = true;
            btn.innerHTML = '<span class="material-symbols-outlined text-[18px] animate-spin" aria-hidden="true">progress_activity</span> Guardando...';

            let recargando = false;
            try {
                const resp = await fetch(form.action, {
                    method: 'POST',
                    body: new FormData(form),
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });

                // Sin permiso o sesión vencida el servidor responde con una redirección/HTML, no JSON.
                const esJson = (resp.headers.get('content-type') || '').includes('json');
                if (!resp.ok || !esJson) {
                    showValidation('No se pudo guardar: no tenés permiso para crear categorías o tu sesión venció. Recargá la página e intentá de nuevo.');
                    return;
                }

                const result = await resp.json();
                if (result.success) {
                    recargando = true;
                    window.location.assign(CATALOGO_CATEGORIAS_URL);
                } else if (result.errors) {
                    handleErrors(result.errors);
                }
            } catch {
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

    // ── Esc para cerrar ─────────────────────────────────────
    function initEscKey() {
        document.addEventListener('keydown', (e) => {
            const modal = el('modal-nueva-categoria');
            if (!modal || modal.classList.contains('hidden')) return;
            if (e.key === 'Escape') { close(); return; }
            if (e.key === 'Tab' && window.CatalogoModule) window.CatalogoModule.trapFocus(modal, e);
        });
    }

    // ── Init ────────────────────────────────────────────────
    function init() {
        initSubmit();
        initEscKey();
    }

    document.addEventListener('DOMContentLoaded', init);

    return { open, close };
})();

if (typeof CatalogoModule !== 'undefined' && typeof CatalogoModule.registerModalApi === 'function') {
    CatalogoModule.registerModalApi('categoria', CategoriaModal);
}
