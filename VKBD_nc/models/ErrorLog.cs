using System.Text.Json;

namespace VKBD_nc.Models
{
	public class ErrorLog
	{
		public DateTime Timestamp { get; set; }
		public string ErrorMessage { get; set; } = string.Empty;
		public long? UserId { get; set; }
		public string? Command { get; set; }
		public string? AdditionalData { get; set; }
	}
}
