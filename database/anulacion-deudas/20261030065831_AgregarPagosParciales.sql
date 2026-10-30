-- Migración 20261030065831_AgregarPagosParciales (solo para revisar: la aplica la API al iniciar).
-- Solo agrega una columna con 0 por defecto. No hay UPDATE, DELETE, DROP ni TRUNCATE.

BEGIN;

ALTER TABLE obligaciones ADD COLUMN IF NOT EXISTS "MontoPagado" numeric(18,2) NOT NULL DEFAULT 0.0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20261030065831_AgregarPagosParciales', '10.0.11'
WHERE NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261030065831_AgregarPagosParciales');

COMMIT;

-- Comprobar después: todas en 0 hasta el primer pago parcial.
-- SELECT count(*), sum("MontoPagado") FROM obligaciones;
