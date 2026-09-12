/**
 * producto-modal-validacion.js
 * Helpers de validación compartidos entre producto-crear-modal.js y
 * producto-editar-modal.js: mismo patrón de mostrar/ocultar el summary de
 * errores y de aplicar errores de servidor a los campos del form. Cada modal
 * parametriza los ids/scoping propios; el comportamiento observable de cada
 * uno no cambia respecto de sus implementaciones previas (ver comentarios
 * inline sobre las diferencias que se preservan a propósito).
 */
window.ProductoModalFormUtils = (function () {
    /**
     * @param {string} boxId  id del contenedor del summary de validación
     * @param {string} txtId  id del nodo de texto del summary
     * @param {object} [opts]
     *   requireBoth    – si true, showValidation sólo actúa cuando box Y msg
     *                    existen (comportamiento previo de producto-crear-modal.js).
     *                    Si false/omitido, cada elemento se actualiza de forma
     *                    independiente (comportamiento previo de producto-editar-modal.js).
     *   scrollIntoView – si true, hace scroll al summary al mostrarlo (alta).
     */
    function bindValidation(boxId, txtId, opts) {
        opts = opts || {};

        function showValidation(text) {
            var box = document.getElementById(boxId);
            var msg = document.getElementById(txtId);
            if (opts.requireBoth) {
                if (!box || !msg) return;
                msg.textContent = text;
                box.classList.remove('hidden');
                box.classList.add('flex');
                if (opts.scrollIntoView) box.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            } else {
                if (box) { box.classList.remove('hidden'); box.classList.add('flex'); }
                if (msg) msg.textContent = text;
            }
        }

        function hideValidation() {
            var box = document.getElementById(boxId);
            if (box) { box.classList.add('hidden'); box.classList.remove('flex'); }
        }

        return { showValidation: showValidation, hideValidation: hideValidation };
    }

    /**
     * Aplica errores de servidor (formato { campo: [mensajes] }) a los spans
     * data-valmsg-for y a los inputs correspondientes, y dispara showValidationFn
     * con el resumen concatenado.
     * @param {object} errors
     * @param {function} showValidationFn
     * @param {string|null} scopeSelector  prefijo de scoping (ej. '#form-editar-producto')
     *   o null para buscar sin scope en todo el documento (comportamiento previo
     *   de producto-crear-modal.js, que no scopeaba el querySelector).
     */
    function handleServerErrors(errors, showValidationFn, scopeSelector) {
        var prefix = scopeSelector ? scopeSelector + ' ' : '';
        var messages = [];
        Object.keys(errors).forEach(function (field) {
            var msgs = errors[field];
            msgs.forEach(function (m) { messages.push(m); });
            if (field) {
                var span = document.querySelector(prefix + '[data-valmsg-for="' + field + '"]');
                if (span) { span.textContent = msgs[0]; span.classList.remove('hidden'); }
                var input = document.querySelector(prefix + '[name="' + field + '"]');
                if (input) input.classList.add('border-red-500');
            }
        });
        if (messages.length) showValidationFn(messages.join('. '));
    }

    return { bindValidation: bindValidation, handleServerErrors: handleServerErrors };
})();
