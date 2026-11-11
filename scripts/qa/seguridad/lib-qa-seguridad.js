'use strict';
// Utilidades compartidas por los sondeos de seguridad (solo contra un stack QA local aislado, nunca TheBuryProjectDb real).
const { execFileSync } = require('child_process');

const BASE = process.env.E2E_BASE_URL || 'http://127.0.0.1:18080';
if (!/^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/.test(BASE)) { console.error('Solo stack local aislado.'); process.exit(2); }

const CONTENEDOR = process.env.QA_SQL_CONTAINER || '';
const SQL_SA_PASSWORD = process.env.QA_SQL_PASSWORD || '';
const BD = process.env.QA_SQL_DATABASE || 'TheBuryProjectDb';
function exigirSql() {
  if (!CONTENEDOR || !SQL_SA_PASSWORD) { console.error('Faltan QA_SQL_CONTAINER y QA_SQL_PASSWORD (sa del SQL Server del stack QA).'); process.exit(2); }
  if (/real|prod/i.test(BD) && !/TheBuryProjectDb/.test(BD)) { console.error('Base no permitida.'); process.exit(2); }
}

// Ejecuta sqlcmd dentro del contenedor del stack QA. Devuelve las filas como arreglos de columnas.
function sql(consulta) {
  exigirSql();
  const salida = execFileSync('docker', ['exec', '-e', `SQLCMDPASSWORD=${SQL_SA_PASSWORD}`, CONTENEDOR, '/opt/mssql-tools18/bin/sqlcmd',
    '-C', '-I', '-S', 'localhost', '-U', 'sa', '-d', BD, '-W', '-h', '-1', '-s', '|', '-Q', `SET NOCOUNT ON; ${consulta}`],
  { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  return salida.split(/\r?\n/).filter((l) => l.trim() !== '').map((l) => l.split('|').map((c) => c.trim()));
}
function sqlEscalar(consulta, def = null) { try { const f = sql(consulta); return f.length ? f[0][0] : def; } catch { return def; } }

// Huella de TODAS las tablas: filas + CHECKSUM_AGG(BINARY_CHECKSUM(*)). Una tabla ilegible para el checksum cae a solo el conteo (-1 en checksum).
function huella() {
  const t = "DECLARE @s nvarchar(max)=N''; SELECT @s += N'BEGIN TRY SELECT ''' + t.name + ''', COUNT_BIG(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' END TRY BEGIN CATCH SELECT ''' + t.name + ''', -1, NULL END CATCH;' FROM sys.tables t; EXEC sp_executesql @s;";
  const h = {};
  for (const f of sql(t)) if (f.length >= 3) h[f[0]] = `${f[1]}:${f[2]}`;
  if (Object.keys(h).length < 50) throw new Error(`Huella invalida: solo ${Object.keys(h).length} tablas.`);
  return h;
}
function diferencias(a, b) {
  return [...new Set([...Object.keys(a), ...Object.keys(b)])].filter((k) => a[k] !== b[k])
    .map((k) => `${k} (${(a[k] || '').split(':')[0]}->${(b[k] || '').split(':')[0]} filas)`).sort();
}

async function login(ctx, user, pass) {
  const page = await ctx.newPage();
  await page.goto(`${BASE}/Identity/Account/Login`);
  await page.fill('#Input_UserName, input[name="Input.UserName"]', user);
  await page.fill('#input-password, input[name="Input.Password"]', pass);
  await page.click('button[type="submit"]');
  const nombre = page.locator('input[placeholder="Nombre y apellido"]');
  if (await nombre.isVisible({ timeout: 5000 }).catch(() => false)) {
    await nombre.fill(`Sondeo ${user}`);
    await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
    await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
  }
  await page.waitForURL((u) => !u.toString().toLowerCase().includes('/login'), { timeout: 20000 });
  return page;
}

// Token antiforgery de la sesion: meta csrf-token del layout o input oculto de cualquier formulario.
async function tokenDe(request, rutas = ['/', '/Home/Privacy', '/Identity/Account/Login']) {
  for (const r of rutas) {
    const resp = await request.get(BASE + r, { maxRedirects: 0 });
    if (resp.status() !== 200) continue;
    const html = await resp.text();
    const m = html.match(/name="csrf-token"[^>]*content="([^"]+)"/) || html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);
    if (m) return m[1].replace(/&#x2B;/g, '+').replace(/&amp;/g, '&');
  }
  return null;
}

const ALIAS = [['view'], ['create'], ['update', 'edit'], ['delete'], ['approve', 'authorize'], ['export'], ['print'], ['duplicate'],
  ['assignpermissions', 'assignroles', 'assign'], ['revoke'], ['block'], ['resetpassword', 'resetpass']];
function coincide(claim, permiso) { // permiso = "modulo.accion"
  const [mod, acc] = permiso.split('.');
  const g = ALIAS.find((x) => x.includes(acc.toLowerCase())) || [acc.toLowerCase()];
  return g.some((a) => `${mod.toLowerCase()}.${a}` === claim.trim().toLowerCase());
}
// Misma regla que el filtro [PermisoRequerido]/[Authorize(Roles)]: SuperAdmin pasa todo [PermisoRequerido].
function debeDenegar(a, claims, roles) {
  const rolesNorm = roles.map((r) => r.toLowerCase());
  if (a.rolesRequeridos.some((req) => !req.some((r) => rolesNorm.includes(r.toLowerCase())))) return true;
  if (rolesNorm.includes('superadmin')) return false;
  return a.permisos.some((p) => !claims.some((c) => coincide(c, p)));
}

module.exports = { BASE, sql, sqlEscalar, huella, diferencias, login, tokenDe, debeDenegar, exigirSql };
