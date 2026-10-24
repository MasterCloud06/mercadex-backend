-- Migración 20261024105333_PermitirVolverARegistrarDeudaAnulada (solo para revisar: la aplica la API al iniciar).
-- Solo cambia un índice. No hay UPDATE, DELETE, DROP TABLE ni TRUNCATE: ninguna fila cambia.
-- Todo dentro de una transacción: si algo falla, el índice anterior queda como estaba.

BEGIN;

DROP INDEX IF EXISTS "UX_obligaciones_unidad_concepto_periodo";

-- Una deuda vigente (pendiente o pagada) por caseta, concepto y mes. Las anuladas (estado 3) no cuentan.
CREATE UNIQUE INDEX "UX_obligaciones_unidad_concepto_periodo"
    ON obligaciones ("UnidadComercialId", "ConceptoCobroId", "PeriodoAnio", "PeriodoMes")
    WHERE "PeriodoAnio" IS NOT NULL AND "PeriodoMes" IS NOT NULL AND "Estado" <> 3;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20261024105333_PermitirVolverARegistrarDeudaAnulada', '10.0.11'
WHERE NOT EXISTS (
    SELECT 1 FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20261024105333_PermitirVolverARegistrarDeudaAnulada');

COMMIT;

-- Comprobar después:
-- SELECT indexdef FROM pg_indexes WHERE indexname = 'UX_obligaciones_unidad_concepto_periodo';
