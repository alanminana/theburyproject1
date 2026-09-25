/**
 * proveedor-crear-modal.js  –  Drawer de alta de Proveedor
 *
 * Expone:
 *   ProveedorCrearModal.open()
 *   ProveedorCrearModal.close()   (pide confirmación si hay cambios sin guardar)
 *   ProveedorCrearModal.submit()
 */
const ProveedorCrearModal = (() => {
    const base = window.TheBury.createProveedorModal({
        modalId: 'modal-crear-proveedor',
        formId: 'form-crear-proveedor',
        summaryId: 'proveedor-validation-summary',
        listId: 'proveedor-error-list',
        url: '/Proveedor/CreateAjax'
    });

    function resetForm() {
        const form = base.form();
        form.reset();
        base.clearErrors();
        // Un proveedor nuevo arranca activo.
        const activo = form.querySelector('input[name="Activo"]');
        if (activo) activo.checked = true;
        const picker = form.querySelector('.proveedor-product-picker');
        if (picker && picker._picker) picker._picker.reset();
    }

    function open() {
        if (!base.modal()) return;
        resetForm();
        base.setBusy(false);
        base.show();
        base.takeSnapshot();
    }

    return { open, close: base.requestClose, submit: base.submit };
})();
