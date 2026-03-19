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
    }

    function closeSidebar() {
        sidebar?.classList.remove('open');
        overlay?.classList.remove('active');
        overlay?.classList.add('hidden');
    }

    toggleBtn?.addEventListener('click', function () {
        sidebar?.classList.contains('open') ? closeSidebar() : openSidebar();
    });

    overlay?.addEventListener('click', closeSidebar);

    // ── Sidebar collapse (desktop) ──
    function applySidebarState(collapsed) {
        if (!sidebar) return;
        if (collapsed) {
            sidebar.classList.add('collapsed');
        } else {
            sidebar.classList.remove('collapsed');
        }
    }

    // Restore persisted state on load
    if (window.matchMedia('(min-width: 1024px)').matches) {
        applySidebarState(localStorage.getItem(STORAGE_KEY) === '1');
    }

    collapseBtn?.addEventListener('click', function () {
        if (!sidebar) return;
        const willCollapse = !sidebar.classList.contains('collapsed');
        applySidebarState(willCollapse);
        localStorage.setItem(STORAGE_KEY, willCollapse ? '1' : '0');
    });

    // ── Simple dropdown toggle (notifications, user menu) ──
    function setupDropdown(triggerId, menuId) {
        const trigger = document.getElementById(triggerId);
        const menu    = document.getElementById(menuId);
        if (!trigger || !menu) return;

        trigger.addEventListener('click', function (e) {
            e.stopPropagation();
            const isOpen = !menu.classList.contains('hidden');
            closeAllDropdowns();
            if (!isOpen) {
                menu.classList.remove('hidden');
                trigger.setAttribute('aria-expanded', 'true');
            }
        });
    }

    function closeAllDropdowns() {
        document.querySelectorAll('[id$="Menu"]').forEach(function (el) {
            el.classList.add('hidden');
        });
        document.querySelectorAll('[aria-expanded]').forEach(function (el) {
            el.setAttribute('aria-expanded', 'false');
        });
    }

    document.addEventListener('click', closeAllDropdowns);

    setupDropdown('notificacionesDropdown', 'notificacionesMenu');
    setupDropdown('userMenuBtn', 'userMenu');
})();
