#!/usr/bin/env node
// Logout que revoca todas las sesiones del usuario (decision del propietario 2026-11-11), sondeo EN VIVO contra un stack QA local en Production.
// No necesita SQL: solo HTTP contra el stack (usuarios sinteticos de DbInitializer). Sale con codigo != 0 si algun caso falla o no pudo autenticar.
//   E2E_BASE_URL=http://127.0.0.1:18260 node scripts/qa/seguridad/sondeo-logout-multinavegador.js
// Casos: 3 motores (Chromium/Firefox/WebKit) con sesiones PROPIAS del mismo usuario + cookies CLONADAS entre motores; logout desde un motor;
// efecto inmediato en la solicitud siguiente; otro usuario intacto; nuevo login funciona y la cookie vieja sigue muerta; dobles envios y
// logouts simultaneos (carrera real contra SQL Server) sin 5xx.
'use strict';
const { chromium, firefox, webkit } = require('playwright');
const L = require('./lib-qa-seguridad');

const PASS = { cajero: 'Cajero123!', vendedor: 'Vendedor123!' };
const resultados = [];
const caso = (nombre, ok, detalle = '') => { resultados.push({ nombre, ok, detalle }); console.log(`${ok ? 'OK    ' : 'FALLA '} ${nombre}${detalle ? ' :: ' + detalle : ''}`); };
const estado = async (request, ruta) => (await request.get(L.BASE + ruta, { maxRedirects: 0 })).status();
const cookieDeSesion = async (ctx) => (await ctx.cookies()).filter((c) => /Identity\.Application/i.test(c.name));

async function sesion(browser, user) { const ctx = await browser.newContext(); await L.login(ctx, user, PASS[user]); return ctx; }
async function logoutPost(ctx) {
  const t = await L.tokenDe(ctx.request);
  const r = await ctx.request.post(`${L.BASE}/Identity/Account/Logout`, { form: { __RequestVerificationToken: t }, headers: { RequestVerificationToken: t }, maxRedirects: 0 });
  return r.status();
}

