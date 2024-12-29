using System;
using System.Collections.Concurrent; // Для потокобезопасных коллекций
using System.Net.Http; // Для работы с HTTP-запросами
using System.Text.Json; // Для сериализации и десериализации JSON
using System.Threading.Tasks; // Для работы с асинхронным программированием
using System.IO; // Для работы с файловой системой
using System.Collections.Generic; // Для работы с коллекциями
using System.Linq; // Для LINQ-запросов
using System.Threading; // Для работы с потоками
using System.Diagnostics; // Для отладки

class Program
{
    // Создаем HttpClient с таймаутом 30 секунд
    private static HttpClient client = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };

    // Токен для аутентификации API
    private static string apiToken = "RWhhYmZtRy1qVUIwcUJrdnB3TjE5aXVjdnZPLVJ1WXFkS2dqR2pCQXBfdz0";

    // Имя выходного файла для сохранения результатов
    private static string outputFile = "results.txt";

    // Семафор для ограничения количества одновременно выполняющихся задач
    private static SemaphoreSlim semaphore = new SemaphoreSlim(5);

    // Счетчик успешных операций
    private static int successCount = 0;

    // Потоко-безопасный словарь для хранения результатов стикеров и их средних цен
    private static ConcurrentDictionary<string, double> results = new ConcurrentDictionary<string, double>();

    static async Task Main()
    {
        // Читаем стикеры из файла
        string[] tickers = await File.ReadAllLinesAsync("ticker.txt");
        // Фильтруем пустые строки и обрезаем пробелы
        tickers = tickers.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray();

        // Общее количество стикеров
        int totalTickers = tickers.Length;
        //Console.WriteLine(totalTickers);
        var toDate = DateTime.Now; // Текущая дата
        var fromDate = toDate.AddMonths(-11); // Дата 11 месяцев назад

        // Настраиваем заголовки для HTTP-запросов
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiToken}");
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var tasks = new List<Task>();
        var processedCount = 0;

        // Обрабатываем стикеры партиями по 25 штук
        for (int i = 0; i < tickers.Length; i += 25)
        {
            var batch = tickers.Skip(i).Take(25); // Берем подмассив стикеров
            var batchTasks = batch.Select(async ticker =>
            {
                await semaphore.WaitAsync(); // Ожидаем, пока освободится семафор
                try
                {
                    // Обрабатываем тикер
                    await ProcessTickerAsync(ticker, fromDate, toDate);
                    var currentCount = Interlocked.Increment(ref processedCount); // Увеличиваем счетчик обработанных стикеров
                    var successRate = (double)successCount / currentCount * 100; // Рассчитываем процент успешных операций
                }
                finally
                {
                    semaphore.Release(); // Освобождаем семафор
                }
            });
            tasks.AddRange(batchTasks); // Добавляем задачи в общий список
        }

        await Task.WhenAll(tasks); // Дождаться завершения всех задач

        // Сортируем результаты и форматируем их для записи
        var sortedResults = results.OrderBy(r => r.Key)
                                   .Select(r => $"{r.Key}:{r.Value:F2}");

        // Записываем результаты в файл
        await File.WriteAllLinesAsync(outputFile, sortedResults);
    }

    // Асинхронный метод для обработки тикера и получения его средней цены
    static async Task ProcessTickerAsync(string ticker, DateTime fromDate, DateTime toDate)
    {
        try
        {
            // Формируем URL для API
            string url = $"https://api.marketdata.app/v1/stocks/candles/D/{ticker}/?from={fromDate:yyyy-MM-dd}&to={toDate:yyyy-MM-dd}&format=json&adjusted=true";
            var response = await client.GetStringAsync(url); // Отправляем GET-запрос и получаем ответ
            var data = JsonSerializer.Deserialize<StockData>(response); // Десериализация ответа в объект StockData

            double averagePrice = 0;
            int count = data.h.Length; // Количество данных о ценах
            for (int i = 0; i < count; i++)
            {
                // Считаем среднюю цену: (макс. цена + мин. цена) / 2
                averagePrice += (data.h[i] + data.l[i]) / 2;
            }
            averagePrice /= count; // Получаем среднюю цену
            results.TryAdd(ticker, averagePrice); // Добавляем результат в словарь
            Interlocked.Increment(ref successCount); // Увеличиваем счетчик успешных операций
        }
        catch
        {
            // Обработка исключений (здесь ничего не делаем, просто игнорируем ошибки)
        }
    }
}

// Класс для представления данных о ценах акции
class StockData
{
    public double[] o { get; set; }   // Цены открытия
    public double[] h { get; set; }   // Максимальные цены
    public double[] l { get; set; }   // Минимальные цены
    public double[] c { get; set; }   // Цены закрытия
    public long[] v { get; set; }     // Объемы торгов
}
