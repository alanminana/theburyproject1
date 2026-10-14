// QA funcional real: Usuarios, Seguridad y Roles (stack aislado bury-qa-seg, http://127.0.0.1:18091).
// Escribe en la DB aislada: usuarios/roles sintéticos con sufijo RUN_ID único por ejecución.
// Las aserciones expresan la regla de negocio esperada: si la app la incumple, el test FALLA (no se adapta el test).
const { test, expect } = require('@playwright/test');
const h = require('./seg-helpers');

test.skip(process.env.SEG_STACK_READY !== '1',
  'BLOCKED: QA-INFRA no declaró READY el stack bury-qa-seg (exportar SEG_STACK_READY=1 cuando lo haga).');

// Modo default (no serial): un fallo no aborta el resto; dependencias faltantes => skip explícito (BLOCKED).
test.describe.configure({ mode: 'default' });

const S = {
  roleA: h.uniqueName('rolA'),       // rol personalizado: clientes.view (luego se le revoca)
  roleZ: h.uniqueName('rolZ'),       // rol base SIN permisos (la app exige >=1 rol al crear usuario)
  roleB: h.uniqueName('rolB'),       // rol para desactivación/bloqueo: clientes.view
  uMain: h.uniqueName('main'),
  uDup: h.uniqueName('dup'),
  uLock: h.uniqueName('lock'),
  uFail: h.uniqueName('fail'),
  uOff: h.uniqueName('off'),
  roleAId: null, roleBId: null, uMainId: null, uOffId: null, uLockId: null, uFailId: null,
  perm: null,                        // { 'modulo.accion': {id, checked} } del catálogo
  offSession: null,                  // sesión abierta del usuario a desactivar (S13-S15)
  revoked: false,
};
const email = (u) => `${u}@qa-seg.invalid`;

// Rutas restringidas para un rol con solo clientes.view (control: deben existir para admin).
const RESTRICTED = [
  '/Seguridad/Index?tab=usuarios',
  '/Seguridad/Index?tab=roles',
  '/Seguridad/Index?tab=permisos-rol',
  '/Usuarios/Create',
  '/Seguridad/CreateRol',
  '/Caja',
  '/Venta',
  '/Producto',
  '/Diagnostico',
];

let browser, admin;

test.beforeAll(async ({ browser: b }, testInfo) => {
  browser = b;
  admin = await h.newActor(browser, testInfo.project.use.baseURL);
  const url = await h.uiLogin(admin.page, h.ADMIN_USER, h.ADMIN_PASS);
  expect(h.isLoginUrl(url), 'login admin del stack aislado').toBeFalsy();
});

test.afterAll(async () => {
  if (S.offSession) await S.offSession.context.close().catch(() => {});
  if (mainSession) await mainSession.context.close().catch(() => {});
  if (admin) await admin.context.close().catch(() => {});
});

async function actor(testInfo) {
  return h.newActor(browser, testInfo.project.use.baseURL);
}

async function tokenFromAnyPage(page) {
  for (const p of ['/', '/Cliente', '/Identity/Account/AccessDenied']) {
    await page.goto(p).catch(() => {});
    const t = await page.locator('input[name="__RequestVerificationToken"]').first().getAttribute('value').catch(() => null);
    if (t) return t;
  }
  return null;
}

// ───────────────────────── S07 / S08: rol personalizado y persistencia ─────────────────────────

test('S07 crear rol personalizado y asignarle permisos', async ({}, testInfo) => {
  const rz = await h.createRole(admin.page, S.roleZ);
  expect(rz.body.success, JSON.stringify(rz.body)).toBe(true);
  h.setDefaultRole(S.roleZ);
  const r = await h.createRole(admin.page, S.roleA);
  expect(r.status).toBe(200);
  expect(r.body.success, JSON.stringify(r.body)).toBe(true);
  S.roleAId = await h.findRoleId(admin.page, S.roleA);
  expect(S.roleAId, 'rol visible en la grilla').toBeTruthy();

  S.perm = await h.readPermissionMatrix(admin.page, S.roleAId);
  expect(S.perm['clientes.view'], 'acción clientes.view en el catálogo de permisos').toBeTruthy();
  expect(Object.values(S.perm).some((p) => p.checked), 'rol nuevo sin permisos').toBe(false);

  const res = await h.savePermissions(admin.page, S.roleAId, [S.perm['clientes.view'].id]);
  expect([302, 200]).toContain(res.status());

  // Rol B (mismo permiso) para los escenarios de bloqueo/desactivación.
  const rb = await h.createRole(admin.page, S.roleB);
  expect(rb.body.success, JSON.stringify(rb.body)).toBe(true);
  S.roleBId = await h.findRoleId(admin.page, S.roleB);
  await h.savePermissions(admin.page, S.roleBId, [S.perm['clientes.view'].id]);
  await h.snap(admin.page, testInfo, 'S07-matriz-permisos-rolB');
});

