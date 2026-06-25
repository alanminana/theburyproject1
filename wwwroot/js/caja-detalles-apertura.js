(function () {
    'use strict';

    TheBury.autoDismissToasts();

    if (typeof TheBury.initHorizontalScrollAffordance === 'function') {
        document.querySelectorAll('[data-oc-scroll]').forEach(function (root) {
            TheBury.initHorizontalScrollAffordance(root);
        });
    }

    document.querySelectorAll('[data-caja-print]').forEach(function (button) {
        button.addEventListener('click', function () {
            window.print();
        });
    });

    var salePaymentFilter = document.querySelector('[data-sale-payment-filter]');
    if (salePaymentFilter) {
        var saleRows = Array.prototype.slice.call(document.querySelectorAll('[data-sale-row]'));
        var saleTotal = document.querySelector('[data-sale-payment-total]');
        var saleEmpty = document.querySelector('[data-sale-filter-empty]');
        var moneyFormatter = new Intl.NumberFormat('es-AR', {
            style: 'currency',
            currency: 'ARS',
            minimumFractionDigits: 2
        });

        function parseAmount(value) {
            var parsed = Number.parseFloat(String(value || '0').replace(',', '.'));
            return Number.isFinite(parsed) ? parsed : 0;
        }

        function applySalePaymentFilter() {
            var filter = salePaymentFilter.value || 'all';
            var total = 0;
            var visible = 0;

            saleRows.forEach(function (row) {
                var matches = filter === 'all' || row.dataset.salePay === filter;
                row.hidden = !matches;
                if (matches) {
                    total += parseAmount(row.dataset.saleTotal);
                    visible += 1;
                }
            });

            if (saleTotal) {
                saleTotal.textContent = moneyFormatter.format(total);
            }

            if (saleEmpty) {
                saleEmpty.classList.toggle('hidden', visible > 0);
            }
        }

        salePaymentFilter.addEventListener('change', applySalePaymentFilter);
        applySalePaymentFilter();
    }

    document.querySelectorAll('[data-mov-filter]').forEach(function (button) {
        button.addEventListener('click', function () {
            var filter = button.getAttribute('data-mov-filter') || 'all';

            document.querySelectorAll('[data-mov-filter]').forEach(function (tab) {
                tab.classList.toggle('btn-soft', tab === button);
                tab.classList.toggle('btn-ghost', tab !== button);
                tab.setAttribute('aria-pressed', tab === button ? 'true' : 'false');
            });

            document.querySelectorAll('#mov-body tr').forEach(function (row) {
                row.hidden = filter !== 'all' && row.dataset.t !== filter;
            });
        });
    });

    // Compatibilidad con la tabla legacy si alguna vista vieja carga este script.
    var inputBuscar = document.getElementById('buscar-concepto');
    var filtroTipo = document.getElementById('filtro-tipo');
    var tabla = document.getElementById('tabla-movimientos');

    if (inputBuscar && filtroTipo && tabla) {
        var filas = tabla.querySelectorAll('tbody tr');

        function filtrar() {
            var texto = inputBuscar.value.trim().toLowerCase();
            var tipo = filtroTipo.value;

            filas.forEach(function (fila) {
                var concepto = (fila.getAttribute('data-tipo') || '');
                var conceptoTexto = (fila.querySelector('[data-concepto]')?.getAttribute('data-concepto') || '').toLowerCase();
                var coincideTipo = !tipo || concepto === tipo;
                var coincideTexto = !texto || conceptoTexto.includes(texto);
                fila.style.display = (coincideTipo && coincideTexto) ? '' : 'none';
            });
        }

        inputBuscar.addEventListener('input', filtrar);
        filtroTipo.addEventListener('change', filtrar);
    }
})();
