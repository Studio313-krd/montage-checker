namespace MontageMonitor.Server.Infrastructure.Security;

public static class PasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 200;

    public static string[] Validate(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return ["Пароль обязателен."];
        }

        var errors = new List<string>();
        if (password.Length is < MinimumLength or > MaximumLength)
        {
            errors.Add($"Пароль должен содержать от {MinimumLength} до {MaximumLength} символов.");
        }

        if (!password.Any(char.IsUpper))
        {
            errors.Add("Пароль должен содержать заглавную букву.");
        }

        if (!password.Any(char.IsLower))
        {
            errors.Add("Пароль должен содержать строчную букву.");
        }

        if (!password.Any(char.IsDigit))
        {
            errors.Add("Пароль должен содержать цифру.");
        }

        if (!password.Any(character => !char.IsLetterOrDigit(character)))
        {
            errors.Add("Пароль должен содержать специальный символ.");
        }

        return [.. errors];
    }
}
