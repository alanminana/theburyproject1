# Deploy, versionado, migraciones y rollback — TheBuryProject

Runbook operativo para llevar una imagen ya construida a producción de forma controlada, y para
volver atrás de forma segura si algo falla. Complementa (no reemplaza) [`docs/docker-produccion.md`](docker-produccion.md),
[`docs/despliegue-produccion.md`](despliegue-produccion.md) y [`docs/backup-restore.md`](backup-restore.md).

**Estado**: validado en un stack Docker aislado (QA), en esta máquina, con datos sintéticos.
**NO VERIFICADO en el host productivo real.** Este documento y los scripts en `scripts/deploy/` no
despliegan ni tocan VPN/firewall/red; eso queda fuera de este bloque.

No se modificó lógica funcional, tests, migraciones existentes, `Dockerfile`, ningún `docker-compose*.yml`,
`Caddyfile`, `scripts/backup/`, monitoreo, CI, ni archivos de red/VPN. Los únicos archivos nuevos son
`scripts/deploy/*.sh` y este documento.

## 0. Qué hace y qué NO hace este mecanismo

`scripts/deploy/deploy.sh` orquesta, sobre el `docker-compose.yml` ya existente, la secuencia:

```
preflight -> db -> db-init -> backup (si corresponde) -> migrate -> app -> health -> smoke
```

`scripts/deploy/rollback.sh` reemplaza únicamente la imagen de `app` por una anterior ya construida,
**sin volver a ejecutar `migrate`** y sin tocar la base de datos.

No hace (nunca, bajo ningún flag): `docker compose down -v`, `system prune`, `volume prune`,
`image prune -a`, `push`/`pull` de registry, `ssh`, eliminar automáticamente la imagen anterior,
ni "arreglar" un fallo funcional encontrado durante el smoke.

## 1. Estado inicial (cómo se hacía deploy antes de este bloque)

No existía un mecanismo de deploy/rollback versionado. La operación real era manual:
`docker compose up -d --build` con `ERP_IMAGE` sin definir (cae al default `theburyproject/erp:local`,
una etiqueta de conveniencia de desarrollo, no una release), sin gate de backup previo a `migrate`,
sin health gate explícito antes de considerar el deploy exitoso, sin smoke test, y sin ningún
registro de qué imagen quedó corriendo ni cuál era la anterior. Un rollback consistía en volver a
correr `docker compose up -d --build` sobre un commit anterior del código — es decir, **rebuild**, no
reutilización de la imagen ya probada, y sin ninguna verificación de compatibilidad de esquema.

## 2. Auditoría del mecanismo existente (sin modificarlo)

| Área | Estado actual | Riesgo | Cambio necesario |
|---|---|---|---|
| Selección de imagen | `ERP_IMAGE` ya existe en `docker-compose.yml` (`${ERP_IMAGE:-theburyproject/erp:local}`), usada por `migrate` y `app` | Sin `ERP_IMAGE` explícito cae a `:local`; nada impedía usar `:latest` | preflight rechaza `latest`/`local`/`CHANGE_ME`, ver §6 |
| Orden de servicios | `db` → `db-init` (`service_completed_successfully`) → `migrate` (ídem) → `app` (ídem) → `caddy` (`service_healthy`) ya está correctamente encadenado vía `depends_on` | Ninguno: el orden ya es correcto | Ninguno; `deploy.sh` respeta ese mismo orden explícitamente en vez de confiar solo en `depends_on` (ver §hallazgo `wait_container_exit` más abajo) |
| `db-init` | Idempotente, crea DB + logins dedicados (app sin DDL, migrate con DDL), rechaza `CHANGE_ME` | Ninguno relevante para deploy | Ninguno |
| `migrate` | One-shot, exit 0 solo si 0 migraciones pendientes tras aplicar (`Data/DbMigrationRunner.cs`); `app` no arranca si falla (`service_completed_successfully`) | Ninguno propio; el riesgo es que nada fuera de `migrate` *verificaba* ese exit code para decidir si continuar | `deploy.sh` lee el exit code real (ver hallazgo) y aborta sin tocar `app` si `!= 0` |
| Health checks | `/health/live` y `/health/ready` ya existen en `Program.cs`; `HEALTHCHECK` de la imagen ya apunta a `/health/ready` | Nada esperaba activamente esos endpoints antes de declarar éxito | `deploy.sh` agrega un gate explícito con timeout finito (§20) |
| Caddy | Depende de `app` healthy solo para el orden de arranque inicial; en un redeploy de `app` Caddy no se reinicia ni necesita reiniciarse | Ninguno (ver §23) | Ninguno; no se gestiona desde `deploy.sh` |
| Backups | `scripts/backup/backup.sh` ya soporta `full`/`log`/`files`/`all`, con `RESTORE VERIFYONLY` + sha256 | Nada lo invocaba automáticamente antes de una migración | `deploy.sh` invoca `backup.sh full` antes de `migrate` cuando la DB ya tiene datos (§8) |
| Identidad de release | No existía ningún tag más allá de `:local` | Imposible saber qué commit/imagen corre en un momento dado | `release-id` + `docker image inspect` (§3, §5) |
| Rollback | No existía; la práctica implícita era rebuild desde un commit anterior | Rebuild no garantiza bit-a-bit la misma imagen que se probó; ninguna verificación de compatibilidad de esquema | `rollback.sh`, gate explícito de confirmación (§16-19) |

