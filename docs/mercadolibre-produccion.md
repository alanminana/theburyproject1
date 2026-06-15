# Mercado Libre — Runbook de producción segura (Fase 20)

> Objetivo: salida productiva controlada del canal Mercado Libre **sin activar
> escrituras reales por defecto**. El ERP arranca en modo simulación: calcula y
> registra lo que enviaría, pero no llama a la API de ML para escribir.
>
> Regla de oro: **ninguna acción real ocurre sin decisión explícita** (flag de
> configuración + confirmación por acción). El backend es el guardián, no la UI.

---

## 1. Modelo de seguridad (cómo está construido)

- **Tokens cifrados** con ASP.NET Data Protection (`MercadoLibreTokenProtector`,
  purpose versionado `...Tokens.v1`). Nunca se persisten en claro.
- **Refresh token rotado**: ML invalida el refresh anterior en cada uso; el ERP
  guarda siempre el nuevo (`AuthService.AplicarTokens`). Los refresh se serializan
  con un `SemaphoreSlim` para evitar dos refresh concurrentes.
- **Sin tokens en logs**: el body de `/oauth/token` nunca se loguea; los headers
  `Authorization` nunca se loguean; los logs solo registran `user_id`, `nickname`,
  `accountId` y vencimientos.
- **Backend gate, no solo UI**: cada acción real está bloqueada en el servicio por
  `ModoSimulacion` (+ confirmación explícita para preguntas/mensajes/precios). La UI
  solo refleja el estado; aunque alguien fuerce el POST, el servicio corta.
- **Antiforgery + permisos por acción**: todos los POST llevan
  `[ValidateAntiForgeryToken]` y `[PermisoRequerido(Modulo="mercadolibre", Accion=...)]`.
- **Idempotencia en DB**: índices únicos sobre `ItemId`, `(ListingId,VariationId)`,
  `MeliOrderId`, `QuestionId`, `MessageId`. Los webhooks se deduplican por
  `(topic, resource)` y los reintentos están acotados (`MaxIntentos = 5`).

---

## 2. Configurar credenciales (NUNCA hardcodear)

`ClientId`, `ClientSecret` y `RedirectUri` van en **UserSecrets (dev)** o
**variables de entorno (prod)**, sección `MercadoLibre`. En `appsettings.json` se
dejan vacíos a propósito.

Dev (UserSecrets):

```bash
dotnet user-secrets set "MercadoLibre:ClientId" "<APP_ID>"
dotnet user-secrets set "MercadoLibre:ClientSecret" "<SECRET>"
dotnet user-secrets set "MercadoLibre:RedirectUri" "https://<host>/MercadoLibre/OAuthCallback"
```

Producción (variables de entorno, doble guion bajo = jerarquía):

```
MercadoLibre__ClientId=<APP_ID>
MercadoLibre__ClientSecret=<SECRET>
MercadoLibre__RedirectUri=https://<dominio-productivo>/MercadoLibre/OAuthCallback
```

Validación rápida: si falta cualquiera de los tres, `EstaConfigurado` es `false` y
el botón de conexión muestra el motivo. No se puede iniciar OAuth sin las tres.

---

## 3. RedirectUri productiva (no ngrok)

- `RedirectUri` debe coincidir **exactamente** con la registrada en Mercado Libre
  Developers (mismo esquema, host y path).
- **ngrok es solo para desarrollo.** En producción usar el dominio real con HTTPS:
  `https://<dominio-productivo>/MercadoLibre/OAuthCallback`.
- Si se cambia el host, actualizar **ambos lados** (ML Developers y la variable de
  entorno) o el callback fallará por `redirect_uri` mismatch.

---

## 4. Webhook productivo

- Endpoint: `POST /api/mercadolibre/webhook` (controller `MercadoLibreWebhookController`,
  `[AllowAnonymous]` porque ML no envía credenciales).
- Contrato: **persistir el evento crudo y responder 200 lo más rápido posible.** El
  procesamiento real corre después, idempotente, en `MercadoLibreWebhookBackgroundService`
  (cada 30 s).
- Nunca devuelve 5xx evitable: si falla la persistencia, loguea y responde 200 igual
  (ML reintenta y deshabilita webhooks con muchos fallos).
- Configurar en ML Developers la URL productiva (no ngrok) y los topics:
  `orders_v2`, `items`, `shipments`, `questions`, `messages`, `claims`.
- Topics desconocidos se guardan y se marcan procesados sin acción (no rompen la cola).

> Endurecimiento recomendado (deuda, ver §9): validar el `user_id`/origen del webhook
> y/o un secreto compartido a nivel proxy. Hoy el endpoint confía en el contenido y
> resuelve la cuenta por `user_id`.

---

## 5. Flags de configuración — matriz de seguridad

Configuración en `MercadoLibre > Configuración` (entidad `MercadoLibreConfiguracion`,
fila única `GetOrCreate`).

