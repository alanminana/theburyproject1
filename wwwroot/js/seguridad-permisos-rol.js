(() => {
    'use strict';

    TheBury.autoDismissToasts();

    const searchInput = document.getElementById('searchPermissionModules');
    const groupFilter = document.getElementById('permissionGroupFilter');
    const clearFiltersButton = document.getElementById('clearPermissionFilters');
    const roleSelect = document.getElementById('permissionRoleSelect');
    const rows = Array.from(document.querySelectorAll('.permission-row'));
    const visibleCount = document.getElementById('permissionsVisibleCount');
    const selectedCount = document.getElementById('permissionsSelectedCount');
    const emptyFilterState = document.getElementById('permissionsEmptyFilterState');
    const copyPermissionsContainer = document.getElementById('copyPermissionsContainer');

    const normalize = TheBury.normalizeText;

    function getVisibleRows() {
        const search = normalize(searchInput?.value);
        const group = normalize(groupFilter?.value);

        return rows.filter(row => {
            const matchSearch = !search || normalize(row.dataset.search).includes(search);
            const matchGroup = !group || normalize(row.dataset.group) === group;
            return matchSearch && matchGroup;
        });
    }

    function updateSelectedCount() {
        if (!selectedCount) return;

        const total = document.querySelectorAll('.permission-checkbox:checked').length;
        selectedCount.textContent = total.toString();
    }

    function applyFilters() {
        if (!rows.length) return;

        const visibleRows = getVisibleRows();
        rows.forEach(row => { row.style.display = 'none'; });
        visibleRows.forEach(row => { row.style.display = ''; });

        if (visibleCount) {
            visibleCount.textContent = visibleRows.length.toString();
        }

        emptyFilterState?.classList.toggle('hidden', visibleRows.length > 0);
    }

    function clearFilters() {
        if (searchInput) searchInput.value = '';
        if (groupFilter) groupFilter.value = '';
        applyFilters();
    }

    searchInput?.addEventListener('input', applyFilters);
    groupFilter?.addEventListener('change', applyFilters);
    clearFiltersButton?.addEventListener('click', clearFilters);

    roleSelect?.addEventListener('change', () => {
        roleSelect.form?.requestSubmit();
    });

    document.querySelectorAll('.permission-checkbox').forEach(checkbox => {
        checkbox.addEventListener('change', updateSelectedCount);
    });

    document.querySelectorAll('.btn-select-row').forEach(button => {
        button.addEventListener('click', () => {
            const rowId = button.dataset.rowId;
            document.querySelectorAll(`.permission-checkbox[data-row-id="${rowId}"]`).forEach(checkbox => {
                checkbox.checked = true;
            });
            updateSelectedCount();
        });
    });

    document.querySelectorAll('.btn-clear-row').forEach(button => {
        button.addEventListener('click', () => {
            const rowId = button.dataset.rowId;
            document.querySelectorAll(`.permission-checkbox[data-row-id="${rowId}"]`).forEach(checkbox => {
                checkbox.checked = false;
            });
            updateSelectedCount();
        });
    });

    document.querySelectorAll('.btn-select-column').forEach(button => {
        button.addEventListener('click', () => {
            const columnKey = button.dataset.columnKey;
            document.querySelectorAll(`.permission-checkbox[data-column-key="${columnKey}"]`).forEach(checkbox => {
                checkbox.checked = true;
            });
            updateSelectedCount();
        });
    });

    function animateOpen(backdrop, card) {
        requestAnimationFrame(() => {
            backdrop?.classList.replace('opacity-0', 'opacity-100');
            if (card) {
                card.classList.replace('scale-95', 'scale-100');
                card.classList.replace('opacity-0', 'opacity-100');
            }
        });
    }

    function animateClose(container, backdrop, card) {
        backdrop?.classList.replace('opacity-100', 'opacity-0');
        if (card) {
            card.classList.replace('scale-100', 'scale-95');
            card.classList.replace('opacity-100', 'opacity-0');
        }
        setTimeout(() => { container.innerHTML = ''; }, 300);
    }

    function bindCopyPermissionsModal() {
        if (!copyPermissionsContainer) return;

        const backdrop = document.getElementById('copyPermissionsBackdrop');
        const card = document.getElementById('copyPermissionsCard');
        const form = document.getElementById('formCopyPermissions');
        const closeButton = document.getElementById('closeCopyPermissions');
        const cancelButton = document.getElementById('cancelCopyPermissions');
        const errorBox = document.getElementById('copyPermissionsErrors');
        const errorList = document.getElementById('copyPermissionsErrorList');
        const submitButton = document.getElementById('submitCopyPermissions');
        const defaultSubmitHtml = submitButton?.innerHTML || '';

        const closeModal = () => animateClose(copyPermissionsContainer, backdrop, card);

        closeButton?.addEventListener('click', closeModal);
        cancelButton?.addEventListener('click', closeModal);
        backdrop?.addEventListener('click', closeModal);
        card?.addEventListener('click', event => event.stopPropagation());

        animateOpen(backdrop, card);

        form?.addEventListener('submit', event => {
            event.preventDefault();

            if (errorBox && errorList) {
                errorBox.classList.add('hidden');
                errorList.innerHTML = '';
            }

            if (submitButton) {
                submitButton.disabled = true;
                submitButton.innerHTML = '<span class="material-symbols-outlined text-sm animate-spin">progress_activity</span> Copiando...';
            }

            fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            })
                .then(response => response.json())
                .then(result => {
                    if (result.success) {
                        window.location.href = result.redirectUrl || window.location.href;
                        return;
                    }

                    if (errorBox && errorList) {
                        errorList.innerHTML = (result.errors || ['No se pudo copiar permisos.'])
                            .map(message => `<p>${message}</p>`)
                            .join('');
                        errorBox.classList.remove('hidden');
                    }

                    if (submitButton) {
                        submitButton.disabled = false;
                        submitButton.innerHTML = defaultSubmitHtml;
                    }
                })
                .catch(() => {
                    if (errorBox && errorList) {
                        errorList.innerHTML = '<p>Error de conexión. Intentá de nuevo.</p>';
                        errorBox.classList.remove('hidden');
                    }

                    if (submitButton) {
                        submitButton.disabled = false;
                        submitButton.innerHTML = defaultSubmitHtml;
                    }
                });
        });
    }

    document.getElementById('btnCopyPermissions')?.addEventListener('click', event => {
        const button = event.currentTarget;
        const params = new URLSearchParams({ roleId: button.dataset.roleId || '' });
        if (button.dataset.returnUrl) {
            params.set('returnUrl', button.dataset.returnUrl);
        }

        fetch(`/Seguridad/CopyPermisosRol?${params.toString()}`, {
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
            .then(response => {
                if (!response.ok) throw new Error(response.status);
                return response.text();
            })
            .then(html => {
                if (!copyPermissionsContainer) return;
                copyPermissionsContainer.innerHTML = html;
                bindCopyPermissionsModal();
            })
            .catch(() => { });
    });

    applyFilters();
    updateSelectedCount();
})();
