/* ficha-inventario.js — pestañas de la ficha (con teclado) y confirmación del alta de unidades en lote. */
(function () {
    'use strict';

    var raiz = document.querySelector('[data-ficha-inventario]');
    if (!raiz) return;

    var botones = Array.prototype.slice.call(raiz.querySelectorAll('[data-fi-tab]'));
    var paneles = Array.prototype.slice.call(raiz.querySelectorAll('[data-fi-panel]'));

    function activar(nombre) {
        botones.forEach(function (boton) {
            var activo = boton.getAttribute('data-fi-tab') === nombre;
            boton.setAttribute('aria-selected', String(activo));
            boton.tabIndex = activo ? 0 : -1;
        });
        paneles.forEach(function (panel) {
            panel.hidden = panel.getAttribute('data-fi-panel') !== nombre;
        });
        // La pestaña queda en la dirección: recargar o volver atrás no la pierde.
        var url = new URL(window.location.href);
        url.searchParams.set('tab', nombre);
        window.history.replaceState(null, '', url);
    }

    botones.forEach(function (boton, indice) {
        boton.addEventListener('click', function () {
            activar(boton.getAttribute('data-fi-tab'));
        });
        boton.addEventListener('keydown', function (evento) {
            var destino = {
                ArrowRight: (indice + 1) % botones.length,
                ArrowLeft: (indice - 1 + botones.length) % botones.length,
                Home: 0,
                End: botones.length - 1
            }[evento.key];
            if (destino === undefined) return;
            evento.preventDefault();
            botones[destino].focus();
            activar(botones[destino].getAttribute('data-fi-tab'));
        });
    });

    // Alta de unidades en lote: se confirma solo a partir de 2 (una unidad no agrega fricción).
    raiz.querySelectorAll('form[data-uf-form]').forEach(function (formulario) {
        var cantidad = formulario.querySelector('#fi-uf-cantidad');
        function actualizar() {
            var n = parseInt(cantidad.value, 10);
            if (!n || n < 2) {
                formulario.removeAttribute('data-inv-confirm');
                return;
            }
            formulario.setAttribute('data-inv-confirm', JSON.stringify({
                titulo: '¿Crear ' + n + ' unidades físicas?',
                datos: [['Producto', formulario.dataset.producto], ['Unidades a crear', String(n)]],
                consecuencias: ['Cada una queda con su código interno.', 'No se pueden borrar en lote desde esta pantalla.', 'El stock del producto no cambia.'],
                etiquetaConfirmar: 'Crear ' + n + ' unidades',
                tono: 'primario'
            }));
        }
        cantidad.addEventListener('input', actualizar);
        actualizar();
    });
})();
