// Guarda común de los scripts de navegador: solo contra un clon de QA local, sin escrituras posibles.
//  - exige CSP_QA_CLON=1 (habilitación explícita) y que no sea CI;
//  - el destino debe ser localhost/127.0.0.1 (nunca otro host);
//  - bloquea a nivel de red TODO método distinto de GET/HEAD/OPTIONS salvo el login (y SignalR /hubs): un confirm que no
//    detuviera el envío no llegaría al servidor; sin service workers;
//  - exige datos centinela de la siembra sintética antes de interactuar (assertSentinel).
const BASE = process.env.CSP_BASE_URL || 'http://127.0.0.1:5199';
const USER = process.env.CSP_QA_USER || 'admin';
const PASS = process.env.CSP_QA_PASS || 'Admin123!';

function assertEntorno() {
  if (process.env.CI) throw new Error('csp-sintetico: no se ejecuta en CI.');
  if (process.env.CSP_QA_CLON !== '1')
    throw new Error('csp-sintetico: definí CSP_QA_CLON=1 para confirmar que el destino es un CLON de QA con la siembra sintética (nunca TheBuryProjectDb).');
  const u = new URL(BASE);
  if (!['127.0.0.1', 'localhost'].includes(u.hostname))
    throw new Error('csp-sintetico: el destino debe ser localhost/127.0.0.1, no ' + u.hostname);
}

async function newContext(browser, opts) {
  assertEntorno();
  const ctx = await browser.newContext({ ...opts, serviceWorkers: 'block' });
  await ctx.route('**/*', route => {
    const r = route.request();
    const m = r.method();
    const url = new URL(r.url());
    if (!['127.0.0.1', 'localhost'].includes(url.hostname) && !/^(data|blob):/.test(r.url())) {
      // recursos externos (fuentes de Google): se permiten solo GET
      return m === 'GET' ? route.continue() : route.abort();
    }
    if (['GET', 'HEAD', 'OPTIONS'].includes(m)) return route.continue();
    if (m === 'POST' && (/\/Identity\/Account\/Login$/.test(url.pathname) || /\/hubs\//.test(url.pathname) || /\/csp-report$/.test(url.pathname))) return route.continue();
    console.log('BLOQUEADO (escritura):', m, url.pathname);
    return route.abort();
  });
  return ctx;
}

async function login(page) {
  await page.goto(BASE + '/Identity/Account/Login');
  await page.fill('input[name="Input.UserName"]', USER);
  await page.fill('input[name="Input.Password"]', PASS);
  await Promise.all([page.waitForNavigation(), page.click('form[method=post] button[type=submit]')]);
  if (new URL(page.url()).origin !== new URL(BASE).origin) throw new Error('csp-sintetico: redirección fuera del origen autorizado.');
}

// Aborta si la página no contiene el texto centinela de la siembra (evita operar sobre datos reales con el mismo Id).
async function assertSentinel(page, url, texto) {
  const r = await page.goto(BASE + url, { waitUntil: 'networkidle' });
  const html = await page.content();
  if (!r || r.status() !== 200 || !html.includes(texto))
    throw new Error(`csp-sintetico: ${url} no contiene el dato sintético «${texto}»; se aborta (¿clon sin sembrar o base real?).`);
}

module.exports = { BASE, USER, PASS, newContext, login, assertSentinel, assertEntorno };
