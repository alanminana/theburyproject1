/*
 * csp-actions.js — acciones declarativas por data-* que reemplazan los handlers inline (onclick=, onsubmit=, …),
 * para poder servir una Content-Security-Policy sin 'unsafe-inline' en script-src-attr (D-05).
 * Sin eval ni new Function: solo llama funciones globales existentes por nombre. La autorización sigue en el servidor.
 *
 *  data-action="print | history-back | stop | remove-closest | click-id | toggle-hide | submit-closest-form | call | call-self"
 *  data-confirm="mensaje"            en <form> (submit) o en botón/enlace (click): si se cancela, no se ejecuta nada más.
 *  data-submit-fn="nombreFn"         en <form>: fn(form) === false cancela el envío.
 *  data-input-fn / data-change-fn    en inputs/selects: llama fn() en input / change.
 *  data-key-fn + data-keys="Enter, " en cualquier elemento: llama fn(args) ante esas teclas (previene el default).
 *  data-fn + data-args='[...]'       argumentos JSON; el literal "$el" se reemplaza por el elemento.
 */
(function () {
    'use strict';

    function args(el) {
        var raw = el.getAttribute('data-args');
        if (!raw) return [];
        try {
            var list = JSON.parse(raw);
            return Array.isArray(list) ? list.map(function (a) { return a === '$el' ? el : a; }) : [];
        } catch (e) { return []; }
    }

    function callFn(el, name, extra) {
        var fn = name ? window[name] : null;
        if (typeof fn !== 'function') return undefined;
        return fn.apply(window, extra || args(el));
    }

    function run(el, ev) {
        var action = el.getAttribute('data-action');
        switch (action) {
            case 'print': window.print(); break;
            case 'history-back': ev.preventDefault(); history.back(); break;
            case 'stop': return 'stop';
            case 'remove-closest':
                var c = el.closest(el.getAttribute('data-target') || 'div');
                if (c) c.remove();
                break;
            case 'click-id':
                var t = document.getElementById(el.getAttribute('data-target-id'));
                if (t) t.click();
                break;
            case 'toggle-hide':
                var a = document.getElementById(el.getAttribute('data-toggle'));
                var b = document.getElementById(el.getAttribute('data-hide'));
                if (a) a.classList.toggle('hidden');
                if (b) b.classList.add('hidden');
                break;
            case 'submit-closest-form':
                var row = el.closest(el.getAttribute('data-closest') || 'tr');
                var f = row && row.querySelector(el.getAttribute('data-form'));
                if (f && f.requestSubmit) f.requestSubmit();
                break;
            case 'call': callFn(el, el.getAttribute('data-fn')); break;
            case 'call-self':
                if (ev.target === el) callFn(el, el.getAttribute('data-fn'));
                break;
        }
        if (el.hasAttribute('data-stop')) return 'stop';
        return undefined;
    }

    // Confirmación previa (captura: corre antes que los demás listeners; cancelar detiene todo, como un "return false" real).
    document.addEventListener('click', function (ev) {
        var el = ev.target.closest ? ev.target.closest('[data-confirm]:not(form)') : null;
        if (el && !window.confirm(el.getAttribute('data-confirm'))) {
            ev.preventDefault();
            ev.stopImmediatePropagation();
        }
    }, true);

    document.addEventListener('submit', function (ev) {
        var form = ev.target;
        if (!form || !form.getAttribute) return;
        var msg = form.getAttribute('data-confirm');
        if (msg && !window.confirm(msg)) {
            ev.preventDefault();
            ev.stopImmediatePropagation();
            return;
        }
        var fnName = form.getAttribute('data-submit-fn');
        if (fnName && callFn(form, fnName, [form]) === false) {
            ev.preventDefault();
            ev.stopImmediatePropagation();
        }
    }, true);

    // Acciones por click: de adentro hacia afuera, igual que la propagación de un onclick inline.
    document.addEventListener('click', function (ev) {
        var el = ev.target.closest ? ev.target.closest('[data-action]') : null;
        while (el) {
            if (run(el, ev) === 'stop') { ev.stopPropagation(); return; }
            el = el.parentElement ? el.parentElement.closest('[data-action]') : null;
        }
    });

    document.addEventListener('input', function (ev) {
        var el = ev.target.closest ? ev.target.closest('[data-input-fn]') : null;
        if (el) callFn(el, el.getAttribute('data-input-fn'));
    });

    document.addEventListener('change', function (ev) {
        var el = ev.target.closest ? ev.target.closest('[data-change-fn]') : null;
        if (el) callFn(el, el.getAttribute('data-change-fn'));
        var op = ev.target.closest ? ev.target.closest('[data-change-next-opacity]') : null;
        if (op && op.nextElementSibling) op.nextElementSibling.style.opacity = op.checked ? '1' : '0';
    });

    document.addEventListener('keydown', function (ev) {
        var el = ev.target.closest ? ev.target.closest('[data-key-fn]') : null;
        if (!el) return;
        var keys = (el.getAttribute('data-keys') || 'Enter').split(',');
        if (keys.indexOf(ev.key) === -1) return;
        ev.preventDefault();
        callFn(el, el.getAttribute('data-key-fn'));
    });
})();
