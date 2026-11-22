# Crédito cerrado al inicio: cupos en $0 (decisión del propietario, 2026-11-22)

Base: `main` ddcef55c. Solo se agregó una prueba, un control SQL de solo lectura y documentación: **no se cambió ninguna regla**, no se tocó ninguna base ni la preproducción.

## 1. Condición inicial obligatoria de la instalación operativa

| Parámetro | Valor inicial | Dónde se cambia después (por pantalla) |
|---|---|---|
| Cupo por puntaje | **$0** en los seis puntajes que tienen cupo (0 al 5); los puntajes 6 al 10 no tienen fila de cupo, por lo que no dan cupo | Clientes → «Límites por puntaje» (permiso `clientes.managecreditlimits`; el modelo restringe el cupo a los puntajes 0-5) |
| Monto máximo financiable por puntaje | **$0** | Configuración de pagos → Crédito personal |
| Punitorios: porcentaje / período / gracia | **0 % / 30 días / 12 días** | Configuración de punitorios (`configuracion.managepunitorio`) |
| Planes de cuotas de Crédito Personal | **sin configurar** | Configuración de pagos → Crédito personal |

Nada se escribe por código ni por SQL: el SQL de este PR solo **lee**. **La semilla de las migraciones deja el puntaje 0 en $200.000**, por lo que una instalación nueva debe llevarlo a $0 por pantalla antes de operar y comprobarlo con el control de §4.

## 2. Qué determina realmente el cupo (verificado en el código de `main`)

1. **Autoridad única del cupo**: `PuntajeCreditoLimite` (tabla `PuntajeCreditoLimites`), resuelta por `CreditoDisponibleService.ResolverLimiteAsync`. La semilla trae filas solo para los puntajes 0 a 5; para 6 a 10 no hay fila: el cálculo lanza `CreditoDisponibleException`; la validación de venta lo toma como cupo $0 (`NoViable`). Matiz: la comprobación secundaria al confirmar (`VentaService`, ~línea 5501) captura esa excepción y conserva la validación legacy, pero la venta ya fue bloqueada al crearse.
2. **Con cupo $0**: la validación de venta a crédito (`ValidacionVentaService.VerificarCupoUnificado`) responde `NoViable` («Sin límite de crédito por puntaje») para cualquier monto > 0 al **crear** la venta, y `CreditoService` rechaza de nuevo todo monto que supere el disponible (segunda barrera).
3. **Un plan de cuotas no habilita financiación**: los planes (`ConfiguracionCreditoPersonalCuotas`) no entran en el cálculo del cupo. Sin planes activos, además, el sistema informa que no puede financiarse. Con planes y cupo $0 sigue siendo `NoViable`.
4. **`ConfiguracionCreditoMontoPorPuntaje` (montos financiables) no es consultada por ninguna regla** de venta o crédito: es configuración que hoy no gobierna nada. Se deja en $0 por consistencia.
5. **La casilla «validar límite de crédito»** (`ValidarLimiteCredito`) solo gobierna la tarjeta de aptitud; `VerificarCupoUnificado` no la consulta. Con la casilla apagada el cupo $0 sigue bloqueando.
6. **Todo es editable por pantalla** (cupos por puntaje 0-5, montos financiables por puntaje 0-10, planes, punitorios, límites por cliente). La validación de los cupos acepta $0 en todos.
7. **Límite de la garantía — operaciones preexistentes**: el cupo se evalúa al **crear** la venta. Una venta/crédito ya configurado antes de cerrar los cupos puede confirmarse después sin nueva excepción (`ConfirmarVentaCreditoAsync` excluye la categoría cupo; sujeto a los demás controles). La garantía «nadie obtiene financiación» vale para una instalación **sin ventas ni créditos preexistentes**; el control de §4 lo exige (0 créditos).

Prueba nueva con servicios reales (sin stubs), `CreditoCuposEnCeroNoFinancianTests` (4/4 verdes): con todos los cupos en $0 y un plan activo, ninguno de los puntajes 0 a 10 valida una venta a crédito, con la casilla activada y apagada; contraprueba (cupo > 0 ⇒ viable); y la semilla actual (puntaje 0 = $200.000, resto $0).

