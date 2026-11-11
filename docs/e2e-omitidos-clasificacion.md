# C-03 — Clasificación de los E2E omitidos

Fecha: 2026-11-10. Base: `main` 61f5f886 (corrida de CI 42541881748: 203 correctos, **20 omitidos**, 0 fallidos). Reproducido en local con la réplica del stack CI (SQL Server + app Docker, mismos seeds, 1366x768): los mismos 20 specs se omiten por las mismas causas.

**Regla:** un test omitido no es un test aprobado. Esta clasificación existe para que ningún omitido quede sin explicación y para que un omitido nuevo rompa CI.

## 1. Resultado

| Estado | Cantidad |
|---|---:|
| Omitidos originales | 20 |
| **Pasaron a ejecutarse en CI (esta tarea)** | **14** |
| Siguen omitidos, clasificados | 6 |

## 2. Detalle por spec

| Spec / test | Causa del skip (leída del código y medida) | Clase | Resolución |
|---|---|---|---|
| `caja-abrir-monto-inicial` ×3 | `/Caja/Abrir` no muestra el selector si no hay una caja activa sin turno abierto; el dataset CI no la tenía | Ejecutable con datos | **Resuelto**: `OmitidosE2ESeeder` crea la caja `E2ECAJALIBRE`. Pasan los 3 |
| `configuracion-punitorio` ×6 (retroactividad, versiones, error del servidor) | Exigen `configuracion.managepunitorio` y `retroactivepunitorio`, que Administrador **no** recibe a propósito (`docs/permisos-matriz-roles.md`) | Ejecutable con permisos | **Resuelto**: el seeder otorga esos dos permisos al usuario E2E solo en la base efímera. La matriz real sigue cubierta por `ConfiguracionPunitorioHttpTests`. Pasan los 6 |
| `cotizacion-credito-personal-aptitud-contradiccion` ×1 | Cliente con DNI numérico "No apto" (`E2E_CLIENTE_NOAPTO_DNI`, por defecto 35996614) inexistente en CI | Ejecutable con datos | **Resuelto**: el seeder crea el cliente `90000502` (BCRA OK, mora de capital de 90 días → "No apto" real) y exporta la variable. **Pasa**. No se usa 35996614 porque `cliente-wizard-nuevo` lo da de alta por UI |
| `configuracion-punitorio` «sin permiso específico… no se renderiza» ×1 | El usuario E2E sí tiene `viewpunitorio`; el test niega acceso | Negativo por diseño | Cubierto por la matriz xUnit. Queda omitido y listado |
| `cotizacion-conversion` T9–T12 ×4 | `COTIZACION-SIMULAR-GUARDAR-DIRECTO-01`: guardar con cliente del sistema encadena a `Venta/Edit`; la cotización «Emitida sin convertir» en Detalles ya no se alcanza desde el simulador, pero el modal de conversión manual sigue existiendo | Ejecutable con datos (el contrato cambió, la pantalla no) | **Resuelto**: `OmitidosE2ESeeder` siembra 4 cotizaciones «Emitida» con cliente (una por test, T11 con descuento por importe de $50) y el spec las abre en Detalles (`E2E_COTIZACION_CONV_IDS`). Pasan los 4; en la base: las 4 quedan `ConvertidaAVenta` con su venta (T11 total 1.950). Una cotización convertida no sirve para repetir: base nueva por corrida |
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

1. Excepción documental: aislar configuración de crédito o doble de BCRA.
2. T5: decidir si se siembran planes de tarjeta (impacto en otros specs).

---

## 7. Actualización 2026-11-11 (F1.1 / F1.2, base `main` 29dff9d9)

Estado previo: 217 correctas, 6 omitidas. Estado resultante: ver la tabla de cifras al final de esta sección (corrida local replicando CI; la cifra de CI la confirma el PR).

### 7.1 Los dos omitidos «pendiente de configuración» pasan a ejecutarse

