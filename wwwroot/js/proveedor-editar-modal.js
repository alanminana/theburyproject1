/**
 * proveedor-editar-modal.js  –  Drawer de edición de Proveedor
 *
 * Expone:
 *   ProveedorEditarModal.open(id)
 *   ProveedorEditarModal.close()   (pide confirmación si hay cambios sin guardar)
 *   ProveedorEditarModal.submit()
 */
const ProveedorEditarModal = (() => {
    const base = window.TheBury.createProveedorModal({
        modalId: 'modal-editar-proveedor',
        formId: 'form-editar-proveedor',
        summaryId: 'edit-validation-summary',
        listId: 'edit-error-list',
        url: '/Proveedor/EditAjax'
    });

    const loading = () => document.getElementById('edit-loading-overlay');

    function setField(name, value) {
        const el = base.form().querySelector(`[name="${name}"]`);
        if (!el) return;
        if (el.type === 'checkbox') {
            el.checked = !!value;
        } else {
            el.value = value ?? '';
        }
    }

    function checkGroup(name, ids) {
        const selected = new Set((ids || []).map(String));
        base.form().querySelectorAll(`input[type="checkbox"][name="${name}"]`).forEach(box => {
            box.checked = selected.has(box.value);
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
        // EditAjax reemplaza todas las asociaciones con lo que el formulario envía: por eso el drawer
        // carga y devuelve productos, categorías y marcas; si faltara alguno, se borrarían al guardar.
        checkGroup('CategoriasSeleccionadas', data.categoriasSeleccionadas);
        checkGroup('MarcasSeleccionadas', data.marcasSeleccionadas);
        const picker = base.form().querySelector('.proveedor-product-picker');
        if (!picker || !picker._picker) {
            // Sin el selector inicializado el formulario no enviaría los productos y se borrarían al guardar.
            throw new Error('No se pudo preparar el selector de productos. Recargá la página e intentá de nuevo.');
        }
        picker._picker.preload(data.productosSeleccionados || []);
    }

    async function open(id) {
        if (!base.modal()) return;

        base.clearErrors();
        base.form().reset();
        const picker = base.form().querySelector('.proveedor-product-picker');
        if (picker && picker._picker) picker._picker.reset();

        base.show();
        loading().hidden = false;
        // Hasta tener los datos cargados no se puede guardar: un formulario vacío pisaría el proveedor.
        base.setLocked(true);
        let loaded = false;

        try {
            const res = await fetch(`/Proveedor/GetEditData/${id}`, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            const json = await base.readJson(res);

            if (json.success) {
                populate(json.data);
                loaded = true;
            } else {
                base.showErrors(json.errors || { '': ['No se pudo cargar el proveedor.'] });
            }
        } catch (error) {
            base.showErrors({ '': [base.describeFailure(error)] });
        } finally {
            loading().hidden = true;
            base.takeSnapshot();
            if (loaded) base.setLocked(false);
        }
    }

    return { open, close: base.requestClose, submit: base.submit };
})();
