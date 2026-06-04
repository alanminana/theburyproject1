/* ============================================================
   Plantillas de contrato · Index — render + interacciones
   Prototipo visual. En producción:
     · el listado viene del Model (IEnumerable<PlantillaContratoCreditoViewModel>)
     · ToggleActiva/{id} se postea por <form method="post"> con AntiForgeryToken
     · TempData["Success"] / TempData["Error"] se renderizan en server
   ============================================================ */
(function () {
  const DATA = JSON.parse(document.getElementById('plantillas-data').textContent);
  const HOY  = new Date(JSON.parse(document.getElementById('hoy').textContent) + 'T00:00:00');

  /* -------- catálogo de variables (compartido con el formulario) -------- */
  const VAR_GROUPS = window.PLANTILLA_VARS = [
    { title: 'Vendedor', icon: 'storefront', vars: [
      ['VENDEDOR_NOMBRE', 'Nombre del vendedor'],
      ['VENDEDOR_DOMICILIO', 'Domicilio del vendedor'],
      ['VENDEDOR_DNI', 'DNI del vendedor'],
      ['VENDEDOR_CUIT', 'CUIT del vendedor'],
    ]},
    { title: 'Comprador', icon: 'person', vars: [
      ['COMPRADOR_NOMBRE', 'Nombre completo del comprador'],
      ['COMPRADOR_DNI', 'DNI del comprador'],
      ['COMPRADOR_CUIL', 'CUIL del comprador'],
      ['COMPRADOR_DOMICILIO', 'Domicilio del comprador'],
      ['COMPRADOR_LOCALIDAD', 'Localidad del comprador'],
      ['COMPRADOR_TELEFONO', 'Teléfono del comprador'],
    ]},
    { title: 'Garante', icon: 'handshake', vars: [
      ['GARANTE_NOMBRE', 'Nombre completo del garante'],
      ['GARANTE_DNI', 'DNI del garante'],
      ['GARANTE_DOMICILIO', 'Domicilio del garante'],
      ['GARANTE_RELACION', 'Relación del garante con el comprador'],
    ]},
    { title: 'Operación', icon: 'receipt_long', vars: [
      ['NUMERO_CONTRATO', 'Número del contrato'],
      ['NUMERO_PAGARE', 'Número del pagaré'],
      ['FECHA_OPERACION', 'Fecha de la venta'],
      ['FECHA_HORA_EMISION', 'Fecha y hora de emisión del contrato'],
      ['PRODUCTOS_DETALLE', 'Lista de productos de la venta'],
    ]},
    { title: 'Montos y cuotas', icon: 'payments', vars: [
      ['PRECIO_TOTAL', 'Total de la venta'],
      ['SALDO_FINANCIADO', 'Monto financiado / total a pagar'],
      ['CANTIDAD_CUOTAS', 'Cantidad de cuotas'],
      ['VALOR_CUOTA', 'Valor de cada cuota'],
      ['PLAN_CUOTAS', 'Plan completo de cuotas con vencimientos'],
      ['INTERES_MORA', 'Interés diario por mora (%)'],
    ]},
    { title: 'Sistema', icon: 'settings', vars: [
      ['USUARIO_GENERACION', 'Usuario que generó el contrato'],
      ['SUCURSAL', 'Sucursal'],
      ['CAJA', 'Caja'],
    ]},
  ];

  /* ---------- helpers ---------- */
  const fmtDate = s => { if (!s) return null; const d = new Date(s + 'T00:00:00'); return d.toLocaleDateString('es-AR', { day: '2-digit', month: 'short', year: 'numeric' }); };
  const dateOnly = s => s ? new Date(s + 'T00:00:00') : null;

  /* Regla de negocio (idéntica a la vista Razor):
     esVigente = Activa && VigenteDesde <= hoy && (VigenteHasta == null || VigenteHasta >= hoy) */
  function estado(p) {
    if (!p.activa) return { key: 'inactiva', chip: 'chip-neutral', label: 'Inactiva', icon: 'do_not_disturb_on', bar: 'is-off' };
    const desde = dateOnly(p.vigenteDesde);
    const hasta = dateOnly(p.vigenteHasta);
    const esVigente = desde <= HOY && (!hasta || hasta >= HOY);
    if (esVigente) return { key: 'vigente', chip: 'chip-ok', label: 'Vigente', icon: 'check_circle', bar: 'is-ok' };
    if (desde > HOY) return { key: 'programada', chip: 'chip-info', label: 'Programada', icon: 'schedule', bar: 'is-info' };
    return { key: 'fuera', chip: 'chip-warn', label: 'Fuera de vigencia', icon: 'event_busy', bar: 'is-warn' };
  }

  /* ---------- mensajes del sistema (TempData) ---------- */
  function renderMessages() {
    const host = document.getElementById('sys-messages');
    const t = DATA.tempData || {};
    let html = '';
    if (t.success) html += alertHtml('ok', 'check_circle', t.success);
    if (t.error)   html += alertHtml('bad', 'error', t.error);
    host.innerHTML = html;
  }
  function alertHtml(tone, icon, msg) {
    return `<div class="alert alert-${tone}" style="margin-bottom:.7rem" data-dismiss>
      <span class="material-symbols-outlined">${icon}</span>
      <div class="flex-1">${msg}</div>
      <button class="btn btn-ghost btn-sm" style="min-height:28px;padding:.2rem .4rem" onclick="this.closest('[data-dismiss]').remove()" aria-label="Cerrar"><span class="material-symbols-outlined" style="font-size:16px">close</span></button>
    </div>`;
  }

  /* ---------- banner: plantilla en uso ---------- */
  function renderUseBanner() {
    const slot = document.getElementById('use-banner-slot');
    const current = DATA.plantillas.find(p => estado(p).key === 'vigente');
    if (current) {
      const hasta = current.vigenteHasta ? `hasta ${fmtDate(current.vigenteHasta)}` : 'sin fecha de fin (indefinida)';
      slot.innerHTML = `<div class="use-banner is-ok">
        <div class="ub-ic"><span class="material-symbols-outlined">verified</span></div>
        <div class="flex-1 min-w-0">
          <div class="ub-title">«${current.nombre}» es la plantilla en uso</div>
          <div class="ub-sub">Todo contrato de venta con crédito personal generado hoy usa este modelo.</div>
          <div class="ub-meta">Vigente desde ${fmtDate(current.vigenteDesde)} · ${hasta} · mora ${current.interesMoraDiarioPorcentaje}% diario</div>
        </div>
        <a href="CrearEditar.html?id=${current.id}" class="btn btn-soft btn-sm" style="flex-shrink:0"><span class="material-symbols-outlined">edit</span>Editar</a>
      </div>`;
    } else {
      slot.innerHTML = `<div class="use-banner is-bad">
        <div class="ub-ic"><span class="material-symbols-outlined">report</span></div>
        <div class="flex-1 min-w-0">
          <div class="ub-title">No hay ninguna plantilla activa y vigente</div>
          <div class="ub-sub">Sin una plantilla vigente no es posible generar contratos de venta con crédito personal. Activá o creá una.</div>
        </div>
        <a href="CrearEditar.html" class="btn btn-danger btn-sm" style="flex-shrink:0" data-perm="configuracion.update"><span class="material-symbols-outlined">add</span>Nueva plantilla</a>
      </div>`;
    }
  }

  /* ---------- tarjeta de plantilla ---------- */
  function tplCard(p) {
    const e = estado(p);
    const isCurrent = e.key === 'vigente';
    const hastaTxt = p.vigenteHasta ? fmtDate(p.vigenteHasta) : 'Indefinida';
    const perm = DATA.permisoUpdate;

    const toggleBtn = perm ? `
      <form method="post" action="/PlantillaContratoCredito/ToggleActiva/${p.id}" style="display:contents" onsubmit="return Plantillas.confirmToggle(event, ${p.id}, ${p.activa})">
        <input type="hidden" name="__RequestVerificationToken" value="(AntiForgeryToken)">
        <button type="submit" class="btn ${p.activa ? 'btn-ghost' : 'btn-amber'} btn-sm btn-ico" title="${p.activa ? 'Desactivar plantilla' : 'Activar plantilla'}" aria-label="${p.activa ? 'Desactivar' : 'Activar'}">
          <span class="material-symbols-outlined">${p.activa ? 'toggle_off' : 'toggle_on'}</span>
        </button>
      </form>` : '';
    const editBtn = perm ? `<a href="CrearEditar.html?id=${p.id}" class="btn btn-soft btn-sm btn-ico" title="Editar plantilla" aria-label="Editar"><span class="material-symbols-outlined">edit</span></a>` : '';
    const deleteBtn = perm ? `
      <form method="post" action="/PlantillaContratoCredito/Delete/${p.id}" style="display:contents" onsubmit="return Plantillas.confirmDelete(event, ${p.id})">
        <input type="hidden" name="__RequestVerificationToken" value="(AntiForgeryToken)">
        <button type="submit" class="btn btn-ghost btn-sm btn-ico btn-icon-danger" aria-label="Eliminar plantilla" title="Eliminar plantilla">
          <span class="material-symbols-outlined">delete</span>
        </button>
      </form>` : '';

    return `<div class="tpl-card ${isCurrent ? 'is-current' : ''} ${p.activa ? '' : 'is-off'}">
      <div class="tpl-head">
        <div class="tpl-mark"><span class="material-symbols-outlined">${isCurrent ? 'contract' : 'description'}</span></div>
        <div class="tpl-id">
          <div class="tpl-name">${p.nombre}
            <span class="chip ${e.chip}"><span class="material-symbols-outlined">${e.icon}</span>${e.label}</span>
          </div>
          <div class="tpl-sub"><span class="material-symbols-outlined" style="font-size:14px">storefront</span>${p.nombreVendedor}<span class="dotsep">·</span><span class="material-symbols-outlined" style="font-size:14px">location_on</span>${p.ciudadFirma}</div>
        </div>
        <div class="tpl-actions">${editBtn}${toggleBtn}${deleteBtn}</div>
      </div>
      <div class="tpl-facts">
        <div class="tpl-fact">
          <div class="l"><span class="material-symbols-outlined">event_available</span>Vigencia</div>
          <div class="v">${fmtDate(p.vigenteDesde)} → ${hastaTxt}</div>
        </div>
        <div class="tpl-fact">
          <div class="l"><span class="material-symbols-outlined">percent</span>Interés mora</div>
          <div class="v mono">${p.interesMoraDiarioPorcentaje}% <span class="muted-2" style="font-weight:400">diario</span></div>
        </div>
        <div class="tpl-fact">
          <div class="l"><span class="material-symbols-outlined">history</span>Última edición</div>
          <div class="v mono">${p.updatedBy}</div>
          <div class="text-xs muted-2" style="margin-top:.15rem">${fmtDate(p.updatedAt)}</div>
        </div>
      </div>
    </div>`;
  }

  /* ---------- render listado ---------- */
  function renderList() {
    const list = document.getElementById('tpl-list');
    const empty = document.getElementById('empty-state');
    const count = document.getElementById('tpl-count');
    if (!DATA.plantillas.length) {
      list.innerHTML = '';
      empty.hidden = false;
      count.textContent = '0 plantillas';
      document.getElementById('use-banner-slot').innerHTML = '';
      return;
    }
    empty.hidden = true;
    // orden: vigente primero, luego programadas, fuera de vigencia, inactivas
    const rank = { vigente: 0, programada: 1, fuera: 2, inactiva: 3 };
    const sorted = [...DATA.plantillas].sort((a, b) => rank[estado(a).key] - rank[estado(b).key]);
    list.innerHTML = sorted.map(tplCard).join('');
    count.textContent = DATA.plantillas.length + (DATA.plantillas.length === 1 ? ' plantilla' : ' plantillas');
  }

  /* ---------- variables ---------- */
  function renderVars() {
    const host = document.getElementById('var-groups');
    host.innerHTML = VAR_GROUPS.map(g => `
      <div class="var-group" data-group="${g.title.toLowerCase()}">
        <div class="vg-title"><span class="material-symbols-outlined">${g.icon}</span>${g.title}</div>
        <div class="var-list">
          ${g.vars.map(([tok, desc]) => `
            <button class="var-item" type="button" data-tok="{{${tok}}}" onclick="Plantillas.copyVar(this)">
              <span class="var-token">{{${tok}}}</span>
              <span class="var-desc">${desc}</span>
              <span class="material-symbols-outlined vi-copy">content_copy</span>
            </button>`).join('')}
        </div>
      </div>`).join('');
    // normalizar data-text (el truco .toLowerCase() de arriba no se evalúa en HTML)
    host.querySelectorAll('.var-item').forEach(b => {
      b.dataset.text = (b.dataset.tok + ' ' + b.querySelector('.var-desc').textContent).toLowerCase();
    });
  }
  function filterVars(q) {
    q = (q || '').trim().toLowerCase();
    let shown = 0;
    document.querySelectorAll('.var-group').forEach(group => {
      let gShown = 0;
      group.querySelectorAll('.var-item').forEach(it => {
        const hit = !q || it.dataset.text.includes(q);
        it.style.display = hit ? '' : 'none';
        it.classList.toggle('is-hit', !!q && hit);
        if (hit) { gShown++; shown++; }
      });
      group.style.display = gShown ? '' : 'none';
    });
    document.getElementById('var-count').textContent = shown;
  }
  function copyVar(btn) {
    const tok = btn.dataset.tok;
    navigator.clipboard?.writeText(tok).catch(() => {});
    toast(`Copiado: ${tok}`, 'content_copy');
  }

  /* ---------- toggle activa (confirmación del navegador) ---------- */
  function confirmDelete(ev, id) {
    ev.preventDefault();
    const p = DATA.plantillas.find(x => x.id === id);
    if (!p) return false;
    const enUso = estado(p).key === 'vigente';
    const msg = enUso
      ? `«${p.nombre}» es la plantilla EN USO. Si la eliminás no podrás generar contratos hasta activar otra vigente.\n\n¿Eliminar definitivamente esta plantilla?`
      : `¿Eliminar definitivamente la plantilla «${p.nombre}»?\n\nEsta acción no se puede deshacer.`;
    if (!window.confirm(msg)) return false;
    // En producción: el form postea a Delete/{id} con AntiForgeryToken.
    DATA.plantillas = DATA.plantillas.filter(x => x.id !== id);
    renderList(); renderUseBanner();
    toast('Plantilla eliminada.', 'delete');
    return false;
  }

  function confirmToggle(ev, id, activa) {
    ev.preventDefault();
    const ok = window.confirm(activa ? '¿Desactivar esta plantilla?' : '¿Activar esta plantilla?');
    if (!ok) return false;
    // En producción: el form postea a ToggleActiva/{id}. Acá simulamos el cambio.
    const p = DATA.plantillas.find(x => x.id === id);
    if (p) p.activa = !p.activa;
    renderList(); renderUseBanner();
    toast(activa ? 'Plantilla desactivada.' : 'Plantilla activada.', 'check_circle');
    return false;
  }

  /* ---------- toast ---------- */
  let tt;
  function toast(msg, icon) {
    document.getElementById('toast-msg').textContent = msg;
    if (icon) document.getElementById('toast-ic').textContent = icon;
    const t = document.getElementById('toast'); t.classList.add('open');
    clearTimeout(tt); tt = setTimeout(() => t.classList.remove('open'), 2200);
  }

  /* ---------- permisos: ocultar acciones si no tiene configuracion/update ---------- */
  function applyPerms() {
    if (!DATA.permisoUpdate) document.querySelectorAll('[data-perm="configuracion.update"]').forEach(n => n.remove());
  }

  /* ---------- init ---------- */
  applyPerms();
  renderMessages();
  renderUseBanner();
  renderList();
  renderVars();

  window.Plantillas = { confirmToggle, confirmDelete, copyVar, filterVars };
})();
