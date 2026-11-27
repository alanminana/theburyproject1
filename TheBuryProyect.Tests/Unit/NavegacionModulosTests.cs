using System.Security.Claims;
using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Reorganización de 12 a 8 entradas: agrupar pantallas no puede quitar ni inventar accesos. Cada permiso que antes
/// tenía una entrada propia en el menú debe seguir teniendo un camino, sin ofrecer destinos que el usuario no puede abrir.
/// </summary>
public class NavegacionModulosTests
{
    private static ClaimsPrincipal Con(params string[] permisos) =>
        new(new ClaimsIdentity(permisos.Select(p => new Claim("Permission", p)), "test"));

    private static string Ruta(DestinoNav d) => $"{d.Controlador}/{d.Accion}" + (d.Tab is null ? "" : $"?tab={d.Tab}");

    [Fact]
    public void ElMenuCompleto_TieneLasOchoEntradasAprobadas_EnOrden()
    {
        var superAdmin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "SuperAdmin")], "test"));

        var titulos = NavegacionModulos.Menu(superAdmin, null).Select(m => m.Titulo);

        Assert.Equal(["Ventas", "Clientes", "Cajas", "Documentos", "Inventario", "Proveedores", "Reportes", "Mercado Libre"], titulos);
    }

    [Fact]
    public void SinPermisos_NoHayEntradas() => Assert.Empty(NavegacionModulos.Menu(Con(), null));

    // Un solo permiso: la entrada del módulo abre directamente la pantalla que ese permiso habilita.
    [Theory]
    [InlineData("ventas.view", "Ventas", "Venta/Index")]
    [InlineData("clientes.view", "Clientes", "Cliente/Index")]
    [InlineData("creditos.view", "Clientes", "Credito/Index")]
    [InlineData("clientes.viewdocs", "Clientes", "DocumentoCliente/Index")]
    [InlineData("caja.view", "Cajas", "Caja/Index")]
    [InlineData("documentos.view", "Documentos", "Documento/Index")]
    [InlineData("productos.view", "Inventario", "Catalogo/Index?tab=productos")]
    [InlineData("precios.view", "Inventario", "ListaPrecio/Index")]
    [InlineData("stock.viewalerts", "Inventario", "AlertaStock/Index")]
    [InlineData("movimientos.view", "Inventario", "MovimientoStock/Index")]
    [InlineData("proveedores.view", "Proveedores", "Proveedor/Index")]
    [InlineData("ordenescompra.view", "Proveedores", "OrdenCompra/Index")]
    [InlineData("cuentasporpagar.view", "Proveedores", "CuentaPorPagar/Index")]
    [InlineData("tesoreria.view", "Proveedores", "Tesoreria/Index")]
    [InlineData("reportes.view", "Reportes", "Reporte/Index")]
    [InlineData("valoracionstock.view", "Reportes", "ValoracionStock/Index")]
    [InlineData("mercadolibre.view", "Mercado Libre", "MercadoLibre/Dashboard")]
    public void UnSoloPermiso_ConservaSuAcceso(string permiso, string modulo, string destino)
    {
        var entrada = Assert.Single(NavegacionModulos.Menu(Con(permiso), null));

        Assert.Equal(modulo, entrada.Titulo);
        Assert.Equal(destino, Ruta(entrada.Destino));
    }

    // MoraController exige cobranzas.viewarrears y cada acción mora.view: con uno solo no se ofrece.
    [Fact]
    public void Mora_ExigeLosDosPermisosDelController()
    {
        Assert.Empty(NavegacionModulos.Menu(Con("mora.view"), null));
        Assert.Empty(NavegacionModulos.Menu(Con("cobranzas.viewarrears"), null));

        var entrada = Assert.Single(NavegacionModulos.Menu(Con("mora.view", "cobranzas.viewarrears"), null));
        Assert.Equal("Mora/Index", Ruta(entrada.Destino));
    }

    [Fact]
    public void LaBarra_MuestraSoloLasPestanasPermitidas()
    {
        var barra = NavegacionModulos.Barra(Con("tesoreria.view", "cuentasporpagar.view"), "Tesoreria", "Index");

        Assert.NotNull(barra);
        Assert.Equal(["Cuentas por pagar", "Tesorería"], barra!.Pestanas.Select(p => p.Titulo));
        Assert.Equal("Tesorería", Assert.Single(barra.Pestanas, p => p.Activa).Titulo);
    }

    [Fact]
    public void ConUnaSolaPestana_NoHayBarra() =>
        Assert.Null(NavegacionModulos.Barra(Con("tesoreria.view"), "Tesoreria", "Index"));

    [Fact]
    public void CajeroDeCobranza_LlegaALasVistasDeMora()
    {
        var barra = NavegacionModulos.Barra(Con("clientes.view", "creditos.view", "mora.view", "cobranzas.viewarrears"), "Mora", "ClientesMora");

        Assert.Equal(["Clientes", "Créditos", "Cobranza y mora"], barra!.Pestanas.Select(p => p.Titulo));
        Assert.Equal(["Panel", "Clientes en mora", "Alertas", "Registro de procesos"], barra.SubPestanas.Select(p => p.Titulo));
        Assert.Equal("Clientes en mora", Assert.Single(barra.SubPestanas, p => p.Activa).Titulo);
    }

    // Quien no ve el catálogo conserva la pantalla completa de alertas y movimientos, sin pestañas que darían 403.
    [Fact]
    public void SinCatalogo_LasPestanasDeInventarioUsanSuPantallaPropia()
    {
        var barra = NavegacionModulos.Barra(Con("stock.viewalerts", "movimientos.view", "precios.view"), "ListaPrecio", "Index");

        Assert.Equal(["AlertaStock/Index", "MovimientoStock/Index", "ListaPrecio/Index"], barra!.Pestanas.Select(p => Ruta(p.Destino)));
    }

    [Fact]
    public void Precios_TieneListasYCambiosPorLotes()
    {
        var barra = NavegacionModulos.Barra(Con("precios.view"), "CambiosPrecios", "Index");

        Assert.Empty(barra!.Pestanas);
        Assert.Equal(["Listas de precios", "Cambios por lotes"], barra.SubPestanas.Select(p => p.Titulo));
    }

    // El catálogo tiene sus propias pestañas en la misma tarjeta; formularios y detalles usan miga.
    [Theory]
    [InlineData("Catalogo", "Index")]
    [InlineData("Proveedor", "Details")]
    [InlineData("CuentaPorPagar", "Pagar")]
    [InlineData("Venta", "Index")]
    public void EstasPantallas_NoLlevanBarraDeModulo(string controlador, string accion)
    {
        var superAdmin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "SuperAdmin")], "test"));

        Assert.Null(NavegacionModulos.Barra(superAdmin, controlador, accion));
    }

    [Theory]
    [InlineData("OrdenCompra", "proveedores")]
    [InlineData("MovimientoStock", "inventario")]
    [InlineData("Devolucion", "ventas")]
    [InlineData("ValoracionStock", "reportes")]
    [InlineData("Mora", "clientes")]
    public void ElMenu_ResaltaElModuloDeCadaPantalla(string controlador, string modulo)
    {
        var superAdmin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "SuperAdmin")], "test"));

        var activo = Assert.Single(NavegacionModulos.Menu(superAdmin, controlador), m => m.Activo);

        Assert.Equal(modulo, activo.Clave);
    }
}
