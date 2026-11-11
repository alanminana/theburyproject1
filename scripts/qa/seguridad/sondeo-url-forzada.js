#!/usr/bin/env node
// F3: sondeo de autorizacion por URL forzada (solo GET, sin seguir redirects). Recoge las pantallas que ve el administrador (menu + un nivel
// de detalle) y las pide como anonimo y como cada rol de prueba. Imprime una matriz de estados y marca anomalias:
//  - anonimo con 200 (debe redirigir a Login o devolver 401/403);
//  - cualquier rol con 5xx;
//  - roles sin acceso esperado a modulos financieros/de administracion que reciben 200 (lista ACCESO_PROHIBIDO, por prefijo de ruta).
// La verdad de la matriz de permisos esta en docs/permisos-matriz-roles.md y en los tests HTTP; esto detecta regresiones gruesas.
// Solo contra un stack QA local aislado. Uso: E2E_BASE_URL=http://127.0.0.1:18180 node scripts/qa/seguridad/sondeo-url-forzada.js
'use strict';
const { chromium } = require('playwright');

const BASE = process.env.E2E_BASE_URL || 'http://127.0.0.1:18080';
if (!/^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/.test(BASE)) { console.error('Solo stack local aislado.'); process.exit(2); }
const USERS = { administrador: 'Admin123!', gerente: 'Gerente123!', vendedor: 'Vendedor123!', cajero: 'Cajero123!', repositor: 'Repositor123!', tecnico: 'Tecnico123!', contador: 'Contador123!' };
// Rutas que, segun docs/permisos-matriz-roles.md, estos roles NO deben abrir (la matriz de permisos las deniega).
const ACCESO_PROHIBIDO = {
  vendedor: [/^\/Seguridad/i, /^\/Usuarios/i, /^\/Roles/i, /^\/ConfiguracionPago/i, /^\/Reporte/i, /^\/Mora\/Configuracion/i],
  cajero: [/^\/Seguridad/i, /^\/Usuarios/i, /^\/Roles/i, /^\/ConfiguracionPago/i, /^\/Reporte/i, /^\/Credito\/Configurar/i],
  repositor: [/^\/Seguridad/i, /^\/Usuarios/i, /^\/Roles/i, /^\/ConfiguracionPago/i, /^\/Caja/i, /^\/Credito/i, /^\/Venta/i],
  tecnico: [/^\/Seguridad/i, /^\/Usuarios/i, /^\/Roles/i, /^\/ConfiguracionPago/i, /^\/Caja/i, /^\/Credito/i, /^\/Venta/i, /^\/Reporte/i],
  contador: [/^\/Seguridad/i, /^\/Usuarios/i, /^\/Roles/i, /^\/ConfiguracionPago/i],
};
const NO_VISITAR = /logout|logoff|signout|\/delete|\/eliminar|\/anular|\/cancelar|\/borrar|\/api\/|\/hubs?\/|health/i;
const DETALLE = /\/(details|detalles|detalle|ver|ficha)[a-z]*(\/|\?|$)/i;

async function login(ctx, user, pass) {
  const page = await ctx.newPage();
  await page.goto(`${BASE}/Identity/Account/Login`);
  await page.fill('#Input_UserName, input[name="Input.UserName"]', user);
  await page.fill('#input-password, input[name="Input.Password"]', pass);
  await page.click('button[type="submit"]');
  const nombre = page.locator('input[placeholder="Nombre y apellido"]');
  if (await nombre.isVisible({ timeout: 5000 }).catch(() => false)) {
    await nombre.fill(`Sondeo ${user}`);
    await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
    await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
  }
  await page.waitForURL((u) => !u.toString().toLowerCase().includes('/login'), { timeout: 20000 });
  return page;
}
const norm = (h) => { try { const u = new URL(h, BASE); if (u.origin !== BASE) return null; u.hash = ''; return u.pathname + u.search; } catch { return null; } };
async function links(page, soloDetalle) {
  const hs = await page.$$eval('a[href]', (as) => as.map((a) => a.getAttribute('href')));
  return [...new Set(hs.map(norm).filter((n) => n && !NO_VISITAR.test(n) && (!soloDetalle || DETALLE.test(n)) && !/^\/Identity\//i.test(n)))];
}
async function estado(request, ruta) {
  const r = await request.get(BASE + ruta, { maxRedirects: 0 });
  return r.status();
}

(async () => {
  const browser = await chromium.launch();
  const ctxAdmin = await browser.newContext();
  const admin = await login(ctxAdmin, 'administrador', USERS.administrador);
  await admin.goto(`${BASE}/`, { waitUntil: 'networkidle' });
  const menu = await links(admin, false);
  const rutas = new Set(menu);
  for (const m of menu) {
    await admin.goto(BASE + m, { waitUntil: 'networkidle' }).catch(() => null);
    (await links(admin, true)).slice(0, 2).forEach((x) => rutas.add(x));
  }
  const lista = [...rutas].sort();
  console.log(`Rutas del administrador: ${lista.length}`);
  // Sin inventario no se probo nada: nunca debe parecer un exito (p. ej. si cambia la pagina de aterrizaje o falla el descubrimiento de enlaces).
  if (lista.length < 10) { console.error(`Inventario insuficiente (${lista.length} rutas): el sondeo no es valido.`); await browser.close(); process.exit(3); }

  const anomalias = [];
  const filas = {};
  // Anonimo
  const anon = await browser.newContext();
  filas.anonimo = {};
  for (const r of lista) {
    const st = await estado(anon.request, r);
    filas.anonimo[r] = st;
    if (st === 200) anomalias.push(`anonimo 200 ${r}`);
    if (st >= 500) anomalias.push(`anonimo ${st} ${r}`);
  }
  for (const [rol, pass] of Object.entries(USERS)) {
    if (rol === 'administrador') continue;
    const ctx = await browser.newContext();
    await login(ctx, rol, pass);
    filas[rol] = {};
    for (const r of lista) {
      const st = await estado(ctx.request, r);
      filas[rol][r] = st;
      if (st >= 500) anomalias.push(`${rol} ${st} ${r}`);
      if (st === 200 && (ACCESO_PROHIBIDO[rol] || []).some((re) => re.test(r))) anomalias.push(`${rol} 200 en ruta prohibida ${r}`);
    }
    await ctx.close();
  }
  for (const rol of Object.keys(filas)) {
    const v = Object.values(filas[rol]);
    const cont = {};
    v.forEach((s) => { cont[s] = (cont[s] || 0) + 1; });
    console.log(`${rol.padEnd(10)} ${JSON.stringify(cont)}`);
  }
  console.log(anomalias.length ? `ANOMALIAS (${anomalias.length}):\n  ${anomalias.join('\n  ')}` : 'Sin anomalias.');
  await browser.close();
  process.exit(anomalias.length ? 1 : 0);
})();
