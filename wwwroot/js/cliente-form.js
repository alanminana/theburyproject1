/* cliente-form.js — Lógica para Create_tw / Edit_tw de Cliente */
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
