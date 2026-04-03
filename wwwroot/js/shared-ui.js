/**
 * shared-ui.js — Shared UI utilities loaded on every page.
 * Exposes window.TheBury for use in all page scripts.
 */

window.TheBury = window.TheBury || {};

/**
 * Format a number as ARS currency (e.g. $ 1.234,56).
 */
TheBury.formatCurrency = function (value) {
    return new Intl.NumberFormat('es-AR', {
        style: 'currency',
        currency: 'ARS',
        minimumFractionDigits: 2
    }).format(value || 0);
};

/**
 * Auto-dismiss all toast elements on the page.
 * Targets both `.toast-msg` and elements whose id starts with "toast-".
 * @param {number} [delay=5000] ms before fade-out starts
 */
TheBury.autoDismissToasts = function (delay) {
    delay = delay || 5000;
    document.querySelectorAll('.toast-msg, [id^="toast-"]').forEach(function (el) {
        setTimeout(function () {
            el.style.transition = 'opacity 0.5s ease, transform 0.4s ease';
            el.style.opacity = '0';
            el.style.transform = 'translateY(-8px)';
            setTimeout(function () { el.remove(); }, 500);
        }, delay);
    });
};

/**
 * Shared confirmation wrapper. Prefer this over calling the modal function directly.
 * Falls back to native confirm only if the shared modal is unavailable.
 */
TheBury.confirmAction = function (message, onConfirm) {
    if (typeof window.openConfirmModal === 'function') {
        window.openConfirmModal(message, onConfirm);
        return;
    }

    if (window.confirm(message || '¿Estás seguro de que deseas continuar?')) {
        if (typeof onConfirm === 'function') {
            onConfirm();
        }
    }
};

/**
 * Normalize a string for accent-insensitive search (NFD + lowercase).
 */
TheBury.normalizeText = function (value) {
    return (value || '').toString()
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '')
        .toLowerCase();
};

// ── Prevent dropdown menus from closing when clicking inside them ──
(function () {
    'use strict';
    document.addEventListener('click', function (e) {
        if (e.target.closest('[id$="Menu"]')) {
            e.stopPropagation();
        }
    }, true);
})();
