using VKBot_nordciti.Services;

namespace VKBot_nordciti.Services
{
    public class DataInitializer : IDataInitializer
    {
        private readonly KeyboardProvider _keyboardProvider;

        public DataInitializer(KeyboardProvider keyboardProvider)
        {
            _keyboardProvider = keyboardProvider;
        }

        public async Task InitializeAsync()
        {
            // Временная заглушка - в реальности будет работать с БД
            await Task.CompletedTask;
            Console.WriteLine("Data initializer called");
        }
    }
}