/**
 * seguridad-index.js — Filtrado, ordenamiento y acciones masivas para tabla de Usuarios
 */
(() => {
    'use strict';

    TheBury.autoDismissToasts();

    // ── Elements ──
    const searchInput = document.getElementById('searchUsuarios');
    const filterRol = document.getElementById('filterRol');
    const filterSucursal = document.getElementById('filterSucursal');
    const filterEstado = document.getElementById('filterEstado');
    const orderBy = document.getElementById('orderBy');
    const toggleOrderDir = document.getElementById('toggleOrderDir');
    const orderDirIcon = document.getElementById('orderDirIcon');
    const clearBtn = document.getElementById('clearFilters');
    const clearFiltersEmpty = document.getElementById('clearFiltersEmpty');
    const rows = Array.from(document.querySelectorAll('.usuario-row'));
    const visibleCount = document.getElementById('visibleCount');
    const emptyFilterState = document.getElementById('emptyFilterState');
    const tableBody = document.getElementById('usuariosTableBody');

    // Bulk
    const selectAll = document.getElementById('selectAll');
    const bulkBar = document.getElementById('bulkBar');
    const selectedCountEl = document.getElementById('selectedCount');
    const bulkIds = document.getElementById('bulkIds');
    const bulkForm = document.getElementById('bulkForm');
    const bulkAccion = document.getElementById('bulkAccion');
    const bulkRol = document.getElementById('bulkRol');
    const bulkSucursalId = document.getElementById('bulkSucursalId');
    const clearSelection = document.getElementById('clearSelection');
    const bulkActionButtons = document.querySelectorAll('[data-bulk-action]');
    const bulkModalButtons = document.querySelectorAll('[data-bulk-modal]');
    const bulkConfigContainer = document.getElementById('bulkConfigContainer');
    const bulkConfigBackdrop = document.getElementById('bulkConfigBackdrop');
    const bulkConfigCard = document.getElementById('bulkConfigCard');
    const bulkConfigTitle = document.getElementById('bulkConfigTitle');
    const bulkConfigDescription = document.getElementById('bulkConfigDescription');
    const bulkConfigError = document.getElementById('bulkConfigError');
    const bulkRoleField = document.getElementById('bulkRoleField');
    const bulkRoleSelect = document.getElementById('bulkRoleSelect');
    const bulkSucursalField = document.getElementById('bulkSucursalField');
    const bulkSucursalSelect = document.getElementById('bulkSucursalSelect');
    const closeBulkConfig = document.getElementById('closeBulkConfig');
    const cancelBulkConfig = document.getElementById('cancelBulkConfig');
    const submitBulkConfig = document.getElementById('submitBulkConfig');

    if (!searchInput || rows.length === 0) return;

    let orderDirection = 'asc';
    let currentBulkModalAction = null;

    const normalizeText = TheBury.normalizeText;

    // ── Filtering ──
    function getVisibleRows() {
        const term = normalizeText(searchInput.value);
        const rol = filterRol ? normalizeText(filterRol.value) : '';
        const sucursal = filterSucursal ? normalizeText(filterSucursal.value) : '';
        const estado = filterEstado ? normalizeText(filterEstado.value) : '';

        return rows.filter(row => {
            const rowSearch = normalizeText(row.dataset.search);
            const rowRoles = (row.dataset.roleValues || '')
                .split('||')
                .map(normalizeText)
                .filter(Boolean);
            const rowSucursal = normalizeText(row.dataset.sucursalId || '');
            const rowEstado = normalizeText(row.dataset.estado);

            const matchTerm = !term || rowSearch.includes(term);
            const matchRol = !rol || rowRoles.includes(rol);
            const matchSucursal = !sucursal || rowSucursal === sucursal;
            const matchEstado = !estado || rowEstado === estado;

            return matchTerm && matchRol && matchSucursal && matchEstado;
        });
    }

    // ── Sorting ──
    function getSortValue(row, field) {
        switch (field) {
            case 'username': return row.dataset.name || '';
            case 'nombre': return row.dataset.fullname || '';
            case 'email': return row.dataset.email || '';
            case 'rol': return row.dataset.rolPrincipal || '';
            case 'sucursal': return row.dataset.sucursal || '';
            case 'estado': return row.dataset.estado || '';
            case 'ultimoAcceso': return row.dataset.ultimoAcceso || '';
            default: return row.dataset.name || '';
        }
    }

    function sortRows(visible) {
        const field = orderBy ? orderBy.value : 'username';
        const dir = orderDirection === 'asc' ? 1 : -1;

        return visible.sort((a, b) => {
            const va = getSortValue(a, field);
            const vb = getSortValue(b, field);

            if (field === 'ultimoAcceso') {
                const da = va ? new Date(va).getTime() : 0;
                const db = vb ? new Date(vb).getTime() : 0;
                return (da - db) * dir;
            }
            return normalizeText(va).localeCompare(normalizeText(vb), 'es') * dir;
        });
    }

    function applyFilters() {
        const visible = getVisibleRows();
        const sorted = sortRows([...visible]);

        // Hide all, then append sorted visible ones
        rows.forEach(r => r.style.display = 'none');
        sorted.forEach(r => {
            r.style.display = '';
            tableBody.appendChild(r);
        });

        // Update count
        if (visibleCount) visibleCount.textContent = visible.length;

        // Toggle empty filter state
        if (emptyFilterState) {
            emptyFilterState.classList.toggle('hidden', visible.length > 0);
        }

        updateBulkBar();
    }

    // ── Event listeners for filters ──
    searchInput.addEventListener('input', applyFilters);
    filterRol?.addEventListener('change', applyFilters);
    filterSucursal?.addEventListener('change', applyFilters);
    filterEstado?.addEventListener('change', applyFilters);
    orderBy?.addEventListener('change', applyFilters);

    toggleOrderDir?.addEventListener('click', () => {
        orderDirection = orderDirection === 'asc' ? 'desc' : 'asc';
        if (orderDirIcon) {
            orderDirIcon.textContent = orderDirection === 'asc' ? 'arrow_upward' : 'arrow_downward';
        }
        applyFilters();
    });

    function clearAllFilters() {
        searchInput.value = '';
        if (filterRol) filterRol.value = '';
        if (filterSucursal) filterSucursal.value = '';
        if (filterEstado) filterEstado.value = '';
        if (orderBy) orderBy.value = 'username';
        orderDirection = 'asc';
        if (orderDirIcon) orderDirIcon.textContent = 'arrow_upward';
        applyFilters();
    }

    clearBtn?.addEventListener('click', clearAllFilters);
    clearFiltersEmpty?.addEventListener('click', clearAllFilters);

    // ── Mass Selection ──
    function getCheckedIds() {
        return rows
            .filter(r => r.style.display !== 'none')
            .map(r => r.querySelector('.row-check'))
            .filter(cb => cb && cb.checked)
            .map(cb => cb.value);
    }

    function updateBulkBar() {
        const ids = getCheckedIds();
        if (!bulkBar) return;

        if (ids.length > 0) {
            bulkBar.classList.remove('hidden');
            if (selectedCountEl) selectedCountEl.textContent = ids.length;
            if (bulkIds) bulkIds.value = ids.join(',');
        } else {
            bulkBar.classList.add('hidden');
        }

        // Sync selectAll checkbox
        const visibleChecks = rows
            .filter(r => r.style.display !== 'none')
            .map(r => r.querySelector('.row-check'))
            .filter(Boolean);
        if (selectAll && visibleChecks.length > 0) {
            selectAll.checked = visibleChecks.every(cb => cb.checked);
            selectAll.indeterminate = !selectAll.checked && visibleChecks.some(cb => cb.checked);
        } else if (selectAll) {
            selectAll.checked = false;
            selectAll.indeterminate = false;
        }
    }

    selectAll?.addEventListener('change', () => {
        const checked = selectAll.checked;
        rows.forEach(r => {
            if (r.style.display !== 'none') {
                const cb = r.querySelector('.row-check');
                if (cb) cb.checked = checked;
            }
        });
        updateBulkBar();
    });

    document.querySelectorAll('.row-check').forEach(cb => {
        cb.addEventListener('change', updateBulkBar);
    });

    clearSelection?.addEventListener('click', () => {
        document.querySelectorAll('.row-check').forEach(cb => cb.checked = false);
        if (selectAll) selectAll.checked = false;
        updateBulkBar();
    });

    function resetBulkHiddenFields() {
        if (bulkAccion) bulkAccion.value = '';
        if (bulkRol) bulkRol.value = '';
        if (bulkSucursalId) bulkSucursalId.value = '';
    }

    function submitBulkAction(action, extra = {}) {
        if (!bulkForm || !bulkAccion || !bulkIds || !bulkIds.value) return;

        resetBulkHiddenFields();
        bulkAccion.value = action;
        if (bulkRol && extra.rol) bulkRol.value = extra.rol;
        if (bulkSucursalId && extra.sucursalId) bulkSucursalId.value = extra.sucursalId;
        bulkForm.requestSubmit();
    }

    bulkActionButtons.forEach(btn => {
        btn.addEventListener('click', () => {
            const action = btn.dataset.bulkAction;
            if (!action) return;
            submitBulkAction(action);
        });
    });

    function clearBulkConfigError() {
        if (!bulkConfigError) return;
        bulkConfigError.textContent = '';
        bulkConfigError.classList.add('hidden');
    }

    function showBulkConfigError(message) {
        if (!bulkConfigError) return;
        bulkConfigError.textContent = message;
        bulkConfigError.classList.remove('hidden');
    }

    function closeBulkConfigModal() {
        currentBulkModalAction = null;
        clearBulkConfigError();
        if (bulkRoleSelect) bulkRoleSelect.value = '';
        if (bulkSucursalSelect) bulkSucursalSelect.value = '';
        bulkConfigBackdrop?.classList.replace('opacity-100', 'opacity-0');
        if (bulkConfigCard) {
            bulkConfigCard.classList.replace('scale-100', 'scale-95');
            bulkConfigCard.classList.replace('opacity-100', 'opacity-0');
        }
        setTimeout(() => bulkConfigContainer?.classList.add('hidden'), 300);
    }

    function openBulkConfigModal(action) {
        currentBulkModalAction = action;
        clearBulkConfigError();
        bulkConfigContainer?.classList.remove('hidden');

        if (action === 'asignarRol') {
            if (bulkConfigTitle) bulkConfigTitle.textContent = 'Asignar rol en lote';
            if (bulkConfigDescription) bulkConfigDescription.textContent = 'El rol seleccionado se agregará a todos los usuarios marcados.';
            bulkRoleField?.classList.remove('hidden');
            bulkSucursalField?.classList.add('hidden');
            if (submitBulkConfig) submitBulkConfig.textContent = 'Asignar rol';
        } else {
            if (bulkConfigTitle) bulkConfigTitle.textContent = 'Cambiar sucursal en lote';
            if (bulkConfigDescription) bulkConfigDescription.textContent = 'La sucursal seleccionada se aplicará a todos los usuarios marcados.';
            bulkRoleField?.classList.add('hidden');
            bulkSucursalField?.classList.remove('hidden');
            if (submitBulkConfig) submitBulkConfig.textContent = 'Cambiar sucursal';
        }

        requestAnimationFrame(() => {
            bulkConfigBackdrop?.classList.replace('opacity-0', 'opacity-100');
            if (bulkConfigCard) {
                bulkConfigCard.classList.replace('scale-95', 'scale-100');
                bulkConfigCard.classList.replace('opacity-0', 'opacity-100');
            }
        });
    }

    bulkModalButtons.forEach(btn => {
        btn.addEventListener('click', () => {
            const action = btn.dataset.bulkModal;
            if (!action) return;
            openBulkConfigModal(action);
        });
    });

    closeBulkConfig?.addEventListener('click', closeBulkConfigModal);
    cancelBulkConfig?.addEventListener('click', closeBulkConfigModal);
    bulkConfigBackdrop?.addEventListener('click', closeBulkConfigModal);
    bulkConfigCard?.addEventListener('click', e => e.stopPropagation());

    submitBulkConfig?.addEventListener('click', () => {
        if (!currentBulkModalAction) return;

        if (currentBulkModalAction === 'asignarRol') {
            const selectedRole = (bulkRoleSelect?.value || '').trim();
            if (!selectedRole) {
                showBulkConfigError('Seleccioná un rol para continuar.');
                return;
            }
            closeBulkConfigModal();
            submitBulkAction(currentBulkModalAction, { rol: selectedRole });
            return;
        }

        const selectedSucursalId = (bulkSucursalSelect?.value || '').trim();
        if (!selectedSucursalId) {
            showBulkConfigError('Seleccioná una sucursal para continuar.');
            return;
        }
        closeBulkConfigModal();
        submitBulkAction(currentBulkModalAction, { sucursalId: selectedSucursalId });
    });

    // ── Create User Modal ──
    const createContainer = document.getElementById('createUserContainer');
    const CREATE_URL = '/Usuarios/Create';

    function openCreateModal() {
        fetch(CREATE_URL, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(r => { if (!r.ok) throw new Error(r.status); return r.text(); })
            .then(html => {
                createContainer.innerHTML = html;
                requestAnimationFrame(() => {
                    const backdrop = document.getElementById('createUserBackdrop');
                    const card = document.getElementById('createUserCard');
                    if (backdrop) backdrop.classList.replace('opacity-0', 'opacity-100');
                    if (card) { card.classList.replace('scale-95', 'scale-100'); card.classList.replace('opacity-0', 'opacity-100'); }
                });
                bindCreateModal();
            })
            .catch(() => { /* silently fail if permission denied */ });
    }

    function closeCreateModal() {
        const backdrop = document.getElementById('createUserBackdrop');
        const card = document.getElementById('createUserCard');
        if (backdrop) backdrop.classList.replace('opacity-100', 'opacity-0');
        if (card) { card.classList.replace('scale-100', 'scale-95'); card.classList.replace('opacity-100', 'opacity-0'); }
        setTimeout(() => { createContainer.innerHTML = ''; }, 300);
    }

    function bindCreateModal() {
        const form = document.getElementById('formCreateUser');
        const errBox = document.getElementById('createUserErrors');
        const errList = document.getElementById('createUserErrorList');

        document.getElementById('closeCreateUser')?.addEventListener('click', closeCreateModal);
        document.getElementById('cancelCreateUser')?.addEventListener('click', closeCreateModal);
        document.getElementById('createUserBackdrop')?.addEventListener('click', closeCreateModal);

        // Prevent card clicks from closing
        document.getElementById('createUserCard')?.addEventListener('click', e => e.stopPropagation());

        form?.addEventListener('submit', e => {
            e.preventDefault();
            const btn = document.getElementById('submitCreateUser');
            if (btn) { btn.disabled = true; btn.innerHTML = '<span class="material-symbols-outlined text-sm animate-spin">progress_activity</span> Guardando...'; }

            const data = new FormData(form);

            fetch(form.action, { method: 'POST', body: data, headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(r => r.json())
                .then(res => {
                    if (res.success) {
                        closeCreateModal();
                        window.location.reload();
                    } else {
                        if (errBox && errList && res.errors) {
                            errList.innerHTML = res.errors.map(e => `<p>${e}</p>`).join('');
                            errBox.classList.remove('hidden');
                        }
                        if (btn) { btn.disabled = false; btn.innerHTML = '<span class="material-symbols-outlined text-[20px]">save</span> Guardar Usuario'; }
                    }
                })
                .catch(() => {
                    if (errBox && errList) { errList.innerHTML = '<p>Error de conexión. Intentá de nuevo.</p>'; errBox.classList.remove('hidden'); }
                    if (btn) { btn.disabled = false; btn.innerHTML = '<span class="material-symbols-outlined text-[20px]">save</span> Guardar Usuario'; }
                });
        });
    }

    // Bind all triggers (header button + empty state button)
    document.getElementById('btnNuevoUsuario')?.addEventListener('click', openCreateModal);
    document.querySelectorAll('.btn-open-create-user').forEach(btn => btn.addEventListener('click', openCreateModal));

    // ── Change Password Modal ──
    const changePassContainer = document.getElementById('changePassContainer');
    const blockUserContainer = document.getElementById('blockUserContainer');

    function openChangePassModal(userId) {
        fetch(`/Usuarios/CambiarPassword?id=${encodeURIComponent(userId)}`, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(r => { if (!r.ok) throw new Error(r.status); return r.text(); })
            .then(html => {
                changePassContainer.innerHTML = html;
                requestAnimationFrame(() => {
                    const backdrop = document.getElementById('changePassBackdrop');
                    const card = document.getElementById('changePassCard');
                    if (backdrop) backdrop.classList.replace('opacity-0', 'opacity-100');
                    if (card) { card.classList.replace('scale-95', 'scale-100'); card.classList.replace('opacity-0', 'opacity-100'); }
                });
                bindChangePassModal();
            })
            .catch(() => { });
    }

    function closeChangePassModal() {
        const backdrop = document.getElementById('changePassBackdrop');
        const card = document.getElementById('changePassCard');
        if (backdrop) backdrop.classList.replace('opacity-100', 'opacity-0');
        if (card) { card.classList.replace('scale-100', 'scale-95'); card.classList.replace('opacity-100', 'opacity-0'); }
        setTimeout(() => { changePassContainer.innerHTML = ''; }, 300);
    }

    function updateRequirementState(element, met) {
        if (!element) return;
        element.className = `flex items-center gap-2 text-xs ${met ? 'text-green-600 dark:text-green-400' : 'text-slate-500 dark:text-slate-400'}`;
        const icon = element.querySelector('.material-symbols-outlined');
        if (icon) icon.textContent = met ? 'check_circle' : 'radio_button_unchecked';
    }

    function evaluatePasswordPolicy(password, policy) {
        const checks = {
            length: password.length >= policy.requiredLength
        };

        if (policy.requireUppercase) checks.uppercase = /[A-Z]/.test(password);
        if (policy.requireLowercase) checks.lowercase = /[a-z]/.test(password);
        if (policy.requireDigit) checks.digit = /\d/.test(password);
        if (policy.requireSymbol) checks.symbol = /[^A-Za-z0-9]/.test(password);

        return checks;
    }

    function getStrengthFromChecks(checks, password) {
        const values = Object.values(checks);
        const met = values.filter(Boolean).length;
        const total = values.length || 1;

        if (!password) return { level: 0, label: '—', color: 'bg-slate-200 dark:bg-slate-700', textColor: 'text-slate-400' };
        if (met < total) return { level: Math.max(1, Math.round((met / total) * 4)), label: 'Incompleta', color: 'bg-amber-500', textColor: 'text-amber-500' };
        if (password.length >= 10) return { level: 4, label: 'Fuerte', color: 'bg-green-500', textColor: 'text-green-500' };
        return { level: 4, label: 'Válida', color: 'bg-primary', textColor: 'text-primary' };
    }

    function bindChangePassModal() {
        const form = document.getElementById('formChangePass');
        const errBox = document.getElementById('changePassErrors');
        const errList = document.getElementById('changePassErrorList');
        const newPw = document.getElementById('newPasswordInput');
        const confirmPw = document.getElementById('confirmPasswordInput');
        const bars = document.getElementById('strengthBars');
        const strengthLabel = document.getElementById('strengthLabel');
        const matchIndicator = document.getElementById('matchIndicator');
        const submitButtonText = 'Resetear contraseña';
        const policy = {
            requiredLength: Number(form?.dataset.requiredLength || 6),
            requireUppercase: form?.dataset.requireUppercase === 'true',
            requireLowercase: form?.dataset.requireLowercase === 'true',
            requireDigit: form?.dataset.requireDigit === 'true',
            requireSymbol: form?.dataset.requireSymbol === 'true'
        };
        const requirementElements = {
            length: document.getElementById('passwordRequirementLength'),
            uppercase: document.getElementById('passwordRequirementUppercase'),
            lowercase: document.getElementById('passwordRequirementLowercase'),
            digit: document.getElementById('passwordRequirementDigit'),
            symbol: document.getElementById('passwordRequirementSymbol')
        };

        document.getElementById('closeChangePass')?.addEventListener('click', closeChangePassModal);
        document.getElementById('cancelChangePass')?.addEventListener('click', closeChangePassModal);
        document.getElementById('changePassBackdrop')?.addEventListener('click', closeChangePassModal);
        document.getElementById('changePassCard')?.addEventListener('click', e => e.stopPropagation());

        // Toggle password visibility
        changePassContainer.querySelectorAll('.toggle-pass-visibility').forEach(btn => {
            btn.addEventListener('click', () => {
                const target = document.getElementById(btn.dataset.target);
                if (!target) return;
                const isPass = target.type === 'password';
                target.type = isPass ? 'text' : 'password';
                btn.querySelector('.material-symbols-outlined').textContent = isPass ? 'visibility_off' : 'visibility';
            });
        });

        function showClientErrors(messages) {
            if (!errBox || !errList) return;
            errList.innerHTML = messages.map(e => `<p>${e}</p>`).join('');
            errBox.classList.remove('hidden');
        }

        function hideClientErrors() {
            if (!errBox || !errList) return;
            errBox.classList.add('hidden');
            errList.innerHTML = '';
        }

        function renderPasswordPolicy() {
            const password = newPw?.value || '';
            const checks = evaluatePasswordPolicy(password, policy);

            Object.entries(requirementElements).forEach(([key, element]) => {
                if (!(key in checks)) return;
                updateRequirementState(element, Boolean(checks[key]));
            });

            if (bars && strengthLabel) {
                const barEls = bars.children;
                const inactive = 'bg-slate-200 dark:bg-slate-700';
                const strength = getStrengthFromChecks(checks, password);
                for (let i = 0; i < 4; i++) {
                    barEls[i].className = `flex-1 ${i < strength.level ? strength.color : inactive} rounded-full transition-colors`;
                }
                strengthLabel.textContent = `Fortaleza: ${strength.label}`;
                strengthLabel.className = `text-[11px] font-medium ${strength.textColor}`;
            }

            return checks;
        }

        function updateMatch() {
            if (!matchIndicator || !confirmPw || !newPw) return;
            if (confirmPw.value && newPw.value && confirmPw.value === newPw.value) {
                matchIndicator.classList.remove('hidden');
                matchIndicator.className = 'text-[11px] flex items-center gap-1 text-green-500';
                matchIndicator.innerHTML = '<span class="material-symbols-outlined text-[14px]">check_circle</span> Las contraseñas coinciden';
                return true;
            } else if (confirmPw.value && newPw.value) {
                matchIndicator.classList.remove('hidden');
                matchIndicator.className = 'text-[11px] flex items-center gap-1 text-red-500';
                matchIndicator.innerHTML = '<span class="material-symbols-outlined text-[14px]">cancel</span> Las contraseñas no coinciden';
                return false;
            } else {
                matchIndicator.classList.add('hidden');
                return false;
            }
        }

        newPw?.addEventListener('input', () => {
            renderPasswordPolicy();
            updateMatch();
            hideClientErrors();
        });
        confirmPw?.addEventListener('input', updateMatch);
        confirmPw?.addEventListener('input', hideClientErrors);

        renderPasswordPolicy();
        updateMatch();

        // Form submit
        form?.addEventListener('submit', e => {
            e.preventDefault();
            hideClientErrors();

            const checks = renderPasswordPolicy();
            const unmetMessages = [];
            if (!checks.length) unmetMessages.push(`La contraseña debe tener al menos ${policy.requiredLength} caracteres.`);
            if (policy.requireUppercase && !checks.uppercase) unmetMessages.push('La contraseña debe incluir al menos una letra mayúscula.');
            if (policy.requireLowercase && !checks.lowercase) unmetMessages.push('La contraseña debe incluir al menos una letra minúscula.');
            if (policy.requireDigit && !checks.digit) unmetMessages.push('La contraseña debe incluir al menos un número.');
            if (policy.requireSymbol && !checks.symbol) unmetMessages.push('La contraseña debe incluir al menos un símbolo.');
            if (!updateMatch()) unmetMessages.push('La contraseña y la confirmación no coinciden.');

            if (unmetMessages.length > 0) {
                showClientErrors(unmetMessages);
                return;
            }

            const btn = document.getElementById('submitChangePass');
            if (btn) { btn.disabled = true; btn.innerHTML = '<span class="material-symbols-outlined text-sm animate-spin">progress_activity</span> Guardando...'; }

            fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(r => r.json())
                .then(res => {
                    if (res.success) {
                        closeChangePassModal();
                        window.location.reload();
                    } else {
                        if (errBox && errList && res.errors) {
                            errList.innerHTML = res.errors.map(e => `<p>${e}</p>`).join('');
                            errBox.classList.remove('hidden');
                        }
                        if (btn) { btn.disabled = false; btn.textContent = submitButtonText; }
                    }
                })
                .catch(() => {
                    if (errBox && errList) { errList.innerHTML = '<p>Error de conexión. Intentá de nuevo.</p>'; errBox.classList.remove('hidden'); }
                    if (btn) { btn.disabled = false; btn.textContent = submitButtonText; }
                });
        });
    }

    // Delegate click on change-password buttons
    document.addEventListener('click', e => {
        const btn = e.target.closest('.btn-change-pass');
        if (btn) openChangePassModal(btn.dataset.userId);
    });

    // ── Block User Modal ──
    function openBlockUserModal(userId, returnUrl) {
        const params = new URLSearchParams({ id: userId });
        if (returnUrl) params.set('returnUrl', returnUrl);

        fetch(`/Usuarios/Bloquear?${params.toString()}`, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(r => { if (!r.ok) throw new Error(r.status); return r.text(); })
            .then(html => {
                if (!blockUserContainer) return;
                blockUserContainer.innerHTML = html;
                requestAnimationFrame(() => {
                    const backdrop = document.getElementById('blockUserBackdrop');
                    const card = document.getElementById('blockUserCard');
                    if (backdrop) backdrop.classList.replace('opacity-0', 'opacity-100');
                    if (card) {
                        card.classList.replace('scale-95', 'scale-100');
                        card.classList.replace('opacity-0', 'opacity-100');
                    }
                });
                bindBlockUserModal();
            })
            .catch(() => { });
    }

    function closeBlockUserModal() {
        const backdrop = document.getElementById('blockUserBackdrop');
        const card = document.getElementById('blockUserCard');
        if (backdrop) backdrop.classList.replace('opacity-100', 'opacity-0');
        if (card) {
            card.classList.replace('scale-100', 'scale-95');
            card.classList.replace('opacity-100', 'opacity-0');
        }
        setTimeout(() => {
            if (blockUserContainer) blockUserContainer.innerHTML = '';
        }, 300);
    }

    function bindBlockUserModal() {
        const form = document.getElementById('formBlockUser');
        const errBox = document.getElementById('blockUserErrors');
        const errList = document.getElementById('blockUserErrorList');
        const motivoInput = form?.querySelector('[name="MotivoBloqueo"]');
        const bloqueadoHastaInput = form?.querySelector('[name="BloqueadoHasta"]');
        const submitButtonText = 'Confirmar bloqueo';

        function showErrors(messages) {
            if (!errBox || !errList) return;
            errList.innerHTML = messages.map(message => `<p>${message}</p>`).join('');
            errBox.classList.remove('hidden');
        }

        function hideErrors() {
            if (!errBox || !errList) return;
            errBox.classList.add('hidden');
            errList.innerHTML = '';
        }

        document.getElementById('closeBlockUser')?.addEventListener('click', closeBlockUserModal);
        document.getElementById('cancelBlockUser')?.addEventListener('click', closeBlockUserModal);
        document.getElementById('blockUserBackdrop')?.addEventListener('click', closeBlockUserModal);
        document.getElementById('blockUserCard')?.addEventListener('click', e => e.stopPropagation());
        motivoInput?.addEventListener('input', hideErrors);
        bloqueadoHastaInput?.addEventListener('input', hideErrors);

        form?.addEventListener('submit', e => {
            e.preventDefault();
            hideErrors();

            const motivo = (motivoInput?.value || '').trim();
            const bloqueadoHasta = bloqueadoHastaInput?.value || '';
            const errors = [];

            if (!motivo) {
                errors.push('El motivo de bloqueo es requerido.');
            }

            if (bloqueadoHasta) {
                const bloqueoDate = new Date(bloqueadoHasta);
                if (Number.isNaN(bloqueoDate.getTime()) || bloqueoDate.getTime() <= Date.now()) {
                    errors.push('La fecha de bloqueo debe ser posterior a la fecha actual.');
                }
            }

            if (errors.length > 0) {
                showErrors(errors);
                return;
            }

            const btn = document.getElementById('submitBlockUser');
            if (btn) {
                btn.disabled = true;
                btn.innerHTML = '<span class="material-symbols-outlined text-sm animate-spin">progress_activity</span> Bloqueando...';
            }

            fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(r => r.json())
                .then(res => {
                    if (res.success) {
                        closeBlockUserModal();
                        window.location.reload();
                    } else {
                        showErrors(res.errors || ['Error al bloquear el usuario.']);
                        if (btn) {
                            btn.disabled = false;
                            btn.innerHTML = `<span class="material-symbols-outlined text-sm">lock</span> ${submitButtonText}`;
                        }
                    }
                })
                .catch(() => {
                    showErrors(['Error de conexión. Intentá de nuevo.']);
                    if (btn) {
                        btn.disabled = false;
                        btn.innerHTML = `<span class="material-symbols-outlined text-sm">lock</span> ${submitButtonText}`;
                    }
                });
        });
    }

    document.addEventListener('click', e => {
        const btn = e.target.closest('.btn-block-user');
        if (btn) openBlockUserModal(btn.dataset.userId, btn.dataset.returnUrl);
    });

    document.addEventListener('submit', e => {
        const form = e.target.closest('.js-unlock-user-form');
        if (!form) return;

        const userName = form.dataset.userName || 'este usuario';
        if (!window.confirm(`¿Desbloquear a ${userName}?`)) {
            e.preventDefault();
        }
    });
})();
