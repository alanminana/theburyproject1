/* cuenta-por-pagar-imputacion.js — ayuda visual de los formularios de imputación (D-03).
   Solo muestra cuánto se imputó frente al monto del pago (o al saldo de la nota de crédito) y completa el saldo de una
   fila. NO decide nada: las reglas (no sobreimputar, suma = monto, saldos) las valida y aplica el servidor. */
(function () {
    'use strict';

    var formateador = new Intl.NumberFormat('es-AR', { style: 'currency', currency: 'ARS' });

    function numero(valor) {
        var n = parseFloat(valor);
        return isFinite(n) ? n : 0;
    }

    function centavos(valor) { return Math.round(valor * 100); }

    function iniciar(form) {
        var campos = Array.prototype.slice.call(form.querySelectorAll('[data-cxp-imputar]'));
        var montoPago = form.querySelector('[data-cxp-monto-pago]');
        var limite = form.querySelector('[data-cxp-limite]');
        var total = form.querySelector('[data-cxp-total]');
        var detalle = form.querySelector('[data-cxp-diferencia]');
        if (!total || !detalle || !campos.length) return;

        function actualizar() {
            var suma = campos.reduce(function (acc, c) { return acc + centavos(numero(c.value)); }, 0);
            total.textContent = formateador.format(suma / 100);

            if (montoPago) {
                var objetivo = centavos(numero(montoPago.value));
                if (!objetivo) {
                    detalle.textContent = 'Ingresá el monto del pago.';
                } else if (suma === objetivo) {
                    detalle.textContent = 'Coincide con el monto del pago.';
                } else if (suma < objetivo) {
                    detalle.textContent = 'Faltan ' + formateador.format((objetivo - suma) / 100) + ' por imputar.';
                } else {
                    detalle.textContent = 'Se imputó ' + formateador.format((suma - objetivo) / 100) + ' de más.';
                }
            } else if (limite) {
                var tope = centavos(numero(limite.getAttribute('data-cxp-limite')));
                detalle.textContent = suma > tope
                    ? 'Supera en ' + formateador.format((suma - tope) / 100) + ' el saldo de la nota de crédito.'
                    : 'Queda sin aplicar ' + formateador.format((tope - suma) / 100) + ' de la nota de crédito.';
            }
        }

        form.addEventListener('input', actualizar);
        form.addEventListener('click', function (e) {
            var boton = e.target.closest('[data-cxp-saldo]');
            if (!boton || !form.contains(boton)) return;
            var campo = boton.parentElement.querySelector('[data-cxp-imputar]');
            if (campo) {
                campo.value = boton.getAttribute('data-cxp-saldo');
                actualizar();
                campo.focus();
            }
        });
        // Doble clic en "Registrar": se deshabilita el botón al enviar (el servidor además es idempotente por formulario).
        form.addEventListener('submit', function () {
            var enviar = form.querySelector('[data-cxp-submit]');
            if (enviar) {
                enviar.setAttribute('aria-disabled', 'true');
                setTimeout(function () { enviar.disabled = true; }, 0);
            }
        });
        actualizar();
    }

    document.addEventListener('DOMContentLoaded', function () {
        Array.prototype.forEach.call(document.querySelectorAll('[data-cxp-imputacion]'), iniciar);
    });
})();
