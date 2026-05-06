using AutoMapper;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Helpers
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // =======================
            // Categoria
            // =======================
            CreateMap<Categoria, CategoriaViewModel>()
                .ForMember(d => d.ParentNombre, o => o.MapFrom(s => s.Parent != null && !s.Parent.IsDeleted ? s.Parent.Nombre : null))
                .ForMember(d => d.AlicuotaIVANombre, o => o.MapFrom(s => s.AlicuotaIVA != null && !s.AlicuotaIVA.IsDeleted ? s.AlicuotaIVA.Nombre : null));

            // =======================
            // Marca
            // =======================
            CreateMap<Marca, MarcaViewModel>()
                .ForMember(d => d.ParentNombre, o => o.MapFrom(s => s.Parent != null && !s.Parent.IsDeleted ? s.Parent.Nombre : null));

            // =======================
            // Producto
            // =======================
            CreateMap<Producto, ProductoViewModel>()
                .ForMember(d => d.CategoriaNombre, o => o.MapFrom(s => s.Categoria != null ? s.Categoria.Nombre : null))
                .ForMember(d => d.MarcaNombre, o => o.MapFrom(s => s.Marca != null ? s.Marca.Nombre : null))
                .ForMember(d => d.AlicuotaIVANombre, o => o.MapFrom(s => s.AlicuotaIVA != null && !s.AlicuotaIVA.IsDeleted ? s.AlicuotaIVA.Nombre : null));

            CreateMap<ProductoViewModel, Producto>()
                .ForMember(d => d.Categoria, o => o.Ignore())
                .ForMember(d => d.AlicuotaIVA, o => o.Ignore())
                .ForMember(d => d.Marca, o => o.Ignore())
                .ForMember(d => d.CreatedAt, o => o.Ignore())
                .ForMember(d => d.CreatedBy, o => o.Ignore())
                .ForMember(d => d.IsDeleted, o => o.Ignore());

            CreateMap<ProductoCaracteristica, ProductoCaracteristicaViewModel>();

            CreateMap<ProductoCaracteristicaViewModel, ProductoCaracteristica>()
                .ForMember(d => d.Producto, o => o.Ignore())
                .ForMember(d => d.ProductoId, o => o.Ignore())
                .ForMember(d => d.CreatedAt, o => o.Ignore())
                .ForMember(d => d.CreatedBy, o => o.Ignore())
                .ForMember(d => d.UpdatedAt, o => o.Ignore())
                .ForMember(d => d.UpdatedBy, o => o.Ignore())
                .ForMember(d => d.IsDeleted, o => o.Ignore())
                .ForMember(d => d.RowVersion, o => o.Ignore());

            // =======================
            // Proveedor
            // =======================
            CreateMap<Proveedor, ProveedorViewModel>()
                .ForMember(d => d.TotalOrdenesCompra, o => o.MapFrom(s => s.OrdenesCompra != null ? s.OrdenesCompra.Count(oc => !oc.IsDeleted) : 0))
                .ForMember(d => d.ChequesVigentes, o => o.MapFrom(s => s.Cheques != null ? s.Cheques.Count(c =>
                    !c.IsDeleted &&
                    c.Estado != EstadoCheque.Cobrado &&
                    c.Estado != EstadoCheque.Rechazado &&
                    c.Estado != EstadoCheque.Anulado) : 0))
                .ForMember(d => d.TotalDeuda, o => o.MapFrom(s => s.OrdenesCompra != null ? s.OrdenesCompra
                    .Where(oc => !oc.IsDeleted && oc.Estado != EstadoOrdenCompra.Cancelada)
                    .Sum(oc => oc.Total) : 0))
                .ForMember(d => d.ProductosSeleccionados, o => o.MapFrom(s => s.ProveedorProductos != null ? s.ProveedorProductos
                    .Where(pp => !pp.IsDeleted)
                    .Select(pp => pp.ProductoId) : Enumerable.Empty<int>()))
                .ForMember(d => d.MarcasSeleccionadas, o => o.MapFrom(s => s.ProveedorMarcas != null ? s.ProveedorMarcas
                    .Where(pm => !pm.IsDeleted)
                    .Select(pm => pm.MarcaId) : Enumerable.Empty<int>()))
                .ForMember(d => d.CategoriasSeleccionadas, o => o.MapFrom(s => s.ProveedorCategorias != null ? s.ProveedorCategorias
                    .Where(pc => !pc.IsDeleted)
                    .Select(pc => pc.CategoriaId) : Enumerable.Empty<int>()))
                .ForMember(d => d.ProductosAsociados, o => o.MapFrom(s => s.ProveedorProductos != null ? s.ProveedorProductos
                    .Where(pp => !pp.IsDeleted && pp.Producto != null && !pp.Producto.IsDeleted)
                    .Select(pp => pp.Producto!.Nombre) : Enumerable.Empty<string>()))
                .ForMember(d => d.MarcasAsociadas, o => o.MapFrom(s => s.ProveedorMarcas != null ? s.ProveedorMarcas
                    .Where(pm => !pm.IsDeleted && pm.Marca != null && !pm.Marca.IsDeleted)
                    .Select(pm => pm.Marca!.Nombre) : Enumerable.Empty<string>()))
                .ForMember(d => d.CategoriasAsociadas, o => o.MapFrom(s => s.ProveedorCategorias != null ? s.ProveedorCategorias
                    .Where(pc => !pc.IsDeleted && pc.Categoria != null && !pc.Categoria.IsDeleted)
                    .Select(pc => pc.Categoria!.Nombre) : Enumerable.Empty<string>()));

            CreateMap<ProveedorViewModel, Proveedor>()
                .ForMember(d => d.ProveedorProductos, o => o.MapFrom(s => (s.ProductosSeleccionados ?? new List<int>())
                    .Select(id => new ProveedorProducto { ProductoId = id })))
                .ForMember(d => d.ProveedorMarcas, o => o.MapFrom(s => (s.MarcasSeleccionadas ?? new List<int>())
                    .Select(id => new ProveedorMarca { MarcaId = id })))
                .ForMember(d => d.ProveedorCategorias, o => o.MapFrom(s => (s.CategoriasSeleccionadas ?? new List<int>())
                    .Select(id => new ProveedorCategoria { CategoriaId = id })))
                .ForMember(d => d.OrdenesCompra, o => o.Ignore())
                .ForMember(d => d.Cheques, o => o.Ignore());

            // =======================
            // OrdenCompra
            // =======================
            CreateMap<OrdenCompra, OrdenCompraViewModel>()
                .ForMember(d => d.ProveedorNombre, o => o.MapFrom(s => s.Proveedor != null ? s.Proveedor.RazonSocial : null))
                .ForMember(d => d.EstadoNombre, o => o.MapFrom(s => s.Estado.ToString()))
                .ForMember(d => d.TotalItems, o => o.MapFrom(s => s.Detalles != null ? s.Detalles.Where(d => !d.IsDeleted).Sum(d => d.Cantidad) : 0))
                .ForMember(d => d.TotalRecibido, o => o.MapFrom(s => s.Detalles != null ? s.Detalles.Where(d => !d.IsDeleted).Sum(d => d.CantidadRecibida) : 0))
                .ForMember(d => d.Detalles, o => o.MapFrom(s => s.Detalles != null ? s.Detalles.Where(d => !d.IsDeleted) : Enumerable.Empty<OrdenCompraDetalle>()));

            CreateMap<OrdenCompraViewModel, OrdenCompra>()
                .ForMember(d => d.Proveedor, o => o.Ignore())
                .ForMember(d => d.Detalles, o => o.MapFrom(s => s.Detalles));

            // =======================
            // OrdenCompraDetalle
            // =======================
            CreateMap<OrdenCompraDetalle, OrdenCompraDetalleViewModel>()
                .ForMember(d => d.ProductoNombre, o => o.MapFrom(s => s.Producto != null ? s.Producto.Nombre : null))
                .ForMember(d => d.ProductoCodigo, o => o.MapFrom(s => s.Producto != null ? s.Producto.Codigo : null));

            CreateMap<OrdenCompraDetalleViewModel, OrdenCompraDetalle>()
                .ForMember(d => d.Producto, o => o.Ignore())
                .ForMember(d => d.OrdenCompra, o => o.Ignore());

            // =======================
            // Cheques
            // =======================
            CreateMap<Cheque, ChequeViewModel>()
                .ForMember(d => d.ProveedorNombre, o => o.MapFrom(s => s.Proveedor != null ? s.Proveedor.RazonSocial : null))
                .ForMember(d => d.OrdenCompraNumero, o => o.MapFrom(s => s.OrdenCompra != null ? s.OrdenCompra.Numero : null))
                .ForMember(d => d.EstadoNombre, o => o.MapFrom(s => s.Estado.ToString()))
                .ForMember(d => d.DiasPorVencer, o => o.MapFrom(s =>
                    s.FechaVencimiento.HasValue
                        ? (int)(s.FechaVencimiento.Value - DateTime.Today).TotalDays
                        : 0));

            CreateMap<ChequeViewModel, Cheque>()
                .ForMember(d => d.Proveedor, o => o.Ignore())
                .ForMember(d => d.OrdenCompra, o => o.Ignore());

            // Mappings para MovimientoStock
            CreateMap<MovimientoStock, MovimientoStockViewModel>()
                .ForMember(d => d.ProductoNombre, o => o.MapFrom(s => s.Producto != null ? s.Producto.Nombre : null))
                .ForMember(d => d.ProductoCodigo, o => o.MapFrom(s => s.Producto != null ? s.Producto.Codigo : null))
                .ForMember(d => d.TipoNombre, o => o.MapFrom(s => s.Tipo.ToString()))
                .ForMember(d => d.OrdenCompraNumero, o => o.MapFrom(s => s.OrdenCompra != null ? s.OrdenCompra.Numero : null))
                .ForMember(d => d.Fecha, o => o.MapFrom(s => s.CreatedAt));

            // =======================
            // Cliente
            // =======================
            CreateMap<Cliente, ClienteResumenViewModel>()
                .ForMember(d => d.NombreCompleto, o => o.MapFrom(s =>
                    !string.IsNullOrWhiteSpace(s.NombreCompleto)
                        ? s.NombreCompleto
                        : s.ToDisplayName()));

            CreateMap<Garante, ClienteResumenViewModel>()
                .ForMember(d => d.Id, o => o.MapFrom(s => s.GaranteClienteId ?? 0))
                .ForMember(d => d.NombreCompleto, o => o.MapFrom(s =>
                    s.GaranteCliente != null
                        ? (!string.IsNullOrWhiteSpace(s.GaranteCliente.NombreCompleto)
                            ? s.GaranteCliente.NombreCompleto
                            : s.GaranteCliente.ToDisplayName())
                        : s.ToDisplayName()))
                .ForMember(d => d.NumeroDocumento, o => o.MapFrom(s =>
                    s.GaranteCliente != null ? s.GaranteCliente.NumeroDocumento : s.NumeroDocumento ?? string.Empty))
                .ForMember(d => d.Telefono, o => o.MapFrom(s =>
                    s.GaranteCliente != null ? s.GaranteCliente.Telefono : s.Telefono ?? string.Empty))
                .ForMember(d => d.Email, o => o.MapFrom(s => s.GaranteCliente != null ? s.GaranteCliente.Email : null))
                .ForMember(d => d.Domicilio, o => o.MapFrom(s => s.GaranteCliente != null ? s.GaranteCliente.Domicilio : s.Domicilio))
                .ForMember(d => d.PuntajeRiesgo, o => o.MapFrom(s => s.GaranteCliente != null ? s.GaranteCliente.PuntajeRiesgo :
 0))
                .ForMember(d => d.Sueldo, o => o.MapFrom(s => s.GaranteCliente != null ? s.GaranteCliente.Sueldo : (decimal?)null));

            CreateMap<Cliente, ClienteViewModel>()
                .ForMember(d => d.NombreCompleto, o => o.MapFrom(s =>
                    !string.IsNullOrWhiteSpace(s.NombreCompleto)
                        ? s.NombreCompleto
                        : s.ToDisplayName()))
                .ForMember(d => d.Edad, o => o.MapFrom(s => ClienteHelper.CalcularEdad(s.FechaNacimiento)))
                .ForMember(d => d.CreditosActivos, o => o.MapFrom(s => s.Creditos.Count(c =>
                    !c.IsDeleted && c.Estado == EstadoCredito.Activo)))
                .ForMember(d => d.MontoAdeudado, o => o.MapFrom(s => s.Creditos
                    .Where(c => !c.IsDeleted && c.Estado == EstadoCredito.Activo)
                    .Sum(c => c.SaldoPendiente)));

            CreateMap<ClienteViewModel, Cliente>()
                .ForMember(d => d.Creditos, o => o.Ignore())
                .ForMember(d => d.ComoGarante, o => o.Ignore());

            // =======================
            // Credito
            // =======================
            CreateMap<Credito, CreditoViewModel>()
                .ForMember(d => d.Cliente, o => o.MapFrom(s => s.Cliente))
                .ForMember(d => d.Garante, o => o.MapFrom(s => s.Garante))
                .ForMember(d => d.Cuotas, o => o.MapFrom(s => s.Cuotas != null ? s.Cuotas.Where(cu => !cu.IsDeleted) : Enumerable.Empty<Cuota>()));

            CreateMap<CreditoViewModel, Credito>()
                .ForMember(d => d.Cliente, o => o.Ignore())
                .ForMember(d => d.Garante, o => o.Ignore())
                .ForMember(d => d.Cuotas, o => o.Ignore());

            // =======================
            // Cuota
            // =======================
            CreateMap<Cuota, CuotaViewModel>()
                .ForMember(d => d.CreditoNumero, o => o.MapFrom(s => s.Credito != null ? s.Credito.Numero : string.Empty))
                .ForMember(d => d.ClienteNombre, o => o.MapFrom(s => s.Credito != null && s.Credito.Cliente != null ? s.Credito.Cliente.ToDisplayName() : string.Empty));

            CreateMap<CuotaViewModel, Cuota>()
                .ForMember(d => d.Credito, o => o.Ignore());

            // =======================
            // Mappings para Ventas
            // =======================
            CreateMap<Venta, VentaViewModel>()
                .ForMember(dest => dest.ClienteNombre, opt => opt.MapFrom(src =>
                    src.Cliente != null ? src.Cliente.ToDisplayName() : string.Empty))
                .ForMember(dest => dest.ClienteDocumento, opt => opt.MapFrom(src => src.Cliente != null ? src.Cliente.NumeroDocumento : string.Empty))
                .ForMember(dest => dest.CreditoNumero, opt => opt.MapFrom(src => src.Credito != null ? src.Credito.Numero : null))
                .ForMember(dest => dest.Detalles, opt => opt.MapFrom(src => src.Detalles != null ? src.Detalles.Where(d => !d.IsDeleted) : Enumerable.Empty<VentaDetalle>()))
                .ForMember(dest => dest.Facturas, opt => opt.MapFrom(src => src.Facturas != null ? src.Facturas.Where(f => !f.IsDeleted) : Enumerable.Empty<Factura>()))
                .ForMember(dest => dest.RecargoDebitoAplicado, opt => opt.MapFrom(src =>
                    src.DatosTarjeta != null &&
                    src.DatosTarjeta.TipoTarjeta == TipoTarjeta.Debito &&
                    src.DatosTarjeta.RecargoAplicado.HasValue
                        ? src.DatosTarjeta.RecargoAplicado.Value
                        : 0m));

            CreateMap<VentaViewModel, Venta>()
                .ForMember(dest => dest.Cliente, opt => opt.Ignore())
                .ForMember(dest => dest.Credito, opt => opt.Ignore())
                .ForMember(dest => dest.Detalles, opt => opt.Ignore())
                .ForMember(dest => dest.Facturas, opt => opt.Ignore())
                .ForMember(dest => dest.DatosCheque, opt => opt.Ignore())
                .ForMember(dest => dest.DatosTarjeta, opt => opt.Ignore());

            CreateMap<VentaDetalle, VentaDetalleViewModel>()
                .ForMember(dest => dest.ProductoNombre, opt => opt.MapFrom(src => src.Producto != null ? src.Producto.Nombre : string.Empty))
                .ForMember(dest => dest.ProductoCodigo, opt => opt.MapFrom(src => src.Producto != null ? src.Producto.Codigo : string.Empty))
                .ForMember(dest => dest.StockDisponible, opt => opt.MapFrom(src => src.Producto != null ? src.Producto.StockActual : 0));

            CreateMap<VentaDetalleViewModel, VentaDetalle>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.RowVersion, opt => opt.Ignore())
                .ForMember(dest => dest.Producto, opt => opt.Ignore())
                .ForMember(dest => dest.Venta, opt => opt.Ignore())
                .ForMember(dest => dest.ComisionPorcentajeAplicada, opt => opt.Ignore())
                .ForMember(dest => dest.ComisionMonto, opt => opt.Ignore())
                .ForMember(dest => dest.PorcentajeIVA, opt => opt.Ignore())
                .ForMember(dest => dest.AlicuotaIVAId, opt => opt.Ignore())
                .ForMember(dest => dest.AlicuotaIVANombre, opt => opt.Ignore())
                .ForMember(dest => dest.PrecioUnitarioNeto, opt => opt.Ignore())
                .ForMember(dest => dest.IVAUnitario, opt => opt.Ignore())
                .ForMember(dest => dest.SubtotalNeto, opt => opt.Ignore())
                .ForMember(dest => dest.SubtotalIVA, opt => opt.Ignore())
                .ForMember(dest => dest.DescuentoGeneralProrrateado, opt => opt.Ignore())
                .ForMember(dest => dest.SubtotalFinalNeto, opt => opt.Ignore())
                .ForMember(dest => dest.SubtotalFinalIVA, opt => opt.Ignore())
                .ForMember(dest => dest.SubtotalFinal, opt => opt.Ignore())
                .ForMember(dest => dest.CostoUnitarioAlMomento, opt => opt.Ignore())
                .ForMember(dest => dest.CostoTotalAlMomento, opt => opt.Ignore());

            CreateMap<Factura, FacturaViewModel>();

            CreateMap<FacturaViewModel, Factura>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.Venta, opt => opt.Ignore());

            // =======================
            // ConfiguracionPago
            // =======================
            CreateMap<ConfiguracionPago, ConfiguracionPagoViewModel>()
                .ForMember(d => d.ConfiguracionesTarjeta, o => o.MapFrom(s => s.ConfiguracionesTarjeta));

            CreateMap<ConfiguracionPagoViewModel, ConfiguracionPago>()
                .ForMember(d => d.ConfiguracionesTarjeta, o => o.Ignore());

            // =======================
            // ConfiguracionTarjeta
            // =======================
            CreateMap<ConfiguracionTarjeta, ConfiguracionTarjetaViewModel>();

            CreateMap<ConfiguracionTarjetaViewModel, ConfiguracionTarjeta>()
                .ForMember(d => d.ConfiguracionPago, o => o.Ignore());

            // PerfilCredito
            CreateMap<PerfilCredito, PerfilCreditoViewModel>().ReverseMap();

            // =======================
            // DatosTarjeta
            // =======================
            CreateMap<DatosTarjeta, DatosTarjetaViewModel>();

            CreateMap<DatosTarjetaViewModel, DatosTarjeta>()
                .ForMember(d => d.Venta, o => o.Ignore())
                .ForMember(d => d.ConfiguracionTarjeta, o => o.Ignore());

            // =======================
            // DatosCheque
            // =======================
            CreateMap<DatosCheque, DatosChequeViewModel>();

            CreateMap<DatosChequeViewModel, DatosCheque>()
                .ForMember(d => d.Venta, o => o.Ignore());

            // =======================
            // VentaCreditoCuota
            // =======================
            CreateMap<VentaCreditoCuota, VentaCreditoCuotaViewModel>();

            CreateMap<VentaCreditoCuotaViewModel, VentaCreditoCuota>()
                .ForMember(d => d.Venta, o => o.Ignore())
                .ForMember(d => d.Credito, o => o.Ignore());

            // =======================
            // DatosCreditoPersonal
            // =======================
            CreateMap<DatosCreditoPersonallViewModel, VentaCreditoCuota>()
                .ForMember(d => d.Venta, o => o.Ignore())
                .ForMember(d => d.Credito, o => o.Ignore());

            // Evaluación Crédito
            CreateMap<EvaluacionCredito, EvaluacionCreditoViewModel>()
                .ForMember(dest => dest.ClienteNombre, opt => opt.MapFrom(src =>
                    src.Cliente != null ? src.Cliente.ToDisplayName() : null))
                .ForMember(dest => dest.Reglas, opt => opt.Ignore()) // Reglas se construyen en el servicio, no se mapean desde la entidad
                .ReverseMap()
                .ForMember(dest => dest.Cliente, opt => opt.Ignore())
                .ForMember(dest => dest.Credito, opt => opt.Ignore());
            // =======================
            // DocumentoCliente
            // =======================
            CreateMap<DocumentoCliente, DocumentoClienteViewModel>()
                .ForMember(dest => dest.Cliente, opt => opt.MapFrom(src => src.Cliente))
                .ForMember(dest => dest.Archivo, opt => opt.Ignore());

            CreateMap<DocumentoClienteViewModel, DocumentoCliente>()
                .ForMember(dest => dest.Cliente, opt => opt.Ignore())
                .ForMember(dest => dest.NombreArchivo, opt => opt.Ignore())
                .ForMember(dest => dest.RutaArchivo, opt => opt.Ignore())
                .ForMember(dest => dest.TipoMIME, opt => opt.Ignore())
                .ForMember(dest => dest.TamanoBytes, opt => opt.Ignore());
            // Mora
            CreateMap<ConfiguracionMora, ConfiguracionMoraViewModel>()
                .ForMember(d => d.DiasGracia,                  o => o.MapFrom(s => s.DiasGracia ?? 0))
                .ForMember(d => d.DiasGraciaMora,              o => o.MapFrom(s => s.DiasGracia ?? 0))
                .ForMember(d => d.TasaMoraDiaria,              o => o.MapFrom(s => s.TasaMoraBase ?? 0))
                .ForMember(d => d.PorcentajeRecargo,           o => o.MapFrom(s => s.TasaMoraBase ?? 0))
                .ForMember(d => d.PorcentajeRecargoPrimerMes,  o => o.MapFrom(s => s.TasaPrimerMes ?? 0))
                .ForMember(d => d.PorcentajeRecargoSegundoMes, o => o.MapFrom(s => s.TasaSegundoMes ?? 0))
                .ForMember(d => d.PorcentajeRecargoTercerMes,  o => o.MapFrom(s => s.TasaTercerMesEnAdelante ?? 0))
                // Campos sin equivalente en la entidad — quedan en default
                .ForMember(d => d.DiasAntesAlertaVencimiento,  o => o.Ignore())
                .ForMember(d => d.CalculoAutomatico,            o => o.Ignore())
                .ForMember(d => d.NotificacionAutomatica,       o => o.Ignore())
                .ForMember(d => d.JobActivo,                    o => o.Ignore())
                .ForMember(d => d.HoraEjecucion,                o => o.Ignore())
                .ForMember(d => d.UltimaEjecucion,              o => o.Ignore());

            CreateMap<ConfiguracionMoraViewModel, ConfiguracionMora>()
                .ForMember(d => d.DiasGracia,                    o => o.MapFrom(s => s.DiasGraciaMora != 0 ? s.DiasGraciaMora : s.DiasGracia))
                .ForMember(d => d.TasaMoraBase,                  o => o.MapFrom(s => s.TasaMoraDiaria != 0 ? (decimal?)s.TasaMoraDiaria : (s.PorcentajeRecargo != 0 ? (decimal?)s.PorcentajeRecargo : null)))
                .ForMember(d => d.TasaPrimerMes,                 o => o.MapFrom(s => s.PorcentajeRecargoPrimerMes != 0 ? (decimal?)s.PorcentajeRecargoPrimerMes : null))
                .ForMember(d => d.TasaSegundoMes,                o => o.MapFrom(s => s.PorcentajeRecargoSegundoMes != 0 ? (decimal?)s.PorcentajeRecargoSegundoMes : null))
                .ForMember(d => d.TasaTercerMesEnAdelante,       o => o.MapFrom(s => s.PorcentajeRecargoTercerMes != 0 ? (decimal?)s.PorcentajeRecargoTercerMes : null))
                // Propiedades de la entidad sin equivalente en este ViewModel simplificado
                .ForMember(d => d.TipoTasaMora,                  o => o.Ignore())
                .ForMember(d => d.BaseCalculoMora,               o => o.Ignore())
                .ForMember(d => d.EscalonamientoActivo,          o => o.Ignore())
                .ForMember(d => d.TopeMaximoMoraActivo,          o => o.Ignore())
                .ForMember(d => d.TipoTopeMora,                  o => o.Ignore())
                .ForMember(d => d.ValorTopeMora,                 o => o.Ignore())
                .ForMember(d => d.MoraMinima,                    o => o.Ignore())
                .ForMember(d => d.DiasParaPrioridadMedia,        o => o.Ignore())
                .ForMember(d => d.DiasParaPrioridadAlta,         o => o.Ignore())
                .ForMember(d => d.DiasParaPrioridadCritica,      o => o.Ignore())
                .ForMember(d => d.MontoParaPrioridadMedia,       o => o.Ignore())
                .ForMember(d => d.MontoParaPrioridadAlta,        o => o.Ignore())
                .ForMember(d => d.MontoParaPrioridadCritica,     o => o.Ignore())
                .ForMember(d => d.ProcesoAutomaticoActivo,       o => o.Ignore())
                .ForMember(d => d.HoraEjecucionDiaria,           o => o.Ignore())
                .ForMember(d => d.AlertasPreventivasActivas,     o => o.Ignore())
                .ForMember(d => d.DiasAntesAlertaPreventiva,     o => o.Ignore())
                .ForMember(d => d.CambiarEstadoCuotaAuto,        o => o.Ignore())
                .ForMember(d => d.ActualizarMoraAutomaticamente, o => o.Ignore())
                .ForMember(d => d.UltimaEjecucion,               o => o.Ignore())
                .ForMember(d => d.NotificacionesActivas,         o => o.Ignore())
                .ForMember(d => d.WhatsAppActivo,                o => o.Ignore())
                .ForMember(d => d.EmailActivo,                   o => o.Ignore())
                .ForMember(d => d.CanalPreferido,                o => o.Ignore())
                .ForMember(d => d.NotificarProximoVencimiento,   o => o.Ignore())
                .ForMember(d => d.DiasAntesNotificacionPreventiva, o => o.Ignore())
                .ForMember(d => d.NotificarCuotaVencida,         o => o.Ignore())
                .ForMember(d => d.NotificarMoraAcumulada,        o => o.Ignore())
                .ForMember(d => d.FrecuenciaRecordatorioMora,    o => o.Ignore())
                .ForMember(d => d.MaximoNotificacionesDiarias,   o => o.Ignore())
                .ForMember(d => d.MaximoNotificacionesPorCuota,  o => o.Ignore())
                .ForMember(d => d.HoraInicioEnvio,               o => o.Ignore())
                .ForMember(d => d.HoraFinEnvio,                  o => o.Ignore())
                .ForMember(d => d.EnviarFinDeSemana,             o => o.Ignore())
                .ForMember(d => d.DiasMaximosSinGestion,         o => o.Ignore())
                .ForMember(d => d.DiasParaCumplirPromesa,        o => o.Ignore())
                .ForMember(d => d.MaximoCuotasAcuerdo,           o => o.Ignore())
                .ForMember(d => d.PorcentajeMinimoEntrega,       o => o.Ignore())
                .ForMember(d => d.PermitirCondonacionMora,       o => o.Ignore())
                .ForMember(d => d.PorcentajeMaximoCondonacion,   o => o.Ignore())
                .ForMember(d => d.BloqueoAutomaticoActivo,       o => o.Ignore())
                .ForMember(d => d.DiasParaBloquear,              o => o.Ignore())
                .ForMember(d => d.CuotasVencidasParaBloquear,    o => o.Ignore())
                .ForMember(d => d.MontoMoraParaBloquear,         o => o.Ignore())
                .ForMember(d => d.TipoBloqueo,                   o => o.Ignore())
                .ForMember(d => d.DesbloqueoAutomatico,          o => o.Ignore())
                .ForMember(d => d.ImpactarScorePorMora,          o => o.Ignore())
                .ForMember(d => d.PuntosRestarPorCuotaVencida,   o => o.Ignore())
                .ForMember(d => d.PuntosRestarPorDiaMora,        o => o.Ignore())
                .ForMember(d => d.PuntosMaximosARestar,          o => o.Ignore())
                .ForMember(d => d.RecuperarScoreAlPagar,         o => o.Ignore())
                .ForMember(d => d.PorcentajeRecuperacionScore,   o => o.Ignore());
            
            // Mapeo para AlertaMora
            CreateMap<AlertaMora, AlertaMoraViewModel>().ReverseMap();
            
            CreateMap<AlertaCobranza, AlertaCobranzaViewModel>()
                .ForMember(dest => dest.ClienteNombre, opt => opt.MapFrom(src =>
                    src.Cliente != null ? src.Cliente.ToDisplayName() : string.Empty))
                .ForMember(dest => dest.ClienteDocumento, opt => opt.MapFrom(src =>
                    src.Cliente != null ? src.Cliente.NumeroDocumento : string.Empty))
                .ForMember(dest => dest.TipoNombre, opt => opt.MapFrom(src => src.Tipo.ToString()))
                .ForMember(dest => dest.RowVersion, opt => opt.MapFrom(src => src.RowVersion))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.CreatedAt))
                .ForMember(dest => dest.Leida, opt => opt.MapFrom(src => src.UpdatedAt != null && src.UpdatedAt > src.CreatedAt))
                .ForMember(dest => dest.Titulo, opt => opt.MapFrom(src =>
                    src.Cliente != null ? $"Alerta: {src.Cliente.NombreCompleto}" : "Alerta de cobranza"))
                .ForMember(dest => dest.ColorAlerta, opt => opt.MapFrom(src => GetColorAlerta(src.Prioridad)))
                .ForMember(dest => dest.IconoAlerta, opt => opt.MapFrom(src => GetIconoAlerta(src.Prioridad)));
            CreateMap<AlertaCobranzaViewModel, AlertaCobranza>();

            // =======================
            // AlertaStock
            // =======================
            CreateMap<AlertaStock, AlertaStockViewModel>()
                .ForMember(dest => dest.ProductoCodigo, opt => opt.MapFrom(src => src.Producto != null ? src.Producto.Codigo : string.Empty))
                .ForMember(dest => dest.ProductoNombre, opt => opt.MapFrom(src => src.Producto != null ? src.Producto.Nombre : string.Empty))
                .ForMember(dest => dest.CategoriaNombre, opt => opt.MapFrom(src => src.Producto != null && src.Producto.Categoria != null ? src.Producto.Categoria.Nombre : string.Empty))
                .ForMember(dest => dest.MarcaNombre, opt => opt.MapFrom(src => src.Producto != null && src.Producto.Marca != null ? src.Producto.Marca.Nombre : string.Empty))
                .ForMember(dest => dest.PorcentajeStockMinimo, opt => opt.MapFrom(src =>
                    src.StockMinimo == 0 ? 0 : (src.StockActual / src.StockMinimo) * 100))
                .ForMember(dest => dest.DiasDesdeAlerta, opt => opt.MapFrom(src =>
                    (int)(DateTime.UtcNow - src.FechaAlerta).TotalDays))
                .ForMember(dest => dest.EstaVencida, opt => opt.MapFrom(src =>
                    src.FechaResolucion == null && (DateTime.UtcNow - src.FechaAlerta).TotalDays > 30));

            CreateMap<AlertaStockViewModel, AlertaStock>()
                .ForMember(dest => dest.Producto, opt => opt.Ignore());

            // =======================
            // PrecioHistorico
            // =======================
            CreateMap<PrecioHistorico, PrecioHistoricoViewModel>()
                .ForMember(dest => dest.ProductoCodigo, opt => opt.MapFrom(src => src.Producto != null ? src.Producto.Codigo : string.Empty))
                .ForMember(dest => dest.ProductoNombre, opt => opt.MapFrom(src => src.Producto != null ? src.Producto.Nombre : string.Empty))
                .ForMember(dest => dest.PorcentajeCambioCompra, opt => opt.MapFrom(src =>
                    src.PrecioCompraAnterior == 0 ? 0 : ((src.PrecioCompraNuevo - src.PrecioCompraAnterior) / src.PrecioCompraAnterior) * 100))
                .ForMember(dest => dest.PorcentajeCambioVenta, opt => opt.MapFrom(src =>
                    src.PrecioVentaAnterior == 0 ? 0 : ((src.PrecioVentaNuevo - src.PrecioVentaAnterior) / src.PrecioVentaAnterior) * 100))
                .ForMember(dest => dest.MargenAnterior, opt => opt.MapFrom(src =>
                    src.PrecioCompraAnterior == 0 ? 0 : ((src.PrecioVentaAnterior - src.PrecioCompraAnterior) / src.PrecioCompraAnterior) * 100))
                .ForMember(dest => dest.MargenNuevo, opt => opt.MapFrom(src =>
                    src.PrecioCompraNuevo == 0 ? 0 : ((src.PrecioVentaNuevo - src.PrecioCompraNuevo) / src.PrecioCompraNuevo) * 100));

            CreateMap<PrecioHistoricoViewModel, PrecioHistorico>()
                .ForMember(dest => dest.Producto, opt => opt.Ignore());

            // =======================
            // Caja
            // =======================
            CreateMap<Caja, CajaViewModel>();

            CreateMap<CajaViewModel, Caja>()
                .ForMember(dest => dest.Aperturas, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.RowVersion, opt => opt.Ignore());

            // =======================
            // Autorizaciones
            // =======================
            CreateMap<UmbralAutorizacion, UmbralAutorizacionViewModel>().ReverseMap();

            CreateMap<SolicitudAutorizacion, GestionarSolicitudViewModel>();

            CreateMap<GestionarSolicitudViewModel, SolicitudAutorizacion>()
                .ForMember(dest => dest.UsuarioAutorizador, opt => opt.Ignore())
                .ForMember(dest => dest.FechaResolucion, opt => opt.Ignore());

            // =======================
            // Devoluciones y Garantías
            // =======================
            CreateMap<Devolucion, CrearDevolucionViewModel>()
                .ForMember(dest => dest.Productos, opt => opt.Ignore());

            CreateMap<CrearDevolucionViewModel, Devolucion>()
                .ForMember(dest => dest.Venta, opt => opt.Ignore())
                .ForMember(dest => dest.Cliente, opt => opt.Ignore())
                .ForMember(dest => dest.Detalles, opt => opt.Ignore())
                .ForMember(dest => dest.NotaCredito, opt => opt.Ignore())
                .ForMember(dest => dest.RMA, opt => opt.Ignore());

            CreateMap<DevolucionDetalle, ProductoDevolucionViewModel>()
                .ForMember(dest => dest.ProductoNombre, opt => opt.MapFrom(src => src.Producto != null ? src.Producto.Nombre : string.Empty))
                .ForMember(dest => dest.CantidadComprada, opt => opt.Ignore())
                .ForMember(dest => dest.CantidadDevolver, opt => opt.MapFrom(src => src.Cantidad));

            CreateMap<ProductoDevolucionViewModel, DevolucionDetalle>()
                .ForMember(dest => dest.Cantidad, opt => opt.MapFrom(src => src.CantidadDevolver))
                .ForMember(dest => dest.Producto, opt => opt.Ignore())
                .ForMember(dest => dest.Devolucion, opt => opt.Ignore());

            CreateMap<Garantia, CrearGarantiaViewModel>();

            CreateMap<CrearGarantiaViewModel, Garantia>()
                .ForMember(dest => dest.VentaDetalle, opt => opt.Ignore())
                .ForMember(dest => dest.Producto, opt => opt.Ignore())
                .ForMember(dest => dest.Cliente, opt => opt.Ignore())
                .ForMember(dest => dest.NumeroGarantia, opt => opt.Ignore())
                .ForMember(dest => dest.FechaVencimiento, opt => opt.Ignore())
                .ForMember(dest => dest.Estado, opt => opt.Ignore());

            CreateMap<RMA, CrearRMAViewModel>();

            CreateMap<CrearRMAViewModel, RMA>()
                .ForMember(dest => dest.Devolucion, opt => opt.Ignore())
                .ForMember(dest => dest.Proveedor, opt => opt.Ignore())
                .ForMember(dest => dest.NumeroRMA, opt => opt.Ignore())
                .ForMember(dest => dest.FechaSolicitud, opt => opt.Ignore())
                .ForMember(dest => dest.Estado, opt => opt.Ignore());

            CreateMap<RMA, GestionarRMAViewModel>()
                .ForMember(dest => dest.RMA, opt => opt.MapFrom(src => src));

            CreateMap<GestionarRMAViewModel, RMA>()
                .ForMember(dest => dest.Devolucion, opt => opt.Ignore())
                .ForMember(dest => dest.Proveedor, opt => opt.Ignore());

            // =======================
            // Ticket
            // =======================
            CreateMap<Ticket, TicketViewModel>()
                .ForMember(d => d.TipoNombre,   o => o.MapFrom(s => s.Tipo.GetDisplayName()))
                .ForMember(d => d.EstadoNombre, o => o.MapFrom(s => s.Estado.GetDisplayName()));

            CreateMap<Ticket, TicketDetalleViewModel>()
                .ForMember(d => d.TipoNombre,   o => o.MapFrom(s => s.Tipo.GetDisplayName()))
                .ForMember(d => d.EstadoNombre, o => o.MapFrom(s => s.Estado.GetDisplayName()));

            CreateMap<CreateTicketRequest, Ticket>()
                .ForMember(d => d.Id,              o => o.Ignore())
                .ForMember(d => d.Estado,          o => o.Ignore())
                .ForMember(d => d.Resolucion,      o => o.Ignore())
                .ForMember(d => d.ResueltoPor,     o => o.Ignore())
                .ForMember(d => d.FechaResolucion, o => o.Ignore())
                .ForMember(d => d.Adjuntos,        o => o.Ignore())
                .ForMember(d => d.ChecklistItems,  o => o.Ignore())
                .ForMember(d => d.CreatedAt,       o => o.Ignore())
                .ForMember(d => d.CreatedBy,       o => o.Ignore())
                .ForMember(d => d.UpdatedAt,       o => o.Ignore())
                .ForMember(d => d.UpdatedBy,       o => o.Ignore())
                .ForMember(d => d.IsDeleted,       o => o.Ignore())
                .ForMember(d => d.RowVersion,      o => o.Ignore());

            CreateMap<UpdateTicketRequest, Ticket>()
                .ForMember(d => d.Id,              o => o.Ignore())
                .ForMember(d => d.Estado,          o => o.Ignore())
                .ForMember(d => d.ModuloOrigen,    o => o.Ignore())
                .ForMember(d => d.VistaOrigen,     o => o.Ignore())
                .ForMember(d => d.UrlOrigen,       o => o.Ignore())
                .ForMember(d => d.ContextKey,      o => o.Ignore())
                .ForMember(d => d.Resolucion,      o => o.Ignore())
                .ForMember(d => d.ResueltoPor,     o => o.Ignore())
                .ForMember(d => d.FechaResolucion, o => o.Ignore())
                .ForMember(d => d.Adjuntos,        o => o.Ignore())
                .ForMember(d => d.ChecklistItems,  o => o.Ignore())
                .ForMember(d => d.CreatedAt,       o => o.Ignore())
                .ForMember(d => d.CreatedBy,       o => o.Ignore())
                .ForMember(d => d.UpdatedAt,       o => o.Ignore())
                .ForMember(d => d.UpdatedBy,       o => o.Ignore())
                .ForMember(d => d.IsDeleted,       o => o.Ignore())
                .ForMember(d => d.RowVersion,      o => o.Ignore());

            CreateMap<TicketAdjunto, TicketAdjuntoViewModel>();

            CreateMap<TicketChecklistItem, TicketChecklistItemViewModel>();

        }

        private static string GetColorAlerta(PrioridadAlerta prioridad)
        {
            return prioridad switch
            {
                PrioridadAlerta.Critica => "dark",
                PrioridadAlerta.Alta => "danger",
                PrioridadAlerta.Media => "warning",
                PrioridadAlerta.Baja => "info",
                _ => "secondary"
            };
        }

        private static string GetIconoAlerta(PrioridadAlerta prioridad)
        {
            return prioridad switch
            {
                PrioridadAlerta.Critica => "bi bi-exclamation-octagon-fill",
                PrioridadAlerta.Alta => "bi bi-exclamation-triangle-fill",
                PrioridadAlerta.Media => "bi bi-exclamation-circle-fill",
                PrioridadAlerta.Baja => "bi bi-info-circle-fill",
                _ => "bi bi-bell"
            };
        }
    }
}
