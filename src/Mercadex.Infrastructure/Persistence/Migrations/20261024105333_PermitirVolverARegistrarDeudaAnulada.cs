using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mercadex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermitirVolverARegistrarDeudaAnulada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_obligaciones_unidad_concepto_periodo",
                table: "obligaciones");

            migrationBuilder.CreateIndex(
                name: "UX_obligaciones_unidad_concepto_periodo",
                table: "obligaciones",
                columns: new[] { "UnidadComercialId", "ConceptoCobroId", "PeriodoAnio", "PeriodoMes" },
                unique: true,
                filter: "\"PeriodoAnio\" IS NOT NULL AND \"PeriodoMes\" IS NOT NULL AND \"Estado\" <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_obligaciones_unidad_concepto_periodo",
                table: "obligaciones");

            migrationBuilder.CreateIndex(
                name: "UX_obligaciones_unidad_concepto_periodo",
                table: "obligaciones",
                columns: new[] { "UnidadComercialId", "ConceptoCobroId", "PeriodoAnio", "PeriodoMes" },
                unique: true,
                filter: "\"PeriodoAnio\" IS NOT NULL AND \"PeriodoMes\" IS NOT NULL");
        }
    }
}
