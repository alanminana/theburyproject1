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

    // ── Helpers ──
    function initScrollAffordances() {
        if (typeof TheBury.initHorizontalScrollAffordance !== 'function') {
            return;
        }

        document.querySelectorAll('[data-oc-scroll]').forEach((root) => {
            TheBury.initHorizontalScrollAffordance(root);
        });
    }

    function showPageFeedback(message, type) {
        if (!feedbackSlot || !message) return;

        const variants = {
            error: {
                wrapper: 'bg-rose-500/10 border-rose-500/20 text-rose-500',
                iconBg: 'bg-rose-500/20 text-rose-500',
                title: 'Error',
                icon: 'error'
            },
            warning: {
                wrapper: 'bg-amber-500/10 border-amber-500/20 text-amber-500',
                iconBg: 'bg-amber-500/20 text-amber-500',
                title: 'Atención',
                icon: 'warning'
            },
            success: {
                wrapper: 'bg-emerald-500/10 border-emerald-500/20 text-emerald-600 dark:text-emerald-400',
                iconBg: 'bg-emerald-500/20 text-emerald-600',
                title: 'Listo',
                icon: 'check_circle'
            }
        };

        const variant = variants[type] || variants.error;
        const toast = document.createElement('div');
        toast.className = `toast-msg flex items-center gap-4 border p-4 rounded-xl ${variant.wrapper}`;
        toast.setAttribute('role', 'alert');
        toast.innerHTML =
            `<div class="${variant.iconBg} p-2 rounded-lg">` +
            `  <span class="material-symbols-outlined">${variant.icon}</span>` +
            '</div>' +
            '<div>' +
            `  <p class="text-sm font-bold leading-tight">${variant.title}</p>` +
            `  <p class="text-xs font-medium opacity-80">${escapeHtml(message)}</p>` +
            '</div>';

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

        var isEdit = form && form.querySelector('[name="Id"]') && form.querySelector('[name="Id"]').value !== '0';
        var sucursalUbicacionHtml = entity.sucursal
            ? '<span class="text-sm text-slate-700 dark:text-slate-300">' + escapeHtml(entity.sucursal) + '</span>'
            : '<span class="text-sm text-slate-400">—</span>';

        var tbody = document.getElementById('cajas-activas-tbody');

        if (isEdit && tbody) {
            var row = tbody.querySelector('[data-caja-row-id="' + entity.id + '"]');
            if (row) {
                var cells = row.querySelectorAll('td');
                if (cells.length >= 3) {
                    cells[0].textContent = entity.codigo;
                    cells[1].textContent = entity.nombre;
                    cells[2].innerHTML = sucursalUbicacionHtml;
                }
            }
            showPageFeedback('Caja actualizada: ' + entity.nombre, 'success');
        } else if (tbody) {
            var tr = document.createElement('tr');
            tr.setAttribute('data-caja-row-id', entity.id);
            tr.className = 'hover:bg-slate-50 dark:hover:bg-slate-800/30 transition-colors';
            tr.innerHTML =
                '<td class="px-6 py-4 text-sm font-mono text-slate-500">' + escapeHtml(entity.codigo) + '</td>' +
                '<td class="px-6 py-4 text-sm font-bold text-slate-900 dark:text-white">' + escapeHtml(entity.nombre) + '</td>' +
                '<td class="px-6 py-4">' + sucursalUbicacionHtml + '</td>' +
                '<td class="px-6 py-4"><div class="flex items-center gap-2 text-slate-400"><span class="size-2 rounded-full bg-slate-400"></span><span class="text-xs font-bold uppercase">Cerrada</span></div></td>' +
                '<td class="px-6 py-4 text-right"><div class="flex min-w-max flex-wrap items-center justify-end gap-2">' +
                  '<a href="/Caja/Abrir?cajaId=' + entity.id + '" class="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg bg-primary text-white text-xs font-bold hover:opacity-90 transition-all no-underline"><span class="material-symbols-outlined text-base">lock_open</span><span class="caja-index-action-label">Abrir caja</span></a>' +
                '</div></td>';
            tbody.appendChild(tr);
            showPageFeedback('Caja creada: ' + entity.nombre, 'success');
        }
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