test('S08 los cambios de permisos persisten (nueva carga y nueva sesión)', async ({}, testInfo) => {
  test.skip(!S.roleAId, 'BLOCKED: depende de S07');
  // Nueva sesión admin independiente: descarta que sea estado de la página o de TempData.
  const other = await actor(testInfo);
  try {
    await h.uiLogin(other.page, h.ADMIN_USER, h.ADMIN_PASS);
    const m = await h.readPermissionMatrix(other.page, S.roleAId);
    const checked = Object.entries(m).filter(([, v]) => v.checked).map(([k]) => k);
    expect(checked).toEqual(['clientes.view']);
    await h.snap(other.page, testInfo, 'S08-matriz-persistida');
  } finally {
    await other.context.close();
  }
});

// ───────────────────────── S01-S06: alta, asignación, login, acceso y revocación ─────────────────────────

test('S01 crear usuario con datos sintéticos', async ({}, testInfo) => {
  const r = await h.createUser(admin.page, { userName: S.uMain, email: email(S.uMain) });
  expect(r.status).toBe(200);
  expect(r.body.success, JSON.stringify(r.body)).toBe(true);
  S.uMainId = await h.findUserId(admin.page, S.uMain);
  expect(S.uMainId).toBeTruthy();
  const row = admin.page.locator(`tr.usuario-row[data-id="${S.uMainId}"]`);
  await expect(row).toHaveAttribute('data-email', email(S.uMain));
  await row.scrollIntoViewIfNeeded();
  await h.snap(admin.page, testInfo, 'S01-usuario-en-grilla');
});

test('S02 asignar rol al usuario desde la edición real', async ({}, testInfo) => {
  test.skip(!S.uMainId || !S.roleAId, 'BLOCKED: depende de S01/S07');
  await admin.page.goto(`/Seguridad/EditUsuario/${S.uMainId}`);
  await admin.page.locator(`input[name="RolesSeleccionados"][value="${S.roleA}"]`).check();
  await h.snap(admin.page, testInfo, 'S02-edicion-rol-marcado');
  await Promise.all([
    admin.page.waitForURL(/Seguridad/),
    admin.page.locator('form[action*="EditUsuario"] button[type="submit"]').click(),
  ]);
  const row = admin.page.locator(`tr.usuario-row[data-id="${S.uMainId}"]`);
  await admin.page.goto('/Seguridad/Index?tab=usuarios&mostrarInactivos=true');
  await expect(row).toHaveAttribute('data-roles', new RegExp(S.roleA.toLowerCase()));
});

let mainSession = null;

test('S03 el usuario inicia sesión', async ({}, testInfo) => {
  test.skip(!S.uMainId, 'BLOCKED: depende de S01');
  mainSession = await actor(testInfo);
  const url = await h.uiLogin(mainSession.page, S.uMain, h.TEST_PASS);
  expect(h.isLoginUrl(url), `quedó en login: ${url}`).toBeFalsy();
  await h.snap(mainSession.page, testInfo, 'S03-post-login');
});

test('S04 accede a la funcionalidad autorizada (clientes.view)', async ({}, testInfo) => {
  test.skip(!mainSession, 'BLOCKED: depende de S03');
  expect(h.classify(await h.probe(mainSession.page, '/Cliente'))).toBe('allowed');
  await mainSession.page.goto('/Cliente');
  await h.snap(mainSession.page, testInfo, 'S04-cliente-index');
});

