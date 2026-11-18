-- Migración 20261118150657_AgregarNumeroConcepto (solo para revisar: la aplica la API al iniciar).
-- Agrega el número de cada concepto. Solo escribe en la columna nueva.

BEGIN;

ALTER TABLE conceptos_cobro ADD COLUMN IF NOT EXISTS "Numero" integer NULL;

UPDATE conceptos_cobro AS c
SET "Numero" = n.numero
FROM (SELECT "Id", row_number() OVER (ORDER BY "Id") AS numero FROM conceptos_cobro) AS n
WHERE c."Id" = n."Id" AND c."Numero" IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_conceptos_cobro_numero" ON conceptos_cobro ("Numero") WHERE "Numero" IS NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20261118150657_AgregarNumeroConcepto', '10.0.11'
WHERE NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118150657_AgregarNumeroConcepto');

COMMIT;

-- Comprobar después:
-- SELECT "Numero", "Codigo", "Nombre" FROM conceptos_cobro ORDER BY "Numero";
