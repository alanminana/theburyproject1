/**
 * marca-crear-modal.js
 * Lógica del modal "Nueva Marca" en la vista Catálogo Index_tw.
 * Maneja: apertura/cierre, envío AJAX y validación.
 */
const MarcaModal = (() => {
    const el = (id) => document.getElementById(id);

    var _panel = null;
    function panel() {
        if (!_panel) {
            _panel = window.InvUI.panelFormulario(document.getElementById('modal-nueva-marca'), {
                cierres: '[data-catalogo-modal-close="marca"]',
                alCerrar: resetForm
            });
        }
        return _panel;
    }

    function open() {
        resetForm();
        panel().abrir();
        const firstInput = document.querySelector('#form-nueva-marca input[name="Codigo"]');
        if (firstInput) firstInput.focus();
    }

    function close() { panel().cerrar(); }

    function resetForm() {
        const form = el('form-nueva-marca');
        if (form) form.reset();
        hideValidation();
        clearFieldErrors();
    }

    function initSubmit() {
        const form = el('form-nueva-marca');
        if (!form) return;

        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            hideValidation();
            clearFieldErrors();

            const errors = validateForm(form);
            if (Object.keys(errors).length > 0) {
                handleServerErrors(errors);
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
                    // Listado, contador y combos salen del servidor.
                    window.location.assign('/Catalogo?tab=marcas');
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
        const errors = {};
        const fd = new FormData(form);
        if (!fd.get('Codigo')?.trim()) errors.Codigo = ['El código es obligatorio'];
        if (!fd.get('Nombre')?.trim()) errors.Nombre = ['El nombre es obligatorio'];
        return errors;
    }

    function handleServerErrors(errors) {
        const messages = [];
        let primero = null;
        for (const [field, msgs] of Object.entries(errors)) {
            msgs.forEach(msg => messages.push(msg));
            if (field) {
                const span = document.querySelector(`#form-nueva-marca [data-valmsg-for="${field}"]`);
                if (span) { span.textContent = msgs[0]; span.classList.remove('hidden'); }
                const input = document.querySelector(`#form-nueva-marca [name="${field}"]`);
                if (input) {
                    input.classList.add('border-red-500');
                    input.setAttribute('aria-invalid', 'true');
                    if (!primero) primero = input;
                }
            }
        }
        if (messages.length) showValidation(messages.map(function (m) { return String(m).replace(/[.\s]+$/, ''); }).join('. '));
        if (primero) primero.focus();
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
        document.querySelectorAll('#form-nueva-marca .border-red-500').forEach(i => {
            i.classList.remove('border-red-500');
            i.removeAttribute('aria-invalid');
        });
    }

    function init() {
        initSubmit();
        panel();
    }

    document.addEventListener('DOMContentLoaded', init);

    return { open, close };
})();

if (typeof CatalogoModule !== 'undefined' && typeof CatalogoModule.registerModalApi === 'function') {
    CatalogoModule.registerModalApi('marca', MarcaModal);
}
