using Microsoft.EntityFrameworkCore;
using VKBD_nc.Data;
using VKBD_nc.models;

namespace VKBD_nc.Services
{
    public class DbService
    {
        private readonly BotDbContext _db;

        public DbService(BotDbContext db)
        {
            _db = db;
        }

        // Лог команды
        

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
