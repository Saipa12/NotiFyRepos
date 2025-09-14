namespace ConsoleApp9.Services;

public record Settings(
string BotToken,
string Timezone,
string IntervalUnit,
int IntervalValue,
int LocalIntervalSeconds,
int? MaxRuns,
int? SendSecond,
int? SendMinute,
int? SendHour,
int? SendWeekday,
int? SendMonthDay,
int? SendMonth,
string DataDir,
string InitialMessage,
string FollowupMessage,
long? AdminChatId)
{
        public static Settings LoadFromEnv() => new Settings(
        BotToken: Env("BOT_TOKEN", required: true)!,
        Timezone: Env("TIMEZONE", "Europe/Warsaw"),
        IntervalUnit: Env("INTERVAL_UNIT", "month").ToLowerInvariant(),
        IntervalValue: int.Parse(Env("INTERVAL_VALUE", "1")),
        LocalIntervalSeconds: int.Parse(Env("LOCAL_INTERVAL_SECONDS", "60")),
        MaxRuns: TryInt(Env("MAX_RUNS", null)),
        SendSecond: TryInt(Env("SEND_SECOND", null)),
        SendMinute: TryInt(Env("SEND_MINUTE", null)),
        SendHour: TryInt(Env("SEND_HOUR", null)),
        SendWeekday: TryInt(Env("SEND_WEEKDAY", null)),
        SendMonthDay: TryInt(Env("SEND_MONTH_DAY", null)),
        SendMonth: TryInt(Env("SEND_MONTH", null)),
	DataDir: Env("DATA_DIR", "./data"),
	InitialMessage: Env("INITIAL_MESSAGE", "Напоминание об оплате за {0:MMMM yyyy}. Пожалуйста, оплатите и нажмите кнопку ниже."),
	FollowupMessage: Env("FOLLOWUP_MESSAGE", "Напоминание: оплата за {0:MMMM yyyy} всё ещё не подтверждена. Нажмите ‘Оплатил ✅’ после оплаты."),
	AdminChatId: TryLong(Env("ADMIN_CHAT_ID", null))
	);

	static string? Env(string key, string? def = null, bool required = false)
	{
		var v = Environment.GetEnvironmentVariable(key);
		if (string.IsNullOrWhiteSpace(v))
		{
			if (required) throw new InvalidOperationException($"Environment variable {key} is required");
			return def;
		}
		return v;
	}
	static int? TryInt(string? s) => int.TryParse(s, out var x) ? x : null;
	static long? TryLong(string? s) => long.TryParse(s, out var x) ? x : null;
}