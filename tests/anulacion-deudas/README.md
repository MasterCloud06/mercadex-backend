# Pruebas locales de la anulación de deudas

Todo contra una base **local** de pruebas (`mercadex_pruebas`), nunca la de Render.
Los scripts `.mjs` se niegan a ejecutarse si `MERCADEX_API_URL` no es `127.0.0.1` o `localhost`,
y `01-monedas.sql` se niega si la base no se llama `mercadex_pruebas`.

Requisitos: PostgreSQL local, .NET 10 SDK, Node 20+ y `dotnet tool install --global dotnet-ef`.

## 1. Base local de pruebas

```powershell
psql -U postgres -c "CREATE ROLE mercadex_pruebas LOGIN PASSWORD 'pruebas_local_2026';"
psql -U postgres -c "CREATE DATABASE mercadex_pruebas OWNER mercadex_pruebas;"
```

## 2. Variables de esta consola (solo locales)

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__MercadexDatabase = "Host=127.0.0.1;Port=5432;Database=mercadex_pruebas;Username=mercadex_pruebas;Password=pruebas_local_2026"
$env:MERCADEX_JWT_KEY = "clave-jwt-solo-para-pruebas-locales-0123456789abcdef"
$env:MERCADEX_ADMIN_USER = "admin"
$env:MERCADEX_ADMIN_PASSWORD = "Admin-Local-2026"
$env:MERCADEX_ADMIN_NOMBRE = "Administrador Local"
$env:MERCADEX_API_KEY = "clave-app-solo-local"   # igual que en Render: el escritorio necesita la clave, el panel no
```

## 3. Datos de prueba, como en producción hoy

Primero con la base en la migración anterior y la API **anterior** (rama `main`), para que los datos
se creen exactamente como los crea hoy Mercadex:

```powershell
cd mercadex-backend
git switch main
dotnet ef database update --project src/Mercadex.Infrastructure --startup-project src/Mercadex.Api
psql -h 127.0.0.1 -U mercadex_pruebas -d mercadex_pruebas -f tests/anulacion-deudas/01-monedas.sql
dotnet run --project src/Mercadex.Api --urls http://127.0.0.1:5189      # dejar abierta
# en otra consola, con las mismas variables:
node tests/anulacion-deudas/sembrar-datos-prueba.mjs antes
psql -h 127.0.0.1 -U mercadex_pruebas -d mercadex_pruebas -At -f database/anulacion-deudas/huella-tablas.sql > huella-antes.txt
```

Queda: 11 casetas (A-01…A-06, B-01…B-04, comedor C-01), 4 socios titulares, alquiler de septiembre y
octubre, limpieza de octubre (bloque A) y una multa en dólares (B-03). Pagadas: A-01 (sept. y oct.),
A-02 (solo sept.: parte pagada y parte pendiente). A-03 sept.: cobrada y con el recibo anulado.
B-01 oct.: pago QR enviado por el socio, en revisión.

> Mercadex no registra pagos parciales de una deuda: cada cobro paga deudas completas. El caso
> «parcialmente pagado» es una caseta con unas deudas pagadas y otras pendientes (A-02).

## 4. API nueva

Detener la API (Ctrl+C) y:

```powershell
git switch <rama de la anulación>
dotnet ef database update --project src/Mercadex.Infrastructure --startup-project src/Mercadex.Api
psql -h 127.0.0.1 -U mercadex_pruebas -d mercadex_pruebas -At -f database/anulacion-deudas/huella-tablas.sql > huella-despues.txt
fc huella-antes.txt huella-despues.txt     # no debe haber diferencias
dotnet run --project src/Mercadex.Api --urls http://127.0.0.1:5189
node tests/anulacion-deudas/sembrar-datos-prueba.mjs despues   # agrega una deuda anulada (B-04 septiembre)
node tests/anulacion-deudas/probar-anulacion.mjs               # pruebas de punta a punta
```

`probar-anulacion.mjs` crea sus propias casetas (bloque `Z…`) y no toca los datos del paso 3.

## 5. Panel y escritorio

- Panel: `cd mercadex-panel; npm ci; npm run dev` → http://localhost:5173 → usuario `admin`,
  contraseña `Admin-Local-2026` → **Deudas**.
- Escritorio: compilar con `$env:MERCADEX_APP_KEY = "clave-app-solo-local"` y abrir con
  `$env:MERCADEX_API_URL = "http://127.0.0.1:5189/"` → Obligaciones → Deudas registradas.
