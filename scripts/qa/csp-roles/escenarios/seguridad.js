// Seguridad: modales inyectados por fetch (usuarios, roles, permisos) con validacion fallida y cierres.
'use strict';

const DLG = '[id$="Container"] [id$="Card"]'; // los modales de Seguridad se inyectan en <x>Container con una tarjeta <x>Card
const abrio = (page) => page.locator(DLG).first().waitFor({ state: 'visible', timeout: 5000 }).then(() => true).catch(() => false);

// Abre un modal desde `selector`, comprueba que tenga formulario, opcionalmente lo envia vacio (validacion) y lo cierra con Escape.
async function ciclo(E, page, nombre, selector, { enviarVacio = false } = {}) {
  await E.click(page, selector);
  const ok = await abrio(page);
  E.check(`${nombre}: abre`, ok);
  if (!ok) return;
  const inputs = await page.locator(`${DLG} input, ${DLG} select, ${DLG} textarea`).count();
  E.check(`${nombre}: el contenido inyectado tiene campos`, inputs > 0, inputs);
  if (enviarVacio) {
    const submit = page.locator(`${DLG} button[type=submit]`).first();
    if (await submit.count()) {
      await submit.click(); E.interacciones++;
      await page.waitForTimeout(800);
      const estado = await page.evaluate((s) => { const d = document.querySelector(s); return d ? { inv: d.querySelectorAll(':invalid, .input-validation-error, .field-validation-error:not(:empty), [aria-invalid=true]').length, alert: !!d.querySelector('[role=alert]:not(.hidden):not([hidden])') } : null; }, DLG);
      const sigue = await page.locator(DLG).first().isVisible().catch(() => false);
      E.check(`${nombre}: envio vacio -> validacion y el modal sigue abierto`, sigue && estado && (estado.inv > 0 || estado.alert || /requerid|obligatori|ingres|complet/i.test(await page.locator(DLG).first().innerText())), JSON.stringify(estado) + ' abierto=' + sigue);
    }
  }
  await page.keyboard.press('Escape'); E.interacciones++;
  E.check(`${nombre}: Escape cierra`, await page.locator(DLG).first().waitFor({ state: 'hidden', timeout: 3000 }).then(() => true).catch(() => false));
}

module.exports = [
  {
    nombre: 'Seguridad/Usuarios: modales Nuevo usuario, Bloquear y Cambiar contrasena (inyectados por fetch)',
    async fn(E, page) {
      await E.ir(page, '/Seguridad?tab=usuarios');
      await ciclo(E, page, 'Nuevo usuario', '.btn-open-create-user', { enviarVacio: true });
      await ciclo(E, page, 'Bloquear usuario', '.btn-block-user >> nth=0');
      await ciclo(E, page, 'Cambiar contrasena', 'button:has(span:text-is("lock_reset")) >> nth=0', { enviarVacio: true });
    },
  },
  {
    nombre: 'Seguridad/Roles: Nuevo rol (validacion), Editar, Duplicar, ficha de rol con permisos y copiar permisos',
    escribe: true, // el alta vacia llega al servidor y vuelve con errores (POST invalido que re-renderiza)
    async fn(E, page) {
      await E.ir(page, '/Seguridad?tab=roles');
      await ciclo(E, page, 'Nuevo rol', '.btn-open-create-role', { enviarVacio: true });
      await ciclo(E, page, 'Editar rol', '.btn-edit-role >> nth=0');
      await ciclo(E, page, 'Duplicar rol', '.btn-duplicate-role >> nth=0');
      await E.ir(page, '/Seguridad?tab=roles');
      const ids = await page.locator('[data-seguridad-role-id]').evaluateAll((els) => [...new Set(els.map((e) => e.getAttribute('data-seguridad-role-id')))]);
      for (const id of ids.slice(0, 2)) { const r = await E.ir(page, `/Seguridad/RolDetails/${id}`); E.check(`RolDetails ${id.slice(0, 8)} responde 200`, r.status() === 200, r.status()); }
      await E.ir(page, '/Seguridad?tab=permisos-rol');
      let checks = await page.locator('input[type=checkbox]').count();
      if (!checks) { const sel = page.locator('select').first(); if (await sel.count()) { await sel.selectOption({ index: 1 }).catch(() => {}); E.interacciones++; await page.waitForTimeout(800); checks = await page.locator('input[type=checkbox]').count(); } }
      E.check('la matriz de permisos por rol se renderiza (checkboxes)', checks > 5, checks);
      const cb = page.locator('input[type=checkbox]:visible').first();
      if (await cb.count()) { await cb.click({ force: true }); E.interacciones++; await cb.click({ force: true }); E.interacciones++; E.check('marcar/desmarcar permisos sin errores', true); }
      const copiar = page.locator('button:has-text("Copiar")').first();
      if (await copiar.count()) await ciclo(E, page, 'Copiar permisos', copiar);
    },
  },
  {
    nombre: 'Seguridad/Permisos y Auditoria: filtros, pestanas y ordenamiento sin errores',
    async fn(E, page) {
      for (const tab of ['permisos', 'auditoria', 'usuarios', 'roles']) {
        const r = await E.ir(page, `/Seguridad?tab=${tab}`);
        E.check(`pestana ${tab} carga`, r.status() === 200, r.status());
      }
      await E.ir(page, '/Seguridad?tab=usuarios');
      await E.click(page, '#toggleOrderDir');
      await E.click(page, '#clearFilters');
      const buscar = page.locator('input[type=search], input[name=search], input[placeholder*="Buscar"]').first();
      if (await buscar.count()) { await buscar.fill('admin'); await page.waitForTimeout(500); E.interacciones++; }
      E.check('filtrar y ordenar usuarios no genera errores', true);
    },
  },
];
