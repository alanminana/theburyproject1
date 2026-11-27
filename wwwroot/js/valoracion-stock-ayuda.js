/* Simulador de la ayuda de Valoración de stock (la ventana la maneja erp-ayuda.js).
   El cálculo lo hace el servidor (GET ValoracionStock/Simular, con la regla real); acá solo se muestra. */
(function () {
    'use strict';

    var dialogo = document.getElementById('ayuda-valoracion');
    if (!dialogo) return;

    var campos = ['cantidad1', 'costo1', 'vendidas', 'cantidad2', 'costo2'];
    var moneda = new Intl.NumberFormat('es-AR', { style: 'currency', currency: 'ARS', minimumFractionDigits: 2, maximumFractionDigits: 2 });
    var numero = new Intl.NumberFormat('es-AR');
    var espera = null;
    var pedido = null;

    function campo(nombre) { return document.getElementById('vs-sim-' + nombre); }
    function salida(nombre, texto) { dialogo.querySelector('[data-vs-out="' + nombre + '"]').textContent = texto; }
    function importe(v) { return v === null || v === undefined ? '—' : moneda.format(v); }

    function fallar(texto) {
        var error = document.getElementById('vs-sim-error');
        error.textContent = texto;
        error.hidden = !texto;
        document.getElementById('vs-sim-salida').hidden = !!texto;
    }

    function mostrar(r) {
        fallar('');
        salida('stock', numero.format(r.stock) + (r.stock === 1 ? ' unidad' : ' unidades'));
        ['costoPromedio', 'valorGerencial', 'ultimoCosto', 'valorAUltimoCosto', 'diferencia'].forEach(function (c) {
            salida(c, importe(r[c]));
        });
    }

    function calcular() {
        var parametros = new URLSearchParams();
        var incompleto = campos.some(function (c) {
            var v = campo(c).value.trim();
            parametros.set(c, v);
            return v === '';
        });
        if (pedido) pedido.abort();
        if (incompleto) {
            fallar('Completá todos los datos.');
            return;
        }

        pedido = new AbortController();
        fetch(dialogo.getAttribute('data-vs-simular-url') + '?' + parametros.toString(), { signal: pedido.signal, headers: { Accept: 'application/json' } })
            .then(function (respuesta) {
                return respuesta.json().then(function (cuerpo) {
                    if (!respuesta.ok) throw new Error(cuerpo.error || 'No se pudo calcular.');
                    return cuerpo;
                });
            })
            .then(mostrar)
            .catch(function (error) {
                if (error.name !== 'AbortError') fallar(error.message || 'No se pudo calcular. Recargá la página.');
            });
    }

    campos.forEach(function (c) {
        campo(c).addEventListener('input', function () {
            window.clearTimeout(espera);
            espera = window.setTimeout(calcular, 150);
        });
    });

    // Se calcula al abrir la ayuda, no al cargar la pantalla.
    dialogo.addEventListener('erp-ayuda:abierta', calcular);
})();
