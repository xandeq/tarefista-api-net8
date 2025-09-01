namespace Tarefista.Api.Services
{
    public class TokenBlacklistService
    {
        private readonly HashSet<string> _blacklistedTokens = new();

        public void BlacklistToken(string token) => _blacklistedTokens.Add(token);

        public bool IsTokenBlacklisted(string token) => _blacklistedTokens.Contains(token);
    }

}
