(function () {
    'use strict';

    var MODAL_ID   = 'modal-editar-producto';
    var FORM_ID    = 'form-editar-producto';
    var VAL_BOX_ID = 'prod-edit-validation-summary';
    var VAL_TXT_ID = 'prod-edit-validation-text';

    var currentRow = null;
    var caractIndex = 0;
    var _openTrigger = null;

    function el(id) { return document.getElementById(id); }

    // ── Abrir / Cerrar ──────────────────────────────────────────

    function open(row, trigger) {
        currentRow = row;
        _openTrigger = (trigger instanceof Element) ? trigger : null;
        var modal = el(MODAL_ID);
        modal.classList.remove('hidden');
        modal.classList.add('flex');
        document.body.style.overflow = 'hidden';
        setTimeout(function () {
            var firstInput = el('prod-edit-codigo');
            if (firstInput) firstInput.focus();
        }, 50);
    }

    function close() {
        var trigger = _openTrigger;
        _openTrigger = null;
        var modal = el(MODAL_ID);
        modal.classList.add('hidden');
        modal.classList.remove('flex');
        document.body.style.overflow = '';
        currentRow = null;
        clearErrors();
        hideValidation();
        if (trigger) trigger.focus();
    }

    // ── Populate ────────────────────────────────────────────────

    function syncAlicuotaToIVA(alicuotaSel, ivaSelect) {
        if (!alicuotaSel || !alicuotaSel.value || !ivaSelect) return;
        var opt = alicuotaSel.options[alicuotaSel.selectedIndex];
        var pct = opt && opt.getAttribute('data-porcentaje');
        if (pct !== null && pct !== '') ivaSelect.value = String(pct);
    }

    function populate(data) {
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

    function loadSubcategorias(categoriaId, selectedId, selectedNombre) {
        var sel = el('prod-edit-subcategoriaId');
        if (!sel) return;
        sel.innerHTML = '<option value="">Sin subcategoría</option>';
        if (!categoriaId) return;
        fetch('/Producto/GetSubcategorias/' + categoriaId, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) { return r.json(); })
            .then(function (list) {
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
            })
            .catch(function () {});
    }

    function loadSubmarcas(marcaId, selectedId, selectedNombre) {
        var sel = el('prod-edit-submarcaId');
        if (!sel) return;
        sel.innerHTML = '<option value="">Sin submarca</option>';
        if (!marcaId) return;
        fetch('/Producto/GetSubmarcas/' + marcaId, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) { return r.json(); })
            .then(function (list) {
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
            })
            .catch(function () {});
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
            '<td class="px-4 py-2"><input name="Caracteristicas[' + i + '].Nombre" type="text" value="' + escHtml(nombre || '') + '" class="w-full bg-transparent border-none text-sm text-white placeholder-slate-500 focus:ring-0 outline-none" placeholder="Característica" /></td>' +
            '<td class="px-4 py-2"><input name="Caracteristicas[' + i + '].Valor" type="text" value="' + escHtml(valor || '') + '" class="w-full bg-transparent border-none text-sm text-white placeholder-slate-500 focus:ring-0 outline-none" placeholder="Valor" /></td>' +
            '<td class="px-4 py-2 text-center"><button type="button" class="prod-edit-remove-caract text-slate-500 hover:text-red-400 transition-colors"><span class="material-symbols-outlined text-base">delete</span></button></td>';
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

    // El cálculo/desglose de precios vive en el script inline de Catalogo/Index_tw
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
            texto.appendChild(document.createTextNode('. Este formulario modifica solo el precio base/fallback.'));
        } else {
            texto.textContent = 'La venta usa este precio base/fallback.';
        }
    }

    // ── Actualizar fila en tabla ────────────────────────────────

    function updateRow(entity) {
        if (!currentRow) return;
        var tds = currentRow.querySelectorAll('td');
        // td[1] = Código
        if (tds[1]) tds[1].textContent = entity.codigo;
        // td[2] = Nombre + descripcion + badge inactivo
        if (tds[2]) {
            var nameEl = tds[2].querySelector('.font-semibold');
            if (nameEl) nameEl.textContent = entity.nombre;
            var descEl = tds[2].querySelector('.text-\\[10px\\].text-slate-400');
            var descripcion = (entity.descripcion || '').trim();
            if (descripcion) {
                if (descEl) {
                    descEl.textContent = descripcion;
                } else if (nameEl) {
                    descEl = document.createElement('p');
                    descEl.className = 'text-[10px] text-slate-400 truncate max-w-[200px]';
                    descEl.textContent = descripcion;
                    nameEl.insertAdjacentElement('afterend', descEl);
                }
            } else if (descEl) {
                descEl.remove();
            }
            var inactivoBadge = tds[2].querySelector('.text-red-400');
            if (entity.activo) {
                if (inactivoBadge) inactivoBadge.remove();
                currentRow.classList.remove('opacity-50');
            } else {
                if (!inactivoBadge) {
                    var badge = document.createElement('span');
                    badge.className = 'text-[10px] font-bold text-red-400';
                    badge.textContent = 'Inactivo';
                    tds[2].querySelector('div > div')?.appendChild(badge);
                }
                currentRow.classList.add('opacity-50');
            }
        }
        // td[3] = Categoría
        if (tds[3]) tds[3].textContent = entity.categoriaNombre || '—';
        // td[4] = Marca
        if (tds[4]) tds[4].textContent = entity.marcaNombre || '—';
        // td[6] = Precio vigente + base/fallback + comisión
        if (tds[6]) {
            var priceEl = tds[6].querySelector('.font-bold');
            if (priceEl) {
                priceEl.textContent = '$ ' + Number(entity.precioActual).toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            }
            var labelEl = tds[6].querySelector('[data-prod-precio-label]');
            var baseLabelEl = tds[6].querySelector('[data-prod-precio-base-label]');
            if (entity.tienePrecioLista) {
                var baseFormatted = Number(entity.precioBase || 0).toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                var listaNombre = entity.listaPrecioActualNombre || '';
                if (labelEl) {
                    labelEl.textContent = 'Vigente por lista: ' + listaNombre;
                }
                if (baseLabelEl) {
                    baseLabelEl.textContent = 'Base/fallback: $ ' + baseFormatted;
                } else if (labelEl) {
                    var newBase = document.createElement('p');
                    newBase.setAttribute('data-prod-precio-base-label', '');
                    newBase.className = 'text-[10px] text-slate-500';
                    newBase.textContent = 'Base/fallback: $ ' + baseFormatted;
                    labelEl.insertAdjacentElement('afterend', newBase);
                }
            } else {
                if (labelEl) labelEl.textContent = 'Base/fallback';
                if (baseLabelEl) baseLabelEl.remove();
            }
            var margenEl = tds[6].querySelector('[data-prod-margen]');
            var margen = Number(entity.margenPorcentaje || 0);
            if (margen > 0) {
                var margenText = '+' + margen.toLocaleString('es-AR', { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + '% margen';
                if (margenEl) {
                    margenEl.textContent = margenText;
                } else {
                    var newMargen = document.createElement('p');
                    newMargen.setAttribute('data-prod-margen', '');
                    newMargen.className = 'text-[10px] text-emerald-400';
                    newMargen.textContent = margenText;
                    var commParent = tds[6].querySelector('[data-producto-comision]')?.closest('p');
                    if (commParent) commParent.insertAdjacentElement('beforebegin', newMargen);
                }
            } else if (margenEl) {
                margenEl.remove();
            }
            var commissionEl = tds[6].querySelector('[data-producto-comision]');
            if (commissionEl) {
                var commission = Number(entity.comisionPorcentaje || 0);
                commissionEl.textContent = commission > 0
                    ? commission.toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%'
                    : 'Sin comisión';
            }
        }
        var commissionBtn = currentRow.querySelector('[data-comision-producto-id]');
        if (commissionBtn) {
            var commissionValue = Number(entity.comisionPorcentaje || 0);
            commissionBtn.setAttribute('data-comision-porcentaje', String(commissionValue));
            commissionBtn.setAttribute('data-comision-producto-nombre', entity.nombre || '');
            if (commissionBtn.hasAttribute('title')) {
                commissionBtn.title = 'Comisión vendedor: ' + commissionValue.toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%';
            }
        }
        // data attributes en el tr
        currentRow.setAttribute('data-producto-codigo', entity.codigo);
        currentRow.setAttribute('data-producto-nombre', entity.nombre);
        currentRow.setAttribute('data-search', [
            entity.codigo || '',
            entity.nombre || '',
            entity.descripcion || '',
            entity.categoriaNombre || '',
            entity.marcaNombre || ''
        ].join(' ').toLowerCase().trim());
    }

    // ── Validación ──────────────────────────────────────────────

    function showValidation(text) {
        var box = el(VAL_BOX_ID);
        var msg = el(VAL_TXT_ID);
        if (box) { box.classList.remove('hidden'); box.classList.add('flex'); }
        if (msg) msg.textContent = text;
    }

    function hideValidation() {
        var box = el(VAL_BOX_ID);
        if (box) { box.classList.add('hidden'); box.classList.remove('flex'); }
    }

    function clearErrors() {
        document.querySelectorAll('#' + FORM_ID + ' [data-valmsg-for]').forEach(function (s) {
            s.textContent = ''; s.classList.add('hidden');
        });
        document.querySelectorAll('#' + FORM_ID + ' .border-red-500').forEach(function (i) {
            i.classList.remove('border-red-500');
        });
    }

    function handleServerErrors(errors) {
        var messages = [];
        Object.keys(errors).forEach(function (field) {
            var msgs = errors[field];
            msgs.forEach(function (m) { messages.push(m); });
            if (field) {
                var span = document.querySelector('#' + FORM_ID + ' [data-valmsg-for="' + field + '"]');
                if (span) { span.textContent = msgs[0]; span.classList.remove('hidden'); }
                var input = document.querySelector('#' + FORM_ID + ' [name="' + field + '"]');
                if (input) input.classList.add('border-red-500');
            }
        });
        if (messages.length) showValidation(messages.join('. '));
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

            var btn = el('btn-guardar-producto-edit');
            var origHTML = btn.innerHTML;
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
                    updateRow(result.entity);
                    close();
                    document.dispatchEvent(new CustomEvent('catalogo:toast', {
                        detail: { message: result.message || 'Producto actualizado', type: 'success' }
                    }));
                } else if (result.errors) {
                    handleServerErrors(result.errors);
                }
            } catch (_) {
                showValidation('Error de conexión. Intentá nuevamente.');
            } finally {
                btn.disabled = false;
                btn.innerHTML = origHTML;
            }
        });
    }

    // ── Event delegation ────────────────────────────────────────

    function initDelegatedEvents() {
        document.addEventListener('click', async function (e) {
            // Abrir modal edit
            var editBtn = e.target.closest('[data-prod-edit-id]');
            if (editBtn) {
                var id = editBtn.getAttribute('data-prod-edit-id');
                var row = editBtn.closest('tr');
                try {
                    var resp = await fetch('/Producto/GetJson/' + id, {
                        headers: { 'X-Requested-With': 'XMLHttpRequest' }
                    });
                    if (!resp.ok) throw new Error();
                    populate(await resp.json());
                    open(row, editBtn);
                } catch (_) {
                    document.dispatchEvent(new CustomEvent('catalogo:toast', {
                        detail: { message: 'Error al cargar el producto.', type: 'error' }
                    }));
                }
                return;
            }

            // Cerrar modal
            if (e.target.closest('[data-prod-edit-modal-close]')) {
                close();
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
                loadSubcategorias(this.value, null, null);
            });
        }
        var marcaSel = el('prod-edit-marcaId');
        if (marcaSel) {
            marcaSel.addEventListener('change', function () {
                loadSubmarcas(this.value, null, null);
            });
        }

        document.addEventListener('keydown', function (e) {
            var modal = el(MODAL_ID);
            if (!modal || modal.classList.contains('hidden')) return;
            if (e.key === 'Escape') { close(); return; }
            if (e.key === 'Tab' && window.CatalogoModule) window.CatalogoModule.trapFocus(modal, e);
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        initSubmit();
        initDelegatedEvents();
    });
})();
