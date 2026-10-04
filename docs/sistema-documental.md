# Sistema documental configurable

Contratos, pagarés, recibos, presupuestos y cualquier documento futuro se generan con un único motor
(`Services/Documentos/`), no con código ligado a una pantalla o a un tipo de venta.

```
OPERACIÓN + EVENTO + REGLA + PLANTILLA = DOCUMENTO GENERADO
```

## Piezas

| Concepto | Entidad | Para qué |
|---|---|---|
| Tipo de documento | `TipoDocumento` | Clase (contrato, pagaré, recibo…), su prefijo y secuencia de numeración propia, si admite varios por operación. |
| Plantilla | `PlantillaDocumento` | Un modelo de un tipo (puede haber varios por tipo). Firmantes requeridos, vigencia, copias. |
| Versión | `PlantillaDocumentoVersion` | Contenido **inmutable**. Editar el contenido crea la versión n+1. |
| Regla | `ReglaDocumento` | Evento + condiciones estructuradas → plantilla o paquete. Prioridad, grupo de exclusión, obligatoria, exige firma. |
| Paquete | `PaqueteDocumental` | Documentos que se emiten/imprimen juntos (ej. contrato + pagaré). Siguen siendo documentos independientes. |
| Documento | `DocumentoGenerado` | Registro histórico: contenido ya renderizado, snapshot de datos, plantilla+versión usadas, relaciones (venta, crédito, cuota, pago, cotización), estado, firmas, cancelación. |

Código: `DocumentoService` (motor: reglas → render → numeración → persistencia, ciclo de vida),
`DocumentoContextoBuilder` (entidades → contexto plano), `PlantillaRenderer` (render sin evaluación de
código), `CondicionDocumento*` (condiciones), `DocumentoNumeracionService`, `DocumentoPdfService`,
`DocumentoConfiguracionService` (administración + validación + auditoría), `DocumentoSeeder`.

## Eventos

Los eventos son código (alguien tiene que dispararlos); qué documentos genera cada uno es configuración.

| Evento | Se dispara en |
|---|---|
| `CONTRATO_CREDITO_SOLICITADO` | `ContratoVentaCreditoService.GenerarAsync` (paso previo a confirmar una venta a crédito personal). |
| `VENTA_CONFIRMADA` | `VentaService.ConfirmarVentaAsync` / `ConfirmarVentaCreditoAsync`, antes del commit. |
| `PAGO_REGISTRADO` | `CreditoService`, una vez por pago aplicado (individual y múltiple), antes del commit. |
| `ENTREGA_REALIZADA` | `VentaEnvioService.CambiarEstadoAsync` al pasar a Entregado. |
| `PRESUPUESTO_GENERADO` | Disponible para cotizaciones (sin disparador automático todavía). |

Agregar un evento: una constante en `EventosDocumentales` + una llamada a `ProcesarEventoAsync` en el flujo.

## Reglas

* **Condiciones**: JSON declarativo, nunca código. Hoja `{campo, operador, valor}`; grupo
  `{op: "todas"|"cualquiera", condiciones: [...]}` (hasta 5 niveles). Operadores: `igual, distinto, mayor,
  mayorIgual, menor, menorIgual, contiene, en, noEn, existe, verdadero, falso`. El campo debe existir en el
  catálogo (`CatalogoDocumento`) y estar disponible para el evento; el valor se valida contra su tipo.
* **Prioridad**: mayor número se evalúa primero.
* **Grupo de exclusión**: reglas del mismo evento y grupo son excluyentes; aplica solo la de mayor prioridad
  que coincida (regla específica reemplaza a la general). Sin grupo, aplican todas las que coinciden.
* **Tipos que no admiten múltiples** (contrato, pagaré…): aunque dos reglas coincidan se emite un único
  documento vigente por operación (gana la de mayor prioridad).
* **Obligatoria**: si el documento no puede generarse el paso del flujo falla (rollback). No obligatoria: el
  error se registra (recuperable, "Generar pendientes" lo reintenta).

## Política transaccional

El motor corre **dentro de la transacción del flujo** que dispara el evento:

1. Primero planifica y valida todo (reglas, plantillas activas/vigentes, datos requeridos) sin escribir.
2. Si falla algo **obligatorio** lanza `DocumentoObligatorioFallidoException` (`REQUIRED_DOCUMENT_FAILED`) y no
   persiste nada: la venta no se confirma / el cobro no se registra / la entrega no se marca / no queda un contrato a medias.
3. Los fallos de documentos no obligatorios se devuelven como errores recuperables y el flujo continúa.
4. Todos los documentos del evento se guardan juntos (todo o nada).

