/* cliente-form.js — Lógica para Create_tw / Edit_tw de Cliente */
(function () {
    'use strict';

    function setSectionState(button, expanded) {
        if (!button) return;

        var sectionName = button.getAttribute('data-cliente-section-toggle');
        var content = document.getElementById('content-' + sectionName);
        var chevron = document.getElementById('chevron-' + sectionName);

        if (!content || !chevron) return;

        content.classList.toggle('hidden', !expanded);
        chevron.classList.toggle('rotate-180', !expanded);
        button.setAttribute('aria-expanded', expanded ? 'true' : 'false');
    }

    function validarMontos() {
        var minInput = document.getElementById('montoMinimo');
        var maxInput = document.getElementById('montoMaximo');
        var errorEl = document.getElementById('montoError');

        if (!minInput || !maxInput || !errorEl) return true;

        var min = parseFloat(minInput.value);
        var max = parseFloat(maxInput.value);

        if (!isNaN(min) && !isNaN(max) && min > max) {
            errorEl.classList.remove('hidden');
            maxInput.classList.add('border-red-500');
            return false;
        }

        errorEl.classList.add('hidden');
        maxInput.classList.remove('border-red-500');
        return true;
    }

    function initClienteForm() {
        document.querySelectorAll('[data-cliente-section-toggle]').forEach(function (button) {
            var sectionName = button.getAttribute('data-cliente-section-toggle');
            var content = document.getElementById('content-' + sectionName);
            setSectionState(button, !(content && content.classList.contains('hidden')));
        });

        if (document.body.dataset.clienteFormHandlersBound !== 'true') {
            document.body.dataset.clienteFormHandlersBound = 'true';

            document.addEventListener('click', function (event) {
                var toggleButton = event.target.closest('[data-cliente-section-toggle]');
                if (!toggleButton) return;

                event.preventDefault();
                event.stopPropagation();

                var sectionName = toggleButton.getAttribute('data-cliente-section-toggle');
                var content = document.getElementById('content-' + sectionName);
                var expanded = !!(content && !content.classList.contains('hidden'));
                setSectionState(toggleButton, !expanded);
            }, true);
        }

        var minInput = document.getElementById('montoMinimo');
        var maxInput = document.getElementById('montoMaximo');

        if (minInput) minInput.addEventListener('input', validarMontos);
        if (maxInput) maxInput.addEventListener('input', validarMontos);

        var form = document.getElementById('clienteForm');
        if (!form) return;

        form.addEventListener('submit', function (e) {
            if (validarMontos()) return;

            e.preventDefault();

            var creditoToggle = document.querySelector('[data-cliente-section-toggle="credito"]');
            if (creditoToggle) {
                setSectionState(creditoToggle, true);
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initClienteForm);
    } else {
        initClienteForm();
    }
})();
