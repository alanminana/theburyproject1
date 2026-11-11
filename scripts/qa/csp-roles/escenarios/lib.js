// Infraestructura compartida de los escenarios F2.1 (CSP Enforce con interacciones reales). Reutiliza el medidor del resto de
// scripts/qa/csp-roles: securitypolicyviolation + pageerror + consola CSP, y agrega comprobaciones FUNCIONALES por escenario.
'use strict';

const BASE = process.env.E2E_BASE_URL || '';
const USERS = { administrador: 'Admin123!', gerente: 'Gerente123!', vendedor: 'Vendedor123!', cajero: 'Cajero123!', contador: 'Contador123!' };

function assertEntorno() {
  if (process.env.CI) throw new Error('escenarios CSP: pensado para un stack QA local aislado; no corre en CI.');
  if (process.env.CSP_QA_STACK_AISLADO !== '1')
    throw new Error('escenarios CSP: definí CSP_QA_STACK_AISLADO=1 para confirmar que E2E_BASE_URL es un stack QA descartable con datos sinteticos (escribe: crea/edita filas).');
  if (!/^http:\/\/(127\.0\.0\.1|localhost):\d+$/.test(BASE))
    throw new Error('escenarios CSP: E2E_BASE_URL debe ser http://127.0.0.1:<puerto> o localhost (stack local aislado).');
}

const FUENTES_GOOGLE = /fonts\.(googleapis|gstatic)\.com/;

class Escenario {
  constructor(nombre, ctx, opts) {
    this.nombre = nombre; this.ctx = ctx; this.opts = opts;
    this.viol = []; this.pageerrors = []; this.consola = []; this.http5xx = []; this.http4xx = []; this.fallidas = [];
    this.checks = []; this.dialogs = []; this.bloqueadas = [];
    this.interacciones = 0; this.paginas = 0; this.csp = new Set();
    this.aceptarDialogos = false;
    this.pages = new Set();
  }
  check(desc, cond, detalle) { this.checks.push({ desc, ok: !!cond, detalle: detalle === undefined ? '' : String(detalle) }); return !!cond; }
  async nuevaPagina() {
    const page = await this.ctx.newPage();
    this.pages.add(page);
    page.on('pageerror', (e) => this.pageerrors.push(`${page.url().replace(this.opts.base, '')} :: ${String(e.message).slice(0, 160)}`));
    page.on('console', (m) => {
      if (m.type() !== 'error' && m.type() !== 'warning') return;
      const t = m.text();
      if (/content security policy|violates the following|refused to (apply|execute|load|connect|frame)/i.test(t) && !/frame-ancestors.*report-only/i.test(t)) { this.viol.push('consola: ' + t.slice(0, 200)); return; }
      if (m.type() === 'error' && /net::ERR_FAILED/.test(t) && this.bloqueadas.length) return; // request abortada por el arnes (escritura bloqueada)
      if (m.type() === 'error' && !FUENTES_GOOGLE.test(t) && !FUENTES_GOOGLE.test(m.location().url || '')) this.consola.push(`${page.url().replace(this.opts.base, '')} :: ${t.slice(0, 160)}`);
    });
    page.on('response', (r) => {
      const st = r.status();
      if (r.url().startsWith(this.opts.base)) {
        const h = r.headers();
        const tipo = this.opts.modo === 'enforce' ? 'content-security-policy' : 'content-security-policy-report-only';
        if (r.request().resourceType() === 'document' && !h[tipo]) this.csp.add(r.url().replace(this.opts.base, ''));
      }
      const ruta = r.url().replace(this.opts.base, '');
      if (st >= 500) this.http5xx.push(`${st} ${r.request().method()} ${ruta}`);
      else if (st >= 400 && !/favicon/.test(ruta)) this.http4xx.push(`${st} ${r.request().method()} ${ruta}`);
    });
    page.on('requestfailed', (r) => { const u = r.url(); if (this.bloqueadas.includes(`${r.method()} ${new URL(u).pathname}`)) return; if (!FUENTES_GOOGLE.test(u) && !/\/hubs\//.test(u) && !/ERR_ABORTED/.test(r.failure()?.errorText || '')) this.fallidas.push(`${r.method()} ${u.replace(this.opts.base, '')} ${r.failure()?.errorText}`); });
    page.on('dialog', async (d) => { this.dialogs.push(`${d.type()}: ${d.message().slice(0, 100)}`); if (this.aceptarDialogos) await d.accept(); else await d.dismiss(); });
    page.on('framenavigated', (f) => { if (f === page.mainFrame()) this.paginas++; });
    return page;
  }
  async ir(page, ruta, espera = 'networkidle') {
    const r = await page.goto(this.opts.base + ruta, { waitUntil: espera, timeout: 45000 });
    return r;
  }
  async click(page, sel, desc) {
    const loc = typeof sel === 'string' ? page.locator(sel).first() : sel;
    await loc.click({ timeout: 8000 });
    this.interacciones++;
    return loc;
  }
  async visible(page, sel, ms = 3000) {
    return page.locator(sel).first().waitFor({ state: 'visible', timeout: ms }).then(() => true).catch(() => false);
  }
  async oculto(page, sel, ms = 3000) {
    return page.locator(sel).first().waitFor({ state: 'hidden', timeout: ms }).then(() => true).catch(() => false);
  }
  // Guarda de escritura: una escritura solo se permite si el escenario declaró {escribe:true}; en otro caso se aborta (y se anota).
  async instalarRutas() {
    await this.ctx.route('**/*', (route) => {
      const r = route.request(); const m = r.method();
      if (/^(data|blob|about):/.test(r.url())) return route.continue();
      let u; try { u = new URL(r.url()); } catch { return route.continue(); }
      if (u.origin !== new URL(this.opts.base).origin) { if (FUENTES_GOOGLE.test(u.hostname)) return route.continue(); this.bloqueadas.push(`origen ${m} ${u.origin}`); return route.abort(); }
      if (['GET', 'HEAD', 'OPTIONS'].includes(m) || this.escribe || /\/hubs\/|\/csp-report$|\/Account\/Login$/.test(u.pathname)) return route.continue();
      this.bloqueadas.push(`${m} ${u.pathname}`); return route.abort();
    });
  }
}

async function login(page, base, user = 'administrador') {
  await page.goto(`${base}/Identity/Account/Login`, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.fill('#Input_UserName, input[name="Input.UserName"]', user);
  await page.fill('#input-password, input[name="Input.Password"]', USERS[user]);
  await page.click('button[type="submit"]');
  const nombre = page.locator('input[placeholder="Nombre y apellido"]');
  if (await nombre.isVisible({ timeout: 4000 }).catch(() => false)) {
    await nombre.fill(`CSP ${user}`);
    await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
    await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
  }
  await page.waitForURL((u) => !u.toString().toLowerCase().includes('/login'), { timeout: 20000 });
}

// Medidor de violaciones a nivel de contexto (todas las paginas y frames hijos del contexto).
async function instrumentar(ctx, esc) {
  await ctx.exposeBinding('__cspEsc', (src, v) => { const e = ctx.__escActual; if (e) e.viol.push(v); });
  await ctx.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', (e) => {
      try { window.__cspEsc(`${e.effectiveDirective} | ${(e.blockedURI || '').slice(0, 80)} | ${e.disposition} | ${location.pathname}`); } catch { /* ignorar */ }
    });
  });
}

