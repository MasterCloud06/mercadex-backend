START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023194916_AgregarObservacionesRecibo') THEN
    ALTER TABLE recibos ADD "Observaciones" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023194916_AgregarObservacionesRecibo') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261023194916_AgregarObservacionesRecibo', '10.0.11');
    END IF;
END $EF$;
COMMIT;

