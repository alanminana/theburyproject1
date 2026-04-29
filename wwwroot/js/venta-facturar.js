(function () {
    'use strict';

    const form = document.querySelector('[data-facturar-form]');
    const submit = document.querySelector('[data-facturar-submit]');
    if (!form || !submit) return;

    form.addEventListener('submit', function () {
        submit.disabled = true;
        submit.innerHTML = '<span class="material-symbols-outlined animate-spin text-[20px]">progress_activity</span> Emitiendo factura...';
    });
})();
