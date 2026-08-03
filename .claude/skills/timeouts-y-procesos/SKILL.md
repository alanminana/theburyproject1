---
name: timeouts-y-procesos
description: Control de tiempo, timeouts y errores inesperados en TheBuryProject: builds o tests colgados, estrategia de validación por partes y formato de reporte de timeout.
---

## Control de tiempo, timeouts y errores inesperados

No ejecutar comandos largos indefinidamente.

### Reglas generales

1. Si un comando tarda más de lo razonable, detenerlo y diagnosticar.
2. No repetir el mismo comando largo más de dos veces.
3. No ejecutar suites completas por defecto.
4. Separar validaciones pesadas.
5. Usar timeouts razonables.
6. No continuar relanzando comandos sin comprender la causa.

### Si un build o test queda colgado

1. identificar procesos iniciados por esta ejecución;
2. registrar sus PID;
3. cerrar únicamente procesos propios;
4. no matar procesos ajenos sin evidencia;
5. documentar qué se cerró;
6. intentar una validación focalizada alternativa.

### Estrategia recomendada

Ejecutar por separado:

1. restore, únicamente si hace falta;
2. build del proyecto principal;
3. build del proyecto de tests;
4. test unitario filtrado;
5. test de integración filtrado;
6. test Playwright focalizado;
7. suite completa solo en pre-merge o por pedido explícito.

### Opciones útiles

```powershell
dotnet build --no-restore /nr:false
dotnet test --no-build --filter "FullyQualifiedName~NombreDelTest"
dotnet test --blame-hang --blame-hang-timeout 120s
npx.cmd playwright test tests/modulo.spec.ts
npx.cmd playwright test --grep "caso específico"
```

Usar `--no-restore` únicamente si las dependencias ya fueron restauradas.

Usar `--no-build` únicamente si existe un binario válido y actualizado.

### Timeout

Si una validación falla por timeout:

```text
Timeout:

- comando:
- duración aproximada:
- punto donde se detuvo:
- proceso relacionado:
- acción tomada:
- validación alternativa:
- estado final:
```

### Gestión de procesos

Cuando sea necesario levantar la aplicación:

* registrar el comando;
* registrar el PID;
* confirmar la URL;
* no iniciar múltiples instancias innecesarias;
* cerrar los procesos propios al terminar;
* verificar que no queden procesos propios de:

  * `dotnet`;
  * `testhost`;
  * `playwright`;
  * `node`;
  * navegadores iniciados por la ejecución.

No cerrar procesos sin poder relacionarlos con la ejecución actual.

Ejemplo:

```text
Proceso app: PID 1234 iniciado
URL: http://localhost:5187
Proceso app: PID 1234 cerrado
Procesos propios restantes: ninguno
```

### Error inesperado fuera de scope

Si aparece un error no relacionado:

1. detenerse;
2. recopilar evidencia mínima;
3. no corregirlo;
4. reportarlo como bloqueo o deuda;
5. no ampliar el scope.

---
