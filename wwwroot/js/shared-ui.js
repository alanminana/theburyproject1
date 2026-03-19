/**
 * shared-ui.js — Shared UI utilities
 */
(function () {
    'use strict';

    // Prevent dropdown menus from closing when clicking inside them
    document.addEventListener('click', function (e) {
        if (e.target.closest('[id$="Menu"]')) {
            e.stopPropagation();
        }
    }, true);
})();
