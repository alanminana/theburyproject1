// details-venta.js — Venta Details page interactions
(function () {
    'use strict';

    const theBury = window.TheBury || {};
    const ventaModule = window.VentaModule || {};
    const formConfirmar = document.getElementById('form-confirmar');
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
            afterOpen: function () {
                requestAnimationFrame(function () {
                    inputMotivoAnulacion?.focus();
                });
            },
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

    document.addEventListener('click', function (event) {
        const printTrigger = event.target.closest('[data-venta-action="print"]');
        if (!printTrigger) {
            return;
        }

        event.preventDefault();
        globalThis.print?.();
    });

    if (formConfirmar) {
        formConfirmar.addEventListener('submit', function (event) {
            event.preventDefault();

            const submitVenta = function () {
                formConfirmar.submit();
            };

            if (typeof theBury.confirmAction === 'function') {
                theBury.confirmAction('¿Está seguro de confirmar esta venta? Esta acción no se puede deshacer.', submitVenta);
                return;
            }

            submitVenta();
        });
    }

    if (detalleScrollAffordance && typeof ventaModule.refreshScrollAffordance === 'function') {
        ventaModule.refreshScrollAffordance(detalleScrollAffordance);
    }
})();
