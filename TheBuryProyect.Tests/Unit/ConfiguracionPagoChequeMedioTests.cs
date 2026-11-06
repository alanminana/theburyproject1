using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// H-15: el medio Cheque solo lo crea/modifica un SuperAdmin y nace siempre inactivo (decisión del propietario:
/// «inicialmente deshabilitado»); el resto de los medios no cambia.
/// </summary>
public class ConfiguracionPagoChequeMedioTests
{
    public class Servicio : DispatchProxy
    {
        public List<ConfiguracionPagoViewModel> Creados { get; } = new();
        public ConfiguracionPagoViewModel? Existente { get; set; }

        protected override object? Invoke(MethodInfo? m, object?[]? args)
        {
            switch (m!.Name)
            {
                case "CreateAsync":
                    var vm = (ConfiguracionPagoViewModel)args![0]!;
                    Creados.Add(vm);
                    return Task.FromResult(new ConfiguracionPagoViewModel { Id = 77, TipoPago = vm.TipoPago, Activo = vm.Activo });
                case "GetByIdAsync":
                    return Task.FromResult(Existente);
                default:
                    throw new NotSupportedException(m.Name);
            }
        }
    }

    public class Admin : DispatchProxy
    {
        public int Ediciones { get; set; }
        public List<string> Llamadas { get; } = new();
        protected override object? Invoke(MethodInfo? m, object?[]? args)
        {
            if (m!.Name == "EditarMedioPagoAsync") { Ediciones++; return Task.FromResult(true); }
            if (m.Name == "ObtenerAdminGlobalAsync")
            {
                var cheque = new MedioPagoGlobalAdminViewModel { Id = 9, TipoPago = TipoPago.Cheque };
                cheque.Planes.Add(new PlanPagoGlobalAdminViewModel { Id = 5 });
                cheque.Tarjetas.Add(new TarjetaGlobalAdminViewModel { Id = 6 });
                var efectivo = new MedioPagoGlobalAdminViewModel { Id = 3, TipoPago = TipoPago.Efectivo };
                efectivo.Planes.Add(new PlanPagoGlobalAdminViewModel { Id = 15 });
                return Task.FromResult(new ConfiguracionPagoGlobalAdminViewModel { Medios = { cheque, efectivo } });
            }
            Llamadas.Add(m.Name);
            if (m.Name is "EliminarMedioPagoAsync" or "CambiarEstadoPlanGlobalAsync" or "EliminarTarjetaGlobalAsync" or "CambiarEstadoTarjetaGlobalAsync")
                return Task.FromResult(true);
            throw new NotSupportedException(m.Name);
        }
    }

