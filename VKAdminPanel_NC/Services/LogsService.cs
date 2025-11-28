public class LogsService
{
    private readonly string _path = "logs/log.txt";

    public IEnumerable<string> ReadLastLines(int count)
    {
        return System.IO.File.ReadLines(_path).Reverse().Take(count);
    }
}
