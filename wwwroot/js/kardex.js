/* kardex.js — filtros en pantalla del Kardex: oculta filas y recalcula los totales de las visibles. */
(function () {
    'use strict';

    var tabla = document.getElementById('kdx-table');
    var filtros = document.getElementById('kdx-filtros');
    if (!tabla || !filtros) return;

    var filas = Array.prototype.slice.call(tabla.querySelectorAll('#kdx-tbody tr:not(#kdx-empty-row)'));
    var valor = function (id) { return document.getElementById(id).value; };
    var formato = function (n) { return n.toLocaleString('es-AR', { maximumFractionDigits: 4 }); };
    var escribir = function (id, texto) {
        var el = document.getElementById(id);
        if (el) el.textContent = texto;
    };

    function aplicar() {
        var desde = valor('kdx-f-desde');
        var hasta = valor('kdx-f-hasta');
        var tipo = valor('kdx-f-tipo').toLowerCase();
        var busca = valor('kdx-f-busca').toLowerCase().trim();
        var filtrado = !!(desde || hasta || tipo || busca);
        var visibles = 0;
        var entradas = 0;
        var salidas = 0;

        filas.forEach(function (fila) {
            var fecha = fila.getAttribute('data-fecha') || '';
            var pasa = (!desde || fecha >= desde)
                && (!hasta || fecha <= hasta)
                && (!tipo || (fila.getAttribute('data-tipo') || '').toLowerCase() === tipo)
                && (!busca || (fila.getAttribute('data-ref') || '').indexOf(busca) !== -1
                    || (fila.getAttribute('data-motivo') || '').indexOf(busca) !== -1);
            fila.hidden = !pasa;
            if (!pasa) return;
            visibles++;
            entradas += parseFloat(fila.getAttribute('data-ent') || '0') || 0;
            salidas += parseFloat(fila.getAttribute('data-sal') || '0') || 0;
        });

        escribir('kdx-vis-count', String(visibles));
        escribir('kdx-foot-ent', entradas ? '+' + formato(entradas) : '—');
        escribir('kdx-foot-sal', salidas ? '−' + formato(salidas) : '—');
        escribir('kdx-foot-label', filtrado ? 'Totales de las filas visibles' : 'Totales del historial');
        escribir('kdx-filter-info', filtrado ? 'Mostrando ' + visibles + ' de ' + filas.length : '');
        var vacia = document.getElementById('kdx-empty-row');
        if (vacia) vacia.hidden = !(filas.length && visibles === 0);
    }

    filtros.addEventListener('input', aplicar);
    filtros.addEventListener('change', aplicar);
    filtros.addEventListener('submit', function (e) { e.preventDefault(); aplicar(); });
    filtros.addEventListener('reset', function () { window.setTimeout(aplicar, 0); });
    document.addEventListener('click', function (e) {
        if (e.target.closest('[data-kdx-limpiar]')) filtros.reset();
    });
})();