// Barrido de interacciones: hace clic (con escrituras bloqueadas, dialogos descartados) en cada control distinto de la pagina que no sea
// chrome del layout y registra si abrio un overlay. Un control = una firma (tag + atributos data-* funcionales), max `porFirma` por firma.
async function barrer(E, page, ruta, { porFirma = 2, max = 40, excluir = null } = {}) {
  await E.ir(page, ruta);
  const SEL = 'button:not([type=submit]):not([disabled]), [role=button], [role=tab], summary, [data-action], [data-fn]';
  const listar = () => page.evaluate(({ SEL, excluir }) => {
    const out = []; const vis = (e) => !!(e.offsetWidth || e.offsetHeight);
    document.querySelectorAll(SEL).forEach((e, i) => {
      if (!vis(e) || e.closest('aside, nav, #sidebar, [id^="tp-"], #ticket-panel, #notas-panel, #notificaciones-panel')) return;
      if (e.matches('[data-user-menu-toggle], #collapseSidebar, #btn-open-notificaciones, #btn-open-notas, #btn-open-ticket-panel')) return;
      const attrs = [...e.attributes].filter((a) => /^data-/.test(a.name) && !/(-id|-nombre|-codigo|-url|-user|-return|-valor|-value)$/.test(a.name) && a.value.length < 40).map((a) => a.name + '=' + a.value).sort().join('&');
      const txt = (e.innerText || '').trim().replace(/\s+/g, ' ').slice(0, 25);
      const sig = e.tagName + '|' + attrs + '|' + (attrs ? '' : txt);
      if (excluir && new RegExp(excluir, 'i').test(sig + ' ' + txt)) return;
      out.push({ i, sig, txt });
    });
    return out;
  }, { SEL, excluir });
  const cand = await listar();
  const cuenta = new Map(); const elegidos = [];
  for (const c of cand) { const n = (cuenta.get(c.sig) || 0) + 1; cuenta.set(c.sig, n); if (n <= porFirma && elegidos.length < max) elegidos.push(c); }
  let hechos = 0; let overlays = 0;
  for (const c of elegidos) {
    await E.ir(page, ruta);
    const loc = page.locator(SEL).nth(c.i);
    const ok = await loc.click({ timeout: 2500 }).then(() => true).catch(() => false);
    if (!ok) continue;
    E.interacciones++; hechos++;
    await page.waitForTimeout(350);
    const ov = await page.evaluate(() => [...document.querySelectorAll('[role=dialog], [aria-modal=true], dialog[open], .fixed.inset-0')].some((e) => !e.classList.contains('hidden') && (e.offsetWidth || e.offsetHeight) && getComputedStyle(e).visibility !== 'hidden')).catch(() => false);
    if (ov) overlays++;
    if (page.url() !== E.opts.base + ruta && !page.url().startsWith(E.opts.base + ruta.split('?')[0])) await page.waitForLoadState('domcontentloaded').catch(() => {});
    await page.keyboard.press('Escape').catch(() => {});
  }
  E.check(`barrido ${ruta}: ${hechos} controles accionados (${elegidos.length} firmas distintas, ${overlays} abrieron un overlay)`, hechos > 0 && hechos >= Math.floor(elegidos.length * 0.6), `${hechos}/${elegidos.length}`);
  return { hechos, overlays, firmas: elegidos.length };
}

module.exports = { barrer, BASE, assertEntorno, Escenario, login, instrumentar };
