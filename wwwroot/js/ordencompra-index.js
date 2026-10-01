/**
 * ordencompra-index.js  –  Órdenes de Compra Index page
 *
 * - Auto-dismiss toast notifications
 * - Initialize the module scroll affordance
 */
(() => {
    if (window.TheBury && typeof window.TheBury.autoDismissToasts === 'function') {
        window.TheBury.autoDismissToasts(4000);
    }

    // Avanzar el envío cambia el estado de la orden: se confirma antes de enviar el formulario.
    document.querySelectorAll('form[data-oc-confirm]').forEach((form) => {
        form.addEventListener('submit', (event) => {
            if (form.dataset.confirmed === 'true') return;
            event.preventDefault();
            const message = form.dataset.confirmMessage || '¿Continuar?';
            // Avanzar el envío no destruye nada: confirmación en tono primario, no de peligro.
            const options = { tone: 'primary', title: form.dataset.confirmTitle, confirmLabel: form.dataset.confirmLabel };
            const proceed = () => {
                form.dataset.confirmed = 'true';
                form.submit();
            };
            if (window.TheBury && typeof window.TheBury.confirmAction === 'function') {
                window.TheBury.confirmAction(message, proceed, options);
            } else if (window.confirm(message)) {
                proceed();
            }
        });
    });

    if (window.TheBury && typeof window.TheBury.initHorizontalScrollAffordance === 'function') {
        window.TheBury.initHorizontalScrollAffordance(document.querySelector('[data-oc-scroll]'));
    }
})();
