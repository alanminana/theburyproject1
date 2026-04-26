/* credito-module.js — helpers locales para Credito */

(function (window, document) {
    'use strict';

    var TheBury = window.TheBury = window.TheBury || {};

    function initSharedUi() {
        if (typeof TheBury.autoDismissToasts === 'function') {
            TheBury.autoDismissToasts();
        }
    }

    function initScrollAffordance(scope) {
        var root = scope || document;

        if (typeof TheBury.initHorizontalScrollAffordance === 'function') {
            root.querySelectorAll('[data-oc-scroll]').forEach(function (scrollRoot) {
                var instance = TheBury.initHorizontalScrollAffordance(scrollRoot);
                if (instance && typeof instance.update === 'function') {
                    instance.update();
                }
            });
        }

        root.querySelectorAll('[data-oc-scroll-region]').forEach(function (region) {
            region.dispatchEvent(new Event('scroll'));
        });
    }

    function refreshScrollAffordance(scope) {
        window.requestAnimationFrame(function () {
            window.requestAnimationFrame(function () {
                initScrollAffordance(scope);
            });
        });
    }

    function parseJsonScript(selector, fallback) {
        var script = document.querySelector(selector);
        if (!script) {
            return fallback;
        }

        try {
            return JSON.parse(script.textContent || '');
        } catch (error) {
            return fallback;
        }
    }

    function bindModalController(options) {
        var modalSelector = options && options.modalSelector ? options.modalSelector : '[data-credito-modal]';
        var modals = Array.from(document.querySelectorAll(modalSelector));

        if (!modals.length) {
            return {
                open: function () { },
                close: function () { }
            };
        }

        function getModal(target) {
            if (!target) return null;
            return document.querySelector(modalSelector + '[data-credito-modal="' + target + '"]');
        }

        function open(target) {
            var modal = getModal(target);
            if (!modal) return;

            modal.classList.remove('hidden');
            modal.classList.add('flex');
            document.body.classList.add('overflow-hidden');

            var autofocusField = modal.querySelector('textarea, input, button[type="submit"]');
            if (autofocusField) {
                window.requestAnimationFrame(function () {
                    autofocusField.focus();
                });
            }

            refreshScrollAffordance(modal);
        }

        function close(modal) {
            if (!modal) return;

            modal.classList.add('hidden');
            modal.classList.remove('flex');

            var stillOpen = modals.some(function (item) {
                return !item.classList.contains('hidden');
            });

            if (!stillOpen) {
                document.body.classList.remove('overflow-hidden');
            }
        }

        document.addEventListener('click', function (event) {
            var openTrigger = event.target.closest('[data-credito-modal-action="open"]');
            if (openTrigger) {
                event.preventDefault();
                open(openTrigger.getAttribute('data-credito-modal-target'));
                return;
            }

            var closeTrigger = event.target.closest('[data-credito-modal-action="close"]');
            if (closeTrigger) {
                event.preventDefault();
                close(closeTrigger.closest(modalSelector));
            }
        });

        modals.forEach(function (modal) {
            modal.addEventListener('click', function (event) {
                if (event.target === modal) {
                    close(modal);
                }
            });
        });

        document.addEventListener('keydown', function (event) {
            if (event.key !== 'Escape') return;

            modals.forEach(function (modal) {
                if (!modal.classList.contains('hidden')) {
                    close(modal);
                }
            });
        });

        return {
            open: open,
            close: close
        };
    }

    function initGaranteToggle() {
        var toggle = document.getElementById('requiere-garante-toggle');
        var garanteField = document.getElementById('garante-field');
        if (!toggle || !garanteField) return;

        function syncVisibility() {
            garanteField.style.display = toggle.checked ? '' : 'none';
        }

        toggle.addEventListener('change', syncVisibility);
        syncVisibility();
    }

    TheBury.CreditoModule = {
        bindModalController: bindModalController,
        initGaranteToggle: initGaranteToggle,
        initSharedUi: initSharedUi,
        initScrollAffordance: initScrollAffordance,
        parseJsonScript: parseJsonScript,
        refreshScrollAffordance: refreshScrollAffordance
    };
})(window, document);
