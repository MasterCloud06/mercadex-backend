using Mercadex.Domain.Organizacion;
using Mercadex.Domain.Socios;
using Xunit;

namespace Mercadex.Domain.Tests.Organizacion;

public sealed class PlanoYNoSociosTests
{
    [Fact]
    public void La_caseta_guarda_su_lugar_en_el_plano()
    {
        var u = new UnidadComercial("125", TipoUnidadComercial.Caseta);
        u.UbicarEnPlano(940, 320);

        Assert.Equal(940, u.PlanoX);
        Assert.Equal(320, u.PlanoY);

        u.UbicarEnPlano(null, null);
        Assert.Null(u.PlanoX);
    }

    [Fact]
    public void Un_comerciante_puede_no_ser_socio_y_el_lugar_de_expedicion_es_opcional()
    {
        var s = new Socio("María", "Quispe", "1234567", null);
        Assert.True(s.EsSocio);
        Assert.Null(s.LugarExpedicion);

        s.DefinirTipo(esSocio: false);
        s.CambiarLugarExpedicion(" lp ");

        Assert.False(s.EsSocio);
        Assert.Equal("LP", s.LugarExpedicion);
    }
}
