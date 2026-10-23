START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023184008_AgregarAnulacionDeDeudas') THEN
    CREATE TABLE anulaciones_obligaciones (
        "Id" uuid NOT NULL,
        "ClaveOperacion" uuid NOT NULL,
        "Tipo" integer NOT NULL,
        "Motivo" character varying(500) NOT NULL,
        "Responsable" character varying(120) NOT NULL,
        "ResponsableId" character varying(120),
        "Filtros" character varying(1000),
        "FechaUtc" timestamp with time zone NOT NULL,
        "Cantidad" integer NOT NULL,
        CONSTRAINT "PK_anulaciones_obligaciones" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023184008_AgregarAnulacionDeDeudas') THEN
    CREATE TABLE detalles_anulacion_obligacion (
        "Id" uuid NOT NULL,
        "AnulacionId" uuid NOT NULL,
        "ObligacionId" uuid NOT NULL,
        "UnidadComercialId" uuid NOT NULL,
        "ConceptoCobroId" uuid NOT NULL,
        "MonedaId" uuid NOT NULL,
        "Monto" numeric(18,2) NOT NULL,
        "PeriodoAnio" integer,
        "PeriodoMes" integer,
        "EstadoAnterior" integer NOT NULL,
        CONSTRAINT "PK_detalles_anulacion_obligacion" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_detalles_anulacion_obligacion_anulaciones_obligaciones_Anul~" FOREIGN KEY ("AnulacionId") REFERENCES anulaciones_obligaciones ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_detalles_anulacion_obligacion_conceptos_cobro_ConceptoCobro~" FOREIGN KEY ("ConceptoCobroId") REFERENCES conceptos_cobro ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_detalles_anulacion_obligacion_monedas_MonedaId" FOREIGN KEY ("MonedaId") REFERENCES monedas ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_detalles_anulacion_obligacion_obligaciones_ObligacionId" FOREIGN KEY ("ObligacionId") REFERENCES obligaciones ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_detalles_anulacion_obligacion_unidades_comerciales_UnidadCo~" FOREIGN KEY ("UnidadComercialId") REFERENCES unidades_comerciales ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023184008_AgregarAnulacionDeDeudas') THEN
    CREATE UNIQUE INDEX "IX_anulaciones_obligaciones_ClaveOperacion" ON anulaciones_obligaciones ("ClaveOperacion");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023184008_AgregarAnulacionDeDeudas') THEN
    CREATE INDEX "IX_anulaciones_obligaciones_FechaUtc" ON anulaciones_obligaciones ("FechaUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023184008_AgregarAnulacionDeDeudas') THEN
    CREATE INDEX "IX_detalles_anulacion_obligacion_AnulacionId" ON detalles_anulacion_obligacion ("AnulacionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023184008_AgregarAnulacionDeDeudas') THEN
    CREATE INDEX "IX_detalles_anulacion_obligacion_ConceptoCobroId" ON detalles_anulacion_obligacion ("ConceptoCobroId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023184008_AgregarAnulacionDeDeudas') THEN
    CREATE INDEX "IX_detalles_anulacion_obligacion_MonedaId" ON detalles_anulacion_obligacion ("MonedaId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023184008_AgregarAnulacionDeDeudas') THEN
    CREATE UNIQUE INDEX "IX_detalles_anulacion_obligacion_ObligacionId" ON detalles_anulacion_obligacion ("ObligacionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023184008_AgregarAnulacionDeDeudas') THEN
    CREATE INDEX "IX_detalles_anulacion_obligacion_UnidadComercialId" ON detalles_anulacion_obligacion ("UnidadComercialId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023184008_AgregarAnulacionDeDeudas') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261023184008_AgregarAnulacionDeDeudas', '10.0.11');
    END IF;
END $EF$;
COMMIT;

