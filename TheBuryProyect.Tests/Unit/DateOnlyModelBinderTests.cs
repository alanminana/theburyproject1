using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

public class DateOnlyModelBinderTests
{
    [Theory]
    [InlineData("2028-08-04", 2028, 8, 4)]
    [InlineData("2026-01-01", 2026, 1, 1)]
    [InlineData("2026-12-31", 2026, 12, 31)]
    public async Task BindModelAsync_FormatoIso_BindeaDateOnly_SinImportarCulturaDelServidor(
        string raw, int anio, int mes, int dia)
    {
        // El caso real: un <input type="date"> siempre postea ISO 8601 sin importar la
        // cultura del navegador. Bajo cultura es-AR (dd/MM/yyyy) sin este binder, un valor
        // como "2028-08-04" fallaba a parsear (día=2028 inválido) — PUN-ML8.
        var bindingContext = CreateBindingContext(raw, CultureInfo.GetCultureInfo("es-AR"));
        var binder = new DateOnlyModelBinder();

        await binder.BindModelAsync(bindingContext);

        Assert.True(bindingContext.Result.IsModelSet);
        Assert.Equal(new DateOnly(anio, mes, dia), bindingContext.Result.Model);
        Assert.Empty(bindingContext.ModelState[nameof(ConfiguracionModel.VigenteDesde)]!.Errors);
    }

    [Fact]
    public async Task BindModelAsync_FormatoCulturaServidor_FallbackBindeaDateOnly()
    {
        // Defensivo: un valor ya en formato es-AR (no proveniente de un <input type="date">)
        // sigue bindeando correctamente vía el fallback de cultura.
        var bindingContext = CreateBindingContext("04/08/2028", CultureInfo.GetCultureInfo("es-AR"));
        var binder = new DateOnlyModelBinder();

        await binder.BindModelAsync(bindingContext);

        Assert.True(bindingContext.Result.IsModelSet);
        Assert.Equal(new DateOnly(2028, 8, 4), bindingContext.Result.Model);
    }

    [Fact]
    public async Task BindModelAsync_ValorInvalido_AgregaErrorDeModelState()
    {
        var bindingContext = CreateBindingContext("no-es-una-fecha", CultureInfo.InvariantCulture);
        var binder = new DateOnlyModelBinder();

        await binder.BindModelAsync(bindingContext);

        Assert.False(bindingContext.Result.IsModelSet);
        Assert.False(bindingContext.ModelState.IsValid);
        Assert.True(bindingContext.ModelState.ContainsKey(nameof(ConfiguracionModel.VigenteDesde)));
    }

    // DateOnly? preserva la semántica de opcional: vacío bindea null, no lanza error.

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BindModelAsync_NullableVacio_BindeaNull(string raw)
    {
        var bindingContext = CreateBindingContext(
            raw,
            nameof(FiltroModel.Desde),
            typeof(FiltroModel),
            nameof(FiltroModel.Desde),
            CultureInfo.GetCultureInfo("es-AR"));
        var binder = new DateOnlyModelBinder();

        await binder.BindModelAsync(bindingContext);

        Assert.True(bindingContext.Result.IsModelSet);
        Assert.Null(bindingContext.Result.Model);
    }

    [Fact]
    public async Task BindModelAsync_NoNullableVacio_AgregaError()
    {
        var bindingContext = CreateBindingContext("", CultureInfo.InvariantCulture);
        var binder = new DateOnlyModelBinder();

        await binder.BindModelAsync(bindingContext);

        Assert.False(bindingContext.Result.IsModelSet);
        Assert.False(bindingContext.ModelState.IsValid);
    }

    // El provider aplica el binder a todo DateOnly/DateOnly? del pipeline MVC
    // (registrado en Program.cs); otros tipos siguen con el binder por defecto.

    [Theory]
    [InlineData(typeof(DateOnly), true)]
    [InlineData(typeof(DateOnly?), true)]
    [InlineData(typeof(DateTime), false)]
    [InlineData(typeof(int), false)]
    [InlineData(typeof(string), false)]
    public void Provider_SoloDevuelveBinderParaDateOnly(Type modelType, bool esperaBinder)
    {
        var provider = new DateOnlyModelBinderProvider();

        var binder = provider.GetBinder(new TestModelBinderProviderContext(modelType));

        if (esperaBinder)
        {
            Assert.IsType<DateOnlyModelBinder>(binder);
        }
        else
        {
            Assert.Null(binder);
        }
    }

    private static DefaultModelBindingContext CreateBindingContext(string raw, CultureInfo culture)
        => CreateBindingContext(
            raw,
            nameof(ConfiguracionModel.VigenteDesde),
            typeof(ConfiguracionModel),
            nameof(ConfiguracionModel.VigenteDesde),
            culture);

    private static DefaultModelBindingContext CreateBindingContext(
        string raw,
        string key,
        Type modelType,
        string propertyName,
        CultureInfo culture)
    {
        var valueProvider = new SingleValueProvider(key, raw, culture);

        return new DefaultModelBindingContext
        {
            ModelMetadata = new EmptyModelMetadataProvider()
                .GetMetadataForProperty(modelType, propertyName),
            ModelName = key,
            ModelState = new ModelStateDictionary(),
            ValueProvider = valueProvider
        };
    }

    private sealed class ConfiguracionModel
    {
        public DateOnly VigenteDesde { get; set; }
    }

    private sealed class FiltroModel
    {
        public DateOnly? Desde { get; set; }
    }

    private sealed class TestModelBinderProviderContext : ModelBinderProviderContext
    {
        private static readonly EmptyModelMetadataProvider Provider = new();
        private readonly ModelMetadata _metadata;

        public TestModelBinderProviderContext(Type modelType)
            => _metadata = Provider.GetMetadataForType(modelType);

        public override Microsoft.AspNetCore.Mvc.ModelBinding.BindingInfo BindingInfo { get; } = new();
        public override ModelMetadata Metadata => _metadata;
        public override IModelMetadataProvider MetadataProvider => Provider;

        public override IModelBinder CreateBinder(ModelMetadata metadata)
            => throw new NotSupportedException();
    }

    private sealed class SingleValueProvider : IValueProvider
    {
        private readonly string _key;
        private readonly string _value;
        private readonly CultureInfo _culture;

        public SingleValueProvider(string key, string value, CultureInfo culture)
        {
            _key = key;
            _value = value;
            _culture = culture;
        }

        public bool ContainsPrefix(string prefix)
            => string.Equals(prefix, _key, StringComparison.Ordinal);

        public ValueProviderResult GetValue(string key)
            => string.Equals(key, _key, StringComparison.Ordinal)
                ? new ValueProviderResult(_value, _culture)
                : ValueProviderResult.None;
    }
}
