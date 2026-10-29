/**
 * notificaciones.js — campana del header: contador de no leídas, lista desplegable y actualización en vivo (SignalR).
 * API: GET /api/Notificacion, GET /api/Notificacion/noLeidas/cantidad, POST /api/Notificacion/{id}/marcarLeida,
 * POST /api/Notificacion/marcarTodasLeidas (el token antiforgery lo agrega csrf-fetch.js). El servidor filtra por el
 * usuario autenticado. Todo contenido de la notificación se inserta con textContent.
 */
(function () {
    'use strict';

    var root = document.querySelector('[data-notif-root]');
    var toggle = document.getElementById('btn-open-notificaciones');
    var badge = document.getElementById('notificacionesBadge');
    // Id del panel NO termina en "Menu": shared-ui.js frena en captura todo click dentro de [id$="Menu"] (dropdowns legacy).
    var menu = document.getElementById('notificacionesPanel');
    var lista = document.getElementById('notificacionesLista');
    var noMsg = document.getElementById('noNotificacionesMsg');
    var estado = document.getElementById('notificacionesEstado');
    var markAllBtn = document.getElementById('marcarTodasLeidasBtn');
    var live = document.getElementById('notificacionesLive');
    if (!root || !toggle || !badge || !menu || !lista || !noMsg || !estado) return;

    var API = '/api/Notificacion';
    var puedeActualizar = root.getAttribute('data-notif-can-update') === 'true';
    var items = [];
    var noLeidas = 0;
    var seq = 0;

    function getJson(url) {
        return fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } }).then(function (res) {
            if (!res.ok) throw new Error('http ' + res.status);
            return res.json();
        });
    }

    function post(url) {
        return fetch(url, { method: 'POST', credentials: 'same-origin' }).then(function (res) {
            if (!res.ok) throw new Error('http ' + res.status);
        });
    }

    function pintarContador() {
        var n = Math.max(0, noLeidas);
        badge.textContent = n > 99 ? '99+' : String(n);
        badge.hidden = n === 0;
        toggle.setAttribute('aria-label', n > 0 ? 'Notificaciones, ' + n + ' sin leer' : 'Notificaciones, ninguna sin leer');
        if (markAllBtn) markAllBtn.disabled = n === 0;
    }

    function mostrarEstado(texto) {
        estado.textContent = texto || '';
        estado.hidden = !texto;
    }

    // Solo rutas internas del mismo sitio: una notificación nunca debe sacar al usuario a otro origen ni ejecutar javascript:.
    function destinoSeguro(url) {
        if (!url || typeof url !== 'string') return null;
        try {
            var u = new URL(url, window.location.origin);
            if (u.origin !== window.location.origin) return null;
            return u.pathname + u.search + u.hash;
        } catch (e) { return null; }
    }

    function nodo(tag, cls, texto) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (texto !== undefined) e.textContent = texto;
        return e;
    }

    function pintarLista() {
        lista.textContent = '';
        items.forEach(function (n) {
            var li = nodo('li');
            var btn = nodo('button', 'notif-item');
            btn.type = 'button';
            btn.setAttribute('data-leida', n.leida ? 'true' : 'false');
            btn.setAttribute('data-prioridad', String(n.prioridad));
            var dot = nodo('span', 'notif-dot');
            dot.setAttribute('aria-hidden', 'true');
            var body = nodo('span', 'notif-body');
            body.appendChild(nodo('span', 'notif-item-title', (n.leida ? '' : 'Sin leer: ') + (n.titulo || '')));
            if (n.mensaje) body.appendChild(nodo('span', 'notif-msg', n.mensaje));
            if (n.tiempoTranscurrido) body.appendChild(nodo('span', 'notif-time', n.tiempoTranscurrido));
            btn.appendChild(dot);
            btn.appendChild(body);
            btn.addEventListener('click', function () { abrirNotificacion(n); });
            li.appendChild(btn);
            lista.appendChild(li);
        });
        noMsg.hidden = items.length > 0 || !estado.hidden;
    }

    function cargar() {
        var mi = ++seq;
        mostrarEstado('Cargando notificaciones...');
        noMsg.hidden = true;
        return Promise.all([getJson(API + '?limite=20'), getJson(API + '/noLeidas/cantidad')]).then(function (r) {
            if (mi !== seq) return;
            items = Array.isArray(r[0]) ? r[0] : [];
            noLeidas = (r[1] && typeof r[1].cantidad === 'number') ? r[1].cantidad : 0;
            mostrarEstado('');
            pintarLista();
            pintarContador();
        }).catch(function () {
            if (mi !== seq) return;
            mostrarEstado('No se pudieron cargar las notificaciones. Probá de nuevo en unos instantes.');
            noMsg.hidden = true;
            lista.textContent = '';
        });
    }

    function refrescarContador() {
        return getJson(API + '/noLeidas/cantidad').then(function (r) {
            var nuevo = (r && typeof r.cantidad === 'number') ? r.cantidad : 0;
            var subio = nuevo > noLeidas;
            noLeidas = nuevo;
            pintarContador();
            if (subio && live) live.textContent = 'Tenés ' + nuevo + ' notificaciones sin leer.';
        }).catch(function () { /* el contador queda como estaba */ });
    }

    function marcarLeida(n) {
        if (n.leida || !puedeActualizar || !n.rowVersion) return Promise.resolve();
        return post(API + '/' + encodeURIComponent(n.id) + '/marcarLeida?rowVersion=' + encodeURIComponent(n.rowVersion)).then(function () {
            n.leida = true;
            noLeidas = Math.max(0, noLeidas - 1);
            pintarLista();
            pintarContador();
        });
    }

    function abrirNotificacion(n) {
        var destino = destinoSeguro(n.url);
        var p = marcarLeida(n).catch(function () { /* si falla marcar, igual se navega */ });
        if (destino) {
            p.then(function () { window.location.assign(destino); });
        }
    }

    function setOpen(open) {
        menu.hidden = !open;
        toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        if (open) cargar();
    }

    toggle.addEventListener('click', function () { setOpen(menu.hidden); });
    document.addEventListener('click', function (e) { if (!menu.hidden && !root.contains(e.target)) setOpen(false); });
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && !menu.hidden) { setOpen(false); toggle.focus(); }
    });
    menu.addEventListener('focusout', function (e) {
        if (!menu.hidden && e.relatedTarget && !root.contains(e.relatedTarget)) setOpen(false);
    });

    if (markAllBtn) {
        markAllBtn.addEventListener('click', function () {
            markAllBtn.disabled = true;
            post(API + '/marcarTodasLeidas').then(function () {
                items.forEach(function (n) { n.leida = true; });
                noLeidas = 0;
                pintarLista();
                pintarContador();
                if (live) live.textContent = 'Todas las notificaciones fueron marcadas como leídas.';
            }).catch(function () {
                markAllBtn.disabled = noLeidas === 0;
                mostrarEstado('No se pudieron marcar como leídas.');
            });
        });
    }

    refrescarContador();

    if (typeof signalR === 'undefined') return;
    var connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/notificaciones')
        .withAutomaticReconnect()
        // Sin log propio del cliente: ante una navegación que aborta la negociación escribía console.error aunque el fallo ya se
        // captura abajo (rompía los E2E que exigen consola limpia).
        .configureLogging(signalR.LogLevel.None)
        .build();

    // El servidor emite "NotificacionesActualizadas" (sin payload) cuando cambia algo del usuario.
    connection.on('NotificacionesActualizadas', function () {
        if (menu.hidden) refrescarContador(); else cargar();
    });
    connection.onreconnected(function () { refrescarContador(); });
    connection.start().catch(function (err) {
        console.warn('SignalR: no se pudo conectar al hub de notificaciones.', err);
    });
})();