**Hallazgo real encontrado durante esta QA** (no una hipótesis): `docker compose up -d <servicio>`
vuelve en cuanto el contenedor *arranca*, no cuando un servicio *one-shot* (`db-init`, `migrate`)
**termina**. Un primer borrador de `deploy.sh` leía el `ExitCode` inmediatamente después de `up -d` y
lo interpretaba como fallo (el contenedor todavía estaba `running`, sin exit code aún). Se corrigió
agregando `wait_container_exit` (`scripts/deploy/lib.sh`), que hace polling hasta que el contenedor
queda en estado `exited` antes de leer su código de salida. Sin este fix, **todo** deploy se hubiese
reportado como fallo de `migrate` aunque la migración funcionara. Confirmado con evidencia real en
[§ Migraciones](#9-migraciones).

**Segundo hallazgo**: `curl -o /dev/null` ejecutado vía `docker compose exec -T` resultó intermitente
en este host (`curl: (23) Failure writing output to destination`, con el health check a veces
reportando timeout pese a que la app respondía 200 real). Se reemplazó por un patrón que nunca
escribe a `/dev/null` dentro del contenedor (`curl -w '\nHTTPCODE:%{http_code}'` + grep del marcador).
Ver `http_code()`/`wait_health()` en `scripts/deploy/lib.sh`.

## 3. Estrategia de releases

```
tag:       theburyproject/erp:<release-id>
release-id: <fecha UTC AAAAMMDD>-<shortsha git, 7 hex>[-dirty]
```

`scripts/deploy/lib.sh` (`suggest_release_id`) calcula este id automáticamente a partir de
`git rev-parse --short=7 HEAD`. Si el working tree tiene cambios sin commitear, agrega el sufijo
`-dirty` — **no oculta la situación**: en esta QA, con el WIP funcional de Codex sin commitear, el
id calculado fue `20260922-3bb3a79-dirty`. Es una advertencia legítima: en un release real, el
build debería partir de un working tree limpio. `deploy.sh`/`preflight.sh` no fuerzan este formato
(aceptan cualquier tag ya construido), pero rechazan explícitamente `latest`, `local` y cualquier
tag con `CHANGE_ME`.

Nunca se reutiliza un tag para contenido distinto: cada release nuevo obtiene un `release-id` nuevo
(nuevo shortsha, o sufijo manual si el shortsha no cambió — ver §5).

## 4. Build once, deploy same image

Validado con evidencia real (no solo conceptual):

1. Build único: `docker build -t theburyproject/erp:20260922-3bb3a79-dirty .` (usa el `Dockerfile`
   existente, sin modificarlo).
2. `docker image save theburyproject/erp:20260922-3bb3a79-dirty -o erp-image.tar` (178.520.064 bytes,
   2,85 s) → `docker image rm` de la imagen original → `docker image load -i erp-image.tar`.
3. El `Id` recuperado (`sha256:3ab3ceb6...c4855a`) es **idéntico** al original. El `.tar` se generó y
   se borró en un directorio fuera del repo (nunca se commiteó).

Esto demuestra que la imagen puede transportarse sin rebuild. Hasta que exista un registry (§37),
`docker save`/`load` es la alternativa de transporte válida entre el host de build y el host de
destino.

`deploy.sh`/`rollback.sh` nunca reconstruyen: usan `docker compose up -d` (sin `--build`) sobre una
imagen que `preflight.sh` ya verificó que existe localmente bajo el tag exacto pedido.

## 5. Identidad de imagen

`scripts/deploy/lib.sh` (`image_identity`) imprime, para cualquier imagen:

```
image=theburyproject/erp:20260922-3bb3a79-dirty
id=sha256:3ab3ceb6688314203c7d702a799aee6003b2b912ad59fbeddd26277493c4855a
digest=theburyproject/erp@sha256:3ab3ceb6...c4855a   (digest LOCAL, de RepoDigests; sin registry no hay digest remoto)
created=2026-09-22T03:48:06.429836775Z
```

`deploy.sh` registra esta identidad en el log antes de tocar nada, y el `release-id` asociado al
commit queda en el nombre del tag. No se agregó ningún endpoint HTTP para esto (información
puramente operacional vía `docker image inspect`).

## 6. Preflight (`scripts/deploy/preflight.sh`)

Validado con casos reales (imagen inexistente, `CHANGE_ME`, env-file ausente — ver §26-27). Se
ejecuta siempre antes de tocar cualquier contenedor, tanto desde `deploy.sh` como standalone.

| Check | Resultado | Bloquea |
|---|---|---|
| `--image` presente | Verificado | Sí |
| `--image` no es `latest`/`LATEST` | Verificado | Sí |
| `--image` no es `local`/`LOCAL` | Verificado | Sí |
| `--image` no contiene `CHANGE_ME` | Verificado | Sí |
| Imagen existe localmente (`docker image inspect`) | Verificado (probado con imagen inexistente → FAIL correcto) | Sí |
| env-file existe | Verificado (probado con archivo ausente → FAIL correcto) | Sí |
| env-file sin `CHANGE_ME` | Verificado (probado → FAIL correcto, sin imprimir el valor) | Sí |
| Variables obligatorias presentes (`MSSQL_SA_PASSWORD`, `MSSQL_DATABASE`, `ERP_DB_USER`, `ERP_DB_PASSWORD`, `ERP_MIGRATION_USER`, `ERP_MIGRATION_PASSWORD`, `ADMIN_EMAIL`, `ERP_DOMAIN`) | Verificado (solo se chequea presencia, nunca se imprime el valor) | Sí |
| `docker` / `docker compose v2` disponibles | Verificado | Sí |
| `docker compose config --quiet` válido | Verificado | Sí |
| `db` corriendo (con `--require-db-up`, usado en redeploy/rollback) | Verificado | Sí |
| Espacio libre > 5 GiB | Verificado (~214 GiB libres en esta QA) | Sí |
| Deploy/rollback concurrente (lock) | Verificado (§28) | Sí |

Un fallo de preflight ocurre **antes** de tocar `db`, `migrate` o `app` — confirmado: con
`--image does-not-exist`, `deploy.sh` nunca llegó a `dc up -d db` y la `app` real siguió corriendo
sin interrupción.

## 7. Script de deploy (`scripts/deploy/deploy.sh`)

```
scripts/deploy/deploy.sh --image theburyproject/erp:<release-id> \
    [--env-file .env] [--project theburyproject] [--backup auto|skip] \
    [--health-timeout 180] [--skip-smoke]
```

`set -Eeuo pipefail`, exit codes documentados en `lib.sh` (0 ok, 1 preflight, 2 imagen, 3 backup,
4 migrate, 5 health, 6 smoke, 7 lock ocupado, 8 interrumpido, 9 rollback rechazado, 10 prereq/compose).
Lock por directorio (`.deploy-state/<project>/.lock`, con recuperación de lock huérfano), liberado en
un `trap ... EXIT`. No usa `--build`. No hace SSH ni push/pull de registry.

## 8. Backup pre-deploy

`deploy.sh` consulta `sys.tables` de la base objetivo; si ya tiene tablas (release existente, no
bootstrap), invoca `scripts/backup/backup.sh full` **sin modificarlo**, propagando `COMPOSE_PROJECT_NAME`
y `COMPOSE_ENV_FILES` (mecanismo ya soportado por `scripts/backup/lib.sh`) para que respalde el
proyecto/entorno correctos. Si `backup.sh` no sale 0, `deploy.sh` aborta **antes** de `migrate`
(exit 3). Con DB nueva/vacía, no hay nada que respaldar y se lo registra explícitamente.

- **Mecanismo utilizado**: `scripts/backup/backup.sh full` (sin tocar el script).
- **Evidencia**: corrida real sobre la QA → `BACKUP DATABASE` (925.696 bytes) + `RESTORE VERIFYONLY WITH CHECKSUM` OK + `sha256sum` OK, en 2 s.
- **Requisito antes de `migrate`**: exit 0 de `backup.sh full` (o DB vacía).
- **Caveat de este host** (Windows + Git Bash, **no aplica en el host Linux real**): el paso de
  *retención* de `backup.sh` invoca `sh /opt/bury-backup/retention.sh` dentro de un contenedor; Git
  Bash reescribe ese argumento como ruta de Windows antes de llegar a Docker
  (`couldn't find ... C:/Program Files/Git/opt/...`), y `backup.sh` sale con `EX_RETENTION=7` **aunque
  el backup en sí (BACKUP DATABASE + VERIFYONLY + sha256) ya se completó y verificó correctamente**.
  Se intentó un workaround (`MSYS_NO_PATHCONV=1`) pero **empeoró las cosas**: esa misma variable hizo
  que Compose (binario nativo de Windows) interpretara mal `COMPOSE_ENV_FILES` (una ruta POSIX
  `/c/Users/...`), por lo que se revirtió. **No se modificó `scripts/backup/backup.sh`** (prohibido).
  En el host Linux real este problema no existe. Documentado, no implementado (según instrucción).

## 9. Migraciones

`deploy.sh` corre `db-init` (idempotente) y luego `migrate`, espera con `wait_container_exit` a que
el contenedor *termine* (no solo arranque — ver hallazgo en §2), lee su exit code real y además
`grep`ea el log por `[migrate] OK: N migraciones aplicadas, 0 pendientes.` como doble verificación
(`Data/DbMigrationRunner.cs` ya garantiza que solo imprime eso si `GetPendingMigrationsAsync()` da 0).

- **Pending antes** (QA, DB nueva): 106.
- **Exit**: 0.
- **Pending después**: 0 (confirmado por el propio log de `migrate`, no hardcodeado).
- **Redeploy con DB ya migrada**: `migrate` exit 0, log `[migrate] OK: 106 migraciones aplicadas, 0 pendientes.` (mismo número, sin reaplicar nada).
- **Fallo probado** (§12): sí, con evidencia real.
- **Comportamiento ante fallo**: `deploy.sh` sale con exit 4, **no** ejecuta `dc up -d app`, la `app` anterior sigue corriendo sin interrupción (confirmado: contenedor `app` con el release anterior siguió `Up`/`healthy` mientras `migrate` fallaba).

## 10. Base ya migrada (redeploy idempotente)

Probado repetidas veces sobre la misma DB QA (117 tablas tras el primer deploy): `db-init` sale 0 en
cada corrida, `migrate` reporta 106/0 pendientes sin duplicar nada, `AspNetUsers` se mantuvo en 1 fila
en todas las corridas posteriores, datos preservados, `app` termina healthy. Tiempo total de un
redeploy sin cambios: ~20 s.

## 11. Base nueva

Probado en el stack aislado desde volúmenes vacíos (`docker compose -p bury-deploy-qa ... down -v`
previo): `db` sano → `db-init` crea DB + logins → `migrate` aplica 106/106 migraciones y siembra
roles/permisos/admin → `app` healthy. Sin pasos manuales. Tiempo total: ~50 s (dominado por el
arranque de SQL Server, ~14 s, y las 106 migraciones, ~20 s).

## 12. Fallo de migración

Simulado **sin tocar los archivos de migración reales**: se borró la última fila de
`__EFMigrationsHistory` (metadato en la base de datos, no código) para que EF Core reintentara
`AddCotizacionCostoEnvio` (`AddColumn CostoEnvio` sobre `Cotizaciones`), columna que ya existía
físicamente en la tabla. Resultado real:

```
migrate exit=1
SqlException: "Column names in each table must be unique. Column name 'CostoEnvio' in table
'Cotizaciones' is specified more than once." (Error Number: 2705)
deploy.sh exit=4 (EX_MIGRATE)
```

`app` (release anterior) siguió `Up (healthy)` sin ser tocada. No hubo restart loop porque `app`
nunca se recreó. Se restauró la fila de `__EFMigrationsHistory` después de la prueba (misma acción
inversa, solo DML sobre la base QA; no se tocó ningún `.cs` de `Migrations/`).

## 13. Fallo después de migrar (migrate OK, app falla)

Este es el caso crítico: la DB ya cambió. Simulado con una imagen sintética (`scripts/deploy` no la
genera; se construyó solo para esta QA, fuera del repo) que delega `--migrate` a la imagen real
(migraciones OK) pero sale 1 al arrancar como `app`. Resultado real:

```
migrate OK: 106 migraciones aplicadas, 0 pendientes.
dc up -d app  (reemplaza el contenedor anterior — aquí empieza el downtime real)
health gate (live): timeout tras 30-60s configurados
deploy.sh exit=5 (EX_HEALTH)
```

El mensaje de error de `deploy.sh` en este punto es explícito: *"La DB pudo haber cambiado (migrate
ya corrió); ver docs/deploy-rollback.md#fallo-después-de-migrar antes de cualquier rollback de
imagen"*. El contenedor `app` quedó en `Restarting` (loop, por `restart: unless-stopped` ya definido
en `docker-compose.yml`) — el servicio está caído hasta intervención manual. `deploy.sh` **no**
intenta rollback automático: exige `rollback.sh --confirm-compatible` explícito (§16).

