# Preparación operativa del piloto de 30 días — instancia provisional (2026-11-25)

Estado: **instancia técnicamente lista; la operación real NO se inició.** Fiscal V2 sin activar (`FISCAL_CRITERIO_V2_DESDE` vacío), `CSP_MODE=Off`, crédito cerrado, sin datos comerciales. Complementa `docs/servidor-provisional-notebook.md` y `docs/puesta-en-marcha-p07-p02.md`; no los repite.

## 1. Evidencia de hoy (medida en la notebook, `theburyproject-prov`)

| Control | Resultado |
|---|---|
| Versión previa | tag `servidor-provisional-20261125`, imagen `erp:20261125-41775ca5` |
| Respaldo previo | `backup.sh all` OK (cadena `2026-11-25_224118Z`), copia cifrada `verify-local-copy.sh` OK (44 archivos sha256), y un respaldo automático pre-migración dentro de `deploy.sh` |
| Actualización | `install.sh` oficial sobre `main` 1ba62e7a (PR #374): imagen inmutable `erp:20261125-1ba62e7a` (`sha256:3d1bc563b6ef…`), preflight → backup → migrate (145 migraciones, 0 pendientes) → health → smoke OK; el `.env` se conservó |
| Identidad | `app` y `migrate` usan la imagen desplegada; `db` y `caddy` sin cambios |
| `verificar-servidor.sh` (como root) | **21 PASS, 0 FAIL, 2 WARN** (Fiscal V2 sin definir; sin copia externa independiente: ambos son decisiones del propietario) |
| Reporte de IVA | autenticado (`prov-admin`): `/Reporte/Iva` 200 en ventas, compras y ambos; aparece el renglón «IVA de notas de crédito fiscales» (con 0 NC) |
| Humo CSP de solo lectura (con CSP Off) | 7 pantallas 200, 0 violaciones, SignalR conectado, 0 escrituras |
| Respaldo posterior a la actualización | `backup.sh all` OK; `restore-test.sh` OK (145 migraciones, 141 tablas, CHECKDB OK); `restore-test-local-copy.sh` desde la copia cifrada OK (41 s) |
| Cron / monitor | 4 crones `bury-*` presentes; `bury-monitor` activo (62 señales, 0 alertas) |
| Capacidad | disco raíz 16 % (188 GB libres); copia cifrada 2,9 GB libres; respaldos de la provisional 47 MB |
| Laboratorio intacto | `theburyproject-notebook`: imagen `20261124-ab27a838`, 3 contenedores sanos, respaldos y copia cifrada propios sin cambios |
| Custodia | paquete de recuperación (`.env`) descifra y es **idéntico al `.env` actual** (la actualización no lo cambió); paquete de claves LUKS verificado. Siguen en la notebook hasta que el propietario los saque |
| Crédito y punitorios | cupos de los 6 puntajes en $0 (por la pantalla real); sin versión de `ConfiguracionesPunitorio` ⇒ el sistema trata el punitorio como «sin configuración» (0 %); `EmpresasConfiguracion.InteresMoraDiarioPorcentaje` = 0 |

## 2. Configuración comercial existente y datos faltantes

Base nueva: solo existen lo sembrado por el sistema y la cuenta inicial.

| Existe (sembrado) | Falta (todo por pantalla; nada se inventa) |
|---|---|
| Sucursal «Casa Central»; 4 alícuotas (21, 10,5, 27, exento); 8 roles con 587 permisos; plantilla de contrato de crédito; configuración de crédito y de mora (por defecto) | **Empresa** (nombre, CUIT/DNI, domicilio, ciudad, jurisdicción, condición fiscal, nombre comercial: hoy placeholders) · **Cajas** (0) y asignación a quien opere caja · **Medios de pago**, recargos/descuentos y planes (0) · **Costos y precios de envío/armado** (0; IVA 21 % por configuración) · **Orígenes de fondos** (0) · **Punitorios**: versión vigente 0 % / período 30 días / gracia 12 días (decidido; no cargada) · **Mora**: `DiasGracia` está en 3 y debe coincidir con la gracia de punitorios (12) · **Catálogo** (productos, categorías, marcas, proveedores) y **clientes** · **Documentos** (plantillas de recibo/comprobante: 0 propias) · **Usuarios reales** (solo existe `prov-admin`) |

Nota: `ConfiguracionesMora.PorcentajeRecargo = 5` es un valor sembrado de la configuración de mora; ningún servicio de cobro lo usa como punitorio (el punitorio sale de `ConfiguracionesPunitorio`). Se informa por si se quiere llevar a 0 por claridad.

## 3. Usuarios reales, roles, permisos y cajas (procedimiento)

Sin crear personas ni contraseñas por el propietario. Orden (`docs/puesta-en-marcha-p07-p02.md` §3):

1. Ingresar con la cuenta inicial (`prov-admin`; contraseña solo en `.env`, 0600).
2. **Seguridad → Usuarios**: crear **el SuperAdmin del propietario** y **el de recuperación**. Cada uno ingresa, acepta Términos y fija su contraseña (≥ 10, mayúscula, minúscula y dígito). No seguir hasta que ambos hayan ingresado.
3. Personal: una cuenta por persona real con el **rol mínimo** (Administrador, Gerente, Vendedor, Cajero, Repositor, Contador, Técnico), sucursal y, para quien opere caja, su caja. Los permisos de cada rol se revisan en **Seguridad → Roles** antes de asignarlos; ningún cambio de permisos se hace por script.
4. **Cajas** (Caja → configuración): crear cada caja y asignarla al cajero.
5. Cambiar la contraseña de la cuenta inicial y desactivarla **solo después** de verificar los dos SuperAdmin.
6. Control: `verificar-servidor.sh --cierre` debe terminar sin FAIL (≥ 2 SuperAdmin operativos y 0 cuentas sintéticas).
7. Crédito cerrado: dejarlo así (`p07-verificar-credito-cerrado.sql`, ya parte de `verificar-servidor.sh`). Abrirlo es decisión posterior, por pantalla, con el control `@Operando = 1`.

## 4. Activación de Fiscal V2 (preparada, NO ejecutada)

`install.sh` **conserva el `.env` existente**: pasar `--fiscal-v2-desde` a una instalación ya hecha no lo persiste. El instante se escribe de forma explícita al autorizar el corte:

```bash
cd ~/theburyproject-prov
cp .env ~/.csp-env-previo-$(date +%F-%H%M) && chmod 600 ~/.csp-env-previo-*      # copia de la configuración previa (secretos: fuera del repo)
NOW=$(date -u +%Y-%m-%dT%H:%M:%SZ)                                               # reloj del servidor, con zona Z
sed -i "s/^FISCAL_CRITERIO_V2_DESDE=.*/FISCAL_CRITERIO_V2_DESDE=$NOW/" .env
grep -E '^FISCAL_CRITERIO_V2_DESDE=' .env                                       # debe mostrar el instante, una sola línea
scripts/deploy/deploy.sh --image theburyproject/erp:20261125-1ba62e7a --backup auto --project theburyproject-prov   # preflight valida el formato; recrea app
docker compose -p theburyproject-prov logs app | grep -i '\[FISCAL\]'            # debe informar V2
```

* Ensayado hoy (sin modificar nada): el `preflight.sh` acepta el instante generado con el reloj del servidor sobre una copia temporal del `.env`.
* El instante **no puede ser posterior** a la primera venta real ni quitarse luego. Tras activar: regenerar los paquetes de custodia (`recovery-package.sh create`), porque el `.env` cambió.
* Verificación con la primera venta acordada: versión fiscal 2, `Subtotal + IVA = Total` (`docs/p02-procedimiento-activacion.md` §3 paso 6; `p02-verificar-instalacion.sql` con `@PuestaEnMarcha`).
* Reversión antes de operar: restaurar el `.env` previo y repetir `deploy.sh`. Con ventas V2 solo `rollback.sh --to-image … --confirm-compatible`.

## 5. Ventana CSP `ReportOnly` (preparada, NO activada)

Procedimiento completo y criterios GO/NO-GO: `docs/csp-enforce-activacion.md` (§2, §3, §5). Particularidades de esta instalación (Docker + Caddy, notebook Linux):

* **Activar (solo con autorización expresa):** `sed -i 's/^CSP_MODE=.*/CSP_MODE=ReportOnly/' .env && ERP_IMAGE=theburyproject/erp:20261125-1ba62e7a docker compose -p theburyproject-prov up -d --no-deps app` (no `restart`: no aplica el cambio). **`ERP_IMAGE` es obligatorio** en una shell nueva: `install.sh`/`deploy.sh` lo exportan solo dentro de su proceso y `docker-compose.yml` cae a `erp:local`, que recrearía `app` con otra imagen (comprobar después con `docker inspect theburyproject-prov-app-1 --format '{{.Config.Image}}'`). Verificar la cabecera `Content-Security-Policy-Report-Only` con `curl -ks -D - -o /dev/null https://tbp-prov:8443/Identity/Account/Login`. Un valor mal escrito deja `app` en bucle de reinicio: revertir con `CSP_MODE=Off`.
* **Línea base de hoy:** `csp-humo-lectura.js --esperar off` = OK (7 pantallas, 0 violaciones). Se corre en el contenedor `mcr.microsoft.com/playwright:v1.59.1-noble` con `--network host`, montando `scripts/ops` en solo lectura y las credenciales por variable de entorno; la notebook no tiene Node ni PowerShell. Con la ventana activa se repite con `--esperar reportonly`.
* **Archivo diario del log** (Docker lo rota a 10 MB × 3 y lo pierde al recrear el contenedor): `docker compose -p theburyproject-prov logs --no-color -t --since 24h app > ~/csp-obs/app-$(date +%F).log`, por cron diario. Antes de cada reinicio archivar a mano. El análisis (`scripts/ops/csp-observacion.ps1`) se corre en la PC de Windows sobre los logs copiados con `scp`.
* **Centinela** el primer y el último día (consola del navegador, ver §2 del documento de CSP). Duración mínima: 10 días hábiles con un cierre/inicio de mes dentro de los 30 días del piloto.
* Reversión: `CSP_MODE=Off` + `ERP_IMAGE=theburyproject/erp:20261125-1ba62e7a docker compose -p theburyproject-prov up -d --no-deps app` (≤ 5 min).

## 6. Pendientes exclusivos del propietario

Sacar de la notebook los paquetes de custodia y las passphrases (`D:\tmp\procedimientos-propietario-20261125.md` §2); destino del respaldo independiente en disco externo cifrado (§1 del mismo archivo) o aceptación escrita del riesgo; cambiar la contraseña de `snowsh`; reservar IPs en el router; Telegram o heartbeat externo para el monitor; datos de §2; identidades de los dos SuperAdmin y del personal; autorización y fecha del corte de Fiscal V2; autorización de la ventana CSP.

El piloto no debe iniciarse hasta cumplir, como mínimo: custodia fuera de la notebook, respaldo independiente (o riesgo aceptado), dos SuperAdmin reales, datos de empresa/cajas/medios de pago cargados, instante de V2 persistido y primera venta de verificación.
