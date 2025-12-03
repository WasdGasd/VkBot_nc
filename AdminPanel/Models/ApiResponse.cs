namespace AdminPanel.Models
{
    public class ApiResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public object? Data { get; set; }
        public DateTime Timestamp { get; set; }

        public ApiResponse(bool success, string message, object? data = null)
        {
            Success = success;
            Message = message;
            Data = data;
            Timestamp = DateTime.UtcNow;
        }
    }
}