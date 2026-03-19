(function () {
    'use strict';

    // Toast auto-dismiss
    document.querySelectorAll('[id^="toast-"]').forEach(function (toast) {
        setTimeout(function () {
            toast.style.transition = 'opacity 0.5s ease';
            toast.style.opacity = '0';
            setTimeout(function () { toast.remove(); }, 500);
        }, 5000);
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
