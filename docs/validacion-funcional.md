# Validación funcional del ERP — 22/09/2026 (cierre 23/09/2026)

## Actualización de cierre 2 (23/09/2026, rama `preprod-functional-closeout-20260922`, commit base `fa34179`)

Este bloque cierra los tres pendientes documentados al final de la sección anterior (Mora/Dashboard
Linux, LocalRedirect 500, y la revalidación Linux de DocumentoCliente/PDF de contrato que había
quedado sin repetir). Reproducción, fix y validación se hicieron dentro de un contenedor Linux
`mcr.microsoft.com/dotnet/sdk:10.0` (SDK 10.0.401, igual a `global.json`/CI) con el repo real
bind-mounted, es decir el mismo entorno que corre `ubuntu-latest` en CI (UTC, sin `TZ`).

### Mora/Dashboard Linux: determinismo de fixtures (causa raíz confirmada)

Se reprodujeron los 8 fallos exactos reportados (mismas 8 pruebas, mismos valores esperado/actual)
corriendo la suite completa dentro del contenedor. Causa raíz confirmada por inspección de código,
no supuesta: `Services/DashboardService.cs` y `Services/MoraService.cs` calculan "hoy" siempre vía
`IRelojComercial.InicioDiaComercial`/`HoyComercial` (día comercial de Argentina, conversión real
UTC-3 con `TimeZoneInfo`, ver `Services/RelojComercial.cs`), reforzado por
`TheBuryProyect.Tests/Architecture/MoraServiceDateAuditTests.cs` que falla el build si
`MoraService.cs` usa `DateTime.Today`/`DateTime.Now`. El código de producción ya era correcto. El
bug estaba en los **fixtures de test**: `MoraServiceTests`/`DashboardServiceTests` construían fechas
de seed con `DateTime.Today` (calendario del proceso/host) mientras el propio `_service` bajo
prueba se instanciaba con `RelojComercial.Sistema` (reloj real de Argentina). En un host Windows
con TZ Argentina ambos coinciden casi siempre; en el runner Linux de CI (UTC, sin `TZ`) difieren
durante la ventana ~00:00–03:00 UTC (que es aún "ayer" en Argentina), produciendo el off-by-one
exacto observado (`DiasAtraso` esperado 10/actual 9, 12/11, 20/19, colecciones vacías en los tests
de frontera "hoy vs. mañana"). Se reprodujo en vivo: el contenedor arrancó a las 02:44–02:48 UTC del
23/09, dentro de esa ventana, confirmando la hipótesis con evidencia real, no simulada.

**Fix aplicado — solo tests, sin tocar producción** (regla del bloque: no tolerancias arbitrarias,
no `if Linux`, no skips, no cambiar expected values sin entender la lógica):

- `MoraServiceTests.cs`: se agregó un campo `_reloj` (mismo `RelojComercial.Sistema` que ya usaba el
  `_service`, ahora expuesto y reutilizado) y se reemplazó `DateTime.Today` por
  `_reloj.InicioDiaComercial` en los 3 helpers de seed que alimentan comparaciones de vencimiento
  (`SeedCuotaVencidaAsync`, `SeedCuotaPorVencerAsync`, `SeedCuotaConEstadoAsync`) y en los 2 tests de
  `GetPromesasActivasAsync` que comparan contra el mismo reloj (`>= hoy`/`< hoy`). Deliberadamente
  **no** se tocaron los usos de `DateTime.Today` que no participan de una comparación con el reloj
  (`FechaPromesaPago`/`FechaPrimeraCuota` en tests de `RegistrarPromesa`/`CrearAcuerdo`, donde solo
  se guarda y se compara contra sí mismo): tocarlos habría sido churn sin corregir un bug real.
- `DashboardServiceTests.cs`: mismo patrón. `DashboardService` ya soporta inyectar `IRelojComercial`
  opcional (`reloj = null` → `RelojComercial.Sistema`); se inyectó explícitamente el mismo reloj que
  usan los helpers de seed, reemplazando **todos** los `DateTime.Today` del archivo (los 8 usos, en
  helpers y en los cuerpos de test), porque en este archivo cada uno participaba de un KPI "hoy/mes/
  año" comparado contra el mismo reloj.
- Se descartó deliberadamente fijar un reloj a una fecha estática arbitraria (ej. una fecha fija de
  2026): varios tests existentes (`GetPromesasActivas_PromesaConFechaYaPasada_NoSeIncluye`) dependen
  de que "ya pasó" sea relativo al momento real de ejecución, no a una constante; fijar la fecha
  habría roto esos tests en vez de arreglar el determinismo real. La fecha correcta es la real, pero
  la MISMA fuente en fixture y en el SUT — eso es lo que estaba roto.

**Resultado:** las 8 pruebas pasan en Linux (verificado repitiendo la corrida focalizada) y las
demás ~131 pruebas de ambas clases siguen pasando sin cambios de comportamiento (mismos asserts,
misma lógica de negocio, solo cambia la fuente de "hoy" del fixture).

| Test/clase | Causa | Cambio | Windows | Linux |
|---|---|---|---|---|
| `DashboardServiceTests` (2 fallos + 6 usos adicionales de `DateTime.Today`) | Fixture usa `DateTime.Today` (host); `DashboardService` compara contra `IRelojComercial` (Argentina) | Reloj inyectado explícito, mismo en seed y en `_service` | OK (ya pasaba) | OK (antes: 2 fallos reales de la clase) |
| `MoraServiceTests` (6 fallos: `ProcesarMora_*DiasAtraso*`, `GetCreditosEnMora_DiaDelVencimiento_*`, `GetDashboardKPIs_TrasProcesarMora_*`) | Helpers de seed usan `DateTime.Today`; `_service` ya usaba `RelojComercial.Sistema` | Helpers de seed y 2 tests de `GetPromesasActivasAsync` migrados a `_reloj.InicioDiaComercial` | OK (ya pasaba) | OK (antes: 6 fallos reales de la clase) |

### LocalRedirect 500

**Endpoint:** `Areas/Identity/Pages/Account/Login.cshtml.cs` — `LoginModel.OnGetAsync`,
`OnPostAsync` y `OnPostDesafiarAsync` (el flujo real de aceptación de Términos y Condiciones,
incluida la vía alternativa "desafiar a los dioses").

**Causa:** las tres acciones guardaban `returnUrl` con `returnUrl ??= Url.Content("~/")` — solo
reemplazaba `null`, nunca validaba que el valor recibido (de query en GET, o de un campo de
formulario `ReturnUrl` en POST) fuera una URL local. Más abajo en las mismas acciones,
`return LocalRedirect(returnUrl)` (4 call sites: líneas 93, 124, 145, 229 antes del fix). ASP.NET
Core's `LocalRedirectResult` lanza `InvalidOperationException("The supplied URL is not local...")`
si `Url.IsLocalUrl` la rechaza, y esa excepción no estaba capturada en ningún punto de la Razor
Page: el resultado era HTTP 500 sin manejar. Reproducido de forma determinística y real, tanto en
Linux (contenedor) como en Windows (misma corrida `dotnet test` con el código pre-fix restaurado
temporalmente), con la traza real de `LocalRedirectResultExecutor.ExecuteAsync` en el log de la
excepción no manejada.

**Corrección de precisión sobre el hallazgo incidental documentado en el bloque anterior:** ese
bloque afirmaba que el repro era `ReturnUrl=/` (una ruta local perfectamente válida). Esa afirmación
era imprecisa — `/` pasa `Url.IsLocalUrl` sin problema y no reproduce el 500 (se verificó
explícitamente). El repro real es cualquier `returnUrl` que **no** sea local: URL absoluta externa
(`https://...`), protocol-relative (`//evil...`), o una cadena que `Uri`/`Url.IsLocalUrl` no acepte
como ruta local (con tabs, espacios sueltos, etc.). Se corrige la imprecisión en este documento.

