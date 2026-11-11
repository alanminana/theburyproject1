// Medición de violaciones CSP durante la suite E2E existente (D-05). Solo mide: no cambia el comportamiento de ninguna prueba.
//
// Uso (la app debe correr con Csp__Mode=ReportOnly):
//   CSP_MEDIR_OUT=/ruta/csp-medicion.jsonl NODE_OPTIONS="--require /ruta/scripts/qa/csp-medicion/preload.js" npx playwright test --project=1366x768
//
// Envuelve chromium.launch() de playwright-core (API pública) para que cada contexto que cree la suite registre:
//   - eventos `securitypolicyviolation` (directiva, URI bloqueada, ruta de la página, archivo/línea, disposición);
//   - errores de página (`pageerror`) y mensajes de consola de tipo error que mencionen «Content Security Policy».
// Cada evento se agrega como una línea JSON a CSP_MEDIR_OUT (appendFileSync: seguro entre workers). Sin CSP_MEDIR_OUT no hace nada.
// `resumen.js` agrupa el resultado. No se ejecuta nunca en producción: solo lo activa quien define NODE_OPTIONS.
'use strict';
const fs = require('fs');

const OUT = process.env.CSP_MEDIR_OUT;
if (OUT) {
  let chromium;
  try { chromium = require('playwright-core').chromium; } catch { chromium = null; }
  if (chromium && !chromium.__cspMedirParcheado) {
    chromium.__cspMedirParcheado = true;
    const escribir = (o) => { try { fs.appendFileSync(OUT, JSON.stringify(o) + '\n'); } catch { /* medición best-effort */ } };

    const instrumentar = async (ctx) => {
      if (ctx.__cspMedir) return;
      ctx.__cspMedir = true;
      escribir({ t: 'contexto-creado', pid: process.pid }); // resumen.js exige creados == instrumentados + fallos
      const vigilar = (p) => {
        if (p.__cspMedirP) return;
        p.__cspMedirP = true;
        p.on('pageerror', (err) => escribir({ t: 'pageerror', mensaje: String(err && err.message || err).slice(0, 200), pagina: p.url() }));
        p.on('console', (m) => {
          if (m.type() === 'error' && /content security policy/i.test(m.text())) escribir({ t: 'consola-csp', mensaje: m.text().slice(0, 200), pagina: p.url() });
        });
      };
      ctx.on('page', vigilar);
      ctx.pages().forEach(vigilar);
      try {
        await ctx.exposeBinding('__cspMedirV', (src, v) => escribir({ t: 'violacion', ...v, pagina: src.page ? src.page.url() : null }));
        await ctx.addInitScript(() => {
          document.addEventListener('securitypolicyviolation', (e) => {
            try {
              window.__cspMedirV({
                directiva: e.effectiveDirective, bloqueado: e.blockedURI, archivo: e.sourceFile, linea: e.lineNumber,
                disposicion: e.disposition, ruta: location.pathname, muestra: (e.sample || '').slice(0, 80),
              });
            } catch { /* ignorar */ }
          });
        });
        // Solo se cuenta el contexto si ambos mecanismos quedaron instalados.
        escribir({ t: 'contexto', pid: process.pid });
      } catch (err) {
        // Contexto ya cerrado o fallo de instalacion: invalida la medicion (resumen.js sale con codigo 3).
        escribir({ t: 'fallo-instrumentacion', mensaje: String(err && err.message || err).slice(0, 200), pid: process.pid });
      }
    };

    // launchPersistentContext devuelve directamente un contexto: tambien se mide (si no, quedaria fuera sin avisar).
    if (typeof chromium.launchPersistentContext === 'function') {
      const lpc = chromium.launchPersistentContext.bind(chromium);
      chromium.launchPersistentContext = async (...a) => { const ctx = await lpc(...a); await instrumentar(ctx); return ctx; };
    }
    const launch = chromium.launch.bind(chromium);
    chromium.launch = async (...a) => {
      const browser = await launch(...a);
      const newContext = browser.newContext.bind(browser);
      browser.newContext = async (...b) => { const ctx = await newContext(...b); await instrumentar(ctx); return ctx; };
      const newPage = browser.newPage.bind(browser);
      browser.newPage = async (...b) => { const page = await newPage(...b); await instrumentar(page.context()); return page; };
      return browser;
    };
  }
}
