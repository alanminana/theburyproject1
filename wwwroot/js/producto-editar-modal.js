(function () {
    'use strict';

    var MODAL_ID   = 'modal-editar-producto';
    var FORM_ID    = 'form-editar-producto';
    var VAL_BOX_ID = 'prod-edit-validation-summary';
    var VAL_TXT_ID = 'prod-edit-validation-text';

    var caractIndex = 0;
    var _tabs = null;
    // Cada apertura del modal tiene un número de secuencia: toda respuesta asíncrona (GetJson, subcategorías,
    // submarcas) que llega con una secuencia vieja se descarta.
    var openSeq = 0;
    var clickSeq = 0;
    // Estado de carga de los selects dependientes. Mientras 'loading' o 'error' el select queda deshabilitado
    // (un campo deshabilitado no se envía, y EditAjax interpreta un campo ausente como "conservar lo guardado");
    // solo un select 'ready' puede enviar "" para significar "quitar la selección".
    var DEP_SELECTS = {
        subcategoria: { id: 'prod-edit-subcategoriaId', status: 'ready', token: 0, cambiado: false, label: 'subcategorías' },
        submarca:     { id: 'prod-edit-submarcaId',     status: 'ready', token: 0, cambiado: false, label: 'submarcas' }
    };

    function el(id) { return document.getElementById(id); }

    // ── Abrir / Cerrar ──────────────────────────────────────────

    var _panel = null;
    function panel() {
        if (!_panel) {
            _panel = window.InvUI.panelFormulario(el(MODAL_ID), {
                cierres: '[data-prod-edit-modal-close]',
                cuentaComoCambio: '[data-prod-edit-add-caract], .prod-edit-remove-caract',
                alCerrar: limpiar
            });
        }
        return _panel;
    }

    function open() {
        panel().abrir();
        activateTab('datos');
        var firstInput = el('prod-edit-codigo');
        if (firstInput) firstInput.focus();
    }

    function limpiar() {
        openSeq++;
        invalidarCargasDependientes();
        clearErrors();
        hideValidation();
    }

    // El navegador devuelve el foco al disparador al cerrar el <dialog>.
    function close() { panel().cerrar(); }

    // ── Populate ────────────────────────────────────────────────

    function syncAlicuotaToIVA(alicuotaSel, ivaSelect) {
        if (!alicuotaSel || !alicuotaSel.value || !ivaSelect) return;
        var opt = alicuotaSel.options[alicuotaSel.selectedIndex];
        var pct = opt && opt.getAttribute('data-porcentaje');
        if (pct !== null && pct !== '') ivaSelect.value = String(pct);
    }

    function populate(data) {
        openSeq++;
        el('prod-edit-id').value          = data.id;
        el('prod-edit-rowversion').value  = data.rowVersion || '';
        el('prod-edit-codigo').value      = data.codigo || '';
        el('prod-edit-nombre').value      = data.nombre || '';
        el('prod-edit-descripcion').value = data.descripcion || '';
        // PrecioCompra persiste el costo real final (compra con IVA + gastos); acá se
        // descompone para precargar compra y gastos por separado.
        var costoEnvio    = Number(data.costoEnvio) || 0;
        var percepciones  = Number(data.percepcionesCompra) || 0;
        var otrosCostos   = Number(data.otrosCostosCompra) || 0;
        var compraConIva  = Math.max(0, (Number(data.precioCompra) || 0) - costoEnvio - percepciones - otrosCostos);
        el('prod-edit-costoEnvio').value          = costoEnvio;
        el('prod-edit-percepcionesCompra').value  = percepciones;
        el('prod-edit-otrosCostosCompra').value   = otrosCostos;
        el('prod-edit-precioCompra').value = compraConIva.toFixed(2);
        // Alícuota de compra informada por el proveedor; si el producto no la tiene, 21%.
        var ivaCompraEl = el('prod-edit-porcentajeIVACompra');
        if (ivaCompraEl) {
            ivaCompraEl.value = data.porcentajeIVACompra != null ? String(Number(data.porcentajeIVACompra)) : '21';
            if (!ivaCompraEl.value) ivaCompraEl.value = '21';
        }
        // precioVenta ya es el precio final de venta con IVA incluido.
        el('prod-edit-precioFinal').value  = data.precioVenta || 0;
        el('prod-edit-comisionPorcentaje').value = data.comisionPorcentaje || 0;
        var maxCuotasEl = el('prod-edit-maxCuotasSinInteres');
        if (maxCuotasEl) maxCuotasEl.value = data.maxCuotasSinInteresPermitidas != null ? String(data.maxCuotasSinInteresPermitidas) : '';
        el('prod-edit-stockActual').value  = data.stockActual || 0;
        el('prod-edit-stockMinimo').value  = data.stockMinimo || 0;
        el('prod-edit-activo').checked     = !!data.activo;

        // IVA: alícuota primero, luego fallback interno (hidden PorcentajeIVA)
        var alicuotaSel = el('prod-edit-alicuotaIVAId');
        var ivaHidden   = el('prod-edit-porcentajeIVA');
        if (alicuotaSel) alicuotaSel.value = data.alicuotaIVAId != null ? String(data.alicuotaIVAId) : '';
        if (alicuotaSel && alicuotaSel.value) {
            syncAlicuotaToIVA(alicuotaSel, ivaHidden);
        } else if (ivaHidden) {
            ivaHidden.value = String(data.porcentajeIVA ?? 21);
        }

        // Categoría
        populateCategoriaSelect(data.categoriaId, data.subcategoriaId, data.subcategoriaNombre);

        // Marca
        populateMarcaSelect(data.marcaId, data.submarcaId, data.submarcaNombre);

        // Características
        renderCaracteristicas(data.caracteristicas || []);

        // Crédito personal (planes propios del producto)
        renderCreditoPersonal(data.creditoPersonal);

        var form = el(FORM_ID);
        if (form) form.action = '/Producto/EditAjax/' + data.id;

        renderContextoPrecio(data);
    }

    function populateCategoriaSelect(categoriaId, subcategoriaId, subcategoriaNombre) {
        var sel = el('prod-edit-categoriaId');
        if (!sel) return;
        sel.innerHTML = '<option value="">Seleccionar categoría</option>';
        var lista = (window.CatalogoData && window.CatalogoData.categorias) || [];
        lista.forEach(function (c) {
            var opt = document.createElement('option');
            opt.value = c.id;
            opt.textContent = c.nombre;
            if (String(c.id) === String(categoriaId)) opt.selected = true;
            sel.appendChild(opt);
        });
        loadSubcategorias(categoriaId, subcategoriaId, subcategoriaNombre);
    }

    function populateMarcaSelect(marcaId, submarcaId, submarcaNombre) {
        var sel = el('prod-edit-marcaId');
        if (!sel) return;
        sel.innerHTML = '<option value="">Seleccionar marca</option>';
        var lista = (window.CatalogoData && window.CatalogoData.marcas) || [];
        lista.forEach(function (m) {
            var opt = document.createElement('option');
            opt.value = m.id;
            opt.textContent = m.nombre;
            if (String(m.id) === String(marcaId)) opt.selected = true;
            sel.appendChild(opt);
        });
        loadSubmarcas(marcaId, submarcaId, submarcaNombre);
    }

    function loadSubcategorias(categoriaId, selectedId, selectedNombre, cambioDelUsuario) {
        loadDependiente(DEP_SELECTS.subcategoria, 'Sin subcategoría', '/Producto/GetSubcategorias/',
            categoriaId, selectedId, selectedNombre, cambioDelUsuario);
    }

    function loadSubmarcas(marcaId, selectedId, selectedNombre, cambioDelUsuario) {
        loadDependiente(DEP_SELECTS.submarca, 'Sin submarca', '/Producto/GetSubmarcas/',
            marcaId, selectedId, selectedNombre, cambioDelUsuario);
    }

    function loadDependiente(dep, textoVacio, url, parentId, selectedId, selectedNombre, cambioDelUsuario) {
        var sel = el(dep.id);
        if (!sel) return;
        var token = ++dep.token;
        dep.cambiado = !!cambioDelUsuario;
        sel.innerHTML = '';
        sel.appendChild(new Option(textoVacio, ''));
        if (!parentId) {
            // Sin padre no hay opciones que cargar: "sin selección" es el estado real, no una carga pendiente.
            setDepStatus(dep, sel, 'ready');
            return;
        }
        setDepStatus(dep, sel, 'loading');
        var seq = openSeq;
        fetch(url + parentId, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) {
                if (!r.ok) throw new Error('HTTP ' + r.status);
                return r.json();
            })
            .then(function (list) {
                if (!Array.isArray(list)) throw new Error('Respuesta inválida');
                if (token !== dep.token || seq !== openSeq) return; // respuesta tardía de otra carga u otro producto
                list.forEach(function (s) {
                    var opt = document.createElement('option');
                    opt.value = s.id;
                    opt.textContent = s.nombre;
                    if (selectedId && String(s.id) === String(selectedId)) opt.selected = true;
                    sel.appendChild(opt);
                });
                if (selectedId && !sel.value && selectedNombre) {
                    var opt = document.createElement('option');
                    opt.value = selectedId;
                    opt.textContent = selectedNombre;
                    opt.selected = true;
                    sel.appendChild(opt);
                }
                setDepStatus(dep, sel, 'ready');
            })
            .catch(function () {
                if (token !== dep.token || seq !== openSeq) return;
                // Se muestra la relación guardada (solo lectura): el select deshabilitado no se envía y el servidor la conserva.
                if (selectedId && selectedNombre) {
                    var guardada = new Option(selectedNombre, selectedId, true, true);
                    sel.appendChild(guardada);
                }
                setDepStatus(dep, sel, 'error');
            });
    }

    function setDepStatus(dep, sel, status) {
        dep.status = status;
        sel.disabled = status !== 'ready';
        sel.setAttribute('aria-busy', status === 'loading' ? 'true' : 'false');
        if (status === 'error') sel.title = 'No se pudieron cargar las ' + dep.label + '.';
        else sel.removeAttribute('title');
        syncGuardarButton();
    }

    function hayCargaPendiente() {
        return DEP_SELECTS.subcategoria.status === 'loading' || DEP_SELECTS.submarca.status === 'loading';
    }

    // Botón "Guardar cambios" inactivo mientras haya una carga en curso. No toca el estado "Guardando..." del submit.
    function syncGuardarButton() {
        var btn = el('btn-guardar-producto-edit');
        var form = el(FORM_ID);
        if (!btn || (form && form.getAttribute('data-guardando') === '1')) return;
        btn.disabled = hayCargaPendiente();
    }

    // Devuelve un mensaje si el estado actual no permite guardar sin riesgo de perder o falsear una relación.
    function bloqueoPorCargaDependiente() {
        if (hayCargaPendiente()) {
            return 'Se están cargando subcategorías y submarcas. Esperá un momento e intentá nuevamente.';
        }
        var fallidos = Object.keys(DEP_SELECTS).map(function (k) { return DEP_SELECTS[k]; })
            .filter(function (d) { return d.status === 'error' && d.cambiado; });
        if (fallidos.length) {
            return 'No se pudieron cargar las ' + fallidos.map(function (d) { return d.label; }).join(' ni las ') +
                   ' de la selección actual. Volvé a elegir la categoría o la marca e intentá nuevamente.';
        }
        return null;
    }

    function invalidarCargasDependientes() {
        Object.keys(DEP_SELECTS).forEach(function (k) {
            DEP_SELECTS[k].token++;
            DEP_SELECTS[k].status = 'ready';
            DEP_SELECTS[k].cambiado = false;
        });
    }

    // ── Características ─────────────────────────────────────────

    function renderCaracteristicas(list) {
        var tbody = el('prod-edit-caracteristicas-body');
        if (!tbody) return;
        tbody.innerHTML = '';
        caractIndex = 0;
        list.forEach(function (c) { addCaracteristicaRow(c.nombre, c.valor, c.id); });
        updateCaractCount();
    }

    function addCaracteristicaRow(nombre, valor, id) {
        var tbody = el('prod-edit-caracteristicas-body');
        if (!tbody) return;
        var i = caractIndex++;
        var tr = document.createElement('tr');
        tr.className = 'border-t border-slate-800';
        tr.innerHTML =
            (id ? '<input type="hidden" name="Caracteristicas[' + i + '].Id" value="' + id + '" />' : '') +
            '<td class="px-4 py-2"><input name="Caracteristicas[' + i + '].Nombre" type="text" value="' + escHtml(nombre || '') + '" class="w-full bg-transparent border-none text-sm text-white placeholder-slate-500 focus:ring-0 outline-none" placeholder="Característica" aria-label="Característica" /></td>' +
            '<td class="px-4 py-2"><input name="Caracteristicas[' + i + '].Valor" type="text" value="' + escHtml(valor || '') + '" class="w-full bg-transparent border-none text-sm text-white placeholder-slate-500 focus:ring-0 outline-none" placeholder="Valor" aria-label="Valor" /></td>' +
            '<td class="px-4 py-2 text-center"><button type="button" aria-label="Quitar característica" class="prod-edit-remove-caract text-slate-500 hover:text-red-400 transition-colors"><span class="material-symbols-outlined text-base">delete</span></button></td>';
        tbody.appendChild(tr);
        tr.querySelector('.prod-edit-remove-caract').addEventListener('click', function () {
            tr.remove();
            reindexCaracteristicas();
            updateCaractCount();
        });
        updateCaractCount();
    }

    function reindexCaracteristicas() {
        var tbody = el('prod-edit-caracteristicas-body');
        if (!tbody) return;
        caractIndex = 0;
        tbody.querySelectorAll('tr').forEach(function (tr) {
            var inputs = tr.querySelectorAll('input');
            var hiddenId = null;
            inputs.forEach(function (inp) {
                var n = inp.name;
                if (!n) return;
                var newName = n.replace(/Caracteristicas\[\d+\]/, 'Caracteristicas[' + caractIndex + ']');
                if (inp.type === 'hidden') hiddenId = inp;
                inp.name = newName;
            });
            if (hiddenId) hiddenId.name = 'Caracteristicas[' + caractIndex + '].Id';
            caractIndex++;
        });
    }

    function updateCaractCount() {
        var tbody = el('prod-edit-caracteristicas-body');
        var count = el('prod-edit-caracteristicas-count');
        if (tbody && count) count.textContent = tbody.querySelectorAll('tr').length + ' cargadas';
    }

    // ── Crédito personal ────────────────────────────────────────

    var CP_PREVIEW_URL = '/Producto/PreviewRecargoCreditoPersonal';

    function creditoPersonalElementos() {
        return {
            admiteHidden: el('prod-edit-credito-admite'),
            cardsWrap: document.querySelector('#' + FORM_ID + ' [data-cp-cards-wrap]'),
            cardsCont: el('prod-edit-credito-cards'),
            maxCuotasWrap: document.querySelector('#' + FORM_ID + ' [data-cp-maxcuotas-wrap]')
        };
    }

    function renderCreditoPersonal(cp) {
        cp = cp || {};
        var maxc = el('prod-edit-credito-maxcuotas');
        if (maxc) maxc.value = (cp.maxCuotasCredito != null) ? String(cp.maxCuotasCredito) : '';

        var modo = cp.modo || 'HeredaGlobal';
        document.querySelectorAll('#' + FORM_ID + ' [data-cp-modo]').forEach(function (r) {
            r.checked = (r.value === modo);
        });

        var cont = el('prod-edit-credito-cards');
        window.ProductoCreditoPersonalUI.renderCards(cont, cp.cuotas || [], 'CreditoPersonal.Cuotas', CP_PREVIEW_URL);
        window.ProductoCreditoPersonalUI.aplicarModo(modo, creditoPersonalElementos());
    }

    function initCreditoPersonalModo() {
        var radios = document.querySelectorAll('#' + FORM_ID + ' [data-cp-modo]');
        if (radios.length) window.ProductoCreditoPersonalUI.wireModo(radios, creditoPersonalElementos());
    }

    // ── Solapas ─────────────────────────────────────────────────
    // Autoridad compartida con producto-crear-modal.js (ver producto-modal-tabs.js):
    // mismo patrón ARIA/teclado para ambos modales, cada uno con sus propios
    // data-attribute.

    function initTabs() {
        _tabs = window.ProductoModalTabs.wire({
            root: el(FORM_ID),
            tabAttr: 'data-prod-edit-tab',
            panelAttr: 'data-prod-edit-panel',
            idPrefix: 'prod-edit'
        });
    }

    function activateTab(name) {
        if (_tabs) _tabs.activate(name);
    }

    // El cálculo/desglose de precios vive en producto-precio-calculo.js
    // (bindPrecioProducto); acá no se duplican listeners de precio.

    // ── Contexto de precio vigente ──────────────────────────────

    function renderContextoPrecio(data) {
        var texto = el('prod-edit-precio-contexto-texto');
        if (!texto) return;
        while (texto.firstChild) texto.removeChild(texto.firstChild);
        if (data.tienePrecioLista) {
            var precio = Number(data.precioActual).toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            var lista = data.listaPrecioActualNombre || 'lista';
            texto.appendChild(document.createTextNode('Precio vigente actual: '));
            var strong = document.createElement('strong');
            strong.className = 'text-primary font-bold';
            strong.textContent = '$ ' + precio;
            texto.appendChild(strong);
            texto.appendChild(document.createTextNode(' por lista '));
            var em = document.createElement('em');
            em.textContent = lista;
            texto.appendChild(em);
            texto.appendChild(document.createTextNode('. Este formulario modifica solo el precio base (el que rige cuando no hay precio por lista).'));
        } else {
            texto.textContent = 'No hay precio por lista vigente: la venta usa este precio base.';
        }
    }

    var _validacion = ProductoModalFormUtils.bindValidation(VAL_BOX_ID, VAL_TXT_ID, {});
    function showValidation(text) { _validacion.showValidation(text); }
    function hideValidation() { _validacion.hideValidation(); }

    function clearErrors() {
        ProductoModalFormUtils.limpiarMarcas(el(FORM_ID));
        document.querySelectorAll('#' + FORM_ID + ' [data-valmsg-for]').forEach(function (s) {
            s.textContent = ''; s.classList.add('hidden');
        });
        document.querySelectorAll('#' + FORM_ID + ' .border-red-500').forEach(function (i) {
            i.classList.remove('border-red-500');
        });
    }

    function handleServerErrors(errors) {
        const primero = ProductoModalFormUtils.handleServerErrors(errors, showValidation, '#' + FORM_ID);
        jumpToFirstErrorTab(errors);
        if (primero) primero.focus();
    }

    function jumpToFirstErrorTab(errors) {
        if (!_tabs) return;
        var fields = Object.keys(errors || {});
        for (var i = 0; i < fields.length; i++) {
            var tabName = _tabs.findTabForField(fields[i]);
            if (tabName) { _tabs.activate(tabName); return; }
        }
    }

    function escHtml(str) {
        var d = document.createElement('div');
        d.textContent = str || '';
        return d.innerHTML;
    }

    // ── Submit AJAX ─────────────────────────────────────────────

    function initSubmit() {
        var form = el(FORM_ID);
        if (!form) return;

        form.addEventListener('submit', async function (e) {
            e.preventDefault();
            hideValidation();
            clearErrors();

            var bloqueo = bloqueoPorCargaDependiente();
            if (bloqueo) {
                showValidation(bloqueo);
                return;
            }

            var btn = el('btn-guardar-producto-edit');
            var origHTML = btn.innerHTML;
            form.setAttribute('data-guardando', '1');
            btn.disabled = true;
            btn.innerHTML = '<span class="material-symbols-outlined text-[18px] animate-spin">progress_activity</span> Guardando...';

            try {
                var resp = await fetch(form.action, {
                    method: 'POST',
                    body: new FormData(form),
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });
                var result = await resp.json();
                if (result.success) {
                    var editado = result.entity && result.entity.id;
                    close();
                    if (window.InvProductos) window.InvProductos.refrescar({ resaltar: editado });
                    document.dispatchEvent(new CustomEvent('catalogo:toast', {
                        detail: { message: result.message || 'Producto actualizado', type: 'success' }
                    }));
                    // B8: avisos no bloqueantes del servidor (p. ej. precio de venta por debajo del costo).
                    (result.advertencias || []).forEach(function (msg) {
                        document.dispatchEvent(new CustomEvent('catalogo:toast', {
                            detail: { message: msg, type: 'warning' }
                        }));
                    });
                } else if (result.errors) {
                    handleServerErrors(result.errors);
                }
            } catch (_) {
                showValidation('Error de conexión. Intentá nuevamente.');
            } finally {
                form.removeAttribute('data-guardando');
                btn.innerHTML = origHTML;
                syncGuardarButton();
            }
        });
    }

    // ── Apertura ────────────────────────────────────────────────
    function abrirPorId(id, trigger) {
        var seqClick = ++clickSeq;
        return fetch('/Producto/GetJson/' + encodeURIComponent(id), { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (resp) {
                if (!resp.ok) throw new Error();
                return resp.json();
            })
            .then(function (data) {
                if (seqClick !== clickSeq) return; // se pidió otro producto después: esta respuesta ya no aplica
                populate(data);
                open();
            })
            .catch(function () {
                document.dispatchEvent(new CustomEvent('catalogo:toast', {
                    detail: { message: 'Error al cargar el producto.', type: 'error' }
                }));
            });
    }

    // ── Event delegation ────────────────────────────────────────

    function initDelegatedEvents() {
        document.addEventListener('click', async function (e) {
            // Abrir modal edit
            var editBtn = e.target.closest('[data-prod-edit-id]');
            if (editBtn) {
                abrirPorId(editBtn.getAttribute('data-prod-edit-id'), editBtn);
                return;
            }

            // Agregar característica
            if (e.target.closest('[data-prod-edit-add-caract]')) {
                var nombreInput = el('prod-edit-caract-nombre');
                var valorInput  = el('prod-edit-caract-valor');
                var nombre = nombreInput ? nombreInput.value.trim() : '';
                var valor  = valorInput  ? valorInput.value.trim()  : '';
                if (nombre && valor) {
                    addCaracteristicaRow(nombre, valor, null);
                    if (nombreInput) nombreInput.value = '';
                    if (valorInput)  valorInput.value  = '';
                }
                return;
            }
        });

        // Dropdown dependientes
        var catSel = el('prod-edit-categoriaId');
        if (catSel) {
            catSel.addEventListener('change', function () {
                loadSubcategorias(this.value, null, null, true);
            });
        }
        var marcaSel = el('prod-edit-marcaId');
        if (marcaSel) {
            marcaSel.addEventListener('change', function () {
                loadSubmarcas(this.value, null, null, true);
            });
        }

        // Enter agrega la característica; antes enviaba el formulario, guardaba, cerraba y la perdía.
        ['prod-edit-caract-nombre', 'prod-edit-caract-valor'].forEach(function (id) {
            var campo = el(id);
            if (!campo) return;
            campo.addEventListener('keydown', function (e) {
                if (e.key !== 'Enter') return;
                e.preventDefault();
                var boton = document.querySelector('[data-prod-edit-add-caract]');
                if (boton) boton.click();
                var nombre = el('prod-edit-caract-nombre');
                if (nombre && !nombre.value) nombre.focus();
            });
        });
    }

    // Deep link desde otras pantallas (ej. Productos asociados de Proveedor):
    // /Catalogo?editarProducto={id} abre este modal con el producto ya cargado.
    function abrirDesdeQueryString() {
        var id;
        try { id = new URLSearchParams(window.location.search).get('editarProducto'); } catch (_) { return; }
        if (!id || !/^\d+$/.test(id)) return;
        // El producto puede no estar en la página visible; sin permiso de edición el servidor rechaza la carga.
        var tbody = document.getElementById('productos-tbody');
        if (tbody && tbody.getAttribute('data-can-edit') === 'true') abrirPorId(id, null);
        // Evita reabrir el modal al recargar o volver con "atrás".
        try {
            var url = new URL(window.location.href);
            url.searchParams.delete('editarProducto');
            window.history.replaceState(null, '', url.pathname + url.search + url.hash);
        } catch (_) { /* sin history API: no es crítico */ }
    }

    document.addEventListener('DOMContentLoaded', function () {
        initTabs();
        initSubmit();
        initDelegatedEvents();
        initCreditoPersonalModo();
        panel();
        abrirDesdeQueryString();
    });
})();