(async () => {
  const motores = { chromium: await chromium.launch(), firefox: await firefox.launch(), webkit: await webkit.launch() };
  try {
    // --- sesiones del mismo usuario en 3 motores + cookies clonadas entre motores ---
    const a = await sesion(motores.chromium, 'cajero');
    const b = await sesion(motores.firefox, 'cajero');
    const c = await sesion(motores.webkit, 'cajero');
    const otro = await sesion(motores.chromium, 'vendedor');
    const cookiesA = await cookieDeSesion(a);
    caso('cookie de sesion presente', cookiesA.length > 0);
    const clonFirefox = await motores.firefox.newContext(); await clonFirefox.addCookies(cookiesA);
    const clonWebkit = await motores.webkit.newContext(); await clonWebkit.addCookies(cookiesA);
    const clonChromium = await motores.chromium.newContext(); await clonChromium.addCookies(cookiesA);
    const todas = {
      'chromium (sesion propia)': a, 'firefox (sesion propia)': b, 'webkit (sesion propia)': c,
      'cookie clonada en firefox': clonFirefox, 'cookie clonada en webkit': clonWebkit, 'cookie clonada en chromium': clonChromium,
    };
    for (const [n, ctx] of Object.entries(todas)) caso(`antes del logout: ${n} accede (200)`, (await estado(ctx.request, '/Dashboard')) === 200);
    caso('antes del logout: otro usuario accede (200)', (await estado(otro.request, '/Dashboard')) === 200);

    // --- logout desde UN motor: boton real del menu de usuario (flujo del navegador) ---
    const page = await a.newPage();
    await page.goto(`${L.BASE}/Dashboard`);
    const btn = page.locator('form[action*="Logout"] button').first();
    let via = 'POST con token';
    if (await btn.count()) {
      await page.locator('header button[aria-haspopup]').first().click({ timeout: 3000 }).catch(() => {});
      if (await btn.isVisible().catch(() => false)) { await btn.click(); via = 'boton «Cerrar sesion» del menu'; }
    }
    if (via === 'POST con token') caso('logout por POST con token (302)', (await logoutPost(a)) === 302);
    await page.waitForTimeout(1500);
    console.log(`INFO   logout ejecutado via ${via}`);
    caso('el logout termina en la pantalla de login (no en un error)', /\/identity\/account\/login/i.test(page.url()) || via === 'POST con token', page.url());

    for (const [n, ctx] of Object.entries(todas)) caso(`tras el logout: ${n} pierde acceso en la solicitud siguiente (302)`, (await estado(ctx.request, '/Dashboard')) === 302);
    caso('tras el logout: otro usuario sigue accediendo (200)', (await estado(otro.request, '/Dashboard')) === 200);

    // --- nuevo login funciona y la cookie vieja sigue muerta ---
    const nueva = await sesion(motores.firefox, 'cajero');
    caso('nuevo login tras el logout: accede (200)', (await estado(nueva.request, '/Dashboard')) === 200);
    caso('nuevo login: las cookies clonadas viejas siguen muertas (302)',
      (await estado(clonWebkit.request, '/Dashboard')) === 302 && (await estado(clonChromium.request, '/Dashboard')) === 302);
    // Cookie vieja intentando cerrar sesion: no debe revocar la sesion nueva (el stamp rota solo con una sesion VIVA).
    const t = await L.tokenDe(nueva.request);
    await clonWebkit.request.post(`${L.BASE}/Identity/Account/Logout`, { form: { __RequestVerificationToken: t }, maxRedirects: 0 });
    caso('logout con una cookie vieja no revoca la sesion nueva (200)', (await estado(nueva.request, '/Dashboard')) === 200);

    // --- doble envio / logouts simultaneos contra SQL Server real ---
    const N = 6; const PARALELO = 8;
    let cinco = 0; let ok302 = 0; let revocadas = 0;
    for (let i = 0; i < N; i++) {
      const s = await sesion(motores.chromium, 'cajero');
      const tok = await L.tokenDe(s.request);
      const rs = await Promise.all(Array.from({ length: PARALELO }, () =>
        s.request.post(`${L.BASE}/Identity/Account/Logout`, { form: { __RequestVerificationToken: tok }, maxRedirects: 0 }).then((r) => r.status())));
      cinco += rs.filter((x) => x >= 500).length;
      ok302 += rs.filter((x) => x === 302).length;
      if ((await estado(s.request, '/Dashboard')) === 302) revocadas++;
      await s.close();
    }
    caso(`${N} rondas x ${PARALELO} logouts simultaneos de la misma sesion: 0 respuestas 5xx`, cinco === 0, `5xx=${cinco}, 302=${ok302}/${N * PARALELO}`);
    caso(`${N} rondas: la sesion queda revocada en todas`, revocadas === N, `${revocadas}/${N}`);
    caso('todas las respuestas de los logouts simultaneos son 302 (sin 400 por doble clic)', ok302 === N * PARALELO, `${ok302}/${N * PARALELO}`);

    // --- logout SIMULTANEO con logins del mismo usuario (cada login escribe UltimoAcceso/AccessFailedCount sobre la misma fila) ---
    // Revision independiente: una escritura concurrente sobre la fila del usuario no puede hacer fallar la rotacion del stamp ni dejar viva
    // la sesion que cierra. Las sesiones creadas por los logins simultaneos pueden o no sobrevivir (depende del orden); la del logout, nunca.
    let viva = 0; let cincoLogin = 0; const RONDAS = 5;
    for (let i = 0; i < RONDAS; i++) {
      const s = await sesion(motores.chromium, 'cajero');
      const cookie = await cookieDeSesion(s);
      const copia2 = await motores.firefox.newContext(); await copia2.addCookies(cookie);
      const tok = await L.tokenDe(s.request);
      const [rLogout, ...logins] = await Promise.allSettled([
        s.request.post(`${L.BASE}/Identity/Account/Logout`, { form: { __RequestVerificationToken: tok }, maxRedirects: 0 }).then((r) => r.status()),
        sesion(motores.firefox, 'cajero'), sesion(motores.webkit, 'cajero'), sesion(motores.chromium, 'cajero'),
      ]);
      if (rLogout.status !== 'fulfilled' || rLogout.value !== 302) cincoLogin++;
      for (const l of logins) if (l.status === 'fulfilled') await l.value.close();
      if ((await estado(copia2.request, '/Dashboard')) !== 302) viva++;
      await copia2.close(); await s.close();
    }
    caso(`${RONDAS} rondas de logout simultaneo con 3 logins del mismo usuario: el logout responde 302`, cincoLogin === 0, `no-302=${cincoLogin}`);
    caso(`${RONDAS} rondas: la cookie copiada de la sesion que cerro queda revocada SIEMPRE`, viva === 0, `vivas=${viva}`);
  } finally {
    for (const m of Object.values(motores)) await m.close().catch(() => {});
  }
  const mal = resultados.filter((r) => !r.ok);
  console.log(`\nsondeo-logout-multinavegador: ${resultados.length} casos, ${mal.length} fallidos`);
  process.exit(mal.length ? 1 : 0);
})().catch((e) => { console.error('ERROR del sondeo:', e.message); process.exit(3); });
