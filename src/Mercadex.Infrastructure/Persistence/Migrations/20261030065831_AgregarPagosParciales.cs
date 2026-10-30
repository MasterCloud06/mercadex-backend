using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mercadex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPagosParciales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MontoPagado",
                table: "obligaciones",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No se borra la columna: guarda lo abonado con pagos parciales.
            // La versión anterior de la API la ignora, así que para volver atrás
            // basta con desplegar esa versión sin revertir la base.
            throw new NotSupportedException(
                "AgregarPagosParciales no se revierte: conserva lo abonado con pagos parciales. " +
                "Para volver atrás despliega la versión anterior de la API sin revertir la base.");
        }
    }
}
