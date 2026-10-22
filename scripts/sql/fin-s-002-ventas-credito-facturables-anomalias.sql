-- FIN-S-002 (O1/O3): detecta ventas a Crédito Personal en estados facturables cuyo crédito NO cumple la condición
-- que ahora exige FacturarVentaAsync. SOLO LECTURA: únicamente SELECT agregados (conteos por categoría), sin datos
-- personales. Ejecutar sobre la base real ANTES de desplegar; no ejecutar contra producción sin autorización.
-- Enums: TipoPago.CreditoPersonal=5; EstadoVenta Confirmada=2, Facturada=3, Entregada=4;
--        EstadoCredito Aprobado=1, Activo=2, Finalizado=3, Generado=8 (permitidos: 2, 3, 8).
SET NOCOUNT ON;

;WITH v AS (
    SELECT v.Id, v.Estado, v.ClienteId, v.CreditoId
    FROM Ventas v
    WHERE v.IsDeleted = 0 AND v.TipoPago = 5 AND v.Estado IN (2, 3, 4)
)
SELECT
    CASE
        WHEN v.CreditoId IS NULL THEN '1-sin credito asociado'
        WHEN c.Id IS NULL THEN '2-credito inexistente o eliminado'
        WHEN c.ClienteId <> v.ClienteId OR v.ClienteId IS NULL THEN '3-credito de otro cliente'
        WHEN c.Estado NOT IN (2, 3, 8) THEN '4-estado de credito no permitido (' + CAST(c.Estado AS varchar(5)) + ')'
        WHEN NOT EXISTS (SELECT 1 FROM Cuotas q WHERE q.CreditoId = c.Id AND q.IsDeleted = 0) THEN '5-credito sin cuotas'
        WHEN c.CantidadCuotas <> (SELECT COUNT(*) FROM Cuotas q WHERE q.CreditoId = c.Id AND q.IsDeleted = 0)
            THEN '6-cantidad de cuotas distinta de la configurada'
        WHEN ABS(c.MontoAprobado - (SELECT SUM(q.MontoCapital) FROM Cuotas q WHERE q.CreditoId = c.Id AND q.IsDeleted = 0)) > 0.05
            THEN '7-capital de cuotas distinto del monto financiado'
        ELSE 'OK'
    END AS Categoria,
    v.Estado AS EstadoVenta,
    COUNT(*) AS Cantidad
FROM v
LEFT JOIN Creditos c ON c.Id = v.CreditoId AND c.IsDeleted = 0
GROUP BY
    CASE
        WHEN v.CreditoId IS NULL THEN '1-sin credito asociado'
        WHEN c.Id IS NULL THEN '2-credito inexistente o eliminado'
        WHEN c.ClienteId <> v.ClienteId OR v.ClienteId IS NULL THEN '3-credito de otro cliente'
        WHEN c.Estado NOT IN (2, 3, 8) THEN '4-estado de credito no permitido (' + CAST(c.Estado AS varchar(5)) + ')'
        WHEN NOT EXISTS (SELECT 1 FROM Cuotas q WHERE q.CreditoId = c.Id AND q.IsDeleted = 0) THEN '5-credito sin cuotas'
        WHEN c.CantidadCuotas <> (SELECT COUNT(*) FROM Cuotas q WHERE q.CreditoId = c.Id AND q.IsDeleted = 0)
            THEN '6-cantidad de cuotas distinta de la configurada'
        WHEN ABS(c.MontoAprobado - (SELECT SUM(q.MontoCapital) FROM Cuotas q WHERE q.CreditoId = c.Id AND q.IsDeleted = 0)) > 0.05
            THEN '7-capital de cuotas distinto del monto financiado'
        ELSE 'OK'
    END,
    v.Estado
ORDER BY Categoria, EstadoVenta;
-- Solo importan, para el gate de facturación, las categorías 1-5 en ventas Confirmada (Estado=2): son las que
-- FacturarVentaAsync rechazaría. Las de Facturada/Entregada son históricas ya facturadas (informativas).
-- 6-7 son controles de integridad adicionales (no son gate de facturación).
