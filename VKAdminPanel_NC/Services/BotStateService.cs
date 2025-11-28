public class BotStateService
{
    public bool Enabled { get; private set; } = true;

    public void Enable() => Enabled = true;
    public void Disable() => Enabled = false;
    public void Restart() { Enabled = false; Thread.Sleep(2000); Enabled = true; }
}
