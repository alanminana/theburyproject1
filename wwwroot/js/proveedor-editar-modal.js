/**
 * proveedor-editar-modal.js  –  Modal de edición de Proveedor
 *
 * Expone:
 *   ProveedorEditarModal.open(id)
 *   ProveedorEditarModal.close()
 *   ProveedorEditarModal.submit()
 */
const ProveedorEditarModal = (() => {
    const modal   = () => document.getElementById('modal-editar-proveedor');
    const form    = () => document.getElementById('form-editar-proveedor');
    const summary = () => document.getElementById('edit-validation-summary');
    const errList = () => document.getElementById('edit-error-list');
    const loading = () => document.getElementById('edit-loading-overlay');

    function showErrors(errors) {
        const ul = errList();
        ul.innerHTML = '';
        for (const [field, msgs] of Object.entries(errors)) {
            msgs.forEach(m => {
                const li = document.createElement('li');
                li.textContent = field ? `${field}: ${m}` : m;
                ul.appendChild(li);
            });
            if (field) {
                const input = form().querySelector(`[name="${field}"]`);
                if (input) input.classList.add('border-red-500', 'ring-1', 'ring-red-500');
            }
        }
        summary().classList.remove('hidden');
        summary().scrollIntoView({ behavior: 'smooth', block: 'center' });
    }

    function clearErrors() {
        summary().classList.add('hidden');
        errList().innerHTML = '';
        form().querySelectorAll('.border-red-500').forEach(el => {
            el.classList.remove('border-red-500', 'ring-1', 'ring-red-500');
        });
    }

    function setField(name, value) {
        const el = form().querySelector(`[name="${name}"]`);
        if (!el) return;
        if (el.type === 'checkbox') {
            el.checked = !!value;
        } else {
            el.value = value ?? '';
        }
    }

    function populate(data) {
        setField('Id', data.id);
        setField('RowVersion', data.rowVersion);
        setField('Cuit', data.cuit);
        setField('RazonSocial', data.razonSocial);
        setField('NombreFantasia', data.nombreFantasia);
        setField('Email', data.email);
        setField('Telefono', data.telefono);
        setField('Contacto', data.contacto);
        setField('Direccion', data.direccion);
        setField('Ciudad', data.ciudad);
        setField('Provincia', data.provincia);
        setField('CodigoPostal', data.codigoPostal);
        setField('Aclaraciones', data.aclaraciones);
        setField('Activo', data.activo);
        // Productos: usar el picker (categorías y marcas ya no se editan en este modal)
        const picker = form().querySelector('.proveedor-product-picker');
        if (picker && picker._picker) {
            picker._picker.preload(data.productosSeleccionados || []);
        }
    }

    async function open(id) {
        clearErrors();
        form().reset();
        modal().classList.remove('hidden');
        loading().classList.remove('hidden');

        try {
            const res = await fetch(`/Proveedor/GetEditData/${id}`);
            const json = await res.json();

            if (json.success) {
                populate(json.data);
            } else {
                showErrors(json.errors || { '': ['No se pudo cargar el proveedor.'] });
            }
        } catch {
            showErrors({ '': ['Error de conexión al cargar datos.'] });
        } finally {
            loading().classList.add('hidden');
        }
    }

    function close() {
        modal().classList.add('hidden');
    }

    async function submit() {
        clearErrors();

        const formData = new FormData(form());
        const params = new URLSearchParams();
        for (const [key, value] of formData.entries()) {
            params.append(key, value);
        }
        if (!formData.has('Activo')) {
            params.append('Activo', 'false');
        }

        try {
            const res = await fetch('/Proveedor/EditAjax', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: params.toString()
            });
            const data = await res.json();

            if (data.success) {
                if (data.entity) {
                    data.entity.contacto = form().querySelector('[name="Contacto"]')?.value || '';
                }
                close();
                onProveedorEditado(data.entity);
            } else if (data.errors) {
                showErrors(data.errors);
            }
        } catch {
            showErrors({ '': ['Error de conexión. Intente nuevamente.'] });
        }
    }

    function escHtml(str) {
        var d = document.createElement('div');
        d.textContent = str || '';
        return d.innerHTML;
    }

    function escAttr(str) {
        return escHtml(str).replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function formatCuit(cuit) {
        if (cuit && cuit.length === 11) {
            return cuit.slice(0, 2) + '-' + cuit.slice(2, 10) + '-' + cuit.slice(10);
        }
        return cuit || '';
    }

    function renderProveedorCell(entity) {
        var nombreFantasiaHtml = entity.nombreFantasia
            ? '<span class="h-1 w-1 rounded-full bg-slate-700"></span><span class="truncate max-w-[14rem]">' + escHtml(entity.nombreFantasia) + '</span>'
            : '';

        return '<div class="min-w-0">' +
            '<p class="truncate text-sm font-bold text-white">' + escHtml(entity.razonSocial) + '</p>' +
            '<p class="mt-0.5 flex flex-wrap items-center gap-1.5 text-[11px] text-slate-500">' +
            '<span class="font-mono">' + escHtml(formatCuit(entity.cuit)) + '</span>' +
            nombreFantasiaHtml +
            '</p>' +
            '</div>';
    }

    function renderContactoCell(entity) {
        var contactoHtml = '';
        if (entity.contacto) {
            contactoHtml += '<span class="truncate font-semibold text-slate-200">' + escHtml(entity.contacto) + '</span>';
        }
        if (entity.email) {
            contactoHtml += '<a href="mailto:' + escAttr(entity.email) + '" class="flex items-center gap-1 truncate text-xs text-primary-on-dark hover:underline no-underline"><span class="material-symbols-outlined text-[14px]">mail</span><span class="truncate">' + escHtml(entity.email) + '</span></a>';
        }
        if (entity.telefono) {
            contactoHtml += '<span class="flex items-center gap-1 text-xs text-slate-400"><span class="material-symbols-outlined text-[14px]">phone</span>' + escHtml(entity.telefono) + '</span>';
        }
        if (!contactoHtml) {
            contactoHtml = '<span class="text-xs text-slate-500">Sin contacto</span>';
        }

        return '<div class="flex max-w-[17rem] flex-col gap-0.5 text-sm">' + contactoHtml + '</div>';
    }

    function renderEstadoBadge(activo) {
        return activo
            ? '<span class="inline-flex items-center gap-1.5 rounded-full bg-emerald-500/10 px-2.5 py-1 text-xs font-bold text-emerald-400"><span class="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>Activo</span>'
            : '<span class="inline-flex items-center gap-1.5 rounded-full bg-slate-800 px-2.5 py-1 text-xs font-bold text-slate-400"><span class="w-1.5 h-1.5 rounded-full bg-slate-400"></span>Inactivo</span>';
    }

    function onProveedorEditado(entity) {
        if (!entity) return;

        var row = document.querySelector('[data-proveedor-row-id="' + entity.id + '"]');
        if (!row) return;

        var cells = row.querySelectorAll('td');
        if (cells.length < 4) return;

        cells[0].innerHTML = renderProveedorCell(entity);
        cells[2].innerHTML = renderContactoCell(entity);
        cells[3].innerHTML = renderEstadoBadge(entity.activo);

        var name = entity.razonSocial || '';
        var detailAction = row.querySelector('[title="Ver detalle"]');
        var editAction = row.querySelector('[title="Editar"]');
        var deleteAction = row.querySelector('[title="Eliminar"]');
        var deleteForm = row.querySelector('[data-proveedor-delete-form]');

        if (detailAction) detailAction.setAttribute('aria-label', 'Ver detalle de ' + name);
        if (editAction) editAction.setAttribute('aria-label', 'Editar ' + name);
        if (deleteAction) deleteAction.setAttribute('aria-label', 'Eliminar ' + name);
        if (deleteForm) deleteForm.setAttribute('data-proveedor-name', name);

        document.dispatchEvent(new CustomEvent('proveedor:toast', {
            detail: { message: 'Proveedor actualizado: ' + (entity.razonSocial || ''), type: 'success' }
        }));
    }

    /* ESC to close */
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && !modal().classList.contains('hidden')) close();
    });

    return { open, close, submit };
})();
