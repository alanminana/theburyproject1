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

        function isOpen() {
            return !modal.classList.contains('hidden');
        }

        function open(trigger) {
            if (typeof config.beforeOpen === 'function') {
                config.beforeOpen(modal, trigger);
            }

            modal.classList.remove('hidden');
            if (displayClass) {
                modal.classList.add(displayClass);
            }
            modal.setAttribute('aria-hidden', 'false');
            bodyLock.lock();

            if (typeof config.afterOpen === 'function') {
                config.afterOpen(modal, trigger);
            }
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
