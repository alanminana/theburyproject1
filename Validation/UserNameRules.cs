namespace TheBuryProject.Validation;

/// <summary>
/// Regla única del nombre de usuario (alta y edición): 3 a 50 caracteres entre letras sin tildes, números y . _ - @ +,
/// con al menos una letra o un número (antes aceptaba "..." o "---", que no identifican a nadie).
/// </summary>
public static class UserNameRules
{
    public const string Pattern = @"^(?=.*[A-Za-z0-9])[A-Za-z0-9._@+-]{3,50}$";

    public const string Message = "El nombre de usuario debe tener entre 3 y 50 caracteres: letras sin tildes, números y . _ - @ + (sin espacios), con al menos una letra o un número.";
}
