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
