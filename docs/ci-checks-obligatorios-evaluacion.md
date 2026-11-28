# Checks de seguridad e infraestructura: ¿obligatorios en `main`?

Evaluación del 2026-11-27. **La protección de `main` no se modificó**: cambiarla requiere la decisión del propietario.

## Situación actual

Obligatorios: `build-test`, `docker-build`, `compose-validate`, `secret-scan` y `test` (Playwright). La protección usa `strict` (la rama tiene
que estar al día con `main`) y se aplica también a administradores: un check obligatorio roto **bloquea toda fusión**.

El resto corre y pone el PR en rojo, pero **no impide fusionar**.

## Historial medido

Últimas corridas con resultado (hasta 40 por workflow, desde el 23–25/11), consultadas con la API de GitHub.

| Check | Corre en | Corridas / fallos | Duración mediana |
|---|---|---|---|
| `sqlserver-superadmin (0)` y `(1)` | todo PR | 28 / 0 cada uno | 7–8 min |
| `sqlserver-configventa (0)` y `(1)` | todo PR | 29 / 0 | 4 min |
| `sqlserver-fin-s-002 (0)` y `(1)` | todo PR | 29 / 0 | 5–6 min |
| `sqlserver-concurrencia (0)` y `(1)` | todo PR | 25 / 0 | 19–20 min |
| `windows-scripts` | todo PR | 26 / 0 (con el lanzador solo desde #382) | 46 s antes; 80–130 s con el lanzador |
| `network-security` | todo PR | 26 / 0, casi todas **sin** las pruebas destructivas | 51 s antes; 322 s desde #382; con la vigilancia, unos 10 min |
| `host-independence` | **solo si cambian ciertas rutas** | 32 / 11 | 6 min |
| `recuperacion-integral`, `rotacion-claves`, `custodia-clave-ca` | **solo ciertas rutas** | 27 / 0 | 2–6 min |
| `simulacro-sftp` | **solo ciertas rutas** | 28 / 2 | 4 min |

Los fallos de `host-independence` y `simulacro-sftp` ocurrieron en ramas en desarrollo. Se inspeccionaron tres: uno detectó un problema real
del árbol de trabajo (finales de línea), otro terminó con la aplicación sin llegar a `healthy` y el de SFTP con un conteo de objetos distinto del
esperado. **No se clasificaron los demás**: no está demostrado si alguno fue intermitente.

## Recomendación

### Paso 1: exigir ya

`sqlserver-superadmin`, `sqlserver-configventa`, `sqlserver-fin-s-002` (las dos variantes de cada uno) y `windows-scripts`.

- Cubren escalada de privilegios, integridad de crédito y manipulación de ventas.
- Corren en todos los PR, sin filtro de rutas, y no fallaron en la ventana medida.
- No alargan la espera: corren en paralelo y `test` (obligatorio) tarda unos 23 min.

### Paso 2: `network-security`, después de observarlo

Contiene las pruebas que reinician Docker, borran reglas y miden accesos reales, justamente lo que conviene que bloquee. Pero con ese contenido
tiene muy pocas corridas, y las pruebas dependen de tiempos. Exigirlo tras **10 corridas verdes seguidas** con la vigilancia incluida, sin
reintentos. Si aparece un fallo intermitente, corregir la causa antes.

### Paso 3 (opcional): `sqlserver-concurrencia`

0 fallos y por debajo de la duración de `test`. Es integridad financiera más que seguridad o infraestructura: decisión del propietario.

### No exigir como están: los cinco con filtro de rutas

Si un check obligatorio no se dispara, GitHub lo deja en «Expected» y **el PR no se puede fusionar nunca**. Para exigirlos hace falta antes un
cambio de workflows, en un PR aparte:

1. un workflow que se dispare en **todo** PR, detecte las rutas cambiadas y ejecute los jobs solo cuando corresponda;
2. un job final que corra siempre (`if: always()`), falle si alguno de los anteriores falló o se canceló, y apruebe si se omitieron;
3. exigir **solo** ese job final.

### Nunca: `build-test-windows`

Tiene `continue-on-error: true`; exigirlo no bloquearía nada.

## Cómo aplicarlo, cuando se autorice

En GitHub: *Settings → Branches → `main` → Require status checks to pass*, agregar los nombres **exactos** de la tabla (las matrices son un
check por variante). Antes, anotar la lista actual. Después, abrir un PR de prueba y confirmar que todos aparecen y terminan.

Para volver atrás, quitar de la lista el check que bloquee. Cambiar el nombre de un job o su matriz deja el nombre viejo esperando para
siempre: actualizar la protección en la misma ventana que ese cambio.
