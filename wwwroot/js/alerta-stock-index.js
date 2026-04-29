(() => {
    'use strict';

    const theBury = window.TheBury || {};
    const nativeSubmit = HTMLFormElement.prototype.submit;

    theBury.autoDismissToasts?.(4500);

    if (typeof theBury.initHorizontalScrollAffordance === 'function') {
        document.querySelectorAll('[data-oc-scroll]').forEach((root) => {
            theBury.initHorizontalScrollAffordance(root);
        });
    }

    document.addEventListener('submit', (event) => {
        const form = event.target.closest('[data-alerta-confirm]');
        if (!form) return;

        event.preventDefault();

        const message = form.dataset.alertaConfirm || 'Confirmar la accion seleccionada?';
        if (typeof theBury.confirmAction === 'function') {
            theBury.confirmAction(message, () => {
                nativeSubmit.call(form);
            });
            return;
        }

        nativeSubmit.call(form);
    });
})();
