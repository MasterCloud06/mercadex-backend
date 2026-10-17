using Mercadex.Domain.Comunicacion;
using Xunit;

namespace Mercadex.Domain.Tests.Comunicacion;

public sealed class ComunicadoTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 17);

    [Fact]
    public void Se_ve_en_la_app_hasta_su_fecha_de_vigencia()
    {
        var c = new Comunicado("Reunión general", "El sábado a las 10:00.", true, Hoy, "Directiva", DateTimeOffset.UtcNow);

        Assert.True(c.VisibleEnApp(Hoy));
        Assert.False(c.VisibleEnApp(Hoy.AddDays(1)));
    }

    [Fact]
    public void Archivado_o_solo_impreso_no_se_ve_en_la_app()
    {
        var impreso = new Comunicado("Aviso", "Texto del aviso.", false, null, null, DateTimeOffset.UtcNow);
        var archivado = new Comunicado("Aviso", "Texto del aviso.", true, null, null, DateTimeOffset.UtcNow);
        archivado.Archivar();

        Assert.False(impreso.VisibleEnApp(Hoy));
        Assert.False(archivado.VisibleEnApp(Hoy));
    }

    [Fact]
    public void Exige_titulo_y_texto()
    {
        Assert.Throws<ArgumentException>(() => new Comunicado(" ", "Texto", true, null, null, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new Comunicado("Título", "", true, null, null, DateTimeOffset.UtcNow));
    }
}
