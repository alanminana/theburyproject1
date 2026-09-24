/* cliente-form.js — Lógica para Create_tw / Edit_tw de Cliente
   (comparte estructura de tabs con cliente-modal.js, que gobierna la misma
   marca _ClienteFormCampos dentro del modal AJAX — ver Cliente/Index) */
(function () {
    'use strict';

    function toggleSection(name, forceOpen) {
        var content = document.getElementById('section-' + name);
        var chevron = document.getElementById('chevron-' + name);
        if (!content || !chevron) {
            activateTab(name);
            return;
        }
        var isOpen = !content.classList.contains('hidden');
        var shouldOpen = forceOpen !== undefined ? forceOpen : !isOpen;
        content.classList.toggle('hidden', !shouldOpen);
        chevron.classList.toggle('rotate-180', !shouldOpen);
    }

    function activateTab(name) {
        var tabId = name === 'credito' || name === 'crediticio' ? 't-credito' : 't-' + name;
        var tab = document.querySelector('[data-cliente-tab="' + tabId + '"]');
        var panel = document.getElementById(tabId);
        if (!tab || !panel) return;

        document.querySelectorAll('#form-tabs .tab').forEach(function (item) {
            item.setAttribute('aria-selected', item === tab ? 'true' : 'false');
        });

        document.querySelectorAll('.tab-panel').forEach(function (item) {
            item.classList.toggle('is-active', item === panel);
        });
    }

    function updatePreview() {
        var apellido = document.getElementById('Apellido');
        var nombre = document.getElementById('Nombre');
        var documento = document.getElementById('NumeroDocumento');
        var ap = apellido ? apellido.value.trim() : '';
        var no = nombre ? nombre.value.trim() : '';
        var nameEl = document.getElementById('pv-name');
        var avatarEl = document.getElementById('pv-avatar');
        var docEl = document.getElementById('pv-doc');

        // Sin nombre/apellido cargado, se deja el texto ya renderizado por
        // Razor (placeholder "Nuevo cliente" en Create, nombre real en Edit)
        // en vez de pisarlo con un literal fijo.
        if (nameEl && (ap || no)) {
            nameEl.textContent = ap + (ap && no ? ', ' : '') + no;
        }
        if (avatarEl && (ap || no)) {
            avatarEl.textContent = ((ap.charAt(0) || '') + (no.charAt(0) || '')).toUpperCase() || '+';
        }
        if (docEl && documento) docEl.textContent = documento.value.trim() || '-';
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

    function getCancelUrl() {
        var cancelLink = document.querySelector('[data-cliente-cancel]');
        return cancelLink ? cancelLink.href : null;
    }

    function initClienteForm() {
        // Accordion toggle (delegated)
        document.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-cliente-section]');
            if (!btn) return;
            toggleSection(btn.getAttribute('data-cliente-section'));
        });

        document.querySelectorAll('[data-cliente-tab]').forEach(function (tab) {
            tab.addEventListener('click', function () {
                activateTab(tab.getAttribute('data-cliente-tab').replace('t-', ''));
            });
        });

        ['Apellido', 'Nombre', 'NumeroDocumento'].forEach(function (id) {
            var el = document.getElementById(id);
            if (el) el.addEventListener('input', updatePreview);
        });

        var minInput = document.getElementById('montoMinimo');
        var maxInput = document.getElementById('montoMaximo');

        if (minInput) minInput.addEventListener('input', validarMontos);
        if (maxInput) maxInput.addEventListener('input', validarMontos);

        var form = document.getElementById('clienteForm');
        if (form) {
            form.addEventListener('submit', function (e) {
                if (!validarMontos()) {
                    e.preventDefault();
                    toggleSection('credito', true);
                }
            });

            iniciarValidacionEntreSolapas(form);
            marcarSolapasConErrores(form);
        }

        // Escape vuelve a la pantalla anterior, pero solo si el formulario sigue intacto:
        // con datos ya escritos (o al cerrar un <select>/selector de fecha con Escape) se
        // perdía todo sin aviso.
        var modificado = false;
        if (form) {
            form.addEventListener('input', function () { modificado = true; });
            form.addEventListener('change', function () { modificado = true; });
        }

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && !modificado) {
                var url = getCancelUrl();
                if (url) window.location.href = url;
            }
        });
    }

    function nombreDeSolapa(panel) {
        return panel.id.replace(/^t-/, '');
    }

    function marcarSolapa(panel) {
        var tab = document.querySelector('[data-cliente-tab="' + panel.id + '"]');
        if (!tab || tab.classList.contains('has-error')) return;
        tab.classList.add('has-error');
        var aviso = document.createElement('span');
        aviso.className = 'sr-only';
        aviso.textContent = ' (con errores)';
        tab.appendChild(aviso);
    }

    // jQuery Validation ignora por defecto los campos ocultos, y las solapas inactivas lo
    // están: Teléfono y Domicilio (obligatorios, en Contacto) no se validaban al enviar desde
    // Personales y el rechazo llegaba recién del servidor, sin ningún error visible. Se validan
    // todas las solapas y, si algo falla, se abre la primera con error, se marcan las que
    // tienen errores y se enfoca el campo.
    function iniciarValidacionEntreSolapas(form) {
        var jq = window.jQuery;
        if (!jq || !jq.validator) return;

        // El validador de unobtrusive puede no haberse creado todavía en este punto del arranque.
        if (jq.validator.unobtrusive && !jq(form).data('validator')) jq.validator.unobtrusive.parse(form);
        var validator = jq(form).data('validator');
        // Fuera: los <input type="hidden"> (Id, RowVersion, etc.) y, en las solapas inactivas,
        // todo lo que no sea [Required]. Los decimales llegan con coma (es-AR: Sueldo="5000000,00")
        // y la regla "number" de jQuery los rechaza: validar eso en solapas ocultas bloqueaba
        // guardar un cliente ya existente. La solapa activa se valida completa y el servidor
        // sigue siendo la autoridad del resto.
        if (validator) validator.settings.ignore = 'input[type="hidden"], .tab-panel:not(.is-active) :not([data-val-required])';

        jq(form).on('invalid-form.validate', function (event, v) {
            v.errorList.forEach(function (item) {
                var panel = item.element.closest('.tab-panel');
                if (panel) marcarSolapa(panel);
            });

            var primero = v.errorList.length ? v.errorList[0].element : null;
            var panelPrimero = primero ? primero.closest('.tab-panel') : null;
            if (panelPrimero) {
                activateTab(nombreDeSolapa(panelPrimero));
                primero.focus();
            }
        });
    }

    // Tras un rechazo del servidor la página se vuelve a renderizar en Personales: se marca
    // cada solapa con errores y se abre la primera.
    function marcarSolapasConErrores(form) {
        var primera = null;
        form.querySelectorAll('.tab-panel').forEach(function (panel) {
            var conError = panel.querySelector('.input-validation-error') ||
                Array.prototype.some.call(panel.querySelectorAll('.field-validation-error'), function (s) {
                    return s.textContent.trim().length > 0;
                });
            if (!conError) return;
            marcarSolapa(panel);
            if (!primera) primera = panel;
        });

        if (primera) {
            activateTab(nombreDeSolapa(primera));
            var campo = primera.querySelector('.input-validation-error');
            if (campo) campo.focus();
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initClienteForm);
    } else {
        initClienteForm();
    }
})();
