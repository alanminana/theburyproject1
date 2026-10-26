/**
 * notificaciones.js — SignalR notification handling
 */
(function () {
    'use strict';

    if (typeof signalR === 'undefined') return;

    var connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/notificaciones')
        .withAutomaticReconnect()
        // Sin log propio del cliente: ante una navegación que aborta la negociación escribía console.error aunque el fallo ya se
        // captura abajo (rompía los E2E que exigen consola limpia).
        .configureLogging(signalR.LogLevel.None)
        .build();

    var badge = document.getElementById('notificacionesBadge');
    var noMsg = document.getElementById('noNotificacionesMsg');
    var menu  = document.getElementById('notificacionesMenu');
    var markAllBtn = document.getElementById('marcarTodasLeidasBtn');

    connection.on('RecibirNotificacion', function (mensaje) {
        if (badge) {
            var count = parseInt(badge.textContent || '0', 10) + 1;
            badge.textContent = count;
            badge.style.display = '';
        }
        if (noMsg) noMsg.style.display = 'none';
    });

    if (markAllBtn) {
        markAllBtn.addEventListener('click', function () {
            if (badge) {
                badge.textContent = '0';
                badge.style.display = 'none';
            }
            if (noMsg) noMsg.style.display = '';
        });
    }

    connection.start().catch(function (err) {
        console.warn('SignalR: no se pudo conectar al hub de notificaciones.', err);
    });
})();
