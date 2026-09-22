# CI / quality gates

Este documento describe el workflow `.github/workflows/ci.yml`: que valida, que bloquea el merge,
como reproducirlo en local y como interpretar un fallo. **No incluye despliegue automatico** (eso
es un bloque separado, pendiente).

Tarea relacionada pero **completamente separada**: hay una validacion funcional en curso (tests,
`Services/DocumentoClienteService.cs`, `Services/CotizacionPdfService.cs`, `docs/validacion-funcional.md`)
con cambios sin commit. Este bloque de CI no toco ninguno de esos archivos.

## CI antes de este cambio

| Workflow | Trigger | Restore | Build | Test | Docker | Seguridad |
|---|---|---|---|---|---|---|
| `ci.yml` | `push` a `main`, `pull_request` | `dotnet restore` del csproj de tests | `dotnet build -c Release` | `dotnet test` + TRX como artifact | No | No |
| `playwright.yml` | `push`/`pull_request` a `main`/`master` | `npm ci` | - | `npx playwright test` (E2E navegador) | No | No |

`ci.yml` ya tenia, sin commitear, un cambio previo de otra tarea (`dotnet-version: '8.0.x'` →
`'10.0.x'`) que se preservo y quedo subsumido por el cambio a `global-json-file` (ver mas abajo).
No habia: cache de NuGet, Docker build, validacion de Compose, EF migrations check, secret
scanning, ni auditoria de dependencias.

## CI final

```text
push a main / pull_request
        │
        ├── build-test (ubuntu) ─── restore → build (Release) → test (TRX) → EF pending-model-changes → auditoria .NET
        ├── docker-build (ubuntu) ── docker build (sin push) → inspeccion de archivos → Trivy (imagen)
        ├── compose-validate (ubuntu) ── docker compose config --quiet (normal / dev / tools / monitoring)
        ├── secret-scan (ubuntu) ── gitleaks sobre los commits NUEVOS del push/PR
        └── build-test-windows (windows, informativo, no bloquea) ── restore → build → test
```

Los cinco jobs corren en paralelo (no hay dependencias `needs` entre ellos): ningun gate espera a
otro innecesariamente.

## Jobs

| Job | Objetivo | Bloquea merge |
|---|---|---|
| `build-test` | Restore + build Release + tests + chequeo de migraciones EF + auditoria de dependencias .NET | Si |
| `docker-build` | Construye la imagen del `Dockerfile` existente, inspecciona su contenido y la escanea con Trivy | Si |
| `compose-validate` | Valida sintaxis de `docker-compose.yml` (+ overlay dev, profile `tools`, monitoring) | Si |
| `secret-scan` | Detecta secretos nuevos en los commits del push/PR (no re-escanea todo el historial) | Si |
| `build-test-windows` | Paridad informativa en Windows | No (`continue-on-error: true`) |

## .NET / SDK

- `global.json` fija `10.0.401` con `rollForward: latestPatch`.
- El workflow usa `actions/setup-dotnet@v4` con `global-json-file: global.json` (no una version
  generica como `'10.0.x'`): el SDK que se instala siempre es el que exige `global.json`, sin
  drift silencioso.
- Un paso `dotnet --info` deja registrada la version efectiva en el log de cada corrida.
- Verificado en local: `dotnet --info` reporta `10.0.401`, coincide con `global.json`.

## Tests

- Comando: `dotnet test TheBuryProyect.Tests/TheBuryProyect.Tests.csproj -c Release --no-build --logger "trx;LogFileName=test-results.trx" --results-directory ./TestResults`
- Release: si (build previo en Release, `--no-build` en el test para no recompilar).
- TRX: si, subido como artifact `test-results` (14 dias de retencion).
- Gate real: el **exit code** de `dotnet test` (falla si `Failed > 0`). El resumen Total/Passed/Failed/Skipped
  en `GITHUB_STEP_SUMMARY` se parsea del TRX (`Skipped = Total - Passed - Failed`, mas robusto que
  depender de un atributo de contador que no siempre viene poblado) y es solo informativo, no un
  segundo gate con un numero fijo.
- Resultado local (2026-09-22, antes de estos cambios de CI, sin tocar tests): **4965 total, 4963
  passed, 0 failed, 2 skipped**, ~2m46s. Estos numeros **no** se hardcodean en el workflow; crecen
  con el tiempo. Los 2 skipped son `ClienteAptitudPunitorioE2ESeedRunner.Limpiar`/`.Sembrar`
  (runners de seed E2E, se saltan intencionalmente fuera de un entorno E2E real).