test('S05 acceso directo a rutas restringidas es denegado', async ({}, testInfo) => {
  test.skip(!mainSession, 'BLOCKED: depende de S03');
  const results = {};
  for (const p of RESTRICTED) {
    const adminView = h.classify(await h.probe(admin.page, p));
    const userView = h.classify(await h.probe(mainSession.page, p));
    results[p] = { admin: adminView, user: userView };
  }
  await testInfo.attach('S05-matriz-acceso.json', { body: JSON.stringify(results, null, 2), contentType: 'application/json' });
  for (const [p, r] of Object.entries(results)) {
    if (r.admin !== 'allowed') continue; // ruta no válida como control (404/redirect) → no concluye
    expect.soft(r.user, `${p} debería estar denegada para rol solo-clientes`).toBe('denied');
  }
  await mainSession.page.goto('/Seguridad/Index?tab=usuarios');
  await h.snap(mainSession.page, testInfo, 'S05-seguridad-denegado');
});

test('S06 quitar permisos revoca el acceso en el request siguiente (misma sesión)', async ({}, testInfo) => {
  test.skip(!mainSession || !S.roleAId, 'BLOCKED: depende de S03/S07');
  expect(h.classify(await h.probe(mainSession.page, '/Cliente'))).toBe('allowed');
  const res = await h.savePermissions(admin.page, S.roleAId, []);
  expect([302, 200]).toContain(res.status());
  S.revoked = true;
  // Sin re-login: misma cookie de sesión.
  expect(h.classify(await h.probe(mainSession.page, '/Cliente'))).toBe('denied');
  await mainSession.page.goto('/Cliente');
  await h.snap(mainSession.page, testInfo, 'S06-cliente-revocado');
});

// ───────────────────────── S16: POST directo sin permisos ─────────────────────────

test('S16 usuario sin permisos no puede modificar datos mediante POST directo', async ({}, testInfo) => {
  test.skip(!mainSession || !S.revoked, 'BLOCKED: depende de S06 (usuario ya sin permisos)');
  const page = mainSession.page;
  const token = await tokenFromAnyPage(page); // token válido del propio usuario: lo que bloquee debe ser la autorización
  testInfo.annotations.push({ type: 'token', description: token ? 'token antiforgery válido del usuario' : 'sin token disponible' });

  const evil = h.uniqueName('evil');
  const adminId = await h.findUserId(admin.page, h.ADMIN_USER);
  const before = await h.readPermissionMatrix(admin.page, S.roleAId);
  const allIds = Object.values(before).map((p) => p.id);

  const attempts = {
    crearUsuarioSuperAdmin: await h.postForm(page, '/Usuarios/Create', {
      UserName: evil, Email: email(evil), Password: h.TEST_PASS, ConfirmPassword: h.TEST_PASS, RolesSeleccionados: ['SuperAdmin'], Activo: 'true',
    }, { token }),
    autoescalarPermisosRol: await h.postForm(page, '/Seguridad/SavePermisosRol', { roleId: S.roleAId, accionIds: allIds }, { token, ajax: false }),
    crearRol: await h.postForm(page, '/Seguridad/CreateRol', { Nombre: `${evil}_rol`, Activo: 'true' }, { token }),
    desactivarAdmin: await h.postForm(page, '/Usuarios/Desactivar', { id: adminId }, { token, ajax: false }),
    bloquearAdmin: await h.postForm(page, '/Usuarios/Bloquear', { UserId: adminId, MotivoBloqueo: 'qa' }, { token }),
    editarseRolSuperAdmin: await h.postForm(page, '/Seguridad/EditUsuario', { Id: S.uMainId, UserName: S.uMain, Email: email(S.uMain), RolesSeleccionados: ['SuperAdmin'], Activo: 'true', RowVersion: 'AAAAAAAAAAA=' }, { token, ajax: false }),
  };
  const statuses = {};
  for (const [k, r] of Object.entries(attempts)) statuses[k] = h.classify({ status: r.status(), location: r.headers()['location'] || '' });
  await testInfo.attach('S16-respuestas.json', { body: JSON.stringify(statuses, null, 2), contentType: 'application/json' });

  for (const [k, v] of Object.entries(statuses)) expect.soft(v, `${k} debe ser denegado`).toBe('denied');

  // La base no cambió.
  expect.soft(await h.userExists(admin.page, evil), 'no se creó usuario').toBe(false);
  expect.soft(await h.findRoleId(admin.page, `${evil}_rol`), 'no se creó rol').toBeNull();
  const after = await h.readPermissionMatrix(admin.page, S.roleAId);
  expect.soft(Object.values(after).filter((p) => p.checked).length, 'permisos del rol intactos').toBe(0);
  await admin.page.goto('/Seguridad/Index?tab=usuarios&mostrarInactivos=true');
  const adminRow = admin.page.locator(`tr.usuario-row[data-id="${adminId}"]`);
  await expect.soft(adminRow).toHaveAttribute('data-estado', /activ/i);
  await expect.soft(adminRow).not.toHaveAttribute('data-estado', /inactiv|bloq/i);
  const roles = await admin.page.locator(`tr.usuario-row[data-id="${S.uMainId}"]`).getAttribute('data-roles');
  expect.soft(roles || '', 'el usuario no se autoasignó SuperAdmin').not.toMatch(/superadmin/);
});

