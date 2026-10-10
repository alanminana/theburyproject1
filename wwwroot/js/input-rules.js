// Reglas de entrada comunes (Argentina): filtran lo que el usuario puede tipear.
// Solo ayudan a la UX: la autoridad es la validacion del servidor (Validation/ArgentinaValidationAttributes.cs).
// Uso: data-rule="nombre" | "dni" | "documento" | "telefono" | "monto" | "none" en el input.
// nombre acepta data-max-words (default 3) y data-max-word (letras por palabra, default 10);
// documento filtra como DNI solo si el select data-tipo="#Selector" vale DNI.
// Los montos tambien se detectan por nombre de campo (precio, costo, monto, sueldo, anticipo, importe,
// gastos); data-rule="none" los excluye. Listeners delegados: funciona en modales y parciales AJAX.
(function () {
    'use strict';

    var MONTO_MAX = 20000000;
    var NOMBRE_MAX_PALABRA = 10;
    var NOMBRE_MAX_PALABRAS = 3;
    var DNI_MAX = 8;
    var TEL_MAX = 20;

    var MONTO_NAME = /(precio|costo|monto|sueldo|anticipo|importe|gastos)/i;
    var NO_MONTO_NAME = /(porcentaje|tasa|descuento|margen|recargo|cuota|cantidad|stock|puntaje|limite|filtro|buscar)/i;

    function ruleOf(el) {
        if (!el || el.tagName !== 'INPUT') return null;
        var explicit = el.getAttribute('data-rule');
        if (explicit) return explicit === 'none' ? null : explicit;
        var type = (el.type || 'text').toLowerCase();
        if (type !== 'text' && type !== 'number' && type !== 'tel') return null;
        var name = (el.name || el.id || '') + '';
        if (name && MONTO_NAME.test(name) && !NO_MONTO_NAME.test(name) && el.getAttribute('data-money') !== 'off') {
            return 'monto';
        }
        return null;
    }

    function sanitizeNombre(value, maxWords, maxWord) {
        var v = String(value || '')
            .replace(/[^\p{L}\p{M}\s'’.\-]/gu, '')
            .replace(/^[\s'’.\-]+/, '')
            .replace(/\s{2,}/g, ' ');
        // Un tramo = letras seguidas; se recorta cada uno al maximo por palabra.
        v = v.replace(/[\p{L}\p{M}]+/gu, function (t) { return limitarLetras(t, maxWord); });
        var parts = v.split(' ');
        if (parts.length > maxWords) {
            // Un unico espacio final se tolera (el usuario esta por escribir); lo que siga se descarta.
            var soloEspacio = parts.length === maxWords + 1 && parts[maxWords] === '';
            if (!soloEspacio) v = parts.slice(0, maxWords).join(' ') + ' ';
        }
        return v;
    }

    function limitarLetras(token, max) {
        var count = 0, out = '';
        for (var i = 0; i < token.length; i++) {
            if (/\p{M}/u.test(token[i])) { out += token[i]; continue; }
            if (count >= max) break;
            count++;
            out += token[i];
        }
        return out;
    }

    function sanitizeTelefono(value) {
        return String(value || '').replace(/[^\d\s+().\-]/g, '').slice(0, TEL_MAX);
    }

    function sanitizeDni(value) {
        return String(value || '').replace(/\D/g, '').slice(0, DNI_MAX);
    }

    // Texto: digitos y un unico separador decimal (, o .), hasta 2 decimales, tope MONTO_MAX.
    function sanitizeMontoTexto(value, previo) {
        var v = String(value || '').replace(/[^\d.,]/g, '');
        var idx = v.search(/[.,]/);
        if (idx !== -1) {
            var ent = v.slice(0, idx);
            var dec = v.slice(idx + 1).replace(/[.,]/g, '').slice(0, 2);
            v = ent + v.charAt(idx) + dec;
        }
        if (parseFloat(v.replace(',', '.')) > MONTO_MAX) return previo || '';
        return v;
    }

    function sanitizeMontoNumber(el) {
        var raw = el.value;
        if (raw === '') return;
        var n = parseFloat(raw);
        var dec = raw.indexOf('.') !== -1 ? raw.split('.')[1].length : 0;
        if (n > MONTO_MAX || n < 0 || dec > 2) {
            el.value = el._irPrev != null ? el._irPrev : '';
        }
    }

    function esTipoDni(el) {
        var sel = el.getAttribute('data-tipo');
        var tipo = sel ? (document.querySelector(sel) || {}).value : 'DNI';
        tipo = String(tipo || 'DNI').trim().toUpperCase();
        return tipo === '' || tipo === 'DNI';
    }

    function apply(el, value) {
        if (el.value === value) return;
        var pos = null;
        try { pos = el.selectionStart; } catch (e) { pos = null; }
        var diff = el.value.length - value.length;
        el.value = value;
        if (pos != null) {
            try { el.setSelectionRange(Math.max(0, pos - diff), Math.max(0, pos - diff)); } catch (e) { /* tipo sin seleccion */ }
        }
    }

    function onInput(e) {
        var el = e.target;
        var rule = ruleOf(el);
        if (!rule) return;

        if (rule === 'nombre') {
            var words = parseInt(el.getAttribute('data-max-words'), 10) || NOMBRE_MAX_PALABRAS;
            var maxWord = parseInt(el.getAttribute('data-max-word'), 10) || NOMBRE_MAX_PALABRA;
            apply(el, sanitizeNombre(el.value, words, maxWord));
        } else if (rule === 'dni') {
            apply(el, sanitizeDni(el.value));
        } else if (rule === 'documento') {
            // Solo cuando el tipo elegido es DNI; otros tipos (CUIL, pasaporte...) no se filtran.
            if (esTipoDni(el)) apply(el, sanitizeDni(el.value));
        } else if (rule === 'telefono') {
            apply(el, sanitizeTelefono(el.value));
        } else if (rule === 'monto') {
            if (el.type === 'number') {
                sanitizeMontoNumber(el);
            } else {
                apply(el, sanitizeMontoTexto(el.value, el._irPrev));
            }
            el._irPrev = el.value;
        }
    }

    // Atributos estaticos utiles (maxlength, inputmode, min/max) al enfocar: cubre inputs dinamicos.
    function onFocusIn(e) {
        var el = e.target;
        var rule = ruleOf(el);
        if (!rule || el._irReady) return;
        el._irReady = true;
        if (rule === 'nombre') {
            el.setAttribute('autocapitalize', 'words');
        } else if (rule === 'dni' || rule === 'documento') {
            if (rule === 'dni' || esTipoDni(el)) el.setAttribute('maxlength', String(DNI_MAX));
            el.setAttribute('inputmode', rule === 'dni' || esTipoDni(el) ? 'numeric' : 'text');
        } else if (rule === 'telefono') {
            el.setAttribute('maxlength', String(TEL_MAX));
            el.setAttribute('inputmode', 'tel');
        } else if (rule === 'monto') {
            el.setAttribute('inputmode', 'decimal');
            if (el.type === 'number') {
                el.setAttribute('max', String(MONTO_MAX));
                if (el.getAttribute('min') == null) el.setAttribute('min', '0');
            }
        }
        el._irPrev = el.value;
    }

    // En montos numericos no tienen sentido el signo ni la notacion cientifica.
    document.addEventListener('keydown', function (e) {
        var el = e.target;
        if (e.ctrlKey || e.metaKey || e.altKey) return;
        if (ruleOf(el) === 'monto' && el.type === 'number' && /^[-+eE]$/.test(e.key)) e.preventDefault();
    }, true);

    // Al salir del campo se quitan espacios sobrantes en los nombres.
    document.addEventListener('focusout', function (e) {
        var el = e.target;
        if (ruleOf(el) === 'nombre' && el.value !== el.value.trim()) {
            el.value = el.value.trim();
            el.dispatchEvent(new Event('change', { bubbles: true }));
        }
    }, true);

    document.addEventListener('input', onInput, true);
    document.addEventListener('focusin', onFocusIn, true);
})();