## 14. Tipos de cambio de DB

| Categoría | Ejemplo | Rollback de imagen |
|---|---|---|
| **A. Backward-compatible** | Agregar columna `nullable` (ej. la migración real `AddCotizacionCostoEnvio`: `AddColumn<decimal> CostoEnvio nullable: true`) | Seguro: la release anterior ignora la columna nueva |
| **B. Compatibilidad limitada** | Nueva columna `NOT NULL` con backfill, agregar un índice único sobre datos preexistentes | Evaluar caso a caso: puede requerir que la release anterior tolere el nuevo estado |
| **C. Breaking/irreversible** | Eliminar una columna que la versión anterior todavía lee, renombrar destructivamente, transformar datos sin conservar el original | Rollback de imagen **no seguro**: `rollback.sh` no reemplaza este análisis |

`rollback.sh` no puede inferir la categoría automáticamente (necesitaría diffear el modelo EF entre
dos commits) — por diseño, **exige** `--confirm-compatible` explícito del operador tras revisar esta
tabla; sin ese flag, se detiene (§16, §18).

## 15. Política futura de migraciones

Estándar recomendado (documentado, no forzado por ningún script): preferir siempre categoría A. Para
cambios que requieran categoría B/C, usar el patrón **EXPAND → deploy compatible → migrar/backfill →
CONTRACT en una release posterior**, de forma que en todo momento exista una imagen candidata a
rollback compatible con el esquema corriente. No se modificó ninguna migración existente para
imponer esto.

