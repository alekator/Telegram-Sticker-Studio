using TL;
using WTelegram;

namespace StickerForge.Telegram;

/// <summary>The caller must provide an independently generated session key protected by the OS.</summary>
public sealed class TelegramLogin : IDisposable
{
    private readonly Client client;
    public TelegramLogin(int apiId, string apiHash, string sessionPath, string sessionKey)
    {
        if (apiId <= 0 || string.IsNullOrWhiteSpace(apiHash) || string.IsNullOrWhiteSpace(sessionPath) || string.IsNullOrWhiteSpace(sessionKey))
            throw new ArgumentException("API credentials, session path and an independently protected session key are required.");
        Helpers.Log = (_, _) => { }; // Do not emit potentially sensitive library diagnostics.
        client = new Client(what => what switch
        {
            "api_id" => apiId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "api_hash" => apiHash,
            "session_pathname" => sessionPath,
            "session_key" => sessionKey,
            _ => null!
        });
        client.FloodRetryThreshold = 0;
    }
    /// <summary>First supply a phone number; subsequent calls supply the requested code or 2FA password.</summary>
    public Task<string?> ContinueAsync(string input) => client.Login(input);
    public bool IsAuthenticated => client.UserId != 0;
    internal Client Client => client;
    public async Task LogoutAsync() => await client.Auth_LogOut();
    public void Dispose() => client.Dispose();
}
