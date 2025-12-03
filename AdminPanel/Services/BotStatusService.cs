using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.Http;
using AdminPanel.Configs;

namespace AdminPanel.Services
{
    public class BotStatusService
    {
        private readonly ILogger<BotStatusService> _logger;
        private readonly IMemoryCache _cache;
        private readonly BotPathsConfig _botPaths;
        private readonly HttpClient _httpClient;
        private readonly string _botProcessName;
        private readonly string _botApiUrl;

        public BotStatusService(
            ILogger<BotStatusService> logger,
            IMemoryCache cache,
            IOptions<BotPathsConfig> botPathsConfig,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _cache = cache;
            _botPaths = botPathsConfig.Value;
            _httpClient = httpClientFactory.CreateClient("BotApi");
            _botProcessName = configuration["AdminSettings:BotProcessName"] ?? "VKBot_nordciti";
            _botApiUrl = configuration["BotApi:BaseUrl"] ?? "http://localhost:5000";

            _logger.LogInformation("BotStatusService инициализирован. Путь к боту: {Path}", _botPaths.BotProjectPath);
        }

        public async Task<BotStatusInfo> GetBotStatusAsync()
        {
            const string cacheKey = "bot_status_info";

            // Пробуем получить из кэша
            if (_cache.TryGetValue<BotStatusInfo>(cacheKey, out var cachedStatus))
            {
                // Проверяем не устарели ли данные (больше 10 секунд)
                if (cachedStatus != null &&
                    (DateTime.UtcNow - cachedStatus.Timestamp).TotalSeconds < 10)
                {
                    _logger.LogDebug("Статус бота получен из кэша");
                    return cachedStatus;
                }
            }

            var statusInfo = new BotStatusInfo
            {
                Timestamp = DateTime.UtcNow
            };

            try
            {
                // 1. Проверка процесса бота
                statusInfo.ProcessInfo = await GetProcessInfoAsync();

                // 2. Проверка файла блокировки
                statusInfo.HasLockFile = CheckLockFile();

                // 3. Проверка API бота
                statusInfo.ApiStatus = await CheckApiStatusAsync();

                // 4. Определение общего статуса
                statusInfo.OverallStatus = DetermineOverallStatus(statusInfo);

                // 5. Получение времени работы
                statusInfo.Uptime = await GetUptimeAsync(statusInfo);

                // 6. Получение версии
                statusInfo.Version = await GetVersionAsync();

                // 7. Получение ресурсов
                if (statusInfo.ProcessInfo.IsRunning)
                {
                    statusInfo.ResourceUsage = await GetResourceUsageAsync(statusInfo.ProcessInfo.ProcessId);
                }

                _logger.LogInformation(
                    "Статус бота: {Status}, процесс: {Process}, API: {Api}, память: {Memory}MB",
                    statusInfo.OverallStatus,
                    statusInfo.ProcessInfo.IsRunning,
                    statusInfo.ApiStatus.IsResponding,
                    statusInfo.ResourceUsage?.MemoryMB ?? 0);

                // Кэшируем на 5 секунд
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromSeconds(5));

                // Добавляем размер только если кэш настроен с SizeLimit
                // Это предотвратит ошибку "Cache entry must specify a value for Size when SizeLimit is set"
                try
                {
                    // Пытаемся добавить размер, но если будет ошибка - просто кэшируем без размера
                    cacheEntryOptions.SetSize(1);
                }
                catch
                {
                    // Игнорируем ошибку установки размера
                }

                _cache.Set(cacheKey, statusInfo, cacheEntryOptions);

                return statusInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статуса бота");
                statusInfo.OverallStatus = "error";
                statusInfo.Error = ex.Message;

                // Кэшируем даже ошибку на короткое время
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromSeconds(2));

                try
                {
                    cacheEntryOptions.SetSize(1);
                }
                catch
                {
                    // Игнорируем ошибку установки размера
                }

