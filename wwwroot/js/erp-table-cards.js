/* ERP-TABLE-CARDS-01 — acompana a css/erp-table-cards.css.
 *
 * Para cada <table data-erp-cards> copia a las celdas de cada fila la etiqueta
 * (data-label) y el rol (data-card) que declara el <th> de su columna, para que
 * el CSS pueda dibujar la fila como tarjeta en telefono. No mueve ni envuelve
 * nodos: solo escribe atributos, asi que no interfiere con otros scripts.
 * Ademas, mientras la tabla se ve como tarjetas (display distinto de table),
 * restituye los roles ARIA de tabla que el navegador pierde.
 */
(function () {
    'use strict';

    var TABLE_ROLES = { TABLE: 'table', THEAD: 'rowgroup', TBODY: 'rowgroup', TFOOT: 'rowgroup', TR: 'row', TH: 'columnheader', TD: 'cell' };
    var mq = window.matchMedia('(max-width: 639.98px)');
    var tables = [];

    function labelOf(th) {
        var clone = th.cloneNode(true);
        Array.prototype.forEach.call(clone.querySelectorAll('.material-symbols-outlined, [data-sort-icon], [aria-hidden="true"], .sr-only, input, svg'), function (n) { n.remove(); });
        return (th.getAttribute('data-card-label') || clone.textContent).replace(/\s+/g, ' ').trim();
    }

    function roleOf(th, label) {
        var explicit = th.getAttribute('data-card');
        if (explicit) return explicit;
        if (th.querySelector('input[type="checkbox"]')) return 'select';
        if (/^acci[oó]n(es)?$/i.test(label)) return 'actions';
        return '';
    }

    function columnsOf(table) {
        var headRow = table.tHead && table.tHead.rows[0];
        if (!headRow) return [];
        return Array.prototype.map.call(headRow.cells, function (th) {
            var label = labelOf(th);
            return { label: label, role: roleOf(th, label) };
        });
    }

    function hasContent(td) {
        return td.textContent.trim() !== '' || !!td.querySelector('img, svg, input, button, a, select, textarea, .material-symbols-outlined');
    }

    function annotate(table) {
        var cols = columnsOf(table);
        if (!cols.length) return;
        var groups = Array.prototype.slice.call(table.tBodies).concat(table.tFoot ? [table.tFoot] : []);
        groups.forEach(function (tbody) {
            Array.prototype.forEach.call(tbody.rows, function (tr) {
                // Cada celda cae en la columna que le corresponde segun los colspan de las anteriores
                // (una fila de totales puede abarcar varias columnas y aun asi conservar las etiquetas del resto).
                var idx = 0;
                Array.prototype.forEach.call(tr.cells, function (td) {
                    var span = td.colSpan || 1;
                    var col = span === 1 ? cols[idx] : null;
                    idx += span;
                    if (!col) { td.setAttribute('data-card', 'span'); td.removeAttribute('data-label'); return; }
                    var role = hasContent(td) ? col.role : 'empty'; // una celda vacia no dibuja etiqueta ni separador
                    if (role) td.setAttribute('data-card', role); else td.removeAttribute('data-card');
                    var wantsLabel = col.label && !/^(title|actions|select)$/.test(role);
                    if (wantsLabel) { if (td.getAttribute('data-label') !== col.label) td.setAttribute('data-label', col.label); }
                    else td.removeAttribute('data-label');
                });
                if (idx !== cols.length) tr.setAttribute('data-card-full', ''); else tr.removeAttribute('data-card-full');
            });
        });
    }

    function setRoles(table, on) {
        var nodes = [table].concat(Array.prototype.slice.call(table.querySelectorAll('thead, tbody, tfoot, tr, th, td')));
        nodes.forEach(function (n) {
            if (on) n.setAttribute('role', TABLE_ROLES[n.tagName]);
            else n.removeAttribute('role');
        });
    }

    function refresh(table) {
        annotate(table);
        setRoles(table, mq.matches);
    }

    function init(table) {
        var pending = false;
        refresh(table);
        tables.push(table);
        new MutationObserver(function () {
            if (pending) return;
            pending = true;
            window.requestAnimationFrame(function () { pending = false; refresh(table); });
        }).observe(table, { childList: true, subtree: true });
    }

    function boot() {
        Array.prototype.forEach.call(document.querySelectorAll('table[data-erp-cards]'), init);
        var onChange = function () { tables.forEach(function (t) { setRoles(t, mq.matches); }); };
        if (mq.addEventListener) mq.addEventListener('change', onChange); else mq.addListener(onChange);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot); else boot();
})();
