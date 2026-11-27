using System.Security.Claims;

namespace TheBuryProject.Helpers;

/// <summary>Destino MVC de una entrada de navegación. <see cref="Tab"/> es la pestaña interna de Inventario (?tab=).</summary>
public sealed record DestinoNav(string Controlador, string Accion = "Index", string? Tab = null);

/// <summary>Pestaña de un módulo, ya resuelta para un usuario.</summary>
public sealed record PestanaVisible(string Clave, string Titulo, string Icono, DestinoNav Destino, bool Activa);

/// <summary>Entrada del menú lateral, ya resuelta para un usuario.</summary>
public sealed record ModuloVisible(string Clave, string Titulo, string Icono, string Grupo, DestinoNav Destino, bool Activo);

/// <summary>Barra de pestañas de la pantalla actual (y sus sub-pestañas, si la pestaña activa las tiene).</summary>
public sealed record BarraModulo(
    string ModuloClave,
    string ModuloTitulo,
    IReadOnlyList<PestanaVisible> Pestanas,
    IReadOnlyList<PestanaVisible> SubPestanas);

/// <summary>
/// Única definición de la navegación por módulos: la usan el menú lateral y las barras de pestañas.
/// Solo decide qué se MUESTRA, replicando los permisos que ya exige cada controller; la autorización real
/// sigue en el servidor ([PermisoRequerido]). Regla: un módulo aparece si el usuario puede ver al menos una
/// de sus pestañas, y su enlace abre la primera permitida (nadie pierde el acceso por agrupar pantallas).
/// </summary>
public static class NavegacionModulos
{
    private sealed record Pestana(
        string Clave,
        string Titulo,
        string Icono,
        Func<ClaimsPrincipal, bool> Permitida,
        Func<ClaimsPrincipal, DestinoNav> Destino,
        string[] Paginas,
        Pestana[]? SubPestanas = null);

    private sealed record Modulo(
        string Clave,
        string Titulo,
        string Icono,
        string Grupo,
        string[] Controladores,
        Pestana[] Pestanas);

    private static Func<ClaimsPrincipal, bool> Permiso(string modulo, string accion) =>
        u => u.TienePermiso(modulo, accion);

    private static Pestana Fija(string clave, string titulo, string icono, Func<ClaimsPrincipal, bool> permitida,
        DestinoNav destino, string[]? paginas = null, Pestana[]? sub = null) =>
        new(clave, titulo, icono, permitida, _ => destino, paginas ?? [], sub);

    /// <summary>
    /// Pestaña de Inventario: dentro del catálogo es un panel de la misma pantalla. Quien no puede ver el
    /// catálogo (productos.view) conserva la pantalla completa propia de la función, si existe.
    /// </summary>
    private static Pestana DelCatalogo(string clave, string titulo, string icono, string tab,
        Func<ClaimsPrincipal, bool> permiso, string? controladorPropio = null) =>
        new(clave, titulo, icono,
            u => controladorPropio is null ? u.TienePermiso("productos", "view") && permiso(u) : permiso(u),
            u => u.TienePermiso("productos", "view")
                ? new DestinoNav("Catalogo", "Index", tab)
                : new DestinoNav(controladorPropio!),
            controladorPropio is null ? [] : [$"{controladorPropio}/Index"]);

    private static bool VeMora(ClaimsPrincipal u) =>
        u.TienePermiso("cobranzas", "viewarrears") && u.TienePermiso("mora", "view");

