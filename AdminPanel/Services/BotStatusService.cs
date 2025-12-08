using AdminPanel.Configs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

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

        // Добавьте эти методы в класс BotStatusService

        /// <summary>
        /// Безопасная остановка процесса
        /// </summary>
        private async Task<bool> SafeStopProcessAsync(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);

                if (process.HasExited)
                {
                    _logger.LogInformation("Процесс {ProcessId} уже завершен", processId);
                    return true;
                }

                // 1. Пытаемся закрыть главное окно
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    _logger.LogInformation("Закрытие главного окна процесса {ProcessId}", processId);
                    process.CloseMainWindow();

                    // Ждем 3 секунды
                    if (process.WaitForExit(3000))
                    {
                        _logger.LogInformation("Процесс {ProcessId} завершился после CloseMainWindow", processId);
                        return true;
                    }
                }

                // 2. Для консольных приложений используем Ctrl+C
                try
                {
                    if (await SendCtrlCSignalAsync(processId))
                    {
                        _logger.LogInformation("Отправлен сигнал Ctrl+C процессу {ProcessId}", processId);

                        if (process.WaitForExit(3000))
                        {
                            _logger.LogInformation("Процесс {ProcessId} завершился после Ctrl+C", processId);
                            return true;
                        }
                    }
                }
                catch
                {
                    // Игнорируем ошибки Ctrl+C
                }

                return false;
            }
            catch (ArgumentException)
            {
                // Процесс не найден - уже завершен
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при безопасной остановке процесса {ProcessId}", processId);
                return false;
            }
        }

        /// <summary>
        /// Безопасная остановка бота с выбором метода
        /// </summary>
        public async Task<BotCommandResult> StopBotAsync(bool graceful = true, int timeoutSeconds = 30)
        {
            _logger.LogInformation("Остановка бота (graceful: {Graceful}, timeout: {Timeout}s)...",
                graceful, timeoutSeconds);

            try
            {
                // Проверяем, запущен ли бот
                var currentStatus = await GetBotStatusAsync();
                if (!currentStatus.ProcessInfo.IsRunning)
                {
                    return BotCommandResult.FailResult("Бот не запущен");
                }

                var processes = Process.GetProcessesByName(_botProcessName);
                var stoppedCount = 0;
                var failedCount = 0;

                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            if (graceful)
                            {
                                // Безопасная остановка
                                if (await SafeStopProcessAsync(process.Id))
                                {
                                    stoppedCount++;
                                    _logger.LogInformation("Процесс безопасно остановлен: PID={ProcessId}", process.Id);
                                }
                                else
                                {
                                    // Если безопасная остановка не удалась, используем принудительную
                                    process.Kill();
                                    process.WaitForExit(3000);
                                    stoppedCount++;
                                    _logger.LogWarning("Процесс принудительно остановлен: PID={ProcessId}", process.Id);
                                }
                            }
                            else
                            {
                                // Принудительная остановка
                                process.Kill();
                                process.WaitForExit(3000);
                                stoppedCount++;
                                _logger.LogWarning("Процесс принудительно остановлен: PID={ProcessId}", process.Id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при остановке процесса {ProcessId}", process.Id);
                        failedCount++;
                    }
                }

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

                if (stoppedCount > 0)
                {
                    var message = graceful
                        ? $"Безопасно остановлено процессов: {stoppedCount}"
                        : $"Принудительно остановлено процессов: {stoppedCount}";

                    if (failedCount > 0)
                    {
                        message += $", не удалось остановить: {failedCount}";
                    }

                    return BotCommandResult.SuccessResult(message, new { StoppedCount = stoppedCount, FailedCount = failedCount });
                }
                else
                {
                    return BotCommandResult.FailResult($"Не удалось остановить ни одного процесса. Ошибки: {failedCount}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при остановке бота");
                return BotCommandResult.FailResult($"Ошибка остановки: {ex.Message}");
            }
        }

        /// <summary>
        /// Отправка Ctrl+C сигнала консольному приложению
        /// </summary>
        private async Task<bool> SendCtrlCSignalAsync(int processId)
        {
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        if (AttachConsole((uint)processId))
                        {
                            SetConsoleCtrlHandler(null, true);
                            GenerateConsoleCtrlEvent(ConsoleCtrlEvent.CTRL_C_EVENT, 0);
                            Thread.Sleep(100);
                            FreeConsole();
                            return true;
                        }
                        return false;
                    }
                    catch
                    {
                        return false;
                    }
                });
            }
            catch
            {
                return false;
            }
        }

        // WinAPI импорты для Ctrl+C
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate HandlerRoutine, bool Add);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GenerateConsoleCtrlEvent(ConsoleCtrlEvent dwCtrlEvent, uint dwProcessGroupId);

        delegate bool ConsoleCtrlDelegate(ConsoleCtrlEvent CtrlType);

        enum ConsoleCtrlEvent
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
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
                    apiStatus.ResponseContent = (content?.Length > 200 ? content.Substring(0, 200) + "..." : content) ?? string.Empty;
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

        // ==================== БЕЗОПАСНЫЕ МЕТОДЫ УПРАВЛЕНИЯ ====================

        public async Task<BotCommandResult> SafeStartBotAsync()
        {
            _logger.LogInformation("Безопасный запуск бота...");

            try
            {
                // Проверяем текущий статус
                var currentStatus = await GetBotStatusAsync();

                if (currentStatus.OverallStatus == "running")
                {
                    return BotCommandResult.FailResult("Бот уже запущен и работает");
                }

                // Проверяем, не находится ли бот в состоянии запуска
                if (currentStatus.HasLockFile && currentStatus.OverallStatus == "starting")
                {
                    // Если есть lock файл и статус starting, ждем завершения
                    _logger.LogInformation("Бот находится в состоянии запуска, ожидаем...");
                    await Task.Delay(5000);

                    // Проверяем снова
                    currentStatus = await GetBotStatusAsync();
                    if (currentStatus.OverallStatus == "running")
                    {
                        return BotCommandResult.FailResult("Бот запустился во время ожидания");
                    }
                }

                // Удаляем старый lock файл если есть
                try
                {
                    var lockFilePath = _botPaths.BotLockFilePath;
                    if (File.Exists(lockFilePath))
                    {
                        var fileAge = DateTime.Now - File.GetLastWriteTime(lockFilePath);
                        if (fileAge.TotalMinutes > 10) // Старый lock файл (больше 10 минут)
                        {
                            File.Delete(lockFilePath);
                            _logger.LogDebug("Удален устаревший lock файл");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось обработать lock файл");
                }

                // Запускаем бота
                return await StartBotAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при безопасном запуске бота");
                return BotCommandResult.FailResult($"Ошибка безопасного запуска: {ex.Message}");
            }
        }

        public async Task<BotCommandResult> SafeStopBotAsync(bool graceful = true, int timeoutSeconds = 30)
        {
            _logger.LogInformation("Безопасная остановка бота (graceful: {Graceful})", graceful);

            try
            {
                var currentStatus = await GetBotStatusAsync();

                if (!currentStatus.ProcessInfo.IsRunning)
                {
                    return BotCommandResult.FailResult("Бот не запущен");
                }

                // Если graceful и API доступен, пытаемся отправить сигнал завершения
                if (graceful && currentStatus.ApiStatus.IsResponding)
                {
                    try
                    {
                        var gracefulResult = await TryGracefulShutdownAsync(timeoutSeconds);
                        if (gracefulResult.Success)
                        {
                            return BotCommandResult.SuccessResult("Бот успешно остановлен (graceful shutdown)");
                        }

                        _logger.LogWarning("Graceful shutdown не удался, используем force stop");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Ошибка graceful shutdown, используем force stop");
                    }
                }

                // Force stop
                return await StopBotAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при безопасной остановке бота");
                return BotCommandResult.FailResult($"Ошибка безопасной остановки: {ex.Message}");
            }
        }

        private async Task<BotCommandResult> TryGracefulShutdownAsync(int timeoutSeconds)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

                var response = await httpClient.PostAsync(
                    $"{_botApiUrl}/api/admin/shutdown",
                    new StringContent("{}", Encoding.UTF8, "application/json"));

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Graceful shutdown signal sent successfully");

                    // Ждем завершения процесса
                    await WaitForProcessExitAsync(timeoutSeconds);

                    // Удаляем lock файл
                    await DeleteLockFileAsync();

                    ClearCache();

                    return BotCommandResult.SuccessResult("Graceful shutdown completed");
                }

                return BotCommandResult.FailResult($"Graceful shutdown failed: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                return BotCommandResult.FailResult($"Graceful shutdown error: {ex.Message}");
            }
        }

        private async Task WaitForProcessExitAsync(int timeoutSeconds)
        {
            var processId = -1;
            try
            {
                var processes = Process.GetProcessesByName(_botProcessName);
                if (processes.Length > 0)
                {
                    processId = processes[0].Id;

                    var startTime = DateTime.Now;
                    while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
                    {
                        try
                        {
                            var process = Process.GetProcessById(processId);
                            if (process.HasExited)
                            {
                                _logger.LogInformation("Процесс {ProcessId} завершился", processId);
                                return;
                            }
                        }
                        catch (ArgumentException)
                        {
                            // Процесс не найден - значит завершился
                            _logger.LogInformation("Процесс {ProcessId} завершился", processId);
                            return;
                        }

                        await Task.Delay(1000);
                    }

                    _logger.LogWarning("Таймаут ожидания завершения процесса {ProcessId}", processId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при ожидании завершения процесса {ProcessId}", processId);
            }
        }

        private async Task DeleteLockFileAsync()
        {
            try
            {
                var lockFilePath = _botPaths.BotLockFilePath;
                if (File.Exists(lockFilePath))
                {
                    File.Delete(lockFilePath);
                    _logger.LogDebug("Lock файл удален: {Path}", lockFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось удалить lock файл");
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