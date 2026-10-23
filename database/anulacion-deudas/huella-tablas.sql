-- Huella (md5) del contenido de cada tabla, para comprobar que una
-- migración o una prueba no modificó datos existentes. Solo lee.
SELECT 'obligaciones' AS tabla, count(*) AS filas, md5(coalesce(string_agg(t::text, '|' ORDER BY t."Id"), '')) AS huella FROM obligaciones t
UNION ALL SELECT 'recibos', count(*), md5(coalesce(string_agg(t::text, '|' ORDER BY t."Id"), '')) FROM recibos t
UNION ALL SELECT 'detalles_recibo', count(*), md5(coalesce(string_agg(t::text, '|' ORDER BY t."Id"), '')) FROM detalles_recibo t
UNION ALL SELECT 'pagos', count(*), md5(coalesce(string_agg(t::text, '|' ORDER BY t."Id"), '')) FROM pagos t
UNION ALL SELECT 'pagos_reportados', count(*), md5(coalesce(string_agg(t::text, '|' ORDER BY t."Id"), '')) FROM pagos_reportados t
UNION ALL SELECT 'cambios_monto_obligacion', count(*), md5(coalesce(string_agg(t::text, '|' ORDER BY t."Id"), '')) FROM cambios_monto_obligacion t
UNION ALL SELECT 'socios', count(*), md5(coalesce(string_agg(t::text, '|' ORDER BY t."Id"), '')) FROM socios t
UNION ALL SELECT 'unidades_comerciales', count(*), md5(coalesce(string_agg(t::text, '|' ORDER BY t."Id"), '')) FROM unidades_comerciales t
UNION ALL SELECT 'registro_cambios', count(*), md5(coalesce(string_agg(t::text, '|' ORDER BY t."Id"), '')) FROM registro_cambios t
ORDER BY 1;
