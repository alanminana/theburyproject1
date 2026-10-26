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

    // FE-D-003: en mobile el menú cerrado está fuera de pantalla solo por transform; sin `inert` sus enlaces seguían
    // recibiendo foco con Tab. En escritorio el menú siempre es visible y nunca es inert.
    const mobileMq = window.matchMedia('(max-width: 1023px)');
    function syncSidebarInert() {
        if (!sidebar) return;
        sidebar.inert = mobileMq.matches && !sidebar.classList.contains('open');
    }
    mobileMq.addEventListener('change', syncSidebarInert);
    syncSidebarInert();

    // ── Sidebar toggle (mobile) ──
    function openSidebar() {
        sidebar?.classList.add('open');
        syncSidebarInert();
        overlay?.classList.add('active');
        overlay?.classList.remove('hidden');
        toggleBtn?.setAttribute('aria-expanded', 'true');
    }

    function closeSidebar() {
        sidebar?.classList.remove('open');
        syncSidebarInert();
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

    // Restore persisted state (el script inline de _Layout ya lo aplicó antes del primer paint)
    if (window.matchMedia('(min-width: 1024px)').matches) {
        applySidebarState(localStorage.getItem(STORAGE_KEY) === '1');
    }

    collapseBtn?.addEventListener('click', function () {
        if (!sidebar) return;
        const willCollapse = !sidebar.classList.contains('collapsed');
        applySidebarState(willCollapse);
        localStorage.setItem(STORAGE_KEY, willCollapse ? '1' : '0');
    });

})();


// Menú de usuario (header)
(function () {
    var root = document.querySelector('[data-user-menu]');
    if (!root) return;
    var toggle = root.querySelector('[data-user-menu-toggle]');
    var panel = root.querySelector('#user-menu-panel');
    function setOpen(open) {
        panel.hidden = !open;
        toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    }
    toggle.addEventListener('click', function () { setOpen(panel.hidden); });
    document.addEventListener('click', function (e) { if (!root.contains(e.target)) setOpen(false); });
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && !panel.hidden) { setOpen(false); toggle.focus(); }
    });
})();
