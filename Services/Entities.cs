using System.Text.Json.Serialization;

public class UsersRoot
{ public List<User> Users { get; set; } = new(); }

public class NotificationsRoot
{ public List<Notification> Notifications { get; set; } = new(); }

public class MetaRoot
{ public string? LastPeriodKey { get; set; } }

public class User
{
	public long Id { get; set; } // Telegram chat/user id
	public bool IsActive { get; set; } = true; // Subscribe to notifications
	public bool NotifyAgain { get; set; } = true; // If true -> keep sending within current period
	public UserInfo Info { get; set; } = new(); // Arbitrary info
}

public class UserInfo
{
	public string? Username { get; set; }
	public string? FirstName { get; set; }
	public string? LastName { get; set; }
	public string? Notes { get; set; }
}

public enum NotificationStatus
{ Sent, Failed, Confirmed }

public class Notification
{
	public long UserId { get; set; }
	public DateTimeOffset SentAt { get; set; }
	public string Message { get; set; } = string.Empty;

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public NotificationStatus Status { get; set; } = NotificationStatus.Sent;
}