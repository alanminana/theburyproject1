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

## 2. Concurrencia entre administradores (PENDIENTE)

**Hallazgo.** El formulario publica `RowVersion`, pero `UpdateConfiguracionExpandidaAsync` lo ignora:
carga la entidad fresca y nunca asigna `Entry(config).Property(c => c.RowVersion).OriginalValue`.
EF compara contra la versión recién leída, así que nunca hay `DbUpdateConcurrencyException`.

**Escenario reproducido** (test `DosAdministradores_FormularioViejo_NoRevierteElGuardadoAnterior`,
hoy en `Skip`):

1. A y B abren el formulario con `TasaMoraBase = 4.5`.
2. A guarda `TasaMoraBase = 9`.
3. B cambia sólo `DiasGracia` y guarda con su formulario viejo, que todavía trae `TasaMoraBase = 4.5`.
4. Resultado: `TasaMoraBase = 4.5`. El cambio de A se revierte sin aviso (last-write-wins).

La corrección de la sección 1 reduce el alcance de este problema a los 14 campos del formulario,
pero no lo elimina.

**Corrección propuesta (no aplicada):** el mismo patrón que `ResolverAlertaAsync` en el mismo
servicio.

- Exigir `viewModel.RowVersion` y asignarlo como `OriginalValue` antes de `SaveChangesAsync`.
- Traducir `DbUpdateConcurrencyException` a `InvalidOperationException("La configuración fue
  modificada por otro usuario. Recargá…")`. El controller ya muestra `ex.Message` en `TempData["Error"]`.
- Habilitar el test en `Skip`.

**Riesgo a decidir:** con la corrección, un formulario que no publique `RowVersion` pasaría a
rechazarse. El único formulario actual sí lo publica. También hay que decidir si
`UpdateConfiguracionAsync` (endpoint `GuardarConfiguracion`, sin vista) recibe el mismo control.
