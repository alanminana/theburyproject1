/**
 * caja-index.js — Módulo de Cajas Index
 * Toast auto-dismiss + modales Crear / Editar caja
 */
(() => {
    'use strict';

    const container = document.getElementById('modalCajaContainer');
    const feedbackSlot = document.getElementById('caja-index-feedback-slot');
    const lgMedia = window.matchMedia('(min-width: 1024px)');
    const nativeSubmit = HTMLFormElement.prototype.submit;

    TheBury.autoDismissToasts();
    initScrollAffordances();
    initFilterTabs();

    // ── Helpers ──
    function initScrollAffordances() {
        if (typeof TheBury.initHorizontalScrollAffordance !== 'function') {
            return;
        }

        document.querySelectorAll('[data-oc-scroll]').forEach((root) => {
            TheBury.initHorizontalScrollAffordance(root);
        });
    }

    function initFilterTabs() {
        const tabs = document.querySelectorAll('.filter-tab');
        if (!tabs.length) return;

        function filterCajas(f, btn) {
            tabs.forEach((b) => {
                b.classList.remove('btn-soft');
                b.classList.add('btn-ghost');
                b.setAttribute('aria-pressed', 'false');
            });
            btn.classList.add('btn-soft');
            btn.classList.remove('btn-ghost');
            btn.setAttribute('aria-pressed', 'true');
            document.querySelectorAll('#cajas-activas-tbody tr, #cajas-inactivas-tbody tr, #cajas-m-cards article').forEach((el) => {
                el.hidden = f === 'all' ? el.dataset.state === 'arch' : el.dataset.state !== f;
            });
        }

        tabs.forEach((btn) => {
            btn.addEventListener('click', function () { filterCajas(this.dataset.filter, this); });
        });

        const initialTab = document.querySelector('.filter-tab[data-filter="all"]');
        if (initialTab) filterCajas('all', initialTab);
    }

    function showPageFeedback(message, type) {
        if (!feedbackSlot || !message) return;

        const typeMap = {
            error:   { cls: 'alert-erp-error',   icon: 'error',        role: 'alert'  },
            warning: { cls: 'alert-erp-warning',  icon: 'warning',      role: 'alert'  },
            success: { cls: 'alert-erp-success',  icon: 'check_circle', role: 'status' }
        };
        const v = typeMap[type] || typeMap.error;
        const toast = document.createElement('div');
        toast.className = `toast-msg alert-erp ${v.cls}`;
        toast.setAttribute('role', v.role);
        const iconSpan = document.createElement('span');
        iconSpan.className = 'material-symbols-outlined';
        iconSpan.textContent = v.icon;
        toast.appendChild(iconSpan);
        toast.appendChild(document.createTextNode(message));

        feedbackSlot.replaceChildren(toast);
        TheBury.autoDismissToasts();
    }

    function showLoading() {
        if (!container) return;
        if (lgMedia.matches) {
            container.innerHTML =
                '<div class="lg:h-full">' +
                '<div class="sticky top-0 max-h-[calc(100vh-4rem)] w-[28rem] bg-white dark:bg-slate-900 rounded-2xl border border-slate-200 dark:border-slate-800 shadow-2xl flex flex-col items-center justify-center gap-3 min-h-[12rem]">' +
                '  <span class="material-symbols-outlined text-primary text-3xl animate-spin">progress_activity</span>' +
                '  <p class="text-sm text-slate-500">Cargando...</p>' +
                '</div></div>';
        } else {
            container.innerHTML =
                '<div class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">' +
                '  <div class="bg-white dark:bg-slate-900 rounded-2xl p-8 flex flex-col items-center gap-3 shadow-2xl">' +
                '    <span class="material-symbols-outlined text-primary text-3xl animate-spin">progress_activity</span>' +
                '    <p class="text-sm text-slate-500">Cargando...</p>' +
                '  </div></div>';
        }
    }

    function clearModalErrors() {
        const errBox = container?.querySelector('#modal-caja-errors');
        if (!errBox) return;
        errBox.classList.add('hidden');
        errBox.textContent = '';
    }

    function setModalErrors(messages) {
        const errBox = container?.querySelector('#modal-caja-errors');
        if (!errBox) return;

        const safeMessages = (messages || []).filter(Boolean);
        if (!safeMessages.length) {
            errBox.classList.add('hidden');
            errBox.textContent = '';
            return;
        }

        errBox.innerHTML = safeMessages.map((message) => `<p>${escapeHtml(message)}</p>`).join('');
        errBox.classList.remove('hidden');
    }

    function animateDrawerIn() {
        const panel = container.querySelector('#modal-caja-panel');
        const backdrop = container.querySelector('#modal-caja-backdrop');
        if (!panel) return;

        if (lgMedia.matches) return;

        requestAnimationFrame(() => {
            if (backdrop) {
                backdrop.classList.remove('hidden');
                requestAnimationFrame(() => backdrop.classList.add('opacity-100'));
            }
            panel.classList.remove('translate-x-full');
        });
    }

    function animateDrawerOut(callback) {
        if (lgMedia.matches) { callback?.(); return; }

        const panel = container.querySelector('#modal-caja-panel');
        const backdrop = container.querySelector('#modal-caja-backdrop');
        if (!panel) { callback?.(); return; }

        if (backdrop) backdrop.classList.remove('opacity-100');
        panel.classList.add('translate-x-full');
        panel.addEventListener('transitionend', () => callback?.(), { once: true });
    }

    function closeModal() {
        if (!container) return;
        animateDrawerOut(() => { container.innerHTML = ''; });
    }

    function handleModalFetchError(message, err) {
        if (container) {
            container.innerHTML = '';
        }
        if (err) {
            console.error(message, err);
        }
        showPageFeedback('No se pudo cargar el formulario. Intente nuevamente.', 'error');
    }

    function loadModal(url) {
        if (!container) return;

        showLoading();
        fetch(url, { credentials: 'same-origin' })
            .then((res) => {
                if (!res.ok) {
                    throw new Error('HTTP ' + res.status);
                }
                return res.text();
            })
            .then((html) => {
                container.innerHTML = html;
                bindFormAndBackdrop();
                container.querySelector('input[name="Codigo"]')?.focus();
            })
            .catch((err) => handleModalFetchError(`Modal load error: ${url}`, err));
    }

    function confirmDelete(form) {
        const message = form.dataset.confirmMessage || '¿Estás seguro de que deseas continuar?';
        TheBury.confirmAction(message, () => {
            nativeSubmit.call(form);
        });
    }

    function onCajaGuardada(entity, form) {
        if (!entity) return;

        const isEdit = form && form.querySelector('[name="Id"]') && form.querySelector('[name="Id"]').value !== '0';
        if (isEdit) {
            _handleCajaEditada(entity);
        } else {
            _handleCajaCreada(entity);
        }
    }

    function _handleCajaEditada(entity) {
        const activaTbody = document.getElementById('cajas-activas-tbody');
        const inactivaTbody = document.getElementById('cajas-inactivas-tbody');
        const rowInActiva = activaTbody?.querySelector('[data-caja-row-id="' + entity.id + '"]');
        const rowInInactiva = inactivaTbody?.querySelector('[data-caja-row-id="' + entity.id + '"]');
        const currentRow = rowInActiva || rowInInactiva;
        const wasActive = !!rowInActiva;
        const nowActive = entity.activa !== false;

        if (currentRow && wasActive === nowActive) {
            const cells = currentRow.querySelectorAll('td');
            if (cells.length >= 2) {
                cells[0].innerHTML =
                    '<div class="font-medium ' + (nowActive ? 'text-white' : 'text-slate-400') + '">' +
                    escapeHtml(entity.nombre) + '</div>' +
                    '<div class="mono text-xs muted-2">' + escapeHtml(entity.codigo) + '</div>';
                cells[1].textContent = _ubicacionText(entity);
            }
            _updateMobileCardText(entity);
            showPageFeedback('Caja actualizada: ' + entity.nombre, 'success');
        } else {
            showPageFeedback('Caja actualizada: ' + entity.nombre, 'success');
            setTimeout(() => location.reload(), 800);
        }
    }

    function _handleCajaCreada(entity) {
        const nowActive = entity.activa !== false;
        const state = nowActive ? 'disp' : 'arch';
        const tbody = document.getElementById(nowActive ? 'cajas-activas-tbody' : 'cajas-inactivas-tbody');
        const mCards = document.getElementById('cajas-m-cards');

        if (!tbody && !mCards) {
            showPageFeedback('Caja creada: ' + entity.nombre, 'success');
            setTimeout(() => location.reload(), 800);
            return;
        }

        if (tbody) {
            const tr = _buildCajaDesktopRow(entity, state);
            tbody.appendChild(tr);
            _applyCurrentFilter(tr);
        }

        if (mCards) {
            const card = _buildCajaMobileCard(entity, state);
            mCards.appendChild(card);
            _applyCurrentFilter(card);
        }

        showPageFeedback('Caja creada: ' + entity.nombre, 'success');
    }

    function _ubicacionText(entity) {
        const parts = [];
        if (entity.sucursal) parts.push(entity.sucursal);
        if (entity.ubicacion) parts.push(entity.ubicacion);
        return parts.join(' · ') || '—';
    }

    function _ubicacionHtml(entity) {
        const parts = [];
        if (entity.sucursal) parts.push(escapeHtml(entity.sucursal));
        if (entity.ubicacion) parts.push(escapeHtml(entity.ubicacion));
        return parts.join(' · ') || '—';
    }

    function _updateMobileCardText(entity) {
        const mCards = document.getElementById('cajas-m-cards');
        if (!mCards) return;
        const editBtn = mCards.querySelector('[data-caja-open-edit][data-caja-id="' + entity.id + '"]');
        const card = editBtn?.closest('article');
        if (!card) return;
        const nameEl = card.querySelector('.font-medium');
        const subEl = card.querySelector('.mono.text-xs');
        if (nameEl) nameEl.textContent = entity.nombre;
        if (subEl) subEl.textContent = entity.codigo + ' · ' + (entity.sucursal || '—');
    }

    function _applyCurrentFilter(el) {
        const activeTab = document.querySelector('.filter-tab[aria-pressed="true"]');
        if (!activeTab) return;
        const f = activeTab.dataset.filter;
        const state = el.dataset.state;
        el.hidden = f === 'all' ? state === 'arch' : state !== f;
    }

    function _buildCajaDesktopRow(entity, state) {
        const tr = document.createElement('tr');
        tr.setAttribute('data-caja-row-id', entity.id);
        tr.setAttribute('data-state', state);
        tr.className = 'transition-colors hover:bg-slate-800/30';

        const isArch = state === 'arch';
        const nameClass = isArch ? 'font-medium text-slate-400' : 'font-medium text-white';
        const chipHtml = isArch
            ? '<span class="chip chip-neutral">Archivada</span>'
            : '<span class="chip chip-ok">Disponible</span>';
        const actionsHtml = isArch
            ? '<button type="button" data-caja-open-edit data-caja-id="' + entity.id + '" ' +
              'class="btn btn-ghost btn-sm" title="Reactivar / Editar">' +
              '<span class="material-symbols-outlined">restore_from_trash</span>Reactivar</button>'
            : '<a href="/Caja/Abrir?cajaId=' + entity.id + '" class="btn btn-primary btn-sm no-underline">' +
              '<span class="material-symbols-outlined">lock_open</span>Abrir</a>' +
              '<button type="button" data-caja-open-edit data-caja-id="' + entity.id + '" ' +
              'class="btn btn-ghost btn-sm" aria-label="Editar">' +
              '<span class="material-symbols-outlined">edit</span></button>';

        tr.innerHTML =
            '<td><div class="' + nameClass + '">' + escapeHtml(entity.nombre) + '</div>' +
            '<div class="mono text-xs muted-2">' + escapeHtml(entity.codigo) + '</div></td>' +
            '<td class="muted">' + _ubicacionHtml(entity) + '</td>' +
            '<td>' + chipHtml + '</td>' +
            '<td><div class="row-actions">' + actionsHtml + '</div></td>';

        return tr;
    }

    function _buildCajaMobileCard(entity, state) {
        const article = document.createElement('article');
        article.className = 'card card-pad';
        article.setAttribute('data-state', state);

        const isArch = state === 'arch';
        const nameClass = isArch ? 'font-medium text-slate-400' : 'font-medium text-white';
        const chipHtml = isArch
            ? '<span class="chip chip-neutral">Archivada</span>'
            : '<span class="chip chip-ok">Disponible</span>';
        const subText = escapeHtml(entity.codigo) + ' · ' + escapeHtml(entity.sucursal || '—');
        const actionsHtml = isArch
            ? '<button type="button" data-caja-open-edit data-caja-id="' + entity.id + '" ' +
              'class="btn btn-ghost btn-sm btn-block">' +
              '<span class="material-symbols-outlined">restart_alt</span>Reactivar</button>'
            : '<a href="/Caja/Abrir?cajaId=' + entity.id + '" class="btn btn-primary btn-sm btn-block no-underline">' +
              '<span class="material-symbols-outlined">lock_open</span>Abrir</a>' +
              '<button type="button" data-caja-open-edit data-caja-id="' + entity.id + '" ' +
              'class="btn btn-ghost btn-sm" aria-label="Editar">' +
              '<span class="material-symbols-outlined">edit</span></button>';

        article.innerHTML =
            '<div class="flex items-center justify-between gap-2">' +
            '<div><div class="' + nameClass + '">' + escapeHtml(entity.nombre) + '</div>' +
            '<div class="mono text-xs muted-2">' + subText + '</div></div>' +
            chipHtml + '</div>' +
            '<div class="mt-3 flex gap-2">' + actionsHtml + '</div>';

        return article;
    }

    function bindFormAndBackdrop() {
        const form = container.querySelector('#formCaja');
        const backdrop = container.querySelector('#modal-caja-backdrop');
        if (!form) return;

        animateDrawerIn();

        // Cerrar al click en backdrop (mobile)
        if (backdrop) {
            backdrop.addEventListener('click', () => closeModal());
        }

        // Interceptar submit
        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            const btn = container.querySelector('#btnGuardarCaja');
            clearModalErrors();
            if (btn) { btn.disabled = true; btn.classList.add('opacity-60'); }

            try {
                const formData = new FormData(form);
                // Checkbox "Activa": si no está checked, enviar false explícitamente
                if (!form.querySelector('[name="Activa"]').checked) {
                    formData.set('Activa', 'false');
                }

                const res = await fetch(form.action, {
                    method: 'POST',
                    body: formData,
                    headers: { 'X-Requested-With': 'XMLHttpRequest' },
                    credentials: 'same-origin'
                });
                const data = await res.json();

                if (data.ok) {
                    closeModal();
                    onCajaGuardada(data.entity, form);
                } else if (data.errors && data.errors.length) {
                    setModalErrors(data.errors);
                } else {
                    setModalErrors(['No se pudo completar la operación.']);
                }
            } catch (err) {
                setModalErrors(['Error de conexión. Intente nuevamente.']);
            } finally {
                if (btn) { btn.disabled = false; btn.classList.remove('opacity-60'); }
            }
        });
    }

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    document.addEventListener('click', (e) => {
        const createTrigger = e.target.closest('[data-caja-open-create]');
        if (createTrigger) {
            e.preventDefault();
            loadModal('/Caja/CreatePartial');
            return;
        }

        const editTrigger = e.target.closest('[data-caja-open-edit]');
        if (editTrigger) {
            e.preventDefault();
            const id = editTrigger.dataset.cajaId;
            if (!id) {
                showPageFeedback('No se pudo identificar la caja seleccionada.', 'error');
                return;
            }
            loadModal('/Caja/EditPartial/' + encodeURIComponent(id));
            return;
        }

        const closeTrigger = e.target.closest('[data-caja-close-modal]');
        if (closeTrigger && container?.contains(closeTrigger)) {
            e.preventDefault();
            closeModal();
        }
    });

    document.addEventListener('submit', (e) => {
        const deleteForm = e.target.closest('form[data-caja-delete-form]');
        if (!deleteForm) return;

        e.preventDefault();
        confirmDelete(deleteForm);
    });

    // Cerrar con Escape
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && container?.querySelector('#modal-caja')) {
            closeModal();
        }
    });
})();
