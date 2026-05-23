(function (window, document) {
    'use strict';

    var modalApis = {};
    var productSelectionApi = null;

    function dispatchModuleEvent(name, detail) {
        document.dispatchEvent(new CustomEvent(name, { detail: detail || null }));
    }

    window.CatalogoModule = window.CatalogoModule || {};
    window.CatalogoModule.events = window.CatalogoModule.events || {
        refreshScroll: 'catalogo:refresh-scroll'
    };
    window.CatalogoModule.registerModalApi = function (name, api) {
        if (!name || !api) return;
        modalApis[name] = api;
    };
    window.CatalogoModule.getModalApi = function (name) {
        return name && modalApis[name] ? modalApis[name] : null;
    };
    window.CatalogoModule.registerProductSelectionApi = function (api) {
        productSelectionApi = api || null;
    };
    window.CatalogoModule.getProductSelectionApi = function () {
        return productSelectionApi;
    };
    window.CatalogoModule.trapFocus = function (modal, e) {
        if (e.key !== 'Tab') return;
        var focusable = Array.prototype.filter.call(
            modal.querySelectorAll(
                'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
            ),
            function (node) { return node.offsetParent !== null; }
        );
        if (!focusable.length) return;
        var first = focusable[0];
        var last = focusable[focusable.length - 1];
        var active = document.activeElement;
        if (e.shiftKey) {
            if (active === first || !modal.contains(active) || focusable.indexOf(active) === -1) {
                e.preventDefault();
                last.focus();
            }
        } else {
            if (active === last || !modal.contains(active)) {
                e.preventDefault();
                first.focus();
            }
        }
    };
    window.CatalogoModule.requestScrollRefresh = function () {
        var eventName = window.CatalogoModule.events.refreshScroll;

        dispatchModuleEvent(eventName);
        window.requestAnimationFrame(function () {
            dispatchModuleEvent(eventName);
            window.requestAnimationFrame(function () {
                dispatchModuleEvent(eventName);
            });
        });
        window.setTimeout(function () {
            dispatchModuleEvent(eventName);
        }, 150);
    };
})(window, document);