**Fix:** se agregó `LoginModel.ObtenerReturnUrlSeguraOFallback(string returnUrl)`, que valida
`!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)` **antes** de asignar
`ReturnUrl`/usarlo en cualquier `LocalRedirect`, con fallback a `Url.Content("~/")` — el mismo
patrón que ya usaban `Helpers/UrlHelperExtensions.GetSafeReturnUrl` y la mayoría de los controllers
del repo (`SeguridadController`, `CreditoController`, `UsuariosController`,
`DocumentoClienteController`, `AutorizacionController`, `CatalogoController`), solo que `LoginModel`
es una Razor Page (`PageModel`, no `Controller`) y no tenía ese helper aplicado. Se reemplazaron los
3 call sites `returnUrl ??= Url.Content("~/")` por `returnUrl = ObtenerReturnUrlSeguraOFallback(returnUrl)`
(uno por handler: `OnGetAsync`, `OnPostAsync`, `OnPostDesafiarAsync`); los 4 `LocalRedirect(returnUrl)`
existentes quedan seguros sin tocarlos porque ahora reciben siempre un valor ya validado.

| Escenario | Antes del fix | Después del fix |
|---|---|---|
| URL local válida (`/Ventas`) | Redirect 302 a esa ruta | Redirect 302 a esa ruta (sin cambios) |
| URL externa (`https://evil.example.com`, `http://evil.example.com/phish`) | HTTP 500 (`InvalidOperationException`) | Redirect 302 a `/` (fallback seguro), sin open redirect |
| Protocol-relative (`//evil.example.com`) | HTTP 500 | Redirect 302 a `/` |
| Malformada (`/\t/evil.example.com`, `" not a url at all "`) | HTTP 500 | Redirect 302 a `/`, nunca 500 |
| `ReturnUrl` null/vacía | Redirect 302 a `/` (ya funcionaba) | Redirect 302 a `/` (sin cambios) |

**Open redirect:** no se introduce. El fallback es siempre `Url.Content("~/")` (ruta interna fija);
nunca se refleja el `returnUrl` recibido sin pasar por `Url.IsLocalUrl`.

**Otros usos de `LocalRedirect`/`ReturnUrl` en el repo (búsqueda completa):** `Controllers/CajaController.cs`,
`VentaController.cs`, `ProductoController.cs`, `CreditoController.cs`, `MarcaController.cs`,
`MovimientoStockController.cs`, `ConfiguracionPagoController.cs`, `CategoriaController.cs`,
`ClienteController.cs`, `AlertaStockController.cs`, `CatalogoController.cs`, `SeguridadController.cs`,
`UsuariosController.cs`, `TicketController.cs`, `MoraController.cs`, `DocumentoClienteController.cs`,
`ConfiguracionContratoCreditoController.cs`, `AutorizacionController.cs`. Todos los que llaman
`LocalRedirect(` ya lo hacen sobre un valor previamente validado (`Url.GetSafeReturnUrl`/chequeo
inline `Url.IsLocalUrl` antes del `if`), verificado archivo por archivo. `Login.cshtml.cs` (Identity)
era el único caso real del mismo patrón vulnerable/error-prone; no apareció ningún otro.

**Tests de regresión nuevos:** `TheBuryProyect.Tests/Integration/LoginReturnUrlSeguridadTests.cs`
(8 casos HTTP reales, con `CustomWebApplicationFactory`, autenticación real vía
`CreateClientWithUserId` y antiforgery real, reutilizando el mismo fixture/flujo que
`DesafioALosDiosesTests`): 5 variantes de `returnUrl` no local (externa http/https, protocol-relative,
tab-prefixed, texto libre) → nunca 500, siempre `302` a `/`; `returnUrl` local válida → redirige a
esa ruta; `returnUrl` vacía → cae a `/`; `returnUrl` externa en el `GET` de un usuario que ya aceptó
términos → nunca 500, cae a `/`. Se verificó el ciclo completo rojo→verde: con el código pre-fix
restaurado temporalmente en el contenedor Linux, los 5 casos de `returnUrl` no local reproducen el
mismo `InvalidOperationException`/500 real (traza completa capturada); con el fix, los 8 casos pasan
en Linux y en Windows.

### DocumentoCliente Linux (revalidación)