| Flag | Dev | Prod inicial | Riesgo si se activa | Validar antes de activar |
|------|-----|--------------|---------------------|--------------------------|
| `ModoSimulacion` | `true` | **`true`** | Las acciones impactan ML real (precio/stock/publicación/mensajes) | QA real read-only OK; operador entiende el cambio; backup DB |
| `PermitirPublicacionDesdeErp` | `false` | **`false`** | Crea publicaciones reales en ML | Borradores validados; mapeo categoría/atributos correcto; 1 publicación piloto |
| `SyncAutomaticaStock` | `false` | **`false`** | Webhook `items` re-empuja stock ERP→ML automáticamente | Vínculo publicación↔producto correcto; origen de stock revisado |
| `SyncAutomaticaPrecio` | `false` | **`false`** | Webhook `items` re-empuja precio ERP→ML automáticamente | Lista de precios + ajuste de canal + redondeo revisados |
| `CrearVentaAutomatica` | `false` | **`false`** | Importar una orden paga crea Venta interna y **descuenta stock** | Cliente ML, sucursal y mapeo de items validados; conciliación caja revisada |
| `ImportacionAutomaticaOrdenes` | `true` | `true` (lectura) | Importa órdenes ML→ERP al recibir webhook. **Solo lectura/persistencia local**, no escribe a ML ni crea Venta (eso depende de `CrearVentaAutomatica`) | Aceptable en primera salida; revisar que el cliente/cuenta default exista |
| `PoliticaDevolucion` | `PendienteRevision` | `PendienteRevision` | — (es sugerencia; la decisión es manual) | — |

Acciones reales sin flag persistente (gate por acción, requieren `ModoSimulacion=false`
**y** confirmación explícita en el momento):

| Acción | Gate |
|--------|------|
| Responder pregunta real | `ModoSimulacion=false` **Y** `confirmarReal=true` (doble cerrojo) |
| Responder mensaje real | `ModoSimulacion=false` **Y** orden no simulada |
| Aplicar lote de precios real | `ModoSimulacion=false` **Y** `confirmarReal=true` |
| Publicar borrador real | `PermitirPublicacionDesdeErp=true` **Y** `ModoSimulacion=false` **Y** borrador validado **Y** cuenta activa |
| Push stock/precio (sync) real | `ModoSimulacion=false` **Y** `confirmarReal=true` |
| Liquidación | Acción manual (operador ingresa neto real); ordenes simuladas locales exigen `ModoSimulacion=true` |

> No existen flags separados `ResponderPreguntasReal` / `ResponderMensajesReal` /
> `LiquidacionAutomatica` / `ProcesarWebhooksAutomatico`: el envío real de
> preguntas/mensajes está protegido por el doble cerrojo por acción, la liquidación
> es siempre manual, y el procesamiento de webhooks nunca escribe a ML salvo que los
> flags de sync estén activos.

---

## 6. Orden de activación segura (gradual)

1. **Día 0 — solo lectura.** `ModoSimulacion=true`. Conectar cuenta (OAuth), probar
   conexión, importar publicaciones, revisar Dashboard. Nada se escribe a ML.
2. **Importación de órdenes.** `ImportacionAutomaticaOrdenes=true` (default). Verificar
   que las órdenes entran al ERP correctamente. Sigue sin crear Venta ni tocar stock.
3. **Venta interna controlada.** Si se desea automatizar la Venta: activar
   `CrearVentaAutomatica` solo tras validar cliente ML, sucursal, mapeo de items y
   conciliación de caja. (Alternativa: dejar manual y crear la Venta desde la orden.)
4. **Precios.** Probar un lote de precios en simulación, revisar el payload, luego
   aplicar real con confirmación explícita (sin activar `SyncAutomaticaPrecio` todavía).
5. **Sync automático.** Activar `SyncAutomaticaStock` / `SyncAutomaticaPrecio` solo
   cuando el vínculo publicación↔producto esté validado en todas las publicaciones.
6. **Publicación desde ERP.** Activar `PermitirPublicacionDesdeErp` al final, con
   borradores validados y una publicación piloto.

> En cada paso: salir de `ModoSimulacion` impacta **todo** el canal a la vez. No
> apagar simulación "para probar una sola cosa": probar primero con la acción
> simulada o en Development.

---

## 7. Operación diaria

- **Probar conexión:** `MercadoLibre > Index > Probar conexión` (hace `GET /users/me`,
  no escribe nada).
- **Importar publicaciones:** `MercadoLibre > Publicaciones > Importar` (lectura ML→ERP,
  idempotente por `ItemId`).
- **Dashboard:** `MercadoLibre > Dashboard` (solo lectura; muestra banner cuando
  `ModoSimulacion` está desactivado).
- **Revisar SyncLogs:** cada acción (real o simulada) queda en `MercadoLibreSyncLogs`
  con operación, fecha y resultado. Filtrar por operación/fecha para auditar.
