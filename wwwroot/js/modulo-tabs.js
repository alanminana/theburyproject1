/* Pestañas de módulo: conserva filtros/búsqueda de cada pestaña al ir y volver, y deja visible la activa.
   Solo se guarda la query de la URL (nunca datos de formularios) y solo para la sesión del navegador. Entrar
   desde el menú lateral abre el módulo sin filtros. */
(function () {
    'use strict';

    var PREFIJO = 'erp:pestana:';
    // Parámetros de un solo uso: no deben reaparecer al volver a la pestaña.
    var EFIMEROS = ['returnUrl', 'volverA', 'returnToVentaId'];

    function leer(clave) {
        try { return sessionStorage.getItem(PREFIJO + clave); } catch (e) { return null; }
    }

    function guardar(clave, valor) {
        try { sessionStorage.setItem(PREFIJO + clave, valor); } catch (e) { /* almacenamiento no disponible */ }
    }

    function consultaActual() {
        var parametros = new URLSearchParams(window.location.search);
        EFIMEROS.forEach(function (p) { parametros.delete(p); });
        return parametros.toString();
    }

    function recordar(clave) {
        guardar(clave, window.location.pathname + (consultaActual() ? '?' + consultaActual() : ''));
    }

    function restaurar(enlace) {
        var clave = enlace.getAttribute('data-modulo-tab');
        var guardada = leer(clave);
        if (!guardada) return;
        var destino = new URL(enlace.href, window.location.origin);
        var previa = new URL(guardada, window.location.origin);
        // Solo se restaura sobre la misma pantalla de destino.
        if (destino.pathname.toLowerCase().replace(/\/index$/, '') !== previa.pathname.toLowerCase().replace(/\/index$/, '')) return;
        if (destino.searchParams.has('tab')) previa.searchParams.set('tab', destino.searchParams.get('tab'));
        enlace.href = previa.pathname + previa.search;
    }

    function marcarOverflow(barra) {
        var izquierda = barra.scrollLeft > 1;
        var derecha = barra.scrollLeft + barra.clientWidth < barra.scrollWidth - 1;
        var oculto = izquierda && derecha ? 'ambos' : izquierda ? 'izquierda' : derecha ? 'derecha' : null;
        if (oculto) barra.setAttribute('data-oculto', oculto);
        else barra.removeAttribute('data-oculto');
    }

    document.querySelectorAll('[data-modulo-tabs]').forEach(function (barra) {
        var enlaces = Array.prototype.slice.call(barra.querySelectorAll('a[data-modulo-tab]'));
        enlaces.forEach(function (enlace) {
            if (enlace.getAttribute('aria-current') === 'page') recordar(enlace.getAttribute('data-modulo-tab'));
            else restaurar(enlace);
        });

        var activa = barra.querySelector('[aria-current="page"]');
        if (activa) barra.scrollLeft = activa.offsetLeft - barra.offsetLeft - (barra.clientWidth - activa.offsetWidth) / 2;
        marcarOverflow(barra);
        barra.addEventListener('scroll', function () { marcarOverflow(barra); }, { passive: true });
        window.addEventListener('resize', function () { marcarOverflow(barra); });
    });

    // Menú «Más»: se cierra al hacer clic afuera o con Escape.
    function cerrarMenus(excepto) {
        document.querySelectorAll('details[data-modulo-mas][open]').forEach(function (menu) {
            if (menu !== excepto) menu.open = false;
        });
    }

    document.addEventListener('click', function (e) {
        cerrarMenus(e.target.closest('details[data-modulo-mas]'));
    });

    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape') return;
        var abierto = document.querySelector('details[data-modulo-mas][open]');
        if (!abierto) return;
        abierto.open = false;
        abierto.querySelector('summary').focus();
    });

    // El catálogo cambia de pestaña sin recargar: se guarda su URL al salir.
    var catalogo = document.getElementById('catalogo-topbar');
    if (catalogo) {
        catalogo.addEventListener('click', function (e) {
            var enlace = e.target.closest('a[data-modulo-tab]');
            if (!enlace) return;
            var actual = new URLSearchParams(window.location.search).get('tab') || 'productos';
            recordar(actual);
        }, true);
    }
})();
