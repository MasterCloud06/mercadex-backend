# Anulación de deudas: despliegue, respaldo y reversión

Migración `20261023184008_AgregarAnulacionDeDeudas`. **Solo agrega**:

- tabla `anulaciones_obligaciones`: una fila por anulación (quién, cuándo, motivo opcional, filtros, clave de operación única);
- tabla `detalles_anulacion_obligacion`: una fila por deuda anulada, con copia de monto, moneda, concepto, caseta y período (índice único por deuda);
- sus índices y claves foráneas (`ON DELETE RESTRICT`).

No modifica la tabla `obligaciones` ni ninguna otra: no hay `UPDATE`, `DELETE`, `DROP` ni `TRUNCATE`.
Las deudas existentes no cambian. Una deuda solo pasa a estado 3 (Anulada) cuando un administrador
la anula desde el panel o el escritorio. El script SQL equivalente, para revisarlo, está en
`20261023184008_AgregarAnulacionDeDeudas.sql` (idempotente).

`Down()` no borra las tablas a propósito (lanza un error): conservan el historial de anulaciones.

## Antes de desplegar

1. **Respaldo** de la base de producción:
   - Render → base de datos → *Backups* → crear uno manual y esperar a que termine, y además
   - copia propia con `pg_dump` (usar la *External Database URL* de Render, nunca escribirla en el repositorio):

     ```powershell
     pg_dump --format=custom --no-owner --file "mercadex-antes-anulacion-$(Get-Date -Format yyyyMMdd-HHmm).dump" "<External Database URL>"
     pg_restore --list "mercadex-antes-anulacion-AAAAMMDD-HHMM.dump" | Select-Object -First 20   # comprobar que se puede leer
     ```

2. Ejecutar `verificar-antes.sql` (solo lee) y guardar el resultado. La consulta 1 debe dar **0**.
3. Ejecutar `huella-tablas.sql` (solo lee) y guardar el resultado.
4. Probar en local con una copia de la base (ver `tests/anulacion-deudas/README.md`).

## Desplegar (con autorización)

1. Merge a `main` de **mercadex-backend** → Render despliega la API. Si `MERCADEX_APLICAR_MIGRACIONES=true`
   la migración se aplica al iniciar (crea dos tablas vacías: segundos).
2. `https://mercadex-api.onrender.com/api/health` → `"migrations.pendientes": 0`.
3. Ejecutar otra vez `huella-tablas.sql`: todas las huellas deben coincidir con las del paso 3 de arriba
   (si alguien cobró en ese intervalo, cambian `obligaciones`, `recibos`, `pagos`, `detalles_recibo` y `registro_cambios`; las demás no).
4. Ejecutar `verificar-despues.sql`: la migración está aplicada, las deudas por estado coinciden con las de antes y las consultas de coherencia dan 0.
5. Recién entonces publicar **mercadex-panel** (merge a `main`) y el instalador del **escritorio**.
   Si se publican antes, sus botones de anulación reciben 404 de la API anterior; nada se rompe.

## Volver atrás

La API anterior no usa las tablas nuevas, así que **no hace falta revertir la base**:

1. En Render, *Rollback* del servicio `mercadex-api` al despliegue anterior (o revert del merge en `main`).
2. Dejar las tablas `anulaciones_obligaciones` y `detalles_anulacion_obligacion` como están (conservan el historial).
3. Las deudas que se hayan anulado quedan en estado 3: la versión anterior ya las excluye de los saldos
   (todas sus consultas de saldo cuentan solo el estado 1) y las muestra como «Anulada» en el escritorio.
4. Limitación de la versión anterior: su «Cargo mensual a varias casetas» no considera las deudas
   anuladas como existentes. Si se repite el cargo de un mes que tiene deudas anuladas, el índice único
   de la base lo rechaza (error 500, no se guarda nada de ese cargo). No se pierde ni duplica nada,
   pero ese mes no se puede volver a generar con la versión anterior.

Restaurar el respaldo **solo** si se detecta pérdida o corrupción de datos, porque se perderían los cobros
registrados después del respaldo. En ese caso: Render → *Backups* → *Restore*, o
`pg_restore --clean --if-exists --no-owner -d "<URL de una base NUEVA>" archivo.dump` sobre una base nueva y
cambiar la conexión del servicio después de verificarla.
