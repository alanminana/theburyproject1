/* cliente-cuil.js - CUIL editable por partes: XX-DNI-X.
   El DNI central no se edita (se refleja desde el número de documento) y el prefijo (2) y
   el verificador (1) son los únicos campos editables. Vacíos se guardan como 0 en el backend.
   Este script solo previsualiza/sincroniza; la autoridad del valor guardado es el servidor. */
(function () {
    'use strict';

    function onlyDigits(value, maxLength) {
        var digits = String(value || '').replace(/\D/g, '');
        return maxLength ? digits.slice(0, maxLength) : digits;
    }

    function isDniType(tipoInput) {
        var tipo = tipoInput ? String(tipoInput.value || '').trim().toUpperCase() : 'DNI';
        return tipo === '' || tipo === 'DNI';
    }

    function initForm(form) {
        if (!form || form.dataset.clienteCuilAutocomplete === 'true') return;

        var group = form.querySelector('[data-cuil-group]');
        var prefijoInput = form.querySelector('#CuilPrefijo');
        var verifInput = form.querySelector('#CuilVerificador');
        var dniMirror = form.querySelector('[data-cuil-dni]');
        var dniInput = form.querySelector('#NumeroDocumento');
        var tipoInput = form.querySelector('#TipoDocumento');
        var hint = form.querySelector('[data-cuil-hint]');

        if (!group || !prefijoInput || !verifInput || !dniInput) return;

        form.dataset.clienteCuilAutocomplete = 'true';

        function restrict(input, maxLength) {
            var clean = onlyDigits(input.value, maxLength);
            if (input.value !== clean) input.value = clean;
        }

        function syncDni() {
            var dniDigits = onlyDigits(dniInput.value, 8);
            if (dniMirror) dniMirror.value = dniDigits;

            var faltaDni = isDniType(tipoInput) && dniDigits.length !== 8;
            if (hint) hint.hidden = !faltaDni;
            group.classList.toggle('cuil-group--sin-dni', faltaDni);
        }

        prefijoInput.addEventListener('input', function () { restrict(prefijoInput, 2); });
        verifInput.addEventListener('input', function () { restrict(verifInput, 1); });
        dniInput.addEventListener('input', syncDni);
        dniInput.addEventListener('change', syncDni);
        if (tipoInput) tipoInput.addEventListener('change', syncDni);

        // Defensa al enviar; el backend igual completa con 0 lo que falte.
        form.addEventListener('submit', function () {
            restrict(prefijoInput, 2);
            restrict(verifInput, 1);
        });

        syncDni();
    }

    function init(root) {
        var scope = root || document;
        var forms = [];

        if (scope.matches && scope.matches('form')) {
            forms.push(scope);
        }

        if (scope.querySelectorAll) {
            scope.querySelectorAll('form').forEach(function (form) {
                forms.push(form);
            });
        }

        forms.forEach(initForm);
    }

    window.ClienteCuilAutocomplete = { init: init };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            init(document);
        });
    } else {
        init(document);
    }
})();
