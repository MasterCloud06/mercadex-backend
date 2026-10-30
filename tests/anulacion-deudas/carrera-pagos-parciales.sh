#!/bin/bash
# Carrera de pagos parciales: prueba determinista, solo contra la API y la base locales.
# Una deuda de 600 con un abono de 200 (recibo A). Mientras la base abona 100 más
# (con la fila bloqueada 3 s), se anula A. Correcto: queda MontoPagado = 100.
# Sin el FOR UPDATE de ObligacionRepository.RevertirAbonoAsync quedaría 0 (se pierde el abono).
# Necesita las variables de tests/anulacion-deudas/README.md y psql como postgres.
B=${MERCADEX_API_URL:-http://127.0.0.1:5189}; B=${B%/}
[[ "$B" =~ ^http://(127\.0\.0\.1|localhost) ]] || { echo "Solo contra la API local."; exit 1; }
H="X-Mercadex-Key: $MERCADEX_API_KEY"
TOKEN=$(curl -s -X POST $B/api/auth/login -H 'Content-Type: application/json' -d "{\"usuario\":\"$MERCADEX_ADMIN_USER\",\"password\":\"$MERCADEX_ADMIN_PASSWORD\"}" | python3 -c 'import json,sys;print(json.load(sys.stdin)["token"])')
AUT="Authorization: Bearer $TOKEN"
sql(){ runuser -u postgres -- psql -d mercadex_pruebas -tA -c "$1"; }
U=$(sql "select \"Id\" from unidades_comerciales where \"Activa\" order by \"Codigo\" limit 1")
C=$(sql "select \"Id\" from conceptos_cobro where \"Activo\" limit 1")
M=$(sql "select \"Id\" from monedas where \"Codigo\"='BOB'")
MES=$(( (RANDOM % 12) + 1 )); ANIO=$(( 2030 + RANDOM % 50 ))
curl -s -X POST $B/api/cobranza/cargo -H "$H" -H "$AUT" -H 'Content-Type: application/json' -d "{\"unidadComercialId\":\"$U\",\"conceptoCobroId\":\"$C\",\"monedaId\":\"$M\",\"monto\":600,\"fechaEmision\":\"2026-10-01\",\"fechaVencimiento\":null,\"anio\":$ANIO,\"mes\":$MES}" >/dev/null
O=$(sql "select \"Id\" from obligaciones where \"UnidadComercialId\"='$U' and \"ConceptoCobroId\"='$C' and \"PeriodoAnio\"=$ANIO and \"PeriodoMes\"=$MES and \"Estado\"=1")
R=$(curl -s -X POST $B/api/caja/cobrar -H "$H" -H "$AUT" -H 'Content-Type: application/json' -d "{\"numeroRecibo\":0,\"unidadComercialId\":\"$U\",\"monedaId\":\"$M\",\"monto\":200,\"formaPago\":1,\"nombrePagador\":\"Prueba\",\"tipoCambio\":1,\"referencia\":null,\"items\":[{\"obligacionId\":\"$O\",\"nombreConcepto\":\"X\",\"monto\":200}]}" | python3 -c 'import json,sys;print(json.load(sys.stdin)["recibo"]["id"])')
( runuser -u postgres -- psql -d mercadex_pruebas -q -c "begin; select 1 from obligaciones where \"Id\"='$O' for update; update obligaciones set \"MontoPagado\" = \"MontoPagado\" + 100 where \"Id\"='$O'; select pg_sleep(3); commit;" >/dev/null ) &
sleep 0.7
curl -s -o /dev/null -w "anular: %{http_code}\n" -X PUT $B/api/caja/recibos/$R/anular -H "$H" -H "$AUT" -H 'Content-Type: application/json' -d '{"motivo":"Prueba de carrera"}'
wait
echo "MontoPagado final: $(sql "select \"MontoPagado\" from obligaciones where \"Id\"='$O'") (correcto: 100.00)"
