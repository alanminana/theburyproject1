---
name: integracion-por-tandas
description: Política de integración por tandas de TheBuryProject para minimizar corridas de CI sin perder gates: agrupar tareas compatibles, validar local con pruebas focalizadas, revisión de Codex antes de integrar, una PR por tanda estable, CI completo una vez, diagnóstico de fallos de CI con causa raíz y fusión sin --admin. Usar al planificar, pushear, abrir PR, corregir CI o fusionar, y cuando el usuario pida "tanda", "lote" o reducir CI.
---

# Integración por tandas

Objetivo: menos corridas de CI, mismos gates. El CI es la **verificación final** de una tanda ya validada localmente, no el ciclo de desarrollo.

## Alcance y límites fijos
- Los cinco checks obligatorios de `main` (`build-test`, `docker-build`, `compose-validate`, `secret-scan`, `test`; verificar con `gh api repos/:owner/:repo/branches/main/protection/required_status_checks`) **nunca** se omiten, desactivan ni reemplazan. Tampoco secret-scan ni auditorías de dependencias. **`--admin` está prohibido** para esquivar protecciones; cambiar la protección de `main` requiere consulta previa.
- Un fallo no se oculta con excepciones, `continue-on-error`, `skip` ni allowlists para "que pase".
- Autorizaciones: AGENTS.md → "Autonomía y autorizaciones". Claude ejecuta sin pedir permiso por operación Git (commit, push de la tanda, PR, fusión normal). Siguen requiriendo autorización del usuario: migraciones productivas sensibles, cambios destructivos de datos reales, credenciales reales, decisiones funcionales/financieras críticas y cambios de alcance. Las restricciones que dé cada pedido (p. ej. no activar CSP Enforce, no desplegar) se respetan.

## 1. Armar la tanda
- Agrupar tareas técnicas **compatibles**: mismo módulo o módulos sin archivos en común, sin mezclar reglas fiscales/financieras pendientes de decisión con trabajo ya decidido.
- Cada tanda declara: objetivo, tareas incluidas, archivos esperados, criterios de aceptación y qué queda fuera.
- Separar en tanda distinta: migraciones EF con datos, cambios de seguridad/permisos sensibles y todo lo que dependa de una decisión del propietario.

## 2. Desarrollo local (sin CI)
- Pruebas **focalizadas** (`--filter`, un proyecto, un spec) durante el desarrollo; AGENTS.md → Validación. No repetir suites extensas ajenas al cambio.
- **Commits locales intermedios: permitidos y recomendados** (puntos de retorno, un commit por unidad lógica). Lo que se evita es el **push** innecesario, no el commit.
- Antes de pushear, una sola pasada local: build del proyecto afectado, tests focalizados, `git diff --check`, análisis estático aplicable (`dotnet build` con warnings del cambio, `dotnet ef migrations has-pending-model-changes` si tocó el modelo) y control de secretos (`gitleaks git --log-opts=<rango>` o equivalente local si está instalado).
- Agentes y worktrees paralelos solo para tareas **independientes** (sin archivos compartidos). Cada uno trabaja en su worktree/rama propia `D:\git\theburyproject1-t-<slug>`; el coordinador reparte archivos antes de lanzar y consolida después. Nunca `git stash` (compartido entre worktrees).

## 3. Consolidar y revisar
- Reunir las ramas compatibles en **una rama de integración temporal** (`integracion/<tanda>`), resolver conflictos ahí y re-ejecutar las pruebas focalizadas sobre el resultado combinado.
- Codex revisa el conjunto antes de la integración final (`codex-review-protocolo`): **una revisión por tanda** sobre el diff consolidado, nivel según el riesgo más alto que contenga. Revisiones adicionales solo con justificación de riesgo (corrección de un hallazgo `high`/`critical`, código sensible nuevo), enfocadas en el delta; no una por subtarea.
- Los hallazgos verificados se corrigen en la rama de integración; el delta corregido se prueba local antes de pushear.

## 4. Push, PR y CI
- **Una PR por tanda compatible**, no por subtarea. Mientras la tanda no esté lista se trabaja con commits locales **sin pushes intermedios**: cada push a una rama con PR abierta dispara el CI completo (`ci.yml` + `playwright.yml`). Abrir la PR cuando la tanda ya está validada y revisada.
- Acumular correcciones y pushear **una vez** el lote ya validado. Si hay que corregir algo, juntar todas las correcciones conocidas en un único push; no pushear "para ver si pasa". El CI completo debe correr sobre el **commit final**.
- Al pushear, el `concurrency: cancel-in-progress` cancela la corrida anterior de esa rama; aun así cada push consume minutos: evitarlos.
- Si **cambia el commit** (push, rebase, amend, merge de `main`), los checks anteriores dejan de valer: esperar los checks del nuevo SHA. No justificar una fusión con checks de un commit anterior.

## 5. Diagnóstico obligatorio ante un check rojo
1. Identificar workflow, job, paso y mensaje exacto (`gh run list`, `gh run view <id> --log-failed`).
2. Recuperar los logs completos y encontrar la causa raíz antes de tocar nada.
3. Clasificar: defecto de código, de test, de configuración, permisos Git, Docker, infraestructura del runner o dependencia externa.
4. Corregir la causa real. Reproducir localmente **solo** el paso que falló, no toda la suite.
5. Si parece intermitente: obtener evidencia primero (mismo test pasando/fallando en otras corridas, log de infraestructura, `gh run view --log` de ambas) antes de re-ejecutar; documentar la evidencia. Un re-run sin evidencia no cuenta como diagnóstico.
6. Corregir en un único commit por causa y esperar los checks del nuevo SHA.

## 6. Fusión y limpieza
- Fusionar con el flujo normal de GitHub (**sin `--admin`**) cuando los cinco checks obligatorios estén verdes **sobre el SHA actual** y la revisión de Codex esté cerrada. Es autónomo; no requiere OK del usuario salvo lo listado en "Autorizaciones".
- Limpiar solo worktrees y ramas propios (`git worktree remove`, `git branch -d`, ramas remotas de la tanda); dejar `main` local actualizado (`git switch main; git pull --ff-only`). No tocar trabajo ajeno ni WIP de otros agentes.

## 7. Informe único por tanda
Commits, PR, estado de CI por check (con SHA), hallazgos de Codex y su disposición, correcciones, tareas cerradas, bloqueos restantes, y qué quedó fuera. Mismo formato que "Entregable" de AGENTS.md más las secciones anteriores.

## Optimización de tiempos de CI
Auditoría y propuestas de caché, reutilización de builds, paralelización y ejecución selectiva: `docs/ci.md` → "Optimización de tiempos". Se **consultan antes de aplicar** las que toquen gates o la protección de `main`; las de bajo riesgo que no cambian checks requeridos pueden aplicarse en su propia PR, nunca mezcladas con trabajo funcional.
