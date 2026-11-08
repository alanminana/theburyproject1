# Validación sintética de handlers (D-05)

Pruebas de navegador (Playwright) de las acciones `data-*` de `wwwroot/js/csp-actions.js` sobre pantallas que **necesitan datos**
(Unidades, DocumentoCliente, Devolución, Comisiones, Mercado Libre) con la CSP en `ReportOnly`, contando violaciones.

**Solo contra un clon de QA, nunca `TheBuryProjectDb`** (la siembra escribe filas ficticias; las pruebas de ML usan una cuenta con tokens falsos y solo
ejecutan diálogos cancelados: no deben generar POST).

1. Clon: `BACKUP DATABASE TheBuryProjectDb ... COPY_ONLY` + `RESTORE ... AS TheBuryProjectDb_qa` (ver memoria `qa-clon-db-localdb`).
2. `sqlcmd -S "(localdb)\MSSQLLocalDB" -d TheBuryProjectDb_qa -I -b -i zz_ins.sql` y luego `-i seed.sql`.
3. App contra el clon: `Csp__Mode=ReportOnly`, `Database__InitializeOnStartup=false`, `--urls http://127.0.0.1:5199`.
4. `node unidades.js | doccli.js | devol.js | comis.js | ml.js` (requiere `playwright` y Chrome; `CSP_BASE_URL` opcional; usuario `admin`/`Admin123!`).
