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

    // Table search by concepto
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
