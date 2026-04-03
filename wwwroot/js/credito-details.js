/* credito-details.js — Modales, toasts y affordance para Credito/Details */

document.addEventListener('DOMContentLoaded', function () {
    var creditoModule = window.TheBury && window.TheBury.CreditoModule;
    if (creditoModule && typeof creditoModule.initSharedUi === 'function') {
        creditoModule.initSharedUi();
    }
    if (creditoModule && typeof creditoModule.bindModalController === 'function') {
        creditoModule.bindModalController();
    }
    if (creditoModule && typeof creditoModule.initScrollAffordance === 'function') {
        creditoModule.initScrollAffordance(document);
    }
});
