using ConsoleApp9.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

var cfg = Settings.LoadFromEnv();
Directory.CreateDirectory(cfg.DataDir);

var usersStore = new JsonStore<UsersRoot>(Path.Combine(cfg.DataDir, "users.json"), new UsersRoot());
var notifStore = new JsonStore<NotificationsRoot>(Path.Combine(cfg.DataDir, "notifications.json"), new NotificationsRoot());
var metaStore = new JsonStore<MetaRoot>(Path.Combine(cfg.DataDir, "meta.json"), new MetaRoot());

var bot = new TelegramBotClient(cfg.BotToken);
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var me = await bot.GetMeAsync(cts.Token);
Console.WriteLine($"Bot started: @{me.Username}");

var handler = new UpdateHandler(bot, usersStore, notifStore, metaStore, cfg);
var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };
bot.StartReceiving(handler.HandleUpdateAsync, handler.HandleErrorAsync, receiverOptions, cts.Token);

// Start scheduler loop
_ = Task.Run(() => new Scheduler(bot, usersStore, notifStore, metaStore, cfg).RunAsync(cts.Token));

await Task.Delay(Timeout.Infinite, cts.Token);