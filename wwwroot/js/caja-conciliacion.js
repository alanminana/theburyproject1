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

    // ── Tabs: ARIA completo + roving tabindex (mismo patrón que venta-index-rework.js / §5) ──
    document.querySelectorAll('[data-cc-tabs]').forEach(function (wrap) {
        var tabs = Array.prototype.slice.call(wrap.querySelectorAll('[data-cc-tab]'));
        var panels = Array.prototype.slice.call(wrap.querySelectorAll('[data-cc-panel]'));

        function activate(tab) {
            var name = tab.getAttribute('data-cc-tab');
            if (!name) return;

            tabs.forEach(function (t) {
                var on = t === tab;
                t.classList.toggle('is-active', on);
                t.setAttribute('aria-selected', on ? 'true' : 'false');
                t.tabIndex = on ? 0 : -1;
            });
            panels.forEach(function (p) {
                var on = p.getAttribute('data-cc-panel') === name;
                p.classList.toggle('is-hidden', !on);
                if (on) { initScroll(p); }
            });
        }

        function moveFocus(current, direction) {
            if (!tabs.length) return;
            var index = tabs.indexOf(current);
            if (index < 0) return;
            var nextIndex = (index + direction + tabs.length) % tabs.length;
            tabs[nextIndex].focus();
            activate(tabs[nextIndex]);
        }

        tabs.forEach(function (t) {
            t.addEventListener('click', function () { activate(t); });
            t.addEventListener('keydown', function (event) {
                if (event.key === 'ArrowRight') {
                    event.preventDefault();
                    moveFocus(t, 1);
                } else if (event.key === 'ArrowLeft') {
                    event.preventDefault();
                    moveFocus(t, -1);
                } else if (event.key === 'Home') {
                    event.preventDefault();
                    tabs[0].focus();
                    activate(tabs[0]);
                } else if (event.key === 'End') {
                    event.preventDefault();
                    tabs[tabs.length - 1].focus();
                    activate(tabs[tabs.length - 1]);
                }
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
        var groupHeaders = Array.prototype.slice.call(body.querySelectorAll('[data-venta-group]'));
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
            var visibleByGroup = {};

            rows.forEach(function (row) {
                var ok = (fMedio === 'all' || row.dataset.ventaMedio === fMedio)
                    && (fEstado === 'all' || row.dataset.ventaEstado === fEstado)
                    && (fImpacta === 'all' || row.dataset.ventaImpacta === fImpacta)
                    && (fCliente === '' || (row.dataset.ventaCliente || '').indexOf(fCliente) !== -1);
                row.hidden = !ok;
                if (ok) {
                    visible += 1;
                    total += parseNum(row.dataset.ventaTotal);
                    visibleByGroup[row.dataset.ventaCategoria] = true;
                }
            });

            // Encabezado de grupo ("Ventas efectivas" / "Operaciones pendientes"): solo se
            // muestra si le queda al menos una fila visible tras el filtro, para no dejar un
            // título de grupo huérfano sobre una tabla vacía.
            groupHeaders.forEach(function (header) {
                header.hidden = !visibleByGroup[header.dataset.ventaGroup];
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

    // ── Filtro de Libro mayor (incluye lo que antes era el filtro exclusivo del tab
    //    "Movimientos" — tipo/dirección, medio, usuario — fusionado en Conciliación) ──
    (function () {
        var body = document.querySelector('[data-lm-body]');
        if (!body) return;
        var rows = Array.prototype.slice.call(body.querySelectorAll('[data-lm-row]'));
        var dirBtns = Array.prototype.slice.call(document.querySelectorAll('[data-lm-dir]'));
        var impactaBtns = Array.prototype.slice.call(document.querySelectorAll('[data-lm-tipo]'));
        var medioSel = document.querySelector('[data-lm-filter="medio"]');
        var usuarioSel = document.querySelector('[data-lm-filter="usuario"]');
        var refInput = document.querySelector('[data-lm-filter="ref"]');
        var empty = document.querySelector('[data-lm-empty]');
        var dir = 'all';
        var impacta = 'all';

        function apply() {
            var fMedio = medioSel ? medioSel.value : 'all';
            var fUsuario = usuarioSel ? usuarioSel.value : 'all';
            var fRef = refInput ? refInput.value.trim().toLowerCase() : '';
            var visible = 0;

            rows.forEach(function (row) {
                var ok = (dir === 'all' || row.dataset.lmDir === dir)
                    && (impacta === 'all' || row.dataset.lmImpacta === impacta)
                    && (fMedio === 'all' || row.dataset.lmMedio === fMedio)
                    && (fUsuario === 'all' || row.dataset.lmUsuario === fUsuario)
                    && (fRef === '' || (row.dataset.lmRef || '').indexOf(fRef) !== -1);
                row.hidden = !ok;
                if (ok) { visible += 1; }
            });
            if (empty) { empty.classList.toggle('hidden', visible > 0); }
        }

        function bindToggleGroup(btns, onPick) {
            btns.forEach(function (btn) {
                btn.addEventListener('click', function () {
                    onPick(btn);
                    btns.forEach(function (b) {
                        var on = b === btn;
                        b.classList.toggle('btn-soft', on);
                        b.classList.toggle('btn-ghost', !on);
                        b.setAttribute('aria-pressed', on ? 'true' : 'false');
                    });
                    apply();
                });
            });
        }

        bindToggleGroup(dirBtns, function (btn) { dir = btn.getAttribute('data-lm-dir') || 'all'; });
        bindToggleGroup(impactaBtns, function (btn) { impacta = btn.getAttribute('data-lm-tipo') || 'all'; });
        if (medioSel) { medioSel.addEventListener('change', apply); }
        if (usuarioSel) { usuarioSel.addEventListener('change', apply); }
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