// ───────────────────────── S09 / S10: duplicados y validaciones ─────────────────────────

test('S09 no se pueden crear usuarios duplicados', async ({}, testInfo) => {
  const ok = await h.createUser(admin.page, { userName: S.uDup, email: email(S.uDup) });
  expect(ok.body.success, JSON.stringify(ok.body)).toBe(true);

  const cases = {
    mismoUsername: await h.createUser(admin.page, { userName: S.uDup, email: `x1_${email(S.uDup)}` }),
    mismoEmail: await h.createUser(admin.page, { userName: `${S.uDup}b`.slice(0, 50), email: email(S.uDup) }),
    usernameMayusculas: await h.createUser(admin.page, { userName: S.uDup.toUpperCase(), email: `x2_${email(S.uDup)}` }),
    emailMayusculas: await h.createUser(admin.page, { userName: `${S.uDup}c`.slice(0, 50), email: email(S.uDup).toUpperCase() }),
  };
  await testInfo.attach('S09-respuestas.json', { body: JSON.stringify(cases, null, 2), contentType: 'application/json' });
  for (const [k, r] of Object.entries(cases)) {
    expect.soft(r.body.success, `${k}: ${JSON.stringify(r.body)}`).toBe(false);
  }
  await admin.page.goto('/Seguridad/Index?tab=usuarios&mostrarInactivos=true');
  const n = await admin.page.locator(`tr.usuario-row[data-email="${email(S.uDup)}"]`).count();
  expect(n, 'una sola fila con ese email').toBe(1);
  expect(await admin.page.locator(`tr.usuario-row[data-name^="${S.uDup.toLowerCase()}"]`).count(), 'sin variantes b/c creadas').toBe(1);
});

