using System;

namespace Models
{
    public class UserSession
    {
        public int Id { get; set; }
        public long UserId { get; set; }
        public string State { get; set; } = string.Empty; // например "ChoosingDate"
        public string? Payload { get; set; } // JSON или текстовое содержимое
        public DateTime UpdatedAt { get; set; }
    }
}
