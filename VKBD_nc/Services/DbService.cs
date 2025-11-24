using Data;
using Models;
using Microsoft.EntityFrameworkCore;

namespace Services
{
    public class DbService
    {
        private readonly AppDbContext _db;

        public DbService(AppDbContext db)
        {
            _db = db;
        }

        // Лог команды
        public async Task LogCommandAsync(long userId, string cmd)
        {
            _db.CommandLogs.Add(new CommandLog
            {
                UserId = userId,
                Command = cmd,
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        // Получить или создать user session
        public async Task<UserSession> GetSessionAsync(long userId)
        {
            var session = await _db.UserSessions.FirstOrDefaultAsync(x => x.UserId == userId);

            if (session == null)
            {
                session = new UserSession
                {
                    UserId = userId,
                    State = "Idle",
                    UpdatedAt = DateTime.UtcNow
                };

                _db.UserSessions.Add(session);
                await _db.SaveChangesAsync();
            }

            return session;
        }

        // Обновить состояние
        public async Task UpdateSessionAsync(long userId, string state, string? payload = null)
        {
            var session = await GetSessionAsync(userId);

            session.State = state;
            session.Payload = payload;
            session.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }
    }
}
