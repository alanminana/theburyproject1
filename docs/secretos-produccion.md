# Secretos y configuración sensible

Auditoría local del 21 de septiembre de 2026. Proyecto real: ASP.NET Core **net10.0**.

**Veredicto: Requiere ajuste operativo antes de producción.** Los cambios y pruebas de este bloque están terminados, pero la contraseña local de administrador está comprometida: coincide con documentación, historial y un valor público de desarrollo compilado. Rotar esa contraseña en cada cuenta donde se haya reutilizado. No se rotaron credenciales reales ni se reescribió Git.

## Alcance y evidencia

Camino canónico: `docker-compose.yml` → `docker/db-init` → `--migrate` / `DbInitializer` → `Program.cs` → Caddy. Desarrollo: `docker-compose.dev.yml` y LocalDB. `/Diagnostico` es una herramienta de Development; la transcripción `contexto-nuevo-chat-claude.txt` y los informes QA son material histórico, no configuración operativa.

Se revisaron código, configuración, scripts, CI, documentación, user-secrets y 4.741 blobs de texto accesibles desde las referencias Git (860 commits). Se buscaron asignaciones sensibles, claves de proveedores, claves privadas y coincidencias exactas con los secretos locales identificados. Es una búsqueda heurística: no prueba ausencia absoluta de secretos codificados, binarios o commits inalcanzables. No se consultaron datos de clientes, uploads ni backups reales.

No existen `.env`, `appsettings.Development.json` ni `backup-offsite.env` en este checkout. No había contenedores ERP disponibles para auditar logs de un despliegue anterior. Se inspeccionaron 91 logs locales sin encontrar la contraseña local comprometida; eso no certifica logs productivos o de proveedores externos.

Las pruebas reales usaron `bury-secrets-audit-3c8ddd`, volúmenes nuevos, SQL Express, contraseñas aleatorias temporales y puertos de loopback. Caddy usó HTTP local para no emitir certificados ni acceder a DNS externo. La prueba no certifica HTTPS de producción.

## Inventario sin valores

| Secreto/config | Clasificación | Dónde se usa / almacenamiento | Servicios que lo reciben | Estado |
|---|---|---|---|---|
| `MSSQL_SA_PASSWORD` | Secreto | `.env` → entorno SQL / sqlcmd | db, db-init | Obligatorio; nunca app/migrate/Caddy |
| `ERP_DB_PASSWORD` | Secreto | `.env` → `ErpDb__Password` → SqlConnectionStringBuilder | db-init, app | Obligatorio; runtime sin DDL |
| `ERP_MIGRATION_PASSWORD` | Secreto | `.env` → `ErpDb__Password` | db-init, migrate | Obligatorio; usuario distinto de runtime |
| `ADMIN_PASSWORD` / `Admin:Password` | Secreto | Bootstrap temporal; Identity persiste el hash | migrate | Retirable después del primer despliegue |
| `MERCADOLIBRE_CLIENT_SECRET` | Secreto | `.env` / user-secrets → opciones OAuth | app | Opcional si ML está desactivado; obligatorio al definir ClientId |
| Access/refresh tokens ML | Secreto | Base SQL, cifrados mediante Data Protection | app; accesibles indirectamente a administradores de DB/backup | No se registran respuestas del endpoint de tokens |
| Claves Data Protection | Secreto | Volumen `bury-keys`, `/keys` | app; backup-files/restore-files acceden a archivos | XML persistido, no cifrado con un protector externo; archivos 600, UID 1654 |
| Claves privadas TLS/ACME | Secreto | Volumen `caddy-data` | caddy, backup-files/restore-files | No provienen de `.env`; backup de archivos puede incluirlas |
| Contraseñas de usuarios / tokens Identity | Secreto | Hashes en SQL / tokens y cookies protegidos | app; SQL y copias de datos | No usar credenciales públicas de Development en producción |
| `RCLONE_CONFIG_OFFSITE_*` de autenticación | Secreto | `backup-offsite.env` separado | backup-offsite, restore-offsite | No configurado localmente; SFTP/S3/B2/Azure según proveedor |
| `RCLONE_CONFIG_SECURE_*` de cifrado | Secreto | `backup-offsite.env` y custodia externa | backup-offsite, restore-offsite | Requerido para recuperar un remoto crypt si se habilita |
| ConnectionStrings con Password/Pwd | Secreto | No admitidas como cadena base en Production | No deben distribuirse completas | Usar credenciales separadas |
| Cadena base sin password, servidor/base/usuarios SQL, admin email, rutas de backup | Configuración sensible | `.env`, configuración local | Servicios correspondientes | No autentican por sí mismos |
| `ERP_DOMAIN`, redirect URI, ClientId, SiteId, puertos, subred, edición SQL | Configuración pública/operativa | `.env`, Compose | Solo los consumidores declarados | No son contraseñas; una clave de licencia SQL sí sería sensible |
| SMTP/email provider, JWT signing secret, otras API keys | No encontrados en el camino activo | Búsqueda de código/configuración | Ninguno identificado | Identity usa cookies; email del administrador no implica SMTP |
| BCRA | Configuración pública | URL HTTPS del servicio | app | Cliente actual sin autenticación/API key |