    private static readonly Modulo[] Modulos =
    [
        new("ventas", "Ventas", "shopping_bag", "Operaciones",
            ["Venta", "Cotizacion", "Devolucion", "ContratoVentaCredito"],
            [Fija("ventas", "Ventas", "shopping_bag", Permiso("ventas", "view"), new("Venta"))]),

        new("clientes", "Clientes", "group", "Operaciones",
            ["Cliente", "ClienteReferencia", "Credito", "Mora", "DocumentoCliente"],
            [
                Fija("clientes", "Clientes", "group", Permiso("clientes", "view"), new("Cliente"), ["Cliente/Index"]),
                Fija("creditos", "Créditos", "credit_score", Permiso("creditos", "view"), new("Credito"), ["Credito/Index"]),
                Fija("mora", "Cobranza y mora", "running_with_errors", VeMora, new("Mora"),
                    ["Mora/Index", "Mora/ClientesMora", "Mora/Alertas", "Mora/Logs"],
                    [
                        Fija("mora-panel", "Panel", "monitoring", VeMora, new("Mora"), ["Mora/Index"]),
                        Fija("mora-clientes", "Clientes en mora", "groups", VeMora, new("Mora", "ClientesMora"), ["Mora/ClientesMora"]),
                        Fija("mora-alertas", "Alertas", "notifications_active", VeMora, new("Mora", "Alertas"), ["Mora/Alertas"]),
                        Fija("mora-registro", "Registro de procesos", "history", VeMora, new("Mora", "Logs"), ["Mora/Logs"])
                    ]),
                Fija("documentacion", "Documentación", "folder_shared", Permiso("clientes", "viewdocs"),
                    new("DocumentoCliente"), ["DocumentoCliente/Index"])
            ]),

        new("cajas", "Cajas", "point_of_sale", "Operaciones", ["Caja"],
            [Fija("cajas", "Cajas", "point_of_sale", Permiso("caja", "view"), new("Caja"))]),

        new("documentos", "Documentos", "description", "Operaciones", ["Documento"],
            [Fija("documentos", "Documentos", "description", Permiso("documentos", "view"), new("Documento"))]),

        new("inventario", "Inventario", "inventory_2", "Gestión",
            ["Catalogo", "Producto", "Categoria", "Marca", "MovimientoStock", "AlertaStock", "ListaPrecio", "CambiosPrecios"],
            [
                DelCatalogo("productos", "Productos", "grid_view", "productos", _ => true),
                DelCatalogo("categorias", "Categorías", "category", "categorias", _ => true),
                DelCatalogo("marcas", "Marcas", "sell", "marcas", _ => true),
                DelCatalogo("alertas", "Alertas", "notifications", "alertas", Permiso("stock", "viewalerts"), "AlertaStock"),
                DelCatalogo("movimientos", "Movimientos", "sync_alt", "movimientos", Permiso("movimientos", "view"), "MovimientoStock"),
                Fija("precios", "Precios", "price_change", Permiso("precios", "view"), new("ListaPrecio"),
                    ["ListaPrecio/Index", "CambiosPrecios/Index"],
                    [
                        Fija("precios-listas", "Listas de precios", "sell", Permiso("precios", "view"), new("ListaPrecio"), ["ListaPrecio/Index"]),
                        Fija("precios-lotes", "Cambios por lotes", "price_change", Permiso("precios", "view"), new("CambiosPrecios"), ["CambiosPrecios/Index"])
                    ])
            ]),

        new("proveedores", "Proveedores", "local_shipping", "Gestión",
            ["Proveedor", "OrdenCompra", "CuentaPorPagar", "Tesoreria"],
            [
                Fija("proveedores", "Proveedores", "local_shipping", Permiso("proveedores", "view"), new("Proveedor"), ["Proveedor/Index"]),
                Fija("ordenes", "Órdenes de compra", "shopping_cart", Permiso("ordenescompra", "view"), new("OrdenCompra"), ["OrdenCompra/Index"]),
                Fija("cxp", "Cuentas por pagar", "account_balance_wallet", Permiso("cuentasporpagar", "view"), new("CuentaPorPagar"), ["CuentaPorPagar/Index"]),
                Fija("tesoreria", "Tesorería", "account_balance", Permiso("tesoreria", "view"), new("Tesoreria"), ["Tesoreria/Index"])
            ]),

        new("reportes", "Reportes", "leaderboard", "Análisis", ["Reporte", "ValoracionStock"],
            [
                Fija("reportes", "Reportes", "leaderboard", Permiso("reportes", "view"), new("Reporte")),
                Fija("valoracion", "Valoración de stock", "monetization_on", Permiso("valoracionstock", "view"), new("ValoracionStock"))
            ]),

        new("mercadolibre", "Mercado Libre", "storefront", "Integraciones", ["MercadoLibre"],
            [Fija("mercadolibre", "Mercado Libre", "storefront", Permiso("mercadolibre", "view"), new("MercadoLibre", "Dashboard"))])
    ];

    private static bool Es(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<ModuloVisible> Menu(ClaimsPrincipal user, string? controlador)
    {
        var menu = new List<ModuloVisible>();
        foreach (var modulo in Modulos)
        {
            var primera = modulo.Pestanas.FirstOrDefault(p => p.Permitida(user));
            if (primera is null) continue;

            menu.Add(new ModuloVisible(modulo.Clave, modulo.Titulo, modulo.Icono, modulo.Grupo,
                primera.Destino(user), modulo.Controladores.Any(c => Es(controlador, c))));
        }
        return menu;
    }

    /// <summary>
    /// Barra de pestañas de una pantalla principal de módulo; null en el resto (formularios y detalles usan miga),
    /// o cuando el usuario solo tendría una opción (no se muestra una barra de una sola pestaña).
    /// El catálogo (Catalogo/Index) tiene sus propias pestañas en la misma tarjeta y no usa esta barra.
    /// </summary>
    public static BarraModulo? Barra(ClaimsPrincipal user, string? controlador, string? accion)
    {
        var pagina = $"{controlador}/{accion}";
        foreach (var modulo in Modulos)
        {
            var actual = modulo.Pestanas.FirstOrDefault(p => p.Paginas.Any(x => Es(pagina, x)));
            if (actual is null) continue;

            var pestanas = Visibles(modulo.Pestanas, user, pagina);
            var sub = actual.SubPestanas is null ? [] : Visibles(actual.SubPestanas, user, pagina);
            if (pestanas.Count < 2 && sub.Count < 2) return null;

            return new BarraModulo(modulo.Clave, modulo.Titulo,
                pestanas.Count < 2 ? [] : pestanas,
                sub.Count < 2 ? [] : sub);
        }
        return null;
    }

    private static IReadOnlyList<PestanaVisible> Visibles(Pestana[] pestanas, ClaimsPrincipal user, string pagina) =>
        pestanas.Where(p => p.Permitida(user))
            .Select(p => new PestanaVisible(p.Clave, p.Titulo, p.Icono, p.Destino(user),
                p.Paginas.Any(x => Es(pagina, x))))
            .ToList();
}