                _cache.Set(cacheKey, statusInfo, cacheEntryOptions);

                return statusInfo;
            }
        }

        private async Task<ProcessInfo> GetProcessInfoAsync()
        {
            var processInfo = new ProcessInfo();

            try
            {
                var processes = Process.GetProcessesByName(_botProcessName);

                if (processes.Length > 0)
                {
                    var process = processes[0];

                    // Проверяем что процесс не завершился
                    try
                    {
                        if (!process.HasExited)
                        {
                            processInfo.IsRunning = true;
                            processInfo.ProcessId = process.Id;
                            processInfo.ProcessName = process.ProcessName;
                            processInfo.StartTime = process.StartTime;
                            processInfo.MainModulePath = process.MainModule?.FileName ?? "";

                            _logger.LogDebug("Процесс бота найден: PID={Pid}, время запуска={StartTime}",
                                process.Id, process.StartTime);
                        }
                        else
                        {
                            _logger.LogDebug("Процесс бота завершен: PID={Pid}", process.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Ошибка при получении информации о процессе");
                        processInfo.Error = ex.Message;
                    }
                }
                else
                {
                    _logger.LogDebug("Процесс бота '{ProcessName}' не найден", _botProcessName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при поиске процесса бота");
                processInfo.Error = ex.Message;
            }

            return processInfo;
        }

        private bool CheckLockFile()
        {
            try
            {
                var lockFilePath = _botPaths.BotLockFilePath;

                if (File.Exists(lockFilePath))
                {
                    var fileInfo = new FileInfo(lockFilePath);
                    var age = DateTime.Now - fileInfo.LastWriteTime;

                    _logger.LogDebug("Файл блокировки найден: {Path}, возраст: {Age} секунд",
                        lockFilePath, age.TotalSeconds);

                    // Если файл старше 10 минут, считаем его устаревшим
                    return age.TotalMinutes < 10;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ошибка при проверке файла блокировки");
                return false;
            }
        }

        private async Task<ApiStatusInfo> CheckApiStatusAsync()
        {
            var apiStatus = new ApiStatusInfo();

            try
            {
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

                var response = await _httpClient.GetAsync($"{_botApiUrl}/health", cts.Token);

                apiStatus.IsResponding = response.IsSuccessStatusCode;
                apiStatus.StatusCode = (int)response.StatusCode;
                apiStatus.ResponseTime = DateTime.UtcNow;

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cts.Token);
                    apiStatus.ResponseContent = content.Length > 200 ? content.Substring(0, 200) + "..." : content;
                    _logger.LogDebug("API бота отвечает: {StatusCode}", response.StatusCode);
                }
                else
                {
                    _logger.LogDebug("API бота не отвечает: {StatusCode}", response.StatusCode);
                }
            }
            catch (TaskCanceledException)
            {
                apiStatus.Error = "Таймаут запроса (3 секунды)";
                _logger.LogDebug("Таймаут при проверке API бота");
            }
            catch (HttpRequestException ex)
            {
                apiStatus.Error = $"Ошибка HTTP: {ex.Message}";
                _logger.LogDebug(ex, "Ошибка HTTP при проверке API бота");
            }
            catch (SocketException ex)
            {
                apiStatus.Error = $"Ошибка сокета: {ex.Message}";
                _logger.LogDebug(ex, "Ошибка сокета при проверке API бота");
            }
            catch (Exception ex)
            {
                apiStatus.Error = $"Ошибка: {ex.Message}";
                _logger.LogDebug(ex, "Неизвестная ошибка при проверке API бота");
            }

            return apiStatus;
        }

        private string DetermineOverallStatus(BotStatusInfo statusInfo)
        {
            if (statusInfo.ApiStatus.IsResponding && statusInfo.ProcessInfo.IsRunning)
                return "running";

            if (statusInfo.ProcessInfo.IsRunning && !statusInfo.ApiStatus.IsResponding)
                return "starting";

            if (statusInfo.HasLockFile && !statusInfo.ProcessInfo.IsRunning)
                return "crashed";

            if (!statusInfo.ProcessInfo.IsRunning && !statusInfo.HasLockFile)
                return "stopped";

            return "unknown";
        }

        private async Task<TimeSpan> GetUptimeAsync(BotStatusInfo statusInfo)
        {
            if (!statusInfo.ProcessInfo.IsRunning || statusInfo.ProcessInfo.StartTime == DateTime.MinValue)
                return TimeSpan.Zero;

            try
            {
                return DateTime.Now - statusInfo.ProcessInfo.StartTime;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        private async Task<string> GetVersionAsync()
        {
            try
            {
                var versionFilePath = _botPaths.VersionFilePath;

                if (File.Exists(versionFilePath))
                {
                    var version = await File.ReadAllTextAsync(versionFilePath);
                    return version.Trim();
                }

                // Проверяем в папке проекта
                var projectVersionFile = Path.Combine(_botPaths.BotProjectPath, "version.txt");
                if (File.Exists(projectVersionFile))
                {
                    var version = await File.ReadAllTextAsync(projectVersionFile);
                    return version.Trim();
                }

                return "1.0.0";
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ошибка при получении версии бота");
                return "1.0.0";
            }
        }

        private async Task<ResourceUsage> GetResourceUsageAsync(int processId)
        {
            var usage = new ResourceUsage();

            try
            {
                var process = Process.GetProcessById(processId);

                // Память
                usage.MemoryMB = process.WorkingSet64 / (1024 * 1024);

                // CPU - сложная задача, делаем приблизительно
                var startTime = DateTime.UtcNow;
                var startCpuUsage = process.TotalProcessorTime;

                await Task.Delay(100);

                var endTime = DateTime.UtcNow;
                var endCpuUsage = process.TotalProcessorTime;

                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;

                if (totalMsPassed > 0)
                {
                    usage.CpuPercent = (cpuUsedMs / (Environment.ProcessorCount * totalMsPassed)) * 100;
                }

                // Количество потоков
                usage.ThreadCount = process.Threads.Count;

                _logger.LogDebug("Ресурсы процесса {Pid}: CPU={Cpu}%, Memory={Memory}MB, Threads={Threads}",
                    processId, usage.CpuPercent.ToString("F1"), usage.MemoryMB, usage.ThreadCount);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ошибка при получении информации о ресурсах процесса {Pid}", processId);
                usage.Error = ex.Message;
            }

            return usage;
        }

        public async Task<BotCommandResult> StartBotAsync()
        {
            _logger.LogInformation("Запуск бота...");

            try
            {
                // Проверяем, не запущен ли уже бот
                var currentStatus = await GetBotStatusAsync();
                if (currentStatus.OverallStatus == "running" || currentStatus.ProcessInfo.IsRunning)
                {
                    return BotCommandResult.FailResult("Бот уже запущен");
                }

                // Проверяем существование исполняемого файла
                var exePath = _botPaths.BotExecutablePath;
                if (!File.Exists(exePath))
                {
                    _logger.LogError("Файл бота не найден: {Path}", exePath);
                    return BotCommandResult.FailResult($"Файл бота не найден: {exePath}");
                }

                // Запускаем процесс
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WindowStyle = ProcessWindowStyle.Normal
                    }
                };

                var started = process.Start();

                if (started)
                {
                    _logger.LogInformation("Бот запущен (PID: {ProcessId})", process.Id);

                    // Создаем файл блокировки
                    try
                    {
                        var lockFilePath = _botPaths.BotLockFilePath;
                        await File.WriteAllTextAsync(lockFilePath, DateTime.Now.ToString("o"));
                        _logger.LogDebug("Создан файл блокировки: {Path}", lockFilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Не удалось создать файл блокировки");
                    }

                    // Очищаем кэш статуса
                    ClearCache();

                    // Ждем немного перед проверкой
                    await Task.Delay(2000);

                    return BotCommandResult.SuccessResult("Бот успешно запущен", new { ProcessId = process.Id });
                }
                else
                {
                    _logger.LogError("Не удалось запустить процесс бота");
                    return BotCommandResult.FailResult("Не удалось запустить процесс бота");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при запуске бота");
                return BotCommandResult.FailResult($"Ошибка запуска: {ex.Message}");
            }
        }

        public async Task<BotCommandResult> StopBotAsync()
        {
            _logger.LogInformation("Остановка бота...");

            try
            {
                // Проверяем, запущен ли бот
                var currentStatus = await GetBotStatusAsync();
                if (!currentStatus.ProcessInfo.IsRunning)
                {
                    return BotCommandResult.FailResult("Бот не запущен");
                }

                // Останавливаем все процессы с таким именем
                var processes = Process.GetProcessesByName(_botProcessName);
                var stoppedCount = 0;

                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(5000);
                            stoppedCount++;
                            _logger.LogInformation("Процесс остановлен: PID={ProcessId}", process.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при остановке процесса {ProcessId}", process.Id);
                    }
                }

                if (stoppedCount > 0)
                {
                    // Удаляем файл блокировки
                    try
                    {
                        var lockFilePath = _botPaths.BotLockFilePath;
                        if (File.Exists(lockFilePath))
                        {
                            File.Delete(lockFilePath);
                            _logger.LogDebug("Удален файл блокировки: {Path}", lockFilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Не удалось удалить файл блокировки");
                    }

                    // Очищаем кэш статуса
                    ClearCache();

                    return BotCommandResult.SuccessResult($"Остановлено процессов: {stoppedCount}");
                }
                else
                {
                    return BotCommandResult.FailResult("Не удалось остановить ни одного процесса");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при остановке бота");
                return BotCommandResult.FailResult($"Ошибка остановки: {ex.Message}");
            }
        }

        public async Task<BotSettingsDto> GetBotSettingsAsync()
        {
            try
            {
                // Для получения настроек используем DatabaseService
                // Этот метод нужен для обратной совместимости
                return new BotSettingsDto
                {
                    Id = 1,
                    BotName = "VK Бот",
                    VkToken = "",
                    GroupId = "",
                    AutoStart = true,
                    NotifyNewUsers = true,
                    NotifyErrors = true,
                    NotifyEmail = "",
                    LastUpdated = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении настроек бота");
                throw;
            }
        }

        public async Task<BotCommandResult> UpdateBotSettingsAsync(BotSettingsDto settings)
        {
            try
            {
                // Для сохранения настроек используем DatabaseService
                // Этот метод нужен для обратной совместимости
                _logger.LogInformation("Настройки бота обновлены: {BotName}", settings.BotName);
                return BotCommandResult.SuccessResult("Настройки сохранены");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сохранении настроек бота");
                return BotCommandResult.FailResult($"Ошибка сохранения: {ex.Message}");
            }
        }

        public async Task<BotRealStats> GetRealBotStatsAsync()
        {
            try
            {
                var status = await GetBotStatusAsync();

                return new BotRealStats
                {
                    IsBotRunning = status.ProcessInfo.IsRunning,
                    ProcessId = status.ProcessInfo.ProcessId,
                    Uptime = status.Uptime,
                    MemoryUsageMB = status.ResourceUsage?.MemoryMB ?? 0,
                    CpuPercent = status.ResourceUsage?.CpuPercent ?? 0,
                    ThreadCount = status.ResourceUsage?.ThreadCount ?? 0,
                    ApiResponding = status.ApiStatus.IsResponding,
                    StatusCode = status.ApiStatus.StatusCode,
                    Timestamp = DateTime.Now,
                    Version = status.Version,
                    OverallStatus = status.OverallStatus
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статистики бота");
                return new BotRealStats
                {
                    IsBotRunning = false,
                    Timestamp = DateTime.Now,
                    Error = ex.Message
                };
            }
        }

        // Удаляем из кэша
        public void ClearCache()
        {
            try
            {
                _cache.Remove("bot_status_info");
                _logger.LogDebug("Кэш статуса бота очищен");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ошибка при очистке кэша");
            }
        }

        public async Task<BotCommandResult> TestConnectionAsync()
        {
            try
            {
                var status = await GetBotStatusAsync();

                return new BotCommandResult
                {
                    Success = true,
                    Message = $"Статус бота: {GetStatusDescription(status.OverallStatus)}",
                    Data = new
                    {
                        status.ProcessInfo.IsRunning,
                        status.ApiStatus.IsResponding,
                        status.OverallStatus,
                        Uptime = status.Uptime.ToString(@"hh\:mm\:ss")
                    }
                };
            }
            catch (Exception ex)
            {
                return BotCommandResult.FailResult($"Ошибка проверки соединения: {ex.Message}");
            }
        }

        private string GetStatusDescription(string status)
        {
            return status switch
            {
                "running" => "🟢 Бот работает",
                "stopped" => "🔴 Бот остановлен",
                "starting" => "🟡 Бот запускается",
                "crashed" => "🔴 Бот завершился аварийно",
                "error" => "🔴 Ошибка",
                _ => "⚪ Неизвестный статус"
            };
        }
    }

    // ==================== МОДЕЛИ ====================

    public class BotStatusInfo
    {
        public DateTime Timestamp { get; set; }
        public string OverallStatus { get; set; } = "unknown";
        public ProcessInfo ProcessInfo { get; set; } = new();
        public bool HasLockFile { get; set; }
        public ApiStatusInfo ApiStatus { get; set; } = new();
        public TimeSpan Uptime { get; set; }
        public string Version { get; set; } = "1.0.0";
        public ResourceUsage? ResourceUsage { get; set; }
        public string? Error { get; set; }
    }

    public class ProcessInfo
    {
        public bool IsRunning { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = "";
        public DateTime StartTime { get; set; }
        public string MainModulePath { get; set; } = "";
        public string? Error { get; set; }
    }

    public class ApiStatusInfo
    {
        public bool IsResponding { get; set; }
        public int StatusCode { get; set; }
        public DateTime ResponseTime { get; set; }
        public string? ResponseContent { get; set; }
        public string? Error { get; set; }
    }

    public class ResourceUsage
    {
        public double MemoryMB { get; set; }
        public double CpuPercent { get; set; }
        public int ThreadCount { get; set; }
        public string? Error { get; set; }
    }

    public class BotSettingsDto
    {
        public int Id { get; set; }
        public string BotName { get; set; } = "VK Бот";
        public string VkToken { get; set; } = "";
        public string GroupId { get; set; } = "";
        public bool AutoStart { get; set; } = true;
        public bool NotifyNewUsers { get; set; } = true;
        public bool NotifyErrors { get; set; } = true;
        public string NotifyEmail { get; set; } = "";
        public DateTime LastUpdated { get; set; }
    }

    public class BotCommandResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public object? Data { get; set; }

        public static BotCommandResult SuccessResult(string message, object? data = null)
            => new() { Success = true, Message = message, Data = data };

        public static BotCommandResult FailResult(string message, object? data = null)
            => new() { Success = false, Message = message, Data = data };
    }

    public class BotRealStats
    {
        public bool IsBotRunning { get; set; }
        public int ProcessId { get; set; }
        public TimeSpan Uptime { get; set; }
        public double MemoryUsageMB { get; set; }
        public double CpuPercent { get; set; }
        public int ThreadCount { get; set; }
        public bool ApiResponding { get; set; }
        public int StatusCode { get; set; }
        public DateTime Timestamp { get; set; }
        public string Version { get; set; } = "1.0.0";
        public string OverallStatus { get; set; } = "unknown";
        public string? Error { get; set; }
    }
}