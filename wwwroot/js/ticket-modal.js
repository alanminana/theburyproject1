(function () {
    'use strict';

    var isOpen = false;
    var selectedFiles = [];

    var MAX_FILE_BYTES = 10 * 1024 * 1024;
    var MAX_FILES = 5;
    var ALLOWED_EXTS = new Set(['.jpg','.jpeg','.png','.gif','.webp','.pdf','.doc','.docx','.xls','.xlsx','.txt','.zip']);

    // ── DOM helpers ───────────────────────────────────────────────────────────────
    function el(id) { return document.getElementById(id); }

    // ── Context capture ───────────────────────────────────────────────────────────

    function deriveVistaFromPath() {
        var segments = window.location.pathname.split('/').filter(Boolean);
        if (segments.length >= 2) return segments[0] + ' / ' + segments[1];
        if (segments.length === 1) return segments[0];
        // Fallback: strip app name from document.title
        return document.title.replace(/\s*-\s*TheBuryProject\s*$/i, '').trim() || document.title;
    }

    // Detecta el modal activo más específico sin depender solo de role="dialog".
    // Primero busca aria-modal="true" visibles; descarta slide-in panels cerrados.
    function detectActiveModal() {
        var candidates = document.querySelectorAll('[aria-modal="true"]');
        for (var i = 0; i < candidates.length; i++) {
            var m = candidates[i];
            if (m.id === 'ticket-modal') continue;
            if (m.classList.contains('hidden')) continue;
            if (m.classList.contains('translate-x-full')) continue; // slide panel cerrado
            var heading = m.querySelector('h3, h5, [id$="Label"], [id$="-title"]');
            return heading ? heading.textContent.trim() : (m.id || '');
        }
        return '';
    }

    function captureContext(overrides) {
        overrides = overrides || {};
        var segments = window.location.pathname.split('/').filter(Boolean);
        var activeModal = overrides.modal !== undefined ? overrides.modal : detectActiveModal();
        return {
            moduloOrigen: overrides.modulo     || (segments[0] || ''),
            vistaOrigen:  overrides.vista      || deriveVistaFromPath(),
            urlOrigen:    overrides.url        || (window.location.pathname + window.location.search),
            contextKey:   overrides.contextKey || '',
            origenModal:  activeModal
        };
    }

    // ── Modal open / close ────────────────────────────────────────────────────────

    function open(contextOverride) {
        var ctx = captureContext(contextOverride);
        populateHidden(ctx);
        resetForm();
        el('ticket-modal').classList.remove('hidden');
        el('ticket-modal').classList.add('flex');
        isOpen = true;
        var titleInput = el('ticket-titulo');
        if (titleInput) setTimeout(function () { titleInput.focus(); }, 50);
    }

    function close() {
        el('ticket-modal').classList.add('hidden');
        el('ticket-modal').classList.remove('flex');
        isOpen = false;
    }

    function populateHidden(ctx) {
        var vista = ctx.vistaOrigen;
        if (ctx.origenModal) vista = vista + ' [' + ctx.origenModal + ']';
        el('ticket-modulo-origen').value = ctx.moduloOrigen;
        el('ticket-vista-origen').value  = vista;
        el('ticket-url-origen').value    = ctx.urlOrigen;
        el('ticket-context-key').value   = ctx.contextKey;
    }

    function resetForm() {
        var form = el('form-ticket');
        if (form) form.reset();
        selectedFiles = [];
        renderFileList();
        setError(null);
        setLoading(false);
    }

    // ── Loading / error state ─────────────────────────────────────────────────────

    function setLoading(on) {
        var loadingEl = el('ticket-modal-loading');
        var bodyEl    = el('ticket-modal-body');
        var submitBtn = el('ticket-modal-submit');
        if (loadingEl) loadingEl.classList.toggle('hidden', !on);
        if (bodyEl)    bodyEl.classList.toggle('hidden', on);
        if (submitBtn) {
            submitBtn.disabled = on;
            submitBtn.classList.toggle('opacity-60', on);
            submitBtn.classList.toggle('cursor-not-allowed', on);
        }
    }

    function setError(msg) {
        var errEl   = el('ticket-modal-error');
        var errText = el('ticket-modal-error-text');
        if (!errEl) return;
        if (msg) {
            if (errText) errText.textContent = msg;
            errEl.classList.remove('hidden');
        } else {
            errEl.classList.add('hidden');
            if (errText) errText.textContent = '';
        }
    }

    // ── File attachments ──────────────────────────────────────────────────────────

    function getExt(name) {
        var i = name.lastIndexOf('.');
        return i >= 0 ? name.slice(i).toLowerCase() : '';
    }

    function fmtSize(bytes) {
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
        return (bytes / 1024 / 1024).toFixed(1) + ' MB';
    }

    function fileIcon(name) {
        var ext = getExt(name);
        if (['.jpg','.jpeg','.png','.gif','.webp'].indexOf(ext) >= 0) return 'image';
        if (ext === '.pdf') return 'picture_as_pdf';
        return 'description';
    }

    function escHtml(str) {
        return str.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    }

    function renderFileList() {
        var lista = el('ticket-adjuntos-lista');
        if (!lista) return;
        if (selectedFiles.length === 0) {
            lista.classList.add('hidden');
            lista.innerHTML = '';
            return;
        }
        lista.classList.remove('hidden');
        lista.innerHTML = selectedFiles.map(function (f, i) {
            return '<li class="flex items-center justify-between gap-2 bg-slate-800/60 rounded px-2 py-1.5">' +
                '<div class="flex items-center gap-1.5 min-w-0 text-xs text-slate-300">' +
                '<span class="material-symbols-outlined text-sm text-slate-400 shrink-0">' + fileIcon(f.name) + '</span>' +
                '<span class="truncate">' + escHtml(f.name) + '</span>' +
                '<span class="text-slate-500 shrink-0 ml-1">' + fmtSize(f.size) + '</span>' +
                '</div>' +
                '<button type="button" data-ticket-file-remove="' + i + '" ' +
                'class="text-slate-500 hover:text-rose-400 transition-colors shrink-0 p-0.5" aria-label="Quitar">' +
                '<span class="material-symbols-outlined text-sm pointer-events-none">close</span>' +
                '</button>' +
                '</li>';
        }).join('');
    }

    function addFiles(fileList) {
        setError(null);
        for (var i = 0; i < fileList.length; i++) {
            var f = fileList[i];
            if (selectedFiles.length >= MAX_FILES) {
                setError('Máximo ' + MAX_FILES + ' archivos por ticket.');
                break;
            }
            if (!ALLOWED_EXTS.has(getExt(f.name))) {
                setError('"' + f.name + '" tiene un formato no permitido.');
                continue;
            }
            if (f.size > MAX_FILE_BYTES) {
                setError('"' + f.name + '" supera el límite de 10 MB.');
                continue;
            }
            var dup = selectedFiles.some(function (x) { return x.name === f.name && x.size === f.size; });
            if (!dup) selectedFiles.push(f);
        }
        renderFileList();
        var inp = el('ticket-adjuntos');
        if (inp) inp.value = '';
    }

    function removeFile(idx) {
        selectedFiles.splice(idx, 1);
        renderFileList();
    }

    async function uploadAdjunto(ticketId, file, token) {
        try {
            var fd = new FormData();
            fd.append('archivo', file);
            var headers = {};
            if (token) headers['RequestVerificationToken'] = token;
            var resp = await fetch('/api/tickets/' + ticketId + '/adjuntos', {
                method: 'POST',
                headers: headers,
                body: fd
            });
            if (!resp.ok) {
                var body = await resp.json().catch(function () { return null; });
                return (body && body.error) || ('Error ' + resp.status);
            }
            return null;
        } catch (e) {
            return e.message || 'Error de red';
        }
    }

    // ── Submit ────────────────────────────────────────────────────────────────────

    function getAntiForgeryToken() {
        var input = document.querySelector('#form-ticket input[name="__RequestVerificationToken"]');
        if (!input) input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function buildPayload() {
        var tipoVal = el('ticket-tipo') ? el('ticket-tipo').value : '';
        return {
            titulo:       (el('ticket-titulo')?.value       || '').trim(),
            descripcion:  (el('ticket-descripcion')?.value  || '').trim(),
            tipo:         tipoVal !== '' ? parseInt(tipoVal, 10) : null,
            moduloOrigen: el('ticket-modulo-origen')?.value || null,
            vistaOrigen:  el('ticket-vista-origen')?.value  || null,
            urlOrigen:    el('ticket-url-origen')?.value    || null,
            contextKey:   el('ticket-context-key')?.value   || null
        };
    }

    function validate(payload) {
        if (!payload.titulo)            return 'El título es obligatorio.';
        if (!payload.descripcion)       return 'La descripción es obligatoria.';
        if (payload.tipo === null || isNaN(payload.tipo)) return 'Seleccioná un tipo de incidencia.';
        return null;
    }

    async function submit() {
        var payload = buildPayload();
        var validationError = validate(payload);
        if (validationError) { setError(validationError); return; }

        setLoading(true);
        setError(null);

        try {
            var headers = { 'Content-Type': 'application/json' };
            var token = getAntiForgeryToken();
            if (token) headers['RequestVerificationToken'] = token;

            var resp = await fetch('/api/tickets', {
                method: 'POST',
                headers: headers,
                body: JSON.stringify(payload)
            });

            if (!resp.ok) {
                var body = await resp.json().catch(function () { return null; });
                var errMsg = (body && (body.message || body.error || body.title)) || ('Error ' + resp.status);
                throw new Error(errMsg);
            }

            var created = await resp.json().catch(function () { return null; });
            var ticketId = created && created.id;

            var uploadErrors = [];
            if (selectedFiles.length > 0 && ticketId) {
                for (var i = 0; i < selectedFiles.length; i++) {
                    var uploadErr = await uploadAdjunto(ticketId, selectedFiles[i], token);
                    if (uploadErr) uploadErrors.push(selectedFiles[i].name);
                }
            }

            close();
            document.dispatchEvent(new CustomEvent('ticket:created'));
            if (uploadErrors.length > 0) {
                showSuccessToast('Ticket creado. No se pudieron subir: ' + uploadErrors.join(', '));
            } else {
                showSuccessToast('Incidencia reportada correctamente. ¡Gracias!');
            }
        } catch (e) {
            setError(e.message || 'No se pudo enviar el reporte. Intentá de nuevo.');
        } finally {
            setLoading(false);
        }
    }

    // ── Transient success toast ───────────────────────────────────────────────────

    function showSuccessToast(msg) {
        var toast = document.createElement('div');
        toast.className = [
            'fixed top-4 right-4 z-[70] flex items-center gap-2',
            'bg-emerald-900 border border-emerald-700 text-emerald-200',
            'text-sm px-4 py-3 rounded-lg shadow-xl transition-opacity duration-500'
        ].join(' ');
        toast.innerHTML =
            '<span class="material-symbols-outlined text-base text-emerald-400 shrink-0">check_circle</span>' +
            '<span>' + msg + '</span>';
        document.body.appendChild(toast);
        setTimeout(function () {
            toast.style.opacity = '0';
            setTimeout(function () { toast.remove(); }, 500);
        }, 3500);
    }

    // ── Document-level event delegation ──────────────────────────────────────────

    document.addEventListener('click', function (e) {
        // Backdrop click: close if clicking the dark overlay directly
        if (isOpen && e.target === el('ticket-modal')) {
            close();
            return;
        }

        // Trigger button: open modal
        var trigger = e.target.closest('[data-open-ticket-modal]');
        if (trigger) {
            open({
                modulo:     trigger.dataset.ticketModulo     || undefined,
                vista:      trigger.dataset.ticketVista      || undefined,
                url:        trigger.dataset.ticketUrl        || undefined,
                contextKey: trigger.dataset.ticketContextKey || undefined,
                modal:      trigger.dataset.ticketModal      || undefined
            });
            return;
        }

        // File remove button
        var removeBtn = e.target.closest('[data-ticket-file-remove]');
        if (removeBtn) {
            removeFile(parseInt(removeBtn.dataset.ticketFileRemove, 10));
            return;
        }

        // Action buttons inside modal
        var actionEl = e.target.closest('[data-ticket-modal-action]');
        if (!actionEl) return;
        var action = actionEl.dataset.ticketModalAction;
        if (action === 'close')  close();
        if (action === 'submit') submit();
    });

    document.addEventListener('keydown', function (e) {
        if (isOpen && e.key === 'Escape') close();
    });

    // ── File input wiring ─────────────────────────────────────────────────────────

    var fileInput = el('ticket-adjuntos');
    if (fileInput) {
        fileInput.addEventListener('change', function () {
            if (this.files && this.files.length > 0) addFiles(this.files);
        });
    }

    // ── Public API ────────────────────────────────────────────────────────────────
    window.TicketModal = { open: open, close: close };

})();
