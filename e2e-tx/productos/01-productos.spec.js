// @ts-check
const { test, expect, evidence } = require('./lib/fx');
const db = require('./lib/db');
const { productoForm } = require('./lib/http');
const { mem } = require('./lib/mem');

const fmt = n => String(n).replace('.', ',');

/** Arma el form de EditAjax a partir de GetJson (+ overrides). */
async function editForm(admin, id, over = {}) {
    const j = await (await admin.get(`/Producto/GetJson/${id}`)).json();
    return {
        Id: String(j.id), RowVersion: j.rowVersion, Codigo: j.codigo, Nombre: j.nombre, Descripcion: j.descripcion ?? '',
        CategoriaId: String(j.categoriaId), MarcaId: String(j.marcaId),
        PrecioCompra: fmt(j.precioCompra), PrecioVenta: fmt(j.precioVenta), PorcentajeIVA: fmt(j.porcentajeIVA),
        CostoEnvio: fmt(j.costoEnvio), PercepcionesCompra: fmt(j.percepcionesCompra), OtrosCostosCompra: fmt(j.otrosCostosCompra),
        ComisionPorcentaje: fmt(j.comisionPorcentaje), StockActual: fmt(j.stockActual), StockMinimo: fmt(j.stockMinimo),
        Activo: String(j.activo), ...over,
    };
}