- No se atribuye a esta tarea ninguna correccion de tests: la suite se ejecuto tal cual estaba,
  incluyendo los cambios sin commitear de la tarea de validacion funcional concurrente.

## Docker

- Build: `docker/build-push-action@v7` sobre el `Dockerfile` **existente**, sin modificarlo.
- Tag CI: `theburyproject/erp:ci-<sha>`.
- Push: no. `push: false`, `load: true` (la imagen queda solo en el runner para inspeccionarla).
- Secretos de build: ninguno. El `Dockerfile` no requiere ningun build-arg — `dotnet publish` no
  toca la base de datos (las migraciones las aplica el servicio `migrate` en runtime, no el build).
  Verificado: `docker build .` completa sin pasar ningun `--build-arg`.
- Inspeccion de imagen: se exporta el filesystem (`docker export | tar -tf -`, sin ejecutar el
  entrypoint de la app) y se busca `.env`, `*.bak`, `.git`, `backup-offsite.env`, `secrets.json`,
  `appsettings.Development.json`. Defensa adicional, no reemplaza el secret scanning del repo.
- Scan de imagen: Trivy `0.36.0` (version fijada, no `latest`), dos pasadas:
  - Reporte completo (`CRITICAL,HIGH`, OS + libreria), no bloqueante — visibilidad total.
  - Gate: solo `CRITICAL` en paquetes de aplicacion (.NET/NuGet), `ignore-unfixed: true` (no
    bloquea por CVEs sin fix disponible). Los hallazgos de paquetes de SO **no bloquean** en este
    bloque porque no se modifica el `Dockerfile`/imagen base para resolverlos (eso implicaria
    tocar el Dockerfile, fuera de alcance). Ver "Pendientes".

## Compose

- Archivos validados: `docker-compose.yml`, `docker-compose.yml + docker-compose.dev.yml`,
  `docker-compose.yml` con `--profile tools`, `docker-compose.monitoring.yml` (standalone, sin
  secretos del ERP).
- Variables sinteticas: `.env` temporal en `$RUNNER_TEMP` generado desde `.env.example`
  reemplazando `CHANGE_ME` por `CI_SYNTHETIC_VALUE_DO_NOT_USE_1234`; se borra al final del job
  (`if: always()`), nunca se commitea ni se sube como artifact.
- `docker compose config` corre con `--quiet` (valida y no vuelca la configuracion resuelta a los
  logs, evitando exponer variables interpoladas).
- Resultado local: las 4 validaciones pasan (`OK base`, `OK dev`, `OK tools`, `OK monitoring`).

## EF

- Comando: `dotnet ef migrations has-pending-model-changes --project TheBuryProyect.csproj` (EF
  Core 10 / `dotnet-ef` 10.0.12, instalado como global tool pinneado a esa version en el job).
- No requiere SQL Server real: compara el modelo compilado contra el snapshot de la ultima
  migracion.
- Estado local verificado: **"No changes have been made to the model since the last migration."**
  (0 pending). El numero de migraciones existentes (~106) no se usa como condicion del gate; el
  gate es la alineacion modelo/migraciones, no un conteo.

## Secret scanning

- Herramienta: gitleaks `v8.30.1`, binario oficial descargado por version fija desde GitHub
  Releases (sin `curl | bash`; se descarga el tarball, se extrae y se ejecuta el binario
  directamente).
- Alcance: **solo los commits nuevos** de este push/PR —
  `gitleaks git --log-opts="<base>..<head>"` (PR: base/head del PR; push: `before..sha`, con
  fallback a `sha~1..sha` si `before` es todo-ceros, primer push de una rama).
- Historico conocido: hay una contrasena de administrador de desarrollo expuesta historicamente en
  el repo (detallado en `docs/secretos-produccion.md`, commit `4bdd461` entre otros). **No se
  reescribio el historial y no se muestra el valor en ningun lado.** Al acotar el scan al rango de
  commits nuevos, ese secreto historico queda fuera del rango escaneado y **no bloquea CI**.
  Verificado localmente (gitleaks vía Docker, `--redact`): escanear el commit `4bdd461` en
  aislamiento con las reglas por defecto no lo marca como hallazgo (es una contrasena reutilizada
  con formato generico, no un token/API-key/clave privada reconocible por patron) — es una
  limitacion conocida de la deteccion por patrones, no del alcance del workflow. La rotacion de esa
  credencial sigue siendo una tarea operativa separada (ver "Secretos historicos" abajo); CI no la
  resuelve.
