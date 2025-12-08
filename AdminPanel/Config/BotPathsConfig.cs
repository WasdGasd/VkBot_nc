namespace AdminPanel.Configs
{
    public class BotPathsConfig
    {
        public string BotProjectPath { get; set; } = "";
        public string DatabasePath => Path.Combine(BotProjectPath, DatabaseName);
        public string BotExecutablePath => Path.Combine(BotProjectPath, "VKBot_nordciti.exe");
        public string VersionFilePath => Path.Combine(BotProjectPath, "version.txt");
        public string BotLockFilePath => Path.Combine(BotProjectPath, "bot.lock");
        public string DatabaseName { get; set; } = "vkbot.db";
    }
}