**Idempotencia**: clave `evento|operación|regla|plantilla` con índice único. Reintentos, doble clic o jobs
repetidos no duplican (`YaExistentes`). Una carrera real se resuelve reintentando una vez.
**Numeración**: `UPDATE` atómico de la fila del tipo dentro de la transacción + índice único `(Tipo, Número)`.
Formato `{Prefijo}-{AAAAMM}-{secuencia}`.

## Versionado e histórico

Cada documento guarda el contenido renderizado, el snapshot de datos y la versión exacta de plantilla.
Editar la plantilla, el cliente o los precios **no** cambia documentos ya emitidos.

* **Reimpresión**: sirve el contenido guardado tal cual (se registra y audita).
* **Regeneración**: acción explícita con motivo; crea un documento nuevo (nuevo número) y deja el anterior como
  `Reemplazado`. Sobre un documento firmado exige confirmación.
* **Cancelación**: estado `Cancelado` con usuario, fecha y motivo. Nada se borra físicamente.

## Plantillas

Sintaxis propia (sin `eval`, sin HTML activo): `{{ruta.variable}}`, secciones `{{#cuotas}}…{{/cuotas}}`
(colecciones `cuotas`, `productos`, o campos verdadero/falso), invertidas `{{^x}}…{{/x}}`. Los tokens de las
plantillas de contrato anteriores (`{{COMPRADOR_NOMBRE}}`, `{{Venta.Total}}`…) siguen funcionando por alias.
Variables disponibles: Configuración → Documentos → Variables. "Datos requeridos" impide emitir el documento
definitivo si alguna variable queda vacía. La vista previa usa datos de ejemplo o una operación real.

## Cómo agregar…

* **Otro contrato** ("para este crédito necesito otro contrato"): crear la plantilla (tipo Contrato) y una regla
  del evento `CONTRATO_CREDITO_SOLICITADO` con sus condiciones, prioridad y el mismo grupo de exclusión que la regla general.
* **Un tipo nuevo** ("constancia al entregar"): crear el tipo, la plantilla y una regla sobre `ENTREGA_REALIZADA`. Sin tocar código.
* **Un paquete**: Configuración → Documentos → Paquetes, y apuntar la regla al paquete.

## Compatibilidad con el contrato anterior

* `ContratoVentaCredito` y su gate de confirmación se mantienen: el paso "Preparar contrato" sigue existiendo y
  ahora emite sus documentos por el motor (misma numeración: el contrato legado usa los números del motor).
  El PDF del contrato imprime el contenido del documento emitido.
* El editor "Plantilla contrato" sigue editando los datos del vendedor/empresa (`empresa.*`) y, si cambian los
  textos de contrato/pagaré, registra una versión nueva en las plantillas estándar.
* Al iniciar la app (`DbInitializer` → `DocumentoSeeder`): se siembra la configuración equivalente al
  comportamiento anterior (tipos, plantillas con el texto vigente, paquete "Crédito estándar", reglas) **solo la
  primera vez**, y cada contrato anterior se importa como dos documentos independientes (contrato y pagaré) con
  su texto histórico ya resuelto. Idempotente y sin tocar el contrato legado.
* La regla "Constancia de entrega" se siembra **desactivada** (el sistema no emitía constancias).

## Permisos (módulo `documentos`)

`view`, `generate`, `reprint`, `sign`, `cancel`, `managetemplates`, `managerules`, `managetypes`.
Admin/SuperAdmin: todos. Gerente: todos menos `managetypes`. Vendedor: `view, generate, reprint, sign`. Cajero: `view, reprint`.

## Auditoría

`ISeguridadAuditoriaService` (módulo `documentos`): crear/modificar tipo, plantilla, nueva versión, activar/desactivar,
crear/modificar/activar/eliminar regla, paquetes, generar, reimprimir, firmar, cancelar, regenerar.

## Limitaciones conocidas

* La firma es un **registro** (rol, firmante, fecha, usuario); no hay firma digital ni captura manuscrita.
* Los documentos importados del sistema anterior se regeneran con la plantilla activa vigente de su tipo (su plantilla de archivo está inactiva).
* El PDF del contrato legado se genera una sola vez (archivo en `App_Data`); las regeneraciones posteriores se
  reimprimen desde Documentos, que siempre usa el contenido vigente del documento.
* Los datos del vendedor/empresa (`empresa.*`) salen de la plantilla de contrato vigente (no hay otra configuración de empresa).
* No se agregaron disparadores automáticos para `PRESUPUESTO_GENERADO`.