## 16. Rollback de imagen (`scripts/deploy/rollback.sh`)

```
scripts/deploy/rollback.sh --to-image theburyproject/erp:<release-anterior> --confirm-compatible \
    [--env-file .env] [--project theburyproject] [--health-timeout 180]
```

Sin `--to-image`, usa la `previous-image` registrada por el último deploy exitoso
(`.deploy-state/<project>/previous-image`). Detiene `app`, la levanta con `--no-deps` y la imagen
destino (**nunca re-ejecuta `migrate`**), espera el health gate y corre el mismo smoke mínimo.

**Probado en vivo, ciclo real B(rota)→A**:

```
rollback: actual=theburyproject/erp:...-qa-broken-startup -> destino=theburyproject/erp:20260922-3bb3a79-dirty
deteniendo app -> levantando app con imagen destino, sin re-ejecutar migrate
health gate OK (4 s) -> smoke OK
rollback OK (8 s total)
```

Verificado tras el rollback: `/health/live`=200, `/health/ready`=200 (ambos parte del gate),
`/Identity/Account/Login`=200 (login accesible), SQL accesible (`sqlcmd` respondió durante todo el
ciclo), dato testigo presente (§17).

## 17. Dato testigo

Se usó el usuario administrador sembrado por `migrate` (`admin@qa.local`, creado normalmente por el
flujo real de seed, no insertado a mano) como dato reconocible. Verificado presente en
`AspNetUsers` **después** del ciclo completo A → B(rota, migrate OK/app crash) → rollback → A, y la
página de login siguió sirviendo 200. La base de datos nunca fue tocada por `rollback.sh` (no hay
ningún `RESTORE`, `DROP`, ni `ALTER` en ese script): la persistencia del dato testigo es una
consecuencia estructural (rollback.sh solo reemplaza el contenedor `app`), no un efecto casual.

