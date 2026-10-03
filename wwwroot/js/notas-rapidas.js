// notas-rapidas.js — notas rápidas personales (texto libre o checklist) accesibles desde el header de cualquier
// pantalla y como tarjeta en el Dashboard. Ambas vistas comparten un único estado.
// Persisten en el servidor por usuario (api/notas-rapidas), no en el navegador.
(function () {
    'use strict';

    var API = '/api/notas-rapidas';
    var panel, trigger, badge;
    var views = [];
    var notas = [];
    var cerradas = {};      // ids de checklists plegados
    var statusTimer = null;

    function token() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function api(method, url, body) {
        var headers = { 'RequestVerificationToken': token() };
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        return fetch(url, {
            method: method,
            credentials: 'same-origin',
            headers: headers,
            body: body === undefined ? undefined : JSON.stringify(body)
        }).then(function (res) {
            if (res.status === 204) return null;
            return res.json().catch(function () { return {}; }).then(function (data) {
                if (!res.ok) {
                    var detalle = res.status === 404 ? ' (el servidor no tiene esta función: reiniciá la aplicación)' : ' (error ' + res.status + ')';
                    throw new Error((data && data.error) || ('No se pudo completar la operación' + detalle + '.'));
                }
                return data;
            });
        });
    }

    function setStatus(v, msg, isError, autoClear) {
        v.status.textContent = msg || '';
        v.status.classList.toggle('text-red-400', !!isError);
        v.status.classList.toggle('text-slate-400', !isError);
        if (statusTimer) { clearTimeout(statusTimer); statusTimer = null; }
        if (msg && autoClear) statusTimer = setTimeout(function () { v.status.textContent = ''; }, 2500);
    }

    function el(tag, cls, text) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (text !== undefined) e.textContent = text;
        return e;
    }

    function icon(name, cls) {
        var s = el('span', 'material-symbols-outlined ' + (cls || ''), name);
        s.setAttribute('aria-hidden', 'true');
        return s;
    }

    function iconButton(name, label, data) {
        var b = el('button', 'inline-flex size-7 shrink-0 items-center justify-center rounded-lg text-slate-500 transition-colors hover:bg-red-500/10 hover:text-red-400');
        b.type = 'button';
        b.title = label;
        b.setAttribute('aria-label', label);
        Object.keys(data).forEach(function (k) { b.dataset[k] = data[k]; });
        b.appendChild(icon(name, 'text-base'));
        return b;
    }

    function fecha(n) {
        return new Date(n.fecha).toLocaleDateString('es-AR', {
            day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit'
        });
    }

    function renderTexto(n) {
        var li = el('li', 'flex items-start gap-2 rounded-xl border border-slate-800 bg-slate-950/40 px-2.5 py-2');
        var body = el('div', 'min-w-0 flex-1');
        body.appendChild(el('p', 'break-words text-sm text-slate-200 notas-texto', n.texto));
        body.appendChild(el('p', 'notas-meta mt-0.5 text-slate-500', fecha(n)));
        li.append(body, iconButton('close', 'Eliminar nota', { notaDelete: n.id }));
        return li;
    }

    function renderItem(n, it) {
        var li = el('li', 'flex items-start gap-2');
        var cb = el('input', 'mt-0.5 size-4 shrink-0 cursor-pointer rounded border-slate-600 bg-slate-900 text-primary focus:ring-primary');
        cb.type = 'checkbox';
        cb.checked = it.completado;
        cb.dataset.itemToggle = it.id;
        cb.dataset.notaId = n.id;
        cb.setAttribute('aria-label', (it.completado ? 'Marcar como pendiente: ' : 'Marcar como hecho: ') + it.texto);
        var text = el('span', 'min-w-0 flex-1 break-words text-sm ' + (it.completado ? 'text-slate-500 line-through' : 'text-slate-200'), it.texto);
        var del = iconButton('close', 'Eliminar ítem', { itemDelete: it.id, notaId: n.id });
        li.append(cb, text, del);
        return li;
    }

    function renderChecklist(n) {
        var hechos = n.items.filter(function (i) { return i.completado; }).length;
        var abierta = !cerradas[n.id];
        var li = el('li', 'rounded-xl border border-slate-800 bg-slate-950/40 px-2.5 py-2');
        li.dataset.notaId = n.id;

        var head = el('div', 'flex items-center gap-1');
        var toggle = el('button', 'flex min-w-0 flex-1 items-center gap-1.5 rounded-md py-0.5 text-left');
        toggle.type = 'button';
        toggle.dataset.notaFold = n.id;
        toggle.setAttribute('aria-expanded', abierta ? 'true' : 'false');
        var chev = icon('chevron_right', 'text-base text-slate-400 transition-transform');
        if (abierta) chev.style.transform = 'rotate(90deg)';
        var title = el('span', 'min-w-0 flex-1 break-words text-sm font-semibold text-slate-200', n.texto);
        var prog = el('span', 'notas-meta shrink-0 rounded-full border border-slate-700 px-1.5 text-slate-400',
            n.items.length ? hechos + '/' + n.items.length : 'vacío');
        toggle.append(chev, title, prog);
        head.append(toggle, iconButton('delete', 'Eliminar checklist', { notaDelete: n.id }));
        li.appendChild(head);

        if (abierta) {
            var items = el('ul', 'mt-2 space-y-1.5 pl-1');
            n.items.forEach(function (it) { items.appendChild(renderItem(n, it)); });
            li.appendChild(items);

            var add = el('form', 'mt-2 flex items-center gap-1.5');
            add.dataset.itemForm = n.id;
            var inp = el('input', 'min-w-0 flex-1 rounded-md border border-slate-700 bg-slate-950/60 px-2 py-1 text-base text-slate-200 placeholder:text-slate-500 focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary sm:text-xs');
            inp.type = 'text';
            inp.maxLength = 500;
            inp.placeholder = 'Agregar ítem…';
            inp.setAttribute('aria-label', 'Nuevo ítem de ' + n.texto);
            inp.dataset.itemInput = n.id;
            var btn = el('button', 'inline-flex size-7 shrink-0 items-center justify-center rounded-md text-slate-400 hover:bg-slate-800 hover:text-white');
            btn.type = 'submit';
            btn.setAttribute('aria-label', 'Agregar ítem');
            btn.appendChild(icon('add', 'text-base'));
            add.append(inp, btn);
            li.appendChild(add);
            li.appendChild(el('p', 'notas-meta mt-1.5 text-slate-500', fecha(n)));
        }
        return li;
    }

    function render(focusItemOf) {
        var pendientes = 0;
        notas.forEach(function (n) {
            if (n.tipo === 'checklist') pendientes += n.items.filter(function (i) { return !i.completado; }).length;
        });

        views.forEach(function (v) {
            v.loading.hidden = true;
            v.empty.hidden = notas.length > 0;
            v.lista.replaceChildren.apply(v.lista, notas.map(function (n) {
                return n.tipo === 'checklist' ? renderChecklist(n) : renderTexto(n);
            }));
        });

        if (focusItemOf) {
            views.forEach(function (v) {
                if (v.focusItem !== focusItemOf) return;
                var i = v.lista.querySelector('[data-item-input="' + focusItemOf + '"]');
                if (i) i.focus();
            });
        }
        views.forEach(function (v) { v.focusItem = null; });

        badge.textContent = pendientes > 99 ? '99+' : String(pendientes);
        badge.hidden = pendientes === 0;
        trigger.setAttribute('aria-label', pendientes
            ? 'Notas rápidas (' + pendientes + ' pendiente' + (pendientes > 1 ? 's' : '') + ')'
            : 'Notas rápidas');
    }

    function reemplazar(nota) {
        var found = false;
        notas = notas.map(function (n) { if (n.id === nota.id) { found = true; return nota; } return n; });
        if (!found) notas.unshift(nota);
    }

    function load() {
        return api('GET', API).then(function (data) {
            notas = Array.isArray(data) ? data : [];
            render();
        }).catch(function (err) {
            views.forEach(function (v) {
                v.loading.hidden = false;
                v.loading.textContent = err.message;
            });
        });
    }

    function isOpen() { return !panel.classList.contains('hidden'); }

    function open() {
        panel.classList.remove('hidden');
        panel.classList.add('flex');
        trigger.setAttribute('aria-expanded', 'true');
        panel.querySelector('[data-notas-input]').focus();
    }

    function close() {
        panel.classList.add('hidden');
        panel.classList.remove('flex');
        trigger.setAttribute('aria-expanded', 'false');
    }

    function setTipo(v, tipo) {
        v.tipo = tipo;
        v.tipoBtns.forEach(function (b) { b.setAttribute('aria-pressed', b.dataset.notasTipo === tipo ? 'true' : 'false'); });
        v.input.placeholder = tipo === 'checklist'
            ? 'Título del checklist y Enter para crear…'
            : 'Escribí una nota y Enter para guardar…';
        v.input.rows = tipo === 'checklist' ? 1 : 2;
    }

    function bindView(root) {
        var v = {
            tipo: 'texto',
            focusItem: null,
            input: root.querySelector('[data-notas-input]'),
            form: root.querySelector('[data-notas-form]'),
            status: root.querySelector('[data-notas-status]'),
            loading: root.querySelector('[data-notas-loading]'),
            empty: root.querySelector('[data-notas-empty]'),
            lista: root.querySelector('[data-notas-lista]'),
            tipoBtns: Array.prototype.slice.call(root.querySelectorAll('[data-notas-tipo]'))
        };
        views.push(v);

        v.tipoBtns.forEach(function (b) {
            b.addEventListener('click', function () { setTipo(v, b.dataset.notasTipo); v.input.focus(); });
        });

        // Enter envía; Shift+Enter inserta salto de línea (nota de texto).
        v.input.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); v.form.requestSubmit(); }
        });

        v.form.addEventListener('submit', function (e) {
            e.preventDefault();
            var texto = v.input.value.trim();
            if (!texto) return;
            api('POST', API, { tipo: v.tipo, texto: texto }).then(function (nota) {
                notas.unshift(nota);
                v.input.value = '';
                setStatus(v, v.tipo === 'checklist' ? 'Checklist creado: agregá ítems abajo' : 'Guardada', false, true);
                if (nota.tipo === 'checklist') v.focusItem = nota.id;
                render(v.focusItem);
            }).catch(function (err) { setStatus(v, err.message, true); });
        });

        root.addEventListener('submit', function (e) {
            var f = e.target.closest('[data-item-form]');
            if (!f) return;
            e.preventDefault();
            var inp = f.querySelector('[data-item-input]');
            var texto = inp.value.trim();
            if (!texto) return;
            var id = f.dataset.itemForm;
            api('POST', API + '/' + id + '/items', { texto: texto }).then(function (nota) {
                reemplazar(nota);
                v.focusItem = nota.id;
                render(nota.id);
            }).catch(function (err) { setStatus(v, err.message, true); });
        });

        root.addEventListener('change', function (e) {
            var cb = e.target.closest('[data-item-toggle]');
            if (!cb) return;
            api('PATCH', API + '/' + cb.dataset.notaId + '/items/' + cb.dataset.itemToggle, { completado: cb.checked })
                .then(function (nota) { reemplazar(nota); render(); })
                .catch(function (err) { cb.checked = !cb.checked; setStatus(v, err.message, true); });
        });

        root.addEventListener('click', function (e) {
            var fold = e.target.closest('[data-nota-fold]');
            if (fold) {
                var id = fold.dataset.notaFold;
                cerradas[id] = !cerradas[id];
                render();
                return;
            }
            var delItem = e.target.closest('[data-item-delete]');
            if (delItem) {
                api('DELETE', API + '/' + delItem.dataset.notaId + '/items/' + delItem.dataset.itemDelete)
                    .then(function (nota) { reemplazar(nota); render(); })
                    .catch(function (err) { setStatus(v, err.message, true); });
                return;
            }
            var del = e.target.closest('[data-nota-delete]');
            if (del) {
                api('DELETE', API + '/' + del.dataset.notaDelete).then(function () {
                    notas = notas.filter(function (n) { return String(n.id) !== del.dataset.notaDelete; });
                    render();
                }).catch(function (err) { setStatus(v, err.message, true); });
            }
        });
    }

    function init() {
        panel = document.getElementById('notas-panel');
        trigger = document.getElementById('btn-open-notas');
        if (!panel || !trigger) return;

        badge = trigger.querySelector('[data-notas-badge]');
        document.querySelectorAll('[data-notas-root]').forEach(bindView);

        trigger.addEventListener('click', function () { if (isOpen()) close(); else open(); });
        panel.querySelector('[data-notas-close]').addEventListener('click', function () { close(); trigger.focus(); });

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && isOpen()) { close(); trigger.focus(); }
        });
        document.addEventListener('mousedown', function (e) {
            if (isOpen() && !panel.contains(e.target) && !trigger.contains(e.target)) close();
        });

        load();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init, { once: true });
    else init();
})();
