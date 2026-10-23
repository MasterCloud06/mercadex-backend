-- Después de desplegar la anulación de deudas. SOLO LEE: no modifica nada.

-- 1. La migración está aplicada y las tablas nuevas existen.
SELECT "MigrationId" FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%AgregarAnulacionDeDeudas';
SELECT count(*) AS anulaciones FROM anulaciones_obligaciones;

-- 2. Deudas por estado: antes de la primera anulación debe ser igual a verificar-antes.sql.
SELECT "Estado", count(*) AS deudas, sum("Monto") AS monto
FROM obligaciones
GROUP BY "Estado"
ORDER BY "Estado";

-- 3. Coherencia: todas deben devolver 0.
--    a) deudas anuladas sin registro de anulación
SELECT count(*) AS anuladas_sin_registro
FROM obligaciones o
WHERE o."Estado" = 3
  AND NOT EXISTS (SELECT 1 FROM detalles_anulacion_obligacion d WHERE d."ObligacionId" = o."Id");
--    b) deudas anuladas que tienen un recibo vigente (los recibos anulados no cuentan)
SELECT count(*) AS anuladas_con_recibo_vigente
FROM detalles_anulacion_obligacion d
JOIN detalles_recibo dr ON dr."ObligacionId" = d."ObligacionId"
JOIN recibos r ON r."Id" = dr."ReciboId"
WHERE r."Estado" <> 2;
--    c) registros de anulación cuya deuda ya no está anulada
SELECT count(*) AS registro_sin_deuda_anulada
FROM detalles_anulacion_obligacion d
JOIN obligaciones o ON o."Id" = d."ObligacionId"
WHERE o."Estado" <> 3;
--    d) monto de la copia distinto del monto de la deuda
SELECT count(*) AS montos_distintos
FROM detalles_anulacion_obligacion d
JOIN obligaciones o ON o."Id" = d."ObligacionId"
WHERE o."Monto" <> d."Monto";