## 18. Rollback incompatible (STOP)

`rollback.sh` sin `--confirm-compatible` **nunca** ejecuta `docker compose up` con la imagen
anterior. Probado en vivo:

```
$ scripts/deploy/rollback.sh --to-image theburyproject/erp:20260922-3bb3a79-dirty \
    --env-file ... --project bury-deploy-qa
STOP: rollback no ejecutado.
[...] verificar en docs/deploy-rollback.md (sección "Tipos de cambio de DB"): A/B/C [...]
exit 9
```

Se bloquea siempre que el operador no pase el flag explícitamente — independientemente de si el
cambio es en realidad compatible o no; la decisión de categoría (§14) queda a cargo del operador,
nunca automatizada.

## 19. DB rollback (distinto de rollback de aplicación)

- **Application rollback** (`B → A` manteniendo la DB): lo hace `rollback.sh`. Es lo único que
  automatiza este bloque.
- **Database recovery** (restore de un backup): usa `scripts/backup/restore-sql.sh` (ya existente,
  no modificado). Implica perder cualquier operación posterior al backup usado — **nunca** se invoca
  automáticamente desde `rollback.sh` ni `deploy.sh`.

Criterio (decisión del operador, no automatizada):

| Situación | Acción recomendada |
|---|---|
| Bug funcional sin problema de esquema | Forward-fix (nueva release) |
| Esquema categoría A, imagen anterior conocida buena | `rollback.sh --confirm-compatible` |
| Esquema categoría B/C, sin pérdida de datos aceptable | Migración correctiva (nueva release, EXPAND/CONTRACT) |
| Corrupción de datos o pérdida aceptable de lo posterior al backup | `restore-sql.sh` (recovery de DB) |

`rollback.sh` **nunca** ejecuta `dotnet ef database update <migración>` ni ningún downgrade de
esquema; solo reemplaza el binario de la aplicación.

## 20. Health gate

`wait_health()` (`scripts/deploy/lib.sh`) hace polling de `/health/live` y luego `/health/ready`
dentro del contenedor `app` (vía `docker compose exec`, no requiere puertos publicados), con timeout
finito configurable (`--health-timeout`, default 180 s). Un contenedor en estado `running` **no**
se interpreta como éxito: se exige 200 explícito de ambos endpoints.

## 21. Health failure

Probado dos veces en vivo (imagen sintética que arranca y sale 1 inmediatamente): `deploy.sh` agotó
el timeout configurado, salió con exit 5, y volcó los últimos logs de `app` como diagnóstico. `app`
quedó en estado `Restarting` — el deploy se reporta `FAILED` (nunca `OK`) y el registro de release
(`releases.log`) queda con `resultado=FALLO etapa=health-live`.

## 22. Smoke post-deploy

`deploy.sh` corre, tras el health gate: `GET /Identity/Account/Login` (esperado 200 — ruta real
descubierta en esta QA, ver hallazgo abajo), `GET /` (informativo, redirige a `/Dashboard` y de ahí
a login si no hay sesión) y `SELECT COUNT(*) FROM AspNetUsers` vía `sqlcmd` (consulta funcional
inocua, de solo lectura). No repite la validación funcional completa ni modifica ningún test.

**Hallazgo real**: el primer borrador de `deploy.sh` probaba `/Account/Login` (convención por
defecto de ASP.NET Identity) y obtuvo 404 real en esta QA — la app usa Identity con Razor Pages y
la ruta real es `/Identity/Account/Login` (confirmado siguiendo el redirect real de `/` → `/Dashboard`
→ `/Identity/Account/Login?ReturnUrl=%2FDashboard`). Corregido en el script antes de dar por válido
ningún smoke.

## 23. Caddy

