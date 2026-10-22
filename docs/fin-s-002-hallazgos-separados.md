# FIN-S-002 — hallazgos registrados fuera del alcance de la PR #97

PROMPT 86. Estos dos hallazgos **no se corrigen en la PR #97**: se registran para PRs propias. Ninguno cambia el
veredicto de las correcciones A1/A2/F1/F2/F3, pero el segundo debilita la garantía de F2 (ver "Relación con F2").

## H-NUM-1 — Numeración de ventas: bloqueo con READ_COMMITTED_SNAPSHOT desactivado

- **Dónde:** `VentaNumberGenerator` + `VentaService.GuardarVentaConReintentoNumeroAsync` (sin cambios en #97).
- **Qué pasa:** con RCSI **desactivado** (default de SQL Server on-prem), el reintento por colisión regenera el
  número dentro de la transacción y su `SELECT` queda bloqueado por la inserción sin commitear de otra alta, mientras
  esa otra alta espera el semáforo estático del generador. Tras ~30 s de timeout fallan varias altas concurrentes.
  Con RCSI activado (default de Azure SQL) todas terminan.
- **Integridad:** no hay riesgo de duplicados (`IX_Ventas_Numero` único); es un problema de **disponibilidad** bajo
  carga concurrente de altas.
- **Evidencia:** `FinS002VentaSqlServerTests.Num_AltasConcurrentes_NumerosUnicos` (FS2-NUM). El test exige unicidad
  siempre y disponibilidad total sólo con `QA_FIN_S002_RCSI=1`; el workflow `fin-s-002-sqlserver.yml` lo corre en
  ambos modos.
- **Pendiente:** PR propia (secuencia SQL o `sp_getapplock` en lugar del `SELECT MAX` + semáforo estático).

## H-IDOR-1 — `Credito/ConfigurarVenta` (GET y POST): crédito y venta no se validan entre sí

- **Dónde:** `Controllers/CreditoController.cs` `ConfigurarVenta` (GET ~l.669, POST ~l.920) y
  `CreditoService.ConfigurarCreditoAsync` (~l.2338).
- **Qué pasa:** el crédito se busca por `id`/`modelo.CreditoId` y la venta por `ventaId`/`modelo.VentaId`, **por
  separado**; en ningún punto se comprueba que `venta.CreditoId == credito.Id` ni que ambos sean del mismo cliente.
  El único permiso exigido es el de clase (`creditos:view`). `ConfigurarCreditoAsync` aplica la configuración al
  crédito indicado y, con `VentaId`, fija `venta.FechaConfiguracionCredito` y mueve la venta a `Presupuesto` sobre la
  venta indicada.
- **GET:** permite leer/renderizar la configuración (cuotas, tasas, monto) de cualquier crédito configurable por id.
- **POST:** permite configurar un crédito ajeno y/o marcar como "configurada" una venta cuyo crédito propio nunca se
  configuró.
- **Relación con F2:** `ConfirmarVentaCreditoAsync` acepta una venta cuyo crédito no está `Configurado` si
  `venta.FechaConfiguracionCredito` tiene valor (la "corrige" a `Configurado`). Con H-IDOR-1 un usuario con acceso a
  la pantalla puede fijar esa fecha sobre una venta ajena sin configurar su crédito, así que F2 (el cliente ya no
  puede forjarla en Create/Update) **no cierra** esa vía por sí sola.
- **Estado de la evidencia:** confirmado por lectura de código; **no reproducido con una prueba ejecutable** (fuera
  de alcance de #97). Las pruebas S2-CF-05/S2-CF-06 cubren la ruta de Create/Update, no esta.
- **Mitigación parcial ya presente en #97:** `FacturarVentaAsync` ahora exige crédito del mismo cliente, no en
  estado previo/terminal y con cuotas generadas (S2-CF-07), por lo que una venta confirmada sobre un crédito sin
  generar ya no se factura.
- **Pendiente (PR propia):** exigir en GET y POST `credito.Id == venta.CreditoId` y `credito.ClienteId ==
  venta.ClienteId` (404 si no coincide), un permiso específico para configurar, y en `ConfirmarVentaCreditoAsync`
  no "corregir" el estado del crédito con sólo la fecha de la venta.
