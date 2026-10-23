-- SOLO PARA LA BASE LOCAL DE PRUEBAS (mercadex_pruebas). Nunca en Render.
-- Las monedas no tienen pantalla ni endpoint para crearlas; en una base
-- nueva hay que insertarlas. Se puede ejecutar varias veces.

DO $$
BEGIN
    IF current_database() <> 'mercadex_pruebas' THEN
        RAISE EXCEPTION 'Este script es solo para la base local mercadex_pruebas (estás en %).', current_database();
    END IF;
END $$;

INSERT INTO monedas ("Id", "Codigo", "Nombre", "Simbolo", "Activa")
SELECT '0192c000-0000-7000-8000-000000000001', 'BOB', 'Boliviano', 'Bs', TRUE
WHERE NOT EXISTS (SELECT 1 FROM monedas WHERE "Codigo" = 'BOB');

INSERT INTO monedas ("Id", "Codigo", "Nombre", "Simbolo", "Activa")
SELECT '0192c000-0000-7000-8000-000000000002', 'USD', 'Dólar estadounidense', '$us', TRUE
WHERE NOT EXISTS (SELECT 1 FROM monedas WHERE "Codigo" = 'USD');
