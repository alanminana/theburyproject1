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
