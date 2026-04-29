/**
 * venta-crear-modal.js  –  Modal de creación de Venta
 *
 * Expone:
 *   VentaCrearModal.open()
 *   VentaCrearModal.close()
 *   VentaCrearModal.submit()
 */
const VentaCrearModal = (() => {
    const modal    = () => document.getElementById('modal-crear-venta');
    const summary  = () => document.getElementById('venta-ajax-validation-summary');
    const errList  = () => document.getElementById('venta-ajax-error-list');

    function showErrors(errors) {
        const ul = errList();
        ul.replaceChildren();
        for (const [field, msgs] of Object.entries(errors)) {
            (Array.isArray(msgs) ? msgs : [msgs]).forEach(m => {
                const li = document.createElement('li');
                li.textContent = field ? `${field}: ${m}` : m;
                ul.appendChild(li);
            });
        }
        const s = summary();
        s.classList.remove('hidden');
        s.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }

    function clearErrors() {
        summary().classList.add('hidden');
        errList().replaceChildren();
        const vendedorSelect = document.getElementById('VendedorUserId');
        if (vendedorSelect) vendedorSelect.classList.remove('ring-1', 'ring-red-500', 'border-red-500');
    }

    function open() {
        clearErrors();
        document.dispatchEvent(new CustomEvent('venta-crear-modal:open'));
        modal().classList.remove('hidden');
        document.body.classList.add('overflow-hidden');
    }

    function close() {
        document.dispatchEvent(new CustomEvent('venta-crear-modal:close'));
        modal().classList.add('hidden');
        document.body.classList.remove('overflow-hidden');
    }

    async function submit() {
        // Respetar el guard de excepción de venta-create.js:
        // Si el panel de excepción está visible y no fue confirmada, no continuar.
        const panelExcepcion = document.getElementById('panel-excepcion-activa');
        const panelVisible   = panelExcepcion && !panelExcepcion.classList.contains('hidden');
        const hdnExcepcion   = document.getElementById('hdn-aplicar-excepcion');
        const excepcionOk    = !panelVisible || (hdnExcepcion && hdnExcepcion.value === 'true');

        if (!excepcionOk) {
            const txt = document.getElementById('txt-excepcion-documental');
            if (txt && !txt.value.trim()) {
                txt.classList.add('border-red-500', 'ring-1', 'ring-red-500');
                txt.focus();
            }
            return;
        }

        // Vendedor obligatorio cuando el select está disponible (roles con delegación)
        const vendedorSelect = document.getElementById('VendedorUserId');
        if (vendedorSelect && !vendedorSelect.value) {
            showErrors({ '': ['El vendedor es obligatorio. Seleccioná un vendedor antes de continuar.'] });
            vendedorSelect.classList.add('ring-1', 'ring-red-500', 'border-red-500');
            vendedorSelect.scrollIntoView({ behavior: 'smooth', block: 'center' });
            vendedorSelect.focus();
            return;
        }

        clearErrors();

        const formEl   = document.getElementById('venta-form');
        const formData = new FormData(formEl);
        const params   = new URLSearchParams();

        for (const [key, value] of formData.entries()) {
            params.append(key, value);
        }

        // Deshabilitar el botón durante el request
        const btnConfirmar = document.getElementById('btn-confirmar');
        const originalText = btnConfirmar ? btnConfirmar.innerHTML : null;
        if (btnConfirmar) {
            btnConfirmar.disabled = true;
            btnConfirmar.innerHTML =
                '<span class="material-symbols-outlined animate-spin">progress_activity</span> Procesando…';
        }

        try {
            const res  = await fetch('/Venta/CreateAjax', {
                method:  'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body:    params.toString()
            });
            const data = await res.json();

            if (data.success && data.requiresRedirect) {
                window.location.href = data.redirectUrl;
            } else if (!data.success) {
                showErrors(data.errors || { '': ['Error al crear la venta.'] });
            }
        } catch {
            showErrors({ '': ['Error de conexión. Intente nuevamente.'] });
        } finally {
            if (btnConfirmar && originalText) {
                btnConfirmar.disabled = false;
                btnConfirmar.innerHTML = originalText;
            }
        }
    }

    // ── Listeners ────────────────────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', () => {
        // Botón abrir
        const btnAbrir = document.getElementById('btn-abrir-modal-crear-venta');
        if (btnAbrir) btnAbrir.addEventListener('click', open);

        // Botón cerrar (header)
        const btnCerrar = document.getElementById('btn-cerrar-modal-crear-venta');
        if (btnCerrar) btnCerrar.addEventListener('click', close);

        // Cerrar al hacer click en el backdrop
        const backdrop = document.getElementById('modal-crear-venta-backdrop');
        if (backdrop) backdrop.addEventListener('click', close);
    });

    // ESC para cerrar
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape') {
            const m = modal();
            if (m && !m.classList.contains('hidden')) close();
        }
    });

    return { open, close, submit };
})();
