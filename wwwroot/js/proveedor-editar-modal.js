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

    function setMultiSelect(name, values) {
        const sel = form().querySelector(`select[name="${name}"]`);
        if (!sel) return;
        Array.from(sel.options).forEach(opt => {
            opt.selected = values.includes(parseInt(opt.value));
        });
    }

    function setCheckboxes(name, values) {
        form().querySelectorAll(`input[type="checkbox"][name="${name}"]`).forEach(cb => {
            cb.checked = values.includes(parseInt(cb.value));
        });
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
        setMultiSelect('CategoriasSeleccionadas', data.categoriasSeleccionadas || []);
        setMultiSelect('MarcasSeleccionadas', data.marcasSeleccionadas || []);
        setCheckboxes('ProductosSeleccionados', data.productosSeleccionados || []);
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
                close();
                location.reload();
            } else if (data.errors) {
                showErrors(data.errors);
            }
        } catch {
            showErrors({ '': ['Error de conexión. Intente nuevamente.'] });
        }
    }

    /* ESC to close */
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && !modal().classList.contains('hidden')) close();
    });

    return { open, close, submit };
})();
