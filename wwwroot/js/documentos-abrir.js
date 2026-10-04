/* Abre como PDF los documentos que acaba de emitir cualquier acción (cobro, confirmación, entrega, reintento…).
   - Páginas/redirecciones: el layout llama a window.TBPDocumentosAbrir(ids) con el aviso de TempData.
   - AJAX (fetch y XMLHttpRequest): se lee el encabezado X-Documentos-Abrir de la respuesta, así ninguna pantalla tiene que
     implementarlo por su cuenta.
   Primero intenta una pestaña nueva; si el navegador la bloquea, muestra el PDF en pantalla con abrir/imprimir/cerrar. */
(function () {
    'use strict';

    var script = document.currentScript;
    var base = script ? script.getAttribute('data-url') : '/Documento/VerVarios';
    var abiertos = {};

    function urlDe(ids) { return base + (base.indexOf('?') >= 0 ? '&' : '?') + 'ids=' + encodeURIComponent(ids); }

    function modal(url) {
        var capa = document.createElement('div');
        capa.setAttribute('role', 'dialog');
        capa.setAttribute('aria-modal', 'true');
        capa.setAttribute('aria-label', 'Documento emitido');
        capa.style.cssText = 'position:fixed;inset:0;z-index:9999;background:rgba(2,6,23,.78);display:flex;align-items:center;justify-content:center;padding:1rem';
        var caja = document.createElement('div');
        caja.style.cssText = 'background:#0f172a;border:1px solid #334155;border-radius:.75rem;width:min(60rem,100%);height:min(92vh,60rem);display:flex;flex-direction:column;overflow:hidden';
        var barra = document.createElement('div');
        barra.style.cssText = 'display:flex;gap:.5rem;align-items:center;padding:.6rem .9rem;border-bottom:1px solid #334155;color:#e2e8f0;font-weight:700';
        var titulo = document.createElement('span');
        titulo.textContent = 'Documento emitido';
        titulo.style.flex = '1';
        var marco = document.createElement('iframe');
        marco.src = url;
        marco.title = 'Documento emitido';
        marco.style.cssText = 'flex:1;border:0;background:#fff';

        function boton(texto, onClick, href) {
            var b = document.createElement(href ? 'a' : 'button');
            b.textContent = texto;
            b.style.cssText = 'color:#e2e8f0;border:1px solid #475569;border-radius:.5rem;padding:.35rem .8rem;font-size:.85rem;background:#1e293b;cursor:pointer;text-decoration:none';
            if (href) { b.href = href; b.target = '_blank'; b.rel = 'noopener'; }
            else { b.type = 'button'; b.addEventListener('click', onClick); }
            return b;
        }

        function cerrar() { capa.remove(); document.removeEventListener('keydown', alTeclear); }
        function alTeclear(ev) { if (ev.key === 'Escape') cerrar(); }

        barra.appendChild(titulo);
        barra.appendChild(boton('Abrir en pestaña nueva', null, url));
        barra.appendChild(boton('Imprimir', function () {
            try { marco.contentWindow.focus(); marco.contentWindow.print(); } catch (e) { window.open(url, '_blank'); }
        }));
        barra.appendChild(boton('Cerrar', cerrar));
        caja.appendChild(barra);
        caja.appendChild(marco);
        capa.appendChild(caja);
        document.addEventListener('keydown', alTeclear);
        document.body.appendChild(capa);
    }

    function abrir(ids) {
        if (!ids || abiertos[ids]) return;
        abiertos[ids] = true;
        var url = urlDe(ids);
        var ventana = null;
        try { ventana = window.open(url, '_blank'); } catch (e) { ventana = null; }
        if (!ventana) modal(url);
    }

    window.TBPDocumentosAbrir = abrir;

    // fetch
    if (typeof window.fetch === 'function') {
        var fetchOriginal = window.fetch;
        window.fetch = function () {
            return fetchOriginal.apply(this, arguments).then(function (respuesta) {
                try { abrir(respuesta.headers.get('X-Documentos-Abrir')); } catch (e) { /* sin encabezado */ }
                return respuesta;
            });
        };
    }

    // XMLHttpRequest (incluye jQuery.ajax)
    if (window.XMLHttpRequest) {
        var envioOriginal = XMLHttpRequest.prototype.send;
        XMLHttpRequest.prototype.send = function () {
            this.addEventListener('load', function () {
                try { abrir(this.getResponseHeader('X-Documentos-Abrir')); } catch (e) { /* sin encabezado */ }
            });
            return envioOriginal.apply(this, arguments);
        };
    }
})();