| Spec | Causa raíz (medida) | Resolución |
|---|---|---|
| `cotizacion-credito-personal-excepcion` | Además de lo ya documentado (validación documental apagada, BCRA sin internet) había **tres defectos del propio spec** ocultos por el skip: (a) el selector `hasText: 'Credito personal'` no coincide con la etiqueta real «Crédito personal»; (b) con planes de Crédito personal la fila es un grupo y la acción «Solicitar excepción» vive en cada plan hijo (`tr.detail[data-g]`); (c) el flujo de «Continuar con excepción» cambió (modal «Confirmar venta a crédito»: fecha de 1ª cuota, «Generar contrato», «Confirmar venta»), el spec esperaba el viejo bloqueo «Continuar con wizard»; (d) el locator del panel `section` con `.first()` tomaba la sección exterior | Spec actualizado al flujo real. Cliente sembrado `90000503` (`OmitidosE2ESeeder`): sin documentos, sin créditos, consulta BCRA «OK» vigente (el cache de 7 días del servicio **real** evita la llamada a internet: no hay doble ni código de producto nuevo). La configuración que falta se activa solo en un paso aislado (7.3) |
| `cotizacion-simulador` T5 | No existe el medio Tarjeta de crédito en el dataset (`ConfiguracionPagoPlanes` vacía): ningún medio produce un grupo con varios planes | Mismo paso aislado: medio Tarjeta de crédito, tarjeta «E2E Tarjeta Planes» y planes de 1/3/6 cuotas (+5 %/+12 %/+20 %) |

### 7.2 Por qué no se siembra en el dataset común

Se midió: no existe ruta HTTP/UI que edite `ConfiguracionCredito` (`UpdateConfiguracionAsync` no tiene controlador), y sembrar planes de tarjeta, Crédito personal y `ValidarDocumentacion=true` en la base compartida cambiaría el comportamiento de todos los specs de Cotización/Venta (el dataset apaga la validación a propósito). En vez de demostrar que «nada cambia», se evita el riesgo: **la suite principal corre exactamente con el dataset de antes**.

### 7.3 Paso aislado `@config-aislada`

- `ConfiguracionAisladaE2ESeedRunner.Activar` / `.Restaurar` (`TheBuryProyect.Tests/E2ESeeding/ConfiguracionAisladaE2ESeeder.cs`): activa `ValidarDocumentacion=true`, tasa y planes de Crédito personal y los planes de tarjeta; `Restaurar` deja la base como el dataset común (solo borra lo marcado por el seeder; verificado en SQL: `ValidarDocumentacion=0`, 0 planes de crédito, tasa NULL, 0 planes de pago, 0 tarjetas).
- `playwright.yml`: la corrida principal usa `--grep-invert "@config-aislada"`; luego `Activar` → segunda corrida `--grep "@config-aislada"` (con `--reporter`/`--output` propios para no pisar el reporte HTML ni las trazas) → `Restaurar` con `if: always()`. Playwright usa `workers: 1` (`playwright.config.js`) y la segunda corrida solo ejecuta esos tests: nada corre en paralelo mientras la configuración está activa.
- Tests `@config-aislada`: excepción documental, T5 y `cotizacion-credito-personal-aptitud-contradiccion` (sus aserciones sobre la fila de Crédito personal eran **vacuas**: el mismo selector sin tilde nunca coincidía y estaban bajo `if (count > 0)`).
- Con `E2E_CONFIG_AISLADA=1` (solo el paso de CI) la falta de la configuración es un **fallo**, no un omitido (comprobado: tras `Restaurar`, excepción y T5 fallan con mensaje claro). Sin esa variable (uso local) se conserva el comportamiento anterior.
- `control-omitidos.js` acepta varios JSON (fusiona las dos corridas) e imprime el desglose por clase y la cobertura alternativa.

### 7.4 Los omitidos que quedan (4)

