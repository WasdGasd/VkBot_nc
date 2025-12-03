using AdminPanel.Controllers;

namespace AdminPanel.Services
{
    public class CommandValidationService
    {
        public ValidationResult ValidateCommand(CreateCommandRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return ValidationResult.Fail("Название команды обязательно");
            }

            if (request.Name.Length > 100)
            {
                return ValidationResult.Fail("Название команды не должно превышать 100 символов");
            }

            if (string.IsNullOrWhiteSpace(request.Response))
            {
                return ValidationResult.Fail("Текст ответа обязателен");
            }

            if (request.Response.Length > 4000)
            {
                return ValidationResult.Fail("Текст ответа не должен превышать 4000 символов");
            }

            return ValidationResult.Success();
        }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }

        public static ValidationResult Success() => new() { IsValid = true };
        public static ValidationResult Fail(string message) => new() { IsValid = false, ErrorMessage = message };
    }
}