- Nuevos leaks: cualquier secreto con formato reconocible (token, API key, clave privada,
  connection string con credenciales embebidas, etc.) introducido en los commits nuevos **bloquea
  CI** (`--exit-code 1`).
- Politica de fallo: bloquear siempre que gitleaks encuentre un hallazgo en el rango de commits
  nuevos. No hay allowlist/baseline con el valor del secreto historico (evita agregarlo en texto
  plano al repo); en su lugar, el scan simplemente no llega a esos commits.

### Secretos historicos (accion pendiente, fuera de CI)

La contrasena de administrador de desarrollo expuesta historicamente sigue pendiente de
**rotacion en cada cuenta donde se haya reutilizado**. CI no soluciona una credencial ya
comprometida; esto es una accion operativa aparte (ver `docs/secretos-produccion.md`). No se
reescribe el historial de Git como parte de este bloque.

## Dependencias .NET

Auditoria con `dotnet list package --vulnerable --include-transitive --format json` sobre
`TheBuryProyect.csproj` y `TheBuryProyect.Tests/TheBuryProyect.Tests.csproj`, contra el feed de
NuGet configurado. No actualiza ningun paquete.

| Package/advisory | Tipo | Severidad | Gate |
|---|---|---|---|
| (sin hallazgos al momento de este cambio, 2026-09-22) | - | - | - |

Politica: `Critical`/`High` bloquean CI; `Moderate` se registra para revision; `Low` solo se
informa. La tabla real de cada corrida queda en `GITHUB_STEP_SUMMARY` y como artifact
`dotnet-dependency-audit` (JSON crudo por proyecto). Verificado en local: ambos proyectos, sin
paquetes vulnerables.

## Container scan

- Herramienta: Trivy, action `aquasecurity/trivy-action@0.36.0` (version fijada).
- Findings: se registran todos (`CRITICAL,HIGH`, OS + libreria) en el log del job, sin bloquear.
- Criterio de bloqueo: solo `CRITICAL`, solo paquetes de aplicacion (.NET/NuGet), solo con fix
  disponible (`ignore-unfixed: true`). Paquetes de SO se informan pero no bloquean en este bloque
  (requeriria tocar el `Dockerfile`/imagen base, fuera de alcance). No hay allowlist de
  excepciones definida todavia; si aparece un finding que se decide aceptar, la excepcion debe
  documentarse aqui con motivo y fecha de revision (no como una lista gigante sin contexto).

## Workflow security

- `permissions: contents: read` a nivel de workflow (minimo privilegio; ningun job necesita
  `write`, `packages: write` ni `id-token: write` porque no hay publicacion/deploy en este bloque).
- Actions de terceros usadas (todas con version fijada, no rangos flotantes ni `latest`):

  | Action | Version | Uso |
  |---|---|---|
  | `actions/checkout` | `v4` | Checkout del repo |
  | `actions/setup-dotnet` | `v4` | Instala el SDK segun `global.json` |
  | `actions/cache` | `v4` | Cache de paquetes NuGet |
  | `actions/upload-artifact` | `v4` | Sube TRX y reportes de seguridad |
  | `docker/setup-buildx-action` | `v4` | Builder de Docker Buildx |
  | `docker/build-push-action` | `v7` | Build de la imagen (sin push) |
  | `aquasecurity/trivy-action` | `0.36.0` | Scan de vulnerabilidades de la imagen |

  Todas son oficiales/mantenidas por el proveedor del ecosistema respectivo (GitHub, Docker,
  Aqua Security). `gitleaks` no se usa como Action de terceros: se descarga el binario oficial
  pinneado por version desde GitHub Releases y se ejecuta directamente, evitando una dependencia
  de licencia de la Action oficial.
- Forks: el workflow no usa `pull_request_target` ni requiere secretos productivos para correr
  (todo el bloque — build, test, Docker, Compose, seguridad — usa datos sinteticos o ninguno), asi
  que puede correr igual sobre PRs de forks sin exponer nada.
- Secrets: no se usa ningun secret de GitHub Actions (`secrets.*`) en este workflow.
- Logs: ningun paso imprime `.env` real, passwords, tokens ni connection strings reales (el
  `.env` de Compose es sintetico y se genera/borra dentro del job; `docker compose config` corre
  con `--quiet`). No se usa `set -x` alrededor de datos sensibles.

## Artifacts

