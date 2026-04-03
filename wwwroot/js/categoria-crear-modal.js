/**
 * categoria-crear-modal.js
 * Lógica del modal "Nueva Categoría" en la vista Catálogo Index_tw.
 * Maneja: apertura/cierre, envío AJAX y validación.
 */
const CategoriaModal = (() => {
    const el = (id) => document.getElementById(id);

    // ── Abrir / Cerrar ──────────────────────────────────────
    function open() {
        const modal = el('modal-nueva-categoria');
        modal.classList.remove('hidden');
        modal.classList.add('flex');
        document.body.style.overflow = 'hidden';
    }

    function close() {
        const modal = el('modal-nueva-categoria');
        modal.classList.add('hidden');
        modal.classList.remove('flex');
        document.body.style.overflow = '';
        resetForm();
    }

    function resetForm() {
        const form = el('form-nueva-categoria');
        if (form) form.reset();
        hideValidation();
        clearFieldErrors();
    }

    // ── Envío AJAX ──────────────────────────────────────────
    function initSubmit() {
        const form = el('form-nueva-categoria');
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

            const btn = el('btn-guardar-categoria');
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
                    location.reload();
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
                const span = document.querySelector(`#form-nueva-categoria [data-valmsg-for="${field}"]`);
                if (span) {
                    span.textContent = msgs[0];
                    span.classList.remove('hidden');
                }
                const input = document.querySelector(`#form-nueva-categoria [name="${field}"]`);
                if (input) input.classList.add('border-red-500');
            }
        }
        if (messages.length) showValidation(messages.join('. '));
    }

    // ── Validación visual ───────────────────────────────────
    function showValidation(text) {
        const box = el('cat-modal-validation-summary');
        const msg = el('cat-modal-validation-text');
        if (box && msg) {
            msg.textContent = text;
            box.classList.remove('hidden');
            box.classList.add('flex');
            box.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    }

    function hideValidation() {
        const box = el('cat-modal-validation-summary');
        if (box) {
            box.classList.add('hidden');
            box.classList.remove('flex');
        }
    }

    function clearFieldErrors() {
        document.querySelectorAll('#form-nueva-categoria [data-valmsg-for]').forEach(span => {
            span.textContent = '';
            span.classList.add('hidden');
        });
        document.querySelectorAll('#form-nueva-categoria .border-red-500').forEach(input => {
            input.classList.remove('border-red-500');
        });
    }

    // ── Esc para cerrar ─────────────────────────────────────
    function initEscKey() {
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                const modal = el('modal-nueva-categoria');
                if (modal && !modal.classList.contains('hidden')) {
                    close();
                }
            }
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
