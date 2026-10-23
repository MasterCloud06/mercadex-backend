using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mercadex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarObservacionesRecibo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Observaciones",
                table: "recibos",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No se borra la columna: guarda las observaciones de los recibos ya
            // emitidos. La versión anterior de la API la ignora, así que para volver
            // atrás basta con desplegar esa versión sin revertir la base.
            throw new NotSupportedException(
                "AgregarObservacionesRecibo no se revierte: conserva las observaciones de los recibos. " +
                "Para volver atrás despliega la versión anterior de la API sin revertir la base.");
        }
    }
}
