using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

public class DecimalModelBinderTests
{
    [Theory]
    [InlineData("12,5", 12.5)]
    [InlineData("12.5", 12.5)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("-1,5", -1.5)]
    [InlineData("1.234,56", 1234.56)]
    public async Task BindModelAsync_FormatoSoportado_BindeaDecimal(string raw, double esperado)
    {
        var bindingContext = CreateBindingContext(raw);
        var binder = new DecimalModelBinder();

        await binder.BindModelAsync(bindingContext);

        Assert.True(bindingContext.Result.IsModelSet);
        Assert.Equal(Convert.ToDecimal(esperado), bindingContext.Result.Model);
        Assert.Empty(bindingContext.ModelState[nameof(ProductoComisionModel.ComisionPorcentaje)]!.Errors);
    }

    [Fact]
    public async Task BindModelAsync_ValorInvalido_AgregaErrorDeModelState()
    {
        var bindingContext = CreateBindingContext("abc");
        var binder = new DecimalModelBinder();

        await binder.BindModelAsync(bindingContext);

        Assert.False(bindingContext.Result.IsModelSet);
        Assert.False(bindingContext.ModelState.IsValid);
        Assert.True(bindingContext.ModelState.ContainsKey(nameof(ProductoComisionModel.ComisionPorcentaje)));
    }

    private static DefaultModelBindingContext CreateBindingContext(string raw)
    {
        var valueProvider = new SingleValueProvider(
            nameof(ProductoComisionModel.ComisionPorcentaje),
            raw,
            CultureInfo.InvariantCulture);

        return new DefaultModelBindingContext
        {
            ModelMetadata = new EmptyModelMetadataProvider()
                .GetMetadataForProperty(typeof(ProductoComisionModel), nameof(ProductoComisionModel.ComisionPorcentaje)),
            ModelName = nameof(ProductoComisionModel.ComisionPorcentaje),
            ModelState = new ModelStateDictionary(),
            ValueProvider = valueProvider
        };
    }

    private sealed class ProductoComisionModel
    {
        public decimal ComisionPorcentaje { get; set; }
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
