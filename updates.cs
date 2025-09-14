using ConsoleApp9.Services;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

public class UpdateHandler
{
	private readonly ITelegramBotClient _bot;
	private readonly JsonStore<UsersRoot> _users;
	private readonly JsonStore<NotificationsRoot> _notifs;
	private readonly JsonStore<MetaRoot> _meta;
	private readonly Settings _cfg;

	public UpdateHandler(ITelegramBotClient bot, JsonStore<UsersRoot> users, JsonStore<NotificationsRoot> notifs, JsonStore<MetaRoot> meta, Settings cfg)
	{ _bot = bot; _users = users; _notifs = notifs; _meta = meta; _cfg = cfg; }

	public async Task HandleUpdateAsync(ITelegramBotClient _, Update u, CancellationToken ct)
	{
		try
		{
			if (u.Type == UpdateType.Message && u.Message!.Chat.Type == ChatType.Private)
			{
				var m = u.Message!; var chatId = m.Chat.Id; var text = m.Text ?? string.Empty;
				var ur = await _users.LoadAsync();
				var user = ur.Users.FirstOrDefault(x => x.Id == chatId) ?? new User
				{
					Id = chatId,
					IsActive = true,
					NotifyAgain = true,
					Info = new UserInfo { Username = m.From?.Username, FirstName = m.From?.FirstName, LastName = m.From?.LastName }
				};
				if (!ur.Users.Any(x => x.Id == chatId)) ur.Users.Add(user);

				if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
				{
					user.IsActive = true;
					await _bot.SendTextMessageAsync(chatId, "Подписка активна. Команды: /status, /stop", cancellationToken: ct);
				}
				else if (text.StartsWith("/stop", StringComparison.OrdinalIgnoreCase))
				{
					user.IsActive = false;
					await _bot.SendTextMessageAsync(chatId, "Вы отписаны. /start — чтобы вернуться.", cancellationToken: ct);
				}
				else if (text.StartsWith("/status", StringComparison.OrdinalIgnoreCase))
				{
					var sb = new StringBuilder();
					sb.AppendLine($"Активен: {user.IsActive}");
					sb.AppendLine($"NotifyAgain (в текущем периоде): {user.NotifyAgain}");
					await _bot.SendTextMessageAsync(chatId, sb.ToString(), cancellationToken: ct);
				}
				else
				{
					await _bot.SendTextMessageAsync(chatId, "Команды: /start, /stop, /status", cancellationToken: ct);
				}

				await _users.SaveAsync(ur);
			}
			else if (u.Type == UpdateType.CallbackQuery)
			{
				var cq = u.CallbackQuery!;
				var chatId = cq.Message!.Chat.Id;
				var data = cq.Data ?? string.Empty;

				if (data == "paid")
				{
					var ur = await _users.LoadAsync();
					var user = ur.Users.FirstOrDefault(x => x.Id == chatId);
					if (user is not null)
					{
						user.NotifyAgain = false; // stop further reminders within current period
						await _users.SaveAsync(ur);

						var nr = await _notifs.LoadAsync();
						nr.Notifications.Add(new Notification { UserId = chatId, SentAt = DateTimeOffset.UtcNow, Message = "User confirmed", Status = NotificationStatus.Confirmed });
						await _notifs.SaveAsync(nr);

						await _bot.AnswerCallbackQueryAsync(cq.Id, "Оплата подтверждена. Спасибо!", cancellationToken: ct);
						await _bot.EditMessageReplyMarkupAsync(chatId, cq.Message!.MessageId, replyMarkup: null, cancellationToken: ct);
						await _bot.SendTextMessageAsync(chatId, "✔️ Спасибо! В этом периоде больше не напомню.", cancellationToken: ct);
					}
				}
				else if (data == "unsubscribe")
				{
					var ur = await _users.LoadAsync();
					var user = ur.Users.FirstOrDefault(x => x.Id == chatId);
					if (user is not null)
					{
						user.IsActive = false;
						await _users.SaveAsync(ur);
						await _bot.AnswerCallbackQueryAsync(cq.Id, "Вы отписаны.", cancellationToken: ct);
						await _bot.SendTextMessageAsync(chatId, "Вы отписались от напоминаний. /start — чтобы вернуться.", cancellationToken: ct);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Update error: {ex}");
		}
	}

	public Task HandleErrorAsync(ITelegramBotClient _, Exception ex, CancellationToken ct)
	{
		var msg = ex switch
		{
			ApiRequestException apiEx => $"Telegram API Error: [{apiEx.ErrorCode}] {apiEx.Message}",
			_ => ex.ToString()
		};
		Console.WriteLine(msg);
		return Task.CompletedTask;
	}
}