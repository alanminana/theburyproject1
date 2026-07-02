# Credito FASE 4 - Cierre evaluador unificado / aptitud compuesta

> Estado: **FASE 4 CERRADA** (sub-fases A, B, C-A, C-B, C-C, D, D-B). Commits en `main`, sin push. Fecha documental: 2026-07-02.
> Documentos hermanos: [`credito-fase-1-cierre.md`](credito-fase-1-cierre.md), [`credito-fase-2-garante.md`](credito-fase-2-garante.md), [`credito-fase-3-cuenta-disponible.md`](credito-fase-3-cuenta-disponible.md).

## Objetivo de FASE 4

Cerrar el evaluador unificado de aptitud crediticia y dejar documentadas las reglas compuestas que gobiernan si un cliente puede acceder a venta a credito.

Reglas cerradas:

- La aptitud crediticia la clasifica `ClienteAptitudService`.
- La documentacion minima exige DNI, servicio y recibo de sueldo o garante valido.
- BCRA/Veraz es obligatorio para evaluar aptitud.
- El garante valido funciona como alternativa al recibo de sueldo, pero no aumenta cupo.
- Antes de evaluar aptitud se debe refrescar BCRA con cache en los flujos canonicos.
- Un buen pagador con BCRA alto no aprueba automaticamente: como maximo requiere autorizacion.

FASE 4 no modifica cupo/disponible productivo. El cupo sigue gobernado por FASE 1 y el disponible real por FASE 3.

---

## FASE 4A - Sueldo OR garante valido

**Commit:** `82f84222dd34e176d76e64ae50823ac06a3863c7`

Regla documental:

```text
DNI + Servicio + (ReciboSueldo OR garante valido)
```

- El recibo de sueldo deja de ser el unico camino documental.
- Un garante valido satisface el requisito alternativo al recibo.
- El garante valido no aumenta cupo ni modifica limite asignado.
- La validacion consume `GaranteService`; no duplica reglas de garante dentro del evaluador.

---

## FASE 4B - BCRA/Veraz obligatorio

**Commit:** `a1fcc787509428756e26d8e3b50f6a314a0d32ec`

Reglas de aptitud con BCRA/Veraz:

- Sin CUIL/CUIT => `NoApto`.
- Sin consulta BCRA registrada => `NoApto`.
- Consulta fallida con intento registrado => `RequiereAutorizacion`.
- Consulta OK sin situacion informada => `RequiereAutorizacion`.
- Situacion 0/1 => continua por camino normal.
- Situacion 2 => `RequiereAutorizacion`.
- Situacion >= 3 => `NoApto`, salvo excepcion de buen pagador cerrada en FASE 4D.

BCRA/Veraz deja de poder omitirse silenciosamente en la aptitud crediticia.

---

## FASE 4C-A - Refresh BCRA antes de aptitud en cliente

**Commit:** `13b3f3427b09f6141dc8971db80f615e151bd504`

Flujo en `Cliente/Details`:

```text
BCRA -> Aptitud
```

- Antes de mostrar la aptitud en la ficha del cliente se refresca BCRA.
- `RecalcularAptitud` ejecuta `ConsultarYActualizarAsync` con cache antes de `EvaluarAptitudAsync`.
- El clasificador sigue siendo `ClienteAptitudService`.

---

## FASE 4C-B - Refresh BCRA antes de venta a credito

**Commit:** `7a703e45c79784664d8d10fa6f1d728007f9f008`

- `ValidacionVentaService` refresca BCRA con cache antes de evaluar aptitud para venta a credito.
- No duplica reglas BCRA en venta.
- `ClienteAptitudService` sigue siendo el clasificador unico de aptitud.
- El flujo de venta deja de evaluar con BCRA potencialmente stale cuando hay refresh disponible.

---

## FASE 4C-C - Limite transaccional en venta

**Commit:** `2108f723ae84decb99733983db9332ad3dfa13af`

