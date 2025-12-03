using System.Diagnostics;

namespace AdminPanel.Services
{
    public static class ProcessHelper
    {
        public static bool IsProcessRunning(string processName)
        {
            try
            {
                return Process.GetProcessesByName(processName).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static Process? GetProcessByName(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                return processes.Length > 0 ? processes[0] : null;
            }
            catch
            {
                return null;
            }
        }

        public static void KillProcess(string processName, bool force = false)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        if (force)
                        {
                            process.Kill();
                        }
                        else
                        {
                            process.CloseMainWindow();
                            if (!process.WaitForExit(3000))
                            {
                                process.Kill();
                            }
                        }
                        process.WaitForExit(5000);
                    }
                    catch
                    {
                        // Игнорируем ошибки при завершении процесса
                    }
                }
            }
            catch
            {
                // Игнорируем общие ошибки
            }
        }

        public static bool StartProcess(string exePath, string? arguments = null, string? workingDirectory = null)
        {
            try
            {
                if (!File.Exists(exePath))
                {
                    throw new FileNotFoundException($"Файл не найден: {exePath}");
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = arguments ?? "",
                        WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(exePath) ?? "",
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WindowStyle = ProcessWindowStyle.Normal
                    }
                };

                return process.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка запуска процесса: {ex.Message}");
                return false;
            }
        }

        public static async Task<(double memoryMB, double cpuPercent, int threadCount)> GetProcessUsageAsync(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);

                if (process.HasExited)
                {
                    return (0, 0, 0);
                }

                // Память
                double memoryMB = process.WorkingSet64 / (1024 * 1024);

                // CPU - измеряем за 500ms
                var startTime = DateTime.UtcNow;
                var startCpuUsage = process.TotalProcessorTime;

                await Task.Delay(500);

                var endTime = DateTime.UtcNow;
                var endCpuUsage = process.TotalProcessorTime;

                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;

                double cpuPercent = 0;
                if (totalMsPassed > 0)
                {
                    cpuPercent = (cpuUsedMs / (Environment.ProcessorCount * totalMsPassed)) * 100;
                }

                // Количество потоков
                int threadCount = process.Threads.Count;

                return (memoryMB, cpuPercent, threadCount);
            }
            catch
            {
                return (0, 0, 0);
            }
        }

        public static List<ProcessBasicInfo> GetProcessesBasicInfo(string? processName = null)
        {
            var processesInfo = new List<ProcessBasicInfo>();

            try
            {
                var processes = processName != null
                    ? Process.GetProcessesByName(processName)
                    : Process.GetProcesses();

                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            var info = new ProcessBasicInfo
                            {
                                Id = process.Id,
                                Name = process.ProcessName,
                                StartTime = process.StartTime,
                                MemoryUsageMB = process.WorkingSet64 / (1024 * 1024),
                                ThreadCount = process.Threads.Count,
                                HasExited = process.HasExited
                            };

                            processesInfo.Add(info);
                        }
                    }
                    catch
                    {
                        // Пропускаем процессы к которым нет доступа
                    }
                }
            }
            catch
            {
                // Игнорируем общие ошибки
            }

            return processesInfo;
        }
    }

    // Используем другое имя класса чтобы избежать конфликта
    public class ProcessBasicInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public DateTime StartTime { get; set; }
        public double MemoryUsageMB { get; set; }
        public int ThreadCount { get; set; }
        public bool HasExited { get; set; }
    }
}