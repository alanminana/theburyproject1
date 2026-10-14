// @ts-check
const { test, expect, evidence } = require('./lib/fx');
const db = require('./lib/db');
const { mk, ajuste } = require('./lib/ops');
const { AUTH } = require('./lib/http');
const { mem } = require('./lib/mem');

async function pickAc(page, base, texto) {
    const input = page.locator(`#ac-${base}-input`);
    await input.click();
    await input.fill(texto);
    const dd = page.locator(`#ac-${base}-dropdown`);
    await expect(dd).toBeVisible();
    await dd.getByText(texto, { exact: false }).first().click();
}

test.describe('UI Catalogo (flujos reales de modales)', () => {
    test('U01 crear producto desde el modal del catalogo', async ({ adminPage: page, st }) => {
        const code = `${st.prefix}-U01`;
        const cat = db.scalar(`SELECT Nombre FROM Categorias WHERE Id=${st.categoriaId}`);
        const marca = db.scalar(`SELECT Nombre FROM Marcas WHERE Id=${st.marcaId}`);
        await page.goto('/Catalogo');
        await page.locator('#btn-crear-producto').click();
        const modal = page.locator('#modal-nuevo-producto');
        await expect(modal).toBeVisible();
        const form = page.locator('#form-nuevo-producto');
        await form.locator('input[name="Codigo"]').fill(code);
        await form.locator('input[name="Nombre"]').fill(`${code} UI`);
        await pickAc(page, 'categoria', cat);
        await pickAc(page, 'marca', marca);
        await page.locator('[data-prod-create-tab="precios"]').click();
        await page.locator('#modal-precioCompra').fill('1000');
        const margenUi = page.locator('#modal-margenPorcentaje');
        await page.locator('#modal-precioFinal').fill('1500');
        await page.locator('#modal-precioFinal').blur();
        const ui = { margen: await margenUi.inputValue().catch(() => null), ganancia: await page.locator('#modal-montoGanancia').inputValue().catch(() => null) };
        await page.locator('[data-prod-create-tab="stock"]').click();
        await form.locator('input[name="StockActual"]').fill('8');
        await page.screenshot({ path: `e2e-tx/productos/evidence/U01-modal.png` });
        await page.locator('#btn-guardar-producto').click();
        await expect.poll(() => db.countProductos(code), { timeout: 15000 }).toBe(1);
        const p = db.producto(code);
        evidence('U01', { ui, db: p, movs: db.movimientos(p.id) });
        expect(p.stock).toBe(8);
        expect(p.precioVenta).toBeGreaterThan(0); // el modal envia costo c/IVA: PrecioCompra neto se registra en evidencia
        expect(db.movimientos(p.id)).toHaveLength(1);
        // fila visible en la tabla tras crear
        await page.goto(`/Catalogo?searchTerm=${code}`);
        await expect(page.locator(`tr[data-producto-codigo="${code}"]`)).toHaveCount(1);
    });

    test('U02 editar desde el modal: precio, margen mostrado vs servidor, persistencia', async ({ adminPage: page, admin, st }) => {
        const code = `${st.prefix}-U02`;
        const p = await mk(admin, st, code, { PrecioCompra: '1000', PrecioVenta: '1500', StockActual: '3' });
        await page.goto(`/Catalogo?searchTerm=${code}`);
        await page.locator(`[data-prod-edit-id="${p.id}"]`).first().click();
        await expect(page.locator('#modal-editar-producto')).toBeVisible();
        await expect(page.locator('#prod-edit-codigo')).toHaveValue(code);
        await page.locator('[data-prod-edit-tab="stock"]').click();
        await expect(page.locator('#prod-edit-stockActual')).toHaveValue('3');
        await expect(page.locator('#prod-edit-stockActual')).toHaveAttribute('readonly', '');
        await page.locator('[data-prod-edit-tab="datos"]').click();
        await page.locator('#prod-edit-nombre').fill(`${code} Editado UI`);
        await page.locator('[data-prod-edit-tab="precios"]').click();
        await page.locator('#prod-edit-precioFinal').fill('1600');
        await page.locator('#prod-edit-precioFinal').blur();
        const margenUi = await page.locator('#prod-edit-margenPorcentaje').inputValue();
        await page.screenshot({ path: 'e2e-tx/productos/evidence/U02-modal.png' });
        await page.locator('#btn-guardar-producto-edit').click();
        await expect.poll(() => db.producto(code).nombre, { timeout: 15000 }).toContain('Editado UI');
        const d = db.producto(code);
        evidence('U02', { margenUi, db: d });
        expect(d.stock).toBe(3);
        expect(d.precioVenta).toBe(1600);
        expect(d.precioCompra).toBe(1000);
        // el margen que muestra el modal debe coincidir con el que devuelve el servidor (60)
        expect.soft(parseFloat(margenUi.replace(',', '.')), `margen en modal=${margenUi} vs servidor=60`).toBeCloseTo(60, 1);
        // la fila refleja el precio nuevo
        await page.goto(`/Catalogo?searchTerm=${code}`);
        const precioFila = await page.locator(`tr[data-producto-codigo="${code}"]`).getAttribute('data-sort-precio');
        expect(Number(precioFila)).toBe(1600);
    });

    test('U03 catalogo muestra precio y stock actualizados tras cambio de precio y movimientos', async ({ adminPage: page, admin, st }) => {
        const code = `${st.prefix}-U03`;
        const p = await mk(admin, st, code, { PrecioCompra: '1000', PrecioVenta: '2000', StockActual: '6', StockMinimo: '2' });
        await admin.json('/Catalogo/AplicarCambioPrecioDirecto', { alcance: 'seleccionados', valorPorcentaje: 10, productoIdsText: String(p.id), tipoCambio: 'porcentaje', redondeo: 'none', motivo: 'U03' });
        await ajuste(admin, p.id, 1, 5, { Motivo: 'U03' }); // stock 1 (<= minimo => bajo)
        await page.goto(`/Catalogo?searchTerm=${code}`);
        const row = page.locator(`tr[data-producto-codigo="${code}"]`);
        await expect(row).toHaveCount(1);
        expect(Number(await row.getAttribute('data-sort-precio'))).toBe(2200);
        const stockTxt = (await row.locator('[data-prod-stock]').innerText()).trim();
        evidence('U03', { stockTxt, db: db.producto(code) });
        expect(stockTxt).toMatch(/1/);
        expect(db.producto(code).stock).toBe(1);
        // Kardex muestra los movimientos
        await page.goto(`/MovimientoStock/Kardex/${p.id}`);
        await expect(page.getByText('Stock inicial').first()).toBeVisible();
        await page.screenshot({ path: 'e2e-tx/productos/evidence/U03-kardex.png' });
    });

    test('U04 UI con solo lectura: sin acciones de escritura visibles', async ({ browser }) => {
        const ctx = await browser.newContext({ baseURL: 'http://127.0.0.1:18092', storageState: AUTH('qaprd_lector'), viewport: { width: 1440, height: 900 } });
        const page = await ctx.newPage();
        await page.route(/fonts\.(googleapis|gstatic)\.com/, r => r.abort());
        await page.goto('/Catalogo');
        const res = {
            crear: await page.locator('#btn-crear-producto').count() && await page.locator('#btn-crear-producto').isVisible(),
            ajusteMasivo: await page.locator('#btn-ajuste-masivo').isVisible().catch(() => false),
            editar: await page.locator('[data-prod-edit-id]:visible').count(),
            eliminar: await page.locator('#catalogo-menu-eliminar:visible').count(),
        };
        await page.screenshot({ path: 'e2e-tx/productos/evidence/U04-lector.png' });
        evidence('U04', res);
        expect.soft(res.crear, 'lector ve boton Crear producto').toBeFalsy();
        expect.soft(res.ajusteMasivo, 'lector ve Ajuste Masivo de precios').toBeFalsy();
        expect.soft(res.editar, 'lector ve botones Editar').toBe(0);
        await ctx.close();
    });
});
