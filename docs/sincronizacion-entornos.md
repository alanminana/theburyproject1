# Sincronización entre GitHub, Windows y Ubuntu

## Qué se sincroniza y qué no

| Elemento | Fuente de verdad | Cómo llega al resto |
|---|---|---|
| Código, migraciones, scripts, documentación | **`origin/main` en GitHub** | `git` (solo fast-forward) |
| Esquema de cada base | Las migraciones del código | Windows: manual y autorizado · Ubuntu: `migrate` dentro de `install.sh` |
| Datos | Cada instalación tiene los suyos | **No se sincronizan.** Se mueven solo con respaldo y restauración autorizados |
| `.env`, `backup-offsite.env`, claves | Cada servidor + custodia externa | Nunca por Git ([operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §3.2) |
| user-secrets, `.claude/settings.local.json` | Cada equipo de desarrollo | Nunca por Git |

## Flujo

```text
rama en un worktree ──push──> PR en GitHub ──CI verde──> main
                                                          ├─ Windows: iniciar-erp-local.ps1 -Actualizar
                                                          └─ Ubuntu:  respaldo → git pull --ff-only → install.sh --yes
```

1. **Se cambia código solo en Windows**, en una rama de un worktree propio. El checkout principal queda en `main` y limpio.
2. **Se integra solo por PR**, con los checks del commit final aprobados.
3. **Windows** se pone al día con el lanzador ([desarrollo-local.md](desarrollo-local.md)). Integrar no actualiza ninguna instancia en ejecución.
4. **Ubuntu** se actualiza cuando se decide desplegar ([operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §2). **Integrado no significa desplegado.**
5. En el servidor **no se edita ni se hace commit**. Un arreglo urgente también pasa por PR.

## Comprobar que las tres partes coinciden

```powershell
# GitHub
git ls-remote origin refs/heads/main
# Windows (en el checkout principal)
git fetch origin; git rev-list --left-right --count origin/main...HEAD      # esperado: 0  0
(Get-Item bin\Debug\net10.0\TheBuryProyect.dll).VersionInfo.ProductVersion  # commit realmente compilado
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\iniciar-erp-local.ps1 -SoloVerificar
```

```bash
# Ubuntu
git rev-parse HEAD
cat .deploy-state/theburyproject/current-image          # AAAAMMDD-<commit> de lo desplegado
docker inspect --format '{{.Config.Image}}' "$(docker compose ps -q app)"   # lo que está corriendo
```

`git rev-parse HEAD` indica el código **descargado**; la imagen indica el **desplegado**. Si difieren, hubo `pull` sin despliegue o un rollback.
Una imagen terminada en `-dirty` se construyó con cambios sin commit: no usarla en producción.

## Registro de despliegues

El servidor guarda `.deploy-state/<proyecto>/releases.log`, que **se pierde con el servidor**. Anotar además fuera de él, tras cada despliegue:
fecha, commit, imagen, cantidad de migraciones, resultado de la verificación y fecha del paquete de recuperación generado.

## Errores que ya ocurrieron

- Ejecutar el ERP local desde una rama atrasada: la DLL podía ser del día y el código de semanas antes. Mirar el commit, no la fecha.
- Tener la base con migraciones que el código no conoce, o al revés. El lanzador se niega en ambos casos.
- `docker compose up` sin `ERP_IMAGE` en el servidor ([operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §0).
