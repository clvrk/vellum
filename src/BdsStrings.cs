
namespace Vellum
{
    static class BdsStrings
    {
        public const string Version = @"^.+ Version (\d+\.\d+\.\d+(?>\.\d+)?)";
        public const string ServerStarted = @"^.+ (Server started\.)";
        public const string PlayerConnected = @".+Player connected:\s(.+),";
        public const string PlayerDisconnected = @".+Player disconnected:\s(.+),";
    }
}
