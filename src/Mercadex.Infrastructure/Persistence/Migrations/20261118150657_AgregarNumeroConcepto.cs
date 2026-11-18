using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mercadex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarNumeroConcepto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Numero",
                table: "conceptos_cobro",
                type: "integer",
                nullable: true);

            // Número para los reportes: 1, 2, 3… en el orden en que se crearon
            // los conceptos (los Id son UUIDv7, ordenados por fecha). Solo llena
            // la columna nueva; nada más cambia. Después se puede editar.
            migrationBuilder.Sql("""
                UPDATE conceptos_cobro AS c
                SET "Numero" = n.numero
                FROM (SELECT "Id", row_number() OVER (ORDER BY "Id") AS numero FROM conceptos_cobro) AS n
                WHERE c."Id" = n."Id" AND c."Numero" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_conceptos_cobro_numero",
                table: "conceptos_cobro",
                column: "Numero",
                unique: true,
                filter: "\"Numero\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No se borra: guarda los números que la asociación haya asignado.
            // La versión anterior de la API la ignora.
            throw new NotSupportedException(
                "AgregarNumeroConcepto no se revierte: conserva los números de los conceptos. " +
                "Para volver atrás despliega la versión anterior de la API sin revertir la base.");
        }
    }
}
