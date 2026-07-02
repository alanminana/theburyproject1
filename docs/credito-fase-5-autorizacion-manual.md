# Credito FASE 5 - Cierre autorizacion manual puntual

> Estado: **FASE 5 CERRADA** (sub-fases A, B, C, D, E, F). Commits en `main`, sin push. Fecha documental: 2026-07-02.
> Documentos hermanos: [`credito-fase-1-cierre.md`](credito-fase-1-cierre.md), [`credito-fase-2-garante.md`](credito-fase-2-garante.md), [`credito-fase-3-cuenta-disponible.md`](credito-fase-3-cuenta-disponible.md), [`credito-fase-4-evaluador-unificado.md`](credito-fase-4-evaluador-unificado.md).

## Objetivo de FASE 5

Cerrar la autorizacion manual puntual por venta para el flujo seguro de Javo.

Reglas cerradas:

- La autorizacion es puntual por venta.
- Requiere control de segundo usuario.
- Audita motivo, usuario y fecha.
- No modifica cupo.
- No modifica puntaje.
- No modifica limite futuro.
- No autoriza otras ventas.

FASE 5 no cambia el modelo de cupo, BCRA, garante ni scoring. La autorizacion manual habilita o rechaza una venta especifica sin convertir esa excepcion en credito futuro.

---

## FASE 5A - Diagnostico

Se detectaron 3 caminos relacionados con autorizaciones/excepciones:

- Flujo formal `Autorizar` / `Rechazar`.
- Excepcion documental directa en `CreateAsync`.
- `RegistrarExcepcionDocumentalAsync` legacy.

Clasificacion:

- El flujo formal `Autorizar` / `Rechazar` es el camino canonico para ventas que requieren autorizacion.
- La excepcion documental directa en `CreateAsync` permitia bypass y quedo fuera del modelo seguro.
- `RegistrarExcepcionDocumentalAsync` era un camino legacy que necesitaba el mismo control de segundo usuario.
- `AutorizacionService` / `SolicitudAutorizacion` queda fuera de scope porque pertenece a otro dominio paralelo.

---

## FASE 5B - Tests autorizacion puntual

**Commit:** `a555391cd71b028c196bb81534147b7d61613ad7`

Cobertura agregada:

- Autorizacion puntual por venta.
- La autorizacion no modifica `PuntajeCliente`.
- La autorizacion no modifica limite/disponible.
- La autorizacion no autoriza otra venta.
- La autorizacion preserva `RazonesAutorizacionJson`.

---

## FASE 5C - Razones estructuradas en UI

**Commit:** `6f23dac`

- `Autorizar` / `Rechazar` muestran `RazonesAutorizacionJson`.
- Si el JSON esta vacio, `null` o corrupto, se usa fallback seguro.
- Deuda menor: `ValorAsociado` / `ValorLimite` solo se muestran si vienen persistidos.

---

## FASE 5D - Gate en ConfigurarVenta

**Commit:** `bc01aea6e92412374793803bd04e2986e109786f`

`CreditoController.ConfigurarVenta` GET/POST bloquea la configuracion si:

```text
venta.RequiereAutorizacion == true
y EstadoAutorizacion != Autorizada
```

Resultado:

- No se puede configurar plan/cuotas antes de la autorizacion.
- Una venta pendiente de autorizacion queda frenada antes de avanzar al tramo de credito.
- El gate protege el flujo canonico sin modificar cupo, BCRA ni garante.

---

## FASE 5E - Modelo seguro en CreateAsync

**Commit:** `f8f21e829f98f7a9957572be1ab98c304c0dc099`

- `CreateAsync` ya no autoexcepciona.
- Excepciones documentales o crediticias excepcionables quedan `PendienteAutorizacion`.
- `AutorizarVentaAsync` bloquea al mismo creador usando `Venta.CreatedBy`.
- `ventas.create` solo no permite bypass.
- `ventas.create` + `ventas.authorize` en el mismo usuario tampoco autoaprueba en `CreateAsync`.

---

## FASE 5F - Autoexcepcion documental legacy

**Commit:** `a65e68d90b685598fc2cd64352ae3f199244cbae`

- `RegistrarExcepcionDocumentalAsync` bloquea si `usuarioAutoriza` coincide con `Venta.CreatedBy`.
- La comparacion es case-insensitive.
- Usuario distinto sigue permitido.
- El camino legacy queda alineado con la regla de segundo usuario.

---

## Flujo final seguro

1. Usuario A crea la venta.
2. Si la venta requiere autorizacion, queda `PendienteAutorizacion`.
3. Mientras sigue pendiente, no puede configurar credito.
4. El mismo creador no puede autorizarla.
5. Usuario B con `ventas.authorize` autoriza o rechaza.
6. La autorizacion queda auditada con motivo, usuario y fecha.
7. La autorizacion no aumenta cupo.
8. La autorizacion no cambia `PuntajeCliente`.
9. La autorizacion no cambia limite futuro.
10. La autorizacion no autoriza otras ventas.

---

## Reglas finales

- Auto-autorizacion bloqueada.
- Autoexcepcion documental bloqueada.
- Excepcion documental directa en `CreateAsync` eliminada.
- `RequiereAutorizacion` debe pasar por flujo formal.
- Rechazo cancela/libera lo que corresponda segun el flujo ya testeado.
- Garante no aumenta cupo.
- BCRA alto buen pagador requiere autorizacion, no aprobacion automatica.

---

## Deuda / riesgos remanentes

- `ValorAsociado` / `ValorLimite` no siempre se persisten en `RazonesAutorizacionJson`.
- Caso `ConfigurarVenta` invocado sin `ventaId` queda como limitacion preexistente.
- `AutorizacionService` / `SolicitudAutorizacion` sigue fuera de scope/paralelo.
- 4 tests preexistentes de `ClienteAptitudServiceTests` siguen fuera de scope por drift `Puntaje 0 = 200000`.
- No hacer push hasta decision explicita.

---

## Commits de FASE 5

| Sub-fase | Commit | Descripcion |
|---|---|---|
| 5A | Sin commit propio | Diagnostico de caminos de autorizacion/excepcion |
| 5B | `a555391cd71b028c196bb81534147b7d61613ad7` | Tests de autorizacion puntual |
| 5C | `6f23dac` | Razones estructuradas en Autorizar/Rechazar |
| 5D | `bc01aea6e92412374793803bd04e2986e109786f` | Gate en ConfigurarVenta |
| 5E | `f8f21e829f98f7a9957572be1ab98c304c0dc099` | CreateAsync sin autoexcepcion y segundo usuario |
| 5F | `a65e68d90b685598fc2cd64352ae3f199244cbae` | Bloqueo de autoexcepcion documental legacy |

---

## Estado de cierre documental

- Archivo documental creado: `docs/credito-fase-5-autorizacion-manual.md`.
- No se modifico codigo productivo.
- No se modificaron tests.
- No se modifico UI.
- No se tocaron stashes.
- No se hizo push.
- No se hizo commit.
