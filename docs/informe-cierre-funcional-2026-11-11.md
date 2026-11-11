# Informe de cierre funcional — 2026-11-11

Base: `main` 89c19b03 (PR #273–#277 fusionados, CI verde en todos). Entorno de las mediciones: réplica local del stack CI (SQL Server + app Docker) y CI de GitHub. **No se tocó `TheBuryProjectDb`, no se ejecutó ninguna migración real, no se activó CSP `Enforce` en el entorno real, no se modificó ninguna regla fiscal ni ningún usuario, permiso o credencial.**

## 1. Resumen ejecutivo

| Frente | Estado | Qué quedó hecho | Qué falta y de quién depende |
|---|---|---|---|
| **F1 — C-03 E2E omitidos** | **Hecho** dentro de lo que no exige decisión | Los 20 omitidos están explicados y medidos. **14 pasaron a ejecutarse** y pasan en CI (217 correctos / 6 omitidos). CI **falla** ante cualquier omitido no clasificado. Se corrigió un defecto del spec de punitorios (fecha en UTC vs día comercial UTC-3) | 6 omitidos clasificados: 1 negativo por diseño, 2 pendientes de configuración (excepción documental, T5), 3 evidencia con workflow propio |
| **F2 — D-05 CSP** | **Hecho** salvo lo que exige entorno real | Umbral de **cero violaciones como gate de CI**. Suite E2E completa con **CSP en `Enforce` real: 213 correctos, 0 violaciones**. 7 roles × 3 navegadores × ~140 páginas: 0 violaciones. Impresión (Imprimir/Volver) y HTTPS + SignalR (wss) OK. Medición propia donde el medidor original no engancha (Firefox/WebKit) | Activar `Enforce` en el real y la ventana de observación: **propietario**. Sin cubrir: Safari/móvil real, host real, modales remotos fuera de la suite, estados sin datos |
| **F3 — Seguridad y permisos** | **Hecho**; 1 defecto corregido | URL forzada por rol (30 rutas, 7 roles + anónimo): sin anomalías. Revocación de sesión en caliente verificada. H-35 sin regresión (CI SQL Server). CSRF por tests de contrato. **Defecto real corregido (#275):** nombres de usuario `...`/`---` | Decisión sobre el rol `vendedor2` (**propietario**, documentada, sin tocar). **P-01 no verificado** (ver 3) |
| **F4 — P-02 IVA** | **Parcial: análisis, sin implementar** (correcto: nada está autorizado) | Matriz de diferencias por pregunta; circuito actual por componente; hallazgo: **los comprobantes son internos** (sin CAE ni integración ARCA/AFIP) | Las 8 respuestas del contador y la pregunta sobre facturación fiscal externa. Sin ellas no se implementa ni clasifica nada |
| **F5 — Cierre** | **Parcial** | Regresión integral = CI de `main` (build-test Linux y Windows, 4 jobs SQL Server con RCSI 0/1, Playwright 1366x768, secret-scan, docker). Este informe | Ver sección 4 |

## 2. Qué se puede cerrar ahora y qué no

**Cerrado y verificado:** F1 (salvo los 6 clasificados), F2 (lo medible en QA), F3 (lo medible en QA), gate de omitidos, gate de CSP.

**Se puede cerrar con una decisión corta del propietario (sin contador):**
1. `vendedor2`: A retirar `ventas.authorize` / B eliminar el rol / C mantener (`docs/seguridad-cierre-f3.md` §2).
2. ¿Las facturas fiscales se emiten fuera del ERP? (`docs/iva-matriz-diferencias.md` §1). Define si P-02 es un ajuste de reportes y leyendas o un proyecto de facturación electrónica (fuera de alcance).
3. Autorizar la ventana de `Enforce` en observación (ítem 8 de `docs/csp-enforce-pendientes.md`).

**Necesita al contador:** las 8 preguntas de `docs/iva-decisiones-contador.md`. Con sus respuestas, cada fila de la matriz pasa a «implementable» o «requiere aclaración».

**Necesita trabajo adicional (no bloqueado por decisiones):**
- Excepción documental: aislar `ValidarDocumentacion` por spec o agregar un doble de BCRA (el stack CI no tiene internet).
- T5 del simulador: sembrar planes de tarjeta evaluando el impacto sobre otros specs.
- Modales remotos de Producto/Proveedor/Catálogo y estados sin datos (CSP ítems 2 y 5), con el harness `scripts/qa/csp-sintetico/` en un clon.
- Sondeo de autorización por URL forzada con POST y parámetros de otro usuario (hoy solo GET).

## 3. Límites que declaro (nada de esto se afirma)

- **Notion:** esta sesión no tiene conector. No leí el tablero maestro, la tarea P-02, las ocho respuestas del contador ni el ítem P-01, y **no actualicé Notion**. Lo hecho está en `docs/` y en los PR; el tablero hay que actualizarlo desde una sesión con acceso, o pegándome su contenido.
- **P-01:** no pude identificarlo; no afirmo que no tenga regresiones.
- **Un check no requerido falló una vez:** `H10-B13 [B13e]` (job FIN SQL Server concurrencia) en la ejecución del PR #275; pasó en reejecuciones del mismo código y en `main`. Se fusionó porque no es un check requerido y la causa es independiente del cambio (solo validación de usuario). Es un **flaky intermitente sin diagnosticar**; conviene una tarea propia.
- **«Sin defectos abiertos dentro del alcance aprobado» no puede afirmarse todavía:** quedan los puntos de la sección 2 y los omitidos listados. Lo que sí se puede decir: no hay defectos conocidos sin registrar.
- **Escritorio y móvil:** el recorrido de roles y de CSP corre a 1366x768 en 3 navegadores; el móvil lo cubren los specs de accesibilidad/responsive existentes en el viewport de CI (la suite de CI corre 1366x768; los demás viewports se ejecutan por pedido). No hay una corrida móvil completa en este cierre.
- **SQL Server QA de circuitos comerciales completos:** los cubren los jobs SQL Server de CI (concurrencia, FIN-S-002, ConfigurarVenta, SuperAdmin) y los E2E de CI (venta → confirmar → facturar, caja, crédito, mora, cotizador). No se ejecutó en esta sesión un barrido manual nuevo de punta a punta sobre un clon de la base real.

## 3b. Estado por módulo (cambios respecto de `docs/aceptacion-erp.md`)

Sin cambios de estado de aceptación: no se modificó funcionalidad de negocio. Lo nuevo por módulo:

| Módulo | Novedad de esta ronda |
|---|---|
| Ventas y Cotizaciones | Conversión manual de cotización (T9–T12) ahora cubierta en CI; antes omitida |
| Caja | `caja-abrir-monto-inicial` (3 tests) ahora cubierto en CI |
| Mora y Configuración | Punitorios: 6 escenarios de gestión/retroactividad ahora cubiertos en CI; spec corregido |
| Crédito / Cotizador | `aptitud-contradiccion` (cliente No apto) ahora cubierto; excepción documental sigue omitida |
| Usuarios y Roles / Seguridad | Nombre de usuario con al menos una letra o un número; evidencia de revocación y URL forzada; `vendedor2` pendiente |
| Infraestructura | CSP: gates de CI y evidencia de `Enforce` |
| Reportes / IVA | Sin cambios; matriz de diferencias lista para las respuestas del contador |

## 4. Pendientes externos (no dependen del software)

Contador (IVA, 8 preguntas); decisión de `Enforce` y su ventana; `vendedor2`; copia fuera del servidor, IP de VPN y firewall del host, licencia AutoMapper (ya diferidos por el propietario); actualización del tablero de Notion.

## 5. Trazabilidad

| PR | Contenido |
|---|---|
| #273 | C-03: clasificación de omitidos, 10 habilitados, gate de omitidos |
| #274 | D-05: gate de cero violaciones, recorridos por rol/navegador, impresión, HTTPS + SignalR |
| #275 | Nombre de usuario exige al menos una letra o un número |
| #276 | F3 evidencia + F4 matriz de diferencias |
| #277 | C-03: T9–T12 de conversión con cotizaciones sembradas (omitidos 10 → 6) |

Cada PR: pruebas, revisión independiente de Codex (hallazgos P1/P2 corregidos: `tee` sin `pipefail`, impresión que pasaba sin botones, sondeo con inventario vacío, skip silencioso con fixture consumido), CI verde y fusión sin `--admin`.