Las herramientas de backup local no reciben passwords SQL por entorno, pero **sí acceden a material secreto dentro de los archivos respaldados**, incluidas claves Data Protection y TLS. El procedimiento SQL de backup usa SA dentro de `db`. La estrategia existente no se modificó.

## Filtraciones encontradas

| Ubicación | Tipo | Versionado | Rotación / acción |
|---|---|---|---|
| User-secrets `Admin:Password` | Credencial local reutilizada como valor público de desarrollo | Almacén fuera de Git, pero valor expuesto en otros archivos | **Rotar** en la cuenta real y actualizar el almacén local; cambiar el almacén no cambia el hash Identity |
| `COORDINACION-AGENTES.md`, transcripción y 13 documentos QA listados al final | Misma contraseña | Sí; valor retirado del árbol actual | Eliminar texto no revoca el secreto |
| Historial, por ejemplo commit `4bdd461`, `COORDINACION-AGENTES.md` | Misma contraseña | Sí | Considerarla comprometida; reescritura eventual como trabajo separado |
| Historial de `AGENTS.md`, `.claude/settings.local.json`, `.playwright-mcp/page-2026-04-28T17-25-15-689Z.yml` | Misma contraseña | Historial accesible | Rotar; no se modificaron instrucciones ni historial |
| `Data/DbInitializer.cs`, valores de desarrollo | Credenciales públicas de prueba; una coincide con user-secrets | Sí, también compiladas en DLL | Development solamente. La coincidencia impide certificar que la contraseña local no está en la imagen |
| `Controllers/DiagnosticoController.cs` | Cadena de conexión y nuevas passwords en respuestas | Código versionado | Se retiraron esos valores; TestLogin pasa a POST con antiforgery |
| `Services/MercadoLibreApiClient.cs` | Cuerpo de error externo en logs, posible reflejo de secretos | Código versionado | Logs conservan operación/status; respuesta sensible descartada antes de propagarla |

Los candidatos restantes corresponden a fixtures de tests, sustituciones sqlcmd, asignaciones sin valores o ejemplos. No se identificaron tokens de proveedor reales adicionales con los patrones utilizados.

**HTTP:** Production mantiene el manejador genérico de errores, no activa DeveloperExceptionPage ni logging sensible/detallado de EF. `/Diagnostico` requiere SuperAdmin y además rechaza entornos distintos de Development. Prueba autenticada en Production: 404. También 404 para `/.env`, `/appsettings.json` y `/backup-offsite.env` a través de Caddy con el Host correcto. Readiness devuelve únicamente `Healthy`, sin configuración.

**Imagen:** se inspeccionaron 1.067 archivos en `/app` de la imagen final construida: no hay archivos `.env`, `backup-offsite.env`, backups, dumps, certificados privados, `.git` ni datos reales de uploads/App_Data/keys. El entorno de imagen no incluye secretos. Las contraseñas temporales no aparecen en la DLL. **Sí aparece el valor público de desarrollo coincidente con el admin local**, por lo que no se afirma ausencia de esa contraseña hasta su rotación. Imágenes antiguas/cachés distribuidas no se sanearon.

