namespace AdminPanel.Models
{
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
}