test('S10 campos obligatorios y validaciones (servidor + UI)', async ({}, testInfo) => {
  const base = h.uniqueName('val');
  const cases = {
    vacio: await h.createUser(admin.page, { userName: '', email: '', password: '', extra: { ConfirmPassword: '' } }),
    emailInvalido: await h.createUser(admin.page, { userName: `${base}1`, email: 'no-es-email' }),
    passwordCorta: await h.createUser(admin.page, { userName: `${base}2`, email: `2${email(base)}`, password: 'Ab1' }),
    passwordSinDigito: await h.createUser(admin.page, { userName: `${base}3`, email: `3${email(base)}`, password: 'Abcdefgh' }),
    passwordSinMayuscula: await h.createUser(admin.page, { userName: `${base}4`, email: `4${email(base)}`, password: 'abcdef12' }),
    confirmacionDistinta: await h.createUser(admin.page, { userName: `${base}5`, email: `5${email(base)}`, extra: { ConfirmPassword: 'Otra1234X' } }),
    usernameLargo: await h.createUser(admin.page, { userName: `${base}_${'x'.repeat(60)}`, email: `6${email(base)}` }),
    rolInexistente: await h.createUser(admin.page, { userName: `${base}7`, email: `7${email(base)}`, roles: [`no_existe_${h.RUN_ID}`] }),
  };
  const rolVacio = await h.createRole(admin.page, '   ');
  const rolDuplicado = S.roleA ? await h.createRole(admin.page, S.roleA) : null;
  await testInfo.attach('S10-respuestas.json', { body: JSON.stringify({ ...cases, rolVacio, rolDuplicado }, null, 2), contentType: 'application/json' });

  for (const [k, r] of Object.entries(cases)) expect.soft(r.body.success, `${k}: ${JSON.stringify(r.body)}`).toBe(false);
  expect.soft(rolVacio.body.success, 'rol con nombre vacío').toBe(false);
  if (rolDuplicado) expect.soft(rolDuplicado.body.success, 'rol duplicado').toBe(false);
  for (let i = 1; i <= 7; i++) expect.soft(await h.userExists(admin.page, `${base}${i}`), `usuario ${base}${i} no debe existir`).toBe(false);

  // UI: modal real de alta, envío vacío → campos inválidos y sin request de alta.
  await admin.page.goto('/Seguridad/Index?tab=usuarios');
  const posts = [];
  admin.page.on('request', (rq) => { if (rq.method() === 'POST' && /\/Usuarios\/Create/i.test(rq.url())) posts.push(rq.url()); });
  const openBtn = admin.page.locator('[data-action="create-user"], #btnCreateUser, button:has-text("Nuevo usuario"), a:has-text("Nuevo usuario")').first();
  if (await openBtn.count()) {
    await openBtn.click();
    const submit = admin.page.locator('#submitCreateUser');
    await submit.waitFor({ state: 'visible' });
    await submit.click();
    const invalid = await admin.page.locator('#formCreateUser :invalid').count();
    expect.soft(invalid, 'campos obligatorios marcados como inválidos').toBeGreaterThan(0);
    expect.soft(posts.length, 'no se envía el alta vacía').toBe(0);
    await h.snap(admin.page, testInfo, 'S10-modal-alta-vacio');
  } else {
    testInfo.annotations.push({ type: 'no-validado', description: 'No se encontró el botón de alta en la UI; validación UI NO VALIDADA' });
  }
});

// ───────────────────────── S11 / S12: bloqueo manual y lockout por intentos ─────────────────────────

test('S11 bloquear y desbloquear usuario', async ({}, testInfo) => {
  test.skip(!S.roleBId, 'BLOCKED: depende de S07');
  const c = await h.createUser(admin.page, { userName: S.uLock, email: email(S.uLock), roles: [S.roleB] });
  expect(c.body.success, JSON.stringify(c.body)).toBe(true);
  S.uLockId = await h.findUserId(admin.page, S.uLock);

  // Sesión abierta ANTES del bloqueo (observación, no regla confirmada).
  const pre = await actor(testInfo);
  await h.uiLogin(pre.page, S.uLock, h.TEST_PASS);
  expect(h.classify(await h.probe(pre.page, '/Cliente'))).toBe('allowed');

  const token = await h.getAntiforgeryToken(admin.page);
  const b = await h.postForm(admin.page, '/Usuarios/Bloquear', { UserId: S.uLockId, MotivoBloqueo: `QA ${h.RUN_ID}` }, { token });
  expect((await b.json()).success).toBe(true);
  await admin.page.goto('/Seguridad/Index?tab=usuarios&mostrarInactivos=true');
  await expect(admin.page.locator(`tr.usuario-row[data-id="${S.uLockId}"]`)).toHaveAttribute('data-estado', /bloq/i);

  const preAfter = h.classify(await h.probe(pre.page, '/Cliente'));
  testInfo.annotations.push({ type: 'observacion', description: `Sesión previa del usuario bloqueado tras el bloqueo: ${preAfter}` });
  await pre.context.close();

  const blocked = await actor(testInfo);
  const url = await h.uiLogin(blocked.page, S.uLock, h.TEST_PASS);
  expect(url, 'login de usuario bloqueado debe ir a Lockout').toMatch(/Lockout/i);
  await h.snap(blocked.page, testInfo, 'S11-login-bloqueado');
  await blocked.context.close();

  const token2 = await h.getAntiforgeryToken(admin.page);
  const d = await h.postForm(admin.page, '/Usuarios/Desbloquear', { id: S.uLockId }, { token: token2, ajax: false });
  expect([302, 200]).toContain(d.status());

  const again = await actor(testInfo);
  const url2 = await h.uiLogin(again.page, S.uLock, h.TEST_PASS);
  expect(h.isLoginUrl(url2) || /Lockout/i.test(url2), `tras desbloquear: ${url2}`).toBe(false);
  expect(h.classify(await h.probe(again.page, '/Cliente'))).toBe('allowed');
  await again.context.close();
});

