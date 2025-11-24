using System.Collections.Concurrent;

namespace Services
{
    public enum ConversationState
    {
        Idle,
        WaitingForDate,
        WaitingForSession,
        WaitingForCategory,
        WaitingForPayment
    }

    // Простейший in-memory state store (thread-safe)
    public class ConversationStateService
    {
        private readonly ConcurrentDictionary<long, ConversationState> _states = new();
        private readonly ConcurrentDictionary<long, ConcurrentDictionary<string, string>> _data = new();

        public ConversationState GetState(long userId)
        {
            if (_states.TryGetValue(userId, out var s)) return s;
            return ConversationState.Idle;
        }

        public void SetState(long userId, ConversationState state)
        {
            _states.AddOrUpdate(userId, state, (k, old) => state);
        }

        public void SetData(long userId, string key, string value)
        {
            var dict = _data.GetOrAdd(userId, _ => new ConcurrentDictionary<string, string>());
            dict.AddOrUpdate(key, value, (k, old) => value);
        }

        public string? GetData(long userId, string key)
        {
            if (_data.TryGetValue(userId, out var dict) && dict.TryGetValue(key, out var val))
                return val;
            return null;
        }

        public void Clear(long userId)
        {
            _states.TryRemove(userId, out _);
            _data.TryRemove(userId, out _);
        }
    }
}