- `VentaService.CreateAsync` valida credito antes de abrir la transaccion de persistencia.
- La persistencia de la venta queda dentro de la transaccion.
- Se evita I/O externo de BCRA dentro del tramo transaccional de creacion.
- El refresh/evaluacion de aptitud queda separado del bloque de escritura de venta.

---

## FASE 4D - Buen pagador con BCRA alto

**Commit:** `83577b2829b68a8e07daef475a4ff1f576588600`

Regla:

- BCRA >= 3 normalmente bloquea (`NoApto`).
- Si el cliente cumple todas las condiciones de buen pagador, pasa de `NoApto` a `RequiereAutorizacion`.
- Nunca pasa a `Apto` automatico.

Condiciones requeridas:

- `PuntajeCliente >= 4`.
- `AntiguedadDias >= 90`.
- `CantidadComprasCliente > 0`.
- `CreditosEnTermino >= 1`.
- `CreditosConAtraso == 0`.
- Sin mora activa en tiempo real.

La excepcion solo habilita revision manual. No elimina el riesgo BCRA alto ni aprueba la venta por si sola.

---

## FASE 4D-B - Integracion flujo consumidor

**Commit:** `f6bb04123941cd63c64ed9ae4a1ef26d45c2254f`

- Sub-fase tests-only.
- Valida que `ValidacionVentaService` consume correctamente la regla de buen pagador con BCRA alto.
- No toca UI.
- No toca servicios productivos.
- No toca cupo/disponible productivo.

---

## Flujo final confirmado

1. Alta/evaluacion de cliente usa `ClienteAptitudService`.
2. `Cliente/Details` refresca BCRA antes de mostrar aptitud.
3. Venta a credito refresca BCRA con cache antes de evaluar aptitud.
4. BCRA obligatorio ya no queda omitido silenciosamente.
5. Mal BCRA no aprueba automatico.
6. Buen pagador con mal BCRA requiere autorizacion.
7. Cupo/disponible siguen fuera de FASE 4 y se mantienen gobernados por las fases previas.

---

## Deuda / riesgos remanentes

- La autorizacion manual de Javo queda para una fase posterior.
- 4 tests preexistentes de `ClienteAptitudServiceTests` siguen fallando por drift `Puntaje 0 = 200000`.
- `EvaluacionCreditoService` sigue siendo flujo huerfano/paralelo.
- `TipoDocumentoCliente.Veraz` sigue como adjunto, no como fuente principal.
- Stashes preexistentes de otras ramas no se tocaron.
- `main` esta ahead de `origin/main`; no hacer push sin decision explicita.

---

## Commits de FASE 4

| Sub-fase | Commit | Descripcion |
|---|---|---|
| 4A | `82f84222dd34e176d76e64ae50823ac06a3863c7` | Sueldo OR garante valido |
| 4B | `a1fcc787509428756e26d8e3b50f6a314a0d32ec` | BCRA/Veraz obligatorio |
| 4C-A | `13b3f3427b09f6141dc8971db80f615e151bd504` | Refresh BCRA antes de aptitud en cliente |
| 4C-B | `7a703e45c79784664d8d10fa6f1d728007f9f008` | Refresh BCRA antes de venta a credito |
| 4C-C | `2108f723ae84decb99733983db9332ad3dfa13af` | Validacion de credito antes de abrir transaccion |
| 4D | `83577b2829b68a8e07daef475a4ff1f576588600` | Buen pagador con BCRA alto requiere autorizacion |
| 4D-B | `f6bb04123941cd63c64ed9ae4a1ef26d45c2254f` | Integracion tests-only en flujo consumidor |

---

## Estado de cierre documental

- Archivo documental creado: `docs/credito-fase-4-evaluador-unificado.md`.
- No se modifico codigo productivo.
- No se modificaron tests.
- No se modifico UI.
- No se tocaron stashes.
- No se hizo push.
- No se hizo commit.
