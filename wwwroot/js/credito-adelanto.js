/* credito-adelanto.js — La aritmética del adelanto pertenece al servidor.
   El adelanto no tiene importe editable: siempre cancela el total autoritativo
   (capital pendiente + punitorio aplicado pendiente) que calcula el servidor. */

document.addEventListener('DOMContentLoaded', function () {
    'use strict';

    var root = document.querySelector('[data-credito-adelanto]');
    if (!root) return;

    var form = root.querySelector('[data-credito-adelanto-form]');
    var medio = root.querySelector('[data-adelanto-medio]');
    var confirmar = root.querySelector('[data-adelanto-confirmar]');
    var estado = root.querySelector('[data-adelanto-preview-status]');
    var rowVersion = root.querySelector('[data-adelanto-rowversion]');
    if (!form || !medio || !confirmar || !estado) return;

    var endpoint = form.getAttribute('data-adelanto-preview-url');
    var previewValida = form.getAttribute('data-preview-valid') === 'true';
    var resultadoPresente = form.getAttribute('data-result-present') === 'true';
    var solicitudActual = null;
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

    function formatearFechaIso(valor) {
        var partes = String(valor || '').split('-');
        return partes.length === 3 ? partes[2] + '/' + partes[1] + '/' + partes[0] : valor;
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

    function mensajeError(payload, fallback) {
        if (payload && payload.message) return payload.message;
        if (payload && payload.errors) {
            var claves = Object.keys(payload.errors);
            if (claves.length && payload.errors[claves[0]].length) return payload.errors[claves[0]][0];
        }
        return fallback;
    }

    function aplicarPreview(payload) {
        escribirCampo('aplicadoCapital', payload.aplicadoCapital, true);
        escribirCampo('aplicadoPunitorio', payload.aplicadoPunitorio, true);
        escribirCampo('recargoMedioPago', payload.recargoMedioPago, true);
        escribirCampo('totalCaja', payload.totalCaja, true);
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
        if (!endpoint || !medio.value) {
            invalidarPreview('Seleccioná un medio de pago.');
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

    medio.addEventListener('change', solicitarPreview);

    form.addEventListener('submit', function (event) {
        if (enviando || !previewValida) {
            event.preventDefault();
            estado.textContent = enviando
                ? 'El adelanto ya se está enviando.'
                : 'Esperá una previsualización válida antes de confirmar.';
            return;
        }
        enviando = true;
        confirmar.disabled = true;
        confirmar.setAttribute('aria-disabled', 'true');
        estado.textContent = 'Registrando adelanto…';
    });

    actualizarConfirmacion();

    // El resultado ya viene calculado por el servidor en el GET inicial (mismo patrón que
    // credito-pagar-cuota.js): sólo se refetch al cambiar el medio de pago. Si no hay resultado
    // previo ni preview server-side, se pide una automáticamente al cargar la pantalla.
    if (!resultadoPresente && !previewValida) {
        solicitarPreview();
    }

    var primerError = root.querySelector('.field-validation-error, .validation-summary-errors');
    if (primerError) primerError.focus({ preventScroll: false });
});
