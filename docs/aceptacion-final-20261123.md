# Informe de aceptación final (2026-11-23)

Base: `main` 647207c1 (#358). Solo documentación. No se tocó ninguna base, VM, contenedor ni usuario. No reabre módulos cerrados: consolida lo ya decidido y probado.

**Convención de evidencia**
- **[V]** verificado en esta pasada (git/gh, lectura de documentos, consulta de solo lectura a la VM).
- **[R]** reportado por documentos o agentes anteriores; no re-ejecutado aquí.
- **[P]** pendiente / no verificable hoy.

## 1. Respuestas directas

| Pregunta | Respuesta |
|---|---|
| ¿Terminado el desarrollo funcional inicial? | **Sí para el alcance de contado y administración** [R, documentado en `aceptacion-erp.md`, `estado-software-pre-instalacion.md`, `hitos-d01-d03…` §16-18]. Los PR #156-#358 están fusionados en `main` [V: `gh pr list`]. **No** está terminado lo diferido (§6) ni crédito, que se mantiene cerrado por decisión. |
| ¿Qué está validado? | Pruebas automáticas, QA en clones y preproducción sintética (§3). **Nada se validó con datos reales de producción.** |
| ¿La nueva instalación funciona? | **No verificable: no existe.** [V] En la VM `tbp` solo corre el proyecto `theburyproject` (preproducción sintética: app healthy, db healthy, caddy en 443). No hay proyecto productivo. Existe un directorio `/srv/bury-backups-prod` (indicio de preparación; sin contenido verificado). El agente 1 no confirmó instalación. |
| ¿Qué falta para operar con datos reales? | §5. |
| Riesgos aceptados | §6. |
| Hitos diferidos | §7. |
| Condiciones para cerrar el proyecto completo | §8. |

**No se declara el proyecto al 100 %.** Hay hitos abiertos (P-04, P-06 offsite, P-08, P-07/P-02 operativos, D-05).

## 2. Estado por hito (sin inferencias)

| Hito | Desarrollo | Técnicamente listo | Operativo | Fuente |
|---|---|---|---|---|
| D-01 listas de precios/costeo | Finalizado | Sí | — | `hitos…` §16.4 [R] |
| D-02 referencias/crédito/autorizaciones | Finalizado funcional | Sí (reservas: lector de pantalla real) | — | §16.4 [R] |
| D-03 Tesorería/anticipos/cheque único | Finalizado | Sí | Orígenes de fondos y cajas reales sin cargar | §17 [R] |
| V-02 cheques informativos | Finalizado (alcance informativo) | Sí | — | §18 [R] |
| P-02 fiscal V2 | Implementado y probado | Sí (instalador con `--fiscal-v2-desde`, verificador SQL) | **Pendiente**: instante fiscal, instalación, aceptación. IVA de punitorio con alícuotas mixtas sin tratamiento (contador) | `p02-*`, `cierre-administrativo-20261121` [R] |
| P-03 seguridad de usuarios | Verificado (software) | Sí | Cuentas reales (2 SuperAdmin) sin crear | `estado-software-pre-instalacion` [R] |
| P-04 servidor definitivo | Diferido | Arnés Win11/Ubuntu fusionado | **No hay servidor físico**; solo la VM `tbp`, que es de la PC de desarrollo | `puesta-en-marcha-estado-20261123` [R] |
| P-05 acceso remoto | Cierre técnico local | Sí | Tailscale real sin confirmar | `p05-cierre-tecnico-local` [R] |
| P-06 respaldos | Local cifrado FINALIZADO (#347); general **En progreso** | Sí | **Sin offsite**; claves LUKS solo dentro de la VM; remontaje tras reinicio sin probar | `cierre-administrativo` §2 [R] |
| P-07 puesta en marcha | Técnico listo | Sí (verificadores con exit≠0, 145 migraciones, config inicial validada en QA #354) | **Pendiente** | `p07-configuracion-inicial-checklist` [R] |
| P-08 monitoreo | Cierre con excepción documentada | Sí | URL de heartbeat sin rotar; DOWN/UP externo no confirmado; falta token/chat_id Telegram | `cierre-administrativo` §6 [R] |
| D-05 CSP | Preparado y medido: 0 violaciones en 7 roles × 3 tamaños con `Enforce` en stack QA | Sí | **No activado**; falta observación `ReportOnly` en uso real (10 días hábiles) | `csp-enforce-*` [R] |
| Crédito personal | Implementado | — | **Cerrado**: cupos $0, sin planes, montos financiables $0 (#349) | `cierre-credito-cupos-cero-20261122` [R] |

Nota CI [V]: al momento de esta consulta, los workflows `CI`, `Playwright Tests` y `FIN SQL Server` sobre 647207c1 seguían en curso (sin conclusión); otros tres SQL Server en success. La verde de CI de #358 debe confirmarse antes de usarla como base de aceptación.

## 3. Evidencia disponible: sintética vs navegador real vs producción

| Nivel | Qué hay | Qué NO es |
|---|---|---|
| Sintético (tests, SQL Server efímero) | Suite 8791 OK / 0 fallos / 253 omitidas por entorno (corren en CI) [R]; concurrencia RCSI 0 y 1; instalación limpia 145 migraciones, 37 verificaciones [R] | No prueba comportamiento con datos reales |
| Navegador real sobre clon/preproducción | Playwright 1366/1280/390; axe 0 violaciones; recorridos de ventas, caja, compras, devoluciones, cheques en clones de la base real (Hitos 9-11) [R] | No es producción ni uso real |
| QA por perfil (#356) | Accesos permitidos y denegaciones por rol sin modificar datos [R] | — |
| Producción real | **Ninguno.** | — |

## 4. Matriz mínima de aceptación (reutiliza evidencia; sin repetir)

| Área | Estado | Evidencia a reutilizar | Falta en la instalación nueva (intervención humana) |
|---|---|---|---|
| Autenticación | Aprobado | P-03, `TransicionCuentasCookieHttpTests`, bootstrap Production | Login real con el administrador inicial; rotación/baja de `admin` y cuentas demo |
| Usuarios y permisos | Aprobado | Barrido de 506 acciones × 10 identidades; #353, #356; `p07-verificar-cuentas.sql` | Alta de ≥ 2 SuperAdmin y personal con rol; verificador con `@Cierre = 1` |
| Alta de clientes | Aprobado con limitaciones | #162, #202; alta fija puntaje 1 (cupo 0) | Un alta real. BCRA con tope 6 s (provisorio) |
| Productos y existencias | Aprobado | Stock = Σ movimientos; Kardex sin cortes | Carga inicial real de productos y stock |
| Ventas de contado | Aprobado con limitaciones | Cotización → venta → confirmar → facturar → caja; #165/#182/#203 | Una venta de contado real de punta a punta; medios de pago reales (recargos no aprobados) |
| Caja y movimientos | Aprobado con limitaciones | Concurrencia SQL, cierres/arqueo (invariantes 0 violaciones) | La instalación nueva trae **0 cajas**: crear cajas; un cierre real |
| Compras | Aprobado con limitaciones | QA OC, recepción parcial; #350 | Costo manual (costeo: decisión abierta); una OC real |
| Proveedores | Aprobado | #351 (baja con deuda rechazada), D-03 | Alta de proveedores reales |
| Tesorería | Finalizado | D-03, `TesoreriaSqlServerTests` | Orígenes de fondos reales (hoy 0) |
| Facturación V2 | Implementado | P-02, #355 (tarjeta V2 con servicios exactos), `p02-verificar-instalacion.sql` | Fecha V2 persistida: **no verificable** sin instalación. Facturación fiscal legal es externa al ERP (decidido); el ERP emite comprobante interno |
| Reportes económicos | Aprobado con limitaciones | Ventas, Márgenes, IVA contrastados con SQL | IVA sin netear devoluciones, costo con IVA, envío/armado y recargo fuera de la ganancia: contador |
| Respaldos | Aprobado con limitaciones | Full diario, logs 15 min, `verifyonly` OK, copia local LUKS [R] | **Sin offsite**; claves fuera de la VM |
| Recuperación | Aprobado con limitaciones | Restauración full + 67 logs, `CHECKDB` OK, 44 s desde copia externa en ensayo [R] | Restauración sobre la instalación real; remontaje tras reinicio |

## 5. Validación de la nueva instalación (Sección C): estado

No iniciable: el agente 1 no confirmó instalación y [V] no existe proyecto productivo. Checklist a ejecutar cuando se confirme, con quién:

| # | Control | Cómo | Estado |
|---|---|---|---|
| 1 | Healthchecks | Evidencia del agente 1 (`docker ps` healthy, `/health`, heartbeat) | [P] |
| 2 | Migraciones | 145 aplicadas, sin pendientes (`__EFMigrationsHistory`) | [P] |
| 3 | Verificadores SQL | `p02-verificar-instalacion.sql`, `p07-verificar-credito-cerrado.sql`, `p07-verificar-cuentas.sql` con `sqlcmd -I -b`, exit 0 (cuentas con `@Cierre = 1`) | [P] |
| 4 | Fecha fiscal V2 | Valor persistido = `--fiscal-v2-desde` (UTC, `Z`) = `FISCAL_CRITERIO_V2_DESDE` | [P] |
| 5 | Seguridad | HTTPS, `ASPNETCORE_ENVIRONMENT=Production`, sin cuentas demo, 2 SuperAdmin, CSP en modo acordado | [P] |
| 6 | Datos de prueba vs operativos | 0 cuentas sintéticas; la preproducción sintética no se usa como productiva | [P] |
| 7 | Pruebas con intervención humana | Login de administrador, una venta de contado, un cierre de caja, una restauración de backup, rotación de credenciales | [P] |

Bloqueos ya documentados para instalar (`puesta-en-marcha-estado-20261123` §2): servidor definitivo o autorización para usar la VM; correo y usuario del administrador inicial; directorio de respaldos propio. La preproducción ocupa `0.0.0.0:443`. #358 (cadena de firewall parametrizable para segunda pila) apunta a resolver la convivencia; no verificado que se haya usado.

## 6. Riesgos aceptados (no mitigados)

1. Pérdida total del PC (robo, disco, ransomware, borrado de la VM): sin offsite. Aceptado temporalmente por el propietario.
2. Claves LUKS (`local-disk.key`, `luks-header`) dentro de la VM: perdidas = respaldo local irrecuperable.
3. Anfitrión sin endurecer (UPS, BIOS, Windows, autoarranque, Wi-Fi): P-04 diferido.
4. Una sola instancia de la app; hora de logs en UTC (esperado).
5. Lector de pantalla real (NVDA/JAWS/VoiceOver) no verificado.
6. IVA de punitorio con alícuotas mixtas, interés de financiación y recargo: tratamiento fiscal sin validar (contador). Facturación fiscal externa.
7. Interés por mora `TasaMoraBase`: unidades contradictorias (diario vs mensual); contado no afectado, crédito cerrado sin exposición. **Debe decidirse (A/B/C) antes de abrir crédito.**

## 7. Funcionalidades diferidas

Crédito personal (abierto solo por decisión posterior); facturación electrónica ARCA/AFIP; cheques propios y endosos; seguimiento de acreditación de cheques (retirado en V-02); saldos por origen/arqueo/conciliación bancaria; exportación del estado de cuenta; R-08 (rol Gerente en cuentas por pagar, manual); R-11 (RMA → NC de proveedor); Mercado Libre (fuera de alcance; defecto DEF-ML-01 registrado); Referencias de clientes: pestaña oculta o implementada según D-02 (confirmar vigente); módulo de lotes por listas más allá del cambio directo; pantalla de sucursales (solo «Casa Central»).

## 8. Decisiones comerciales abiertas del propietario

1. Servidor de instalación (físico P-04 / VM / reemplazo de preproducción) y autorización del corte.
2. Identidad del administrador y de los 2 SuperAdmin; custodia de sus claves y de las claves LUKS.
3. Datos reales de empresa (CUIT, domicilio, vencimiento de pagaré, condición fiscal por defecto).
4. Sucursales: ¿alcanza «Casa Central»?
5. Orígenes de fondos y cajas reales.
6. Medios de pago con recargos/descuentos (−5 %/+3 % no aprobados).
7. Precios/costos de envío y armado (hoy $0).
8. Método de costeo de compras (manual hoy).
9. Instante `--fiscal-v2-desde`.
10. Aceptar o mitigar la falta de offsite antes de datos reales.
11. Telegram (token/chat_id) y observador externo de P-08.
12. Interés por mora (A/B/C) antes de abrir crédito.

## 9. Criterios para declarar el ERP **listo para operar** (contado inicial)

Todos deben cumplirse y quedar evidenciados:

- [ ] CI de `main` verde sobre el SHA instalado.
- [ ] Instalación productiva independiente de la preproducción, con healthchecks OK.
- [ ] 145 migraciones aplicadas; los 3 verificadores SQL con exit 0.
- [ ] Fecha fiscal V2 persistida coincide con la autorizada.
- [ ] ≥ 2 SuperAdmin reales; 0 cuentas demo/sintéticas; clave de `admin` rotada o cuenta desactivada.
- [ ] Datos reales de empresa, cajas y orígenes de fondos cargados.
- [ ] Una venta de contado real de prueba (con anulación/devolución controlada) y un cierre de caja cuadrado.
- [ ] Respaldo diario ejecutado en la instalación nueva, restauración verificada, claves custodiadas fuera de la VM.
- [ ] Decisión explícita del propietario sobre offsite (mitigar o aceptar por escrito).
- [ ] Crédito verificado cerrado (`p07-verificar-credito-cerrado.sql`).

## 10. Criterios adicionales para cerrar el **proyecto completo**

P-04 con servidor físico y anfitrión endurecido; P-06 con offsite real y remontaje probado; P-08 con canal externo y observador DOWN/UP confirmados; D-05 con 10 días hábiles en `ReportOnly` real y decisión de `Enforce`; decisiones del contador (IVA/NC/redondeo) cerradas; apertura o descarte definitivo del crédito; revisión de lectores de pantalla.

## 11. Notion

No hay conector de Notion disponible en esta sesión: **no se actualizó**. Los estados de §2 no deben pasarse a Notion sin coordinar con el agente principal y sin la validación de la instalación (§5).

## 12. Defectos reproducibles hallados en esta pasada

Ninguno. No se ejecutaron pruebas funcionales nuevas.
