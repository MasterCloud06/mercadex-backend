-- Antes de desplegar la anulación de deudas. SOLO LEE: no modifica nada.

-- 1. Debe devolver 0: hoy ningún proceso deja deudas en estado 3 (Anulada).
SELECT count(*) AS deudas_ya_anuladas FROM obligaciones WHERE "Estado" = 3;

-- 2. Deudas por estado (1 Pendiente, 2 Pagada, 3 Anulada): guardar para comparar después.
SELECT "Estado", count(*) AS deudas, sum("Monto") AS monto
FROM obligaciones
GROUP BY "Estado"
ORDER BY "Estado";
