# P-07: configuración inicial validada en QA y checklist final (2026-11-23)

Complementa `puesta-en-marcha-p07-p02.md` (orden del corte) y `cierre-credito-cupos-cero-20261122.md` (crédito cerrado); no los repite. Alcance: **solo configuración por pantalla** sobre una instalación **nueva y descartable** (pila Docker `buryp07*`, `docker-compose.ci.yml`, destruida al terminar). No se tocó ninguna base real ni producción. Caja/Compras, roles/seguridad y fiscalidad V2 son de otros frentes y no se tocaron.

## 1. Resultado de la validación (cada parámetro se guardó por su pantalla y se recuperó releyendo la pantalla y la base)

| Configuración | Pantalla (permiso) | Valor cargado en QA | Guardado / recuperado | Comentario |
|---|---|---|---|---|
| Datos de empresa | Documentos → Empresa (`documentos.managetemplates`) | Datos **ficticios** de QA | OK (pantalla + SQL) | La instalación nueva trae textos de ejemplo («Nombre del vendedor…», «Provincia»): hay que reemplazarlos |
| Sucursales | **No existe pantalla** | — | — | Solo existe «Casa Central» sembrada; ver §3-A |
| Orígenes de fondos | Tesorería → Nuevo/Editar (`tesoreria.configure`) | 2 orígenes `QA-*` (efectivo, cuenta) | Alta + edición OK | La instalación nueva trae 0 orígenes |
| Medios de pago | Configuración de pagos → Medios (`configuracion.update`) | Alta de un medio global sin recargo | OK | Sin recargos/descuentos: **no están aprobados**, no se cargaron |
| Parámetros de crédito | Configuración de pagos → Crédito personal | Cupo puntaje 0: $200.000 → **$0**; montos financiables $0; **0 planes activos** | OK | `p07-verificar-credito-cerrado.sql` (`@Operando = 0`): sin FALLA |
| Mora (gracia) | Mora → Configuración | Gracia **3 → 12** | OK (solo cambió `DiasGracia`; el resto se preservó) | Coincide con la gracia de punitorios |
| Punitorios | Configuración de pagos → Punitorios (`configuracion.managepunitorio`) | **0 % / 30 días / gracia 12**, activa | OK («Activo al 0 %», verificador OK) | Versión append-only: la historia queda |
| Envío y armado | Servicios de venta (`configuracion.update`) | Precio y costo **$0** (se probó con un valor no aprobado y se volvió a $0) | OK | Con $0 las 8 filas quedan activas en $0 |

Verificadores SQL en la misma base (sqlcmd `-b`): `p02-verificar-instalacion.sql` → exit 0; `p07-verificar-credito-cerrado.sql` → exit 0; `p07-verificar-cuentas.sql` → exit 1 **esperado** en este stack (el overlay de CI siembra cuentas demo; en Production solo existe la cuenta inicial, ver `puesta-en-marcha-p07-p02.md` §3). `scripts/install/test-verificadores-sql.sh` en modo vivo contra la base: **18/18** (ver §4).

## 2. Defecto corregido

`scripts/install/test-verificadores-sql.sh` en modo vivo fallaba **en Windows/Git Bash** (9 de 18 casos): `docker cp` recibía rutas `/d/...` sin convertir. Ahora usa `cygpath -m` para las rutas del host (igual que `run-p07.sh`) y `MSYS_NO_PATHCONV=1` para las del contenedor. Sin cambios de comportamiento en Linux/CI.

## 3. Configuración obligatoria sin procedimiento completo por pantalla

- **A. Sucursales.** No hay pantalla para renombrar «Casa Central» ni para crear otras; `Sucursal` solo alimenta la asignación de usuarios. Si el negocio opera con una sola sucursal, no bloquea. Si necesita otro nombre u otras sucursales, **se requiere desarrollar la pantalla** (decisión del propietario; no se hizo por estar fuera del alcance de defectos).
- **B. Texto contractual de interés por mora.** La mora trae `TasaMoraBase = 5` y el contrato de crédito imprime «interés diario del 5 %» (`contrato.interesMoraDiario`), mientras el punitorio aprobado es 0 %. No afecta importes (los calcula el punitorio) y el crédito está cerrado, pero **antes de abrir el crédito** hay que decidir qué porcentaje debe figurar en el contrato.
- **C. Cajas:** la instalación nueva trae 0 cajas (módulo Caja: otro frente; solo se deja constancia).
- **D. Vencimiento del pagaré y condición fiscal por defecto** (Documentos → Empresa): quedan sin definir hasta que el propietario los fije.

## 4. Cómo correr los verificadores contra una base descartable (Windows/Git Bash o Linux)

```bash
P07_DB_CONTAINER=<contenedor-db> P07_SA_PASSWORD=<sa> P07_DB_NAME=<base> bash scripts/install/test-verificadores-sql.sh   # 18 PASS esperado
```

Para la base real, los tres `.sql` se ejecutan con `sqlcmd -I -b` (ver `puesta-en-marcha-p07-p02.md` §3); un exit distinto de 0 detiene el procedimiento.

## 5. Checklist final

**Automático / ya resuelto por el equipo técnico (no requiere al propietario)**
- [x] Instalación limpia, migraciones, verificadores SQL con exit != 0 ante FALLA (#352) y modo vivo operativo también en Windows.
- [x] Punitorios 0 % / 30 / 12 y mora gracia 12: se cargan por pantalla y se verifican con `p07-verificar-credito-cerrado.sql`.
- [x] Cupos $0 (puntajes 0 al 5), montos financiables $0, sin planes de cuotas, envío/armado en $0: se cargan/verifican por pantalla.
- [x] Guardado y recuperación de cada pantalla probados (§1).

**Decisión o dato del propietario (nada de esto se puede inventar)**
- [ ] Datos reales de la empresa (nombre, CUIT, domicilio, ciudad, provincia) y, si se desea, vencimiento del pagaré y condición fiscal por defecto.
- [ ] ¿Una sola sucursal («Casa Central») alcanza? Si no: nombres/altas y desarrollo de la pantalla (§3-A).
- [ ] Orígenes de fondos reales (efectivo, cuentas bancarias) y las cajas.
- [ ] Medios de pago con sus recargos/descuentos (la referencia −5 % / +3 % no está aprobada).
- [ ] Precios y costos de envío y armado (hoy $0).
- [ ] Porcentaje de interés por mora que debe figurar en el contrato antes de abrir el crédito (§3-B).
- [ ] Los dos SuperAdmin reales, personal, rol, sucursal y caja (`puesta-en-marcha-p07-p02.md` §3).
- [ ] Instante de `--fiscal-v2-desde`, destino de la copia externa de respaldos y autorización del corte.
