/* Ayuda de Listas de precios: iconos de información y simulador (la ventana la maneja erp-ayuda.js).
   El cálculo lo hace el servidor (GET ListaPrecio/Simular, con las reglas reales); acá solo se muestra. Nada se guarda. */
(function () {
    'use strict';

    var dialogo = document.getElementById('lp-ayuda');
    if (!dialogo) return;

    var $ = function (id) { return document.getElementById(id); };

    // «Ver más» de un campo: la ayuda se abre en la pestaña de campos y en ese campo.
    dialogo.addEventListener('erp-ayuda:abierta', function (e) {
        var origen = e.detail.origen;
        var campo = origen && origen.getAttribute('data-lp-campo');
        if (!campo) return;
        $('lp-tab-campos').click();
        var detalle = $('lp-campo-' + campo);
        detalle.open = true;
        detalle.querySelector('summary').focus();
        detalle.scrollIntoView({ block: 'start' });
    });

    // Iconos de información junto a los campos.
    document.addEventListener('click', function (e) {
        var info = e.target.closest('[data-lp-info]');
        if (!info) return;
        var tip = $(info.getAttribute('aria-controls'));
        var mostrar = tip.hidden;
        tip.hidden = !mostrar;
        info.setAttribute('aria-expanded', mostrar ? 'true' : 'false');
    });

    // ── Simulador ──
    var moneda = new Intl.NumberFormat('es-AR', { style: 'currency', currency: 'ARS', minimumFractionDigits: 2 });
    var numero = new Intl.NumberFormat('es-AR', { maximumFractionDigits: 2 });
    var porciento = new Intl.NumberFormat('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    var campos = ['costo', 'margen', 'recargo', 'redondeo', 'minimo', 'prueba'];
    var espera = null;
    var pedido = null;

    function valor(nombre) { return $('lp-sim-' + nombre).value.trim(); }
    function salida(nombre, texto) { dialogo.querySelector('[data-lp-out="' + nombre + '"]').textContent = texto; }
    function conSigno(importe) { return (importe > 0 ? '+ ' : importe < 0 ? '− ' : '') + moneda.format(Math.abs(importe)); }

    function aviso(elemento, tono, texto) {
        elemento.className = 'lp-aviso' + (tono ? ' lp-aviso--' + tono : '');
        elemento.textContent = texto;
        elemento.hidden = !texto;
    }

    function fallar(texto) {
        aviso($('lp-sim-error'), 'error', texto);
        $('lp-sim-salida').hidden = true;
        aviso($('lp-sim-prueba-resultado'), '', '');
    }

    function mostrar(r) {
        aviso($('lp-sim-error'), 'error', '');
        $('lp-sim-salida').hidden = false;

        var producto = $('lp-sim-producto');
        salida('producto', producto.options[producto.selectedIndex].text);
        salida('margenPct', numero.format(Number(valor('margen') || 0)));
        salida('recargoPct', numero.format(Number(valor('recargo') || 0)));
        salida('costo', moneda.format(r.costo));
        salida('margenImporte', moneda.format(r.margenImporte));
        salida('subtotal', moneda.format(r.subtotal));
        salida('recargoImporte', moneda.format(r.recargoImporte));
        salida('ajusteRedondeo', conSigno(r.ajusteRedondeo));
        salida('sugerido', moneda.format(r.sugerido));
        salida('margenSugerido', r.margenSugerido === null ? 'no se calcula sin costo' : porciento.format(r.margenSugerido) + ' %');

        [['costo', r.costo], ['margen', r.margenImporte], ['recargo', r.recargoImporte]].forEach(function (parte) {
            var ancho = r.antesDeRedondeo > 0 ? parte[1] / r.antesDeRedondeo * 100 : 0;
            dialogo.querySelector('[data-lp-barra="' + parte[0] + '"]').style.width = ancho + '%';
        });

        var minimo = valor('minimo');
        var margen = valor('margen');
        var notas = [];
        if (minimo && margen && Number(minimo) > Number(margen))
            notas.push('El ERP no deja guardar una lista con margen mínimo mayor que su margen.');
        if (r.sugeridoBajoMinimo)
            notas.push('Este sugerido queda por debajo del margen mínimo: «Fijar precio» lo rechazaría.');
        if (r.costo > 0 && r.sugerido <= 0)
            notas.push('Con este redondeo el sugerido queda en cero y no se puede fijar.');
        aviso($('lp-sim-aviso-minimo'), '', notas.join(' '));

        var resultado = $('lp-sim-prueba-resultado');
        if (r.precioPrueba === null) {
            aviso(resultado, '', 'Ingresá un precio mayor a cero para probarlo.');
        } else if (r.pruebaConDecimalesDeMas) {
            aviso(resultado, 'error', 'Rechazado: el precio admite hasta dos decimales.');
        } else if (r.costo <= 0) {
            aviso(resultado, 'ok', 'Sin costo cargado, el margen mínimo no se controla.');
        } else if (r.pruebaRechazada) {
            aviso(resultado, 'error', 'Rechazado: ' + moneda.format(r.precioPrueba) + ' tiene un margen de ' + porciento.format(r.margenPrueba) +
                ' % y el mínimo es ' + numero.format(Number(minimo)) + ' %. El precio más bajo que se acepta es ' + moneda.format(r.precioMinimo) + '.');
        } else {
            aviso(resultado, 'ok', 'Se puede fijar: ' + moneda.format(r.precioPrueba) + ' tiene un margen de ' + porciento.format(r.margenPrueba) +
                ' % sobre el costo' + (minimo ? ' (mínimo ' + numero.format(Number(minimo)) + ' %).' : ' (sin margen mínimo).'));
        }
    }

    function calcular() {
        var invalido = campos.some(function (c) { var el = $('lp-sim-' + c); return el.validity && (el.validity.badInput || el.validity.rangeOverflow || el.validity.rangeUnderflow); });
        if (invalido || valor('costo') === '') {
            if (pedido) pedido.abort();
            fallar('Ingresá un costo y revisá los límites: importes hasta 20.000.000 y porcentajes de 0 a 999,99.');
            return;
        }

        var parametros = new URLSearchParams();
        [['costo', 'costo'], ['margen', 'margen'], ['recargo', 'recargo'], ['redondeo', 'redondeo'], ['minimo', 'minimo'], ['prueba', 'precioPrueba']]
            .forEach(function (par) { if (valor(par[0]) !== '') parametros.set(par[1], valor(par[0])); });

        // Solo se muestra la respuesta del último pedido.
        if (pedido) pedido.abort();
        pedido = new AbortController();
        fetch(dialogo.getAttribute('data-lp-simular-url') + '?' + parametros.toString(), { signal: pedido.signal, headers: { Accept: 'application/json' } })
            .then(function (respuesta) {
                if (!respuesta.ok) throw new Error(String(respuesta.status));
                return respuesta.json();
            })
            .then(mostrar)
            .catch(function (error) {
                if (error.name !== 'AbortError') fallar('No se pudo calcular. Revisá los valores o recargá la página.');
            });
    }

    function programar() {
        window.clearTimeout(espera);
        espera = window.setTimeout(calcular, 150);
    }

    campos.forEach(function (c) { $('lp-sim-' + c).addEventListener('input', programar); });
    $('lp-sim-redondeo').addEventListener('change', programar);
    $('lp-sim-producto').addEventListener('change', function () {
        var costo = Number(this.options[this.selectedIndex].getAttribute('data-costo'));
        $('lp-sim-costo').value = costo;
        $('lp-sim-prueba').value = Math.round(costo * 1.1);
        programar();
    });

    // «Usar los valores de esta pantalla»: del formulario de la lista o de los datos que publica la vista.
    var origen = document.querySelector('[data-lp-sim-origen]');
    var formulario = { margen: 'MargenPorcentaje', recargo: 'RecargoPorcentaje', minimo: 'MargenMinimoPorcentaje', redondeo: 'ReglaRedondeo' };
    var usar = $('lp-sim-usar-pantalla');
    if (origen || $(formulario.margen)) {
        usar.hidden = false;
        usar.addEventListener('click', function () {
            ['costo', 'margen', 'recargo', 'minimo', 'redondeo'].forEach(function (c) {
                if (origen) {
                    if (origen.hasAttribute('data-' + c)) $('lp-sim-' + c).value = origen.getAttribute('data-' + c);
                } else if (formulario[c]) {
                    $('lp-sim-' + c).value = $(formulario[c]).value;
                }
            });
            programar();
        });
    }

    calcular();
})();