No modificado. Observado (sin tocarlo): `caddy` depende de `app` `service_healthy` solo para el
*arranque inicial* del stack; en un redeploy de `app` (lo único que tocan `deploy.sh`/`rollback.sh`),
Caddy no se reinicia ni lo necesita — seguirá proxyeando en cuanto el nuevo contenedor `app` esté
`healthy` de nuevo, sin ninguna acción adicional. No se implementó blue/green (fuera de alcance).

## 24. Downtime

**Método de medición**: se derivó de los timestamps (con resolución de segundo) que `deploy.sh` ya
escribe en su propio log en cada etapa, tomando la ventana desde que empieza `--- app ---` (momento
en que se toca el contenedor `app`, único paso que interrumpe el servicio) hasta `health gate OK`.
No se generaron valores inventados; no se hizo un poll HTTP externo de alta resolución (se intentó
un poller cada 0,25 s y resultó contaminado por el mismo bug de `curl -o /dev/null` corregido en
`lib.sh`, así que se descartó ese resultado en vez de reportarlo).

```
sin migración (redeploy release B sobre A, mismo esquema): "--- app ---" 17:35:39 -> "health gate OK" 17:35:48  =>  ~9 s
con migración (106/106 aplicadas antes del paso app):      "--- app ---" 17:27:06 -> "health gate OK" 17:27:11  =>  ~5 s
```

Hallazgo relevante: el tiempo de `migrate` **no** se suma al downtime, porque el contenedor `app`
anterior sigue sirviendo tráfico durante todo `db-init`+`migrate` (confirmado en la prueba de fallo
de migrate: `app` siguió `Up/healthy` mientras `migrate` corría y fallaba). El downtime real está
acotado por el tiempo de recreación del contenedor `app` + el intervalo de polling del health gate
(3 s), no por el tamaño de la migración.

## 25. Idempotencia

La misma imagen (`theburyproject/erp:20260922-3bb3a79-dirty`) se desplegó 4 veces sobre la misma DB
en esta QA (incluyendo una vez inmediatamente después de rechazar un segundo deploy concurrente, ver
§28). En todas: datos conservados, `AspNetUsers` estable en 1 fila, `migrate` reportó 106/0
pendientes sin reaplicar nada, sin duplicar seeds, terminó exit 0 cada vez.

## 26. Imagen inexistente

`preflight.sh --image theburyproject/erp:does-not-exist` → `FAIL` explícito, exit 1, **antes** de
tocar `db`/`db-init`. `deploy.sh` con la misma imagen: mismo resultado, la `app` real (imagen buena)
quedó `Up (healthy)` sin interrupción.

## 27. Env inválido

Probado con datos sintéticos: `ERP_DB_PASSWORD=CHANGE_ME` en el env-file → `preflight.sh` lo detecta
por el escaneo genérico de `CHANGE_ME` (sin imprimir ningún valor del archivo) y falla, exit 1.
Env-file inexistente → falla igual de temprano, exit 1. Ambos casos se detectan en preflight, antes
de `migrate`.

## 28. Deploy concurrente

Probado en vivo: se lanzó un deploy en background y, 1 s después, un segundo deploy sobre el mismo
`--project`. Resultado real:

```
segundo deploy: "FAIL hay un deploy/rollback en curso sobre 'bury-deploy-qa' (pid <N>)" -> preflight FALLO -> exit 1
primero: continuó y terminó exit 0 normalmente
```

Mecanismo: lock por directorio (`mkdir` atómico) en `.deploy-state/<project>/.lock`, con recuperación
automática si el PID dueño del lock ya no existe (lock huérfano).

## 29. Interrupciones

Análisis de qué puede quedar en cada punto (según el diseño del script, con `trap ... INT TERM` que
libera el lock y sale con exit 8):

1. **Antes de `migrate`** (durante preflight/db/db-init): sin efecto sobre datos; el lock se libera; reintentar es seguro.
2. **Durante `migrate`**: el contenedor `migrate` sigue corriendo en Docker aunque el script se interrumpa (el script no lo mata). El script **no** intenta revertir una migración en curso. El operador debe esperar a que el contenedor `migrate` termine por sí solo y revisar su exit code (`docker compose ps -a`) antes de reintentar.
3. **Después de `migrate`, antes de `health gate OK`**: la DB ya pudo haber cambiado y `app` ya se está recreando. Igual que el interrumpido: revisar el estado real de `app` (`docker compose ps`) antes de asumir nada.
4. **Esperando readiness**: igual que el caso 3.

En todos los casos, el lock se libera (confirmado por el `trap ... EXIT`, que corre siempre) y el
script informa el estado con exit 8, sin intentar ninguna acción correctiva automática.

**Nota de validación honesta**: se revisó el código de `trap_interrupt`/`cleanup` (instalado con
`trap ... INT TERM` y `trap ... EXIT`) y se confirmó que el `EXIT` trap libera el lock en **todas**
las rutas de salida ya ejercitadas en esta QA (éxito, fallo de cada gate, rechazo de preflight). El
intento de entregar una señal `SIGINT` real a un `deploy.sh` en ejecución **desde otra invocación del
mismo harness de automatización** no logró interrumpirlo de forma confiable (el proceso siguió hasta
su timeout normal en vez de recibir la señal) — una limitación del entorno de pruebas de esta sesión,
no del script. El mecanismo de liberación de lock vía trap está verificado; la entrega de `SIGINT`
en caliente sobre un `migrate` real en curso queda como **NO PROBADO** con esa metodología concreta.

