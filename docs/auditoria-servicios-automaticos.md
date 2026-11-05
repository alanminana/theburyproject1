# Auditoría de servicios automáticos (BackgroundService)

Auditoría de preproducción, 2026-11-05, sobre `main` 629d7331 + los cambios de esta rama. Alcance: los 7 `BackgroundService` registrados
en `Program.cs`. Todos corren mientras la aplicación esté viva, **incluso con `Database:InitializeOnStartup=false`** (esa clave solo evita
migraciones/seed). Una sola instancia de la app es la configuración soportada (`docs/despliegue-produccion.md` §8).

## Resumen

| Servicio | Cuándo corre | Estado por defecto | Escribe | Idempotente | Recomendación antes de operar |
|---|---|---|---|---|---|
| `MoraBackgroundService` | Revisa cada 10 min; ejecuta 1 vez por día comercial a la hora configurada (`ConfiguracionMora.HoraEjecucionDiaria`, 08:00) ±10 min, solo si `ProcesoAutomaticoActivo` | **Activo** (`JobActivo=1`) | `AlertasCobranza`, `LogsMora`, estado de `Cuotas` (si `CambiarEstadoCuotaAuto`), score de cliente (si `ImpactarScorePorMora`), `ConfiguracionMora.UltimaEjecucion` | Sí (ver abajo) | Decisión del dueño: puede quedar activo (sin créditos no hace nada) o apagarse desde Configuración de Mora hasta tener créditos reales |
| `AcuerdoIncumplimientoBackgroundService` | 1 min tras el arranque y luego cada hora; actúa 1 vez por día comercial | Activo | Acuerdos de pago (incumplimiento), promesas vencidas | Sí | Sin acuerdos no hace nada |
| `BorradorCreditoVencimientoBackgroundService` | Cada hora, **solo** si `BorradoresCredito:VencimientoAutomaticoHabilitado=true` | **Apagado** (false; valor inválido también apaga) | Cancela borradores de crédito vencidos | Sí (lock de crédito/venta + re-verificación) | Dejar apagado: cambiarlo es una decisión comercial |
| `AlertaStockBackgroundService` | 30 s tras el arranque y cada 2 h; limpieza diaria | Activo | `AlertasStock` (alta; limpieza de las de más de 30 días) | Sí (no duplica alertas abiertas) | Seguro: solo genera avisos |
| `DocumentoVencidoBackgroundService` | Diario 02:00 UTC | Activo | `DocumentosCliente.Estado` Verificado→Vencido (un `UPDATE` masivo) | Sí (solo toca Verificados vencidos) | Seguro |
| `CotizacionVencimientoBackgroundService` | Diario 03:00 UTC | Activo | `Cotizaciones.Estado` Emitida→Vencida | Sí; transacción + `RowVersion` (una conversión concurrente produce conflicto y se reintenta al día siguiente) | Seguro |
| `MercadoLibreWebhookBackgroundService` | Cada 30 s | Activo salvo `MercadoLibre:Webhook:Enabled=false` (sin integración ML no encuentra eventos) | `MercadoLibreWebhookEvents` y lo que procese ML | Sí (agrupa por topic+resource; reintentos acotados) | Mercado Libre queda **fuera de alcance**: usar `MercadoLibre__Webhook__Enabled=false` en producción |

## Mora (el único con efecto sobre datos de negocio relevante)

- **Qué hace** (`MoraService.ProcesarMoraAsync`): selecciona cuotas vencidas no terminales con saldo de capital, y por crédito
  crea **una** alerta de cobranza (si ya existe una activa, solo refresca `DiasAtraso`), resuelve las alertas de créditos ya
  regularizados, genera alertas de próximos vencimientos y —si están habilitadas— pasa cuotas a Vencida y ajusta el score. **No cobra, no
  aplica punitorios ni modifica saldos de dinero.** Cada corrida deja un registro en `LogsMora` (también las fallidas).
- **Fallo a mitad de operación**: el trabajo va en un solo `SaveChanges`; si falla se registra `LogMora.Exitoso=false` con el detalle y no
  queda estado parcial. El job vuelve a intentarlo en la siguiente revisión (10 min) mientras siga dentro de la ventana de ±10 min.
- **Defectos corregidos en esta rama** (`Services/MoraBackgroundService.cs`):
  1. La hora configurada se comparaba contra la hora **UTC**: "08:00" corría a las 05:00 de Argentina. Ahora usa la hora comercial
     (`IRelojComercial`).
  2. "Ya se ejecutó hoy" vivía solo en memoria (y mezclaba `DateTime.Today` local con UTC): un reinicio dentro de la ventana repetía la
     corrida. Ahora también se consulta `ConfiguracionMora.UltimaEjecucion` (persistida) por día comercial.
  Cobertura: `MoraBackgroundServiceProgramacionTests` (5 casos: hora comercial vs UTC, reinicio el mismo día, ejecución de ayer, frontera
  de medianoche UTC/Argentina, ejecución en memoria).
- **Pendiente conocido (solo importa con más de una instancia)**: no hay índice único de alerta activa por crédito ni `sp_getapplock`
  alrededor del job; dos corridas *simultáneas* de procesos distintos podrían duplicar alertas. Con una sola instancia no ocurre; no se
  agrega migración sobre la base real sin autorización.
- **Operación**: se apaga desde *Configuración de Mora* (`ProcesoAutomaticoActivo`). Ese parámetro es del negocio; no se modificó.

## Acuerdos, borradores, stock, documentos, cotizaciones

- Todos capturan excepciones por ciclo y siguen vivos; ninguno borra datos de negocio ni mueve dinero.
- `AcuerdoIncumplimiento`: serializa con `SemaphoreSlim` por proceso y `RowVersion` entre procesos; si un acuerdo falla no se da el día por
  cumplido y se reintenta en la hora siguiente.
- `BorradorCreditoVencimiento`: falla **cerrado** (apagado ante cualquier valor distinto de `true`).
- `AlertaStock`: tras reiniciar espera 30 s y ejecuta; es idempotente.
- `DocumentoVencido` y `CotizacionVencimiento` usan hora UTC para *programar* la ejecución (23:00 y 00:00 de Argentina). El criterio de
  vencimiento lo calcula el servicio, no el reloj de programación; el efecto es solo cuándo corre. No se modificó.

## Webhook de Mercado Libre

- `POST /api/mercadolibre/webhook` es anónimo por contrato de ML, con *rate limit* de 1.000/min y tope de 64 KB por cuerpo. Con ML fuera de
  alcance, esto es superficie expuesta sin uso. Ver "Interruptor" abajo.

## Interruptor del webhook de Mercado Libre (agregado en esta rama)

`MercadoLibre:Webhook:Enabled` (variable de entorno `MercadoLibre__Webhook__Enabled`). Por defecto **habilitado** (no cambia nada si no se
configura). Con `false`: el endpoint anónimo `/api/mercadolibre/webhook` responde 404 (no persiste eventos) y el consumidor en segundo plano
no arranca. Un valor ilegible conserva el comportamiento actual. Recomendado `false` en el despliegue de producción mientras ML esté fuera de
alcance. Cobertura: `MercadoLibreWebhookInterruptorTests`.

## Qué NO se hizo (requiere decisión del dueño)

- Apagar o dejar encendido el job de mora (parámetro comercial).
- Habilitar el vencimiento automático de borradores de crédito.
