# C-03 — Clasificación de los E2E omitidos

Fecha: 2026-11-10. Base: `main` 61f5f886 (corrida de CI 42541881748: 203 correctos, **20 omitidos**, 0 fallidos). Reproducido en local con la réplica del stack CI (SQL Server + app Docker, mismos seeds, 1366x768): los mismos 20 specs se omiten por las mismas causas.

**Regla:** un test omitido no es un test aprobado. Esta clasificación existe para que ningún omitido quede sin explicación y para que un omitido nuevo rompa CI.

## 1. Resultado

| Estado | Cantidad |
|---|---:|
| Omitidos originales | 20 |
| **Pasaron a ejecutarse en CI (esta tarea)** | **10** |
| Siguen omitidos, clasificados | 10 |

## 2. Detalle por spec

| Spec / test | Causa del skip (leída del código y medida) | Clase | Resolución |
|---|---|---|---|
| `caja-abrir-monto-inicial` ×3 | `/Caja/Abrir` no muestra el selector si no hay una caja activa sin turno abierto; el dataset CI no la tenía | Ejecutable con datos | **Resuelto**: `OmitidosE2ESeeder` crea la caja `E2ECAJALIBRE`. Pasan los 3 |
| `configuracion-punitorio` ×6 (retroactividad, versiones, error del servidor) | Exigen `configuracion.managepunitorio` y `retroactivepunitorio`, que Administrador **no** recibe a propósito (`docs/permisos-matriz-roles.md`) | Ejecutable con permisos | **Resuelto**: el seeder otorga esos dos permisos al usuario E2E solo en la base efímera. La matriz real sigue cubierta por `ConfiguracionPunitorioHttpTests`. Pasan los 6 |
| `cotizacion-credito-personal-aptitud-contradiccion` ×1 | Cliente con DNI numérico "No apto" (`E2E_CLIENTE_NOAPTO_DNI`, por defecto 35996614) inexistente en CI | Ejecutable con datos | **Resuelto**: el seeder crea el cliente `90000502` (BCRA OK, mora de capital de 90 días → "No apto" real) y exporta la variable. **Pasa**. No se usa 35996614 porque `cliente-wizard-nuevo` lo da de alta por UI |
| `configuracion-punitorio` «sin permiso específico… no se renderiza» ×1 | El usuario E2E sí tiene `viewpunitorio`; el test niega acceso | Negativo por diseño | Cubierto por la matriz xUnit. Queda omitido y listado |
| `cotizacion-conversion` T9–T12 ×4 | `COTIZACION-SIMULAR-GUARDAR-DIRECTO-01`: guardar con cliente del sistema encadena a `Venta/Edit`; la cotización «Emitida sin convertir» en Detalles ya no se alcanza desde el simulador | Obsoleto (contrato cambiado por decisión de producto) | Pendiente reescribir sembrando cotizaciones Emitidas (una por test). Queda listado |
| `cotizacion-credito-personal-excepcion` ×1 | Necesita `ValidarDocumentacion=true` (el dataset compartido la desactiva a propósito) y BCRA alcanzable (el stack CI no tiene internet). Probado: con el cliente sembrado y la validación apagada resulta «Apto» | Pendiente de configuración | Requiere aislar la configuración de crédito del spec o un doble de BCRA. Queda listado |
| `cotizacion-simulador` T5 ×1 | `ConfiguracionPagoPlanes` está vacía en el dataset: ningún medio con varios planes | Pendiente de configuración | Sembrar planes de tarjeta modifica la configuración global que usan otros specs; se evalúa aparte. Queda listado |
| `pr75-mora-visual-evidence` ×3 | `E2E_MORA_VISUAL=1`: es evidencia visual con seed y workflow propios (`pr75-mora-visual.yml`) | Evidencia con workflow propio | Fuera de esta suite por diseño |

## 3. Defecto real encontrado al habilitar los specs

`configuracion-punitorio` calculaba la fecha «retroactiva» en **UTC**, pero el servidor decide con el **día comercial (UTC-3)**. Entre 00:00 y 03:00 UTC «ayer UTC» ya era el día comercial de hoy: el formulario no abría el modal, el servidor aceptaba la versión como no retroactiva y esa versión consumía el dataset append-only, rompiendo los tests siguientes. Es un defecto del spec (no del producto), corregido con `fechaComercial()`. Estaba oculto porque el spec se omitía en CI.

## 4. Control que evita que vuelva a pasar

`scripts/qa/control-omitidos.js` (paso «Control de omitidos» del workflow) lee el JSON de Playwright y falla si hay un omitido que no figure en `scripts/qa/omitidos-esperados.json`, si el reporte está vacío, o si hay más omitidos que los clasificados. Un esperado que deja de omitirse solo avisa (hay que actualizar la lista).

## 5. Cómo reproducir en local

Stack: `docker compose -p <proyecto> -f docker-compose.yml -f docker-compose.ci.yml up -d --build app` con un `.env` sintético (ver `docs/ci-playwright-e2e.md`). Seeds, en orden: `GenericE2ESeedRunner`, `ClienteAptitudPunitorioE2ESeedRunner`, `DescuentoPermisoE2ESeedRunner`, `OmitidosE2ESeedRunner` (`dotnet test --filter "FullyQualifiedName~<Runner>.Sembrar"` con `E2E_SEED_CONNECTION`). Luego `npx playwright test --project=1366x768` con las variables de `e2e/.auth/*-seed-ids.json`.

Nota: los specs de punitorios son **no idempotentes** sobre una misma base (versionado append-only): repetir la suite exige base nueva.

## 6. Pendientes de esta línea

1. Reescribir T9–T12 de conversión con cotizaciones sembradas.
2. Excepción documental: aislar configuración de crédito o doble de BCRA.
3. T5: decidir si se siembran planes de tarjeta (impacto en otros specs).
