/**
 * proveedor-crear-modal.js  –  Modal de creación de Proveedor
 *
 * Expone:
 *   ProveedorCrearModal.open()
 *   ProveedorCrearModal.close()
 *   ProveedorCrearModal.submit()
 */
const ProveedorCrearModal = (() => {
    const modal = () => document.getElementById('modal-crear-proveedor');
    const form  = () => document.getElementById('form-crear-proveedor');
    const summary = () => document.getElementById('proveedor-validation-summary');
    const errorList = () => document.getElementById('proveedor-error-list');

    function getToken() {
        const el = form().querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function showErrors(errors) {
        const ul = errorList();
        ul.innerHTML = '';
        for (const [field, msgs] of Object.entries(errors)) {
            msgs.forEach(m => {
                const li = document.createElement('li');
                li.textContent = field ? `${field}: ${m}` : m;
                ul.appendChild(li);
            });
            // Highlight field
            if (field) {
                const input = form().querySelector(`[name="${field}"]`);
                if (input) input.classList.add('border-red-500', 'ring-1', 'ring-red-500');
            }
        }
        summary().classList.remove('hidden');
        summary().scrollIntoView({ behavior: 'smooth', block: 'center' });
    }

    function clearErrors() {
        summary().classList.add('hidden');
        errorList().innerHTML = '';
        form().querySelectorAll('.border-red-500').forEach(el => {
            el.classList.remove('border-red-500', 'ring-1', 'ring-red-500');
        });
    }

    function resetForm() {
        form().reset();
        clearErrors();
        // Re-check the Activo toggle (default true)
        const activo = form().querySelector('input[name="Activo"]');
        if (activo) activo.checked = true;
    }

    function open() {
        resetForm();
        modal().classList.remove('hidden');
    }

    function close() {
        modal().classList.add('hidden');
    }

    async function submit() {
        clearErrors();

        const formData = new FormData(form());
        // Encode form data as URL params for MVC model binding
        const params = new URLSearchParams();
        for (const [key, value] of formData.entries()) {
            params.append(key, value);
        }
        // If Activo checkbox unchecked, it won't be in FormData — send false
        if (!formData.has('Activo')) {
            params.append('Activo', 'false');
        }

        try {
            const res = await fetch('/Proveedor/CreateAjax', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: params.toString()
            });
            const data = await res.json();

            if (data.success) {
                close();
                window.location.reload();
            } else if (data.errors) {
                showErrors(data.errors);
            }
        } catch {
            showErrors({ '': ['Error de conexión. Intente nuevamente.'] });
        }
    }

    /* ESC to close */
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && !modal().classList.contains('hidden')) close();
    });

    return { open, close, submit };
})();
