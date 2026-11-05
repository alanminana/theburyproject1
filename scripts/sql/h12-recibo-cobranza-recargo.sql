/*
  H12 - RECIBO_COBRANZA: discriminar recargo/descuento del medio de pago y mostrar el total efectivamente cobrado.

  Por que existe: la plantilla por defecto del recibo cambio en la PR #227, pero una base ya inicializada conserva su plantilla
  persistida (el sembrador no reescribe plantillas existentes). Sin este script el recibo documenta solo el importe base aunque
  Caja registre importe + recargo.

  Que hace: crea UNA version nueva de la plantilla RECIBO_COBRANZA reemplazando unicamente el bloque final estandar
      >> Monto total: {{pago.importeTotalFormato}}
      Son Pesos: ${{pago.importeTotalFormato}}
      Recibi conforme la suma de {{pago.importeTotalLetras}}
  por el bloque con ajuste (usa las variables pago.tieneAjusteMedioPago, pago.ajusteMedioPagoEtiqueta, pago.ajusteMedioPagoFormato,
  pago.totalCobradoFormato y pago.totalCobradoLetras, disponibles desde la PR #227). El resto de la plantilla se conserva tal cual.

  Seguridad:
   - La version anterior NO se modifica ni se borra: queda en PlantillasDocumentoVersion (se puede volver cambiando VersionActual).
   - Los documentos ya emitidos no se tocan (guardan su contenido renderizado).
   - Si la plantilla vigente fue personalizada y NO termina con el bloque estandar, NO se modifica (se informa y se sale).
   - Si ya contiene el bloque nuevo, no hace nada (idempotente).
   - Transaccional y serializado ante ejecuciones simultaneas.

  Requisito: la aplicacion debe correr una version con las variables nuevas (main >= a881a006) antes de emitir recibos.

  Uso (sqlcmd; -f 65001 y -I son obligatorios):
    sqlcmd -S <servidor> -d <base> -I -b -f 65001 -i h12-recibo-cobranza-recargo.sql -v DryRun=1   (ensayo: revierte)
    sqlcmd -S <servidor> -d <base> -I -b -f 65001 -i h12-recibo-cobranza-recargo.sql -v DryRun=0   (aplica)

  Hacer un respaldo verificado antes de aplicar (BACKUP ... WITH CHECKSUM; RESTORE VERIFYONLY).
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun bit = $(DryRun);
DECLARE @Autor nvarchar(256) = N'H12-RECIBO';
DECLARE @Ahora datetime2 = SYSUTCDATETIME();
DECLARE @Lf nchar(1) = NCHAR(10);

BEGIN TRANSACTION;

DECLARE @Lock int;
EXEC @Lock = sp_getapplock @Resource = N'h12-recibo-cobranza', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
IF @Lock < 0
    THROW 51020, 'No se pudo obtener el bloqueo de aplicacion: hay otra ejecucion en curso.', 1;

DECLARE @PlantillaId int, @VersionActual int;
SELECT @PlantillaId = Id, @VersionActual = VersionActual
FROM dbo.PlantillasDocumento WITH (UPDLOCK, HOLDLOCK)
WHERE Codigo = N'RECIBO_COBRANZA' AND IsDeleted = 0;

IF @PlantillaId IS NULL
    THROW 51021, 'No existe la plantilla RECIBO_COBRANZA: no se modifica nada.', 1;

DECLARE @Contenido nvarchar(max), @Variables nvarchar(max);
SELECT @Contenido = Contenido, @Variables = VariablesRequeridas
FROM dbo.PlantillasDocumentoVersion
WHERE PlantillaDocumentoId = @PlantillaId AND Numero = @VersionActual AND IsDeleted = 0;

IF @Contenido IS NULL
    THROW 51022, 'La version vigente de RECIBO_COBRANZA no existe: no se modifica nada.', 1;

DECLARE @Norm nvarchar(max) = RTRIM(REPLACE(@Contenido, NCHAR(13) + NCHAR(10), @Lf));

IF CHARINDEX(N'pago.tieneAjusteMedioPago', @Norm) > 0
BEGIN
    PRINT 'RECIBO_COBRANZA ya contiene el bloque con ajuste: no se hace nada.';
    ROLLBACK TRANSACTION;
    RETURN;
END;

DECLARE @Viejo nvarchar(max) =
    N'>> Monto total: {{pago.importeTotalFormato}}' + @Lf + @Lf +
    N'Son Pesos: ${{pago.importeTotalFormato}}' + @Lf + @Lf +
    N'Recib' + NCHAR(237) + N' conforme la suma de {{pago.importeTotalLetras}}';

DECLARE @Nuevo nvarchar(max) =
    N'>> Monto total: {{pago.importeTotalFormato}}' + @Lf +
    N'{{#pago.tieneAjusteMedioPago}}' + @Lf +
    N'>> {{pago.ajusteMedioPagoEtiqueta}}: {{pago.ajusteMedioPagoFormato}}' + @Lf +
    N'>> Total cobrado: {{pago.totalCobradoFormato}}' + @Lf +
    N'{{/pago.tieneAjusteMedioPago}}' + @Lf + @Lf +
    N'Son Pesos: ${{pago.totalCobradoFormato}}' + @Lf + @Lf +
    N'Recib' + NCHAR(237) + N' conforme la suma de {{pago.totalCobradoLetras}}';

IF LEN(@Norm) < LEN(@Viejo) OR RIGHT(@Norm, LEN(@Viejo)) <> @Viejo COLLATE Latin1_General_CS_AS
BEGIN
    PRINT 'RECIBO_COBRANZA fue personalizada y no termina con el bloque estandar: NO se modifica. Editar desde Configuracion de documentos.';
    ROLLBACK TRANSACTION;
    RETURN;
END;

DECLARE @Siguiente int = (SELECT ISNULL(MAX(Numero), 0) + 1 FROM dbo.PlantillasDocumentoVersion WHERE PlantillaDocumentoId = @PlantillaId);
DECLARE @ContenidoNuevo nvarchar(max) = LEFT(@Norm, LEN(@Norm) - LEN(@Viejo)) + @Nuevo;

INSERT INTO dbo.PlantillasDocumentoVersion (PlantillaDocumentoId, Numero, Contenido, VariablesRequeridas, Comentario, CreatedAt, CreatedBy, IsDeleted)
VALUES (@PlantillaId, @Siguiente, @ContenidoNuevo, @Variables, N'Recargo/descuento del medio de pago y total cobrado (H12)', @Ahora, @Autor, 0);

UPDATE dbo.PlantillasDocumento SET VersionActual = @Siguiente, UpdatedAt = @Ahora, UpdatedBy = @Autor WHERE Id = @PlantillaId;

PRINT CONCAT('RECIBO_COBRANZA: version ', @VersionActual, ' -> ', @Siguiente, '.');

IF @DryRun = 1
BEGIN
    PRINT 'DryRun=1: se revierte.';
    ROLLBACK TRANSACTION;
END
ELSE
BEGIN
    COMMIT TRANSACTION;
    PRINT 'Aplicado.';
END;
