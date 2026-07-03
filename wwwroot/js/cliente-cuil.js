/* cliente-cuil.js - Autocompleta CUIL/CUIT desde DNI sin hacerlo obligatorio. */
(function () {
    'use strict';

    var DEFAULT_PREFIX = '20';
    var VALID_PREFIXES = {
        '20': true,
        '23': true,
        '24': true,
        '27': true,
        '30': true,
        '33': true,
        '34': true
    };
    var MULTIPLIERS = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    function onlyDigits(value, maxLength) {
        var digits = String(value || '').replace(/\D/g, '');
        return maxLength ? digits.slice(0, maxLength) : digits;
    }

    function isDniType(tipoInput) {
        var tipo = tipoInput ? String(tipoInput.value || '').trim().toUpperCase() : 'DNI';
        return tipo === '' || tipo === 'DNI';
    }

    function calculateVerifier(firstTenDigits) {
        if (!/^\d{10}$/.test(firstTenDigits)) return '';

        var sum = 0;
        for (var i = 0; i < MULTIPLIERS.length; i += 1) {
            sum += Number(firstTenDigits.charAt(i)) * MULTIPLIERS[i];
        }

        var verifier = 11 - (sum % 11);
        if (verifier === 11) return '0';
        if (verifier === 10) return '9';
        return String(verifier);
    }

    function getSafePrefix(prefix) {
        return VALID_PREFIXES[prefix] ? prefix : DEFAULT_PREFIX;
    }

    function buildCuil(prefix, dniDigits) {
        if (!/^\d{8}$/.test(dniDigits)) return '';

        var base = getSafePrefix(prefix) + dniDigits;
        return base + calculateVerifier(base);
    }

    function dispatchFieldEvents(input) {
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function setInputValue(input, value) {
        if (!input || input.value === value) return;

        input.value = value;
        dispatchFieldEvents(input);
    }

    function syncCuilFromDni(form, force) {
        var tipoInput = form.querySelector('#TipoDocumento');
        var dniInput = form.querySelector('#NumeroDocumento');
        var cuilInput = form.querySelector('#CuilCuit');

        if (!dniInput || !cuilInput || !isDniType(tipoInput)) return;

        var dniDigits = onlyDigits(dniInput.value, 8);
        if (dniInput.value !== dniDigits) {
            dniInput.value = dniDigits;
        }

        if (dniDigits.length !== 8) return;

        var currentCuil = onlyDigits(cuilInput.value, 11);
        var previousDni = cuilInput.dataset.clienteCuilDni || '';
        var currentMiddle = currentCuil.length >= 10 ? currentCuil.slice(2, 10) : '';
        var canReplace = force
            || currentCuil === ''
            || currentCuil.length < 11
            || currentMiddle === previousDni
            || currentMiddle === dniDigits;

        if (!canReplace) return;

        var prefix = currentCuil.length >= 2 ? currentCuil.slice(0, 2) : DEFAULT_PREFIX;
        var nextCuil = buildCuil(prefix, dniDigits);
        cuilInput.dataset.clienteCuilDni = dniDigits;

        if (nextCuil) {
            setInputValue(cuilInput, nextCuil);
        }
    }

    function normalizeManualCuil(form) {
        var dniInput = form.querySelector('#NumeroDocumento');
        var cuilInput = form.querySelector('#CuilCuit');
        if (!dniInput || !cuilInput) return;

        var cuilDigits = onlyDigits(cuilInput.value, 11);
        if (cuilInput.value !== cuilDigits) {
            cuilInput.value = cuilDigits;
        }

        var dniDigits = onlyDigits(dniInput.value, 8);
        if (dniDigits.length !== 8 || cuilDigits.length < 2) return;

        var prefix = cuilDigits.slice(0, 2);
        if (!VALID_PREFIXES[prefix]) return;

        var nextCuil = buildCuil(prefix, dniDigits);
        if (nextCuil && (cuilDigits.length < 11 || cuilDigits.slice(2, 10) === dniDigits)) {
            setInputValue(cuilInput, nextCuil);
        }
    }

    function initForm(form) {
        if (!form || form.dataset.clienteCuilAutocomplete === 'true') return;

        var tipoInput = form.querySelector('#TipoDocumento');
        var dniInput = form.querySelector('#NumeroDocumento');
        var cuilInput = form.querySelector('#CuilCuit');
        if (!dniInput || !cuilInput) return;

        form.dataset.clienteCuilAutocomplete = 'true';
        dniInput.setAttribute('inputmode', 'numeric');
        cuilInput.setAttribute('inputmode', 'numeric');

        dniInput.addEventListener('input', function () {
            syncCuilFromDni(form, true);
        });
        dniInput.addEventListener('change', function () {
            syncCuilFromDni(form, true);
        });

        if (tipoInput) {
            tipoInput.addEventListener('change', function () {
                syncCuilFromDni(form, true);
            });
        }

        cuilInput.addEventListener('input', function () {
            normalizeManualCuil(form);
        });

        form.addEventListener('submit', function () {
            if (isDniType(tipoInput)) {
                dniInput.value = onlyDigits(dniInput.value, 8);
            }
            cuilInput.value = onlyDigits(cuilInput.value, 11);
        });

        syncCuilFromDni(form, false);
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

    window.ClienteCuilAutocomplete = {
        init: init,
        buildCuil: buildCuil
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            init(document);
        });
    } else {
        init(document);
    }
})();
