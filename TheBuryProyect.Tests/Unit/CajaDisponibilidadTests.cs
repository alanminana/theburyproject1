using TheBuryProject.Models.Entities;
using TheBuryProject.ViewModels;
using Xunit;

namespace TheBuryProject.Tests.Unit;

public class CajaDisponibilidadTests
{
    private static Caja C(int id, bool activa = true) => new() { Id = id, Activa = activa, Nombre = $"C{id}", Codigo = $"C{id}" };
    private static AperturaCaja A(int cajaId) => new() { CajaId = cajaId };

    [Fact]
    public void SinCajas_EsSinCajas()
    {
        var r = CajaDisponibilidad.Resolver(new List<Caja>(), new List<AperturaCaja>(), null, false);
        Assert.Equal(EstadoCajasSistema.SinCajas, r.Estado);
    }

    [Fact]
    public void SoloInactivasOConTurno_EsSinDisponibles()
    {
        var r = CajaDisponibilidad.Resolver(new[] { C(1, false), C(2) }, new[] { A(2) }, null, false);
        Assert.Equal(EstadoCajasSistema.SinDisponibles, r.Estado);
        Assert.Equal(0, r.CajasDisponibles);
        Assert.Equal(1, r.CajasConTurnoAbierto);
    }

    [Fact]
    public void ActivaSinTurno_EsDisponibleSinTurno()
    {
        var r = CajaDisponibilidad.Resolver(new[] { C(1), C(2) }, new[] { A(2) }, null, false);
        Assert.Equal(EstadoCajasSistema.DisponibleSinTurno, r.Estado);
        Assert.Equal(1, r.CajasDisponibles);
    }

    [Fact]
    public void UsuarioConTurno_EsConTurno()
    {
        var r = CajaDisponibilidad.Resolver(new[] { C(1) }, new[] { A(1) }, null, true);
        Assert.Equal(EstadoCajasSistema.ConTurno, r.Estado);
    }

    [Fact]
    public void Padron_FiltraCajasDisponiblesYVisibles()
    {
        var r = CajaDisponibilidad.Resolver(new[] { C(1), C(2) }, new List<AperturaCaja>(), new HashSet<int> { 2 }, false);
        Assert.Equal(1, r.TotalCajas);
        Assert.Equal(1, r.CajasDisponibles);

        var sinPadron = CajaDisponibilidad.Resolver(new[] { C(1) }, new List<AperturaCaja>(), new HashSet<int>(), false);
        Assert.Equal(EstadoCajasSistema.SinCajas, sinPadron.Estado);
    }
}
