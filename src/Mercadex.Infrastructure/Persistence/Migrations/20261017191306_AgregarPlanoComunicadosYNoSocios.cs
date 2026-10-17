using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mercadex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPlanoComunicadosYNoSocios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlanoX",
                table: "unidades_comerciales",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanoY",
                table: "unidades_comerciales",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LugarExpedicion",
                table: "socios",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SinAfiliacion",
                table: "socios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "comunicados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Texto = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    FechaUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Autor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    MostrarEnApp = table.Column<bool>(type: "boolean", nullable: false),
                    VigenteHasta = table.Column<DateOnly>(type: "date", nullable: true),
                    Archivado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comunicados", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_comunicados_FechaUtc",
                table: "comunicados",
                column: "FechaUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comunicados");

            migrationBuilder.DropColumn(
                name: "PlanoX",
                table: "unidades_comerciales");

            migrationBuilder.DropColumn(
                name: "PlanoY",
                table: "unidades_comerciales");

            migrationBuilder.DropColumn(
                name: "LugarExpedicion",
                table: "socios");

            migrationBuilder.DropColumn(
                name: "SinAfiliacion",
                table: "socios");
        }
    }
}
