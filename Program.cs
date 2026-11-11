using System.Text.Json;
using System.IO;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

// 1. Сначала загружаем настройки из файла .env (токен)
if (File.Exists(".env"))
{
foreach (var line in File.ReadAllLines(".env"))
{
var parts = line.Split('=', 2);
if (parts.Length == 2)
Environment.SetEnvironmentVariable(parts[0], parts[1]);
}
}

// 2. Берем токен из переменной
string token = Environment.GetEnvironmentVariable("BOT_TOKEN");

var bot = new TelegramBotClient(token);

// 3. Часовой пояс
var timeZone = TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");
var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);

// 4. Загружаем напоминания из файла reminders.json
string filePath = "reminders.json";
var reminders = new List<Reminder>();
if (File.Exists(filePath))
{
string json = File.ReadAllText(filePath);
reminders = JsonSerializer.Deserialize<List<Reminder>>(json) ?? new List<Reminder>();
Console.WriteLine($"Загружено {reminders.Count} напоминаний из файла.");
}

// 5. Словари для состояний
var userStates = new Dictionary<long, string>();
var userTexts = new Dictionary<long, string>();

try

{

    bot.StartReceiving(

    async (botClient, update, token) =>
    {
        if (update.Message is Message message && message.Text is string text)
        {
            Console.WriteLine($"Пришло: {text}");

            if (text == "/start")
            {
                await botClient.SendMessage(message.Chat, "Здарова\nСписок команд\n/start - запуск\n/remind - задать напоминание\n/list - список напоминаний");
            }
            else if (text == "/remind")
            {
                // Ставим флажок, что ждем текст
                userStates[message.Chat.Id] = "waiting_text";

                // Задаем вопрос
                await botClient.SendMessage(message.Chat, "Чё напомнить?");
            }
            else if (text == "/list")
            {
                // 1. Ищем напоминания только этого пользователя
                var myReminders = reminders.Where(r => r.ChatId == message.Chat.Id).OrderBy(r => r.Time).ToList();

                // 2. Если список пуст
                if (myReminders.Count == 0)
                {
                    await botClient.SendMessage(message.Chat, "У тебя пока нет напоминаний.");
                }
                else
                {
                    // 3. Если есть — собираем красивый текст
                    string resultText = "📋 Твои напоминания:\n\n";

                    // Проходимся циклом по всем напоминаниям
                    foreach (var r in myReminders)
                    {
                        // Добавляем каждое напоминание в формате: "25.12 18:30 — Купить хлеб"
                        resultText += $"{r.Time:dd.MM HH:mm} — {r.Text}\n";
                    }

                    // 4. Отправляем
                    await botClient.SendMessage(message.Chat, resultText);
                }
            }
            else
            {
                // Проверяем: а этот пользователь вообще ждет от нас ответа?
                if (userStates.ContainsKey(message.Chat.Id))
                {
                    string state = userStates[message.Chat.Id];

                    // Если он должен ввести текст
                    if (state == "waiting_text")
                    {
                        // Сохраняем его текст в память
                        userTexts[message.Chat.Id] = text;

                        // Меняем состояние: теперь ждем время
                        userStates[message.Chat.Id] = "waiting_time";

                        await botClient.SendMessage(message.Chat, "Когда напомнить? (дату и время в таком формате вводи: ДД.ММ ЧЧ:ММ, 01.11 03:33)");
                    }
                    // Если он должен ввести время
                    else if (state == "waiting_time")
                    {
                        string cleanText = text.Trim();

                        // Пытаемся разобрать дату строго по формату: День.Месяц Час:Минута
                        if (DateTime.TryParseExact(cleanText, "dd.MM HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                        {
                            // Собираем полную дату, подставляя текущий год
                            var currentNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
                            DateTime finalDate = new DateTime(currentNow.Year, parsedDate.Month, parsedDate.Day, parsedDate.Hour, parsedDate.Minute, 0);

                            // Если пользователь ввел дату, которая уже прошла в этом году, переносим на следующий
                            if (finalDate < currentNow)
                            {
                                finalDate = finalDate.AddYears(1);
                            }

                            // Создаем напоминание
                            var reminder = new Reminder
                            {
                                Text = userTexts[message.Chat.Id],
                                ChatId = message.Chat.Id,
                                Time = finalDate
                            };
                            reminders.Add(reminder);
                            File.WriteAllText(filePath, JsonSerializer.Serialize(reminders));

                            // Очищаем состояния
                            userStates.Remove(message.Chat.Id);
                            userTexts.Remove(message.Chat.Id);

                            await botClient.SendMessage(message.Chat, $"Ладно, напомню {finalDate:dd.MM.yyyy в HH:mm}.");

                        }
                        
                        else
                        {
                            // Если формат не совпал — выдаем ошибку
                            await botClient.SendMessage(message.Chat, "Ошибка! Введи дату и время строго в формате: ДД.ММ ЧЧ:ММ\nНапример: 25.12 18:30");
                        }
                    }
                }
            }

        }
    },
    (botClient, expection, token) =>
    {
        Console.WriteLine($"Ошибка: {expection.Message}");
        return Task.CompletedTask;
    }
);
    Console.WriteLine("Бот запущен!");
    // Запускаем фоновый цикл проверки времени
    _ = Task.Run(async () =>
    {
        while (true)
        {
            try
            {

                // Ждем 10 секунд
                await Task.Delay(10000);

                var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);

                // Ищем те напоминания, время которых уже наступило
                var dueReminders = reminders.Where(r => r.Time <= now && r.Time.Year > 2024).ToList();

                foreach (var r in dueReminders)
                {
                    // Отправляем сообщение
                    await bot.SendMessage(r.ChatId, $"Ержан вставай\n{r.Text}");

                    // Удаляем из списка, чтобы не отправить дважды
                    reminders.Remove(r);
                    File.WriteAllText(filePath, JsonSerializer.Serialize(reminders));
                }
            }
            catch (Exception ex) // <--- ЕСЛИ ОШИБКА, ЛОВИМ ЕЁ
            {
                Console.WriteLine($"Ошибка в фоновом цикле: {ex.Message}");
                // Цикл НЕ ПРЕРЫВАЕТСЯ, он просто перейдет на следующую итерацию
            }
        }
    });
    Console.WriteLine("Бот успешно запущен!");
}
catch (Exception ex)
{
    Console.WriteLine($"КРИТИЧЕСКАЯ ОШИБКА ПРИ ЗАПУСКЕ: {ex.Message}");
}

await Task.Delay(-1);


