using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mercadex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarAnulacionDeDeudas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anulaciones_obligaciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaveOperacion = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Responsable = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ResponsableId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Filtros = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FechaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Cantidad = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anulaciones_obligaciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "detalles_anulacion_obligacion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnulacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObligacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnidadComercialId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConceptoCobroId = table.Column<Guid>(type: "uuid", nullable: false),
                    MonedaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PeriodoAnio = table.Column<int>(type: "integer", nullable: true),
                    PeriodoMes = table.Column<int>(type: "integer", nullable: true),
                    EstadoAnterior = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detalles_anulacion_obligacion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_detalles_anulacion_obligacion_anulaciones_obligaciones_Anul~",
                        column: x => x.AnulacionId,
                        principalTable: "anulaciones_obligaciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_detalles_anulacion_obligacion_conceptos_cobro_ConceptoCobro~",
                        column: x => x.ConceptoCobroId,
                        principalTable: "conceptos_cobro",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_detalles_anulacion_obligacion_monedas_MonedaId",
                        column: x => x.MonedaId,
                        principalTable: "monedas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_detalles_anulacion_obligacion_obligaciones_ObligacionId",
                        column: x => x.ObligacionId,
                        principalTable: "obligaciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_detalles_anulacion_obligacion_unidades_comerciales_UnidadCo~",
                        column: x => x.UnidadComercialId,
                        principalTable: "unidades_comerciales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_anulaciones_obligaciones_ClaveOperacion",
                table: "anulaciones_obligaciones",
                column: "ClaveOperacion",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_anulaciones_obligaciones_FechaUtc",
                table: "anulaciones_obligaciones",
                column: "FechaUtc");

            migrationBuilder.CreateIndex(
                name: "IX_detalles_anulacion_obligacion_AnulacionId",
                table: "detalles_anulacion_obligacion",
                column: "AnulacionId");

            migrationBuilder.CreateIndex(
                name: "IX_detalles_anulacion_obligacion_ConceptoCobroId",
                table: "detalles_anulacion_obligacion",
                column: "ConceptoCobroId");

            migrationBuilder.CreateIndex(
                name: "IX_detalles_anulacion_obligacion_MonedaId",
                table: "detalles_anulacion_obligacion",
                column: "MonedaId");

            migrationBuilder.CreateIndex(
                name: "IX_detalles_anulacion_obligacion_ObligacionId",
                table: "detalles_anulacion_obligacion",
                column: "ObligacionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_detalles_anulacion_obligacion_UnidadComercialId",
                table: "detalles_anulacion_obligacion",
                column: "UnidadComercialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Estas tablas guardan el historial de anulaciones (quién, cuándo,
            // por qué y qué deudas). No se borran al revertir: la versión
            // anterior de la API no las usa, así que para volver atrás basta
            // con desplegar esa versión y dejar las tablas como están.
            // Ver database/anulacion-deudas/README.md.
            throw new NotSupportedException(
                "AgregarAnulacionDeDeudas no se revierte: conserva el historial de anulaciones. " +
                "Para volver atrás despliega la versión anterior de la API sin revertir la base.");
        }
    }
}
