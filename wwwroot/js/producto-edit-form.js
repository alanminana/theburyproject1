(function () {
    'use strict';

    var precioVenta   = document.getElementById('edit-precioVenta');
    var alicuotaIVA   = document.getElementById('edit-alicuotaIVAId');
    var porcentajeIVA = document.getElementById('edit-porcentajeIVA');
    var precioFinal   = document.getElementById('edit-precioFinal');

    if (!precioVenta || !porcentajeIVA || !precioFinal) return;

    function calcular() {
        var pv  = parseFloat(precioVenta.value) || 0;
        var iva = parseFloat(porcentajeIVA.value) || 0;
        precioFinal.value = (pv * (1 + iva / 100)).toFixed(2).replace('.', ',');
    }

    function syncAlicuota() {
        if (alicuotaIVA && alicuotaIVA.value) {
            var opt = alicuotaIVA.options[alicuotaIVA.selectedIndex];
            var pct = opt && opt.getAttribute('data-porcentaje');
            if (pct !== null && pct !== '') {
                porcentajeIVA.value = pct;
            }
        }
        calcular();
    }

    if (alicuotaIVA) alicuotaIVA.addEventListener('change', syncAlicuota);
    precioVenta.addEventListener('input', calcular);
    porcentajeIVA.addEventListener('change', calcular);
    calcular();
})();
