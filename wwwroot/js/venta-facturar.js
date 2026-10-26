(function () {
    'use strict';

    const form = document.querySelector('[data-facturar-form]');
    const submit = document.querySelector('[data-facturar-submit]');
    if (!form || !submit) return;

    form.addEventListener('submit', function (e) {
        // La validación del navegador (jquery-validate) puede cancelar el envío después de este handler:
        // se espera a que termine el despacho del evento y solo entonces se bloquea el botón. Si el envío
        // quedó cancelado, el botón sigue activo para corregir el campo y reintentar.
        setTimeout(function () {
            if (e.defaultPrevented) return;
            submit.disabled = true;
            submit.innerHTML = '<span class="material-symbols-outlined animate-spin text-[20px]">progress_activity</span> Emitiendo factura...';
        }, 0);
    });
})();
