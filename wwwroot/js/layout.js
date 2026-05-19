/**
 * layout.js — Sidebar toggle, collapse & dropdown handling for the main layout
 */
(function () {
    'use strict';

    const sidebar      = document.getElementById('sidebar');
    const overlay      = document.getElementById('sidebarOverlay');
    const toggleBtn    = document.getElementById('toggleSidebar');
    const collapseBtn  = document.getElementById('collapseSidebar');
    const STORAGE_KEY  = 'sidebar-collapsed';

    // ── Sidebar toggle (mobile) ──
    function openSidebar() {
        sidebar?.classList.add('open');
        overlay?.classList.add('active');
        overlay?.classList.remove('hidden');
        toggleBtn?.setAttribute('aria-expanded', 'true');
    }

    function closeSidebar() {
        sidebar?.classList.remove('open');
        overlay?.classList.remove('active');
        overlay?.classList.add('hidden');
        toggleBtn?.setAttribute('aria-expanded', 'false');
        toggleBtn?.focus();
    }

    toggleBtn?.addEventListener('click', function () {
        sidebar?.classList.contains('open') ? closeSidebar() : openSidebar();
    });

    overlay?.addEventListener('click', closeSidebar);

    // ── Keyboard: Escape para cerrar + focus-trap mientras sidebar mobile está abierto ──
    function getFocusables() {
        return Array.from(sidebar?.querySelectorAll(
            'a[href], button:not([disabled]), [tabindex]:not([tabindex="-1"])'
        ) ?? []);
    }

    document.addEventListener('keydown', function (e) {
        if (!sidebar?.classList.contains('open')) return;

        if (e.key === 'Escape') {
            closeSidebar();
            return;
        }

        if (e.key === 'Tab') {
            const focusables = getFocusables();
            if (!focusables.length) return;
            const first = focusables[0];
            const last  = focusables[focusables.length - 1];
            if (e.shiftKey) {
                if (document.activeElement === first) {
                    e.preventDefault();
                    last.focus();
                }
            } else {
                if (document.activeElement === last) {
                    e.preventDefault();
                    first.focus();
                }
            }
        }
    });

    // ── Sidebar collapse (desktop) ──
    function applySidebarState(collapsed) {
        if (!sidebar) return;
        if (collapsed) {
            sidebar.classList.add('collapsed');
        } else {
            sidebar.classList.remove('collapsed');
        }
    }

    // Restore persisted state — se omite en el rango donde CSS ya auto-colapsa (notebooks 125%)
    const mediaAutoCollapse = window.matchMedia('(min-width: 1024px) and (max-width: 1600px)');
    if (window.matchMedia('(min-width: 1024px)').matches && !mediaAutoCollapse.matches) {
        applySidebarState(localStorage.getItem(STORAGE_KEY) === '1');
    }

    collapseBtn?.addEventListener('click', function () {
        if (!sidebar) return;
        const willCollapse = !sidebar.classList.contains('collapsed');
        applySidebarState(willCollapse);
        localStorage.setItem(STORAGE_KEY, willCollapse ? '1' : '0');
    });

})();
