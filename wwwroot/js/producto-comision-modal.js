(function () {
    'use strict';

    var modal = null;
    var currentBtn = null;

    function el(id) { return document.getElementById(id); }

    function openModal(btn) {
        if (!modal) modal = document.getElementById('modal-comision-vendedor');
        if (!modal) return;

        currentBtn = btn;
        var nombre = btn.getAttribute('data-comision-producto-nombre') || '—';
        var productoId = btn.getAttribute('data-comision-producto-id') || '';
        var porcentaje = btn.getAttribute('data-comision-porcentaje') || '0';

        var nombreEl = el('comision-modal-nombre');
        if (nombreEl) nombreEl.textContent = nombre;

        var idEl = el('comision-modal-productoId');
        if (idEl) idEl.value = productoId;

        var pctEl = el('comision-modal-porcentaje');
        if (pctEl) pctEl.value = porcentaje;

        hideMessages();
        modal.classList.remove('hidden');
        modal.classList.add('flex');

        setTimeout(function () {
            var inp = el('comision-modal-porcentaje');
            if (inp) { inp.focus(); inp.select(); }
        }, 50);
    }

    function closeModal() {
        if (!modal) return;
        modal.classList.remove('flex');
        modal.classList.add('hidden');
        currentBtn = null;
    }

    function hideMessages() {
        var val = el('comision-modal-validation');
        var suc = el('comision-modal-success');
        if (val) val.classList.add('hidden');
        if (suc) suc.classList.add('hidden');
    }

    function showError(msg) {
        var box = el('comision-modal-validation');
        var txt = el('comision-modal-validation-text');
        if (!box || !txt) return;
        txt.textContent = msg;
        box.classList.remove('hidden');
    }

    function showSuccess(msg) {
        var box = el('comision-modal-success');
        var txt = el('comision-modal-success-text');
        if (!box || !txt) return;
        txt.textContent = msg;
        box.classList.remove('hidden');
    }

    function setBtnLoading(btn, loading) {
        if (loading) {
            btn.disabled = true;
            btn.dataset.origText = btn.textContent.trim();
            while (btn.firstChild) btn.removeChild(btn.firstChild);
            var spinner = document.createElement('span');
            spinner.className = 'material-symbols-outlined text-[18px] animate-spin';
            spinner.textContent = 'progress_activity';
            btn.appendChild(spinner);
            btn.appendChild(document.createTextNode(' Guardando...'));
        } else {
            btn.disabled = false;
            while (btn.firstChild) btn.removeChild(btn.firstChild);
            var icon = document.createElement('span');
            icon.className = 'material-symbols-outlined text-[18px]';
            icon.textContent = 'save';
            btn.appendChild(icon);
            btn.appendChild(document.createTextNode(' Guardar'));
        }
    }

    function updateRowDisplay(productoId, nuevoValor) {
        var row = document.querySelector('tr[data-catalogo-producto-id="' + productoId + '"]');
        if (!row) return;

        var span = row.querySelector('[data-producto-comision]');
        if (span) {
            span.textContent = nuevoValor > 0
                ? nuevoValor.toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%'
                : 'Sin comisión';
        }

        var rowBtn = row.querySelector('[data-comision-producto-id]');
        if (rowBtn) {
            rowBtn.setAttribute('data-comision-porcentaje', nuevoValor);
            rowBtn.title = 'Comisión vendedor: ' + nuevoValor.toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%';
        }
    }

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-comision-producto-id]');
        if (btn) { openModal(btn); return; }

        if (e.target.closest('[data-comision-modal-close]')) { closeModal(); }
    });

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && modal && !modal.classList.contains('hidden')) closeModal();
    });

    document.addEventListener('submit', function (e) {
        var f = e.target;
        if (!f || f.id !== 'form-comision-vendedor') return;
        e.preventDefault();
        handleSubmit(f);
    });

    async function handleSubmit(f) {
        hideMessages();

        var pctEl = el('comision-modal-porcentaje');
        var raw = (pctEl ? pctEl.value : '').trim().replace(',', '.');
        var val = parseFloat(raw);
        if (isNaN(val) || val < 0 || val > 100) {
            showError('El porcentaje debe estar entre 0 y 100.');
            return;
        }

        var submitBtn = el('btn-guardar-comision');
        if (submitBtn) setBtnLoading(submitBtn, true);

        try {
            var fd = new FormData(f);
            fd.set('porcentajeComision', raw);

            var resp = await fetch(f.action, {
                method: 'POST',
                body: fd,
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });

            if (!resp.ok) { showError('Error de servidor (' + resp.status + '). Intentá nuevamente.'); return; }

            var data = await resp.json();
            if (data.success) {
                var productoId = el('comision-modal-productoId').value;
                updateRowDisplay(productoId, data.comisionPorcentaje);
                showSuccess(data.message || 'Comisión actualizada exitosamente.');
                setTimeout(closeModal, 1400);
            } else {
                showError(data.message || 'Error al guardar la comisión.');
            }
        } catch (_) {
            showError('Error de comunicación. Intentá nuevamente.');
        } finally {
            if (submitBtn) setBtnLoading(submitBtn, false);
        }
    }

}());
