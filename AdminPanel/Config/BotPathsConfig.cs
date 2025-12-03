namespace AdminPanel.Configs
{
    public class BotPathsConfig
    {
        public string BotProjectPath { get; set; } = @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti";
        public string DatabaseName { get; set; } = "vkbot.db";

        public string DatabasePath => Path.Combine(BotProjectPath, DatabaseName);

        public string BotExecutablePath => Path.Combine(BotProjectPath, "VKBot_nordciti.exe");

        public string BotLogsPath => Path.Combine(BotProjectPath, "logs");

        public string BotConfigPath => Path.Combine(BotProjectPath, "appsettings.json");

        public string BotLockFilePath => Path.Combine(BotProjectPath, "bot.lock");

        public string VersionFilePath => Path.Combine(BotProjectPath, "version.txt");
    }
}