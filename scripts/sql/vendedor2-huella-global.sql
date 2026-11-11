/*
  vendedor2-huella-global.sql  --  SOLO LECTURA.  Huella de TODA la base: por cada tabla de usuario, filas y suma de
  verificacion de todas las columnas. Sirve para demostrar que eliminar / restaurar el rol vendedor2 cambia SOLO lo esperado:
  guardar la salida antes y despues y compararlas (diff). Excluye la tabla propia de respaldo dbo.__Respaldo_Rol_vendedor2.

  Uso:
    sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -I -W -h -1 -s "|" -f 65001 -d <base> -i vendedor2-huella-global.sql -v BaseEsperada=<base> > huella-antes.txt

  Notas: la suma de verificacion usa BINARY_CHECKSUM(*) (columnas rowversion incluidas: cambian si la fila se reescribe,
  p. ej. tras un rollback que reinserta filas; por eso la comparacion "rol restaurado" usa tambien las sumas por columnas
  funcionales de vendedor2-preflight.sql). Las tablas con tipos no soportados (text, ntext, image, xml) solo informan filas.
*/
SET NOCOUNT ON;
IF DB_NAME() <> N'$(BaseEsperada)'
    THROW 52301, 'Base incorrecta (BaseEsperada no coincide con DB_NAME()).', 1;
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'BEGIN TRY SELECT N''' + s.name + N'.' + t.name + N''' AS Tabla, COUNT(*) AS Filas, '
    + CASE WHEN EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                        WHERE c.object_id = t.object_id AND ty.name IN (N'text', N'ntext', N'image', N'xml', N'geography', N'geometry', N'hierarchyid', N'sql_variant'))
           THEN N'CAST(NULL AS int)' ELSE N'CHECKSUM_AGG(BINARY_CHECKSUM(*))' END
    + N' AS Suma FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N'; END TRY BEGIN CATCH SELECT N''' + s.name + N'.' + t.name + N''', -1, -1; END CATCH;' + CHAR(10)
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0 AND t.name <> N'__Respaldo_Rol_vendedor2'
ORDER BY s.name, t.name;
EXEC sp_executesql @sql;
