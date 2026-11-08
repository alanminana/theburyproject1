# Validación sintética de handlers (D-05)

Pruebas de navegador (Playwright) de las acciones `data-*` de `wwwroot/js/csp-actions.js` sobre pantallas que **necesitan datos**
(Unidades, DocumentoCliente, Devolución, Comisiones, Mercado Libre) con la CSP en `ReportOnly`, contando violaciones.

**Solo contra un clon de QA, nunca `TheBuryProjectDb`** (la siembra escribe filas ficticias; las pruebas de ML usan una cuenta con tokens falsos y solo
ejecutan diálogos cancelados: no deben generar POST).

1. Clon: `BACKUP DATABASE TheBuryProjectDb ... COPY_ONLY` + `RESTORE ... AS TheBuryProjectDb_qa` (el nombre debe contener `_qa` o `_s`).
2. **Marcar el clon** (rechaza `TheBuryProjectDb` y nombres sin `_qa`/`_s`): `sqlcmd -S "(localdb)\MSSQLLocalDB" -d TheBuryProjectDb_qa -I -b -i marcar-clon.sql`.
   `zz_ins.sql` y `seed.sql` NO ejecutan nada si la base no tiene la marca `csp_sintetico_clon=1` (guarda con `SET NOEXEC ON` + `-b`), y `zz_ins` solo acepta una lista cerrada de tablas `dbo`.
3. `sqlcmd ... -d TheBuryProjectDb_qa -I -b -i zz_ins.sql` y luego `-i seed.sql` (desactiva y borra tokens de cuentas ML reales del clon antes de crear la ficticia).
4. App contra el clon: `Csp__Mode=ReportOnly`, `Database__InitializeOnStartup=false`, `--urls http://127.0.0.1:5199`. Bloquear la salida de red del servidor hacia Mercado Libre si el clon conserva credenciales.
5. `CSP_QA_CLON=1 node unidades.js | doccli.js | devol.js | comis.js | ml.js` (requiere `playwright` y Chrome). Guardas de `guard.js`: no corre con `CI`, exige `CSP_QA_CLON=1`, solo `localhost/127.0.0.1`,
   **bloquea en red todo método que no sea GET** (salvo login y SignalR) y aborta si la página no muestra los datos centinela de la siembra. Usuario/clave: `CSP_QA_USER`/`CSP_QA_PASS` (por defecto el admin de desarrollo local).
