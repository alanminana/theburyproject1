// ticket-panel.js — panel lateral de gestión de incidentes
(function () {
    'use strict';

    // ── DOM refs ──────────────────────────────────────────────────────────
    var panel, overlay, panelTitle, backBtn, closeBtn;
    var viewLista, viewDetalle;
    var listaLoading, listaError, listaErrorText, listaRetry, listaEmpty, listaItems, listaMore, loadMoreBtn;
    var detalleLoading, detalleContent;
    var filterBusqueda, filterEstado, filterTipo, filterBtn;

    // ── State ─────────────────────────────────────────────────────────────
    var state = {
        view: 'lista',
        currentPage: 1,
        hasNextPage: false,
        currentTicketId: null,
        loaded: false,
        canEdit: false,
        canStatus: false,
        canResolve: false,
        canCreate: false
    };

    // ── Config ────────────────────────────────────────────────────────────
    var estadoCfg = {
        0: { label: 'Pendiente', cls: 'border border-amber-500/20 bg-amber-500/10 text-amber-400' },
        1: { label: 'En Curso',  cls: 'border border-primary/20 bg-primary/10 text-primary' },
        2: { label: 'Resuelto',  cls: 'border border-emerald-500/20 bg-emerald-500/10 text-emerald-400' },
        3: { label: 'Cancelado', cls: 'border border-slate-700 bg-slate-800/50 text-slate-500' }
    };

    var tipoCfg = {
        0: { label: 'Error texto',     cls: 'bg-orange-500/10 text-orange-400' },
        1: { label: 'Error funcional', cls: 'bg-red-500/10 text-red-400' },
        2: { label: 'Mejora',          cls: 'bg-blue-500/10 text-blue-400' },
        3: { label: 'Nueva func.',     cls: 'bg-purple-500/10 text-purple-400' },
        4: { label: 'Eliminación',     cls: 'bg-rose-500/10 text-rose-400' },
        5: { label: 'Fusión',          cls: 'bg-cyan-500/10 text-cyan-400' },
        6: { label: 'Otro',            cls: 'bg-slate-700/50 text-slate-400' }
    };

    var transicionesCfg = {
        0: [{ label: 'Iniciar',   value: 1, cls: 'bg-primary/10 border border-primary/20 text-primary hover:bg-primary/20' },
            { label: 'Cancelar',  value: 3, cls: 'bg-slate-700 text-slate-300 hover:bg-slate-600' }],
        1: [{ label: 'Resolver',  value: 2, cls: 'bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 hover:bg-emerald-500/20' },
            { label: 'Cancelar',  value: 3, cls: 'bg-slate-700 text-slate-300 hover:bg-slate-600' }],
        2: [{ label: 'Reabrir',   value: 1, cls: 'bg-primary/10 border border-primary/20 text-primary hover:bg-primary/20' }],
        3: [{ label: 'Reactivar', value: 0, cls: 'bg-amber-500/10 border border-amber-500/20 text-amber-400 hover:bg-amber-500/20' }]
    };

    // ── Init ──────────────────────────────────────────────────────────────
    function init() {
        panel = document.getElementById('ticket-panel');
        if (!panel) return;

        overlay        = document.getElementById('ticket-panel-overlay');
        panelTitle     = document.getElementById('tp-panel-title');
        backBtn        = document.getElementById('tp-back-btn');
        closeBtn       = document.getElementById('tp-close-btn');
        viewLista      = document.getElementById('tp-view-lista');
        viewDetalle    = document.getElementById('tp-view-detalle');
        listaLoading   = document.getElementById('tp-lista-loading');
        listaError     = document.getElementById('tp-lista-error');
        listaErrorText = document.getElementById('tp-lista-error-text');
        listaRetry     = document.getElementById('tp-lista-retry');
        listaEmpty     = document.getElementById('tp-lista-empty');
        listaItems     = document.getElementById('tp-lista-items');
        listaMore      = document.getElementById('tp-lista-more');
        loadMoreBtn    = document.getElementById('tp-load-more');
        detalleLoading = document.getElementById('tp-detalle-loading');
        detalleContent = document.getElementById('tp-detalle-content');
        filterBusqueda = document.getElementById('tp-filter-busqueda');
        filterEstado   = document.getElementById('tp-filter-estado');
        filterTipo     = document.getElementById('tp-filter-tipo');
        filterBtn      = document.getElementById('tp-filter-btn');

        state.canEdit    = panel.dataset.canEdit    === 'true';
        state.canStatus  = panel.dataset.canStatus  === 'true';
        state.canResolve = panel.dataset.canResolve === 'true';
        state.canCreate  = panel.dataset.canCreate  === 'true';

        // Layout trigger
        var openTrigger = document.getElementById('btn-open-ticket-panel');
        if (openTrigger) openTrigger.addEventListener('click', function () { openPanel(); });

        // Panel header
        closeBtn.addEventListener('click', closePanel);
        backBtn.addEventListener('click', goToLista);
        if (overlay) overlay.addEventListener('click', closePanel);

        // Filters
        if (filterBtn) filterBtn.addEventListener('click', function () { loadLista(1); });
        if (filterBusqueda) {
            filterBusqueda.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') loadLista(1);
            });
        }
        if (listaRetry)  listaRetry.addEventListener('click',  function () { loadLista(1); });
        if (loadMoreBtn) loadMoreBtn.addEventListener('click', function () { loadLista(state.currentPage + 1, true); });

        // List item click → detail
        if (listaItems) {
            listaItems.addEventListener('click', function (e) {
                var item = e.target.closest('[data-tp-ticket]');
                if (item) openDetalle(parseInt(item.dataset.tpTicket, 10));
            });
        }

        // Escape key
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && isOpen()) closePanel();
        });

        // Refresh after ticket creation
        document.addEventListener('ticket:created', function () {
            state.loaded = false;
            if (isOpen() && state.view === 'lista') loadLista(1);
        });
    }

    // ── Open / close ──────────────────────────────────────────────────────
    function isOpen() {
        return panel && !panel.classList.contains('translate-x-full');
    }

    function openPanel(ticketId) {
        if (!panel) return;
        panel.classList.remove('translate-x-full');
        panel.removeAttribute('aria-hidden');
        if (overlay) { overlay.classList.remove('hidden'); overlay.classList.add('flex'); }
        document.body.style.overflow = 'hidden';

        if (typeof ticketId === 'number') {
            openDetalle(ticketId);
        } else {
            goToLista();
            if (!state.loaded) loadLista(1);
        }
    }

    function closePanel() {
        if (!panel) return;
        panel.classList.add('translate-x-full');
        panel.setAttribute('aria-hidden', 'true');
        if (overlay) { overlay.classList.add('hidden'); overlay.classList.remove('flex'); }
        document.body.style.overflow = '';
    }

    // ── View switching ────────────────────────────────────────────────────
    function goToLista() {
        state.view = 'lista';
        viewLista.classList.remove('hidden');
        viewDetalle.classList.add('hidden');
        viewDetalle.classList.remove('flex');
        backBtn.classList.add('hidden');
        panelTitle.textContent = 'Incidentes';
    }

    function goToDetalle() {
        state.view = 'detalle';
        viewLista.classList.add('hidden');
        viewDetalle.classList.remove('hidden');
        viewDetalle.classList.add('flex');
        backBtn.classList.remove('hidden');
        panelTitle.textContent = 'Detalle';
    }

    // ── Lista ─────────────────────────────────────────────────────────────
    function loadLista(page, append) {
        state.currentPage = page || 1;
        if (!append) {
            setListaState('loading');
            if (listaItems) listaItems.innerHTML = '';
        }

        var params = new URLSearchParams();
        var q  = filterBusqueda ? filterBusqueda.value.trim() : '';
        var es = filterEstado   ? filterEstado.value           : '';
        var tp = filterTipo     ? filterTipo.value             : '';
        if (q)  params.set('Busqueda', q);
        if (es) params.set('Estado',   es);
        if (tp) params.set('Tipo',     tp);
        params.set('Page',     String(state.currentPage));
        params.set('PageSize', '20');

        fetch('/api/tickets?' + params.toString())
            .then(function (r) { return r.ok ? r.json() : Promise.reject(); })
            .then(function (data) {
                var items = Array.isArray(data) ? data : (data.items || []);
                state.hasNextPage = !!(data.hasNextPage);
                state.loaded = true;

                if (!append && items.length === 0) {
                    setListaState('empty');
                } else {
                    setListaState('items');
                    renderListaItems(items, append);
                }
                if (listaMore) listaMore.classList.toggle('hidden', !state.hasNextPage);
            })
            .catch(function () { setListaState('error'); });
    }

    function setListaState(s) {
        var els = [listaLoading, listaError, listaEmpty, listaItems];
        els.forEach(function (el) {
            if (!el) return;
            el.classList.add('hidden');
            el.classList.remove('flex');
        });
        var target = s === 'loading' ? listaLoading
                   : s === 'error'   ? listaError
                   : s === 'empty'   ? listaEmpty
                   : listaItems;
        if (target) {
            target.classList.remove('hidden');
            if (s !== 'items') target.classList.add('flex');
        }
    }

    function renderListaItems(items, append) {
        if (!append && listaItems) listaItems.innerHTML = '';
        items.forEach(function (t) {
            var ec = estadoCfg[t.estado] || estadoCfg[0];
            var tc = tipoCfg[t.tipo]     || tipoCfg[6];
            var li = document.createElement('li');
            li.className = 'px-4 py-3 cursor-pointer transition-colors hover:bg-slate-800/40 active:bg-slate-800';
            li.setAttribute('data-tp-ticket', t.id);
            li.innerHTML =
                '<div class="flex items-start justify-between gap-2 min-w-0">' +
                '<p class="flex-1 min-w-0 text-sm font-semibold text-white truncate leading-snug">' + esc(t.titulo) + '</p>' +
                '<span class="shrink-0 text-[10px] text-slate-500 leading-none mt-0.5">' + relDate(t.createdAt) + '</span>' +
                '</div>' +
                '<div class="mt-1.5 flex flex-wrap items-center gap-1.5">' +
                '<span class="inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ' + ec.cls + '">' + ec.label + '</span>' +
                '<span class="inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-semibold ' + tc.cls + '">' + tc.label + '</span>' +
                (t.moduloOrigen ? '<span class="text-[10px] text-slate-600">' + esc(t.moduloOrigen) + '</span>' : '') +
                '</div>';
            listaItems.appendChild(li);
        });
    }

    // ── Detalle ───────────────────────────────────────────────────────────
    function openDetalle(id) {
        state.currentTicketId = id;
        goToDetalle();

        detalleLoading.classList.remove('hidden');
        detalleLoading.classList.add('flex');
        detalleContent.classList.add('hidden');
        detalleContent.innerHTML = '';

        fetch('/api/tickets/' + id)
            .then(function (r) { return r.ok ? r.json() : Promise.reject(); })
            .then(function (ticket) {
                panelTitle.textContent = '#' + ticket.id;
                detalleLoading.classList.add('hidden');
                detalleLoading.classList.remove('flex');
                detalleContent.classList.remove('hidden');
                renderDetalle(ticket);
            })
            .catch(function () {
                detalleLoading.classList.add('hidden');
                detalleLoading.classList.remove('flex');
                detalleContent.classList.remove('hidden');
                detalleContent.innerHTML =
                    '<div class="flex items-center gap-2 rounded-xl border border-red-500/20 bg-red-500/10 p-3 text-sm text-red-400">' +
                    '<span class="material-symbols-outlined text-lg">error</span>Error al cargar el ticket.</div>';
            });
    }

    function renderDetalle(ticket) {
        var estado   = ticket.estado;
        var editable = estado === 0 || estado === 1;
        var ec   = estadoCfg[estado]    || estadoCfg[0];
        var tc   = tipoCfg[ticket.tipo] || tipoCfg[6];
        var trans = transicionesCfg[estado] || [];
        var html = '';

        // ─ Info
        html += '<div class="rounded-xl border border-slate-800 bg-slate-800/30 p-4 space-y-2.5">';
        html += '<div class="flex flex-wrap items-center gap-1.5">';
        html += '<span class="inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ' + ec.cls + '">' + ec.label + '</span>';
        html += '<span class="inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-semibold ' + tc.cls + '">' + tc.label + '</span>';
        if (ticket.moduloOrigen) html += '<span class="text-xs text-slate-500">' + esc(ticket.moduloOrigen) + '</span>';
        html += '</div>';
        html += '<p class="text-base font-bold text-white leading-snug">' + esc(ticket.titulo) + '</p>';
        if (ticket.descripcion) {
            html += '<p class="text-sm text-slate-300 leading-relaxed">' + esc(ticket.descripcion).replace(/\n/g, '<br>') + '</p>';
        }
        if (ticket.vistaOrigen) {
            html += '<p class="text-[11px] text-slate-600">Vista: ' + esc(ticket.vistaOrigen) + '</p>';
        }
        html += '<div class="flex flex-wrap gap-3 text-xs text-slate-600">';
        if (ticket.createdBy) html += '<span>Por: <span class="text-slate-400">' + esc(ticket.createdBy) + '</span></span>';
        if (ticket.createdAt) html += '<span>' + new Date(ticket.createdAt).toLocaleString('es-AR', { dateStyle: 'short', timeStyle: 'short' }) + '</span>';
        html += '</div>';
        html += '</div>';

        // ─ Estado actions
        if (state.canStatus && trans.length > 0) {
            html += '<div class="space-y-2">';
            html += '<p class="text-[10px] font-bold uppercase tracking-widest text-slate-500">Cambiar estado</p>';
            html += '<div class="flex flex-wrap gap-2">';
            trans.forEach(function (opt) {
                html += '<button type="button" class="inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-bold transition-colors ' + opt.cls + '" data-tp-do="estado" data-tp-val="' + opt.value + '">' + opt.label + '</button>';
            });
            html += '</div></div>';
        }

        // ─ Resolución
        if (ticket.resolucion) {
            html += '<div class="space-y-2">';
            html += '<p class="text-[10px] font-bold uppercase tracking-widest text-slate-500">Resolución</p>';
            html += '<div class="rounded-xl border border-emerald-500/20 bg-emerald-500/5 p-3 space-y-1">';
            html += '<p class="text-sm text-emerald-300 leading-relaxed">' + esc(ticket.resolucion).replace(/\n/g, '<br>') + '</p>';
            if (ticket.resueltoPor) {
                html += '<p class="text-xs text-slate-500">Por: ' + esc(ticket.resueltoPor);
                if (ticket.fechaResolucion) html += ' · ' + new Date(ticket.fechaResolucion).toLocaleString('es-AR', { dateStyle: 'short', timeStyle: 'short' });
                html += '</p>';
            }
            html += '</div></div>';
        } else if (state.canResolve && estado === 1) {
            html += '<div class="space-y-2">';
            html += '<p class="text-[10px] font-bold uppercase tracking-widest text-slate-500">Registrar resolución</p>';
            html += '<textarea id="tp-resolucion-txt" class="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm text-slate-200 placeholder:text-slate-600 resize-none focus:border-primary focus:outline-none transition-colors" rows="3" placeholder="Cómo se resolvió el problema..."></textarea>';
            html += '<button type="button" data-tp-do="resolver" class="inline-flex items-center gap-1.5 rounded-lg bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 px-3 py-1.5 text-xs font-bold hover:bg-emerald-500/20 transition-colors"><span class="material-symbols-outlined text-sm">check_circle</span>Confirmar resolución</button>';
            html += '</div>';
        }

        // ─ Checklist
        var checkItems = ticket.checklistItems || [];
        var doneCnt = checkItems.filter(function (i) { return i.completado; }).length;
        html += '<div class="space-y-2">';
        html += '<p class="text-[10px] font-bold uppercase tracking-widest text-slate-500">Checklist';
        if (checkItems.length > 0) html += ' <span class="font-normal text-slate-600">(' + doneCnt + '/' + checkItems.length + ')</span>';
        html += '</p>';
        if (checkItems.length > 0) {
            html += '<ul class="space-y-1.5">';
            checkItems.forEach(function (item) {
                html += '<li class="flex items-center gap-2.5 group" data-cid="' + item.id + '">';
                html += '<input type="checkbox" class="h-4 w-4 shrink-0 rounded accent-primary cursor-pointer"' +
                    (item.completado ? ' checked' : '') +
                    (state.canEdit ? '' : ' disabled') + ' data-tp-do="toggle-check">';
                html += '<span class="flex-1 text-sm ' + (item.completado ? 'line-through text-slate-500' : 'text-slate-300') + '">' + esc(item.descripcion) + '</span>';
                if (state.canEdit && editable) {
                    html += '<button type="button" class="opacity-0 group-hover:opacity-100 text-slate-600 hover:text-red-400 transition-all" data-tp-do="del-check" title="Eliminar"><span class="material-symbols-outlined text-base leading-none">close</span></button>';
                }
                html += '</li>';
            });
            html += '</ul>';
        } else {
            html += '<p class="text-xs text-slate-600">Sin ítems en el checklist.</p>';
        }
        if (state.canEdit && editable) {
            html += '<div class="flex gap-2">';
            html += '<input id="tp-check-input" type="text" class="flex-1 rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm text-slate-200 placeholder:text-slate-600 focus:border-primary focus:outline-none transition-colors" placeholder="Nuevo ítem..." />';
            html += '<button type="button" data-tp-do="add-check" class="p-2 rounded-lg bg-slate-800 text-slate-400 hover:text-white hover:bg-slate-700 transition-colors" title="Agregar"><span class="material-symbols-outlined text-base leading-none">add</span></button>';
            html += '</div>';
        }
        html += '</div>';

        // ─ Adjuntos
        var adjuntos = ticket.adjuntos || [];
        html += '<div class="space-y-2">';
        html += '<p class="text-[10px] font-bold uppercase tracking-widest text-slate-500">Adjuntos</p>';
        if (adjuntos.length > 0) {
            html += '<ul class="space-y-1.5">';
            adjuntos.forEach(function (adj) {
                var isImg = adj.tipoMIME && adj.tipoMIME.startsWith('image/');
                var icon  = isImg ? 'image' : 'attach_file';
                var size  = adj.tamanoBytes > 1048576
                    ? (adj.tamanoBytes / 1048576).toFixed(1) + ' MB'
                    : Math.round(adj.tamanoBytes / 1024) + ' KB';
                html += '<li class="flex items-center gap-2 group" data-aid="' + adj.id + '">';
                html += '<span class="material-symbols-outlined text-sm text-slate-500 shrink-0">' + icon + '</span>';
                html += '<a href="/' + esc(adj.rutaArchivo) + '" target="_blank" rel="noopener" class="flex-1 min-w-0 text-xs text-slate-300 hover:text-primary truncate no-underline">' + esc(adj.nombreArchivo) + '</a>';
                html += '<span class="shrink-0 text-[10px] text-slate-600">' + size + '</span>';
                if (state.canEdit && editable) {
                    html += '<button type="button" class="opacity-0 group-hover:opacity-100 text-slate-600 hover:text-red-400 transition-all" data-tp-do="del-adj" title="Eliminar adjunto"><span class="material-symbols-outlined text-base leading-none">close</span></button>';
                }
                html += '</li>';
            });
            html += '</ul>';
        } else {
            html += '<p class="text-xs text-slate-600">Sin archivos adjuntos.</p>';
        }
        if (state.canEdit && editable) {
            html += '<label class="inline-flex cursor-pointer items-center gap-1.5 rounded-lg border border-slate-700 bg-slate-800/50 px-3 py-1.5 text-xs font-semibold text-slate-400 hover:text-white hover:border-slate-600 transition-colors">';
            html += '<span class="material-symbols-outlined text-sm">upload_file</span>Subir archivo';
            html += '<input type="file" class="hidden" id="tp-file-input" accept=".pdf,.doc,.docx,.xls,.xlsx,.txt,.jpg,.jpeg,.png,.gif,.webp,.zip">';
            html += '</label>';
        }
        html += '</div>';

        detalleContent.innerHTML = html;
        wireDetalle(ticket);
    }

    function wireDetalle(ticket) {
        var id       = ticket.id;
        var editable = ticket.estado === 0 || ticket.estado === 1;

        detalleContent.querySelectorAll('[data-tp-do="estado"]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                apiCambiarEstado(id, parseInt(btn.dataset.tpVal, 10));
            });
        });

        var resolverBtn = detalleContent.querySelector('[data-tp-do="resolver"]');
        if (resolverBtn) {
            resolverBtn.addEventListener('click', function () {
                var txt = document.getElementById('tp-resolucion-txt');
                if (txt && txt.value.trim()) apiResolver(id, txt.value.trim());
            });
        }

        detalleContent.querySelectorAll('[data-tp-do="toggle-check"]').forEach(function (chk) {
            chk.addEventListener('change', function () {
                var itemId = parseInt(chk.closest('[data-cid]').dataset.cid, 10);
                apiToggleCheck(itemId, chk.checked);
            });
        });

        detalleContent.querySelectorAll('[data-tp-do="del-check"]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var itemId = parseInt(btn.closest('[data-cid]').dataset.cid, 10);
                apiDeleteCheck(itemId, id);
            });
        });

        var addCheckBtn = detalleContent.querySelector('[data-tp-do="add-check"]');
        if (addCheckBtn) {
            addCheckBtn.addEventListener('click', function () {
                var inp = document.getElementById('tp-check-input');
                if (inp && inp.value.trim()) apiAddCheck(id, inp.value.trim());
            });
        }
        var checkInput = document.getElementById('tp-check-input');
        if (checkInput) {
            checkInput.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    var btn = detalleContent.querySelector('[data-tp-do="add-check"]');
                    if (btn) btn.click();
                }
            });
        }

        detalleContent.querySelectorAll('[data-tp-do="del-adj"]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var adjId = parseInt(btn.closest('[data-aid]').dataset.aid, 10);
                apiDeleteAdj(adjId, id);
            });
        });

        var fileInput = document.getElementById('tp-file-input');
        if (fileInput) {
            fileInput.addEventListener('change', function () {
                if (fileInput.files && fileInput.files[0]) {
                    apiSubirAdj(id, fileInput.files[0]);
                    fileInput.value = '';
                }
            });
        }
    }

    // ── API ───────────────────────────────────────────────────────────────
    function jsonPost(url, method, body) {
        return fetch(url, {
            method: method,
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
    }

    function apiCambiarEstado(ticketId, nuevoEstado) {
        jsonPost('/api/tickets/' + ticketId + '/estado', 'PATCH', { nuevoEstado: nuevoEstado })
            .then(function (r) {
                if (r.ok || r.status === 204) { state.loaded = false; openDetalle(ticketId); }
                else r.json().then(function (d) { toast(d.error || 'Error al cambiar estado.', 'error'); });
            })
            .catch(function () { toast('Error de conexión.', 'error'); });
    }

    function apiResolver(ticketId, texto) {
        jsonPost('/api/tickets/' + ticketId + '/resolucion', 'PATCH', { resolucion: texto })
            .then(function (r) {
                if (r.ok || r.status === 204) { state.loaded = false; openDetalle(ticketId); }
                else r.json().then(function (d) { toast(d.error || 'Error al registrar resolución.', 'error'); });
            })
            .catch(function () { toast('Error de conexión.', 'error'); });
    }

    function apiToggleCheck(itemId, completado) {
        jsonPost('/api/tickets/checklist/' + itemId, 'PATCH', { completado: completado })
            .catch(function () { toast('Error al actualizar checklist.', 'error'); });
    }

    function apiAddCheck(ticketId, descripcion) {
        jsonPost('/api/tickets/' + ticketId + '/checklist', 'POST', { descripcion: descripcion, orden: 0 })
            .then(function (r) { if (r.ok) openDetalle(ticketId); })
            .catch(function () { toast('Error al agregar ítem.', 'error'); });
    }

    function apiDeleteCheck(itemId, ticketId) {
        fetch('/api/tickets/checklist/' + itemId, { method: 'DELETE' })
            .then(function (r) { if (r.ok || r.status === 204) openDetalle(ticketId); })
            .catch(function () { toast('Error al eliminar ítem.', 'error'); });
    }

    function apiDeleteAdj(adjId, ticketId) {
        fetch('/api/tickets/adjuntos/' + adjId, { method: 'DELETE' })
            .then(function (r) { if (r.ok || r.status === 204) openDetalle(ticketId); })
            .catch(function () { toast('Error al eliminar adjunto.', 'error'); });
    }

    function apiSubirAdj(ticketId, file) {
        var fd = new FormData();
        fd.append('archivo', file);
        fetch('/api/tickets/' + ticketId + '/adjuntos', { method: 'POST', body: fd })
            .then(function (r) {
                if (r.ok) { openDetalle(ticketId); }
                else r.json().then(function (d) { toast(d.error || 'Error al subir el archivo.', 'error'); });
            })
            .catch(function () { toast('Error de conexión.', 'error'); });
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    function esc(str) {
        return String(str || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function relDate(dateStr) {
        if (!dateStr) return '';
        var d    = new Date(dateStr);
        var mins = Math.floor((Date.now() - d.getTime()) / 60000);
        if (mins < 2)  return 'ahora';
        if (mins < 60) return mins + 'm';
        var hrs = Math.floor(mins / 60);
        if (hrs < 24)  return hrs + 'h';
        var days = Math.floor(hrs / 24);
        if (days < 30) return days + 'd';
        return d.toLocaleDateString('es-AR', { day: '2-digit', month: '2-digit' });
    }

    function toast(msg, type) {
        document.dispatchEvent(new CustomEvent('catalogo:toast', {
            detail: { message: msg, type: type || 'success' }
        }));
    }

    // ── Public API ────────────────────────────────────────────────────────
    window.TicketPanel = {
        open:    openPanel,
        close:   closePanel,
        refresh: function () { state.loaded = false; if (isOpen()) loadLista(1); }
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