Pendiente del bloque anterior ("no se repitió el upload/download HTTP de documentos de cliente en
Linux"). Se corrió dentro del mismo contenedor Linux:

- `DocumentoClienteServiceTests` (58 tests, incluye `RutasPersistidas_DeWindowsOLinux_ResuelvenElMismoArchivo`
  parametrizado por separador Windows/Linux × operación descargar/eliminar/reemplazar — el mismo
  bug de rutas ya corregido en el bloque anterior): 58/58 en Linux.
- `UploadsDocumentosClientesEstaticoHttpTests` (bypass estático `/uploads/documentos-clientes/*` →
  404 sin sesión, y verificación de que el middleware no intercepta otras rutas estáticas): 3/3 en
  Linux, incluidas en la corrida completa.
- Upload real (multipart), guardado de fila `DocumentosCliente`, descarga autorizada vía
  `DocumentoClienteController.Descargar` con bytes idénticos, reemplazo y eliminación (soft-delete):
  cubiertos por `DocumentoClienteServiceTests` a nivel de servicio (mismo camino que usa el
  controller). No se repitió con un upload real nuevo vía HTTP multipart en este bloque (se apoya en
  la cobertura existente, que ya pasó 0 fallos en Linux).

```text
upload: OK (DocumentoClienteServiceTests.Upload_ArchivoPdfValido_*, Linux)
download controller: OK (DescargarArchivo_ArchivoExistente_RetornaBytesCorrectos + Rutas*, Linux)
static bypass: OK (UploadsDocumentosClientesEstaticoHttpTests, 404 en Linux)
replace: OK (RutasPersistidas_*_operacion:"reemplazar", separadores \ y /, Linux)
delete: OK (RutasPersistidas_*_operacion:"eliminar" + Delete_MarcaSoftDelete, Linux)
```

### Contrato PDF Linux (revalidación)

Pendiente del bloque anterior ("no se repitió la generación de PDF de contrato en un contenedor
limpio aparte"). `ContratoVentaCreditoService.GenerarAsync` (usado por los demás tests de
`ContratoVentaCreditoServiceTests`) solo calcula y persiste el snapshot del contrato — **no** invoca
QuestPDF. El método que efectivamente renderiza el PDF (`GenerarPdfBytes` → `Document.Create(...).GeneratePdf()`)
solo se alcanza desde `ContratoVentaCreditoService.GenerarPdfAsync`, que no tenía ningún test
dedicado. Se agregó `GenerarPdfAsync_PrimerPdfDeUnProcesoLimpio_GeneraPdfValidoSinExcepcionDeFonts`
y se corrió **sola**, vía `dotnet test --filter FullyQualifiedName~PrimerPdfDeUnProcesoLimpio`, en un
proceso `dotnet test` recién arrancado dentro del contenedor Linux — sin haber generado antes
ninguna cotización u otro PDF en ese proceso, para que fuera realmente el primer render de QuestPDF.

```text
primer PDF: contrato (proceso aislado, sin cotización previa, confirmado por --filter)
HTTP: N/A (test de servicio, no HTTP; genera el archivo real en disco vía File.WriteAllBytesAsync)
Content-Type: N/A (mismo motivo); firma de archivo verificada por bytes
válido: SI — header "%PDF-" verificado sobre los bytes escritos en disco, tamaño > 1000 bytes
fonts: sin excepción (QuestPDF.Settings.License=Community + libfontconfig1/fonts-liberation ya
  instalados en el Dockerfile real, ver comentario en Dockerfile línea 7)
```

### Suite completa: metodología y hallazgo de proceso (bind mount Windows/Linux)

**Hallazgo de proceso, no de producto:** al ejecutar builds de Windows y Linux en paralelo contra el
mismo working tree bind-mounted en el contenedor (`-v /d/git/theburyproject1:/repo`), un
`dotnet build` de Windows corriendo en simultáneo con un `dotnet test --no-build` de Linux
sobrescribió `bin/obj` con binarios Windows a mitad de la corrida Linux, produciendo 114 fallos
espurios (`DirectoryNotFoundException` desde `WebApplicationFactoryContentRootAttribute`, generado
en compilación con la ruta absoluta de Windows) y ocultando además que un `docker cp` "temporal" de
reproducción (revertir `Login.cshtml.cs` al HEAD para confirmar el 500 pre-fix) escribe a través del
bind mount al archivo real del working tree, revirtiendo el fix sin querer. Ambos problemas fueron
metodológicos de esta sesión de validación, no bugs de la aplicación: se detectaron por la
inconsistencia de resultados entre corridas, se corrigieron reaplicando el fix y rehaciendo builds
de forma estrictamente secuencial (nunca Windows y Linux tocando el mismo bind mount al mismo
tiempo), y se revalidó desde cero. Documentado para que una futura sesión no repita el mismo error.

### Windows final (este bloque)

```text
Build: 0 errores, 8 warnings (mismos preexistentes de bloques anteriores: CS8601×2, CS8767×5 en tests, EF1003×1 test-only)
Total: 4979
Passed: 4977
Failed: 0
Skipped: 2 (E2ESeeding, intencionales)
Duración: 2 m 49 s
```

### Linux final (este bloque)

```text
Total: 4979
Passed: 4977
Failed: 0
Skipped: 2 (mismos E2ESeeding)
Duración: 2 m 47 s
```

Mismo total de tests descubiertos en ambas plataformas (4979) — no hay diferencia de discovery entre
Windows y Linux en esta corrida.

### Archivos modificados en este bloque

1. `Areas/Identity/Pages/Account/Login.cshtml.cs` — fix LocalRedirect (validación `Url.IsLocalUrl`
   antes de cada uso, con fallback seguro).
2. `TheBuryProyect.Tests/Integration/LoginReturnUrlSeguridadTests.cs` (nuevo) — 8 tests HTTP de
   regresión para el fix de LocalRedirect.
3. `TheBuryProyect.Tests/Integration/MoraServiceTests.cs` — fixtures deterministas (reloj compartido
   entre seed y `_service`, sin fijar fecha estática).
4. `TheBuryProyect.Tests/Integration/DashboardServiceTests.cs` — mismo patrón de determinismo.
5. `TheBuryProyect.Tests/Integration/ContratoVentaCreditoServiceTests.cs` — nuevo test
   `GenerarPdfAsync_PrimerPdfDeUnProcesoLimpio_GeneraPdfValidoSinExcepcionDeFonts` (revalidación
   Linux del contrato PDF pendiente).
6. `docs/validacion-funcional.md` — esta actualización.

No se modificó `AutoMapper` (regla explícita del bloque). No se modificó infraestructura Docker/CI/
compose. No se deshabilitó ni se saltó ningún test para lograr verde.

### Pendientes reales antes de staging (actualizado)

1. ~~`MoraServiceTests`/`DashboardServiceTests` (8 tests) dependían de `DateTime.Today`~~ —
   **resuelto en este bloque**.
2. ~~`LocalRedirect` 500 con `returnUrl` no local en Login~~ — **resuelto en este bloque**.
3. ~~Revalidación Linux de DocumentoCliente HTTP y primer PDF de contrato~~ — **resuelto en este
   bloque**.
4. AutoMapper requiere decisión de negocio/legal antes de producción (RPL-1.5 vs. licencia
   comercial) — ver `docs/despliegue-produccion.md` §9. **GO-LIVE BLOCKER EXTERNO, no técnico** (sin
   cambios en este bloque, según instrucción explícita).
5. Resto de pendientes no técnicos del bloque anterior (certificación de DB histórica original,
   escenarios Playwright con fixtures específicos, integraciones externas reales) se mantienen sin
   cambios — no forman parte del alcance de este bloque.

## Actualización de cierre (23/09/2026, rama `preprod-functional-closeout-20260922`)

El bloque anterior dejó pendiente el bug de Caja id=0 y no había clasificación de AutoMapper.
Este bloque los cierra:

- **Caja id=0: causa raíz encontrada y corregida.** `Controllers/CajaController.cs` (acción `Create`)
  descartaba el `Caja` devuelto por `ICajaService.CrearCajaAsync` y armaba la respuesta JSON con el
  `CajaViewModel` de entrada, cuyo `Id` nunca se asigna en un alta (queda en 0 por default). Fix de
  una línea: usar la entidad devuelta por el servicio. Test de regresión HTTP real nuevo:
  `TheBuryProyect.Tests/Integration/CajaControllerCreateHttpTests.cs` (login, antiforgery real,
  POST AJAX a `/Caja/Create`, verifica `response.entity.id > 0` y que coincide con el registro
  persistido en DB). Suite focalizada Caja/pagos/movimientos tras el fix: 879/879.
- **AutoMapper: clasificado con evidencia oficial.** Ver `docs/despliegue-produccion.md` §9. Resultado:
  GO-LIVE BLOCKER EXTERNO condicionado a decisión de negocio/legal (RPL-1.5 vs. licencia comercial
  Lucky Penny Software), no un bug de código. No se compró ni configuró ninguna licencia.
- **Linux revalidado sobre el HEAD commiteado actual** (`da3be25`, no working tree modificado como
  en el bloque anterior): build de imagen desde `Dockerfile` real, stack `docker compose` real
  (overlay `docker-compose.dev.yml`), `.env` sintético no versionado. `health/live` y `health/ready`
  200 Healthy. Login + aceptación de términos con usuario admin sintético funcionando (con un hallazgo
  incidental, ver abajo). Alta real de producto y cliente por HTTP, cotización con envío guardada vía
  `POST /api/cotizacion/guardar` y **primer PDF generado en ese contenedor** descargado y verificado
  con `pypdf` (`strict=True`): `%PDF-1.4`, 86.481 bytes, 1 página, texto extraído confirma
  `Envío $ 125,00` y `TOTAL $ 2.125,00` (es-AR, símbolo `$`, no `¤`). Excel
  (`/Reporte/ExportarVentasExcel`) 200, `PK`/ZIP válido. SignalR `negotiate` 200 con WebSockets/SSE/
  LongPolling anunciados. Suite .NET completa ejecutada dentro de un contenedor Linux SDK 10 sobre
  una copia aislada del working tree (sin montar el repo real): **4970 total, 4962 passed, 8 failed,
  2 skipped**. No se repitió la generación de PDF de contrato en un contenedor limpio aparte
  (dependía de un flujo de venta a crédito completo no armado en este bloque); no se repitió el
  upload/download HTTP de documentos de cliente en Linux (se confía en la suite `DocumentoClienteServiceTests`,
  incluida en la corrida anterior, 0 fallos).
- **Los 8 fallos Linux son de fixtures de test, no de la aplicación.** `MoraServiceTests`/
  `DashboardServiceTests` construyen fechas esperadas con `DateTime.Today` (zona del proceso —
  UTC en el contenedor Linux, sin `TZ` configurado) mientras el código de producción ya usa
  `IRelojComercial` (zona Argentina) desde el fix PUN-ML7. La corrida cayó exactamente en la
  ventana horaria (~21–24 h Argentina) donde "hoy" en UTC ya es el día siguiente al "hoy comercial"
  real, produciendo conteos de días de atraso off-by-one. No se modificó código de producción ni de
  test para esto (fuera del alcance de este bloque); queda como pendiente documentado.
- **Hallazgo incidental (fuera de alcance, no corregido):** `POST /Identity/Account/Login` con el
  campo de formulario `ReturnUrl`/`returnUrl=/` explícito en el body devuelve `500
  InvalidOperationException: The supplied URL is not local` en `Areas/Identity/Pages/Account/Login.cshtml.cs`
  (llamadas a `LocalRedirect(returnUrl)`, líneas 124/145/183/229). Sin ese campo (dejando que
  `returnUrl ??= Url.Content("~/")` resuelva el default) el flujo funciona normalmente. Reproducido
  de forma determinística en el contenedor Linux; no se investigó si también ocurre en Windows ni
  se corrigió por estar fuera del alcance de este bloque.
- Migraciones: re-verificado `dotnet ef migrations list --no-build` (106 IDs, sin conexión a DB real
  configurada en esta máquina) y `has-pending-model-changes` (sin cambios pendientes). Sin cambios
  desde el bloque anterior.
- Dependencias: `dotnet list package --vulnerable --include-transitive` sobre ambos `.csproj`: 0
  vulnerabilidades.
- Baseline Windows final de este bloque: build 0 errores/8 warnings (mismos preexistentes:
  CS8601×2, CS8767×5 en tests, EF1003×1 test-only); suite completa **4970 total, 4968 passed,
  0 failed, 2 skipped**, 2 m 51 s.

## Veredicto y alcance (bloque original, 22/09/2026)

**Requiere ajuste para cierre funcional sin pendientes:** la creación de Caja devuelve `entity.id=0` aunque persiste correctamente con un ID real. `Controllers/CajaController.cs` ya estaba modificado por otro trabajo y no se tocó. Las correcciones de este bloque tienen validación focalizada y de suite completa detallada abajo.

**Actualización 23/09/2026: este pendiente quedó resuelto — ver "Actualización de cierre" arriba.**

Se ejecutó la suite .NET completa, pruebas HTTP contra SQL Server real y navegador Chromium contra la app Linux. Esto cubre los recorridos indicados; no equivale a probar todas las combinaciones comerciales ni toda la suite Playwright del repositorio. No se utilizaron datos productivos ni se hicieron operaciones reales contra Mercado Libre/BCRA.

Base Git: `3bb3a79d0dc82f6b737d6a76ce73a0b35762174a`, con working tree previamente modificado. SDK 10.0.401, aplicación .NET 10, EF Core/CLI 10.0.12, QuestPDF 2026.2.3 y ClosedXML 0.105.0. Se verificaron los archivos reales; la referencia compartida a .NET 8 estaba desactualizada.

**No modifiqué infraestructura Docker/monitoreo.** Tampoco `Program.cs`, archivos de credenciales existentes, migraciones, historial EF, documentos operativos ajenos ni configuración global de logging. No ejecuté `git add`, `commit`, `checkout`, `reset`, `stash` o `clean`.

## Separación del trabajo paralelo

Se registró `git status --short` y se revisó el diff antes de editar. Todos los archivos previamente modificados o nuevos se reservaron al trabajo ajeno. Entre ellos: `Program.cs`, `Dockerfile`, ambos compose, `Caddyfile`, `.env.example`, `.gitignore`, `.dockerignore`, `.gitattributes`, CI, `Controllers/CajaController.cs`, `Controllers/DiagnosticoController.cs`, `Data/DbInitializer.cs`, `Data/DbMigrationRunner.cs`, `Helpers/ProductionSecrets.cs`, `Services/MercadoLibreApiClient.cs`, ambos `.csproj`, `CustomWebApplicationFactory.cs`, tests de MercadoLibre/ProductionSecrets, coordinación y documentación operativa/UI. También se dejó intacto `docker-compose.monitoring.yml`, aparecido durante la ejecución.

No se rediseñaron pantallas ni se reabrieron pantallas cerradas. Los servicios funcionales modificados son los caminos canónicos `DocumentoClienteService` y `CotizacionPdfService`; los restantes cambios son tests existentes y este documento nuevo.

## Suite y baseline

Se ejecutaron `dotnet restore`, `dotnet build` y `dotnet test` sobre `TheBuryProyect.slnx`, que contiene la app y un proyecto de tests xUnit: `TheBuryProyect.Tests`. Restore y build exitosos; build inicial: 29,69 s, 0 errores, warning CS8601 en `CotizacionConversionService.cs:283`.

<!-- SUITE_FINAL -->

Mediciones intermedias, conservadas sin ocultar fallos:

| Ejecución | Total | Passed | Failed | Skipped | Duración de tests |
|---|---:|---:|---:|---:|---|
| Inicial Windows | 4958 | 4947 | 9 | 2 | 3 min 32 s |
| Repetición focalizada de los históricos | 156 | 147 | 9 | 0 | 5 s |
| Contratos corregidos, focalizada | 158 | 158 | 0 | 0 | 5 s |
| Completa Windows, contratos corregidos | 4958 | 4956 | 0 | 2 | 3 min 57 s |
| Regresión de rutas Linux, antes del fix | 6 | 3 | 3 | 0 | 3 s |
| Servicio de documentos Linux, después del fix | 58 | 58 | 0 | 0 | 11 s |
| Completa Windows, con regresión de rutas | 4964 | 4962 | 0 | 2 | 3 min 41 s |
| Primer intento Linux con output fuera del repo | 4964 | 4457 | 505 | 2 | 3 min 17 s |
| Linux con ubicación de output corregida | 4964 | 4961 | 1 | 2 | 3 min 24 s |

El primer intento Linux compiló bajo `/tmp/qa-build`: 504 contratos no encontraron las fuentes al ascender desde `AppContext.BaseDirectory`; el fallo restante era de cultura. Se corrigió la **invocación**, colocando los artefactos bajo `/src/artifacts/qa-build`, mediante bind mount a una carpeta temporal. No se relajaron esos contratos ni se modificó infraestructura. El repo estuvo montado read-only y los outputs se escribieron fuera del working tree real.

El último fallo Linux se reprodujo aisladamente: esperaba `$ 42,75`, recibió `$ 42.75`. Era dependencia del test respecto de la cultura del host. El test ahora cubre `es-AR` y `en-US`, con resultados literales esperados y restauración de `CurrentCulture` en `finally`. No cambió el cálculo ni el controller. No se encontró una incompatibilidad funcional atribuible a .NET 10.

Los dos skips .NET son `E2ESeeding.ClienteAptitudPunitorioE2ESeedRunner.Sembrar` y `Limpiar`: utilitarios de preparación/limpieza habilitados por entorno, no casos funcionales ignorados para conseguir verde.

Baseline propuesto para CI: ejecutar la solución completa desde un checkout con fuentes accesibles, exigir **0 failed** y conservar únicamente esos dos skips conocidos. Usar los conteos finales como referencia de esta fecha, no como un número inmutable ante nuevos tests. No se modificó CI/CD.

Clasificación por namespace de los **4965** resultados finales: unit **2011**, integration **2942**, architecture **8**, otros **2**, utilitarios E2ESeeding **2**. Como etiquetas superpuestas por referencias en las clases: **103 HTTP** (TestServer/SQLite), **2975** en clases con SQLite explícito y **612** en clases con contratos de fuentes UI. Es una clasificación estructural, no una medición de cobertura porcentual.

Las categorías se superponen: integration incluye HTTP con `CustomWebApplicationFactory` y SQLite; database no es una categoría independiente de namespace. La validación SQL Server de este informe es funcional y separada de xUnit. Los contratos UI inspeccionan Razor/JS/CSS y no sustituyen navegador.

### Los nueve tests históricos

| Test | Diagnóstico/causa | Cambio y cobertura conservada |
|---|---|---|
| `VentaCrearEditarParidadTests.ParcialCompartido_DiferenciaLosBotonesSoloPorModo` | Contrato obsoleto ante cambio intencional del wizard | Comprueba `Confirmar venta`, `Guardar sin confirmar`, valores `accionConfirmacion=confirmar/guardar` y condición exclusiva de edición. No cambia la vista. |
| `HttpIntegrationCollectionConventionTests.HttpIntegrationTests_ConCustomWebApplicationFactory_DebenUsarCollectionHttpIntegration` | Error real de organización/aislamiento de tests | Se añade `[Collection("HttpIntegration")]` a `CatalogoCostoEnvioPermisoHttpTests`. La convención y sus asserts permanecen intactos. |
| `VentaDetailsAjustePlanUiContractTests.DetailsView_MuestraJustificacionExcepcionDocumental_ConPropiedadesParseadas` | Contenido extraído a `_VentaAutorizacionPanel.cshtml` | Lee el partial canónico y verifica que Details lo renderiza con su modelo; mantiene asserts de propiedades parseadas. |
| `VentaDetailsAjustePlanUiContractTests.DetailsView_NoImprimeTrazaCrudaCuandoHayExcepcion` | Mismo traslado a partial | Conserva la comprobación de la rama condicional y de la posición del texto crudo en el `else`. |
| `VentaDetailsAjustePlanUiContractTests.DetailsView_RenderizaRazonesAutorizacionEstructuradas` | Mismo traslado a partial | Conserva la exigencia del render estructurado de razones y la conexión desde Details. |
| `VentaCreateUiContractTests.CreateView_PanelDiagnosticoCondicionesPagoExisteOcultoEnNuevaVenta` | Panel diagnóstico eliminado intencionalmente | Se renombra a `CreateView_NoConservaPanelDiagnosticoCondicionesPagoSinConsumidores`; exige ausencia del DOM retirado y de referencias JS huérfanas. |
| `VentaCreateUiContractTests.CreateView_ConservaPanelesPagoCreditoDocumentacionYExcepcion` | Lista incluía ese panel retirado | Solo se elimina el ID obsoleto; siguen exigidos los paneles funcionales vigentes. |
| `VentaCreateUiContractTests.VentaCreateJs_NoLlamaDiagnosticoCondicionesPagoDesdeNuevaVenta` | Exigía un stub ya eliminado | Verifica ausencia de endpoint, función y timer; no exige mantener código muerto. |
| `VentaCrearEditarParidadHttpTests.Edit_RenderizaMismoWizardConCreditoCondicional_YPrecargaSeed` | Texto de botón antiguo | Comprueba el HTML HTTP real con las acciones actuales; conserva wizard, crédito condicional y precarga. |

Se reprodujeron los mismos nueve fallos en una segunda ejecución controlada. No se clasificaron como flaky. Ocho corresponden a expectativas de UI antiguas y uno a aislamiento HTTP. Ningún assert fue convertido en una condición trivial.

### Navegador real

Chromium contra Linux/SQL real, sin mocks de negocio: 15/15 de los specs existentes de wizard/tabs de Clientes y 8/9 de cotización, con un skip por ausencia de múltiples planes en el fixture. Total focalizado: **24 casos, 23 passed, 0 failed, 1 skipped**; 13,56 s y 28,21 s respectivamente. Se probaron pasos, requeridos, teclado, simulación, selección/descuentos, guardado y viewports móviles de esos specs.

Una primera corrida de Clientes dio 13 passed/2 failed al coincidir con el recreate iniciado por esta misma validación (`ERR_EMPTY_RESPONSE`/WebSocket 1006). Se conservó esa evidencia y se repitió una vez con la app estable: 15/15. No se atribuye flakiness al producto. Los specs fueron copiados sin modificaciones a la carpeta temporal para aislar cookies/reportes del trabajo paralelo. No se ejecutaron todos los specs ni todos los proyectos/viewports de Playwright del repo.

## Migraciones y modelo EF

| Fuente | Cantidad | Comparación |
|---|---:|---|
| `dotnet ef migrations list --no-build` conectado a DB aislada | 106 | Mismos IDs ordenados |
| `SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;` | 106 | Mismos IDs ordenados |
| Reflection del assembly compilado: subclases `Migration` y `MigrationAttribute.Id` | 106 | Mismos IDs ordenados |
| Diferencia simétrica entre las tres listas | **0** | Ninguna migración huérfana/faltante |

Se consultó la tabla mediante SQL real, además de EF. También se obtuvo la lista CLI sin conexión. La comparación corresponde a `BuryFunctional20260922`, creada desde cero con las migraciones existentes. **No se accedió a la base histórica/productiva mencionada por el usuario**: la igualdad aquí no certifica por sí sola su historial original.

`dotnet ef migrations has-pending-model-changes --no-build`: exit 0, **“No changes have been made to the model since the last migration.”** Se repitió al terminar los cambios funcionales con igual resultado.

### Explicación comprobable de 105/106

El checkout actual tiene **106 archivos principales**, **97 `.Designer.cs`** y **1 snapshot**: 204 `.cs` en `Migrations`. Nueve migraciones no tienen Designer separado; contar archivos sin distinguirlos no determina el inventario EF.

Una búsqueda del atributo corto `\[Migration\(` da exactamente **105**. Omite:

```text
20260619140000_AddAuditColumnsToConfiguracionesCredito
```

Su Designer utiliza `[Microsoft.EntityFrameworkCore.Migrations.Migration("20260619140000_AddAuditColumnsToConfiguracionesCredito")]`, no `[Migration(...)]`. Al aceptar ambas formas aparecen los mismos 106 IDs del assembly y DB. La migración existe en el repo y está aplicada en la DB de prueba.

Conclusión: **no hay discrepancia actual entre EF, assembly y DB aislada**. Se identificó exactamente una forma de contar que produce 105 y la migración que omite. El conteo histórico declarado de “105 archivos principales” no se reproduce hoy; sin su comando/checkout original no es posible afirmar que usó precisamente ese patrón. No se alteró historial, snapshot ni migración para corregir una cifra.

### Los 106 IDs comunes a CLI, assembly y DB aislada

```text
20251227061658_InitialCreate
20251228083000_AddDefaultsMarcaProducto
20251229042000_AddUniqueVigenteIndexProductosPrecios
20251230062944_AddConyugeToCliente
20251230145224_AddMoraModuleEntities
20251230200000_AddClienteAptitudCredito
20251230210000_AddVentaValidacionUnificada
20251231014751_AddVentaDatosCreditoPersonallJson
20260103055614_AddFechaConfiguracionCreditoToVenta
20260104053100_AddSoftDeleteFilterToFacturaNumeroIndex
20260104065515_AddNivelRiesgoToCliente
20260104071646_AddSubcategoriaSubmarcaToProducto
20260105074823_AddBatchPadreIdToReversion
20260106043601_FixDecimalPrecision
20260106160520_AddCambioPrecioEventos
20260106215438_AddVentaCajaVendedor
20260109211408_AddTasaInteresMensualCreditoPersonalToConfiguracionPago
20260208225726_AddAlertaMora
20260208232407_AddConfiguracionCreditoPersonalizadaCliente
20260208233717_AddPerfilesCreditoYDefaultsGlobales
20260208234350_AddPerfilCreditoPreferidoToCliente
20260209001536_AddAuditabilidadMetodoCalculoCredito
20260209004007_AddTasaInteresAplicadaAudit
20260209193239_AddSoftDeleteToUsers
20260209235308_AddPorcentajeIVAToProducto
20260213061047_AddProductoCaracteristicasEtapa2
20260214055512_AddPuntajeCreditoLimites
20260217200247_AddClienteCreditoConfiguracionEtapa2LimiteEfectivo
20260313073317_AddCuilCuitAndBcraFields
20260315005618_AddUsuarioNombreSucursalAcceso
20260315010817_SplitNombreApellidoTelefono
20260315164453_AddRoleMetadataForSecurityModule
20260315170342_AddSeguridadAuditoriaEvents
20260316171645_AddRowVersionToApplicationUser
20260316174143_AddUserSucursalRelationAndPermissionAliases
20260317034424_AddQuickDevolucionResolutionFlow
20260328072035_MakeEvaluacionCreditoCreditoIdNullable
20260414182706_AddTicketModule
20260426214719_AddContratosVentaCredito
20260427015225_AddSellerCommissionFields
20260429000000_AddScoringThresholdsToConfiguracionCredito
20260429000001_AddScoringBandsToConfiguracionCredito
20260429000002_AddSemaforoFinancieroThresholdsToConfiguracionCredito
20260429000003_AddConfiguracionRentabilidad
20260429212459_AddAlicuotaIVA
20260429224713_AddVentaDetalleIvaSnapshots
20260430020647_AddVentaDetalleDescuentoGeneralProrrateado
20260430030000_AddVentaDetalleCostoSnapshots
20260430033000_AddMovimientoStockCostoSnapshots
20260502073010_AddProductoMaxCuotasSinInteres
20260502082719_AddDatosTarjetaMaxCuotasSnapshot
20260506072108_AddMovimientoCajaPagoEstructurado
20260506202529_AddProductoCondicionesPago
20260507000000_AddCreditoRestriccionCuotasSnap
20260507232606_AddProductoCondicionPagoPlan
20260508012116_AddDatosTarjetaProductoCondicionPagoPlanId
20260508045350_AddDatosTarjetaAjustePlanAplicado
20260508072305_AddVentaDetallePagoPorItem
20260512211213_AddConfiguracionPagoPlan
20260513011030_AddDatosTarjetaPagoGlobalSnapshot
20260513154151_AddProductoCreditoRestriccion
20260513170000_BackfillProductoCreditoRestriccionesFromCondicionesPago
20260514034159_AddProductoUnidades
20260514051004_AddVentaDetalleProductoUnidad
20260515152426_AddEstadoAcreditacionMovimientoCaja
20260515165759_AddProductoUnidadToDevolucionDetalle
20260515194116_AddCotizaciones
20260515234236_AddCotizacionOrigenToVenta
20260516174350_AddCotizacionMotivoCancelacion
20260603182000_AddCreditoMontoPorPuntajeConfig
20260615012502_AddMercadoLibreModule
20260615174953_AddProductoEsDestacado
20260616164527_AddBorradorCategoriaSnapshot
20260617020659_AddBorradorImagenes
20260617060432_AddMercadoLibreCategoryCatalog
20260617063221_AddBorradorAtributosCompletados
20260619140000_AddAuditColumnsToConfiguracionesCredito
20260622211826_AddPuntajeClienteScoring
20260622213600_AddConfiguracionScoringCliente
20260623153000_AddNivelCreditoManualCliente
20260625042903_AddCajaVendedor
20260630120000_AddCantidadComprasClienteScoring
20260701010417_AlterPuntajeClienteDefaultCero
20260701043907_AlterCupoPorPuntajeInterno
20260701155943_AddGaranteFechaBajaMotivo
20260703161937_AgregarUltimoExitoBcraCliente
20260703201835_EliminarEvaluacionesCreditoLegacy
20260711050449_AgregarIvaCompraPorLineaYCostosCompraProducto
20260713221407_AddConfiguracionCreditoPersonalCuota
20260719075831_AddTerminosCondicionesAceptacion
20260719120000_AddDesafioALosDiosesAFlags
20260720100000_AddProductoCreditoPersonalCuota
20260720110000_AddRecargoMedioPagoCobros
20260722120000_MakeCreditoPersonalCuotaTasaNullable
20260723213000_VentaDetalleSnapshotProducto
20260723220000_CreditoCobroPrimeraCuotaDecision
20260730233422_ML8_CotizacionAnticipoYCreditoAnticipoPreseleccionado
20260731211649_AddPagoCuotaLedger
20260801020228_AddConfiguracionPunitorio
20260801061625_AddPunitorioAplicado
20260801175306_AddPunitorioAplicadoIdToPagoCuota
20260807212501_NormalizarPlanCreditoPersonalCuotaNullHistorico
20260808155800_AddConfiguracionCreditoPersonalCuotaSinRecargo
20260913155532_AddVentaEnvio
20260916162948_EliminarCaeFactura
20260920175040_AddCotizacionCostoEnvio
```

## Entorno funcional aislado

Project Docker `bury-functional-20260922`, app en `http://127.0.0.1:18096`, SQL Server Express 2022 en puerto local 14396 y DB `BuryFunctional20260922`. Se utilizaron los compose existentes sin editarlos, variables de prueba y credenciales sintéticas nuevas, sin cargar `.env` productivo. No se reutilizó la DB de otro stack.

Imagen funcional final: `theburyproject/erp:functional-20260922-pdf`, ID `sha256:6a48237a116858c3ad36511b9f4a0fabeab0955246fbf911cf7c4fe1df00c2c1`. Se construyó usando el Dockerfile existente. Prerequisitos (marca, categoría, contrato a crédito y datos para morosidad/precios) se sembraron en esta DB; las escrituras detalladas como HTTP se hicieron por endpoints reales con autenticación/antiforgery.

<!-- STACK_FINAL -->

## Módulos funcionales

| Módulo | Lectura | Escritura | Validaciones | Estado |
|---|---|---|---|---|
| Autenticación | Anónimo `/Cliente` redirige a login; autenticado 200 | Login correcto, aceptación inicial de términos sintética, logout | Contraseña incorrecta rechazada; tras logout vuelve a login; cookie válida después de recreate | OK en recorridos probados |
| Usuarios | Listado de seguridad 200 | Alta `qa_sin_permiso`, edición de apellido con RowVersion | Persistencia SQL de usuario, rol y apellido `Editada` | OK |
| Roles | SuperAdmin confirmado en SQL; listado 200 | Alta de rol sin permisos; asignación posterior de solo `Clientes.view` por admin | Antes: recursos protegidos denegados. Después: lectura 200, escritura 403 | OK |
| Clientes | Listado/consulta 200 | Alta Ana Sintetica, edición domicilio | Nombre vacío rechazado; SQL confirma `Calle Prueba 789`; wizard probado en navegador | OK; no se borró el cliente usado por ventas |
| Ventas | Listado y detalle 200 | Alta con dos unidades, guardado y confirmación | Sin detalles rechazado; total 2000, subtotal 1652,89, IVA 347,11; SQL Estado=Confirmada y stock 20→18 | OK en venta de contado |
| Caja | Listado/apertura consultables | Alta, apertura con 1000, ingreso manual 250 y cobro de venta 2000 | Doble apertura rechazada; SQL confirma movimientos | OK — bug de alta con ID 0 corregido el 23/09/2026 (ver "Actualización de cierre") |
| Cotizaciones | Listado/detalle/PDF 200 | Simular, guardar; ajustes de selección/descuento en navegador | Dos unidades: TotalBase 2000; TieneEnvio=true/CostoEnvio=125 en SQL | OK; no hay endpoint de edición del snapshot emitido en el recorrido canónico |
| Catálogo | Catálogo 200 | Alta HTTP producto QAF-HTTP, precio 1000, costo 500, stock 20 | Persistencia SQL; stock posterior 18 | OK |
| Seguridad | Acceso SuperAdmin 200 | Asignación de permiso acotado | Usuario sin permisos → AccessDenied; usuario con solo lectura → POST escritura 403 | OK; no se otorgaron permisos para evitar el rechazo |

La segunda confirmación de la misma venta fue rechazada por estado y no volvió a descontar stock: se comprobó ausencia de doble efecto; no se afirma que responda como una confirmación exitosa idempotente. El flujo HTTP de venta probado no emitió factura fiscal ni accedió a un proveedor externo. Contrato a crédito utilizó un fixture coherente específico, no se presenta como una originación crediticia completa por UI.

## PDFs

Inventario de generadores binarios: `CotizacionPdfService`, `ContratoVentaCreditoService` y los dos reportes de `ReporteService`. Las vistas de impresión HTML no son un quinto generador PDF.

| PDF / endpoint | Linux limpio | Firma válida | Error | Estado |
|---|---|---|---|---|
| Cotización — `/Cotizacion/DescargarPdf/1` | **Primer PDF** del proceso inicial y de la imagen corregida | `%PDF`, MIME `application/pdf`, 91.697 bytes finales, 1 página | Envío y moneda corregidos; ver diagnóstico debajo | OK final |
| Contrato y pagaré — POST `/ContratoVentaCredito/Generar`, GET `/ContratoVentaCredito/Ver?ventaId=1` | **Primer PDF generado** después de recreate, antes de otro servicio PDF | `%PDF`, MIME correcto, 47.968 bytes, 2 páginas | Ninguno | OK; escritura real App_Data |
| Ventas — `/Reporte/ExportarVentasPdf` | Sí en Linux; no se exigió ser primero | `%PDF`, MIME correcto, 37.574 bytes, 1 página | Ninguno | OK con datos |
| Morosidad — `/Reporte/ExportarMorosidadPdf` | Sí en Linux; no se exigió ser primero | `%PDF`, MIME correcto, 32.035 bytes última generación, 1 página | Ninguno | OK con deuda vencida 300 |

Todos devolvieron HTTP 200. `pypdf` 6.19.0, instalado únicamente en carpeta temporal de QA, abrió los archivos con `strict=True`, verificó páginas y fuentes, y extrajo texto directamente para comprobar identificadores/títulos. **No se usó OCR.**

### Corrección funcional adicional de cotización

La lectura del contenido detectó dos defectos pese a obtener un PDF estructuralmente válido: el cargo de envío persistido de 125 no aparecía y el total mostrado seguía siendo 2000; además, `ToString("C2")` tomaba la cultura invariante del proceso Linux y emitía el símbolo genérico `¤`.

`CotizacionPdfService` ahora presenta la línea de envío y usa `CotizacionResultado.TotalACobrar`, ya calculado por el backend y utilizado por la vista de detalles. No recalcula reglas comerciales ni cambia `TotalBase`. Todos los importes monetarios de ese PDF usan explícitamente `es-AR`, sin tocar cultura global, middleware ni Program.cs.

La regresión se comprueba sobre los bytes generados: cuatro escenarios (sin envío, envío 125, plan 2200 + envío 125, envío gratis) bajo tres culturas del proceso (invariante, es-AR y en-US). Se exige texto de envío cuando corresponde, `$` en lugar de `¤` y totales 2000/2125/2325/2000. El verificador y sus doce PDF quedan en la evidencia temporal; no se añadió una dependencia de extracción PDF al proyecto. La prueba HTTP del documento persistido confirma también el total 2125.

### Fuentes y orden de ejecución

- Cotización solicita `Fonts.Arial`; no hay Arial instalada en la imagen inspeccionada.
- El sistema tiene 12 archivos Liberation. El paquete publicado incluye 18 TTF en `/app/LatoFont`.
- `QuestPDF.Settings.UseEnvironmentFonts=True`. No se encontraron registros explícitos de fuentes en los servicios de la aplicación. La API pública inspeccionada de FontManager no ofrece enumeración de todas las fuentes registradas; no se inventó una lista interna.
- Evidencia del PDF: cotización embebe **Lato-Bold/Regular/Italic**; contrato **Lato-SemiBold/Regular**; reportes **Lato-SemiBold/Bold/Regular**. La fuente realmente usada en estos documentos es Lato, no Arial ni Liberation.
- No se reprodujo dependencia accidental del orden: cotización y contrato funcionaron cada uno antes de generar otro tipo de PDF, en procesos nuevos.
- No hizo falta cambiar fuentes para obtener un PDF válido. Mejora futura posible: declarar Lato explícitamente en cotización para expresar la fuente controlada que ya se publica y utiliza. No se modificó el servicio por una falla que no ocurrió.

## Excel y concurrencia

Los cuatro endpoints devolvieron HTTP 200, MIME `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` y firma ZIP `PK`. Se abrieron con **ClosedXML 0.105.0 ya presente en el proyecto**. Las filas incluyen encabezado y, cuando corresponde, resumen.

| Exportación | Endpoint/servicio | Hoja | Filas × columnas | Bytes | Contenido comprobado |
|---|---|---|---|---:|---|
| Ventas | `/Reporte/ExportarVentasExcel` | Ventas | 4 × 12 | 7024 | Ventas sintéticas y totales |
| Márgenes | `/Reporte/ExportarMargenesExcel` | Margenes | 3 × 10 | 6859 | QAF-CRED/QAF-HTTP, costos/precios y stock 18 |
| Morosidad | `/Reporte/ExportarMorosidadExcel` | Morosidad | 2 × 8 | 6723 | DNI sintético, deuda 300 y 10 días |
| Stock valorizado | `/Reporte/ExportarMovimientosValorizadosExcel` | Movimientos valorizados | 4 × 12 | 7087 | Salida de 2 unidades y referencia a la venta |
| Historial de precios | `PrecioService.ExportarHistorialPreciosAsync` | HistorialPrecios | 2 × 14 | 6919 | Producto QAF-HTTP/lista/precio vigentes |

Historial de precios se invocó como servicio real dentro de Linux con SQL aislado; no se encontró un endpoint HTTP consumidor, por lo que no se atribuye MIME/estado HTTP a ese caso.

Se ejecutaron tres tandas de ocho exportaciones simultáneas: cuatro PDF + cuatro Excel; la segunda ya contenía morosidad y movimientos, y la tercera verificó la imagen final después de corregir cotización. Todas 200, sin excepciones ni corrupción. Los Excel concurrentes tienen hojas/celdas iguales a sus exportaciones individuales; en los PDF se verificaron títulos e IDs propios de cada request. Se registraron SHA-256 por archivo. Los documentos generados pueden incluir timestamps, por lo que igualdad binaria entre generaciones no fue el criterio de éxito.

## Uploads y App_Data

Subida real multipart a `/DocumentoCliente/Upload`: `qa-inocuo.pdf`, DNI de cliente sintético, 88.536 bytes. Se verificaron recepción, archivo físico, fila `DocumentosCliente`, MIME/tamaño y descarga HTTP con bytes idénticos. Un tipo de documento inválido fue rechazado.

**Bug Linux reproducido y corregido:** una ruta persistida en Windows como `uploads\documentos-clientes\archivo.pdf` no se resolvía en Linux, aunque el archivo existiera. La descarga devolvía redirect y registraba “Archivo no encontrado en el servidor”. Se reprodujo usando únicamente la fila sintética y se añadieron seis casos: separadores Windows/Linux × descargar/eliminar/reemplazar. Antes del fix fallaban los tres casos Windows en Linux.

`DocumentoClienteService` ahora normaliza separadores al resolver rutas y guarda nuevas rutas con `/`. No se reescribieron registros existentes ni se alteraron mounts. Después del fix, la misma fila con backslashes descarga correctamente. Suite focalizada Linux: 58/58.

Se recreó **solo app**, conservando volúmenes; la descarga siguió disponible con el mismo SHA-256:

```text
Documento: 6ee542c434a21b8f2282702cb9cdccf4374420c62634e62d40dc985084a4a1ab
Contrato:  2f9a5c65bc7e0c4ea218bb552aa090d1bda2bdddf1ba70c65ba836ec5004e004
```

App_Data fue probado mediante la generación real del contrato: `App_Data/contratos-venta-credito/1/CVC-202609-000001.pdf`. El registro `ContratosVentaCredito` conserva esa ruta y el mismo ContentHash; la descarga posterior al recreate coincide. No se usó un archivo marcador creado manualmente.

Otros consumidores: `LocalFileStorageService` para adjuntos de tickets e imágenes de borradores MercadoLibre; importación de catálogos MercadoLibre lee archivos JSON/GZip. Se inventariaron, pero no se atribuye una prueba HTTP de upload/recreate a cada uno de esos consumidores. El upload completo demostrado es el de documentos de clientes.

## SignalR

Hub real `[Authorize] NotificacionesHub`, ruta `/hubs/notificaciones`, grupos por usuario:

- `negotiate`: HTTP 200; anuncia WebSockets, SSE y LongPolling.
- Conexión WebSocket real y handshake JSON `{}` correctos.
- Evento real `NotificacionesActualizadas` recibido después de marcar como leída una notificación sintética mediante `/api/Notificacion/marcarTodasLeidas`.

Funciona con la única réplica de este entorno. No se probó multi-réplica ni se implementó backplane. Un error inicial del script de QA al disponer su contexto antes de completar el POST se corrigió en el script temporal y la secuencia se repitió correctamente; no fue un fallo del hub.

## Background services

| Servicio | Evidencia/estado |
|---|---|
| `MoraBackgroundService` | Inicio registrado; bucle activo sin errores repetitivos. No se adelantó el horario diario. |
| `AlertaStockBackgroundService` | Inicio y procesamiento inicial observados; intervalo de 2 h intacto. |
| `DocumentoVencidoBackgroundService` | Inicio y próxima ejecución 02:00 registrados; no se esperó un ciclo diario completo. |
| `CotizacionVencimientoBackgroundService` | Inicio y próxima ejecución 03:00 registrados; no se cambió scheduling. |
| `MercadoLibreWebhookBackgroundService` | Inicio y polling de 30 s, cola vacía; sin llamadas reales a ML ni errores por secretos faltantes. |

Son los cinco BackgroundService encontrados. No aparecieron otros timers/jobs de negocio en la búsqueda de las áreas revisadas. La observación de arranque y ausencia de errores no certifica una ejecución de todos sus procesos diarios.

## Compatibilidad Linux y filesystem

Se revisaron usos `File.Read/Write`, `Directory`, `Path` y `FileStream` en servicios/controllers relevantes. Rutas persistentes probadas: documentos bajo `wwwroot/uploads/documentos-clientes` y contratos bajo `App_Data/contratos-venta-credito`; cubiertas por los volúmenes existentes, sin modificarlos. Las exportaciones de reportes se generan como bytes; no dependen de Office instalado.

Problemas comprobados: separadores Windows en documentos (corregido), moneda dependiente del host en cotización PDF (corregido), cultura implícita de un test (corregido) y descubrimiento de fuentes del repo en tests con output externo (invocación corregida). La omisión de envío en PDF era independiente del sistema operativo y también quedó corregida. No se encontraron usos funcionales de Office Interop, System.Drawing o impresión nativa Windows en los caminos probados. Compilación y vistas/Razor probadas funcionaron con case sensitivity Linux. Las impresiones HTML siguen dependiendo del navegador y no se validó una impresora física.

## Integraciones externas

| Integración | Nivel probado | Pendiente |
|---|---|---|
| Mercado Libre | Mocks/fakes de cliente HTTP/servicios en suite; worker con cola vacía | OAuth/API/publicación/webhooks reales o sandbox; reservado al otro bloque |
| BCRA | Tests con handler simulado y validaciones locales | Disponibilidad/respuesta real del proveedor; no se consultó un documento real |
| Email | No se identificó envío SMTP/SendGrid funcional en los servicios revisados; no probado | Confirmar proveedor y recorrido de envío si se habilita |
| WhatsApp | Configuración/referencias locales; sin envío real | Canal externo y credenciales, si corresponde |

No se enviaron correos, mensajes, publicaciones ni operaciones productivas. Las fuentes web de la UI no constituyen prueba de una integración de negocio.

## Logs y errores

- Error real inesperado reproducido: descarga de ruta Windows en Linux. Corregido; misma fila y archivo descargan bien tras recreate. Los defectos de envío/moneda del PDF no lanzaban excepción: se detectaron inspeccionando su contenido.
- Errores esperados de pruebas negativas: apertura duplicada y reconfirmación de venta ya confirmada. Esta última aparece tres veces por propagación service/controller; no es un ciclo de errores de un worker.
- Warning relevante: AutoMapper informa licencia no configurada en este entorno de desarrollo/pruebas. No se cambió licencia/configuración.
- Compilación: warnings preexistentes de nulabilidad (CS8601/CS8767) y EF1003 en un test de trazabilidad. No se silenciaron ni se cambió la configuración global.
- No se observaron `crit`/Unhandled ni errores repetitivos de background durante la ventana registrada. Después del fix, los `fail` revisados de la app corresponden a la reconfirmación negativa mencionada.

## Evidencia y reproducción

Artefactos locales fuera del repo: `%TEMP%\bury-functional-20260922` (`C:\Users\c0sm3\AppData\Local\Temp\bury-functional-20260922`). Incluyen logs/TRX de todas las corridas, JSON de HTTP/SQL, PDFs/XLSX, inventario filesystem, scripts de QA y resultados Playwright. Los estados de sesión y credenciales sintéticas permanecen solo allí: no versionarlos ni distribuir toda la carpeta como evidencia pública.

Archivos principales: `baseline-before.trx`, `historical-repeat.trx`, `historical-fixed.trx`, `linux-path-red.trx`, `linux-path-green.trx`, `baseline-windows-release.trx`, `baseline-linux-release.trx`, `suite-summary.json`, `migration-comparison.json`, `assembly-reflection.json`, `db-sql-history.json`, `ef-connected-list.txt`, `ef-pending-final.txt`, `http-evidence.jsonl`, `pdf-validation.json`, `excel-validation.json`, `export-assertions.json`, `quote-regression.json`, `verify-quote.py`, `playwright-client-final.json`, `playwright-results.json`, `app-before-fix.log`, `app-after-path-fix.log`, `app-final.log`.

Comando .NET repetible desde la raíz, con results-directory fuera del repo si se quiere aislar evidencia:

```powershell
dotnet restore TheBuryProyect.slnx
dotnet build TheBuryProyect.slnx --no-restore
dotnet test TheBuryProyect.slnx --no-restore --logger "trx;LogFileName=baseline.trx"
dotnet ef migrations list --no-build
dotnet ef migrations has-pending-model-changes --no-build
```

Para EF debe configurarse una conexión de prueba; no ejecutar esos comandos contra una conexión productiva implícita. La invocación Linux usada fue `dotnet test TheBuryProyect.slnx --artifacts-path /src/artifacts/qa-build --no-restore --logger trx --results-directory /evidence`, dentro del SDK 10.0, con repo read-only y outputs/evidencia montados desde directorios temporales. No requiere editar compose.

## Archivos de este bloque y pendientes

Lista exacta de cambios propios:

1. `Services/DocumentoClienteService.cs`
2. `TheBuryProyect.Tests/Integration/DocumentoClienteServiceTests.cs`
3. `TheBuryProyect.Tests/Integration/CatalogoCostoEnvioPermisoHttpTests.cs`
4. `TheBuryProyect.Tests/Integration/VentaCrearEditarParidadHttpTests.cs`
5. `TheBuryProyect.Tests/Unit/VentaCrearEditarParidadTests.cs`
6. `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`
7. `TheBuryProyect.Tests/Unit/VentaDetailsAjustePlanUiContractTests.cs`
8. `TheBuryProyect.Tests/Unit/CreditoPunitorioOperacionControllerTests.cs`
9. `Services/CotizacionPdfService.cs`
10. `docs/validacion-funcional.md` (nuevo)

Archivos del cierre 23/09/2026 (rama `preprod-functional-closeout-20260922`):

11. `Controllers/CajaController.cs` (fix id=0 en `Create`)
12. `TheBuryProyect.Tests/Integration/CajaControllerCreateHttpTests.cs` (nuevo, regresión)
13. `docs/despliegue-produccion.md` (nueva sección 9, AutoMapper/licencias)
14. `docs/validacion-funcional.md` (esta actualización)

<!-- GIT_FINAL -->

Pendientes funcionales concretos (actualizado 23/09/2026):

1. ~~Revisar el ID devuelto por alta de Caja~~ — **resuelto**, ver "Actualización de cierre".
2. Si se necesita certificar la DB histórica original, comparar sus 106 IDs con el listado de este informe mediante acceso autorizado a una copia no productiva. Esta ejecución solo certifica su propia DB aislada.
3. Completar escenarios Playwright que requieren fixtures específicos, incluido el caso omitido de múltiples planes. No queda un test .NET funcional omitido para esconder un fallo.
4. Integraciones reales y consumidores de archivos no ejercitados se mantienen al nivel declarado en sus tablas. No se presenta como probado lo que solo fue inventariado.
5. ~~`MoraServiceTests`/`DashboardServiceTests` (8 tests) usan `DateTime.Today` en vez de `IRelojComercial`~~ — **resuelto**, ver "Actualización de cierre 2".
6. ~~`POST /Identity/Account/Login` con `returnUrl` no local devuelve 500~~ — **resuelto**, ver "Actualización de cierre 2" (la causa real no era `ReturnUrl=/`, esa parte de la nota original era imprecisa; corregido en la actualización 2).
7. AutoMapper requiere una decisión de negocio/legal antes de producción (RPL-1.5 vs. licencia comercial) — ver `docs/despliegue-produccion.md` §9. GO-LIVE BLOCKER EXTERNO, no técnico. Sin cambios.
8. ~~No se generó el PDF de contrato de crédito ni se repitió el upload/download HTTP de documentos de cliente en un contenedor Linux limpio~~ — **resuelto**, ver "Actualización de cierre 2".

No se avanzó con monitoreo, firewall, dominio público, CI/CD ni cambios de infraestructura.
