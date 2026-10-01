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
 * Show/hide an element by toggling the `hidden` utility class.
 * Null-safe: no-op if `el` is null/undefined.
 */
TheBury.show = function (el) { el?.classList.remove('hidden'); };
TheBury.hide = function (el) { el?.classList.add('hidden'); };

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
            setTimeout(function () {
                var h = el.offsetHeight;
                el.style.overflow = 'hidden';
                el.style.maxHeight = h + 'px';
                el.style.transition = 'max-height 0.3s ease, margin 0.3s ease, padding 0.3s ease';
                requestAnimationFrame(function () {
                    el.style.maxHeight = '0';
                    el.style.marginTop = '0';
                    el.style.marginBottom = '0';
                    el.style.paddingTop = '0';
                    el.style.paddingBottom = '0';
                });
                setTimeout(function () { el.remove(); }, 320);
            }, 500);
        }, delay);
    });
};

// ── Confirm modal (rendered via _ConfirmModal partial in _Layout) ──
(function () {
    'use strict';

    // Por defecto la confirmación es de peligro (rojo). Una acción que no destruye nada puede pedir
    // options.tone = 'primary' para no entrenar al usuario a confirmar a ciegas.
    var DANGER_CLASS = 'px-4 py-2 text-sm rounded-lg bg-red-600 text-white hover:bg-red-700 transition-colors';
    var PRIMARY_CLASS = 'px-4 py-2 text-sm rounded-lg bg-primary text-white font-semibold hover:bg-primary/90 transition-colors';
    var lastFocused = null;

    function getModal()      { return document.getElementById('confirmModal'); }
    function getActionBtn()  { return document.getElementById('confirmModalAction'); }
    function isOpen(modal)   { return !!modal && !modal.classList.contains('hidden'); }

    window.openConfirmModal = function (bodyText, onConfirm, options) {
        var modal = getModal();
        if (!modal) return;

        lastFocused = document.activeElement;

        var body = document.getElementById('confirmModalBody');
        if (bodyText && body) body.textContent = bodyText;

        // Título y rótulo del botón: opcionales, y se restauran en cada apertura para que un
        // llamador no herede los del anterior.
        var title = document.getElementById('confirmModalLabel');
        if (title) title.textContent = (options && options.title) || 'Confirmar acción';

        // Nota opcional (ej. observaciones al resolver una alerta): oculta por defecto,
        // así los llamadores existentes que no la piden no ven ningún cambio visual.
        var noteWrapper = document.getElementById('confirmModalNoteWrapper');
        var noteInput = document.getElementById('confirmModalNote');
        var showNote = !!(options && options.showNote);
        if (noteWrapper) noteWrapper.classList.toggle('hidden', !showNote);
        if (noteInput) {
            noteInput.value = '';
            noteInput.placeholder = (options && options.notePlaceholder) || 'Observaciones (opcional)...';
        }

        // Replace action button to clear previous listeners
        var actionBtn = getActionBtn();
        if (actionBtn) {
            var freshBtn = actionBtn.cloneNode(true);
            freshBtn.className = (options && options.tone === 'primary') ? PRIMARY_CLASS : DANGER_CLASS;
            freshBtn.textContent = (options && options.confirmLabel) || 'Confirmar';
            actionBtn.parentNode.replaceChild(freshBtn, actionBtn);
            if (typeof onConfirm === 'function') {
                freshBtn.addEventListener('click', function () {
                    var note = showNote && noteInput ? noteInput.value : '';
                    onConfirm(note);
                    window.closeConfirmModal();
                });
            }
        }

        modal.classList.remove('hidden');
        modal.classList.add('flex');

        // El foco entra al diálogo: en la nota si se pide, y si no en "Cancelar" (opción segura).
        var cancelBtn = modal.querySelector('.border-t [data-confirm-modal-close]');
        var initial = showNote && noteInput ? noteInput : cancelBtn;
        if (initial && typeof initial.focus === 'function') initial.focus();
    };

    window.closeConfirmModal = function () {
        var modal = getModal();
        if (!isOpen(modal)) return;
        modal.classList.add('hidden');
        modal.classList.remove('flex');

        // Devuelve el foco a quien abrió el diálogo (si sigue en la página).
        var target = lastFocused;
        lastFocused = null;
        if (target && typeof target.focus === 'function' && document.contains(target)) target.focus();
    };

    document.addEventListener('DOMContentLoaded', function () {
        var modal = getModal();
        if (!modal) return;

        // Backdrop click
        modal.addEventListener('click', function (e) {
            if (e.target === modal) window.closeConfirmModal();
        });

        // Close buttons inside modal
        modal.addEventListener('click', function (e) {
            if (e.target.closest('[data-confirm-modal-close]')) {
                window.closeConfirmModal();
            }
        });

        // Tab queda dentro del diálogo mientras está abierto.
        modal.addEventListener('keydown', function (e) {
            if (e.key !== 'Tab' || !isOpen(modal)) return;
            var focusables = Array.prototype.slice.call(
                modal.querySelectorAll('button, textarea, input, select, a[href]')
            ).filter(function (el) { return !el.disabled && el.offsetParent !== null; });
            if (!focusables.length) return;
            var first = focusables[0];
            var last = focusables[focusables.length - 1];
            if (e.shiftKey && document.activeElement === first) {
                e.preventDefault();
                last.focus();
            } else if (!e.shiftKey && document.activeElement === last) {
                e.preventDefault();
                first.focus();
            }
        });

        // Escape key
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') window.closeConfirmModal();
        });
    });
})();

/**
 * Shared confirmation wrapper. Prefer this over calling the modal function directly.
 * Falls back to native confirm only if the shared modal is unavailable.
 */
TheBury.confirmAction = function (message, onConfirm, options) {
    if (typeof window.openConfirmModal === 'function') {
        window.openConfirmModal(message, onConfirm, options);
        return;
    }

    if (window.confirm(message || '¿Estás seguro de que deseas continuar?')) {
        if (typeof onConfirm === 'function') {
            onConfirm();
        }
    }
};

/**
 * Same as confirmAction, but shows an optional note textarea and passes its
 * value (string, possibly empty) as the argument to onConfirm.
 */
TheBury.confirmActionWithNote = function (message, onConfirm, notePlaceholder) {
    if (typeof window.openConfirmModal === 'function') {
        window.openConfirmModal(message, onConfirm, { showNote: true, notePlaceholder: notePlaceholder });
        return;
    }

    if (window.confirm(message || '¿Estás seguro de que deseas continuar?')) {
        if (typeof onConfirm === 'function') {
            onConfirm('');
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
