# Cierre administrativo 2026-11-21: C-02, P-06 (alcance local), P-02 y preparación P-07

Base: `main` a098606a (PR #347). Solo documentación: no se tocó ninguna base, respaldo, clave, VM ni infraestructura.

## 1. C-02: DESCARTADO por decisión del propietario

- **Qué era**: preservación/recuperación de los respaldos históricos anteriores a la infraestructura actual (referenciado en `backup-ca-key-custodia.md` como «No se tocó C-02»). No hay otra tarea activa con ese nombre en la documentación.
- **Decisión (2026-11-21)**: el propietario decidió **no conservar** esos respaldos históricos.
- **Clasificación**: **CANCELADO / DESCARTADO**. No es «recuperación histórica realizada» ni «cerrado con éxito».
- **Alcance de esta ejecución**: no se eliminó ningún archivo, backup, base, clave ni carpeta. Los respaldos activos de P-06 no se tocaron. Si algún archivo histórico sigue ocupando espacio, su borrado requiere una orden aparte del propietario; no se localizaron rutas nuevas en esta ejecución.

## 2. P-06: RESPALDOS LOCALES FINALIZADO (P-06 general sigue En progreso)

Integrado en main (#347). Detalle y evidencia: `p06-respaldo-disco-local-vm.md` (no se repiten las pruebas: restauración full + 67 logs, `DBCC CHECKDB` correcto, verificación diaria 03:15, alertas, protección contra escritura al disco raíz).

- Los dos discos virtuales (`sda`, `sdb`) dependen del **mismo PC y del mismo volumen físico Windows `C:`**.
- **No existe respaldo offsite.** Las alertas `backup.offsite-*` siguen activas y no se silenciaron.
- **Riesgo de pérdida total del PC (robo, falla de disco, ransomware, borrado de la VM): ACEPTADO TEMPORALMENTE por el propietario para continuar el proyecto. No está mitigado.** No hay recuperación garantizada ante pérdida del anfitrión.
- Custodia pendiente (acción del propietario, sin copiar secretos al repositorio): `/etc/bury-backup/local-disk.key` y `/etc/bury-backup/local-disk.luks-header` deben guardarse **fuera de la VM**. Perdidos, el contenedor cifrado es irrecuperable (`/srv/bury-backups` subsiste).
- Decisión del propietario: solo el disco virtual adicional; no se insiste con nube ni disco externo. VM no reiniciada (el remontaje tras reinicio real sigue sin probarse).

## 3. P-02: condición inicial aprobada

Para toda instalación operativa **nueva**:

| Parámetro | Valor aprobado |
|---|---|
| Punitorio: porcentaje | **0 %** |
| Punitorio: período técnico | **30 días** (el sistema exige ≥ 1; con 0 % no genera importe) |
| Punitorio: días de gracia | **12** (día 13 devenga) |
| Neteo de IVA por NC fiscal externa | **desactivado** |
| IVA del punitorio con alícuotas mixtas | **sin tratamiento** (pendiente del contador; no se inventa) |

Verificado en código (`ConfiguracionPunitorioService`): rechaza solo porcentaje < 0, período < 1 y gracia < 0, por lo que **0 % / 30 / 12 es una versión válida**; el cálculo informa el estado `TasaCero`. Se aplica **por pantalla** al arrancar la instalación nueva (no se tocó ninguna base existente). El 1 %/30 días de los ensayos es sintético y no es valor comercial. Los valores pueden modificarse luego por pantalla (versionado append-only). La documentación ya no pide decidir tasa y período. Desarrollo V2/P3/P6 implementado y probado; **a la espera de instalación y aceptación operativa**.

## 4. P-07: estado

- **Técnico listo**: instalador con `--fiscal-v2-desde`, guarda de proyecto Compose existente, base nueva independiente (145 migraciones, 37 verificaciones OK), control SQL de cuentas `p07-verificar-cuentas.sql` (`@Cierre = 1` exige ≥ 2 SuperAdmin operativos y 0 cuentas sintéticas), revisión Codex sin hallazgos (#346). La preproducción sintética se conserva y no puede usarse como productiva.
- **Operativo pendiente**: servidor definitivo (P-04), personas reales (2 SuperAdmin y personal con rol mínimo, sucursal, caja), parámetros económicos, copia offsite, instante `--fiscal-v2-desde`, corte autorizado. Procedimiento y lista de verificación: `puesta-en-marcha-p07-p02.md` §2-§7.
- **No ejecutado**: base definitiva, migraciones sobre bases existentes, alta de usuarios reales, secretos, primera venta, cambio de tráfico, borrado de datos de ensayo.

## 5. Hallazgo: qué impide realmente financiar (cupo $200.000 vs montos en 0)

Lectura del código en main:

- El **cupo** del cliente sale de `PuntajeCreditoLimite` (`CreditoDisponibleService`). Sembrado: puntaje 0 (todo cliente nuevo) = **$200.000**; puntajes 1-10 = 0.
- La tabla **«monto máximo financiable por puntaje»** (`ConfiguracionCreditoMontoPorPuntaje`, sembrada en 0) solo la leen/escriben las pantallas de configuración; **ninguna regla de venta o crédito la consulta**.
- El bloqueo efectivo hoy es otro: sin **planes de Crédito Personal activos** el sistema informa «no puede financiarse con este medio de pago» (`ConfiguracionPagoService`). En la instalación nueva (sin planes) no se financia; **al crear el primer plan, un cliente nuevo podría financiar hasta $200.000** aunque los «montos financiables» sigan en 0.
- La afirmación anterior «sembrados en 0, sin definir no se financia» era **inexacta**; se corrigió en `puesta-en-marcha-p07-p02.md`.
- **No se cambió ninguna regla.** Conclusión derivada de la lectura del código, no de un ensayo dedicado. Decisión del propietario (§7): si no quiere que un cliente nuevo financie $200.000, poner el cupo del puntaje 0 en 0 antes de crear planes; si la tabla de montos financiables debe regir, es desarrollo nuevo.

## 6. Riesgos no mitigados / aceptados / diferidos

| Riesgo | Estado |
|---|---|
| Pérdida total del PC, sin offsite | Aceptado temporalmente, **no mitigado** |
| Claves LUKS solo dentro de la VM | Pendiente del propietario |
| Anfitrión P-04 (UPS, BIOS, Windows, VMware, Wi-Fi, autoarranque) | Diferido; **no declarar apto para producción segura** sin aceptar el riesgo |
| CSP D-05 Enforce | Diferido; observación Report-Only en operación real, 10 días hábiles |
| P-08: URL de heartbeat sin rotar; DOWN/UP externo no confirmado | Postergado por el propietario; sin cambios |
| Remontaje de la copia local tras reinicio real | No probado |

## 7. Decisiones que siguen siendo del propietario

1. Autorizar el corte y fijar `--fiscal-v2-desde`.
2. Quiénes son los 2 SuperAdmin, custodia de sus claves y de las claves LUKS; lista del personal con rol, sucursal y caja.
3. Cupo del puntaje 0 ($200.000 sembrado: mantener o poner en 0), planes de cuotas, medios de pago y costos de envío/armado.
4. Aceptar o mitigar el riesgo de la falta de offsite antes de operar con datos reales.
