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
		while (!ct.IsCancellationRequested)
		{
			var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TZ);
			var next = NextTrigger(nowLocal);
			Console.WriteLine($"[Scheduler] Next trigger: {next:yyyy-MM-dd HH:mm:ss zzz}");
			try { await Task.Delay(next - nowLocal, ct); } catch { }
			if (ct.IsCancellationRequested) break;
			try { await OnTickAsync(ct); } catch (Exception ex) { Console.WriteLine($"[Scheduler] Tick error: {ex}"); }
		}
	}

	private async Task OnTickAsync(CancellationToken ct)
	{
		var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TZ);
		var periodKey = CurrentPeriodKey(nowLocal);

		// Detect new period -> reset NotifyAgain=true for all users
		var meta = await _meta.LoadAsync();
		if (!string.Equals(meta.LastPeriodKey, periodKey, StringComparison.Ordinal))
		{
			var ur = await _users.LoadAsync();
			foreach (var u in ur.Users) u.NotifyAgain = true; // open new cycle
			await _users.SaveAsync(ur);
			meta.LastPeriodKey = periodKey;
			await _meta.SaveAsync(meta);
			Console.WriteLine($"[Scheduler] New period started: {periodKey}. Reset notifyAgain=true.");
		}

		// Send messages to users with NotifyAgain=true and IsActive=true
		await SendRemindersAsync(nowLocal, ct);
	}

	private async Task SendRemindersAsync(DateTimeOffset nowLocal, CancellationToken ct)
	{
		var ur = await _users.LoadAsync();
		var nr = await _notifs.LoadAsync();

		var periodDate = new DateTime(nowLocal.Year, nowLocal.Month, 1); // used for {0:MMMM yyyy}
		var text = string.Format(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), _cfg.InitialMessage, periodDate);
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

	private DateTimeOffset NextTrigger(DateTimeOffset nowLocal)
	{
		var unit = _cfg.IntervalUnit;
		var val = Math.Max(1, _cfg.IntervalValue);
		var next = unit switch
		{
			"year" => NextYearly(nowLocal),
			"month" => NextMonthly(nowLocal),
			"week" => NextWeekly(nowLocal),
			"day" => NextDaily(nowLocal),
			"hour" => NextHourly(nowLocal),
			"minute" => NextMinutely(nowLocal),
			_ => NextMonthly(nowLocal)
		};

		// Add (val-1) intervals to support value>1
		for (int i = 1; i < val; i++)
		{
			next = unit switch
			{
				"year" => next.AddYears(1),
				"month" => next.AddMonths(1),
				"week" => next.AddDays(7),
				"day" => next.AddDays(1),
				"hour" => next.AddHours(1),
				"minute" => next.AddMinutes(1),
				_ => next
			};
		}
		return next;
	}

	private DateTimeOffset NextYearly(DateTimeOffset now)
	{
		int month = Clamp(_cfg.SendMonth ?? 1, 1, 12);
		int day = Clamp(_cfg.SendMonthDay ?? 1, 1, DateTime.DaysInMonth(now.Year, month));
		int h = Clamp(_cfg.SendHour ?? 9, 0, 23);
		int m = Clamp(_cfg.SendMinute ?? 0, 0, 59);
		int s = Clamp(_cfg.SendSecond ?? 0, 0, 59);
		var candidate = new DateTimeOffset(now.Year, month, day, h, m, s, now.Offset);
		return now < candidate ? candidate : new DateTimeOffset(now.Year + 1, Clamp(month, 1, 12), Clamp(day, 1, DateTime.DaysInMonth(now.Year + 1, month)), h, m, s, now.Offset);
	}

	private DateTimeOffset NextMonthly(DateTimeOffset now)
	{
		int day = Clamp(_cfg.SendMonthDay ?? 1, 1, DateTime.DaysInMonth(now.Year, now.Month));
		int h = Clamp(_cfg.SendHour ?? 9, 0, 23);
		int m = Clamp(_cfg.SendMinute ?? 0, 0, 59);
		int s = Clamp(_cfg.SendSecond ?? 0, 0, 59);
		var candidate = new DateTimeOffset(now.Year, now.Month, Clamp(day, 1, DateTime.DaysInMonth(now.Year, now.Month)), h, m, s, now.Offset);
		if (now < candidate) return candidate;
		var n = now.AddMonths(1);
		day = Clamp(day, 1, DateTime.DaysInMonth(n.Year, n.Month));
		return new DateTimeOffset(n.Year, n.Month, day, h, m, s, now.Offset);
	}

	private DateTimeOffset NextWeekly(DateTimeOffset now)
	{
		int wd = Clamp(_cfg.SendWeekday ?? 1, 1, 7); // 1=Mon..7=Sun
		int h = Clamp(_cfg.SendHour ?? 9, 0, 23);
		int m = Clamp(_cfg.SendMinute ?? 0, 0, 59);
		int s = Clamp(_cfg.SendSecond ?? 0, 0, 59);

		int currentWd = ((int)now.DayOfWeek + 6) % 7 + 1; // convert .NET Sun=0 -> Mon=1..Sun=7
		int deltaDays = wd - currentWd;
		if (deltaDays < 0 || (deltaDays == 0 && now.TimeOfDay >= new TimeSpan(h, m, s)))
			deltaDays += 7;

		var targetDate = now.Date.AddDays(deltaDays);
		return new DateTimeOffset(targetDate.Year, targetDate.Month, targetDate.Day, h, m, s, now.Offset);
	}

	private DateTimeOffset NextDaily(DateTimeOffset now)
	{
		int h = Clamp(_cfg.SendHour ?? 9, 0, 23);
		int m = Clamp(_cfg.SendMinute ?? 0, 0, 59);
		int s = Clamp(_cfg.SendSecond ?? 0, 0, 59);
		var today = new DateTimeOffset(now.Year, now.Month, now.Day, h, m, s, now.Offset);
		return now < today ? today : today.AddDays(1);
	}

	private DateTimeOffset NextHourly(DateTimeOffset now)
	{
		int min = Clamp(_cfg.SendMinute ?? 0, 0, 59);
		int sec = Clamp(_cfg.SendSecond ?? 0, 0, 59);
		var thisHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, min, sec, now.Offset);
		return now < thisHour ? thisHour : thisHour.AddHours(1);
	}

	private DateTimeOffset NextMinutely(DateTimeOffset now)
	{
		int sec = Clamp(_cfg.SendSecond ?? 0, 0, 59);
		var thisMinute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, sec, now.Offset);
		return now < thisMinute ? thisMinute : thisMinute.AddMinutes(1);
	}

	private static int Clamp(int v, int lo, int hi) => Math.Min(Math.Max(v, lo), hi);

	private string CurrentPeriodKey(DateTimeOffset nowLocal)
	{
		return _cfg.IntervalUnit switch
		{
			"year" => nowLocal.Year.ToString(),
			"month" => $"{nowLocal:yyyy-MM}",
			"week" => $"{ISOWeek.GetYear(nowLocal.Date)}-W{ISOWeek.GetWeekOfYear(nowLocal.Date):D2}",
			"day" => $"{nowLocal:yyyy-MM-dd}",
			"hour" => $"{nowLocal:yyyy-MM-dd HH}",
			"minute" => $"{nowLocal:yyyy-MM-dd HH:mm}",
			_ => $"{nowLocal:yyyy-MM}"
		};
	}
}