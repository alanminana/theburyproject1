// Helpers de la suite QA de Seguridad/Usuarios/Roles.
// Aislamiento: cada actor usa su propio BrowserContext en memoria; nunca se escribe storageState
// ni se reutilizan cookies/credenciales de otras suites.
const { expect } = require('@playwright/test');

const RUN_ID = process.env.SEG_RUN_ID || `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
const ADMIN_USER = process.env.SEG_ADMIN_USER || 'admin';
const ADMIN_PASS = process.env.SEG_ADMIN_PASS || 'Admin123!';
const TEST_PASS = `Qa${RUN_ID.slice(0, 6)}9x`; // cumple política: dígito, mayúscula, minúscula, >= 6

function uniqueName(prefix) {
  return `qaseg_${prefix}_${RUN_ID}`.slice(0, 50);
}

async function newActor(browser, baseURL) {
  const context = await browser.newContext({ baseURL, ignoreHTTPSErrors: true });
  const page = await context.newPage();
  return { context, page };
}

/**
 * Login por la UI real. Si aparece la pantalla de Términos y Condiciones (primer ingreso), la acepta.
 * Devuelve la URL final.
 */
async function uiLogin(page, userName, password, { acceptTerms = true } = {}) {
  await page.goto('/Identity/Account/Login');
  await page.locator('input[name="Input.UserName"]').fill(userName);
  await page.locator('input[name="Input.Password"]').fill(password);
  await Promise.all([
    page.waitForLoadState('domcontentloaded'),
    page.locator('form:has(input[name="Input.Password"]) button[type="submit"]').first().click(),
  ]);
  await page.waitForLoadState('domcontentloaded');

  const terms = page.locator('input[name="Input.AceptaTerminos"]');
  if (acceptTerms && (await terms.count()) > 0 && (await terms.first().isVisible().catch(() => false))) {
    await page.locator('input[name="Input.NombreCompleto"]').fill(`QA Seguridad ${RUN_ID}`);
    await terms.first().check({ force: true });
    await Promise.all([
      page.waitForLoadState('domcontentloaded'),
      page.locator('form:has(input[name="Input.AceptaTerminos"]) button[type="submit"]').first().click(),
    ]);
    await page.waitForLoadState('domcontentloaded');
  }
  return page.url();
}

function isLoginUrl(url) {
  return /\/Identity\/Account\/Login/i.test(url);
}

/** Toma el token antiforgery de cualquier form de la página indicada (sesión del actor). */
async function getAntiforgeryToken(page, path = '/Seguridad/Index?tab=usuarios') {
  if (path) await page.goto(path);
  const token = await page.locator('input[name="__RequestVerificationToken"]').first().getAttribute('value');
  expect(token, `token antiforgery en ${path}`).toBeTruthy();
  return token;
}

/** POST form-urlencoded con la cookie del actor. maxRedirects 0 para observar el status real. */
async function postForm(page, path, fields, { token, ajax = true } = {}) {
  const form = new URLSearchParams();
  for (const [k, v] of Object.entries(fields)) {
    if (Array.isArray(v)) v.forEach((x) => form.append(k, String(x)));
    else if (v !== undefined && v !== null) form.append(k, String(v));
  }
  if (token) form.append('__RequestVerificationToken', token);
  const headers = { 'Content-Type': 'application/x-www-form-urlencoded' };
  if (ajax) headers['X-Requested-With'] = 'XMLHttpRequest';
  return page.request.post(path, { data: form.toString(), headers, maxRedirects: 0, failOnStatusCode: false });
}

/** GET sin seguir redirects: devuelve { status, location }. */
async function probe(page, path) {
  const res = await page.request.get(path, { maxRedirects: 0, failOnStatusCode: false });
  return { status: res.status(), location: res.headers()['location'] || '' };
}

/** Clasifica una respuesta de acceso: 'allowed' | 'login' | 'denied' | 'other'. */
function classify({ status, location }) {
  if (status === 200) return 'allowed';
  if ((status === 302 || status === 301) && /Account\/Login/i.test(location)) return 'login';
  if (status === 403 || ((status === 302 || status === 301) && /AccessDenied/i.test(location))) return 'denied';
  return `other:${status}:${location}`;
}

let DEFAULT_ROLE = `qaseg_rolZ_${RUN_ID}`.slice(0, 50); // rol base sin permisos, creado en S07 (nombre determinista)
function setDefaultRole(name) { DEFAULT_ROLE = name; }

async function createUser(adminPage, { userName, email, password = TEST_PASS, roles = DEFAULT_ROLE ? [DEFAULT_ROLE] : [], activo = true, extra = {} }) {
  const token = await getAntiforgeryToken(adminPage);
  const res = await postForm(adminPage, '/Usuarios/Create', {
    UserName: userName,
    Email: email,
    Password: password,
    ConfirmPassword: extra.ConfirmPassword ?? password,
    Nombre: 'Qa',
    Apellido: 'Seguridad',
    RolesSeleccionados: roles,
    Activo: activo ? 'true' : 'false',
    EmailConfirmed: 'true',
    ...extra,
  }, { token });
  let body = null;
  try { body = await res.json(); } catch { body = { raw: (await res.text()).slice(0, 400) }; }
  return { status: res.status(), body };
}

/** Busca el Id de un usuario en la grilla de Seguridad (incluye inactivos). */
async function findUserId(adminPage, userName) {
  await adminPage.goto('/Seguridad/Index?tab=usuarios&mostrarInactivos=true');
  const row = adminPage.locator(`tr.usuario-row[data-name="${userName.toLowerCase()}"]`);
  if ((await row.count()) === 0) return null;
  return row.first().getAttribute('data-id');
}

async function userExists(adminPage, userName) {
  return (await findUserId(adminPage, userName)) !== null;
}

async function createRole(adminPage, nombre, descripcion = 'Rol sintético QA seguridad') {
  const token = await getAntiforgeryToken(adminPage, '/Seguridad/Index?tab=roles');
  const res = await postForm(adminPage, '/Seguridad/CreateRol', { Nombre: nombre, Descripcion: descripcion, Activo: 'true' }, { token });
  let body = null;
  try { body = await res.json(); } catch { body = { raw: (await res.text()).slice(0, 400) }; }
  return { status: res.status(), body };
}

async function findRoleId(adminPage, nombre) {
  await adminPage.goto('/Seguridad/Index?tab=roles');
  const row = adminPage.locator(`tr.rol-row[data-name="${nombre.toLowerCase()}"]`);
  if ((await row.count()) === 0) return null;
  const href = await row.first().locator('a[href*="roleId="]').first().getAttribute('href');
  const m = /roleId=([^&]+)/.exec(href || '');
  return m ? decodeURIComponent(m[1]) : null;
}

/**
 * Lee la matriz de permisos de un rol: { 'modulo.accion': { id, checked } }.
 * La clave de módulo sale del badge ModuloClave y la de acción del title "Clave: x" del chip.
 */
async function readPermissionMatrix(adminPage, roleId) {
  await adminPage.goto(`/Seguridad/Index?tab=permisos-rol&roleId=${encodeURIComponent(roleId)}`);
  await adminPage.locator('#permissionsMatrixForm').waitFor();
  return adminPage.evaluate(() => {
    const out = {};
    document.querySelectorAll('#permissionsMatrixForm [data-search][data-module]').forEach((block) => {
      const badge = block.querySelector('span.rounded-full');
      const modKey = (badge?.textContent || '').trim().toLowerCase();
      block.querySelectorAll('input[name="accionIds"]').forEach((cb) => {
        const title = cb.closest('label')?.getAttribute('title') || '';
        const accKey = title.replace(/^Clave:\s*/i, '').trim().toLowerCase();
        if (modKey && accKey) out[`${modKey}.${accKey}`] = { id: Number(cb.value), checked: cb.checked };
      });
    });
    return out;
  });
}

async function savePermissions(adminPage, roleId, accionIds) {
  const token = await getAntiforgeryToken(adminPage, `/Seguridad/Index?tab=permisos-rol&roleId=${encodeURIComponent(roleId)}`);
  return postForm(adminPage, '/Seguridad/SavePermisosRol', { roleId, accionIds }, { token, ajax: false });
}

async function snap(page, testInfo, name) {
  const path = testInfo.outputPath(`${name}.png`);
  await page.screenshot({ path, fullPage: true });
  await testInfo.attach(name, { path, contentType: 'image/png' });
}

module.exports = {
  RUN_ID, ADMIN_USER, ADMIN_PASS, TEST_PASS,
  uniqueName, newActor, uiLogin, isLoginUrl, getAntiforgeryToken, postForm, probe, classify,
  setDefaultRole, createUser, findUserId, userExists, createRole, findRoleId, readPermissionMatrix, savePermissions, snap,
};
