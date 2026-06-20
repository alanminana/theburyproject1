using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;

namespace TheBuryProject.Validation
{
    /// <summary>
    /// Atributos de validación específicos para datos argentinos (DNI, CUIL/CUIT,
    /// teléfono, código postal y nombres de persona).
    /// Reglas acordadas con el negocio:
    ///  - DNI: exactamente 8 dígitos numéricos.
    ///  - CUIL/CUIT: 11 dígitos con dígito verificador válido (módulo 11). Formato XX-DNI-X,
    ///    donde los 8 del medio son el mismo DNI de la persona.
    ///  - Nombre/Apellido: solo letras (acentos, ñ, espacios, apóstrofo, guion), mínimo configurable.
    /// Todos consideran null/vacío como válido para no pisar a [Required]; usar [Required]
    /// cuando el campo sea obligatorio. El backend es la autoridad: validación server-side.
    /// </summary>
    internal static class ValidationHelpers
    {
        public static string OnlyDigits(string value) =>
            new string(value.Where(char.IsDigit).ToArray());

        public static ValidationResult Fail(string message, ValidationContext ctx) =>
            string.IsNullOrEmpty(ctx.MemberName)
                ? new ValidationResult(message)
                : new ValidationResult(message, new[] { ctx.MemberName! });
    }

    /// <summary>
    /// Solo letras (incluye acentos y ñ), espacios internos, apóstrofo, punto y guion.
    /// Debe empezar y terminar con letra. Mínimo configurable (default 2).
    /// Pensado para nombres y apellidos de PERSONAS (no para razón social ni nombres de catálogo).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class SoloLetrasAttribute : ValidationAttribute
    {
        public int MinLength { get; set; } = 2;

        // \p{L} = letra Unicode (incluye acentos y ñ), \p{M} = marcas diacríticas combinantes.
        private static readonly Regex Pattern = new(
            @"^\p{L}[\p{L}\p{M}\s'’.\-]*\p{L}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public SoloLetrasAttribute()
            : base("El campo {0} solo puede contener letras y debe tener al menos {1} caracteres.")
        {
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string raw || string.IsNullOrWhiteSpace(raw))
                return ValidationResult.Success;

            var s = raw.Trim();
            if (s.Length < MinLength || !Pattern.IsMatch(s))
                return ValidationHelpers.Fail(FormatErrorMessage(validationContext.DisplayName), validationContext);

            return ValidationResult.Success;
        }

        public override string FormatErrorMessage(string name) =>
            string.Format(ErrorMessageString, name, MinLength);
    }

