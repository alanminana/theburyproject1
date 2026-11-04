// Reglas de entrada comunes (Argentina): filtran lo que el usuario puede tipear.
// Solo ayudan a la UX: la autoridad es la validacion del servidor (Validation/ArgentinaValidationAttributes.cs).
// Uso: data-rule="nombre" | "lugar" (como nombre pero admite digitos: "9 de Julio") | "dni" | "documento" | "telefono" | "monto" | "none" en el input.
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

    function sanitizeNombre(value, maxWords, maxWord, conDigitos) {
        var v = String(value || '')
            .replace(conDigitos ? /[^\p{L}\p{M}\d\s'’.\-]/gu : /[^\p{L}\p{M}\s'’.\-]/gu, '')
            .replace(/^[\s'’.\-]+/, '')
            .replace(/\s{2,}/g, ' ');
        // Un tramo = letras seguidas (y digitos si se admiten); se recorta cada uno al maximo de letras por palabra.
        v = v.replace(conDigitos ? /[\p{L}\p{M}\d]+/gu : /[\p{L}\p{M}]+/gu, function (t) { return limitarLetras(t, maxWord); });
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
            if (/[\p{M}\d]/u.test(token[i])) { out += token[i]; continue; }
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
    // Valor numérico de un monto escrito en formato es-AR o anglosajón, o NaN. Con ambos separadores el último es el
    // decimal; con un solo tipo repetido es de miles ("1.234.567"); con uno solo y tres dígitos detrás ("1.500") es
    // ambiguo y se lee como miles (el mayor valor posible): el servidor lo rechaza con un mensaje claro (FIN-S-017).
    function montoANumero(v) {
        var tienePunto = v.indexOf('.') !== -1, tieneComa = v.indexOf(',') !== -1;
        var n;
        if (tienePunto && tieneComa) {
            var decimal = v.lastIndexOf(',') > v.lastIndexOf('.') ? ',' : '.';
            var miles = decimal === ',' ? /\./g : /,/g;
            n = v.replace(miles, '').replace(decimal, '.');
        } else if (tienePunto || tieneComa) {
            var sep = tienePunto ? '.' : ',';
            var partes = v.split(sep);
            var unico = partes.length === 2;
            var comoMiles = !unico || partes[1].length === 3;
            n = comoMiles ? partes.join('') : partes[0] + '.' + partes[1];
        } else {
            n = v;
        }
        return parseFloat(n);
    }

    // Antes el primer separador era siempre el decimal y se recortaba a 2 cifras: escribir "250.000" (miles con punto)
    // dejaba "250.00", un monto mil veces menor sin aviso. Ahora se conservan los separadores de miles y sólo se
    // recortan los decimales cuando de verdad lo son (un único separador con 1, 2 o más de 3 dígitos detrás, o la coma/
    // punto final cuando hay ambos tipos).
    function sanitizeMontoTexto(value, previo) {
        var v = String(value || '').replace(/[^\d.,]/g, '');
        // Formato anglosajón con ambos separadores ("250,000.00"): el servidor lo lee con el MISMO contrato (el último es
        // el decimal), pero se lleva a es-AR acá para que lo escrito y lo guardado coincidan a simple vista.
        if (v.indexOf('.') !== -1 && v.indexOf(',') !== -1 && v.lastIndexOf('.') > v.lastIndexOf(',')) {
            v = v.replace(/,/g, '').replace('.', ',');
        }
        var ultimo = Math.max(v.lastIndexOf('.'), v.lastIndexOf(','));
        if (ultimo !== -1) {
            var cola = v.slice(ultimo + 1);
            var tienePunto = v.indexOf('.') !== -1, tieneComa = v.indexOf(',') !== -1;
            var sepUnico = !(tienePunto && tieneComa) && (v.match(/[.,]/g) || []).length === 1;
            var decimalSeguro = (tienePunto && tieneComa) || (sepUnico && cola.length !== 3);
            if (decimalSeguro && cola.length > 2) v = v.slice(0, ultimo + 1) + cola.slice(0, 2);
        }
        if (montoANumero(v) > MONTO_MAX) return previo || '';
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

        if (rule === 'nombre' || rule === 'lugar') {
            var words = parseInt(el.getAttribute('data-max-words'), 10) || NOMBRE_MAX_PALABRAS;
            var maxWord = parseInt(el.getAttribute('data-max-word'), 10) || NOMBRE_MAX_PALABRA;
            apply(el, sanitizeNombre(el.value, words, maxWord, rule === 'lugar'));
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
        if (rule === 'nombre' || rule === 'lugar') {
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
        var r = ruleOf(el);
        if ((r === 'nombre' || r === 'lugar') && el.value !== el.value.trim()) {
            el.value = el.value.trim();
            el.dispatchEvent(new Event('change', { bubbles: true }));
        }
    }, true);

    document.addEventListener('input', onInput, true);
    document.addEventListener('focusin', onFocusIn, true);
})();
