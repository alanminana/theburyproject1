/**
 * caja-index.js — Módulo de Cajas Index
 * Toast auto-dismiss + modales Crear / Editar caja
 */
(() => {
    'use strict';

    const container = document.getElementById('modalCajaContainer');
    const lgMedia = window.matchMedia('(min-width: 1024px)');

    TheBury.autoDismissToasts();

    // ── Helpers ──
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

    function bindFormAndBackdrop() {
        const form = container.querySelector('#formCaja');
        const backdrop = container.querySelector('#modal-caja-backdrop');
        if (!form) return;

        animateDrawerIn();

        // Cerrar al click en backdrop (mobile)
        if (backdrop) {
            backdrop.addEventListener('click', () => cerrarModalCaja());
        }

        // Interceptar submit
        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            const errBox = container.querySelector('#modal-caja-errors');
            const btn = container.querySelector('#btnGuardarCaja');
            if (errBox) { errBox.classList.add('hidden'); errBox.textContent = ''; }
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
                    cerrarModalCaja();
                    window.location.reload();
                } else if (data.errors && data.errors.length) {
                    if (errBox) {
                        errBox.innerHTML = data.errors.map(e => '<p>' + escapeHtml(e) + '</p>').join('');
                        errBox.classList.remove('hidden');
                    }
                }
            } catch (err) {
                if (errBox) {
                    errBox.textContent = 'Error de conexión. Intente nuevamente.';
                    errBox.classList.remove('hidden');
                }
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

    // ── API pública ──
    window.abrirModalCrearCaja = function () {
        if (!container) return;
        showLoading();
        fetch('/Caja/CreatePartial', { credentials: 'same-origin' })
            .then(res => {
                if (!res.ok) throw new Error('HTTP ' + res.status);
                return res.text();
            })
            .then(html => {
                container.innerHTML = html;
                bindFormAndBackdrop();
                container.querySelector('input[name="Codigo"]')?.focus();
            })
            .catch((err) => {
                container.innerHTML = '';
                console.error('CreatePartial error:', err);
                alert('Error al cargar el formulario.');
            });
    };

    window.abrirModalEditarCaja = function (id) {
        if (!container) return;
        showLoading();
        fetch('/Caja/EditPartial/' + encodeURIComponent(id), { credentials: 'same-origin' })
            .then(res => {
                if (!res.ok) throw new Error('Not found');
                return res.text();
            })
            .then(html => {
                container.innerHTML = html;
                bindFormAndBackdrop();
                container.querySelector('input[name="Codigo"]')?.focus();
            })
            .catch(() => {
                container.innerHTML = '';
                alert('Error al cargar la caja.');
            });
    };

    window.cerrarModalCaja = function () {
        if (!container) return;
        animateDrawerOut(() => { container.innerHTML = ''; });
    };

    // Cerrar con Escape
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && container?.querySelector('#modal-caja')) {
            cerrarModalCaja();
        }
    });
})();
