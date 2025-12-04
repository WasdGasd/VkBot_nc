using Microsoft.AspNetCore.Mvc;
using AdminPanel.Services;
using AdminPanel.Models;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace AdminPanel.Controllers
{
    [ApiController]
    [Route("api/bot-management")]
    public class BotManagementController : ControllerBase
    {
        private readonly BotStatusService _botStatusService;
        private readonly DatabaseService _dbService;
        private readonly ILogger<BotManagementController> _logger;
        private static SemaphoreSlim _managementLock = new SemaphoreSlim(1, 1);
        private readonly string _botApiUrl;

        public BotManagementController(
            BotStatusService botStatusService,
            DatabaseService dbService,
            ILogger<BotManagementController> logger,
            IConfiguration configuration)
        {
            _botStatusService = botStatusService;
            _dbService = dbService;
            _logger = logger;
            _botApiUrl = configuration["BotApi:BaseUrl"] ?? "http://localhost:5000";
        }

        /// <summary>
        /// Получить текущий статус бота
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var status = await _botStatusService.GetBotStatusAsync();
                var settings = await _dbService.GetBotSettingsAsync();

                var response = new
                {
                    success = true,
                    data = new
                    {
                        status.OverallStatus,
                        status.ProcessInfo.IsRunning,
                        status.ApiStatus.IsResponding,
                        Uptime = status.Uptime.TotalSeconds,
                        settings.BotName,
                        CanStart = CanStartBot(status),
                        CanStop = CanStopBot(status),
                        CanRestart = CanRestartBot(status),
                        timestamp = DateTime.UtcNow
                    }
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения статуса бота");
                return StatusCode(500, new ApiResponse(false, "Ошибка получения статуса", ex.Message));
            }
        }

        /// <summary>
        /// Запустить бота безопасно
        /// </summary>
        [HttpPost("start")]
        public async Task<IActionResult> StartBot()
        {
            // Проверяем блокировку для предотвращения конфликтов
            if (!await _managementLock.WaitAsync(TimeSpan.FromSeconds(1)))
            {
                return StatusCode(429, new ApiResponse(false, "Операция уже выполняется"));
            }

            try
            {
                _logger.LogInformation("Получен запрос на безопасный запуск бота");

                // 1. Проверяем текущий статус
                var currentStatus = await _botStatusService.GetBotStatusAsync();

                if (currentStatus.ProcessInfo.IsRunning)
                {
                    return Ok(new ApiResponse(true, "Бот уже запущен"));
                }

                // 2. Проверяем настройки автостарта
                var settings = await _dbService.GetBotSettingsAsync();

                // 3. Выполняем предварительные проверки
                var preCheckResult = await PreStartChecksAsync();
                if (!preCheckResult.Success)
                {
                    return BadRequest(new ApiResponse(false, preCheckResult.Message));
                }

                // 4. Запускаем бота
                var startResult = await _botStatusService.StartBotAsync();

                if (!startResult.Success)
                {
                    return BadRequest(new ApiResponse(false, startResult.Message));
                }

                _logger.LogInformation("Бот успешно запущен");

                // 5. Ждем и проверяем статус
                await Task.Delay(3000);
                var updatedStatus = await _botStatusService.GetBotStatusAsync();

                return Ok(new ApiResponse(true, "Бот запускается", new
                {
                    ProcessId = startResult.Data,
                    Status = updatedStatus.OverallStatus,
                    ApiResponding = updatedStatus.ApiStatus.IsResponding,
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при запуске бота");
                return StatusCode(500, new ApiResponse(false, "Ошибка запуска", ex.Message));
            }
            finally
            {
                _managementLock.Release();
            }
        }

        /// <summary>
        /// Остановить бота безопасно
        /// </summary>
        [HttpPost("stop")]
        public async Task<IActionResult> StopBot([FromBody] StopBotRequest request)
        {
            if (!await _managementLock.WaitAsync(TimeSpan.FromSeconds(1)))
            {
                return StatusCode(429, new ApiResponse(false, "Операция уже выполняется"));
            }

            try
            {
                _logger.LogInformation("Получен запрос на остановку бота (graceful: {Graceful})", request?.Graceful ?? true);

                // 1. Проверяем текущий статус
                var currentStatus = await _botStatusService.GetBotStatusAsync();

                if (!currentStatus.ProcessInfo.IsRunning)
                {
                    return Ok(new ApiResponse(true, "Бот уже остановлен"));
                }

                var graceful = request?.Graceful ?? true;
                var timeoutSeconds = request?.TimeoutSeconds ?? 30;

                // 2. Если graceful, отправляем сигнал завершения
                if (graceful && currentStatus.ApiStatus.IsResponding)
                {
                    try
                    {
                        if (await TryGracefulShutdownAsync(timeoutSeconds))
                        {
                            _logger.LogInformation("Сигнал graceful shutdown отправлен");

                            // Ждем завершения
                            await WaitForGracefulShutdownAsync(currentStatus.ProcessInfo.ProcessId, timeoutSeconds);

                            // Проверяем, остановился ли процесс
                            var checkStatus = await _botStatusService.GetBotStatusAsync();
                            if (!checkStatus.ProcessInfo.IsRunning)
                            {
                                _logger.LogInformation("Бот успешно завершился graceful shutdown");

                                // Очищаем файл блокировки
                                await ClearLockFileAsync();

                                return Ok(new ApiResponse(true, "Бот успешно остановлен (graceful shutdown)", new
                                {
                                    Method = "graceful",
                                    timestamp = DateTime.UtcNow
                                }));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Ошибка при graceful shutdown, используем force stop");
                    }
                }

                // 3. Безопасная остановка процессов
                var processes = Process.GetProcessesByName("VKBot_nordciti");
                var stoppedCount = 0;
                var force = !graceful;

                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            if (force)
                            {
                                // Принудительная остановка
                                process.Kill();
                                process.WaitForExit(5000);
                                stoppedCount++;
                                _logger.LogWarning("Процесс принудительно остановлен: PID={ProcessId}", process.Id);
                            }
                            else
                            {
                                // Безопасная остановка
                                if (await SafeStopProcessAsync(process.Id))
                                {
                                    stoppedCount++;
                                    _logger.LogInformation("Процесс безопасно остановлен: PID={ProcessId}", process.Id);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при остановке процесса {ProcessId}", process.Id);
                    }
                }

                // 4. Очищаем файл блокировки
                await ClearLockFileAsync();

                if (stoppedCount > 0)
                {
                    var message = force
                        ? $"Принудительно остановлено процессов: {stoppedCount}"
                        : $"Безопасно остановлено процессов: {stoppedCount}";

                    return Ok(new ApiResponse(true, message, new
                    {
                        Method = force ? "force" : "safe",
                        StoppedCount = stoppedCount,
                        timestamp = DateTime.UtcNow
                    }));
                }

                return BadRequest(new ApiResponse(false, "Не удалось остановить бота"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при остановке бота");
                return StatusCode(500, new ApiResponse(false, "Ошибка остановки", ex.Message));
            }
            finally
            {
                _managementLock.Release();
            }
        }

        /// <summary>
        /// Перезапустить бота
        /// </summary>
        [HttpPost("restart")]
        public async Task<IActionResult> RestartBot()
        {
            if (!await _managementLock.WaitAsync(TimeSpan.FromSeconds(1)))
            {
                return StatusCode(429, new ApiResponse(false, "Операция уже выполняется"));
            }

            try
            {
                _logger.LogInformation("Получен запрос на перезапуск бота");

                // 1. Останавливаем бота
                var currentStatus = await _botStatusService.GetBotStatusAsync();

                if (currentStatus.ProcessInfo.IsRunning)
                {
                    var stopResponse = await StopBot(new StopBotRequest { Graceful = true, TimeoutSeconds = 30 });
                    if (stopResponse is OkObjectResult stopResult && stopResult.Value is ApiResponse apiResponse && apiResponse.Success)
                    {
                        _logger.LogInformation("Бот успешно остановлен перед перезапуском");
                    }

                    await Task.Delay(2000);
                }

                // 2. Запускаем бота
                var startResponse = await StartBot();

                _logger.LogInformation("Бот успешно перезапущен");

                return Ok(new ApiResponse(true, "Бот перезапущен", new
                {
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при перезапуске бота");
                return StatusCode(500, new ApiResponse(false, "Ошибка перезапуска", ex.Message));
            }
            finally
            {
                _managementLock.Release();
            }
        }

        /// <summary>
        /// Отправить тестовое сообщение для проверки работоспособности
        /// </summary>
        [HttpPost("test")]
        public async Task<IActionResult> TestBot()
        {
            try
            {
                var status = await _botStatusService.GetBotStatusAsync();

                if (!status.ProcessInfo.IsRunning)
                {
                    return Ok(new ApiResponse(false, "Бот не запущен"));
                }

                if (!status.ApiStatus.IsResponding)
                {
                    return Ok(new ApiResponse(false, "API бота не отвечает"));
                }

                // Отправляем тестовый запрос
                var healthResult = await _botStatusService.TestConnectionAsync();

                return Ok(new ApiResponse(
                    healthResult.Success,
                    healthResult.Message,
                    healthResult.Data
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при тестировании бота");
                return StatusCode(500, new ApiResponse(false, "Ошибка тестирования", ex.Message));
            }
        }

        /// <summary>
        /// Получить детальную информацию о состоянии бота
        /// </summary>
        [HttpGet("details")]
        public async Task<IActionResult> GetDetails()
        {
            try
            {
                var status = await _botStatusService.GetBotStatusAsync();
                var settings = await _dbService.GetBotSettingsAsync();
                var dbInfo = await _dbService.GetDatabaseInfoAsync();

                var details = new
                {
                    // Информация о процессе
                    Process = new
                    {
                        status.ProcessInfo.IsRunning,
                        status.ProcessInfo.ProcessId,
                        status.ProcessInfo.StartTime,
                        status.ProcessInfo.ProcessName,
                        HasLockFile = status.HasLockFile
                    },

                    // Информация об API
                    Api = new
                    {
                        status.ApiStatus.IsResponding,
                        status.ApiStatus.StatusCode,
                        status.ApiStatus.ResponseTime,
                        LastResponse = (status.ApiStatus.ResponseContent?.Length > 100
        ? status.ApiStatus.ResponseContent.Substring(0, 100) + "..."
        : status.ApiStatus.ResponseContent) ?? ""
                    },

                    // Ресурсы
                    Resources = status.ResourceUsage != null ? new
                    {
                        status.ResourceUsage.MemoryMB,
                        status.ResourceUsage.CpuPercent,
                        status.ResourceUsage.ThreadCount
                    } : null,

                    // Системная информация
                    System = new
                    {
                        status.OverallStatus,
                        status.Version,
                        Uptime = status.Uptime.ToString(@"dd\.hh\:mm\:ss"),
                        status.Timestamp
                    },

                    // Настройки
                    Settings = new
                    {
                        settings.BotName,
                        settings.AutoStart,
                        LastUpdated = settings.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss")
                    },

                    // База данных
                    Database = new
                    {
                        dbInfo.Exists,
                        dbInfo.FileSizeKB,
                        dbInfo.CommandsCount,
                        dbInfo.LastModified,
                        dbInfo.ConnectionTested
                    },

                    // Возможные действия
                    AvailableActions = new
                    {
                        CanStart = CanStartBot(status),
                        CanStop = CanStopBot(status),
                        CanRestart = CanRestartBot(status),
                        CanTest = status.ProcessInfo.IsRunning
                    }
                };

                return Ok(new ApiResponse(true, "Детальная информация", details));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения детальной информации");
                return StatusCode(500, new ApiResponse(false, "Ошибка получения информации", ex.Message));
            }
        }

        // ==================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ====================

        private bool CanStartBot(BotStatusInfo status)
        {
            return !status.ProcessInfo.IsRunning &&
                   string.IsNullOrEmpty(status.Error);
        }

        private bool CanStopBot(BotStatusInfo status)
        {
            return status.ProcessInfo.IsRunning;
        }

        private bool CanRestartBot(BotStatusInfo status)
        {
            return status.ProcessInfo.IsRunning ||
                  (status.HasLockFile && !status.ProcessInfo.IsRunning);
        }

        private async Task<PreCheckResult> PreStartChecksAsync()
        {
            var result = new PreCheckResult();

            try
            {
                // 1. Проверяем существование исполняемого файла
                var exePath = @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\VKBot_nordciti.exe";
                if (!System.IO.File.Exists(exePath))
                {
                    result.AddError($"Файл бота не найден: {exePath}");
                }

                // 2. Проверяем базу данных
                var dbInfo = await _dbService.GetDatabaseInfoAsync();
                if (!dbInfo.Exists)
                {
                    result.AddWarning("База данных не найдена");
                }

                // 3. Проверяем наличие конфигурации
                var configPath = @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\appsettings.json";
                if (!System.IO.File.Exists(configPath))
                {
                    result.AddWarning("Конфигурационный файл не найден");
                }

                // 4. Проверяем свободный порт
                if (!await CheckPortAvailabilityAsync(5000))
                {
                    result.AddWarning("Порт 5000 может быть занят");
                }

                if (result.HasErrors)
                {
                    result.Success = false;
                    result.Message = "Предварительные проверки не пройдены";
                }
                else if (result.HasWarnings)
                {
                    result.Message = "Предварительные проверки пройдены с предупреждениями";
                }
                else
                {
                    result.Message = "Все предварительные проверки пройдены успешно";
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении предварительных проверок");
                result.Success = false;
                result.AddError($"Ошибка проверок: {ex.Message}");
                return result;
            }
        }

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

                // 3. Используем API для graceful shutdown
                if (await TryApiShutdownAsync())
                {
                    _logger.LogInformation("Graceful shutdown через API успешен для процесса {ProcessId}", processId);

                    if (process.WaitForExit(5000))
                    {
                        return true;
                    }
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
                            GenerateConsoleCtrlEvent(ConsoleCtrlEvent.CTRL_C_EVENT, 0u);
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

        /// <summary>
        /// Пытаемся остановить бот через его API
        /// </summary>
        private async Task<bool> TryApiShutdownAsync()
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                var response = await httpClient.PostAsync(
                    $"{_botApiUrl}/api/admin/shutdown",
                    new StringContent("{}", Encoding.UTF8, "application/json"));

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Отправляем graceful shutdown сигнал через API
        /// </summary>
        private async Task<bool> TryGracefulShutdownAsync(int timeoutSeconds)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

                var response = await httpClient.PostAsync(
                    $"{_botApiUrl}/api/admin/shutdown",
                    new StringContent("{}", Encoding.UTF8, "application/json"));

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось отправить graceful shutdown signal");
                return false;
            }
        }

        private async Task WaitForGracefulShutdownAsync(int processId, int timeoutSeconds)
        {
            var maxWaitTime = TimeSpan.FromSeconds(timeoutSeconds);
            var checkInterval = TimeSpan.FromSeconds(1);
            var elapsed = TimeSpan.Zero;

            while (elapsed < maxWaitTime)
            {
                try
                {
                    var process = Process.GetProcessById(processId);
                    if (process.HasExited)
                    {
                        _logger.LogInformation("Бот завершил работу gracefully");
                        return;
                    }
                }
                catch (ArgumentException)
                {
                    // Process not found - значит завершился
                    _logger.LogInformation("Бот завершил работу gracefully");
                    return;
                }

                await Task.Delay(checkInterval);
                elapsed = elapsed.Add(checkInterval);
            }

            _logger.LogWarning("Таймаут ожидания graceful shutdown");
        }

        private async Task ClearLockFileAsync()
        {
            try
            {
                var lockFilePath = @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\bot.lock";
                if (System.IO.File.Exists(lockFilePath))
                {
                    System.IO.File.Delete(lockFilePath);
                    _logger.LogDebug("Файл блокировки удален");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось удалить файл блокировки");
            }
        }

        private async Task<bool> CheckPortAvailabilityAsync(int port)
        {
            try
            {
                using var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync("localhost", port);
                return false; // Порт занят
            }
            catch (SocketException)
            {
                return true; // Порт свободен
            }
        }
    }

    // ==================== МОДЕЛИ ====================

    public class StopBotRequest
    {
        public bool Graceful { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 30;
    }

    public class PreCheckResult
    {
        public bool Success { get; set; } = true;
        public string Message { get; set; } = "";
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        public bool HasErrors => Errors.Any();
        public bool HasWarnings => Warnings.Any();

        public void AddError(string error)
        {
            Errors.Add(error);
        }

        public void AddWarning(string warning)
        {
            Warnings.Add(warning);
        }
    }
}