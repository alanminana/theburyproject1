# Cierre técnico del software — índice de evidencia vigente (2026-11-25)

Documento de entrada. Resume **qué evidencia está vigente** (reproducible hoy sobre `main`) y **qué es histórico** (describe el estado de otro momento o de otra infraestructura y no se debe usar para decidir). El detalle de lo pendiente para operar está en
[`pendientes-software-vs-operacion-20261124.md`](pendientes-software-vs-operacion-20261124.md) (Lista B, 15 requisitos, **no bloquean** este cierre).

Decisiones vigentes: la notebook Ubuntu `192.168.50.223` es **exclusivamente** el servidor de pruebas; el servidor productivo definitivo se define después; la VM `tbp` no se usa; no hay datos comerciales reales.

## 1. Evidencia VIGENTE (reproducible)

| Alcance | Evidencia | Cómo se reproduce |
|---|---|---|
| Instalación limpia, migraciones, identidad de imagen, reinicio completo y corte brusco | [`notebook-laboratorio-independiente.md`](notebook-laboratorio-independiente.md) | `install.sh` + `deploy.sh`; `scripts/qa/instalacion-limpia.sh` |
| Recorridos funcionales integrales (compras, stock, caja, cotización → venta → factura, devoluciones y NC, crédito, cierre de caja, reportes, permisos): 131/131 | idem | `bash scripts/qa/p07/run-p07.sh` |
| Fiscal V2 sobre base nueva: 37/37 | idem; `docs/p02-procedimiento-activacion.md` | `scripts/qa/p02/` |
| Respaldo y recuperación con datos conocidos (141 tablas, documentos, FK, casos negativos): 47/47; recuperación estricta que se detiene ante cualquier fallo | `notebook-laboratorio-independiente.md`; `backup-restore.md` §10 | `bash scripts/qa/p07/run-dr-datos.sh --image <imagen>`; `scripts/backup/recover.sh` |
| Interfaz sobre Docker + Caddy + HTTPS en Chromium/Firefox/WebKit (axe, WSS de SignalR, teclado, modales, zoom, móvil, reduced-motion, forced-colors, impresión, cierre de pantallas) | [`qa-ronda-interfaz-20261124.md`](qa-ronda-interfaz-20261124.md); [`ui/UI-REFACTOR-STATUS.md`](ui/UI-REFACTOR-STATUS.md) | `bash scripts/qa/ui-stack/run-ui-stack.sh --out <dir>` |
| Retención de respaldos: un `rm` fallido ya no se declara éxito | `scripts/backup/test-retention-capacity.sh` (105/0) | idem |
| Autenticidad del paquete de recuperación cifrado (GnuPG rechaza un paquete alterado, con y sin sidecar) | `scripts/backup/test-recovery-package.sh` (39 casos, determinista) | idem |
| Regresión de lo anterior bajo demanda | workflow **manual** `.github/workflows/regresion-manual.yml` (`p07`, `dr-datos`, `ui-stack`, `todo`) | Actions → *Regresion manual* |
| CI de cada PR | `ci.yml` / `playwright.yml` (21 checks) | automático |

## 2. Estado final de la Lista A

| # | Estado | PR |
|---|---|---|
| A1 | Hecho | #367–#370 |
| A2–A5, A8 | Hecho (3 navegadores) | #371 |
| A6 | Hecho (cierre técnico por evidencia; sin rediseño ni nuevo `/ui-module` en las 7 pantallas agregadas) | #371 |
| A7 | Hecho (workflow solo manual) | #371 |
| A9 | Hecho | #371 |
| A10 | Hecho | #371 |
| Hallazgo posterior al merge de #371 | `test-recovery-package.sh` intermitente (ver §4), corregido | esta PR |

## 3. Evidencia HISTÓRICA (no usar para decidir; se conserva tal como se emitió)

| Documento | Por qué es histórico |
|---|---|
| `aceptacion-final-20261123.md`, `puesta-en-marcha-*`, `p04-*`, `p05-*`, `p06-*` anteriores al 24/11, informes sobre la VM `tbp` | Describen la VM (fuera del plan desde 2026-11-24) o criterios del anfitrión Windows (H01-H09, VMware, `vgk.sys`) que ya no aplican. Cada uno lleva una nota de una línea. |
| `estado-finalizacion-20261124.md` | Foto del estado del 24/11 (antes de #371). Su §5 (texto para Notion) sigue siendo útil. |
| `estado-software-pre-instalacion.md`, `cierre-*`, `fase-*`, `informe-cierre-*` | Cierres por hito o por fase de etapas anteriores; las pruebas que citan se repiten en la evidencia vigente de §1. |
| `pendientes-software-vs-operacion-20261124.md` §2 (tabla original de la Lista A) | Describe lo pendiente *antes* de #371; el estado actual es el de §1b del mismo documento y el de este índice. |

## 4. Incidente posterior al merge de #371 (CI de `main`, `compose-validate`)

- **Síntoma:** `test-hardening` 107 OK, 1 fallo: dos casos de `test-recovery-package.sh` («paquete alterado SIN sidecar» y «…con sidecar REEMPLAZADO») esperaban exit 3 y no lo obtuvieron. Pasó en la PR y falló en `main`: **intermitente**.
- **Causa raíz (reproducida):** la alteración era `printf 'X' | dd … seek=120 conv=notrunc`, una `X` fija en el offset 120. El cifrado simétrico de GnuPG usa sal y clave de sesión aleatorias, así que el byte 120 cambia en cada paquete; con probabilidad 1/256 ya vale `X`, el archivo queda **idéntico**, GnuPG lo descifra bien (rc 0) y fallan a la vez los dos casos. En la notebook se generó un paquete con `X` natural en el byte 120 (apareció a los 41 intentos): el `dd` no cambió el archivo y `gpg -d` devolvió 0.
- **Corrección (solo el test):** la alteración invierte el byte (XOR 0xFF), por lo que **siempre** cambia; se verifica con `cmp` que el archivo cambió y conserva el tamaño; se comprueba que **GnuPG por sí solo** descifra el original y rechaza el alterado (rc 2); y que en ambos casos (sin sidecar y con sidecar coincidente) el rechazo proviene del descifrado/autenticación y no de otra causa. `recovery-package.sh` no se modificó: el cifrado autenticado ya detectaba la alteración cuando esta ocurría.
- **Resultado:** 4 corridas consecutivas, 39 PASS, 0 FAIL.