test('S12 límite de intentos fallidos (5) bloquea la cuenta', async ({}, testInfo) => {
  test.skip(!S.roleBId, 'BLOCKED: depende de S07');
  const c = await h.createUser(admin.page, { userName: S.uFail, email: email(S.uFail), roles: [S.roleB] });
  expect(c.body.success, JSON.stringify(c.body)).toBe(true);
  S.uFailId = await h.findUserId(admin.page, S.uFail);

  const a = await actor(testInfo);
  try {
    // Borde inferior: 4 fallos + correcta → entra (y resetea el contador).
    for (let i = 0; i < 4; i++) {
      const u = await h.uiLogin(a.page, S.uFail, 'Incorrecta1X');
      expect(h.isLoginUrl(u)).toBe(true);
      await expect(a.page.getByText('Usuario o contraseña incorrectos.')).toBeVisible();
    }
    const ok = await h.uiLogin(a.page, S.uFail, h.TEST_PASS);
    expect(h.isLoginUrl(ok) || /Lockout/i.test(ok), `4 fallos no deben bloquear: ${ok}`).toBe(false);
  } finally { await a.context.close(); }

  const b = await actor(testInfo);
  try {
    // Borde: 5 fallos consecutivos → bloqueo; la contraseña correcta ya no alcanza.
    let last = '';
    for (let i = 0; i < 5; i++) last = await h.uiLogin(b.page, S.uFail, 'Incorrecta1X');
    testInfo.annotations.push({ type: 'quinto-intento', description: last });
    const after = await h.uiLogin(b.page, S.uFail, h.TEST_PASS);
    expect(after, 'con 5 fallos la cuenta queda bloqueada aun con la contraseña correcta').toMatch(/Lockout/i);
    await h.snap(b.page, testInfo, 'S12-lockout');
  } finally { await b.context.close(); }

  await admin.page.goto('/Seguridad/Index?tab=usuarios&mostrarInactivos=true');
  await expect(admin.page.locator(`tr.usuario-row[data-id="${S.uFailId}"]`)).toHaveAttribute('data-estado', /bloq/i);
});

// ───────────────────────── S13-S15: desactivación (REGLA DE NEGOCIO CONFIRMADA) ─────────────────────────