    /// <summary>
    /// DNI argentino: exactamente 8 dígitos numéricos.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class DniArgentinoAttribute : ValidationAttribute
    {
        private static readonly Regex Pattern = new(@"^\d{8}$", RegexOptions.Compiled);

        public DniArgentinoAttribute()
            : base("El campo {0} debe ser un DNI de exactamente 8 dígitos numéricos.")
        {
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string raw || string.IsNullOrWhiteSpace(raw))
                return ValidationResult.Success;

            return Pattern.IsMatch(raw.Trim())
                ? ValidationResult.Success
                : ValidationHelpers.Fail(FormatErrorMessage(validationContext.DisplayName), validationContext);
        }
    }

    /// <summary>
    /// CUIL/CUIT argentino: 11 dígitos con dígito verificador válido (módulo 11).
    /// Acepta entrada con o sin guiones/espacios (se normaliza a dígitos).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class CuilCuitArgentinoAttribute : ValidationAttribute
    {
        private static readonly string[] PrefijosValidos = { "20", "23", "24", "27", "30", "33", "34" };
        private static readonly int[] Multiplicadores = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };

        public CuilCuitArgentinoAttribute()
            : base("El campo {0} no es un CUIL/CUIT válido: deben ser 11 dígitos con dígito verificador correcto.")
        {
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string raw || string.IsNullOrWhiteSpace(raw))
                return ValidationResult.Success;

            return EsValido(ValidationHelpers.OnlyDigits(raw))
                ? ValidationResult.Success
                : ValidationHelpers.Fail(FormatErrorMessage(validationContext.DisplayName), validationContext);
        }

        /// <summary>
        /// Valida 11 dígitos, prefijo de tipo conocido y dígito verificador (módulo 11).
        /// </summary>
        public static bool EsValido(string? soloDigitos)
        {
            if (string.IsNullOrEmpty(soloDigitos) || soloDigitos.Length != 11 || !soloDigitos.All(char.IsDigit))
                return false;

            if (!PrefijosValidos.Contains(soloDigitos.Substring(0, 2)))
                return false;

            int suma = 0;
            for (int i = 0; i < 10; i++)
                suma += (soloDigitos[i] - '0') * Multiplicadores[i];

            int resto = suma % 11;
            int verificador = 11 - resto;
            if (verificador == 11) verificador = 0;
            else if (verificador == 10) verificador = 9;

            return verificador == (soloDigitos[10] - '0');
        }
    }

    /// <summary>
    /// Valida que el CUIL/CUIT contenga al DNI de la persona en el medio (formato XX-DNI-X).
    /// Solo se aplica cuando el campo referenciado es un DNI de 8 dígitos.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class CuilCoincideConDniAttribute : ValidationAttribute
    {
        private readonly string _dniPropertyName;

        public CuilCoincideConDniAttribute(string dniPropertyName)
            : base("El CUIL/CUIT debe contener el DNI en el medio (formato XX-DNI-X).")
        {
            _dniPropertyName = dniPropertyName;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string rawCuil || string.IsNullOrWhiteSpace(rawCuil))
                return ValidationResult.Success;

            var cuilDigits = ValidationHelpers.OnlyDigits(rawCuil);
            if (cuilDigits.Length != 11)
                return ValidationResult.Success; // el formato lo valida CuilCuitArgentino

            var dniProperty = validationContext.ObjectType.GetProperty(_dniPropertyName);
            if (dniProperty?.GetValue(validationContext.ObjectInstance) is not string rawDni
                || string.IsNullOrWhiteSpace(rawDni))
                return ValidationResult.Success;

            var dniDigits = ValidationHelpers.OnlyDigits(rawDni);
            if (dniDigits.Length != 8)
                return ValidationResult.Success; // solo aplica cuando hay un DNI real de 8 dígitos

            return cuilDigits.Substring(2, 8) == dniDigits
                ? ValidationResult.Success
                : ValidationHelpers.Fail(FormatErrorMessage(validationContext.DisplayName), validationContext);
        }
    }

    /// <summary>
    /// Valida un número de documento argentino según el tipo indicado en otra propiedad:
    ///  - DNI  → exactamente 8 dígitos numéricos.
    ///  - CUIL/CUIT → 11 dígitos con verificador válido.
    /// Otros tipos (LE, LC, Pasaporte, etc.) no se validan estrictamente.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class DocumentoArgentinoAttribute : ValidationAttribute
    {
        private readonly string _tipoDocumentoPropertyName;

        public DocumentoArgentinoAttribute(string tipoDocumentoPropertyName)
        {
            _tipoDocumentoPropertyName = tipoDocumentoPropertyName;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string rawNumero || string.IsNullOrWhiteSpace(rawNumero))
                return ValidationResult.Success;

            var tipoProperty = validationContext.ObjectType.GetProperty(_tipoDocumentoPropertyName);
            var tipo = (tipoProperty?.GetValue(validationContext.ObjectInstance) as string ?? "DNI")
                .Trim().ToUpperInvariant();

            var numero = rawNumero.Trim();
            var digits = ValidationHelpers.OnlyDigits(numero);
            var display = validationContext.DisplayName;

            if (tipo == "DNI")
            {
                if (digits.Length != 8 || digits.Length != numero.Length)
                    return ValidationHelpers.Fail(
                        $"El {display} (DNI) debe tener exactamente 8 dígitos numéricos.", validationContext);
            }
            else if (tipo is "CUIL" or "CUIT")
            {
                if (!CuilCuitArgentinoAttribute.EsValido(digits))
                    return ValidationHelpers.Fail(
                        $"El {display} ({tipo}) debe tener 11 dígitos con dígito verificador válido.", validationContext);
            }

            return ValidationResult.Success;
        }
    }

    /// <summary>
    /// Teléfono argentino: solo dígitos, espacios, +, guiones y paréntesis,
    /// con una cantidad de dígitos dentro de un rango razonable.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class TelefonoArgentinoAttribute : ValidationAttribute
    {
        private static readonly Regex FormatoPermitido = new(@"^[\d\s+().\-]+$", RegexOptions.Compiled);

        public int MinDigits { get; set; } = 7;
        public int MaxDigits { get; set; } = 14;

        public TelefonoArgentinoAttribute()
            : base("El campo {0} debe ser un teléfono válido (entre {1} y {2} dígitos, solo números y los símbolos + - ( ) ).")
        {
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string raw || string.IsNullOrWhiteSpace(raw))
                return ValidationResult.Success;

            var s = raw.Trim();
            var cantidadDigitos = s.Count(char.IsDigit);
            if (!FormatoPermitido.IsMatch(s) || cantidadDigitos < MinDigits || cantidadDigitos > MaxDigits)
                return ValidationHelpers.Fail(FormatErrorMessage(validationContext.DisplayName), validationContext);

            return ValidationResult.Success;
        }

        public override string FormatErrorMessage(string name) =>
            string.Format(ErrorMessageString, name, MinDigits, MaxDigits);
    }

    /// <summary>
    /// Código postal argentino: 4 dígitos (formato viejo) o CPA
    /// (1 letra + 4 dígitos + 3 letras, ej. C1234ABC).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class CodigoPostalArgentinoAttribute : ValidationAttribute
    {
        private static readonly Regex Pattern = new(
            @"^(\d{4}|[A-Za-z]\d{4}[A-Za-z]{3})$", RegexOptions.Compiled);

        public CodigoPostalArgentinoAttribute()
            : base("El campo {0} debe ser un código postal válido: 4 dígitos o formato CPA (ej. C1234ABC).")
        {
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string raw || string.IsNullOrWhiteSpace(raw))
                return ValidationResult.Success;

            return Pattern.IsMatch(raw.Trim())
                ? ValidationResult.Success
                : ValidationHelpers.Fail(FormatErrorMessage(validationContext.DisplayName), validationContext);
        }
    }
}
