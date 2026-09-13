/* credito-pagar-cuota.js — La aritmética del cobro pertenece al servidor. */

document.addEventListener('DOMContentLoaded', function () {
    'use strict';

    var creditoModule = (window.TheBury && window.TheBury.CreditoModule) || {};
    var root = document.querySelector('[data-credito-pago]');
    if (!root) return;

    var form = root.querySelector('[data-credito-pago-form]');
    var monto = root.querySelector('[data-pago-monto]');
    var medio = root.querySelector('[data-pago-medio]');
    var pagarTotal = root.querySelector('[data-pago-total]');
    var confirmar = root.querySelector('[data-pago-confirmar]');
    var estado = root.querySelector('[data-pago-preview-status]');
    var rowVersion = root.querySelector('[data-pago-rowversion]');
    if (!form || !monto || !medio || !confirmar || !estado) return;

    var endpoint = form.getAttribute('data-pago-preview-url');
    var previewValida = form.getAttribute('data-preview-valid') === 'true';
    var resultadoPresente = form.getAttribute('data-result-present') === 'true';
    var solicitudActual = null;
    var temporizador = null;
    var enviando = false;
    var dinero = new Intl.NumberFormat('es-AR', {
        style: 'currency',
        currency: 'ARS',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });

    function escribirCampo(nombre, valor, formatearDinero) {
        var elemento = root.querySelector('[data-preview-field="' + nombre + '"]');
        if (!elemento) return;
        elemento.textContent = formatearDinero ? dinero.format(valor) : valor;
    }

    // Delegado a CreditoModule.formatearFechaIso (credito-module.js), compartido con
    // credito-adelanto.js.
    function formatearFechaIso(valor) {
        return typeof creditoModule.formatearFechaIso === 'function'
            ? creditoModule.formatearFechaIso(valor)
            : valor;
    }

    function actualizarConfirmacion() {
        confirmar.disabled = resultadoPresente || enviando || !previewValida;
    }

    function invalidarPreview(mensaje) {
        previewValida = false;
        form.setAttribute('data-preview-valid', 'false');
        estado.textContent = mensaje || 'Previsualización pendiente.';
        actualizarConfirmacion();
    }

    // Delegado a CreditoModule.mensajeError (credito-module.js), compartido con
    // credito-adelanto.js.
    function mensajeError(payload, fallback) {
        return typeof creditoModule.mensajeError === 'function'
            ? creditoModule.mensajeError(payload, fallback)
            : fallback;
    }

    function aplicarPreview(payload) {
        escribirCampo('importeIngresado', payload.importeIngresado, true);
        escribirCampo('aplicadoPunitorio', payload.aplicadoPunitorio, true);
        escribirCampo('aplicadoCapital', payload.aplicadoCapital, true);
        escribirCampo('excedente', payload.excedente, true);
        escribirCampo('recargoMedioPago', payload.recargoMedioPago, true);
        escribirCampo('totalCaja', payload.totalCaja, true);
        escribirCampo('punitorioRestante', payload.punitorioRestante, true);
        escribirCampo('capitalRestante', payload.capitalRestante, true);
        escribirCampo('estadoEstimadoTexto', payload.estadoEstimadoTexto, false);
        escribirCampo('fechaComercial', formatearFechaIso(payload.fechaComercial), false);

        if (rowVersion && payload.cuotaRowVersionBase64) {
            rowVersion.value = payload.cuotaRowVersionBase64;
        }

        previewValida = true;
        form.setAttribute('data-preview-valid', 'true');
        estado.textContent = 'Previsualización calculada por el servidor.';
        actualizarConfirmacion();
    }

    async function solicitarPreview() {
        if (!endpoint || !monto.value.trim() || !medio.value) {
            invalidarPreview('Completá el importe y el medio de pago.');
            return;
        }

        if (solicitudActual) solicitudActual.abort();
        solicitudActual = new AbortController();
        estado.textContent = 'Calculando previsualización…';

        try {
            var response = await fetch(endpoint, {
                method: 'POST',
                body: new FormData(form),
                credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                signal: solicitudActual.signal
            });
            var payload = await response.json().catch(function () { return null; });
            if (!response.ok) {
                invalidarPreview(mensajeError(payload,
                    response.status === 409
                        ? 'La cuota cambió. Recargá la pantalla antes de continuar.'
                        : 'No se pudo calcular la previsualización.'));
                return;
            }
            aplicarPreview(payload);
        } catch (error) {
            if (error.name !== 'AbortError') {
                invalidarPreview('No se pudo conectar para calcular la previsualización.');
            }
        } finally {
            solicitudActual = null;
        }
    }

    function programarPreview() {
        invalidarPreview('Previsualización pendiente…');
        window.clearTimeout(temporizador);
        temporizador = window.setTimeout(solicitarPreview, 350);
    }

    monto.addEventListener('input', programarPreview);
    monto.addEventListener('blur', function () {
        window.clearTimeout(temporizador);
        solicitarPreview();
    });
    medio.addEventListener('change', solicitarPreview);

    if (pagarTotal) {
        pagarTotal.addEventListener('click', function () {
            monto.value = pagarTotal.getAttribute('data-total-cobrable') || '';
            monto.focus();
            solicitarPreview();
        });
    }

    form.addEventListener('submit', function (event) {
        if (enviando || !previewValida) {
            event.preventDefault();
            estado.textContent = enviando
                ? 'El pago ya se está enviando.'
                : 'Esperá una previsualización válida antes de confirmar.';
            return;
        }
        enviando = true;
        confirmar.disabled = true;
        confirmar.setAttribute('aria-disabled', 'true');
        estado.textContent = 'Registrando pago…';
    });

    actualizarConfirmacion();

    var primerError = root.querySelector('.field-validation-error, .validation-summary-errors');
    if (primerError) primerError.focus({ preventScroll: false });
});
