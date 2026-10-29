# Mora: preservación de configuración y concurrencia

## 1. Preservación de campos (RESUELTO, 2026-10-13)

**Síntoma.** `/Mora/ConfiguracionExpandida` publica 14 campos editables (+ `Id`, `RowVersion`,
token antiforgery y `returnUrl`). `MoraService.UpdateConfiguracionExpandidaAsync` asignaba los 49
campos del ViewModel. Los 35 que el formulario no envía llegaban con el valor por defecto del
binder (`null`/`false`) y se perdían en silencio cada vez que alguien guardaba, aunque no cambiara nada.

**Campos que el formulario edita (14):** `ProcesoAutomaticoActivo`, `ActualizarMoraAutomaticamente`,
`CambiarEstadoCuotaAuto`, `HoraEjecucionDiaria`, `DiasGracia`, `TasaMoraBase`,
`AlertasPreventivasActivas`, `DiasAntesAlertaPreventiva`, `ImpactarScorePorMora`,
`PuntosRestarPorCuotaVencida`, `PuntosRestarPorDiaMora`, `PuntosMaximosARestar`,
`PorcentajeRecuperacionScore`, `RecuperarScoreAlPagar`.

**Campos que se perdían (35):** `TipoTasaMora`, `BaseCalculoMora`, `EscalonamientoActivo`,
`TasaPrimerMes`, `TasaSegundoMes`, `TasaTercerMesEnAdelante`, `TopeMaximoMoraActivo`, `TipoTopeMora`,
`ValorTopeMora`, `MoraMinima`, `DiasParaPrioridad{Media,Alta,Critica}`,
`MontoParaPrioridad{Media,Alta,Critica}`, `NotificacionesActivas`, `WhatsAppActivo`, `EmailActivo`,
`CanalPreferido`, `NotificarProximoVencimiento`, `DiasAntesNotificacionPreventiva`,
`NotificarCuotaVencida`, `NotificarMoraAcumulada`, `FrecuenciaRecordatorioMora`,
`MaximoNotificacionesDiarias`, `MaximoNotificacionesPorCuota`, `HoraInicioEnvio`, `HoraFinEnvio`,
`EnviarFinDeSemana`, `DiasMaximosSinGestion`, `DiasParaCumplirPromesa`, `MaximoCuotasAcuerdo`,
`PorcentajeMinimoEntrega`, `PermitirCondonacionMora`.

**Campos que el servicio no tocaba nunca:** `PorcentajeMaximoCondonacion` (está en el ViewModel pero
no se mapeaba), los de Bloqueos (`BloqueoAutomaticoActivo`, `DiasParaBloquear`,
`CuotasVencidasParaBloquear`, `MontoMoraParaBloquear`, `TipoBloqueo`, `DesbloqueoAutomatico`),
`UltimaEjecucion` y los 5 deprecated (`PorcentajeRecargo`, `CalculoAutomatico`,
`NotificacionAutomatica`, `JobActivo`, `HoraEjecucion`). Los campos `Tramo*` del ViewModel no existen
en la entidad.

**Corrección.** `GuardarConfiguracionExpandida` pasa `Request.Form.Keys` al servicio, y el servicio
aplica sólo los campos presentes, sin distinguir mayúsculas de minúsculas. Un campo enviado vacío
sigue limpiando el valor, porque es una edición explícita. Un checkbox destildado viaja como el
hidden `"false"` y se guarda como `false`. Sin lista (`camposEnviados = null`) se mantiene el
comportamiento anterior de aplicar el ViewModel completo.

**Tests:** `MoraConfiguracionExpandidaPreservacionHttpTests` (render real + POST como un navegador)
y `MoraConfiguracionExpandidaPreservacionServiceTests` (SQLite en memoria).

**Fuera de alcance:** no se reconstruyó ninguna configuración histórica. Los valores que ya se
perdieron en bases reales siguen como están.

## 2. Concurrencia entre administradores (RESUELTO, 2026-10-14, QA-CFG-FIX)

**Hallazgo original.** `UpdateConfiguracionExpandidaAsync` ignoraba el `RowVersion` publicado por el
formulario: EF comparaba contra la versión recién leída y el segundo administrador pisaba en silencio
el guardado del primero (last-write-wins).

**Corrección.**

- Con `camposEnviados != null` (todo POST de formulario) el `RowVersion` es obligatorio: se asigna como
  `OriginalValue` y, si falta o está vacío, se rechaza con "Falta información de concurrencia".
  Un formulario sin token no elude la protección. Sólo las llamadas internas con el ViewModel
  completo (`camposEnviados == null`) pueden omitirlo; si lo traen, se valida igual.
- `DbUpdateConcurrencyException` se traduce a `InvalidOperationException("La configuración fue modificada
  por otro usuario. Recargá los datos e intentá nuevamente.")`; el controller ya lo muestra en `TempData["Error"]`.
  La entidad obsoleta se desasocia del contexto: nada de lo aplicado se persiste.
- Test `DosAdministradores_FormularioViejo_NoRevierteElGuardadoAnterior` habilitado, más
  `FormularioSinRowVersion_NoEludeLaProteccion`, `FormularioConRowVersionAjeno_…` y dos tests de servicio.
- Verificado con SQL Server real (rowversion nativo): ver `docs/qa-cfg-resultados.md`.

**Pendiente de decisión:** `UpdateConfiguracionAsync` (endpoint `GuardarConfiguracion`, sin vista, ViewModel sin
`RowVersion`) mantiene last-write-wins: no hay formulario que lo publique. Si se conserva el endpoint,
hay que agregarle `RowVersion`; si no, retirarlo.

## 3. Validaciones (RESUELTO, 2026-10-14)

- `[Range(0, 100)]` con límites enteros sobre `decimal?` redondeaba el valor antes de comparar
  (`100.01`→100, `-0.01`→0). Ahora `[Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true)]`
  (13 propiedades: tasas, porcentajes, montos y puntos). Límite monetario `9999999999999999.99` (columna `decimal(18,2)`).
- `CrearAcuerdoPagoAsync` valida en servidor, antes de dividir o persistir: cuotas > 0, importes no negativos,
  total > 0, condonación ≤ mora original, entrega ≤ total, y el tope `PorcentajeMaximoCondonacion`
  (porcentaje de la **mora original**; exacto pasa, +0.01 rechaza). Los rechazos no escriben en la base.
- Permisos (`mora/config`, `mora/manage`) y `[ValidateAntiForgeryToken]` ya estaban en `GuardarConfiguracion*` y `CrearAcuerdo`; cubiertos por CFG-09/10/11.

## 4. Régimen de tasas (SIN CAMBIOS, decisión de negocio pendiente)

Ver "Consecuencias financieras" en `docs/qa-cfg-resultados.md`. No se modificó ninguna fórmula.

## DiasParaCumplirPromesa (H2-D)

Lo lee el job diario (`AcuerdoIncumplimientoBackgroundService` -> `IMoraService.VerificarPromesasVencidasAsync`, fecha comercial, independiente de `ProcesoAutomaticoActivo`). Semantica conservadora (la ayuda de la entidad, "dias maximos para esperar cumplimiento", es ambigua): son dias de espera DESPUES de la fecha prometida; la promesa activa (alerta en `PromesaPago`) pasa a incumplida (`EnGestion`, fecha/monto de promesa limpios, prioridad +1, evento de auditoria `PromesaIncumplidaAutomatica`) cuando hoy comercial > prometida + dias. Sin valor configurado no hace nada. No toca cuotas, dinero, punitorios ni acuerdos; idempotente.
