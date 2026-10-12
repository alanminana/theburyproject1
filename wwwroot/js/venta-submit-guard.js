// venta-submit-guard.js — evita el doble envío en formularios POST de Venta (autorizar,
// rechazar, cancelar, eliminar, facturar…) y comunica "en curso" en el botón apretado.
//
// El botón NO se deshabilita: un submitter disabled se excluye del form-data si trae
// name/value (ver el BLOCKER de venta-create.js). Se bloquea el segundo envío con el flag
// del form y se marca el botón con aria-busy + spinner (venta-motion.css).
(function () {
    'use strict';

    document.addEventListener('submit', function (event) {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || (form.method || '').toLowerCase() !== 'post') return;

        if (form.dataset.vmSubmitting === '1') {
            event.preventDefault();
            return;
        }

        // Otro listener (validación) ya canceló el envío: no hay nada en curso.
        if (event.defaultPrevented) return;

        form.dataset.vmSubmitting = '1';
        const boton = event.submitter;
        if (boton instanceof HTMLElement) {
            boton.classList.add('vm-btn-busy');
            boton.setAttribute('aria-busy', 'true');
        }
    });

    // Volver con "atrás" puede restaurar la página desde bfcache con el envío marcado en curso.
    window.addEventListener('pageshow', function (event) {
        if (!event.persisted) return;
        document.querySelectorAll('form[data-vm-submitting]').forEach(function (form) {
            delete form.dataset.vmSubmitting;
        });
        document.querySelectorAll('.vm-btn-busy').forEach(function (boton) {
            boton.classList.remove('vm-btn-busy');
            boton.removeAttribute('aria-busy');
        });
    });
})();
