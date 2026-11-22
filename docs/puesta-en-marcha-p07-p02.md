# Puesta en marcha P-07 / P-02: qué está listo, qué falta y en qué orden (2026-11-21)

Complementa `instalacion-servidor.md`, `p02-procedimiento-activacion.md` §7 y `p03-ensayo-transicion-cuentas.md` §4; no los repite. Todo lo de esta página se validó en una base **nueva y descartable** (`scripts/qa/p02/validacion-base-nueva.sh`: 37 OK / 0 FALLA, 145 migraciones, V2 desde el primer arranque). **No se tocó ninguna base real ni la preproducción sintética de la VM.**

## 1. Estado

| Frente | Listo (técnico) | Falta (no es ingeniería) |
|---|---|---|
| P-07 instalación limpia | `install.sh` genera `.env`, construye, despliega con preflight/backup/migrate/health/smoke; ahora acepta `--fiscal-v2-desde` y **rechaza** generar un `.env` nuevo sobre un proyecto Compose que ya tiene volúmenes | Servidor definitivo (P-04, diferido), dominio/CA en los clientes, copia externa de respaldos (P-06) |
| P-02 V2 / P3 / P6 | Mecanismo `FISCAL_CRITERIO_V2_DESDE` (se resuelve al arrancar; inválida = no arranca; preflight la valida con la misma regla), P3 llega con las 145 migraciones | Instante real de puesta en marcha |
| Cuentas (P-03) | Alta por pantalla ensayada; `scripts/sql/p07-verificar-cuentas.sql` (solo lectura) comprueba el resultado | Las personas reales y sus contraseñas (las escribe cada una) |

## 2. Orden de la puesta en marcha (en el servidor definitivo, proyecto propio)

```bash
# 1) Base NUEVA: proyecto, .env y backup-dir propios (nunca los de preproducción). Imprime UNA vez la contraseña inicial del administrador.
bash scripts/install/install.sh --yes --project theburyproject-prod --env-file ~/theburyproject-prod.env \
     --admin-email <correo real del propietario> --admin-user <usuario> --backup-dir /srv/bury-backups-prod \
     --fiscal-v2-desde <instante ISO con zona, p. ej. 2026-12-01T03:00:00Z = 00:00 de Argentina> --schedule
# 2) Verificación de instalación limpia (solo lectura): p02-verificar-instalacion.sql con @PuestaEnMarcha completado  -> sin FALLA
# 3) Cuentas (§3) y parámetros (§4) por pantalla, con la app ya arriba
# 4) Control de cuentas al cierre: p07-verificar-cuentas.sql con @Cierre = 1 -> sin FALLA
# 5) backup.sh all + restore-test.sh en verde; guardar el .env completo fuera del servidor
# 6) Primera venta real/acordada: versión fiscal 2, Subtotal + IVA = Total (runbook P-02 §3 paso 6)
```

`FISCAL_CRITERIO_V2_DESDE` no puede ser posterior a la primera venta real ni quitarse luego. Si no se pasa `--fiscal-v2-desde`, el `.env` queda en V1 (se puede completar antes del primer arranque de ventas, no después).

## 3. Cuentas reales (sin inventar personas ni contraseñas)

1. El administrador inicial (`ADMIN_EMAIL`/`ADMIN_USERNAME` del `.env`) es el primer SuperAdmin; en Production solo existe esa cuenta (`p07-verificar-cuentas.sql` con `@Cierre = 0` lo informa).
2. Con ese acceso, Seguridad → Usuarios: crear **el SuperAdmin del propietario** y **el de recuperación**; cada uno ingresa, acepta Términos y fija su contraseña (≥ 10, mayúscula, minúscula y dígito). No avanzar hasta que ambos hayan ingresado.
3. Cambiar la contraseña de la cuenta inicial (guardada offline) y, si se desea, desactivarla **solo después** de verificar los dos SuperAdmin.
4. Personal: una cuenta por persona real, rol mínimo, sucursal y, para quien opere caja, la asignación de caja. No hay cuentas ni nombres de ejemplo en la instalación.
5. `p07-verificar-cuentas.sql` con `@Cierre = 1` debe dar: ≥ 2 SuperAdmin operativos y 0 cuentas sintéticas/demo activas (correos `@tbp.local`/`@example.*`/`.invalid`, usuarios `p0N.*` y los roles demo `cajero`, `contador`, `gerente`, `repositor`, `tecnico`, `vendedor`, `administrador`).

Ejecución (igual que la verificación P-02, con la contraseña del `.env`, nunca escrita en el repositorio):
`docker cp scripts/sql/p07-verificar-cuentas.sql <db>:/tmp/c.sql && docker exec <db> /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d TheBuryProjectDb -I -b -i /tmp/c.sql`

## 4. Parámetros que la instalación nueva NO trae (todos por pantalla; nada se siembra en nombre del propietario)

