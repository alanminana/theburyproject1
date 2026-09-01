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
        }

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                var url = getCancelUrl();
                if (url) window.location.href = url;
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initClienteForm);
    } else {
        initClienteForm();
    }
})();