test.describe('PRODUCTOS', () => {

    test('P01 crear producto con codigo unico', async ({ admin, st }) => {
        mem.A = `${st.prefix}-P01`;
        const antes = { existe: db.countProductos(mem.A) };
        const r = await admin.form('/Producto/CreateAjax', productoForm(st, { Codigo: mem.A, Nombre: `${mem.A} Original`, PrecioCompra: '1000', PrecioVenta: '1500', StockMinimo: '2' }));
        const j = await r.json();
        expect(j.success, JSON.stringify(j)).toBe(true);
        const p = db.producto(mem.A);
        evidence('P01', { antes, resp: j, db: p });
        expect(p).not.toBeNull();
        mem.idA = p.id;
        expect(p).toMatchObject({ codigo: mem.A, precioCompra: 1000, precioVenta: 1500, stock: 0, stockMin: 2, activo: true, isDeleted: false });
        expect(db.countProductos(mem.A)).toBe(1);
        expect(db.movimientos(mem.idA)).toHaveLength(0); // stock 0 => sin movimiento inicial
    });

    test('P02 persistencia: GetJson y busqueda de catalogo', async ({ admin }) => {
        const j = await (await admin.get(`/Producto/GetJson/${mem.idA}`)).json();
        expect(j).toMatchObject({ codigo: mem.A, nombre: `${mem.A} Original`, precioVenta: 1500, precioCompra: 1000 });
        const html = await (await admin.get(`/Catalogo?searchTerm=${encodeURIComponent(mem.A)}`)).text();
        evidence('P02', { getJson: j, catalogoContieneCodigo: html.includes(mem.A) });
        expect(html).toContain(mem.A);
    });

    test('P03 editar nombre, costo, precio y atributos', async ({ admin }) => {
        const antes = db.producto(mem.A);
        const f = await editForm(admin, mem.idA, { Nombre: `${mem.A} Editado`, Descripcion: 'desc editada', PrecioCompra: '1200', PrecioVenta: '1800', StockMinimo: '5' });
        const j = await (await admin.form(`/Producto/EditAjax/${mem.idA}`, f)).json();
        expect(j.success, JSON.stringify(j)).toBe(true);
        const despues = db.producto(mem.A);
        evidence('P03', { antes, despues, resp: j });
        expect(despues).toMatchObject({ nombre: `${mem.A} Editado`, precioCompra: 1200, precioVenta: 1800, stockMin: 5 });
        // Historial de precios del Edit (PreciosHistoricos)
        const h = db.rows(`SELECT PrecioCompraAnterior,PrecioCompraNuevo,PrecioVentaAnterior,PrecioVentaNuevo FROM PreciosHistoricos WHERE ProductoId=${mem.idA} ORDER BY Id`);
        expect(h.length).toBe(1);
        expect(h[0].map(Number)).toEqual([1000, 1200, 1500, 1800]);
    });

    test('P03b edicion NO puede alterar StockActual (solo via movimientos)', async ({ admin }) => {
        const f = await editForm(admin, mem.idA, { StockActual: '999' });
        await admin.form(`/Producto/EditAjax/${mem.idA}`, f);
        expect(db.producto(mem.A).stock).toBe(0);
        expect(db.movimientos(mem.idA)).toHaveLength(0);
    });

    test('P03c RowVersion obsoleto es rechazado (concurrencia)', async ({ admin }) => {
        const stale = await editForm(admin, mem.idA);
        // otra edicion legitima cambia el RowVersion
        const f2 = await editForm(admin, mem.idA, { Descripcion: 'cambio concurrente' });
        expect((await (await admin.form(`/Producto/EditAjax/${mem.idA}`, f2)).json()).success).toBe(true);
        const r = await (await admin.form(`/Producto/EditAjax/${mem.idA}`, { ...stale, Nombre: `${mem.A} STALE` })).json();
        evidence('P03c', { resp: r, nombreFinal: db.producto(mem.A).nombre });
        expect(r.success).toBe(false);
        expect(db.producto(mem.A).nombre).not.toContain('STALE');
    });

    test('P04 codigos duplicados (exacto, otra capitalizacion, con espacios)', async ({ admin, st }) => {
        const variantes = [mem.A, mem.A.toLowerCase(), `  ${mem.A}  `];
        const res = [];
        for (const c of variantes) {
            const j = await (await admin.form('/Producto/CreateAjax', productoForm(st, { Codigo: c, Nombre: 'dup' }))).json();
            res.push({ codigo: c, success: j.success, errors: j.errors });
            expect.soft(j.success, `duplicado aceptado: '${c}'`).toBe(false);
        }
        const total = db.num(`SELECT COUNT(*) FROM Productos WHERE UPPER(LTRIM(RTRIM(Codigo)))=UPPER('${mem.A}') AND IsDeleted=0`);
        evidence('P04', { res, filasConEseCodigo: total });
        expect(total).toBe(1);
        // edicion hacia un codigo existente (otro producto seed)
        const seedCode = st.seed[0][1];
        const f = await editForm(admin, mem.idA, { Codigo: seedCode });
        const j = await (await admin.form(`/Producto/EditAjax/${mem.idA}`, f)).json();
        expect(j.success).toBe(false);
        expect(db.producto(mem.A)).not.toBeNull();
    });

    test('P05 campos obligatorios y valores invalidos', async ({ admin, st }) => {
        const casos = [
            ['sin codigo', { Codigo: '' }],
            ['codigo solo espacios', { Codigo: '   ' }],
            ['sin nombre', { Nombre: '' }],
            ['sin categoria', { CategoriaId: '0' }],
            ['sin marca', { MarcaId: '0' }],
            ['costo negativo', { PrecioCompra: '-1' }],
            ['venta negativa', { PrecioVenta: '-5' }],
            ['venta > 20M', { PrecioVenta: '20000001' }],
            ['venta 3 decimales', { PrecioVenta: '10,123' }],
            ['venta texto', { PrecioVenta: 'abc' }],
            ['IVA 101', { PorcentajeIVA: '101' }],
            ['IVA negativo', { PorcentajeIVA: '-1' }],
            ['stock negativo', { StockActual: '-3' }],
            ['stock minimo negativo', { StockMinimo: '-1' }],
            ['codigo 51 chars', { Codigo: 'X'.repeat(51) }],
            ['nombre 201 chars', { Nombre: 'N'.repeat(201) }],
            ['percepciones negativas', { PercepcionesCompra: '-1' }],
            ['comision 101', { ComisionPorcentaje: '101' }],
        ];
        const out = [];
        for (const [nombre, over] of casos) {
            const base = { Codigo: `${st.prefix}-P05-${out.length}`, Nombre: `${st.prefix} inv` };
            const f = productoForm(st, { ...base, ...over });
            const r = await admin.form('/Producto/CreateAjax', f);
            const j = await r.json().catch(() => ({ success: null, status: r.status() }));
            const creado = db.countProductos(f.Codigo) > 0 || (over.Codigo !== undefined && db.num(`SELECT COUNT(*) FROM Productos WHERE Nombre='${db.esc(f.Nombre)}' AND IsDeleted=0`) > 0);
            out.push({ caso: nombre, success: j.success, creado, errors: j.errors });
            expect.soft(j.success === true || creado, `valor invalido aceptado: ${nombre}`).toBe(false);
        }
        evidence('P05', out);
    });

    test('P06a permisos: lector (solo ver) no crea, edita ni elimina', async ({ lector, admin, st }) => {
        expect((await lector.get('/Catalogo')).status()).toBe(200);
        expect((await lector.get(`/Producto/GetJson/${mem.idA}`)).status()).toBe(200);
        const code = `${st.prefix}-P06L`;
        const antes = db.producto(mem.A);
        const c = await lector.form('/Producto/CreateAjax', productoForm(st, { Codigo: code, Nombre: 'lector' }));
        const f = await editForm(admin, mem.idA, { Nombre: `${mem.A} HACK-LECTOR`, PrecioVenta: '1' });
        const e = await lector.form(`/Producto/EditAjax/${mem.idA}`, f);
        const d = await lector.form(`/Producto/Delete/${mem.idA}`, {});
        const despues = db.producto(mem.A);
        evidence('P06a', { statuses: { create: c.status(), edit: e.status(), delete: d.status() }, antes, despues, creado: db.countProductos(code) });
        for (const r of [c, e, d]) expect([302, 401, 403]).toContain(r.status());
        expect(db.countProductos(code)).toBe(0);
        expect(despues).toEqual(antes);
    });

    test('P06b permisos: operador (create/update, sin delete) crea y edita pero no elimina', async ({ oper, admin, st }) => {
        const code = `${st.prefix}-P06O`;
        const c = await (await oper.form('/Producto/CreateAjax', productoForm(st, { Codigo: code, Nombre: `${code} oper` }))).json();
        expect(c.success, JSON.stringify(c)).toBe(true);
        const id = db.producto(code).id;
        const f = await editForm(admin, id, { Nombre: `${code} oper edit` });
        const e = await (await oper.form(`/Producto/EditAjax/${id}`, f)).json();
        expect(e.success, JSON.stringify(e)).toBe(true);
        const d = await oper.form(`/Producto/Delete/${id}`, {});
        evidence('P06b', { delete: d.status(), db: db.producto(code) });
        expect([302, 401, 403]).toContain(d.status());
        if (d.status() === 302) expect((d.headers()['location'] || '').toLowerCase()).toMatch(/denied|login|forbid|acceso/);
        expect(db.producto(code).isDeleted).toBe(false);
    });

    test('P06c anonimo no accede', async ({ playwright }) => {
        const ctx = await playwright.request.newContext({ baseURL: 'http://127.0.0.1:18092' });
        const r = await ctx.get('/Producto/GetJson/1', { maxRedirects: 0 });
        expect([302, 401, 403]).toContain(r.status());
        await ctx.dispose();
    });
});