| Parámetro | Estado conocido | Dónde |
|---|---|---|
| Punitorios: tasa, período y gracia | **Decidido por el propietario (2026-11-21)**: tasa **0 %**, período técnico **30 días** (el sistema exige período ≥ 1), gracia **12 días** (día 13 devenga). Modificables luego por pantalla. El 1 %/30 días del ensayo era sintético y **no** es valor comercial. Con 0 % el sistema no genera importe (estado `TasaCero`) | Configuración de punitorios (`configuracion.managepunitorio`): cargar una versión vigente con 0 / 30 / 12 |
| Mora: días de gracia de la bandeja | Debe coincidir con la gracia de punitorios | Configuración de mora |
| Crédito: cupo y monto financiable por puntaje | **Decidido (2026-11-22): $0 en TODOS los puntajes (0 al 10) y montos financiables $0.** La semilla trae el puntaje 0 en $200.000: **llevarlo a $0 por pantalla antes de operar** y comprobarlo con `p07-verificar-credito-cerrado.sql`. Detalle y verificación del flujo: `cierre-credito-cupos-cero-20261122.md` | Clientes → «Límites por puntaje» (`clientes.managecreditlimits`) y Configuración de pagos |
| Planes de cuotas y % de recargo | Sin planes; el plan es la única autoridad del porcentaje | Configuración de pagos |
| Recargos/descuentos por medio de pago | A cargar (referencia dada: −5 % / +3 %) | Configuración de pagos |
| Costos de envío y armado | En $0 hasta que se definan; IVA de servicios 21 % por configuración | Configuración de pagos → Envíos y armados |
| Catálogos: origen de fondos, cajas, sucursal, datos de la empresa | Sin datos iniciales (la sucursal por defecto existe) | Tesorería, Caja, Documentos |

No se activan: neteo de IVA por NC fiscal externa ni IVA del punitorio con alícuotas mixtas (esperan al contador).

## 5. Reversión y protección de datos

- **Separación:** la base nueva vive en un proyecto Compose propio con volúmenes propios. `install.sh` se niega a generar un `.env` nuevo si `--project` ya tiene volúmenes (el `.env` nuevo traería secretos que esa base no conoce). La preproducción sintética de la VM no se puede usar como productiva (`p02-procedimiento-activacion.md` §7).
- **Antes de operar** (sin ventas): si algo sale mal se repite la instalación en otro proyecto; borrar el proyecto fallido (`down -v`) es destructivo y solo se hace sobre una base sin datos reales y con confirmación del propietario.
- **Con operación:** `rollback.sh --to-image <anterior> --confirm-compatible` solo cambia la imagen. Con ventas V2 la imagen destino debe conocer V2 (cualquier build posterior a #329). No ejecutar `p02-nc-fiscal-externa-down.sql` con vínculos cargados. Datos: restaurar desde `backup.sh`/`restore-test.sh`.
- Sin copia externa de respaldos (P-06) la instalación **no** debe declararse segura frente a la pérdida del servidor.

## 6. Decisiones mínimas del propietario para empezar a operar

1. **Instante real de puesta en marcha** (`--fiscal-v2-desde`).
2. ~~Tasa y período de punitorios~~ **Decidido**: 0 % / 30 días / gracia 12 días (§4). Solo falta cargarlo por pantalla al arrancar.
3. **Quiénes son los dos SuperAdmin** y dónde se custodian sus claves; lista del personal con rol, sucursal y caja.
4. ~~Cupos/montos por puntaje~~ **Decidido: $0** (§4). Siguen pendientes planes de cuotas, medios de pago y costos de envío/armado (§4), que no bloquean el inicio con el crédito cerrado.
5. **Destino de la copia externa de respaldos** (condición para llamar «segura» a la producción).

El servidor definitivo (P-04) y CSP Enforce (D-05) siguen diferidos; las respuestas del contador no bloquean el inicio.

## 7. Lista de verificación del primer inicio operativo (no ejecutar hasta el corte autorizado)

- [ ] Propietario autoriza el corte y fija el instante `--fiscal-v2-desde`.
- [ ] Proyecto `theburyproject-prod` (nunca el de preproducción); `docker compose -p theburyproject-prod ps` sin volúmenes previos antes de instalar.
- [ ] `install.sh` terminado; `p02-verificar-instalacion.sql` sin FALLA.
- [ ] Dos SuperAdmin reales ingresaron; `p07-verificar-cuentas.sql` con `@Cierre = 1` sin FALLA (≥ 2 SuperAdmin, 0 cuentas sintéticas).
- [ ] Personal con rol mínimo, sucursal y caja; cuenta inicial con contraseña cambiada.
- [ ] Punitorios cargados: 0 % / 30 días / gracia 12; mora: gracia coincidente (12).
- [ ] **Crédito cerrado (condición inicial obligatoria):** cupos $0 en los puntajes 0 al 10 (el 0 viene sembrado en $200.000: bajarlo por pantalla), montos financiables $0, sin planes de cuotas, ningún límite por cliente; solo SuperAdmin con `ventas.authorize` y `clientes.managecreditlimits`. Comprobar con `scripts/sql/p07-verificar-credito-cerrado.sql` (`@Operando = 0`) sin FALLA.
- [ ] **Para abrir el crédito más adelante (decisión del propietario, por pantalla):** definir cupos por puntaje, planes de cuotas y permisos; volver a correr el control con `@Operando = 1` y dejar constancia.
- [ ] Costos de envío/armado, medios de pago, origen de fondos y cajas cargados.
- [ ] `backup.sh all` + `restore-test.sh` verdes; `.env` completo guardado fuera del servidor; clave y cabecera LUKS de la copia local custodiadas fuera de la VM.
- [ ] Primera venta acordada: versión fiscal 2, Subtotal + IVA = Total.
- [ ] Reversión (§5) entendida: antes de operar, repetir instalación en otro proyecto; con operación, solo `rollback.sh` de imagen o restauración de backup. No borrar volúmenes con datos reales.
