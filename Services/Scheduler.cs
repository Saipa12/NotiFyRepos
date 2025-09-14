using ConsoleApp9.Services;
using System.Globalization;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

public class Scheduler
{
	private readonly ITelegramBotClient _bot;
	private readonly JsonStore<UsersRoot> _users;
	private readonly JsonStore<NotificationsRoot> _notifs;
	private readonly JsonStore<MetaRoot> _meta;
	private readonly Settings _cfg;

	private TimeZoneInfo TZ => TimeZoneInfo.FindSystemTimeZoneById(_cfg.Timezone);

	public Scheduler(ITelegramBotClient bot, JsonStore<UsersRoot> users, JsonStore<NotificationsRoot> notifs, JsonStore<MetaRoot> meta, Settings cfg)
	{ _bot = bot; _users = users; _notifs = notifs; _meta = meta; _cfg = cfg; }

        public async Task RunAsync(CancellationToken ct)
        {
                int runs = 0;
                var interval = TimeSpan.FromSeconds(Math.Max(1, _cfg.LocalIntervalSeconds));
                while (!ct.IsCancellationRequested && (_cfg.MaxRuns == null || runs < _cfg.MaxRuns))
                {
                        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TZ);
                        var next = nowLocal + interval;
                        Console.WriteLine($"[Scheduler] Next trigger: {next:yyyy-MM-dd HH:mm:ss zzz}");
                        try { await Task.Delay(interval, ct); } catch { }
                        if (ct.IsCancellationRequested) break;
                        try { await OnTickAsync(ct); } catch (Exception ex) { Console.WriteLine($"[Scheduler] Tick error: {ex}"); }
                        runs++;
                }
        }

        private async Task OnTickAsync(CancellationToken ct)
        {
                var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TZ);
                var periodKey = CurrentPeriodKey(nowLocal);

                var meta = await _meta.LoadAsync();
                bool initial = false;
                if (!string.Equals(meta.LastPeriodKey, periodKey, StringComparison.Ordinal))
                {
                        var ur = await _users.LoadAsync();
                        foreach (var u in ur.Users) u.NotifyAgain = true; // open new cycle
                        await _users.SaveAsync(ur);
                        meta.LastPeriodKey = periodKey;
                        await _meta.SaveAsync(meta);
                        Console.WriteLine($"[Scheduler] New period started: {periodKey}. Reset notifyAgain=true.");
                        initial = true;
                }

                await SendRemindersAsync(nowLocal, initial, ct);
        }

        private async Task SendRemindersAsync(DateTimeOffset nowLocal, bool initial, CancellationToken ct)
        {
                var ur = await _users.LoadAsync();
                var nr = await _notifs.LoadAsync();

                var periodDate = new DateTime(nowLocal.Year, nowLocal.Month, 1); // used for {0:MMMM yyyy}
                var template = initial ? _cfg.InitialMessage : _cfg.FollowupMessage;
                var text = string.Format(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), template, periodDate);
		var keyboard = new InlineKeyboardMarkup(new[]
		{
new [] { InlineKeyboardButton.WithCallbackData("Оплатил ✅", "paid") },
new [] { InlineKeyboardButton.WithCallbackData("Отписаться", "unsubscribe") }
});

		foreach (var u in ur.Users.Where(x => x.IsActive && x.NotifyAgain))
		{
			try
			{
				await _bot.SendTextMessageAsync(u.Id, text, replyMarkup: keyboard, cancellationToken: ct);
				nr.Notifications.Add(new Notification { UserId = u.Id, SentAt = DateTimeOffset.UtcNow, Message = text, Status = NotificationStatus.Sent });
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Send] Failed to {u.Id}: {ex.Message}");
				nr.Notifications.Add(new Notification { UserId = u.Id, SentAt = DateTimeOffset.UtcNow, Message = text, Status = NotificationStatus.Failed });
			}
		}

		await _notifs.SaveAsync(nr);
	}

        private string CurrentPeriodKey(DateTimeOffset nowLocal)
        {
                var val = Math.Max(1, _cfg.IntervalValue);
                return _cfg.IntervalUnit switch
                {
                        "year" => (((nowLocal.Year - 1) / val) * val + 1).ToString(),
                        "month" =>
                                {
                                        int startMonth = ((nowLocal.Month - 1) / val) * val + 1;
                                        return $"{nowLocal.Year:D4}-{startMonth:D2}";
                                },
                        "week" =>
                                {
                                        int isoYear = ISOWeek.GetYear(nowLocal.Date);
                                        int isoWeek = ISOWeek.GetWeekOfYear(nowLocal.Date);
                                        int startWeek = ((isoWeek - 1) / val) * val + 1;
                                        return $"{isoYear}-W{startWeek:D2}";
                                },
                        "day" =>
                                {
                                        var start = nowLocal.Date.AddDays(-((nowLocal.Day - 1) % val));
                                        return $"{start:yyyy-MM-dd}";
                                },
                        "hour" =>
                                {
                                        int startHour = (nowLocal.Hour / val) * val;
                                        var start = new DateTimeOffset(nowLocal.Year, nowLocal.Month, nowLocal.Day, startHour, 0, 0, nowLocal.Offset);
                                        return $"{start:yyyy-MM-dd HH}";
                                },
                        "minute" =>
                                {
                                        int startMinute = (nowLocal.Minute / val) * val;
                                        var start = new DateTimeOffset(nowLocal.Year, nowLocal.Month, nowLocal.Day, nowLocal.Hour, startMinute, 0, nowLocal.Offset);
                                        return $"{start:yyyy-MM-dd HH:mm}";
                                },
                        _ => $"{nowLocal:yyyy-MM}"
                };
        }
}