test('S13 desactivar un usuario', async ({}, testInfo) => {
  test.skip(!S.roleBId, 'BLOCKED: depende de S07');
  const c = await h.createUser(admin.page, { userName: S.uOff, email: email(S.uOff), roles: [S.roleB] });
  expect(c.body.success, JSON.stringify(c.body)).toBe(true);
  S.uOffId = await h.findUserId(admin.page, S.uOff);

  // Sesión abierta antes de la desactivación (para S15).
  S.offSession = await actor(testInfo);
  const url = await h.uiLogin(S.offSession.page, S.uOff, h.TEST_PASS);
  expect(h.isLoginUrl(url)).toBe(false);
  expect(h.classify(await h.probe(S.offSession.page, '/Cliente'))).toBe('allowed');
  // Segunda sesión abierta del mismo usuario (otro contexto/navegador): también debe perder el acceso (S15b).
  S.offSession2 = await actor(testInfo);
  expect(h.isLoginUrl(await h.uiLogin(S.offSession2.page, S.uOff, h.TEST_PASS))).toBe(false);
  expect(h.classify(await h.probe(S.offSession2.page, '/Cliente'))).toBe('allowed');

  // El rol Administrador del seed NO tiene usuarios.delete (solo SuperAdmin; verificado en RolesPermisosSeeder).
  // Se usa un operador sintético con rol delegado (usuarios.view + usuarios.delete).
  const roleOps = h.uniqueName('rolOps'), uOps = h.uniqueName('ops');
  const ro = await h.createRole(admin.page, roleOps);
  expect(ro.body.success, JSON.stringify(ro.body)).toBe(true);
  const roleOpsId = await h.findRoleId(admin.page, roleOps);
  const m = await h.readPermissionMatrix(admin.page, roleOpsId);
  expect(m['usuarios.delete'] && m['usuarios.view'], 'acciones usuarios.view/delete en el catálogo').toBeTruthy();
  await h.savePermissions(admin.page, roleOpsId, [m['usuarios.view'].id, m['usuarios.delete'].id]);
  const co = await h.createUser(admin.page, { userName: uOps, email: email(uOps), roles: [roleOps] });
  expect(co.body.success, JSON.stringify(co.body)).toBe(true);
  const ops = await actor(testInfo);
  try {
    const ou = await h.uiLogin(ops.page, uOps, h.TEST_PASS);
    expect(h.isLoginUrl(ou)).toBe(false);
    const token = await tokenFromAnyPage(ops.page);
    expect(token, 'token antiforgery del operador').toBeTruthy();
    const r = await h.postForm(ops.page, '/Usuarios/Desactivar', { id: S.uOffId }, { token, ajax: false });
    const loc = r.headers()['location'] || '';
    await testInfo.attach('S13-respuesta-desactivar.json', { body: JSON.stringify({ status: r.status(), location: loc }, null, 2), contentType: 'application/json' });
    testInfo.annotations.push({ type: 'evidencia', description: `POST /Usuarios/Desactivar (operador con usuarios.delete) => ${r.status()} ${loc}` });
    expect(loc, 'el POST no debe redirigir a login/AccessDenied').not.toMatch(/Login|AccessDenied/i);
    expect([302, 200]).toContain(r.status());
  } finally { await ops.context.close(); }
  await admin.page.goto('/Seguridad/Index?tab=usuarios&mostrarInactivos=true');
  const row = admin.page.locator(`tr.usuario-row[data-id="${S.uOffId}"]`);
  await expect(row).toHaveAttribute('data-estado', /inactiv/i);
  await row.scrollIntoViewIfNeeded();
  await h.snap(admin.page, testInfo, 'S13-usuario-inactivo');
});

// S14 y S15 van juntos con aserciones soft: un fallo en S14 reiniciaría el worker y se perdería la sesión previa de S15.
test('S14+S15 usuario desactivado: no puede iniciar sesión y sus sesiones previas quedan invalidadas', async ({}, testInfo) => {
  test.skip(!S.uOffId || !S.offSession, 'BLOCKED: depende de S13');
  const ev = {};

  // S15 primero (la sesión previa existe desde S13): request siguiente tras desactivar.
  const prev = S.offSession.page;
  ev.S15_sesionPrevia_GET_Cliente = h.classify(await h.probe(prev, '/Cliente'));
  ev.S15_sesionPrevia_GET_Venta = h.classify(await h.probe(prev, '/Venta'));
  await prev.goto('/Cliente');
  await h.snap(prev, testInfo, 'S15-sesion-previa-tras-desactivar');
  ev.S15_sesionPrevia_urlFinal = prev.url();
  // S15b: la segunda sesión abierta también queda invalidada.
  if (S.offSession2) {
    ev.S15b_segundaSesion_GET_Cliente = h.classify(await h.probe(S.offSession2.page, '/Cliente'));
    ev.S15b_segundaSesion_GET_Venta = h.classify(await h.probe(S.offSession2.page, '/Venta'));
    await S.offSession2.context.close().catch(() => {});
  }

  // S14: login nuevo del usuario desactivado.
  const a = await actor(testInfo);
  try {
    ev.S14_urlPostLogin = await h.uiLogin(a.page, S.uOff, h.TEST_PASS);
    await h.snap(a.page, testInfo, 'S14-login-desactivado');
    ev.S14_GET_Cliente = h.classify(await h.probe(a.page, '/Cliente'));
    ev.S14_mensajeEnLogin = (await a.page.locator('.text-red-500, .validation-summary-errors, [role="alert"]').allInnerTexts().catch(() => [])).join(' | ').slice(0, 200);
  } finally { await a.context.close(); }

  await testInfo.attach('S14-S15-evidencia.json', { body: JSON.stringify(ev, null, 2), contentType: 'application/json' });
  testInfo.annotations.push({ type: 'evidencia', description: JSON.stringify(ev) });

  expect.soft(ev.S14_GET_Cliente, 'REGLA S14: un usuario desactivado no puede acceder tras iniciar sesión').not.toBe('allowed');
  expect.soft(h.isLoginUrl(ev.S14_urlPostLogin) || /Lockout/i.test(ev.S14_urlPostLogin),
    `REGLA S14: no debe quedar autenticado (url ${ev.S14_urlPostLogin})`).toBe(true);
  expect.soft(ev.S15_sesionPrevia_GET_Cliente, 'REGLA S15: la sesión previa del usuario desactivado debe quedar invalidada').not.toBe('allowed');
  expect.soft(ev.S15b_segundaSesion_GET_Cliente, 'REGLA S15b: la segunda sesión abierta también debe quedar invalidada').not.toBe('allowed');
});