    private static (ConfiguracionPagoController c, Servicio s, Admin a) Crear(string rol)
    {
        var s = DispatchProxy.Create<IConfiguracionPagoService, Servicio>();
        var a = DispatchProxy.Create<IConfiguracionPagoGlobalAdminService, Admin>();
        var c = (ConfiguracionPagoController)RuntimeHelpers.GetUninitializedObject(typeof(ConfiguracionPagoController));
        void Set(string campo, object? valor) =>
            typeof(ConfiguracionPagoController).GetField(campo, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(c, valor);
        Set("_configuracionPagoService", s);
        Set("_configuracionPagoGlobalAdminService", a);
        Set("_logger", NullLogger<ConfiguracionPagoController>.Instance);
        var identidad = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "u"), new Claim(ClaimTypes.Role, rol) }, "test");
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identidad) };
        c.ControllerContext = new ControllerContext { HttpContext = http };
        c.TempData = new TempDataDictionary(http, new NullTempData());
        return (c, (Servicio)(object)s, (Admin)(object)a);
    }

    private sealed class NullTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static MedioPagoGlobalCommandViewModel Cmd(TipoPago tipo, bool activo = true) =>
        new() { TipoPago = tipo, Nombre = "Medio", Activo = activo };

    [Fact]
    public async Task Crear_Cheque_ComoAdministrador_SeRechazaYNoCreaNada()
    {
        var (c, s, _) = Crear(Roles.Administrador);

        await c.CrearMedioPagoGlobal(Cmd(TipoPago.Cheque));

        Assert.Empty(s.Creados);
        Assert.Contains("SuperAdmin", c.TempData["Error"]?.ToString());
    }

    [Fact]
    public async Task Crear_Cheque_ComoSuperAdmin_NaceInactivoAunqueSePidaActivo()
    {
        var (c, s, _) = Crear(Roles.SuperAdmin);

        await c.CrearMedioPagoGlobal(Cmd(TipoPago.Cheque, activo: true));

        var creado = Assert.Single(s.Creados);
        Assert.Equal(TipoPago.Cheque, creado.TipoPago);
        Assert.False(creado.Activo);
    }

    [Fact]
    public async Task Crear_OtroMedio_ComoAdministrador_NoCambia()
    {
        var (c, s, _) = Crear(Roles.Administrador);

        await c.CrearMedioPagoGlobal(Cmd(TipoPago.Transferencia, activo: true));

        var creado = Assert.Single(s.Creados);
        Assert.True(creado.Activo);
    }

    [Fact]
    public async Task Editar_Cheque_ComoAdministrador_SeRechaza_ComoSuperAdminSeAplica()
    {
        var edit = new MedioPagoGlobalEditViewModel { Nombre = "Cheque", Activo = true };

        var (cAdm, sAdm, aAdm) = Crear(Roles.Administrador);
        sAdm.Existente = new ConfiguracionPagoViewModel { Id = 9, TipoPago = TipoPago.Cheque };
        await cAdm.EditarMedioPagoGlobal(9, edit);
        Assert.Equal(0, aAdm.Ediciones);

        var (cSa, sSa, aSa) = Crear(Roles.SuperAdmin);
        sSa.Existente = new ConfiguracionPagoViewModel { Id = 9, TipoPago = TipoPago.Cheque };
        await cSa.EditarMedioPagoGlobal(9, edit);
        Assert.Equal(1, aSa.Ediciones);
    }

    [Fact]
    public async Task Editar_OtroMedio_ComoAdministrador_SeAplica()
    {
        var (c, s, a) = Crear(Roles.Administrador);
        s.Existente = new ConfiguracionPagoViewModel { Id = 3, TipoPago = TipoPago.Efectivo };

        await c.EditarMedioPagoGlobal(3, new MedioPagoGlobalEditViewModel { Nombre = "Efectivo", Activo = true });

        Assert.Equal(1, a.Ediciones);
    }
    [Fact]
    public async Task Eliminar_Cambiar_Cheque_ComoAdministrador_SeRechaza_ComoSuperAdminSeAplica()
    {
        var (cAdm, _, aAdm) = Crear(Roles.Administrador);
        await cAdm.EliminarMedioPagoGlobal(9);
        await cAdm.CambiarEstadoPlanGlobal(5, true, 9);
        await cAdm.EliminarTarjetaGlobal(6, 9);
        await cAdm.CambiarEstadoTarjetaGlobal(6, true, 9);
        Assert.Empty(aAdm.Llamadas);
        Assert.Contains("SuperAdmin", cAdm.TempData["Error"]?.ToString());

        var (cSa, _, aSa) = Crear(Roles.SuperAdmin);
        await cSa.EliminarMedioPagoGlobal(9);
        await cSa.CambiarEstadoPlanGlobal(5, true, 9);
        Assert.Equal(new[] { "EliminarMedioPagoAsync", "CambiarEstadoPlanGlobalAsync" }, aSa.Llamadas);
    }

    [Fact]
    public async Task Eliminar_Cambiar_OtroMedio_ComoAdministrador_SeAplica()
    {
        var (c, _, a) = Crear(Roles.Administrador);
        await c.EliminarMedioPagoGlobal(3);
        await c.CambiarEstadoPlanGlobal(15, true, 3);
        Assert.Equal(new[] { "EliminarMedioPagoAsync", "CambiarEstadoPlanGlobalAsync" }, a.Llamadas);
    }
}