## 30. Logs de diagnóstico

Ante cualquier fallo, `deploy.sh`/`rollback.sh` vuelcan (acotado, `--tail 60`): logs del servicio que
falló (`db-init`, `migrate` o `app`) y el resultado del health check. Nunca imprimen el contenido de
`.env`/env-file, ni ninguna variable de contraseña (`preflight.sh` solo reporta *presencia* de cada
clave obligatoria, nunca su valor).

## 31. Comandos destructivos prohibidos

Auditado el código de `scripts/deploy/*.sh`: no contienen `docker compose down -v`,
`docker system prune`, `docker volume prune`, `docker volume rm`, `docker image prune -a`, ni ningún
`push`/`pull` de registry ni `ssh`. No eliminan automáticamente la imagen anterior (`docker image rm`
no aparece en ninguno de los dos scripts).

## 32. Retención de imágenes

`deploy.sh`/`rollback.sh` mantienen registro textual de `current-image` y `previous-image`
(`.deploy-state/<project>/`), pero **no borran** ninguna imagen — la retención/limpieza de imágenes
Docker queda como una operación separada y consciente del operador (`docker image ls` +
`docker image rm` manual), fuera de este mecanismo.

## 33. Estado one-shot esperado (tras deploy exitoso)

Confirmado en vivo (`docker compose ps -a` tras un deploy OK):

```
db         Up (healthy)
db-init    Exited (0)
migrate    Exited (0)
app        Up (healthy)
```

(`caddy` no se gestiona desde `deploy.sh`; en un host real con Caddy ya corriendo, sigue `Up` sin
cambios.)

## 34. Release record

`scripts/deploy/lib.sh` (`append_release_record`) escribe una línea por intento en
`.deploy-state/<project>/releases.log` (texto plano, sin secretos): timestamp, resultado, etapa (si
falló), imagen, imagen previa, `migrate_exit`, `health`, `smoke`. Ejemplo real de esta QA (9 líneas,
incluye 3 fallos deliberados y sus recuperaciones):

```
timestamp=2026-09-22T17:27:12Z resultado=OK imagen=theburyproject/erp:20260922-3bb3a79-dirty previa=theburyproject/erp:20260922-3bb3a79-dirty migrate_exit=0 health=OK smoke=OK
timestamp=2026-09-22T17:29:02Z resultado=FALLO etapa=migrate imagen=theburyproject/erp:20260922-3bb3a79-dirty previa=... migrate_exit=1
timestamp=2026-09-22T17:31:04Z resultado=FALLO etapa=health-live imagen=theburyproject/erp:...-qa-broken-startup previa=... migrate_exit=0
timestamp=2026-09-22T17:31:39Z resultado=OK etapa=rollback imagen=theburyproject/erp:20260922-3bb3a79-dirty reemplazada=...
```

No requiere una base de datos nueva ni un servicio nuevo: es un archivo de texto append-only.

## 35. Release notes (template)

```
Release:      theburyproject/erp:<release-id>
Commit:       <sha completo>
Image:        <docker image inspect --format '{{.Id}}'>
DB migrations: <N aplicadas> (ver log de `migrate`)
Configuration changes: <variables de entorno nuevas/retiradas, si las hay>
Rollback compatibility: A (backward-compatible) | B (evaluar) | C (no rollback de imagen)
Known issues: <bugs funcionales encontrados en smoke, sin corregir aquí>
```

## 36. Configuración entre releases

Cada release que agregue o retire una variable de entorno debe declararlo en sus release notes bajo
`CONFIG CHANGES`, y actualizar `.env.example` (ya versionado) en el mismo commit del cambio de
código que la introduce — nunca versionar `.env` en sí (ya está en `.gitignore`).

## 37. Registry futuro (solo documentado, no configurado)

```
CI -> build image -> tests -> push exact image/digest a un registry
    -> host de destino hace pull del digest exacto -> deploy.sh --image <digest>
```

Hasta que exista ese registry, `docker image save`/`load` (§4) es el mecanismo de transporte entre
el host de build y el host de destino.

## 38. Escenarios — evidencia