// ───────────────────────── S17: antiforgery ─────────────────────────

test('S17 antiforgery: POST sin token / token ajeno / token alterado se rechaza y no modifica la base', async ({}, testInfo) => {
  const n1 = h.uniqueName('csrf1'), n2 = h.uniqueName('csrf2'), n3 = h.uniqueName('csrf3');
  const fields = (n) => ({ UserName: n, Email: email(n), Password: h.TEST_PASS, ConfirmPassword: h.TEST_PASS, Activo: 'true' });

  // Token emitido para OTRA identidad (un usuario distinto, sesión propia de esta suite).
  let foreignToken = null;
  if (S.uLockId) {
    const other = await actor(testInfo);
    await h.uiLogin(other.page, S.uLock, h.TEST_PASS);
    foreignToken = await tokenFromAnyPage(other.page);
    await other.context.close();
  }
  const valid = await h.getAntiforgeryToken(admin.page);
  const tampered = valid.slice(0, -6) + (valid.endsWith('AAAAAA') ? 'BBBBBB' : 'AAAAAA');

  const results = {
    sinToken: (await h.postForm(admin.page, '/Usuarios/Create', fields(n1))).status(),
    tokenAlterado: (await h.postForm(admin.page, '/Usuarios/Create', fields(n2), { token: tampered })).status(),
    tokenDeOtraIdentidad: foreignToken ? (await h.postForm(admin.page, '/Usuarios/Create', fields(n3), { token: foreignToken })).status() : 'NO VALIDADO',
  };
  let matrixBefore = null;
  if (S.roleBId) {
    matrixBefore = await h.readPermissionMatrix(admin.page, S.roleBId);
    results.savePermisosSinToken = (await h.postForm(admin.page, '/Seguridad/SavePermisosRol',
      { roleId: S.roleBId, accionIds: Object.values(matrixBefore).map((p) => p.id) }, { ajax: false })).status();
  }
  results.createRolSinToken = (await h.postForm(admin.page, '/Seguridad/CreateRol', { Nombre: `${n1}_rol`, Activo: 'true' })).status();
  await testInfo.attach('S17-status.json', { body: JSON.stringify(results, null, 2), contentType: 'application/json' });

  for (const [k, v] of Object.entries(results)) if (v !== 'NO VALIDADO') expect.soft(v, `${k} debe responder 400`).toBe(400);
  for (const n of [n1, n2, n3]) expect.soft(await h.userExists(admin.page, n), `usuario ${n} no debe existir`).toBe(false);
  expect.soft(await h.findRoleId(admin.page, `${n1}_rol`), 'rol no creado').toBeNull();
  if (matrixBefore) {
    const after = await h.readPermissionMatrix(admin.page, S.roleBId);
    const on = (m) => Object.entries(m).filter(([, v]) => v.checked).map(([k]) => k).sort();
    expect.soft(on(after), 'permisos de rolB sin cambios').toEqual(on(matrixBefore));
  }
  // Control positivo: con token válido el mismo POST sí funciona (descarta falso verde por otro motivo).
  const ctrl = h.uniqueName('csrfok');
  const ok = await h.createUser(admin.page, { userName: ctrl, email: email(ctrl) });
  expect(ok.body.success, `control positivo: ${JSON.stringify(ok.body)}`).toBe(true);
});
