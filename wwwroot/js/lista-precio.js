/* D-01: confirmación de la baja de una lista de precios. La regla vive en el servidor; esto solo evita un clic accidental. */
(function () {
    'use strict';

    document.querySelectorAll('form[data-lp-baja]').forEach(function (form) {
        form.addEventListener('submit', function (event) {
            var nombre = form.getAttribute('data-lp-nombre') || 'esta lista';
            if (!window.confirm('¿Dar de baja la lista «' + nombre + '»? Sus precios e historial se conservan.')) {
                event.preventDefault();
            }
        });
    });
})();