| # | Escenario | Resultado |
|---|---|---|
| 1 | Deploy A desde DB nueva | **VALIDADO EN QA** — 106/106 migraciones, health+smoke OK, ~50s |
| 2 | Redeploy A (idempotente) | **VALIDADO EN QA** — repetido 4 veces, datos preservados, 106/0 pendientes siempre |
| 3 | Deploy B sin migración | **VALIDADO EN QA** — release B (mismo contenido, tag distinto) sobre A, sin diffs de esquema |
| 4 | Deploy B con cambio DB compatible | **NO PROBADO** — requeriría un cambio de modelo/migración real, que cuenta como código funcional; fuera del alcance autorizado de este bloque |
| 5 | Migrate failure | **VALIDADO EN QA** — fila de `__EFMigrationsHistory` borrada (no el archivo de migración), error SQL real, exit 4, app anterior intacta |
| 6 | App startup failure | **VALIDADO EN QA** — imagen sintética de prueba (fuera del repo), exit 5, app queda en Restarting |
| 7 | Readiness failure | **VALIDADO EN QA** (parcial) — mecanismo de `wait_health` verificado contra `/health/ready` real; el mismo camino de código que detecta fallo de `/health/live` (escenario 6) cubre `/health/ready` |
| 8 | Rollback B → A compatible | **VALIDADO EN QA** — 8s, health+smoke OK, dato testigo (admin seed) presente |
| 9 | Imagen inexistente | **VALIDADO EN QA** — preflight FAIL antes de tocar nada |
| 10 | Env inválido | **VALIDADO EN QA** — `CHANGE_ME` y env-file ausente, ambos detectados en preflight |
| 11 | Deploy concurrente | **VALIDADO EN QA** — segundo deploy rechazado por lock, exit 1 |
| 12 | Interrupción controlada | **PARCIAL** — liberación de lock vía trap verificada en todas las rutas de salida ejercitadas; entrega de SIGINT en caliente sobre un `migrate` real **NO PROBADO** (limitación del harness de esta sesión, ver §29) |

## 39. No se arreglaron bugs funcionales

Durante el smoke no se encontró ningún bug funcional nuevo (los endpoints usados — login, health,
conteo de `AspNetUsers` — respondieron según lo esperado una vez corregida la ruta real de login en
el propio script de smoke, que es infraestructura de este bloque, no código de producto). No se tocó
Caja, PDFs, documentos, ventas, tests, controllers ni servicios.

## 40. Troubleshooting rápido

| Síntoma | Causa probable | Acción |
|---|---|---|
| `preflight FALLO` | Ver el log: imagen/env-file/CHANGE_ME/lock | Corregir lo señalado; no forzar |
| `deploy.sh` exit 4 | `migrate` falló | Revisar logs de `migrate` (`docker compose logs migrate`); la app anterior sigue sirviendo, no hay urgencia de rollback |
| `deploy.sh` exit 5 | `app` no respondió `/health/live`\|`/health/ready` | La DB ya pudo cambiar (si `migrate` corrió). Evaluar categoría de cambio (§14) antes de `rollback.sh` |
| `deploy.sh`/`rollback.sh` exit 7 | Lock ocupado | Verificar si hay otro deploy real en curso; si el PID no existe, el lock se autorrecupera en el siguiente intento |
| `rollback.sh` exit 9 | Falta `--confirm-compatible` | Revisar §14, confirmar categoría A, reintentar con el flag |
| Backup falla solo por retención en Windows/Git Bash | Bug conocido de este host (§8), no reproducible en Linux | En el host real (Linux) no debería ocurrir; si ocurre, es un hallazgo nuevo a investigar aparte |

## Checklist de deploy

1. `git status`/`git diff` limpios o WIP conocido y aceptado (el `release-id` marca `-dirty` si no).
2. Imagen ya construida (build once) y disponible localmente bajo el tag exacto.
3. `scripts/deploy/preflight.sh --image <tag> --env-file <.env> --project <proyecto>` → OK.
4. `scripts/deploy/deploy.sh --image <tag> --env-file <.env> --project <proyecto>` (sin `--skip-smoke` en producción).
5. Revisar `releases.log` y el log completo antes de anunciar éxito.
6. Si falló: NO reintentar ciegamente. Diagnosticar (§40) antes de decidir forward-fix o rollback.

## Checklist de rollback

1. Confirmar la imagen destino (`.deploy-state/<project>/previous-image` o un tag explícito conocido-bueno).
2. Clasificar el cambio de esquema entre la release actual y la destino (§14: A/B/C).
3. Solo si es categoría A: `scripts/deploy/rollback.sh --to-image <tag> --confirm-compatible --env-file <.env> --project <proyecto>`.
4. Verificar health + smoke del resultado (el script ya lo hace; revisar el log igual).
5. Si la categoría es B/C: NO usar este script. Evaluar forward-fix, migración correctiva o `scripts/backup/restore-sql.sh`.
6. Documentar el incidente (release notes, §35) incluyendo por qué se hizo rollback.

## Pendientes (solo deploy/rollback)

- Escenario 4 (deploy con cambio de esquema backward-compatible real) — requiere una migración real nueva, fuera del alcance autorizado de este bloque.
- Entrega de `SIGINT` en caliente sobre un `migrate` real en curso — no reproducible de forma confiable con el harness de esta sesión (§29); el mecanismo de liberación de lock vía trap sí está verificado.
- Registry real (§37) — explícitamente fuera de alcance para este bloque.
- Automatizar la clasificación A/B/C (§14) comparando el modelo EF entre commits — hoy es una decisión manual del operador; documentado como mejora futura, no implementado.
- El caveat de retención de `backup.sh` en Windows/Git Bash (§8) no aplica al host Linux real; si se detecta en producción, sería un hallazgo nuevo a investigar por separado (no se tocó `scripts/backup/`).

## Confirmación final

No modifiqué lógica funcional, tests existentes, migraciones existentes, `Dockerfile`, ningún
`docker-compose*.yml`, `Caddyfile`, backups, monitoreo, CI, VPN ni firewall.

No hice staging, commit ni push.