## 3. Vías que NO dependen del cupo del puntaje (autorizaciones deliberadas, ya aprobadas; no se modificó nada)

El resultado exigido, «con todos los cupos en $0 nadie obtiene financiación», **se cumple para el cálculo automático**. Existen dos vías explícitas, con permiso y auditoría, que son reglas de negocio ya aprobadas y por eso **no se consideran defecto**:

| Vía | Qué permite | Quién | Control recomendado al inicio |
|---|---|---|---|
| **Excepción de cupo/documental en la venta** (D-02) | Levanta el bloqueo `SinLimiteCredito` / «excede el crédito disponible» con motivo obligatorio; nunca la mora. La venta queda autorizada y marcada `EXCEPCION_DOC` | Usuario con `ventas.authorize` | Que solo SuperAdmin tenga `ventas.authorize` hasta definir cupos |
| **Cupo por cliente** (límite manual, override, excepción de cupo, puntaje manual, monto máximo personalizado) | Da cupo a un cliente puntual por encima del preset | Usuario con `clientes.managecreditlimits` | Que ningún cliente lo tenga al inicio; solo SuperAdmin tiene el permiso |

Por eso el control de §4 informa (INFO) qué roles tienen esos dos permisos y exige (FALLA) que no exista ningún límite por cliente mientras el crédito esté cerrado. Si el propietario quiere que ni siquiera un usuario autorizado pueda exceptuar con cupo $0, es un cambio de regla de negocio y requiere su autorización: no se hizo.

Las cotizaciones y el simulador solo consultan; no persisten crédito ni reservan cupo.

## 4. Verificación previa a operar (solo lectura)

`scripts/sql/p07-verificar-credito-cerrado.sql` (`@Operando = 0`, sin FALLA exigible): cupos por puntaje > 0, montos financiables > 0, límites por cliente, overrides/excepciones/puntaje manual, créditos existentes (deben ser 0), roles con `ventas.authorize` y `clientes.managecreditlimits`, planes activos y punitorio vigente hoy (0 % / 30 / 12, activa). Con FALLA termina con error (`RAISERROR` severidad 16; con `sqlcmd -b` el código de salida es distinto de 0). Ejecución igual que `p07-verificar-cuentas.sql`:

`docker cp scripts/sql/p07-verificar-credito-cerrado.sql <db>:/tmp/k.sql && docker exec <db> /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d TheBuryProjectDb -I -b -i /tmp/k.sql`

Estado de la validación del script: **sintaxis comprobada con `SET PARSEONLY ON` en SQL Server (LocalDB), sin ejecutarlo contra ninguna base**; los nombres de tablas y columnas salen del modelo EF. Falta su primera ejecución real en la base nueva de la puesta en marcha (o en un ensayo con base descartable).

Cuando el propietario decida abrir el crédito (definir cupos por puntaje, planes y permisos), vuelve a correrlo con `@Operando = 1` y deja constancia.

## 5. Decisión sobre la semilla de $200.000

No se cambió: la semilla vive en una migración (`HasData`), y corregirla exigiría una migración nueva que además se aplicaría a las bases existentes. El propietario pidió no tocar bases existentes ni migrar sin autorización. **Propuesta opcional** (requiere autorización): migración condicional que ponga el puntaje 0 en $0 solo si todavía vale exactamente $200.000 con `UsuarioActualizacion = 'System'` (sin alterar lo que alguien haya editado). Mientras tanto la condición se garantiza con el procedimiento y el control de §1 y §4.

## 6. Estado

- Cupos en $0: **decisión registrada como condición inicial obligatoria**; aplicarla a la instalación nueva es un paso de la puesta en marcha (§7 de `puesta-en-marcha-p07-p02.md`), no se ejecutó nada.
- Defectos encontrados: **ninguno** en el flujo de financiación de altas nuevas; diferencias documentales corregidas (semilla $200.000, puntajes 6-10 sin fila, confirmación de operaciones preexistentes).
