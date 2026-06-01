/* ============================================================
   Crédito · Index — render + interacciones
   (prototipo visual; en producción los datos vienen del Model
    y las acciones postean a los controllers reales)
   ============================================================ */
(function () {
  const DATA = JSON.parse(document.getElementById('cartera-data').textContent);

  /* ---------- helpers ---------- */
  const money = n => '$ ' + Math.round(n).toLocaleString('es-AR');
  const el = (html) => { const t = document.createElement('template'); t.innerHTML = html.trim(); return t.content.firstElementChild; };
  const parseDate = s => { const [d, m, y] = s.split('/').map(Number); return new Date(y, m - 1, d); };

  const ESTADO = {
    activo:    { chip: 'chip-ok',      label: 'Activo',    bar: 'is-accent' },
    mora:      { chip: 'chip-bad',     label: 'En mora',   bar: 'is-bad' },
    pendiente: { chip: 'chip-warn',    label: 'Pendiente', bar: 'is-warn' },
    cancelado: { chip: 'chip-neutral', label: 'Cancelado', bar: '' }
  };

  /* ---------- resúmenes por cliente ---------- */
  function resumen(cli) {
    let saldo = 0, montoMora = 0, vencidas = 0, nextVenc = null;
    cli.creditos.forEach(cr => {
      saldo += cr.saldo;
      cr.cuotas.forEach(q => {
        montoMora += q.mora || 0;
        if (q.estado === 'vencida') vencidas++;
        if (q.estado === 'pendiente') {
          const d = parseDate(q.venc);
          if (!nextVenc || d < nextVenc) nextVenc = d;
        }
      });
    });
    const estados = [...new Set(cli.creditos.map(c => c.estado))];
    if (vencidas > 0) estados.push('mora');
    return { saldo, montoMora, vencidas, nextVenc, hasMora: vencidas > 0, estados };
  }

  /* ---------- tabla de cuotas ---------- */
  function cuotasTable(cr) {
    if (!cr.cuotas.length) {
      return `<div class="px-3 py-4 text-center text-xs muted-2">Crédito pendiente de aprobación — el plan de cuotas se genera al aprobar.</div>`;
    }
    const rows = cr.cuotas.map(q => {
      const venc = q.estado === 'vencida';
      const diasChip = q.dias > 0 ? `<span class="chip ${q.dias >= 10 ? 'chip-bad' : 'chip-warn'}" style="margin-left:.4rem">+${q.dias} d</span>` : '';
      const estChip = venc ? '<span class="chip chip-bad">Vencida</span>' : '<span class="chip chip-neutral">Pendiente</span>';
      return `<tr class="${venc ? 'is-venc' : ''}">
        <td class="c"><input type="checkbox" class="chk cuota-chk" data-monto="${q.monto}" data-mora="${q.mora || 0}" onchange="CreditoIndex.onCuota(this)"></td>
        <td><span class="cuota-n ${venc ? 'venc' : ''}">${q.n}</span></td>
        <td>${q.venc}${diasChip}</td>
        <td class="r num">${money(q.monto)}</td>
        <td class="r num" ${q.mora ? 'style="color:#fbbf24"' : ''}>${q.mora ? money(q.mora) : '<span class="dash">—</span>'}</td>
        <td class="c">${estChip}</td>
        <td class="r"><a href="PagarCuota.html" class="btn btn-soft btn-sm">Pagar</a></td>
      </tr>`;
    }).join('');
    return `<table class="cuota-tbl">
      <thead><tr><th class="c" style="width:36px"></th><th>Cuota</th><th>Vencimiento</th><th class="r">Monto</th><th class="r">Mora</th><th class="c">Estado</th><th class="r">Acción</th></tr></thead>
      <tbody>${rows}</tbody></table>`;
  }

  /* ---------- ítem de crédito ---------- */
  function creditItem(cr) {
    const e = ESTADO[cr.estado] || ESTADO.pendiente;
    const pct = Math.round((cr.cuotasPagas / cr.cuotasTotal) * 100);
    return `<div class="credit-item" data-credito-credit>
      <button class="credit-head" type="button" data-credito-credit-toggle aria-expanded="false" onclick="CreditoIndex.toggleCredit(this)">
        <span class="material-symbols-outlined chev" data-credito-credit-toggle-icon>chevron_right</span>
        <span class="credit-no">${cr.numero}</span>
        <span class="chip ${e.chip}">${e.label}</span>
        <span style="flex:1"></span>
        <div class="credit-prog hide-mobile">
          <div class="bar"><div class="bar-fill ${e.bar}" style="width:${pct}%"></div></div>
          <span class="pl">${cr.cuotasPagas}/${cr.cuotasTotal}</span>
        </div>
        <div style="text-align:right;min-width:96px">
          <div class="num" style="font-weight:600;color:#fff">${money(cr.saldo)}</div>
          <div class="text-xs muted-2">saldo</div>
        </div>
      </button>
      <div class="credit-cuotas" data-credito-cuotas-container>
        ${cuotasTable(cr)}
        <div class="px-1 pt-2"><a href="Details.html" class="btn btn-ghost btn-sm">Ver detalle del crédito<span class="material-symbols-outlined">chevron_right</span></a></div>
      </div>
    </div>`;
  }

  /* ---------- footer de pago múltiple ---------- */
  function payFooter() {
    return `<div class="pay-foot is-empty" data-pay-foot>
      <div class="pay-grid">
        <div class="pay-resumen">
          <div class="pr"><div class="l">Cuotas</div><div class="v" data-sel-count>0</div></div>
          <div class="pr"><div class="l">Subtotal</div><div class="v" data-credito-resumen-subtotal>$ 0</div></div>
          <div class="pr"><div class="l">Mora</div><div class="v mora" data-credito-resumen-mora>$ 0</div></div>
          <div class="pr"><div class="l">Total a pagar</div><div class="v total" data-credito-resumen-total>$ 0</div></div>
        </div>
        <div class="pay-actions">
          <div style="min-width:160px">
            <label class="label" style="margin-bottom:.3rem">Medio de pago</label>
            <select class="field" data-credito-medio-pago>
              <option>Efectivo</option><option>Transferencia</option><option>Tarjeta de débito</option><option>Mercado Pago</option>
            </select>
          </div>
          <button class="btn btn-success" data-credito-registrar-pago-multiple data-credito-pago-url="/Credito/RegistrarPagoMultiple" disabled onclick="CreditoIndex.registrar(this)"><span class="material-symbols-outlined">payments</span>Registrar pago</button>
        </div>
      </div>
      <p class="hint" data-pay-hint>Seleccioná una o más cuotas de arriba para registrar el pago.</p>
    </div>`;
  }

  /* ---------- tarjeta de cliente ---------- */
  function clienteCard(cli) {
    const r = resumen(cli);
    const avatarTone = cli.tono === 'slate' ? 'avatar--slate' : '';
    const nextTxt = r.hasMora ? 'Cuota vencida' : (r.nextVenc ? r.nextVenc.toLocaleDateString('es-AR') : '—');
    const statusChip = r.hasMora
      ? `<span class="chip chip-bad"><span class="material-symbols-outlined">error</span>${r.vencidas} venc.</span>`
      : (cli.creditos.some(c => c.estado === 'pendiente')
        ? `<span class="chip chip-warn">Pendiente</span>`
        : `<span class="chip chip-ok">Al día</span>`);
    const moraSum = r.hasMora ? `<div class="cli-sum"><div class="v bad">${money(r.montoMora)}</div><div class="l">En mora</div></div>` : '';

    const searchTxt = (cli.nombre + ' ' + cli.doc + ' ' + cli.creditos.map(c => c.numero).join(' ')).toLowerCase();

    const card = el(`<div class="cli-card ${r.hasMora ? 'has-mora' : ''}" data-credito-cliente-id="${cli.id}" data-search="${searchTxt}" data-estados="${r.estados.join(',')}" data-mora="${r.hasMora ? 1 : 0}">
      <button class="cli-head" type="button" onclick="CreditoIndex.toggleClient(this)">
        <div class="avatar ${avatarTone}">${cli.iniciales}</div>
        <div class="cli-id">
          <div class="cli-name">${cli.nombre}</div>
          <div class="cli-meta"><span class="mono">${cli.doc}</span><span class="sep" style="color:#334155">·</span><span>${cli.creditos.length} crédito${cli.creditos.length > 1 ? 's' : ''}</span><span class="sep" style="color:#334155">·</span><span>Próx. ${nextTxt}</span></div>
        </div>
        <div class="cli-summary">
          <div class="cli-sum"><div class="v">${money(r.saldo)}</div><div class="l">Saldo</div></div>
          ${moraSum}
          ${statusChip}
        </div>
        <span class="material-symbols-outlined chev">expand_more</span>
      </button>
      <div class="cli-summary-m">
        <span class="chip chip-neutral">${money(r.saldo)} saldo</span>
        ${r.hasMora ? `<span class="chip chip-bad">${money(r.montoMora)} mora</span>` : ''}
        ${statusChip}
      </div>
      <div class="cli-body">
        <div class="cli-body-pad">
          <div class="flex items-center justify-between mb-2.5">
            <span class="text-xs muted-2 uppercase" style="letter-spacing:.05em;font-weight:600">Créditos del cliente</span>
            <button class="btn btn-ghost btn-sm" data-credito-cliente-panel-open data-credito-cliente-id="${cli.id}" onclick="CreditoIndex.openPanel(${cli.id})"><span class="material-symbols-outlined">open_in_full</span>Abrir en panel</button>
          </div>
          <div>${cli.creditos.map(creditItem).join('')}</div>
          ${payFooter()}
        </div>
      </div>
    </div>`);
    return card;
  }

  /* ---------- render listas ---------- */
  function renderClientes() {
    const list = document.getElementById('clientes-list');
    list.innerHTML = '';
    DATA.clientes.forEach(c => list.appendChild(clienteCard(c)));
  }

  function renderMoras() {
    // agrupar las cuotas vencidas por cliente
    const groups = [];
    DATA.clientes.forEach(cli => {
      const items = [];
      cli.creditos.forEach(cr => cr.cuotas.forEach(q => { if (q.estado === 'vencida') items.push({ cr, q }); }));
      if (items.length) {
        items.sort((a, b) => b.q.dias - a.q.dias);
        const worst = items[0].q.dias;
        const totalMora = items.reduce((s, x) => s + (x.q.mora || 0), 0);
        const totalMonto = items.reduce((s, x) => s + x.q.monto, 0);
        groups.push({ cli, items, worst, totalMora, totalMonto });
      }
    });
    groups.sort((a, b) => b.worst - a.worst);

    const list = document.getElementById('moras-list');
    list.innerHTML = groups.map(g => {
      const crit = g.worst >= 10;
      const totalCli = g.totalMonto + g.totalMora;
      const rows = g.items.map(({ cr, q }) => `
        <div class="mora-cuota">
          <span class="cuota-n venc">${q.n}</span>
          <div class="flex-1 min-w-0">
            <span class="mono text-xs" style="color:#cbd5e1">${cr.numero}</span>
            <span class="text-xs muted-2"> · cuota ${q.n}/${cr.cuotasTotal} · venció ${q.venc}</span>
          </div>
          <span class="chip ${q.dias >= 10 ? 'chip-bad' : 'chip-warn'}">+${q.dias} d</span>
          <span class="num" style="min-width:92px;text-align:right;font-weight:600;color:#fff">${money(q.monto + (q.mora || 0))}</span>
        </div>`).join('');
      return `<div class="cli-card ${crit ? 'has-mora' : ''}" style="margin-bottom:.75rem${crit ? ';border-color:rgba(244,63,94,.4)' : ''}">
        <button class="cli-head" type="button" onclick="CreditoIndex.toggleClient(this)">
          <div class="dias-badge ${crit ? 'bad' : 'warn'}">${g.worst}<div class="u">días</div></div>
          <div class="avatar ${g.cli.tono === 'slate' ? 'avatar--slate' : ''}">${g.cli.iniciales}</div>
          <div class="cli-id">
            <div class="cli-name">${g.cli.nombre}</div>
            <div class="cli-meta"><span class="mono">${g.cli.doc}</span><span style="color:#334155">·</span><span>${g.items.length} cuota${g.items.length > 1 ? 's' : ''} vencida${g.items.length > 1 ? 's' : ''}</span></div>
          </div>
          <div class="cli-summary"><div class="cli-sum"><div class="v bad">${money(totalCli)}</div><div class="l">Total en mora</div></div></div>
          <span class="material-symbols-outlined chev">expand_more</span>
        </button>
        <div class="cli-summary-m"><span class="chip chip-bad">${money(totalCli)} en mora</span><span class="chip chip-neutral">${g.items.length} cuota${g.items.length > 1 ? 's' : ''}</span></div>
        <div class="cli-body">
          <div class="cli-body-pad">
            ${rows}
            <div style="margin-top:.9rem;display:flex;justify-content:flex-end">
              <a href="PagarCuota.html" class="btn btn-danger btn-sm"><span class="material-symbols-outlined">payments</span>Registrar pago de cuotas</a>
            </div>
          </div>
        </div>
      </div>`;
    }).join('') + `<div style="margin-top:1rem;text-align:center"><a href="CuotasVencidas.html" class="btn btn-soft btn-sm">Ver listado operativo completo<span class="material-symbols-outlined">chevron_right</span></a></div>`;
  }

  /* ---------- interacciones ---------- */
  function toggleClient(btn) { btn.closest('.cli-card').classList.toggle('is-open'); }
  function toggleCredit(btn) {
    const item = btn.closest('.credit-item');
    const open = item.classList.toggle('is-open');
    btn.setAttribute('aria-expanded', open);
    btn.querySelector('[data-credito-credit-toggle-icon]').textContent = open ? 'expand_more' : 'chevron_right';
  }

  function onCuota(chk) {
    const card = chk.closest('.cli-card');
    const checks = [...card.querySelectorAll('.cuota-chk:checked')];
    let sub = 0, mora = 0;
    checks.forEach(c => { sub += +c.dataset.monto; mora += +c.dataset.mora; });
    const foot = card.querySelector('[data-pay-foot]');
    foot.querySelector('[data-sel-count]').textContent = checks.length;
    foot.querySelector('[data-credito-resumen-subtotal]').textContent = money(sub);
    foot.querySelector('[data-credito-resumen-mora]').textContent = money(mora);
    foot.querySelector('[data-credito-resumen-total]').textContent = money(sub + mora);
    const btn = foot.querySelector('[data-credito-registrar-pago-multiple]');
    btn.disabled = checks.length === 0;
    foot.classList.toggle('is-empty', checks.length === 0);
    chk.closest('tr').classList.toggle('is-sel', chk.checked);
    foot.querySelector('[data-pay-hint]').textContent = checks.length === 0
      ? 'Seleccioná una o más cuotas de arriba para registrar el pago.'
      : `${checks.length} cuota${checks.length > 1 ? 's' : ''} seleccionada${checks.length > 1 ? 's' : ''} · el pago se aplica al crédito correspondiente.`;
  }

  function registrar(btn) {
    const card = btn.closest('.cli-card');
    const total = card.querySelector('[data-credito-resumen-total]').textContent;
    const medio = card.querySelector('[data-credito-medio-pago]').value;
    toast(`Pago de ${total} registrado (${medio}).`);
    card.querySelectorAll('.cuota-chk:checked').forEach(c => { c.checked = false; onCuota(c); });
  }

  /* ---------- panel lateral (cartera completa) ---------- */
  function openPanel(id) {
    const cli = DATA.clientes.find(c => c.id === id);
    const r = resumen(cli);
    document.getElementById('panel-avatar').textContent = cli.iniciales;
    document.getElementById('panel-avatar').className = 'avatar ' + (cli.tono === 'slate' ? 'avatar--slate' : '');
    document.getElementById('credito-cliente-panel-title').textContent = cli.nombre;
    document.getElementById('panel-sub').textContent = cli.doc + ' · ' + cli.creditos.length + ' crédito(s)';
    const content = document.getElementById('credito-cliente-panel-content');
    content.innerHTML = `
      <div class="grid grid-cols-3 gap-2.5">
        <div class="minimetric"><div class="mm-label">Saldo total</div><div class="mm-value num">${money(r.saldo)}</div></div>
        <div class="minimetric"><div class="mm-label">En mora</div><div class="mm-value num" style="${r.hasMora ? 'color:#fb7185' : ''}">${money(r.montoMora)}</div></div>
        <div class="minimetric"><div class="mm-label">Cuotas venc.</div><div class="mm-value num" style="${r.vencidas ? 'color:#fb7185' : ''}">${r.vencidas}</div></div>
      </div>
      <h3 class="text-sm font-semibold text-white" style="margin:1.25rem 0 .65rem">Créditos</h3>
      <div class="space-y-2">${cli.creditos.map(cr => {
        const e = ESTADO[cr.estado] || ESTADO.pendiente;
        const pct = Math.round((cr.cuotasPagas / cr.cuotasTotal) * 100);
        return `<div class="card-2" style="padding:.85rem 1rem;border-radius:11px">
          <div class="flex items-center justify-between gap-2 mb-2">
            <span class="credit-no">${cr.numero}</span><span class="chip ${e.chip}">${e.label}</span>
          </div>
          <div class="flex items-center justify-between text-sm mb-1.5"><span class="muted">Saldo</span><span class="num font-semibold text-white">${money(cr.saldo)}</span></div>
          <div class="bar"><div class="bar-fill ${e.bar}" style="width:${pct}%"></div></div>
          <div class="flex items-center justify-between text-xs muted-2 mt-1.5"><span>${cr.cuotasPagas}/${cr.cuotasTotal} cuotas</span><a href="Details.html" class="hover:text-white">Ver detalle ›</a></div>
        </div>`;
      }).join('')}</div>
      <div style="margin-top:1.25rem;display:flex;gap:.6rem">
        <a href="/projects/_/Clientes/Details.html" class="btn btn-soft btn-sm btn-block" onclick="return false"><span class="material-symbols-outlined">person</span>Legajo del cliente</a>
        <a href="Create.html" class="btn btn-primary btn-sm btn-block"><span class="material-symbols-outlined">add_card</span>Nueva línea</a>
      </div>`;
    document.getElementById('credito-cliente-panel').classList.add('open');
  }
  function closePanel() { document.getElementById('credito-cliente-panel').classList.remove('open'); }

  /* ---------- tabs ---------- */
  function goTab(name) {
    document.querySelectorAll('#credito-tabs .tab').forEach(t => t.setAttribute('aria-selected', t.dataset.creditoTab === name));
    document.getElementById('tab-creditos').classList.toggle('is-active', name === 'creditos');
    document.getElementById('tab-moras').classList.toggle('is-active', name === 'moras');
    if (name === 'moras') document.querySelector('#credito-tabs').scrollIntoView ? null : null;
  }

  /* ---------- filtros ---------- */
  function applyFilters() {
    const q = document.getElementById('f-search').value.trim().toLowerCase();
    const est = document.getElementById('f-estado').value;
    const venc = document.getElementById('f-vencidas').checked;
    let shown = 0;
    document.querySelectorAll('#clientes-list .cli-card').forEach(card => {
      const okQ = !q || card.dataset.search.includes(q);
      const okEst = !est || card.dataset.estados.split(',').includes(est);
      const okVenc = !venc || card.dataset.mora === '1';
      const show = okQ && okEst && okVenc;
      card.hidden = !show; if (show) shown++;
    });
    document.getElementById('empty-state').hidden = shown !== 0;
    document.getElementById('cli-count').textContent = shown + (shown === 1 ? ' cliente' : ' clientes');
    document.getElementById('btn-clear').hidden = !(q || est || venc);
  }
  function clearFilters() {
    document.getElementById('f-search').value = '';
    document.getElementById('f-estado').value = '';
    document.getElementById('f-vencidas').checked = false;
    applyFilters();
  }

  /* ---------- toast ---------- */
  let tt;
  function toast(msg) {
    document.getElementById('toast-msg').textContent = msg;
    const t = document.getElementById('toast'); t.classList.add('open');
    clearTimeout(tt); tt = setTimeout(() => t.classList.remove('open'), 2400);
  }

  document.addEventListener('keydown', e => { if (e.key === 'Escape') closePanel(); });

  /* ---------- init ---------- */
  renderClientes();
  renderMoras();

  window.CreditoIndex = { toggleClient, toggleCredit, onCuota, registrar, openPanel };
  window.goTab = goTab;
  window.applyFilters = applyFilters;
  window.clearFilters = clearFilters;
  window.closePanel = closePanel;
})();
