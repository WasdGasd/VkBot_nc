namespace VKBot_nordciti.Models
{
    public class DailyStatsRecord
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public int TotalUsers { get; set; }
        public int ActiveUsers { get; set; }
        public int NewUsers { get; set; }
        public int MessagesCount { get; set; }
        public int CommandsCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}