**Logs:** cero coincidencias de las cuatro contraseñas temporales en `docker compose logs` de db/db-init/migrate/app/caddy; cero asignaciones de credenciales detectadas. Los términos `password`, `secret`, `token`, `Bearer`, `User Id=` y `connection string` se buscaron sin distinguir mayúsculas: los avisos de claves Data Protection no contienen su material criptográfico. Los contenedores de herramientas se probaron sin ejecutar backups ni contactar un proveedor; no se certifican logs offsite reales.

## `.env`, permisos y variables

- En Git: **no** (`.env` no existe localmente y no figura en `git ls-files`). Tampoco figuran `backup-offsite.env`, dumps, claves privadas ni directorios de backups/keys reales.
- En build: excluido, junto con variantes `.env.*`, `*.env`, user-secrets y appsettings de Development. `.dockerignore` excluye también logs, dumps, claves y estado de runtime en subdirectorios.
- Caddy monta únicamente su configuración y sus volúmenes; no monta el repo ni `.env`. ASP.NET sirve `wwwroot`, no la raíz del contenido.
- Propietario recomendado: usuario de despliegue dedicado. Directorio de secretos 700; `.env` y `backup-offsite.env` 600. Con un despliegue operado exclusivamente por root, propietario root. No dar lectura al usuario HTTP. No se cambiaron permisos del host Windows ni de un servidor real.

Ejecutar en el servidor definitivo, con la cuenta de despliegue real:

```sh
chmod 700 /srv/bury-secrets
chmod 600 /srv/bury-secrets/.env /srv/bury-secrets/backup-offsite.env
# Configurar el propietario real mediante chown; no usar nombres de ejemplo sin adaptarlos.
```

Si el archivo permanece junto a Compose, aplicar 600 allí. `backup-offsite.env` se resuelve relativo al Compose actual; moverlo requiere ajustar esa ruta explícitamente. No se cambió su ubicación.

| Grupo | Variables |
|---|---|
| Obligatorias Compose | `ERP_DOMAIN`, `MSSQL_SA_PASSWORD`, `ERP_DB_USER`, `ERP_DB_PASSWORD`, `ERP_MIGRATION_USER`, `ERP_MIGRATION_PASSWORD`, `ADMIN_EMAIL` |
| Primer bootstrap | `ADMIN_PASSWORD`; usuario configurado por `ADMIN_USERNAME` (default admin) |
| Obligatorias app Production | Cadena base, `ErpDb__User`, `ErpDb__Password` |
| Opcionales integración | `MERCADOLIBRE_*`; al definir ClientId debe haber un ClientSecret no vacío/no provisional |
| Configuración operativa | `MSSQL_DATABASE`, `MSSQL_PID`, `BURY_NET_SUBNET`; elegir explícitamente la edición válida para producción |
| Solo Development | `APP_PORT`, `MSSQL_HOST_PORT`, user-secrets, overlay Development; este overlay crea usuarios de prueba |
| Solo backup | `BACKUP_*`, credenciales `RCLONE_CONFIG_*` en archivo separado |

### Caracteres e interpolación

Probados realmente en SQL SA, runtime, migraciones y login admin: `$`, `'`, `"`, `;`, `#`, `\` y la secuencia `$(...)`. Sin restricciones artificiales de caracteres. En dotenv, usar comillas simples y escapar una comilla simple interna como `\'`. No ejecutar `source .env`: dotenv y shell no son equivalentes. No se probaron NUL ni passwords multilínea; tampoco son una recomendación operativa.

Compose interpola `.env` al construir el modelo y solo entrega al contenedor las claves declaradas. `$$` en el YAML conserva `$` para el shell del contenedor. La salida de `config` vuelve a escapar dólares para poder reutilizarse; se comprobó la llegada de los valores reales mediante autenticación y entorno del contenedor. Ver [interpolación oficial](https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/).