- **Revisar webhooks:** `MercadoLibreWebhookEvents` guarda el `RawBody` crudo, `Topic`,
  `Procesado`, `IntentosProcesamiento`. Eventos agotados (`Procesado=true` con error)
  quedan auditados sin bloquear la cola.

### Volver a modo simulación (rollback inmediato)

`MercadoLibre > Configuración > ModoSimulacion = true > Guardar`. A partir de ese
momento ninguna acción vuelve a escribir a ML. Es el botón de pánico.

### Detectar tokens filtrados

- Buscar en logs cualquier ocurrencia de `access_token`, `refresh_token`, `Bearer ` o
  el body de `/oauth/token`. **No debe haber ninguna.** Si aparece, es un bug: cortar
  y corregir antes de continuar.
- Test de regresión que cubre esto: los servicios de preguntas/mensajes en simulación
  no llaman a la API (los contadores del fake quedan en 0); ver
  `MercadoLibreQuestionMessageServiceTests` y `MercadoLibreProductionDefaultsTests`.

---

## 8. Qué NO hacer en la primera salida

- No apagar `ModoSimulacion` el primer día.
- No activar `PermitirPublicacionDesdeErp`, `SyncAutomaticaStock`, `SyncAutomaticaPrecio`
  ni `CrearVentaAutomatica` sin validación previa por flag (ver §5/§6).
- No usar ngrok como `RedirectUri` ni como webhook productivo.
- No publicar productos reales, cambiar precio/stock real ni enviar mensajes reales
  como "prueba". Para probar, usar la acción **simulada** o el entorno Development.
- No desplegar sin backup de la base.

---

## 9. Deuda / endurecimiento recomendado (no bloqueante)

- **Persistencia de claves de Data Protection.** Los tokens se cifran con Data
  Protection. Si las claves no se persisten en una ubicación estable (hoy se usa el
  default del host), un reinicio/redeploy puede invalidar la clave y obligar a
  **re-autorizar la cuenta** (OAuth de nuevo). No hay pérdida de datos, pero conviene
  configurar `PersistKeysToFileSystem` (o key ring compartido) en producción. Cambio
  transversal a toda la app → decidir con criterio de infraestructura.
- **Seguridad del webhook.** Validar origen/`user_id` o secreto compartido a nivel
  proxy (ver §4).
- **Índice por `Resource` en `MercadoLibreWebhookEvents`** (hoy hay índice por `Topic`,
  `Procesado`, `RecibidoUtc`). No crítico al volumen esperado. Nota: la deduplicación
  por `(Topic, Resource)` se resuelve **en memoria** (`GroupBy` tras `ToListAsync`), no
  con un `WHERE`/`JOIN` por `Resource`, así que un índice sobre `Resource` no acelera
  el camino actual; agregarlo solo tendría sentido si se introduce una consulta que
  filtre por `Resource` en DB.
- **Cosmético — detalle ABM a 1 columna en desktop.** Las vistas de detalle ML
  (`Listing`, `Orden`, `Configuracion`, `Borrador`, `AumentoNuevo`, `ListingCrearProducto`)
  usan `xl:grid-cols-2`, clase que **no está compilada** en `wwwroot/css/tailwind.css`
  (solo existen `xl:grid-cols-3/4` y `lg:grid-cols-2`). Resultado: el contenedor de dos
  paneles queda en 1 columna también en pantallas anchas. No afecta funcionalidad ni
  produce overflow (la base es `grid-cols-1`). Resolución segura sin recompilar Tailwind:
  cambiar `xl:grid-cols-2` → `lg:grid-cols-2` (clase ya compilada) y validar la matriz de
  viewports con browser real. Se deja como deuda cosmética no bloqueante.

---

## 10. Checklist go / no-go

**GO** si todo esto se cumple:

- [ ] `dotnet build -c Release` OK.
- [ ] Tests ML OK (`--filter FullyQualifiedName~MercadoLibre`).
- [ ] Dashboard carga OK.
- [ ] `ModoSimulacion = true`.
- [ ] `PermitirPublicacionDesdeErp = false`.
- [ ] `SyncAutomaticaStock = false` y `SyncAutomaticaPrecio = false`.
- [ ] No hay tokens en logs.
- [ ] Webhook productivo configurado (URL real, no ngrok).
- [ ] Backup de DB realizado.
- [ ] El operador entiende los flags y el botón de pánico (§7).
- [ ] Primer día: solo lectura / importación.

**NO-GO** si ocurre cualquiera de esto:

- [ ] Tokens expuestos en logs o respuestas.
- [ ] Errores de OAuth / refresh sin resolver.
- [ ] Sync de stock/precio automático activo sin validación.
- [ ] Publicaciones sin vínculo a producto cuando se va a sincronizar.
- [ ] Órdenes reales podrían crear Venta automáticamente sin revisión
      (`CrearVentaAutomatica=true` sin validar conciliación).
- [ ] Webhooks fallan o duplican.
- [ ] No hay backup de DB.
