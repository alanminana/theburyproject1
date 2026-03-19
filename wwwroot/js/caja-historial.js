/**
 * caja-historial.js — Historial de Cierres page
 * Toast auto-dismiss
 */
(() => {
    'use strict';

    document.querySelectorAll('.toast-msg').forEach(el => {
        setTimeout(() => {
            el.style.transition = 'opacity 0.5s ease, transform 0.5s ease';
            el.style.opacity = '0';
            el.style.transform = 'translateY(-8px)';
            setTimeout(() => el.remove(), 500);
        }, 5000);
    });
})();
