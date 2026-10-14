// @ts-check
const { chromium } = require('playwright/test');
const { assertAuthorized, writeState } = require('./lib/env');
const db = require('./lib/db');
const { AUTH, loginUi, client } = require('./lib/http');

const PASS = 'QaPrd#2026x';

require('./lib/mem').reset();
module.exports = async () => {
    assertAuthorized();
    const dbName = db.scalar('SELECT DB_NAME()');
    if (dbName !== 'BuryQA_PRD') throw new Error(`DB inesperada: ${dbName}`);

    const run = Date.now().toString(36).toUpperCase();
    const st = { run, prefix: `QAPRD${run}`, pass: PASS, startedAt: new Date().toISOString() };

    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    await loginUi(browser, 'administrador', 'Admin123!', AUTH('admin'));

    st.categoriaId = db.num('SELECT TOP 1 Id FROM Categorias WHERE IsDeleted=0 AND ParentId IS NULL ORDER BY Id');
    st.marcaId = db.num('SELECT TOP 1 Id FROM Marcas WHERE IsDeleted=0 AND ParentId IS NULL ORDER BY Id');
    st.listaId = db.num('SELECT TOP 1 Id FROM ListasPrecios WHERE IsDeleted=0 AND Activa=1 ORDER BY Id');
    st.seed = db.rows("SELECT Id,Codigo FROM Productos WHERE Codigo LIKE 'E2EGENERIC-%' AND IsDeleted=0 ORDER BY Id");

    const ctx = await browser.newContext({ baseURL: 'http://127.0.0.1:18092', storageState: AUTH('admin') });
    const api = client(ctx.request);
    const roles = {
        QA_PRD_LECTOR: ['productos.view', 'movimientos.view', 'precios.view'],
        QA_PRD_OPER: ['productos.view', 'productos.create', 'productos.update', 'movimientos.view', 'movimientos.create', 'precios.view', 'precios.simulate'],
    };
    for (const [rol, perms] of Object.entries(roles)) {
        let id = db.scalar(`SELECT Id FROM AspNetRoles WHERE Name='${rol}'`);
        if (!id) {
            await api.form('/Seguridad/CreateRol', { Nombre: rol, Descripcion: 'QA-TX permisos', Activo: 'true' });
            id = db.scalar(`SELECT Id FROM AspNetRoles WHERE Name='${rol}'`);
        }
        if (!id) throw new Error(`No se pudo crear rol ${rol}`);
        const ids = perms.map(p => {
            const [m, a] = p.split('.');
            const v = db.num(`SELECT a.Id FROM AccionesModulo a JOIN ModulosSistema m ON m.Id=a.ModuloId WHERE m.Clave='${m}' AND a.Clave='${a}'`);
            if (!v) throw new Error(`Permiso inexistente ${p}`);
            return v;
        });
        const t = await api.token();
        const body = new URLSearchParams();
        body.append('roleId', id);
        body.append('__RequestVerificationToken', t);
        ids.forEach(i => body.append('accionIds', String(i)));
        const r = await ctx.request.post('/Seguridad/SavePermisosRol', {
            data: body.toString(),
            headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'X-Requested-With': 'XMLHttpRequest' },
            maxRedirects: 0,
        });
        if (r.status() >= 400) throw new Error(`SavePermisosRol ${rol} -> ${r.status()}`);
    }
    for (const [u, rol] of [['qaprd_lector', 'QA_PRD_LECTOR'], ['qaprd_oper', 'QA_PRD_OPER']]) {
        if (!db.scalar(`SELECT Id FROM AspNetUsers WHERE UserName='${u}'`)) {
            const r = await api.form('/Usuarios/Create', {
                UserName: u, Email: `${u}@qa.local`, Password: PASS, ConfirmPassword: PASS,
                Nombre: 'Qa', Apellido: 'Prd', EmailConfirmed: 'true', Activo: 'true', RolesSeleccionados: rol,
            });
            const j = await r.json().catch(() => ({}));
            if (!j.success) throw new Error(`Crear usuario ${u}: ${JSON.stringify(j)}`);
        }
        await loginUi(browser, u, PASS, AUTH(u));
    }
    const permsOf = rol => db.rows(`SELECT m.Clave+'.'+a.Clave FROM RolPermisos rp JOIN AccionesModulo a ON a.Id=rp.AccionId JOIN ModulosSistema m ON m.Id=rp.ModuloId JOIN AspNetRoles r ON r.Id=rp.RoleId WHERE r.Name='${rol}' AND rp.IsDeleted=0`).map(r => r[0]);
    st.permisosLector = permsOf('QA_PRD_LECTOR');
    st.permisosOper = permsOf('QA_PRD_OPER');
    await ctx.close();
    await browser.close();
    writeState(st);
    console.log('[QA-TX] setup OK', JSON.stringify({ run, cat: st.categoriaId, marca: st.marcaId, lista: st.listaId }));
};
