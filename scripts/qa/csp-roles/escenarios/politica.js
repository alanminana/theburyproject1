// F2.2: la politica es efectiva y observable. Controles NEGATIVOS (violaciones provocadas a proposito), reporte a /csp-report con WARN en el log
// de la app, rate limit, nonce por solicitud, SignalR conectado y paginas de error con cabecera.
'use strict';
const { execFileSync } = require('child_process');

const CONTENEDOR = process.env.CSP_QA_APP_CONTAINER || 'bury-mb-app-1';
const logsApp = () => { try { return execFileSync('docker', ['logs', '--since', '3m', CONTENEDOR], { encoding: 'utf8', maxBuffer: 20 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] }); } catch (e) { return String(e.stdout || '') + String(e.stderr || ''); } };

module.exports = [
  {
    nombre: 'Politica efectiva: script inline sin nonce, handler inline, estilo inline y recurso externo son BLOQUEADOS y reportados (control negativo)',
    violacionesEsperadas: [/script-src-elem|Executing inline script/, /script-src-attr|inline event handler/, /style-src-elem|Applying inline style/, /img-src|frame-src|example\.invalid/],
    permitFallidas: [/example\.invalid/],
    async fn(E, page) {
      await E.ir(page, '/Cliente');
      await page.evaluate(() => {
        const s = document.createElement('script'); s.textContent = 'window.__pwnInline = true'; document.body.appendChild(s);
        const b = document.createElement('button'); b.id = 'qa-inline'; b.setAttribute('onclick', 'window.__pwnAttr = true'); b.textContent = 'x'; b.style.cssText = 'position:fixed;top:5px;left:5px;z-index:99999'; document.body.appendChild(b);
        const st = document.createElement('style'); st.textContent = 'body{outline:3px solid red}'; document.head.appendChild(st);
        const i = document.createElement('img'); i.src = 'https://example.invalid/x.png'; document.body.appendChild(i);
        const f = document.createElement('iframe'); f.src = 'https://example.invalid/'; document.body.appendChild(f);
      });
      E.interacciones++;
      await page.click('#qa-inline'); E.interacciones++;
      await page.waitForTimeout(1500);
      const r = await page.evaluate(() => ({ inline: !!window.__pwnInline, attr: !!window.__pwnAttr, estilo: getComputedStyle(document.body).outlineStyle !== 'solid' || getComputedStyle(document.body).outlineWidth === '0px' }));
      E.check('el script inline sin nonce NO se ejecuta', !r.inline);
      E.check('el handler onclick inline NO se ejecuta', !r.attr);
      E.check('el <style> inline sin nonce NO se aplica', r.estilo);
      // el reporte llega a /csp-report y se registra como WARN (report-uri; el envio es asincrono)
      await page.waitForTimeout(2500);
      const log = logsApp();
      E.check('el log de la app registra "CSP violation" para script-src-elem', /CSP violation:.*script-src-elem/.test(log), `contenedor=${CONTENEDOR}`);
      E.check('el log registra la violacion de script-src-attr (handler inline)', /CSP violation:.*script-src-attr/.test(log));
      E.check('el log registra la violacion de style-src-elem', /CSP violation:.*style-src-elem/.test(log));
    },
  },
  {
    nombre: 'Politica: nonce distinto por solicitud (HTML), paginas de error con cabecera, SignalR conectado',
    permit4xx: [/404 GET \/ruta-que-no-existe/],
    async fn(E, page) {
      const ws = [];
      page.on('websocket', (w) => { const r = { url: w.url(), cerrado: false }; w.on('close', () => { r.cerrado = true; }); ws.push(r); });
      const nonces = new Set();
      for (let i = 0; i < 4; i++) {
        const r = await E.ir(page, '/Cliente');
        const csp = r.headers()['content-security-policy'] || '';
        const n = (csp.match(/script-src 'self' 'nonce-([^']+)'/) || [])[1];
        nonces.add(n);
        const enHtml = await page.evaluate((x) => [...document.querySelectorAll('script[nonce]')].some((s) => s.nonce === x), n);
        E.check(`solicitud ${i + 1}: el nonce de la cabecera coincide con el de los <script> inline`, !!n && enHtml, n);
      }
      E.check('el nonce cambia en cada solicitud', nonces.size === 4, nonces.size);
      const r404 = await E.ir(page, '/ruta-que-no-existe');
      E.check('la pagina 404 (HTML con layout) lleva Content-Security-Policy', r404.status() === 404 && !!r404.headers()['content-security-policy'] && !r404.headers()['content-security-policy-report-only']);
      const j404 = await page.context().request.get(`${E.opts.base}/api/ruta-que-no-existe`, { headers: { Accept: 'application/json' } });
      E.check('el 404 de API (respuesta de AJAX) lleva Content-Security-Policy', !!j404.headers()['content-security-policy']);
      await E.ir(page, '/');
      await page.waitForFunction(() => true);
      await page.waitForTimeout(4000);
      const hub = ws.filter((w) => /\/hubs\/notificaciones/.test(w.url));
      E.check('SignalR: el WebSocket /hubs/notificaciones esta abierto bajo Enforce', hub.length > 0 && hub.some((w) => !w.cerrado), `sockets=${hub.length}`);
      E.interacciones++;
    },
  },
  {
    // El limite es por IP y ventana de 1 minuto (30): este escenario debe ser el ULTIMO de los que envian reportes.
    nombre: 'Politica: el rate limit de /csp-report corta el flood (429) sin dejar de aceptar los primeros reportes',
    async fn(E, page) {
      await E.ir(page, '/Cliente');
      const cuerpo = JSON.stringify({ 'csp-report': { 'document-uri': 'http://qa/', 'violated-directive': 'script-src-elem', 'blocked-uri': 'inline' } });
      const sonda = async () => (await page.context().request.post(`${E.opts.base}/csp-report`, { data: cuerpo, headers: { 'Content-Type': 'application/csp-report' } })).status();
      // espera a que se reinicie la ventana de 1 minuto (otros escenarios/corridas previas pueden haberla consumido)
      let libre = false;
      for (let i = 0; i < 40 && !libre; i++) { libre = (await sonda()) === 204; if (!libre) await page.waitForTimeout(2000); }
      E.check('la ventana del rate limit se reinicia y vuelve a aceptar reportes', libre);
      const estados = [];
      for (let i = 0; i < 45; i++) {
        const r = await page.context().request.post(`${E.opts.base}/csp-report`, { data: cuerpo, headers: { 'Content-Type': 'application/csp-report' } });
        estados.push(r.status());
        E.interacciones++;
      }
      const aceptados = estados.filter((x) => x === 204).length; const limitados = estados.filter((x) => x === 429).length;
      E.check('se aceptan reportes mientras no se supera el limite (204)', aceptados >= 1, aceptados);
      E.check('al superar el limite por IP se responde 429', limitados >= 1, `204=${aceptados} 429=${limitados}`);
      E.check('no hay otro estado inesperado (solo 204/429)', estados.every((x) => x === 204 || x === 429), [...new Set(estados)].join(','));
      const malo = await page.context().request.post(`${E.opts.base}/csp-report`, { data: 'x'.repeat(20000), headers: { 'Content-Type': 'application/csp-report' } });
      // deja el stack sin limitar para el resto de las corridas
      for (let i = 0; i < 40; i++) { if ((await sonda()) === 204) break; await page.waitForTimeout(2000); }
      E.check('un cuerpo > 8 KB se rechaza (413) o se limita (429), nunca se acepta', [413, 429].includes(malo.status()), malo.status());
    },
  },
];