Evitar en terminal grabada/CI: `docker compose config`, `config --environment`, `docker inspect` sin filtro, `exec ... env`, `dotnet user-secrets list`, `set -x`, dump de configuración, logs HTTP de cuerpos/Authorization. `config` **sí contiene las credenciales resueltas**. Para validar sintaxis: `docker compose config --quiet`. Para inventariar, procesar la salida en memoria e imprimir solo nombres/presencia, como en esta auditoría. No pasar passwords como argumentos de `sqlcmd -P` en procedimientos manuales; usar `SQLCMDPASSWORD` sin imprimirla. El healthcheck existente de SQL aún usa `-P` dentro del contenedor: queda como exposición a inspección de procesos por administradores del host; no se alteró el healthcheck fuera del alcance autorizado.

## Separación por servicio comprobada

| Servicio | Secretos disponibles | Correcto |
|---|---|---|
| db | SA | Sí |
| db-init | SA, runtime, migración | Sí; crea/sincroniza ambos logins |
| migrate | Migración, admin inicial en primer despliegue | Sí; sin SA ni password runtime |
| app | Runtime; ClientSecret ML si se configura; claves y tokens por volumen/DB | Sí; sin SA, migración, bootstrap ni offsite |
| caddy | Claves TLS/ACME propias; ninguna password SQL | Sí |
| backup-files / restore-files | Archivos con secretos respaldados; sin credenciales por entorno | Sí |
| backup-offsite / restore-offsite | Solo configuración/credenciales del proveedor cuando existan | Sí; entorno sin secretos en esta prueba sin proveedor |

db-init y migrate terminan como one-shot; se utilizó inspección filtrada de contenedores detenidos como equivalente de `exec env`. Docker conserva el entorno de los contenedores detenidos: eliminarlos una vez comprobado el despliegue, especialmente `migrate` tras retirar el bootstrap. Quien administra Docker puede leer entornos y volúmenes; `.env` no es un vault.

## Docker Secrets: mantener `.env`

Se mantiene `.env` en este bloque, con claves por servicio y archivo offsite separado. Es una decisión para el stack de un servidor actual, no una equivalencia de protección con un vault.

