/**
 * venta-module.js
 *
 * Local helpers for Venta screens.
 */
(() => {
    'use strict';

    window.VentaModule = window.VentaModule || {};

    if (window.VentaModule.initSharedUi) {
        return;
    }

    function initSharedUi(timeoutMs) {
        const theBury = window.TheBury || {};
        if (typeof theBury.autoDismissToasts === 'function') {
            theBury.autoDismissToasts(timeoutMs);
        }
    }

    function initScrollAffordance(rootOrSelector) {
        const theBury = window.TheBury || {};
        if (typeof theBury.initHorizontalScrollAffordance !== 'function') {
            return null;
        }

        const instance = theBury.initHorizontalScrollAffordance(rootOrSelector);
        requestAnimationFrame(() => {
            if (instance && typeof instance.update === 'function') {
                instance.update();
            }
        });
        return instance;
    }

    function refreshScrollAffordance(instance) {
        if (!instance || typeof instance.update !== 'function') {
            return;
        }

        requestAnimationFrame(() => instance.update());
    }

    function createBodyLockController() {
        let previousOverflow = '';

        return {
            lock() {
                previousOverflow = document.body.style.overflow;
                document.body.style.overflow = 'hidden';
            },
            unlock() {
                document.body.style.overflow = previousOverflow;
            }
        };
    }

    function bindModal(name, options) {
        const modal = document.querySelector('[data-venta-modal="' + name + '"]');
        if (!modal) {
            return null;
        }

        const config = options || {};
        const displayClass = config.displayClass || '';
        const bodyLock = createBodyLockController();
        const focusableSelector = [
            'a[href]', 'button:not([disabled])', 'input:not([disabled]):not([type="hidden"])',
            'select:not([disabled])', 'textarea:not([disabled])',
            '[tabindex]:not([tabindex="-1"])'
        ].join(', ');
        let triggerElement = null;

        function isOpen() {
            return !modal.classList.contains('hidden');
        }

        function open(trigger) {
            if (typeof config.beforeOpen === 'function') {
                config.beforeOpen(modal, trigger);
            }

            triggerElement = trigger instanceof HTMLElement ? trigger : document.activeElement;
            modal.classList.remove('hidden');
            if (displayClass) {
                modal.classList.add(displayClass);
            }
            modal.setAttribute('aria-hidden', 'false');
            bodyLock.lock();

            if (typeof config.afterOpen === 'function') {
                config.afterOpen(modal, trigger);
            }

            const initialFocus = modal.querySelector('[data-modal-initial-focus]')
                || modal.querySelector(focusableSelector)
                || modal;
            requestAnimationFrame(() => initialFocus.focus());
        }

        function close() {
            if (typeof config.beforeClose === 'function') {
                config.beforeClose(modal);
            }

            modal.classList.add('hidden');
            if (displayClass) {
                modal.classList.remove(displayClass);
            }
            modal.setAttribute('aria-hidden', 'true');
            bodyLock.unlock();

            if (typeof config.afterClose === 'function') {
                config.afterClose(modal);
            }

            if (triggerElement instanceof HTMLElement && document.contains(triggerElement)) {
                triggerElement.focus();
            }
            triggerElement = null;
        }

        document.addEventListener('click', function (event) {
            const openTrigger = event.target.closest('[data-venta-modal-action="open"][data-venta-modal-target="' + name + '"]');
            if (openTrigger) {
                event.preventDefault();
                open(openTrigger);
                return;
            }

            const closeTrigger = event.target.closest('[data-venta-modal-action="close"]');
            if (!closeTrigger) {
                return;
            }

            const targetModal = closeTrigger.closest('[data-venta-modal]');
            if (targetModal !== modal) {
                return;
            }

            event.preventDefault();
            close();
        });

        document.addEventListener('keydown', function (event) {
            if (event.key === 'Escape' && isOpen()) {
                close();
                return;
            }

            if (event.key !== 'Tab' || !isOpen()) {
                return;
            }

            const focusable = Array.from(modal.querySelectorAll(focusableSelector))
                .filter((element) => !element.hasAttribute('hidden'));
            if (focusable.length === 0) {
                event.preventDefault();
                modal.focus();
                return;
            }

            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        });

        return {
            modal,
            open,
            close,
            isOpen
        };
    }

    window.VentaModule.initSharedUi = initSharedUi;
    window.VentaModule.initScrollAffordance = initScrollAffordance;
    window.VentaModule.refreshScrollAffordance = refreshScrollAffordance;
    window.VentaModule.bindModal = bindModal;
})();
