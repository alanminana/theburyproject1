/**
 * csrf-fetch.js — agrega el token antiforgery a todo fetch/XHR mutante hacia el mismo origen.
 * Las APIs (/api/*) y Cliente/ActualizarBcra validan el header RequestVerificationToken (COM-S-010); el token sale del
 * <meta name="csrf-token"> del layout. Si el llamador ya envía uno no vacío, se respeta. No toca GET/HEAD/OPTIONS ni
 * pedidos a otros orígenes, y solo actúa sobre esos endpoints JSON (que no redirigen): un POST a un endpoint MVC que
 * redirija fuera del sitio no debe arrastrar el token. En fetch además se prohíben los redirects (redirect:'error').
 */
(function () {
    'use strict';

    var meta = document.querySelector('meta[name="csrf-token"]');
    var token = meta ? meta.getAttribute('content') : '';
    if (!token) return;

    var HEADER = 'RequestVerificationToken';
    var SEGURO = /^(GET|HEAD|OPTIONS|TRACE)$/i;

    // Solo APIs JSON del propio sitio. Los formularios y demás endpoints MVC llevan su propio token.
    function destinoConToken(url) {
        try {
            var u = new URL(url, window.location.href);
            if (u.origin !== window.location.origin) return false;
            var ruta = u.pathname.toLowerCase();
            return ruta.indexOf('/api/') === 0 || ruta === '/cliente/actualizarbcra';
        } catch (e) { return false; }
    }

    if (typeof window.fetch === 'function') {
        var fetchOriginal = window.fetch;
        window.fetch = function (input, init) {
            try {
                var esRequest = typeof Request !== 'undefined' && input instanceof Request;
                var metodo = (init && init.method) || (esRequest ? input.method : 'GET');
                var url = esRequest ? input.url : String(input);
                if (!SEGURO.test(metodo) && destinoConToken(url)) {
                    var base = (init && init.headers) || (esRequest ? input.headers : undefined);
                    var headers = new Headers(base);
                    if (!headers.get(HEADER)) headers.set(HEADER, token);
                    init = Object.assign({}, init, { headers: headers });
                    if (!init.redirect && !(esRequest && input.redirect !== 'follow')) init.redirect = 'error';
                }
            } catch (e) { /* ante cualquier duda, el pedido sale como estaba */ }
            return fetchOriginal.call(this, input, init);
        };
    }

    if (window.XMLHttpRequest) {
        var proto = window.XMLHttpRequest.prototype;
        var openOriginal = proto.open;
        var sendOriginal = proto.send;
        var setHeaderOriginal = proto.setRequestHeader;

        proto.open = function (metodo, url) {
            this.__csrf = { metodo: String(metodo), mismoOrigen: destinoConToken(String(url)), enviado: false };
            return openOriginal.apply(this, arguments);
        };
        proto.setRequestHeader = function (nombre, valor) {
            if (this.__csrf && String(nombre).toLowerCase() === HEADER.toLowerCase()) {
                if (!valor) return;     // un valor vacío no sirve y, sumado al nuestro, rompería el header
                this.__csrf.enviado = true;
            }
            return setHeaderOriginal.apply(this, arguments);
        };
        proto.send = function () {
            var c = this.__csrf;
            if (c && c.mismoOrigen && !c.enviado && !SEGURO.test(c.metodo)) {
                try { setHeaderOriginal.call(this, HEADER, token); } catch (e) { /* sin open válido */ }
            }
            return sendOriginal.apply(this, arguments);
        };
    }
})();