| Artifact | Contenido | Retencion | Datos sensibles excluidos |
|---|---|---|---|
| `test-results` | `*.trx` de la suite de tests | 14 dias | No hay datos de runtime/DB en el TRX |
| `dotnet-dependency-audit` | JSON de `dotnet list package --vulnerable` por proyecto | 14 dias | No contiene secretos, solo nombres/versiones de paquetes |

No se sube el `.env` sintetico de Compose, ni la imagen Docker, ni binarios de produccion.

## Required checks recomendados

Nombres exactos de job para configurar como required checks en la proteccion de rama (no se
modifico la configuracion del repositorio; esto es solo la recomendacion documentada):

```text
build-test
docker-build
compose-validate
secret-scan
```

`build-test-windows` **no** deberia ser required check (es informativo, `continue-on-error: true`).

## Ejecucion local

```bash
# .NET
dotnet restore TheBuryProyect.Tests/TheBuryProyect.Tests.csproj
dotnet build TheBuryProyect.Tests/TheBuryProyect.Tests.csproj -c Release --no-restore
dotnet test TheBuryProyect.Tests/TheBuryProyect.Tests.csproj -c Release --no-build \
  --logger "trx;LogFileName=test-results.trx" --results-directory ./TestResults

# EF: pending model changes (requiere `dotnet tool install --global dotnet-ef --version 10.0.12`)
dotnet ef migrations has-pending-model-changes --project TheBuryProyect.csproj

# Auditoria de dependencias
dotnet list TheBuryProyect.csproj package --vulnerable --include-transitive --format json --output-version 1

# Docker build (sin push)
docker build -t theburyproject/erp:ci-local .

# Compose (con .env sintetico, nunca el .env real)
ENV_FILE=$(mktemp)
sed 's/CHANGE_ME/CI_SYNTHETIC_VALUE_DO_NOT_USE_1234/g' .env.example > "$ENV_FILE"
docker compose --env-file "$ENV_FILE" -f docker-compose.yml config --quiet
docker compose --env-file "$ENV_FILE" -f docker-compose.yml -f docker-compose.dev.yml config --quiet
docker compose --env-file "$ENV_FILE" -f docker-compose.yml --profile tools config --quiet
docker compose -f docker-compose.monitoring.yml config --quiet
rm -f "$ENV_FILE"

# Secret scan (gitleaks, solo el ultimo commit como ejemplo)
docker run --rm -v "$PWD:/repo" -w /repo ghcr.io/gitleaks/gitleaks:v8.30.1 \
  git --log-opts="HEAD~1..HEAD" --redact -v --exit-code 1
```

## Como interpretar un fallo

- **`build-test` falla en Build**: error de compilacion real, revisar el log del paso "Build
  (Release)". No deberia fallar por warnings (no se usa `TreatWarningsAsErrors`).
- **`build-test` falla en Test**: hay tests en rojo (`Failed > 0`). Descargar el artifact
  `test-results` para ver el TRX completo. No asumir que es un test nuevo: puede ser preexistente
  o parte del WIP de otra tarea si corre sobre una rama con cambios sin commitear (en un push/PR
  real, el checkout siempre parte de un commit, asi que esto solo aplica a corridas locales).
- **`build-test` falla en "EF Core - pending model changes"**: hay un cambio de modelo (entidad,
  propiedad, indice, etc.) sin su migracion correspondiente. Generar la migracion con
  `dotnet ef migrations add <Nombre>` antes de mergear.
- **`build-test` falla en el gate de vulnerabilidades**: algun paquete tiene un advisory
  `Critical`/`High`. Revisar la tabla en el step summary o el artifact `dotnet-dependency-audit`
  y decidir si se actualiza el paquete (fuera de alcance de este bloque, se hace aparte) o se
  documenta una excepcion explicita.
- **`docker-build` falla en "Inspeccionar contenido de la imagen"**: el build esta incluyendo un
  archivo que no deberia (revisar `.dockerignore`).
- **`docker-build` falla en el gate de Trivy**: hay una vulnerabilidad `CRITICAL` con fix
  disponible en un paquete de aplicacion (.NET/NuGet). El reporte completo (paso anterior, no
  bloqueante) tiene el detalle completo incluyendo SO.
- **`compose-validate` falla**: error real de sintaxis/referencias en algun `docker-compose*.yml`,
  o una variable requerida (`:?`) que no esta cubierta por el `.env` sintetico generado desde
  `.env.example` (si se agrega una variable requerida nueva, agregarla tambien a `.env.example`).
