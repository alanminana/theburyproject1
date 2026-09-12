(function () {
    function toNumber(el) {
        if (!el) return 0;
        var raw = String(el.value || '').replace(',', '.');
        var value = parseFloat(raw);
        return Number.isFinite(value) ? value : 0;
    }

    function setMoney(id, value) {
        var el = document.getElementById(id);
        if (el) el.value = (Number.isFinite(value) ? value : 0).toFixed(2);
    }

    function setHidden(id, value) {
        var el = document.getElementById(id);
        if (el) el.value = (Number.isFinite(value) ? value : 0).toFixed(2);
    }

    // Regla comercial: costo real (compra con IVA + gastos) + ganancia = precio final
    // de venta con IVA incluido. El IVA de venta solo se desglosa (neto = final / factor,
    // IVA = final - neto); nunca se vuelve a sumar al precio final.
    function bindPrecioProducto(cfg) {
        var compraConIva = document.getElementById(cfg.compraConIva);
        var ivaCompraPct = document.getElementById(cfg.ivaCompraPct);
        var envio = document.getElementById(cfg.envio);
        var percepciones = document.getElementById(cfg.percepciones);
        var otros = document.getElementById(cfg.otros);
        var margenPct = document.getElementById(cfg.margenPct);
        var montoGanancia = document.getElementById(cfg.montoGanancia);
        var precioFinal = document.getElementById(cfg.precioFinal);
        var alicuotaVenta = document.getElementById(cfg.alicuotaVenta);
        var ivaVentaHidden = document.getElementById(cfg.ivaVentaHidden);

        if (!compraConIva || !precioFinal) return;

        // Alícuota general del sistema: valor inicial del hidden (fallback interno).
        var ivaGeneral = toNumber(ivaVentaHidden) || 21;

        function ivaVentaPercent() {
            var selected = alicuotaVenta?.selectedOptions?.[0];
            var pct = selected ? parseFloat(String(selected.dataset.porcentaje || '').replace(',', '.')) : NaN;
            return Number.isFinite(pct) ? pct : ivaGeneral;
        }

        function syncIvaHidden() {
            if (ivaVentaHidden) ivaVentaHidden.value = String(ivaVentaPercent());
        }

        function renderCompraYCosto() {
            var compraTotal = toNumber(compraConIva);
            var ivaCompra = toNumber(ivaCompraPct);
            var divisorCompra = ivaCompra > 0 ? (1 + ivaCompra / 100) : 1;
            var compraSinIva = compraTotal / divisorCompra;

            setMoney(cfg.compraSinIvaOut, compraSinIva);
            setMoney(cfg.ivaCompraOut, compraTotal - compraSinIva);

            var costoReal = compraTotal + toNumber(envio) + toNumber(percepciones) + toNumber(otros);
            setMoney(cfg.costoRealOut, costoReal);
            setHidden(cfg.costoRealHidden, costoReal);
            return costoReal;
        }

        function renderDesgloseVenta() {
            var final_ = toNumber(precioFinal);
            var pct = ivaVentaPercent();
            var neto = pct > 0 ? final_ / (1 + pct / 100) : final_;
            setMoney(cfg.ventaSinIvaOut, neto);
            setMoney(cfg.ivaVentaOut, final_ - neto);
        }

        // origen: qué campo disparó el cambio, para evitar cálculos circulares.
        // 'costo'       → ganancia % manda: recalcula ganancia $ y precio final.
        // 'ganancia'    → ganancia $ manda: recalcula % y precio final.
        // 'precioFinal' → precio manual manda: recalcula ganancia real ($ y %).
        // 'alicuota'    → solo desglose: el precio final no cambia.
        function calcular(origen) {
            var costoReal = renderCompraYCosto();

            if (origen === 'precioFinal') {
                var gananciaReal = toNumber(precioFinal) - costoReal;
                if (montoGanancia) montoGanancia.value = gananciaReal.toFixed(2);
                if (margenPct) margenPct.value = costoReal > 0 ? ((gananciaReal / costoReal) * 100).toFixed(2) : '0.00';
            } else if (origen === 'ganancia') {
                var gananciaPesos = toNumber(montoGanancia);
                precioFinal.value = (costoReal + gananciaPesos).toFixed(2);
                if (margenPct) margenPct.value = costoReal > 0 ? ((gananciaPesos / costoReal) * 100).toFixed(2) : '0.00';
            } else if (origen !== 'alicuota') {
                var ganancia = costoReal * (toNumber(margenPct) / 100);
                if (montoGanancia) montoGanancia.value = ganancia.toFixed(2);
                precioFinal.value = (costoReal + ganancia).toFixed(2);
            }

            renderDesgloseVenta();
        }

        [compraConIva, ivaCompraPct, envio, percepciones, otros, margenPct].forEach(function (el) {
            if (el) el.addEventListener('input', function () { calcular('costo'); });
            if (el) el.addEventListener('change', function () { calcular('costo'); });
        });

        if (montoGanancia) {
            montoGanancia.addEventListener('input', function () { calcular('ganancia'); });
            montoGanancia.addEventListener('change', function () { calcular('ganancia'); });
        }

        precioFinal.addEventListener('input', function () { calcular('precioFinal'); });
        precioFinal.addEventListener('change', function () { calcular('precioFinal'); });

        if (alicuotaVenta) {
            alicuotaVenta.addEventListener('change', function () {
                syncIvaHidden();
                calcular('alicuota');
            });
        }

        calcular(cfg.initOrigen || 'costo');

        // En edición, el JS externo carga los valores después de abrir el modal.
        // Recalcula varias veces sin bloquear la UI para tomar esos datos.
        // Usa initOrigen ('precioFinal') para derivar la ganancia real desde el
        // precio cargado sin pisarlo.
        if (cfg.modalId) {
            var modal = document.getElementById(cfg.modalId);
            if (modal) {
                // Solo al pasar de oculto a visible: si se re-disparara con el modal ya
                // abierto, recalcular desde el margen redondeado pisaría un precio manual.
                var estabaOculto = modal.classList.contains('hidden');
                new MutationObserver(function () {
                    var ocultoAhora = modal.classList.contains('hidden');
                    if (estabaOculto && !ocultoAhora) {
                        var attempts = 0;
                        var timer = window.setInterval(function () {
                            syncIvaHidden();
                            calcular(cfg.initOrigen || 'costo');
                            attempts += 1;
                            if (attempts >= 8) window.clearInterval(timer);
                        }, 150);
                    }
                    estabaOculto = ocultoAhora;
                }).observe(modal, { attributes: true, attributeFilter: ['class'] });
            }
        }
    }

    // Sin modalId: el reintento de recálculo post-apertura solo aplica al modal de
    // edición (carga valores async). En el alta pisaría un precio final tipeado
    // rápido, porque el origen 'costo' re-deriva el precio desde el margen.
    bindPrecioProducto({
        initOrigen: 'costo',
        compraConIva: 'modal-precioCompra',
        ivaCompraPct: 'modal-porcentajeIVACompra',
        compraSinIvaOut: 'modal-precioCompraSinIva',
        ivaCompraOut: 'modal-ivaCompraImporte',
        envio: 'modal-costoEnvio',
        percepciones: 'modal-percepcionesCompra',
        otros: 'modal-otrosCostosCompra',
        costoRealOut: 'modal-costoRealFinal',
        costoRealHidden: 'modal-precioCompraRealFinal',
        margenPct: 'modal-margenPorcentaje',
        montoGanancia: 'modal-montoGanancia',
        precioFinal: 'modal-precioFinal',
        alicuotaVenta: 'modal-alicuotaIVAId',
        ivaVentaHidden: 'modal-porcentajeIVA',
        ventaSinIvaOut: 'modal-ventaSinIva',
        ivaVentaOut: 'modal-ivaVentaImporte'
    });

    bindPrecioProducto({
        modalId: 'modal-editar-producto',
        initOrigen: 'precioFinal',
        compraConIva: 'prod-edit-precioCompra',
        ivaCompraPct: 'prod-edit-porcentajeIVACompra',
        compraSinIvaOut: 'prod-edit-precioCompraSinIva',
        ivaCompraOut: 'prod-edit-ivaCompraImporte',
        envio: 'prod-edit-costoEnvio',
        percepciones: 'prod-edit-percepcionesCompra',
        otros: 'prod-edit-otrosCostosCompra',
        costoRealOut: 'prod-edit-costoRealFinal',
        costoRealHidden: 'prod-edit-precioCompraRealFinal',
        margenPct: 'prod-edit-margenPorcentaje',
        montoGanancia: 'prod-edit-montoGanancia',
        precioFinal: 'prod-edit-precioFinal',
        alicuotaVenta: 'prod-edit-alicuotaIVAId',
        ivaVentaHidden: 'prod-edit-porcentajeIVA',
        ventaSinIvaOut: 'prod-edit-ventaSinIva',
        ivaVentaOut: 'prod-edit-ivaVentaImporte'
    });
}());
