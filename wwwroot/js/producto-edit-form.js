(function () {
    'use strict';

    var precioFinal     = document.getElementById('edit-precioFinal');
    var alicuotaIVA     = document.getElementById('edit-alicuotaIVAId');
    var porcentajeIVA   = document.getElementById('edit-porcentajeIVA'); // hidden: fallback interno
    var ventaSinIva     = document.getElementById('edit-ventaSinIva');
    var ivaVentaImporte = document.getElementById('edit-ivaVentaImporte');

    if (!precioFinal || !porcentajeIVA) return;

    // Porcentaje configurado del producto al cargar; se usa cuando no hay alícuota seleccionada.
    var ivaFallback = parseFloat(porcentajeIVA.value);
    if (!Number.isFinite(ivaFallback)) ivaFallback = 21;

    function ivaPercent() {
        if (alicuotaIVA && alicuotaIVA.value) {
            var opt = alicuotaIVA.options[alicuotaIVA.selectedIndex];
            var pct = parseFloat(opt && opt.getAttribute('data-porcentaje'));
            if (Number.isFinite(pct)) return pct;
        }
        return ivaFallback;
    }

    // Solo desglose: el precio final con IVA nunca se modifica desde acá.
    function desglosar() {
        var final_ = parseFloat(precioFinal.value) || 0;
        var pct = ivaPercent();
        var neto = pct > 0 ? final_ / (1 + pct / 100) : final_;
        if (ventaSinIva) ventaSinIva.value = neto.toFixed(2).replace('.', ',');
        if (ivaVentaImporte) ivaVentaImporte.value = (final_ - neto).toFixed(2).replace('.', ',');
    }

    function syncAlicuota() {
        porcentajeIVA.value = String(ivaPercent());
        desglosar();
    }

    if (alicuotaIVA) alicuotaIVA.addEventListener('change', syncAlicuota);
    precioFinal.addEventListener('input', desglosar);
    desglosar();
})();