La app no lee `_FILE` ni usa AddKeyPerFile. Compose monta secretos como archivos, pero **no convierte automáticamente `_FILE` en una contraseña**; es una convención de cada imagen. Ver [Docker Compose secrets](https://docs.docker.com/compose/how-tos/use-secrets/).

La imagen SQL 2022 instalada, digest `sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090`, se probó en otro contenedor vacío con solo `MSSQL_SA_PASSWORD_FILE`: SQL llegó a iniciar, pero la autenticación con el contenido del archivo falló. No se considera soporte válido. Microsoft documenta `MSSQL_SA_PASSWORD`, no esa variable `_FILE`: [variables SQL Linux](https://learn.microsoft.com/en-us/sql/linux/configure/environment-variables?view=sql-server-ver17).

Una migración posterior necesitaría: lector pequeño de archivo en app/db-init (archivo prioritario, sin log, error cerrado si falta/no puede leerse), montajes selectivos y permisos compatibles con UID 1654. Para SQL, un wrapper que exporte la password antes de ejecutar el entrypoint conserva la exposición del entorno del proceso; también exigiría adaptar consumidores existentes del secreto. No se introdujo un wrapper frágil ni se tocaron backups. Compose local usa archivos montados; no ofrece por sí solo la custodia cifrada de Swarm/Vault.

## Fail-fast y bootstrap

- App Production: ausencia, vacío o `CHANGE_ME` en credenciales obligatorias produce salida no cero antes de abrir Kestrel. Rechaza `sa`, cadenas base con password/autenticación integrada y formatos inválidos sin reflejar la cadena en la excepción. Development/Testing conservan LocalDB.
- db-init: variables requeridas ausentes/vacías fallan; marcadores `CHANGE_ME` en SA/usuarios/passwords también. Compose ya rechaza ausentes/vacíos antes de crear servicios.
- SQL: SA vacío/ausente se bloquea por Compose; el placeholder no cumple la política SQL y el stack no queda sano. No se añadió un wrapper de arranque al servidor.
- Primer admin Production: sin password o con placeholder falla el bootstrap; un error de creación Identity también falla. Se verificó una base nueva sin contraseña: `migrate` salió con error.
- Admin existente: `migrate` funciona con password bootstrap vacía. No cambia su contraseña. Se probó realmente.
- `ERP_DOMAIN=CHANGE_ME` sigue siendo sintácticamente no vacío para Compose; sustituirlo por el dominio real. Es configuración pública, no validación de secreto implementada aquí.

Después del primer login: cambiar contraseña desde Identity, comprobar un segundo login, retirar `ADMIN_PASSWORD` de `.env` y del gestor de despliegue, y ejecutar `docker compose rm -f migrate`. Conservar `ADMIN_EMAIL`/`ADMIN_USERNAME` coherentes con el usuario existente. El proyecto no fuerza todavía un cambio de contraseña en la UI; es un paso operativo explícito. No quitar el bootstrap antes de verificar el acceso administrativo.

## Rotación sin pérdida de datos

| Credencial | Procedimiento |
|---|---|
| SQL runtime | Ventana breve de mantenimiento; detener app, guardar nueva password en `.env` y custodia externa, ejecutar `docker compose run --rm db-init`, recrear app y verificar readiness/login. db-init sincroniza los dos logins con `.env`: mantener la credencial de migración correcta. No borrar volúmenes. |
| Migración | Cambiar su password en `.env`/custodia, ejecutar db-init, ejecutar migrate y verificar salida 0. Eliminar contenedores one-shot viejos. No afecta el esquema por sí mismo. |
| SA | Mantener sesión administrativa abierta; cambiar password del login mediante conexión segura, actualizar `.env`, recrear db y verificar health/db-init. **Cambiar solo `.env` no rota el login en un volumen existente.** No ejecutar `down -v`. Coordinar el corte porque healthcheck y herramientas usan la nueva variable. |
| Admin Identity | Cambiar/resetear en Identity mediante flujo autenticado; comprobar login; actualizar user-secrets si es una cuenta de desarrollo. `ADMIN_PASSWORD` es bootstrap y no resetea usuarios existentes. Revocar sesiones si corresponde al incidente. |
| OAuth/API | Revocar/rotar en el proveedor, actualizar ClientSecret y recrear app; reconectar la cuenta si invalida refresh tokens. No borrar las claves Data Protection para rotar el client secret. |
| Offsite | Crear credencial nueva en proveedor, actualizar solo `backup-offsite.env`, verificar lectura/escritura con las herramientas existentes y revocar la anterior después. La password de cifrado crypt exige un plan propio: no cambiarla como si fuese una clave de acceso, porque perdería acceso a archivos anteriores. |

No se ejecutó ninguna rotación real. Las passwords temporales solo existieron en el entorno aislado.

## Recuperación del servidor y CI

Guardar por separado, fuera del servidor, una copia controlada de `.env`, `backup-offsite.env`, contraseñas de cifrado offsite, instrucciones y fechas de rotación en un gestor de contraseñas/vault o archivo cifrado con copia offline. Custodiar la clave de descifrado en otro lugar accesible a responsables autorizados y verificar periódicamente la recuperación. No incorporar esos archivos al backup ERP actual. Las claves Data Protection y el estado TLS ya forman parte del diseño de backup de archivos existente; son necesarios para recuperar tokens/cookies y deben protegerse como los datos SQL.

CI actual compila y prueba sin secretos productivos. Un despliegue futuro necesitará autenticación al servidor/registro y acceso controlado al secret store; SQL/SA/OAuth no deben entregarse al job de build/test. Inyectar en el despliegue solo las claves que éste necesite y evitar que salidas, trazas y artefactos las conserven. No se implementó ni modificó CI/CD en esta tarea.

## Pruebas ejecutadas

1. Build Docker de la imagen final con net10.0: correcto.
2. `dotnet test ... --filter 'FullyQualifiedName~ProductionSecretsTests|FullyQualifiedName~MercadoLibreApiClientTests|FullyQualifiedName~MercadoLibrePublicacionServiceTests' --no-restore --verbosity quiet`: **45/45**. Avisos preexistentes de nulabilidad y un EF1003 en otro archivo de tests; sin fallos.
3. Stack aislado completo db → db-init → migrate → app → caddy: arranque correcto; db/app healthy, one-shot salida 0.
4. Login Identity real vía Caddy con password temporal especial: cookie autenticada emitida (pantalla de aceptación inicial de términos). `/Diagnostico` autenticado: 404.
5. Readiness y rutas sensibles HTTP verificadas; passwords especiales aceptadas por SQL/SqlClient/Identity.
6. Inspección real de entornos, incluidos one-shot detenidos y cuatro servicios de herramientas: sin secretos inesperados.
7. Consulta SQL como runtime: sysadmin=0, db_owner=0, db_ddladmin=0, db_datareader=1, db_datawriter=1.
8. App/db-init con password ausente, vacía y provisional: seis rechazos correctos sin imprimir su valor.
9. Migración sin bootstrap con admin existente: salida 0; base nueva sin admin/password: rechazo explícito.
10. Imagen final y entorno de imagen inspeccionados; escaneo de logs y Git descrito arriba.
11. Prueba separada de `_FILE` en la imagen SQL instalada: no autentica con el contenido del archivo.
12. Contexto Docker se?uelo con reglas reales y 18 archivos sensibles ficticios: todos excluidos; control p?blico incluido. `git check-ignore` confirma exclusiones y permite las plantillas `.example`.
13. Limpieza Docker terminada: contenedores, vol?menes e imagen propios eliminados; otros procesos/contenedores intactos.
14. `git diff --check`: sin errores tras retirar un espacio final en una l?nea documental redactada.

No se ejecutaron backup/restore, rotaciones productivas, llamadas OAuth reales ni pruebas de un proveedor offsite. No se modificaron migraciones de esquema, permisos SQL, healthchecks, recursos, políticas de restart ni configuración TLS productiva.

## Archivos modificados por este bloque

- `.gitignore`, `.dockerignore`: exclusiones de secretos, variantes locales y dumps.
- `.env.example`, `docker-compose.yml`: bootstrap temporal y uso seguro de dotenv.
- `Helpers/ProductionSecrets.cs`, `Program.cs`: fail-fast y cadena SQL segura.
- `docker/db-init/init-db.sh`: rechazo de placeholders sin imprimir valores.
- `Data/DbInitializer.cs`: validar primer bootstrap y permitir quitar su password después.
- `Controllers/DiagnosticoController.cs`: retirar secretos de respuestas y password por GET.
- `Services/MercadoLibreApiClient.cs`: no registrar cuerpos externos; descartar respuestas sensibles preservando errores de validación normales.
- `TheBuryProyect.Tests/Unit/ProductionSecretsTests.cs`, `TheBuryProyect.Tests/Unit/MercadoLibre/MercadoLibreApiClientTests.cs`: regresiones focalizadas.
- `docs/despliegue-produccion.md`, `docs/secretos-produccion.md`: procedimiento y evidencia.
- Redacción de la misma contraseña expuesta, sin cambiar el resto del contenido: `COORDINACION-AGENTES.md`, `contexto-nuevo-chat-claude.txt`, `docs/cotiz-qa-3-conversion-e2e-cotizacion-venta.md`, `docs/credito-fase-12-configuracion-mora.md`, `docs/kira-ventas-modal-rework-1b-css-wizard.md`, `docs/kira-ventas-modal-rework-1c-js-wizard.md`, `docs/ui-4g-fix-runtime-nav-activo-dashboard.md`, `docs/ui-5a-normalizacion-global-iconos.md`, `docs/ui-5e-deudas-venta-caja-dinamicas.md`, `docs/ui-5e-qa-playwright-sidebar.md`, `docs/ui-5g-limpieza-toasts-dinamicos.md`, `docs/ui-5i-toasts-js-restantes.md`, `docs/ui-5j-auditoria-catalogo-proveedor-js.md`, `docs/ui-5k-seguridad-frontend-proveedor-renderproductos.md`, `docs/ventas-ux-smoke-venta-create.md`.

Los scripts/resultados auxiliares de esta ejecución están en `artifacts/secrets-audit/` (ignorado). No versionar archivos temporales de prueba.

## Pendientes reales

1. Rotar la contraseña local comprometida en todas las cuentas donde se reutilizó; no alcanza con cambiar user-secrets. Determinar si llegó a producción. No compartir el valor para hacerlo.
2. Validar `.env` y permisos en el servidor definitivo: no existe archivo productivo accesible en este checkout.
3. Cuando haya proveedor offsite, verificar credenciales/permisos/logs reales sin divulgar valores; conservar aparte la clave de recuperación.
4. Decidir separadamente si se reescribe el historial y se retiran imágenes/cachés antiguas; la rotación es prioritaria.


## Working tree y staging

Sin commit, push ni staging. Se preservaron cambios previos en Docker/net10, CI, tests, documentaci?n y `Controllers/CajaController.cs`. Algunos archivos de este bloque ya estaban modificados o sin seguimiento al comenzar; el diff respecto de HEAD incluye ese trabajo anterior. Revisarlo antes de agregar. Los archivos previos del stack deben integrarse coherentemente; este comando no agrega por s? solo todos esos prerrequisitos.

Comando exacto para agregar los archivos tocados por este bloque (no ejecutado):

```powershell
git add -- ".dockerignore" `
  ".gitignore" `
  ".env.example" `
  "docker-compose.yml" `
  "docker/db-init/init-db.sh" `
  "Helpers/ProductionSecrets.cs" `
  "Program.cs" `
  "Data/DbInitializer.cs" `
  "Controllers/DiagnosticoController.cs" `
  "Services/MercadoLibreApiClient.cs" `
  "TheBuryProyect.Tests/Unit/ProductionSecretsTests.cs" `
  "TheBuryProyect.Tests/Unit/MercadoLibre/MercadoLibreApiClientTests.cs" `
  "docs/despliegue-produccion.md" `
  "docs/secretos-produccion.md" `
  "COORDINACION-AGENTES.md" `
  "contexto-nuevo-chat-claude.txt" `
  "docs/cotiz-qa-3-conversion-e2e-cotizacion-venta.md" `
  "docs/credito-fase-12-configuracion-mora.md" `
  "docs/kira-ventas-modal-rework-1b-css-wizard.md" `
  "docs/kira-ventas-modal-rework-1c-js-wizard.md" `
  "docs/ui-4g-fix-runtime-nav-activo-dashboard.md" `
  "docs/ui-5a-normalizacion-global-iconos.md" `
  "docs/ui-5e-deudas-venta-caja-dinamicas.md" `
  "docs/ui-5e-qa-playwright-sidebar.md" `
  "docs/ui-5g-limpieza-toasts-dinamicos.md" `
  "docs/ui-5i-toasts-js-restantes.md" `
  "docs/ui-5j-auditoria-catalogo-proveedor-js.md" `
  "docs/ui-5k-seguridad-frontend-proveedor-renderproductos.md" `
  "docs/ventas-ux-smoke-venta-create.md"
```

## Limpieza local rechazada por la herramienta

La revisi?n autom?tica rechaz? tanto el borrado recursivo de los directorios temporales como el intento m?s limitado de borrar ?nicamente siete archivos concretos. Motivo devuelto: `blocked by policy`, sin otra explicaci?n. No se insisti? con otros mecanismos.

Quedan fuera del repositorio dos directorios creados por esta ejecuci?n:

- `C:/Users/c0sm3/AppData/Local/Temp/bury-secrets-audit-b1ey2wxl`
- `C:/Users/c0sm3/AppData/Local/Temp/bury-secrets-audit-v6n5mcdk`

Contienen configuraci?n y contrase?as exclusivamente temporales del stack ya eliminado. No son credenciales productivas ni se reutilizaron. El usuario puede borrar esos dos directorios tras revisar este informe. Los contenedores, vol?menes y la imagen temporal s? se eliminaron correctamente.