| Spec | Clase | Cobertura |
|---|---|---|
| `configuracion-punitorio` «sin permiso específico…» ×1 | **Negativo por diseño. No cuenta como ejecutado.** El usuario E2E tiene `viewpunitorio` | xUnit `ConfiguracionPunitorioHttpTests`: `CreditoPersonal_SinViewpunitorio_NoRenderizaLaPestanaDePunitorios` (nuevo: renderiza la página real sin la pestaña) y su par `CreditoPersonal_ConViewpunitorio_RenderizaLaPestanaDePunitorios`; denegación de endpoints: `PreviewPunitorio_SinPermiso_DevuelveForbidden`, `CrearVersionPunitorio_SinNingunPermiso_DevuelveForbidden`, `CrearVersionPunitorio_SoloConfiguracionView_SinManagepunitorio_DevuelveForbidden`. Antes solo había un test de contrato sobre el texto de la vista; el render real no estaba cubierto |
| `pr75-mora-visual-evidence` ×3 | **Evidencia con workflow propio** | `.github/workflows/pr75-mora-visual.yml` (`workflow_dispatch`, y `pull_request` con cambios en el spec, `MoraVisual*.cs`, `Views/Mora/**`, `wwwroot/css/mora-*.css` o el propio workflow). Siembra `MoraVisualE2ESeedRunner.Sembrar` y corre solo ese spec con `E2E_MORA_VISUAL=1`. **Corrida 42967619825 (`workflow_dispatch` sobre `main`, 2026-11-11): success** |

### 7.5 F1.2 — estabilidad (hallazgos)

- **Fixtures consumibles T9–T12** (`E2E_COTIZACION_CONV_IDS`): una segunda corrida sobre la misma base no se omite en silencio: el seeder es idempotente y devuelve las mismas cotizaciones, ya `ConvertidaAVenta`; el spec falla con un mensaje claro («fixture ya consumido… usar base nueva por corrida»; T9 fallaba antes por el badge «Emitida» sin explicar nada: ahora tiene el mismo mensaje). Es no idempotente por diseño: base nueva por corrida. Solo se omiten si falta la variable, y en CI el control de omitidos lo convierte en fallo.
- **UTC vs día comercial**: en `e2e/` la única fecha calculada era la de punitorios (ya corregida con `fechaComercial()`; `vigenciaFutura` suma 2 años, sin riesgo). Los DNI con `Date.now()` no dependen de la fecha. Los seeders usan `DateTime.UtcNow` con márgenes de decenas de días (vencimientos −10/−90), insensibles al desfase de 3 h. No se encontró otro defecto. (Fuera de alcance: `min="@DateTime.Today"` en `_CotizadorForm.cshtml` es código de producto.)
- **Skips condicionales** (108 ocurrencias en `e2e/`): todos son por dato/permiso ausente. En CI ninguno puede ocultar un fallo: `control-omitidos.js` falla ante cualquier omitido no clasificado. Los tres sitios donde el skip ocultaba un defecto real del spec (selector sin tilde, `if (count > 0)`) se corrigieron. `ui-4e-layout-visual` usa `if (!ok) test.skip()` por autenticación vencida: queda protegido por el mismo control.

### 7.6 Cifras de la corrida local (réplica de CI, 1366x768, 2026-11-11)

| Corrida | Correctas | Fallidas | Omitidas |
|---|---:|---:|---:|
| Base nueva #1 (principal 216 + aislada 4, incluyen 1 `setup` cada una) | 220 | 0 | 4 |
| Base nueva #2 (idéntica) | 220 | 0 | 4 |
| Misma base ya usada (#3) | 206 | 14 | 4 |

220 correctas = 218 specs + 2 ejecuciones del `setup` de login (antes: 217 = 216 specs + 1 `setup`; ahora +2 specs que dejan de omitirse). Violaciones CSP: 0 en ambas corridas.

**Corrida repetida sobre la misma base (14 fallos): no idempotente por diseño, ninguno es silencioso.** `configuracion-punitorio` ×5 (versionado append-only de punitorios), `cotizacion-conversion` T9–T12 ×4 (cotizaciones ya convertidas), `credito-pago-cuota-individual` ×2 y `credito-punitorio-detalle` ×2 (cuotas/créditos ya consumidos por la primera corrida), `venta-descuento-permiso` ×1 (venta sembrada ya modificada). Se exige base nueva por corrida (como en CI). Los tests `@config-aislada` sí pasan en la repetición.

**Evidencia 4–6 en local:** con `MoraVisualE2ESeedRunner.Sembrar` y `E2E_MORA_VISUAL=1`, los 3 specs de `pr75-mora-visual-evidence` pasan (1366x768, 1920x1080, 390x844).
