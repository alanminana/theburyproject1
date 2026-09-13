// details-venta.js — Venta Details page interactions
(function () {
    'use strict';

    const theBury = window.TheBury || {};
    const ventaModule = window.VentaModule || {};
    const formConfirmar = document.getElementById('form-confirmar');
    // Checkbox "Facturar al confirmar" + modal de tipo de factura (mismo patrón que
    // Venta/Edit): reemplaza al viejo botón directo "Confirmar y facturar" con Tipo B fijo.
    const chkFacturarDetails = document.getElementById('chk-facturar-details');
    const btnConfirmarFacturarDetails = document.getElementById('btn-confirmar-facturar-details');
    let modalConfirmarFacturarDetails = null;
    if (typeof ventaModule.bindModal === 'function') {
        modalConfirmarFacturarDetails = ventaModule.bindModal('confirmar-facturar', {
            displayClass: 'flex'
        });
    }
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

            // "Facturar al confirmar" tildado: no confirmar a ciegas con Tipo B fijo — abrir
            // el modal para elegir el tipo. btnConfirmarFacturarDetails hace su propio submit
            // (form.submit() no dispara este listener), así que acá no hace falta distinguir
            // "de dónde vino el submit" como en el wizard de Edit.
            if (chkFacturarDetails?.checked && modalConfirmarFacturarDetails) {
                modalConfirmarFacturarDetails.open();
                return;
            }

            const submitVenta = function () {
                formConfirmar.submit();
            };

            if (typeof theBury.confirmAction === 'function') {
                theBury.confirmAction('¿Está seguro de confirmar esta venta? Esta acción no se puede deshacer.', submitVenta);
                return;
            }

            submitVenta();
        });

        btnConfirmarFacturarDetails?.addEventListener('click', function () {
            const accionUrl = btnConfirmarFacturarDetails.dataset.actionUrl;
            const submitVenta = function () {
                // form.submit() no dispara 'submit' (evita reabrir este mismo modal en loop) y
                // no respeta formaction de botones — por eso se pisa form.action a mano antes.
                if (accionUrl) {
                    formConfirmar.action = accionUrl;
                }
                formConfirmar.submit();
            };

            if (typeof theBury.confirmAction === 'function') {
                theBury.confirmAction('¿Está seguro de confirmar y facturar esta venta? Esta acción no se puede deshacer.', submitVenta);
                return;
            }

            submitVenta();
        });
    }

    if (detalleScrollAffordance && typeof ventaModule.refreshScrollAffordance === 'function') {
        ventaModule.refreshScrollAffordance(detalleScrollAffordance);
    }
})();
