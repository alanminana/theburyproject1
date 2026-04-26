/**
 * proveedor-crear-modal.js  –  Modal de creación de Proveedor
 *
 * Expone:
 *   ProveedorCrearModal.open()
 *   ProveedorCrearModal.close()
 *   ProveedorCrearModal.submit()
 */
const ProveedorCrearModal = (() => {
    const modal = () => document.getElementById('modal-crear-proveedor');
    const form  = () => document.getElementById('form-crear-proveedor');
    const summary = () => document.getElementById('proveedor-validation-summary');
    const errorList = () => document.getElementById('proveedor-error-list');

    function showErrors(errors) {
        const ul = errorList();
        ul.innerHTML = '';
        for (const [field, msgs] of Object.entries(errors)) {
            msgs.forEach(m => {
                const li = document.createElement('li');
                li.textContent = field ? `${field}: ${m}` : m;
                ul.appendChild(li);
            });
            // Highlight field
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
        errorList().innerHTML = '';
        form().querySelectorAll('.border-red-500').forEach(el => {
            el.classList.remove('border-red-500', 'ring-1', 'ring-red-500');
        });
    }

    function resetForm() {
        form().reset();
        clearErrors();
        // Re-check the Activo toggle (default true)
        const activo = form().querySelector('input[name="Activo"]');
        if (activo) activo.checked = true;
        // Reset product picker
        const picker = form().querySelector('.proveedor-product-picker');
        if (picker && picker._picker) picker._picker.reset();
    }

    function open() {
        resetForm();
        modal().classList.remove('hidden');
    }

    function close() {
        modal().classList.add('hidden');
    }

    async function submit() {
        clearErrors();

        const formData = new FormData(form());
        // Encode form data as URL params for MVC model binding
        const params = new URLSearchParams();
        for (const [key, value] of formData.entries()) {
            params.append(key, value);
        }
        // If Activo checkbox unchecked, it won't be in FormData — send false
        if (!formData.has('Activo')) {
            params.append('Activo', 'false');
        }

        try {
            const res = await fetch('/Proveedor/CreateAjax', {
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
                onProveedorCreado(data.entity);
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

    function getAntiForgeryToken() {
        var token = document.querySelector('input[name="__RequestVerificationToken"]');
        return token ? token.value : '';
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

    function renderActionsCell(entity) {
        var name = escAttr(entity.razonSocial || '');
        var token = escAttr(getAntiForgeryToken());

        return '<div class="flex items-center justify-end gap-0.5">' +
            '<a href="/Proveedor/Details/' + entity.id + '" class="row-action row-action--primary no-underline" title="Ver detalle" aria-label="Ver detalle de ' + name + '">' +
            '<span class="material-symbols-outlined">visibility</span><span class="row-action__label">Ver</span></a>' +
            '<button type="button" data-proveedor-modal-action="open" data-proveedor-modal="edit" data-proveedor-id="' + entity.id + '" class="row-action" title="Editar" aria-label="Editar ' + name + '">' +
            '<span class="material-symbols-outlined">edit</span><span class="row-action__label">Editar</span></button>' +
            '<form action="/Proveedor/Delete/' + entity.id + '" method="post" class="inline" data-proveedor-delete-form data-proveedor-name="' + name + '">' +
            '<input name="__RequestVerificationToken" type="hidden" value="' + token + '" />' +
            '<button type="submit" class="row-action row-action--danger" title="Eliminar" aria-label="Eliminar ' + name + '">' +
            '<span class="material-symbols-outlined">delete</span><span class="row-action__label">Eliminar</span></button>' +
            '</form>' +
            '</div>';
    }

    function onProveedorCreado(entity) {
        if (!entity) return;

        var tbody = document.getElementById('proveedores-tbody');
        if (tbody) {
            var emptyRow = tbody.querySelector('tr td[colspan]');
            if (emptyRow) emptyRow.closest('tr').remove();

            var tr = document.createElement('tr');
            tr.setAttribute('data-proveedor-row-id', entity.id);
            tr.className = 'transition-colors hover:bg-white/5';
            tr.innerHTML =
                '<td class="px-5 py-3">' + renderProveedorCell(entity) + '</td>' +
                '<td class="px-5 py-3"><span class="text-xs text-slate-500">Sin productos</span></td>' +
                '<td class="px-5 py-3">' + renderContactoCell(entity) + '</td>' +
                '<td class="px-5 py-3">' + renderEstadoBadge(entity.activo) + '</td>' +
                '<td class="px-5 py-3 text-right">' + renderActionsCell(entity) + '</td>';
            tbody.appendChild(tr);
        }

        document.dispatchEvent(new CustomEvent('proveedor:toast', {
            detail: { message: 'Proveedor creado: ' + (entity.razonSocial || ''), type: 'success' }
        }));
    }

    /* ESC to close */
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && !modal().classList.contains('hidden')) close();
    });

    return { open, close, submit };
})();