- **`secret-scan` falla**: gitleaks encontro un patron de secreto en alguno de los commits nuevos
  del push/PR. Revisar el log del job (gitleaks reporta archivo/linea con `--redact`, sin imprimir
  el valor); si es un falso positivo evidente, evaluarlo caso por caso, nunca commitear el secreto
  real para "probar" el workflow.

## Archivos modificados

| Archivo | Proposito |
|---|---|
| `.github/workflows/ci.yml` | Workflow de CI ampliado: SDK segun `global.json`, cache NuGet, EF migrations check, auditoria de dependencias .NET, Docker build + inspeccion + Trivy, validacion de Compose con `.env` sintetico, secret scanning de commits nuevos, `permissions`, `concurrency`, timeouts, job informativo de Windows |
| `docs/ci.md` | Este documento |

No se modifico ningun otro archivo. En particular, no se tocaron: codigo del ERP, controllers,
services, views, tests existentes, migraciones, `Program.cs`, `Dockerfile`, `docker-compose*.yml`,
`Caddyfile`, scripts de backup/monitoreo, ni `docs/validacion-funcional.md`.

## Estado

```text
VALIDADO LOCALMENTE:
  - dotnet --info -> SDK 10.0.401 (coincide con global.json)
  - dotnet restore + build -c Release -> 0 errores, 8 warnings preexistentes (no bloquean)
  - dotnet test -> 4965 total, 4963 passed, 0 failed, 2 skipped (~2m46s)
  - dotnet ef migrations has-pending-model-changes -> "No changes have been made to the model
    since the last migration."
  - dotnet list package --vulnerable --include-transitive (ambos proyectos) -> sin hallazgos
  - docker build . -> build exitoso, sin build-args, imagen generada
  - docker compose config --quiet (normal, dev, profile tools, monitoring) -> las 4 validaciones OK
  - gitleaks (binario oficial via Docker, v8.30.1) con --log-opts sobre un rango acotado -> sintaxis
    y comportamiento de "solo commits nuevos" verificados
  - YAML del workflow parseado y validado sintacticamente (PyYAML)

REQUIERE PRIMERA EJECUCION REAL EN GITHUB:
  - Los 5 jobs completos dentro de GitHub Actions (runners, permisos del token, cache real,
    Trivy Action, upload-artifact) no se ejecutaron dentro de GitHub Actions en si; se validaron
    los comandos y la logica localmente/via Docker donde fue posible.
```

## Pendientes (solo CI / quality gates)

- `git diff --check` (chequeo liviano de whitespace/marcadores de conflicto en el diff) no se
  agrego: es opcional segun el alcance pedido y el repo tiene deuda historica de estilo; se deja
  como mejora futura no bloqueante si se decide agregarlo.
- El scan de Trivy no bloquea por vulnerabilidades de paquetes de SO (solo las informa); resolverlas
  implica tocar el `Dockerfile`/imagen base, fuera de alcance de este bloque.
- No hay allowlist/baseline de excepciones de container scan todavia (no hizo falta: sin hallazgos
  Critical al momento de este cambio). Si aparece una excepcion real, documentarla aqui con motivo
  y fecha de revision en vez de crear una lista gigante.
- Tests dependientes de locale/timezone: no se detecto ninguno al ejecutar la suite completa en
  este runner (Windows, cultura `es-*`), pero no se audito exhaustivamente el codigo de tests en
  busca de dependencias implicitas de `CultureInfo.CurrentCulture`/zona horaria. Si aparecen,
  documentarlos para la tarea de validacion funcional (no se tocan tests en este bloque).
- Branch protection / required checks: no se cambio ninguna configuracion de GitHub (Settings del
  repo); la lista de checks recomendados queda documentada arriba para que se active manualmente.
- Conexion con un registry real (build-once-promote-same-image): todavia no hay registry
  configurado. Cuando exista, el paso natural es reemplazar `load: true` por `push: true` con las
  credenciales del registry como secret de GitHub Actions, y promover la misma imagen (por
  digest) a los ambientes siguientes en vez de reconstruirla. Eso pertenece al bloque de deploy,
  no a este.
- Deploy automatico: explicitamente fuera de alcance de este bloque (SSH deploy, `docker compose
  pull` de produccion, restart de servidor, push a registry, migraciones productivas).

## Confirmacion

```text
No modifique codigo funcional, tests existentes, Dockerfile, Compose, Caddy, backups, monitoreo,
VPN ni firewall.
No hice staging, commit ni push.
```
