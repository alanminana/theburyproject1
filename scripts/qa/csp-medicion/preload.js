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
      escribir({ t: 'contexto' });
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
      } catch { /* contexto ya cerrado */ }
      ctx.on('page', (p) => {
        p.on('pageerror', (err) => escribir({ t: 'pageerror', mensaje: String(err && err.message || err).slice(0, 200), pagina: p.url() }));
        p.on('console', (m) => {
          if (m.type() === 'error' && /content security policy/i.test(m.text())) escribir({ t: 'consola-csp', mensaje: m.text().slice(0, 200), pagina: p.url() });
        });
      });
    };

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
