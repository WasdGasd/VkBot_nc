namespace VKBot_nordciti.Services
{
    public enum ConversationState
    {
        Idle,
        WaitingForDate,
        WaitingForSession,
        WaitingForCategory,
        WaitingForPayment
    }

    public class ConversationStateService
    {
        private readonly Dictionary<long, ConversationState> _userStates = new();
        private readonly Dictionary<long, Dictionary<string, string>> _userData = new();

        public ConversationState GetState(long userId)
        {
            return _userStates.TryGetValue(userId, out var state) ? state : ConversationState.Idle;
        }

        public void SetState(long userId, ConversationState state)
        {
            _userStates[userId] = state;
        }

        public string? GetData(long userId, string key)
        {
            if (_userData.TryGetValue(userId, out var data) && data.TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }

        public void SetData(long userId, string key, string value)
        {
            if (!_userData.ContainsKey(userId))
            {
                _userData[userId] = new Dictionary<string, string>();
            }
            _userData[userId][key] = value;
        }

        public void ClearUserData(long userId)
        {
            _userStates.Remove(userId);
            _userData.Remove(userId);
        }
    }
}