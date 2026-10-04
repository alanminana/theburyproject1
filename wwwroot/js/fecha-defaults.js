/*
 * Reglas globales para inputs de fecha, declaradas con data-fecha:
 *   hoy        -> min = hoy y, si está vacío, se completa con la fecha de hoy.
 *   futura     -> min = hoy, sin completar (campos opcionales como "vence" o "hasta").
 *   nacimiento -> max = hoy, min = hoy - 80 años.
 * Si el input ya trae un valor pasado (edición de un registro histórico) no se impone
 * min, para no bloquear el submit de datos existentes.
 * Aplica a input[type=date] y [type=datetime-local], también a los insertados luego en el DOM.
 */
(function () {
    'use strict';

    var pad = function (n) { return String(n).padStart(2, '0'); };

    function hoyIso(d) { return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()); }

    function aplicar(input) {
        var regla = input.getAttribute('data-fecha');
        if (!regla || input.dataset.fechaAplicada === '1') return;
        input.dataset.fechaAplicada = '1';

        var ahora = new Date();
        var esLocal = input.type === 'datetime-local';
        var hoy = hoyIso(ahora);
        var minimo = esLocal ? hoy + 'T00:00' : hoy;
        var valor = input.value || '';
        // DateTime sin inicializar llega como 0001-01-01.
        if (valor.indexOf('0001') === 0) { valor = ''; input.value = ''; }

        if (regla === 'nacimiento') {
            var limite = new Date(ahora.getFullYear() - 80, ahora.getMonth(), ahora.getDate());
            input.max = hoy;
            input.min = hoyIso(limite);
            return;
        }

        var esPasado = valor !== '' && valor.slice(0, 10) < hoy;
        if (!esPasado) input.min = minimo;

        if (regla === 'hoy' && valor === '' && !input.readOnly && !input.disabled) {
            input.value = esLocal ? hoy + 'T' + pad(ahora.getHours()) + ':' + pad(ahora.getMinutes()) : hoy;
        }
    }

    function barrer(raiz) {
        var lista = (raiz || document).querySelectorAll('input[data-fecha]');
        for (var i = 0; i < lista.length; i++) aplicar(lista[i]);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { barrer(); });
    else barrer();

    new MutationObserver(function (muts) {
        for (var i = 0; i < muts.length; i++) {
            var n = muts[i].addedNodes;
            for (var j = 0; j < n.length; j++) {
                if (n[j].nodeType !== 1) continue;
                if (n[j].matches && n[j].matches('input[data-fecha]')) aplicar(n[j]);
                else if (n[j].querySelectorAll) barrer(n[j]);
            }
        }
    }).observe(document.documentElement, { childList: true, subtree: true });
})();
