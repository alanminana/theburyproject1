// credito-details.js — Modal logic for Credito Details page
(function () {
    'use strict';

    const btnAprobar = document.getElementById('btn-aprobar');
    const btnRechazar = document.getElementById('btn-rechazar');
    const btnCancelar = document.getElementById('btn-cancelar');

    const modalAprobar = document.getElementById('modal-aprobar');
    const modalRechazar = document.getElementById('modal-rechazar');
    const modalCancelar = document.getElementById('modal-cancelar');

    function openModal(modal) {
        if (!modal) return;
        modal.classList.remove('hidden');
        modal.classList.add('flex');
    }

    function closeModal(modal) {
        if (!modal) return;
        modal.classList.add('hidden');
        modal.classList.remove('flex');
    }

    if (btnAprobar && modalAprobar) {
        btnAprobar.addEventListener('click', function () { openModal(modalAprobar); });
    }
    if (btnRechazar && modalRechazar) {
        btnRechazar.addEventListener('click', function () { openModal(modalRechazar); });
    }
    if (btnCancelar && modalCancelar) {
        btnCancelar.addEventListener('click', function () { openModal(modalCancelar); });
    }

    // Close buttons inside modals
    document.querySelectorAll('.modal-close').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var modal = btn.closest('.fixed');
            if (modal) closeModal(modal);
        });
    });

    // Close on backdrop click
    [modalAprobar, modalRechazar, modalCancelar].forEach(function (modal) {
        if (!modal) return;
        modal.addEventListener('click', function (e) {
            if (e.target === modal) closeModal(modal);
        });
    });

    // Close on Escape
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') {
            [modalAprobar, modalRechazar, modalCancelar].forEach(function (modal) {
                if (modal && !modal.classList.contains('hidden')) closeModal(modal);
            });
        }
    });
})();
