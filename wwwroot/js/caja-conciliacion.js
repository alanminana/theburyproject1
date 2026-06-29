/*
 * Detalle de caja (apertura live + cierre read-only): tabs, filtros internos por tab y
 * calculadora de conteo físico. Toda la aritmética sensible la resuelve el backend
 * (CajaConciliacionBuilder); acá solo hay interacción de UI y una calculadora de verificación.
 */
(function () {
    'use strict';

    var TB = window.TheBury || {};

    if (typeof TB.autoDismissToasts === 'function') {
        TB.autoDismissToasts();
    }

    var money = new Intl.NumberFormat('es-AR', {
        style: 'currency',
        currency: 'ARS',
        minimumFractionDigits: 2
    });

    function parseNum(value) {
        var n = Number.parseFloat(String(value == null ? '' : value).replace(',', '.'));
        return Number.isFinite(n) ? n : 0;
    }

    // ── Horizontal scroll affordance (lazy: las tablas en tabs ocultos miden mal hasta mostrarse) ──
    var scrollInited = new WeakSet();
    function initScroll(scope) {
        if (typeof TB.initHorizontalScrollAffordance !== 'function') return;
        (scope || document).querySelectorAll('[data-oc-scroll]').forEach(function (root) {
            if (scrollInited.has(root)) return;
            scrollInited.add(root);
            TB.initHorizontalScrollAffordance(root);
        });
    }

    // ── Print ──
    document.querySelectorAll('[data-caja-print]').forEach(function (btn) {
        btn.addEventListener('click', function () { window.print(); });
    });

    // ── Tabs ──
    document.querySelectorAll('[data-cc-tabs]').forEach(function (wrap) {
        var tabs = Array.prototype.slice.call(wrap.querySelectorAll('[data-cc-tab]'));
        var panels = Array.prototype.slice.call(wrap.querySelectorAll('[data-cc-panel]'));

        function activate(name) {
            tabs.forEach(function (t) {
                var on = t.getAttribute('data-cc-tab') === name;
                t.classList.toggle('is-active', on);
                t.setAttribute('aria-selected', on ? 'true' : 'false');
            });
            panels.forEach(function (p) {
                var on = p.getAttribute('data-cc-panel') === name;
                p.classList.toggle('is-hidden', !on);
                if (on) { initScroll(p); }
            });
        }

        tabs.forEach(function (t) {
            t.addEventListener('click', function () {
                activate(t.getAttribute('data-cc-tab'));
            });
        });

        // Panel visible por defecto (Resumen).
        var current = wrap.querySelector('[data-cc-panel]:not(.is-hidden)');
        if (current) { initScroll(current); }
    });

    // ── Filtro de Ventas ──
    (function () {
        var body = document.querySelector('[data-venta-body]');
        if (!body) return;
        var rows = Array.prototype.slice.call(body.querySelectorAll('[data-venta-row]'));
        var controls = {
            medio: document.querySelector('[data-venta-filter="medio"]'),
            estado: document.querySelector('[data-venta-filter="estado"]'),
            impacta: document.querySelector('[data-venta-filter="impacta"]'),
            cliente: document.querySelector('[data-venta-filter="cliente"]')
        };
        var empty = document.querySelector('[data-venta-empty]');
        var countOut = document.querySelector('[data-venta-count]');
        var totalOut = document.querySelector('[data-venta-total-out]');

        function apply() {
            var fMedio = controls.medio ? controls.medio.value : 'all';
            var fEstado = controls.estado ? controls.estado.value : 'all';
            var fImpacta = controls.impacta ? controls.impacta.value : 'all';
            var fCliente = controls.cliente ? controls.cliente.value.trim().toLowerCase() : '';
            var visible = 0, total = 0;

            rows.forEach(function (row) {
                var ok = (fMedio === 'all' || row.dataset.ventaMedio === fMedio)
                    && (fEstado === 'all' || row.dataset.ventaEstado === fEstado)
                    && (fImpacta === 'all' || row.dataset.ventaImpacta === fImpacta)
                    && (fCliente === '' || (row.dataset.ventaCliente || '').indexOf(fCliente) !== -1);
                row.hidden = !ok;
                if (ok) { visible += 1; total += parseNum(row.dataset.ventaTotal); }
            });

            if (empty) { empty.classList.toggle('hidden', visible > 0); }
            if (countOut) { countOut.textContent = visible; }
            if (totalOut) { totalOut.textContent = money.format(total); }
        }

        Object.keys(controls).forEach(function (k) {
            var el = controls[k];
            if (!el) return;
            el.addEventListener(el.tagName === 'SELECT' ? 'change' : 'input', apply);
        });
        apply();
    })();

    // ── Filtro de Movimientos ──
    (function () {
        var body = document.querySelector('[data-mov-body]');
        if (!body) return;
        var rows = Array.prototype.slice.call(body.querySelectorAll('[data-mov-row]'));
        var tipoBtns = Array.prototype.slice.call(document.querySelectorAll('[data-mov-tipo]'));
        var medioSel = document.querySelector('[data-mov-filter="medio"]');
        var usuarioSel = document.querySelector('[data-mov-filter="usuario"]');
        var empty = document.querySelector('[data-mov-empty]');
        var tipo = 'all';

        function apply() {
            var fMedio = medioSel ? medioSel.value : 'all';
            var fUsuario = usuarioSel ? usuarioSel.value : 'all';
            var visible = 0;
            rows.forEach(function (row) {
                var ok = (tipo === 'all' || row.dataset.movT === tipo)
                    && (fMedio === 'all' || row.dataset.movMedio === fMedio)
                    && (fUsuario === 'all' || row.dataset.movUsuario === fUsuario);
                row.hidden = !ok;
                if (ok) { visible += 1; }
            });
            if (empty) { empty.classList.toggle('hidden', visible > 0); }
        }

        tipoBtns.forEach(function (btn) {
            btn.addEventListener('click', function () {
                tipo = btn.getAttribute('data-mov-tipo') || 'all';
                tipoBtns.forEach(function (b) {
                    var on = b === btn;
                    b.classList.toggle('btn-soft', on);
                    b.classList.toggle('btn-ghost', !on);
                    b.setAttribute('aria-pressed', on ? 'true' : 'false');
                });
                apply();
            });
        });
        if (medioSel) { medioSel.addEventListener('change', apply); }
        if (usuarioSel) { usuarioSel.addEventListener('change', apply); }
        apply();
    })();

    // ── Filtro de Libro mayor ──
    (function () {
        var body = document.querySelector('[data-lm-body]');
        if (!body) return;
        var rows = Array.prototype.slice.call(body.querySelectorAll('[data-lm-row]'));
        var tipoBtns = Array.prototype.slice.call(document.querySelectorAll('[data-lm-tipo]'));
        var refInput = document.querySelector('[data-lm-filter="ref"]');
        var empty = document.querySelector('[data-lm-empty]');
        var tipo = 'all';

        function apply() {
            var fRef = refInput ? refInput.value.trim().toLowerCase() : '';
            var visible = 0;
            rows.forEach(function (row) {
                var ok = (tipo === 'all' || row.dataset.lmImpacta === tipo)
                    && (fRef === '' || (row.dataset.lmRef || '').indexOf(fRef) !== -1);
                row.hidden = !ok;
                if (ok) { visible += 1; }
            });
            if (empty) { empty.classList.toggle('hidden', visible > 0); }
        }

        tipoBtns.forEach(function (btn) {
            btn.addEventListener('click', function () {
                tipo = btn.getAttribute('data-lm-tipo') || 'all';
                tipoBtns.forEach(function (b) {
                    var on = b === btn;
                    b.classList.toggle('btn-soft', on);
                    b.classList.toggle('btn-ghost', !on);
                    b.setAttribute('aria-pressed', on ? 'true' : 'false');
                });
                apply();
            });
        });
        if (refInput) { refInput.addEventListener('input', apply); }
        apply();
    })();

    // ── Calculadora de conteo físico ──
    (function () {
        var box = document.querySelector('[data-conteo]');
        if (!box) return;
        var esperada = parseNum(box.getAttribute('data-conteo-esperada'));
        var qtyInputs = Array.prototype.slice.call(box.querySelectorAll('[data-conteo-qty]'));
        var otrosInput = box.querySelector('[data-conteo-otros]');
        var otrosOut = box.querySelector('[data-conteo-otros-out]');
        var totalOut = box.querySelector('[data-conteo-total]');
        var difOut = box.querySelector('[data-conteo-diferencia]');
        var estadoOut = box.querySelector('[data-conteo-estado]');
        var touched = false;

        function recalc() {
            var total = 0;
            qtyInputs.forEach(function (input) {
                var qty = Math.max(0, Math.floor(parseNum(input.value)));
                var valor = parseNum(input.getAttribute('data-conteo-valor'));
                var subtotal = qty * valor;
                total += subtotal;
                var cell = input.closest('tr') && input.closest('tr').querySelector('[data-conteo-subtotal]');
                if (cell) { cell.textContent = money.format(subtotal); }
            });
            var otros = parseNum(otrosInput ? otrosInput.value : 0);
            total += otros;
            if (otrosOut) { otrosOut.textContent = money.format(otros); }
            if (totalOut) { totalOut.textContent = money.format(total); }

            var dif = total - esperada;
            if (difOut) {
                var sign = dif > 0 ? '+ ' : (dif < 0 ? '− ' : '');
                difOut.textContent = sign + money.format(Math.abs(dif));
                difOut.classList.toggle('text-emerald-400', touched && Math.abs(dif) < 0.005);
                difOut.classList.toggle('text-amber-300', touched && dif > 0.005);
                difOut.classList.toggle('text-rose-400', touched && dif < -0.005);
            }
            if (estadoOut) {
                var chip, label;
                if (!touched) { chip = 'chip-neutral'; label = 'Cargá el conteo para ver la diferencia'; }
                else if (Math.abs(dif) < 0.005) { chip = 'chip-ok'; label = 'Caja correcta'; }
                else if (dif < 0) { chip = 'chip-bad'; label = 'Falta efectivo'; }
                else { chip = 'chip-warn'; label = 'Sobra efectivo'; }
                var badge = document.createElement('span');
                badge.className = 'chip ' + chip;
                badge.textContent = label;
                estadoOut.replaceChildren(badge);
            }
        }

        function onInput() { touched = true; recalc(); }
        qtyInputs.forEach(function (i) { i.addEventListener('input', onInput); });
        if (otrosInput) { otrosInput.addEventListener('input', onInput); }
        recalc();
    })();
})();
