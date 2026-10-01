// details-venta.js — Venta Details page interactions
(function () {
    'use strict';

    const theBury = window.TheBury || {};
    const ventaModule = window.VentaModule || {};
    const inputFacturaId = document.getElementById('anular-factura-id');
    const inputMotivoAnulacion = document.getElementById('anular-factura-motivo');
    const labelFacturaNumero = document.getElementById('modal-anular-factura-numero');

    const detalleScrollAffordance = typeof ventaModule.initScrollAffordance === 'function'
        ? ventaModule.initScrollAffordance('#venta-details-scroll')
        : null;

    if (typeof ventaModule.initSharedUi === 'function') {
        ventaModule.initSharedUi();
    } else if (typeof theBury.autoDismissToasts === 'function') {
        theBury.autoDismissToasts();
    }

    if (labelFacturaNumero) {
        labelFacturaNumero.dataset.defaultNumero = labelFacturaNumero.textContent || '';
    }

    if (typeof ventaModule.bindModal === 'function') {
        ventaModule.bindModal('facturar-modal', {
            displayClass: 'flex'
        });

        ventaModule.bindModal('actualizar-envio', {
            displayClass: 'flex'
        });

        ventaModule.bindModal('anular-factura', {
            displayClass: 'flex',
            beforeOpen: function (_modal, trigger) {
                if (trigger) {
                    if (inputFacturaId && trigger.dataset.facturaId) {
                        inputFacturaId.value = trigger.dataset.facturaId;
                    }

                    if (labelFacturaNumero && trigger.dataset.facturaNumero) {
                        labelFacturaNumero.textContent = trigger.dataset.facturaNumero;
                    }
                }
            },
            // El foco inicial lo resuelve bindModal vía [data-modal-initial-focus] en el
            // textarea Motivo (ver Details_tw.cshtml). Un afterOpen local acá competía con
            // ese rAF y perdía la carrera: el foco terminaba en el botón cerrar (H3).
            beforeClose: function () {
                inputMotivoAnulacion?.form?.reset();
                if (inputFacturaId) {
                    inputFacturaId.value = inputFacturaId.defaultValue;
                }
                if (labelFacturaNumero) {
                    labelFacturaNumero.textContent = labelFacturaNumero.dataset.defaultNumero || labelFacturaNumero.textContent;
                }
            }
        });
    }

    // ENVIO-ML5: "Fallido" exige motivo y "Reprogramado" exige nueva fecha (motivo opcional), ver
    // VentaEnvioService.CambiarEstadoAsync — el resto de los estados no pide datos extra.
    const ESTADO_ENVIO_FALLIDO = '5';
    const ESTADO_ENVIO_REPROGRAMADO = '7';
    const selectNuevoEstadoEnvio = document.getElementById('envio-nuevo-estado');
    const bloqueMotivoEnvio = document.getElementById('envio-motivo-bloque');
    const textareaMotivoEnvio = document.getElementById('envio-motivo');
    const labelMotivoEnvio = document.getElementById('envio-motivo-label');
    const bloqueFechaEnvio = document.getElementById('envio-fecha-bloque');
    const inputFechaEnvio = document.getElementById('envio-fecha-programada');

    function actualizarMotivoEnvioVisibilidad() {
        if (!selectNuevoEstadoEnvio || !bloqueMotivoEnvio) return;
        const esFallido = selectNuevoEstadoEnvio.value === ESTADO_ENVIO_FALLIDO;
        const esReprogramado = selectNuevoEstadoEnvio.value === ESTADO_ENVIO_REPROGRAMADO;
        bloqueMotivoEnvio.classList.toggle('hidden', !(esFallido || esReprogramado));
        if (labelMotivoEnvio) {
            labelMotivoEnvio.textContent = esReprogramado
                ? labelMotivoEnvio.dataset.labelReprogramado
                : labelMotivoEnvio.dataset.labelFallido;
        }
        if (textareaMotivoEnvio) {
            textareaMotivoEnvio.required = esFallido;
            if (!esFallido && !esReprogramado) textareaMotivoEnvio.value = '';
        }
        if (bloqueFechaEnvio) {
            bloqueFechaEnvio.classList.toggle('hidden', !esReprogramado);
        }
        if (inputFechaEnvio) {
            inputFechaEnvio.required = esReprogramado;
            if (!esReprogramado) inputFechaEnvio.value = '';
        }
    }

    selectNuevoEstadoEnvio?.addEventListener('change', actualizarMotivoEnvioVisibilidad);
    actualizarMotivoEnvioVisibilidad();

    document.addEventListener('click', function (event) {
        const printTrigger = event.target.closest('[data-venta-action="print"]');
        if (!printTrigger) {
            return;
        }

        event.preventDefault();
        globalThis.print?.();
    });

    if (detalleScrollAffordance && typeof ventaModule.refreshScrollAffordance === 'function') {
        ventaModule.refreshScrollAffordance(detalleScrollAffordance);
    }
})();
