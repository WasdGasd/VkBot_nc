using System.Text.Json.Serialization;

namespace VKBD_nc.Models
{
	// Модель для ответа LongPoll сервера
	public class LongPollServerResponse
	{
		[JsonPropertyName("response")]
		public LongPollServer? Response { get; set; }
	}

	public class LongPollServer
	{
		[JsonPropertyName("key")]
		public string Key { get; set; } = string.Empty;

		[JsonPropertyName("server")]
		public string Server { get; set; } = string.Empty;

		[JsonPropertyName("ts")]
		public string Ts { get; set; } = string.Empty;
	}

	// Модель для обновлений LongPoll
	public class LongPollUpdate
	{
		[JsonPropertyName("ts")]
		public string Ts { get; set; } = string.Empty;

		[JsonPropertyName("failed")]
		public int? Failed { get; set; }

		[JsonPropertyName("updates")]
		public UpdateItem[]? Updates { get; set; }
	}

	// Модель элемента обновления
	public class UpdateItem
	{
		[JsonPropertyName("type")]
		public string Type { get; set; } = string.Empty;

		[JsonPropertyName("object")]
		public UpdateObject? Object { get; set; }

		[JsonPropertyName("group_id")]
		public long GroupId { get; set; }
	}

	// Объект обновления
	public class UpdateObject
	{
		[JsonPropertyName("user_id")]
		public long? UserId { get; set; }

		[JsonPropertyName("message")]
		public MessageItem? Message { get; set; }
	}

	// Модель сообщения
	public class MessageItem
	{
		[JsonPropertyName("id")]
		public long Id { get; set; }

		[JsonPropertyName("from_id")]
		public long FromId { get; set; }

		[JsonPropertyName("text")]
		public string Text { get; set; } = string.Empty;

		[JsonPropertyName("peer_id")]
		public long PeerId { get; set; }

		[JsonPropertyName("date")]
		public long Date { get; set; }
	}
}