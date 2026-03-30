using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TheBuryProject.Helpers;

/// <summary>
/// Model binder que acepta decimales con coma o punto como separador,
/// independientemente de la cultura del servidor.
/// Registrar con [ModelBinder(typeof(DecimalModelBinder))] en el ViewModel.
/// </summary>
public class DecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None)
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);

        var raw = valueResult.FirstValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            bindingContext.Result = ModelBindingResult.Success(0m);
            return Task.CompletedTask;
        }

        // Normalizar: si tiene coma como decimal y punto como miles → "1.234,56" → "1234.56"
        // Si tiene solo coma → "1,37" → "1.37"
        // Si tiene solo punto → "1.37" → "1.37"  (invariant, no ambiguo)
        string normalized = raw.Trim();

        bool hasComma = normalized.Contains(',');
        bool hasDot   = normalized.Contains('.');

        if (hasComma && hasDot)
        {
            // Formato tipo "1.234,56": punto = miles, coma = decimal
            normalized = normalized.Replace(".", "").Replace(",", ".");
        }
        else if (hasComma)
        {
            // Solo coma → separador decimal
            normalized = normalized.Replace(",", ".");
        }
        // Solo punto o ninguno → ya es formato invariante

        if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
        {
            bindingContext.Result = ModelBindingResult.Success(result);
        }
        else
        {
            bindingContext.ModelState.AddModelError(bindingContext.ModelName, "Ingresá un monto válido.");
        }

        return Task.CompletedTask;
    }
}
