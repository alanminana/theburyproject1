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

    // Igual que data-alerta-confirm, pero además deja escribir observaciones
    // antes de confirmar (ej. Resolver desde el listado).
    document.addEventListener('submit', (event) => {
        const form = event.target.closest('[data-alerta-confirm-note]');
        if (!form) return;

        event.preventDefault();

        const message = form.dataset.alertaConfirmNote || 'Confirmar la accion seleccionada?';
        const notePlaceholder = form.dataset.alertaNotePlaceholder || 'Observaciones (opcional)...';

        const submitWithNote = (note) => {
            let obsInput = form.querySelector('input[name="observaciones"]');
            if (!obsInput) {
                obsInput = document.createElement('input');
                obsInput.type = 'hidden';
                obsInput.name = 'observaciones';
                form.appendChild(obsInput);
            }
            obsInput.value = note || '';
            nativeSubmit.call(form);
        };

        if (typeof theBury.confirmActionWithNote === 'function') {
            theBury.confirmActionWithNote(message, submitWithNote, notePlaceholder);
            return;
        }

        submitWithNote('');
    });
})();
