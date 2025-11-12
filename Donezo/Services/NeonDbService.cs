using Npgsql;
using Microsoft.Maui.Storage;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Donezo.Services;

public interface INeonDbService
{
    Task<string> PingAsync(CancellationToken ct = default);
    Task<bool> RegisterUserAsync(string username, string password, CancellationToken ct = default);
    Task<bool> AuthenticateUserAsync(string username, string password, CancellationToken ct = default);
}

public class NeonDbService : INeonDbService
{
    private readonly string _connectionString;
    private bool _schemaEnsured;
    private readonly ILogger<NeonDbService>? _logger;

#if DEBUG
    // Debug-only fallback. DO NOT commit real secrets for production usage.
    private const string DebugFallbackConnectionString = "postgresql://neondb_owner:npg_6dAFRg0tBGDT@ep-super-hat-ad6vip1b-pooler.c-2.us-east-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require";
#endif

    public NeonDbService(ILogger<NeonDbService>? logger = null)
    {
        _logger = logger;

        var raw =
            // 1. OS environment variable (desktop scenarios)
            Environment.GetEnvironmentVariable("NEON_CONNECTION_STRING")
            // 2. SecureStorage (mobile/device scenarios)
            ?? TryGetFromSecureStorage()
#if DEBUG
            // 3. Debug-only fallback constant (for quick local testing)
            ?? DebugFallbackConnectionString
#endif
            // 4. Fail if nothing available
            ?? throw new InvalidOperationException("Neon connection string not found. Set env var, store via SecureStorage, or (DEBUG) fallback.");

        _connectionString = NormalizeConnectionString(raw);

        // Kick off schema ensure in background so first usage has tables ready
        _ = InitializeInBackground();
    }

    private static string NormalizeConnectionString(string cs)
    {
        // URL-style -> convert to key=value pairs; drop channel_binding entirely
        if (cs.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            cs.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(cs, UriKind.Absolute, out var uri))
                return cs; // fallback

            var user = string.Empty;
            var pass = string.Empty;
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':', 2);
                user = Uri.UnescapeDataString(parts[0]);
                if (parts.Length > 1) pass = Uri.UnescapeDataString(parts[1]);
            }

            var db = uri.AbsolutePath.TrimStart('/');
            var host = uri.Host;
            var port = uri.IsDefaultPort ? 5432 : uri.Port;

            var b = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Username = user,
                Password = pass,
                Database = db
            };

            // Parse query params; ignore channel_binding, map sslmode
            if (!string.IsNullOrWhiteSpace(uri.Query))
            {
                var q = uri.Query.TrimStart('?');
                foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split('=', 2);
                    var key = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
                    var val = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;

                    switch (key)
                    {
                        case "sslmode":
                            if (Enum.TryParse<Npgsql.SslMode>(val, true, out var mode))
                                b.SslMode = mode;
                            else if (string.Equals(val, "require", StringComparison.OrdinalIgnoreCase))
                                b.SslMode = SslMode.Require;
                            break;
                        case "trust_server_certificate":
                            if (bool.TryParse(val, out var tsc)) b.TrustServerCertificate = tsc;
                            break;
                        case "channel_binding":
                            // ignore
                            break;
                        default:
                            // ignore unknowns to avoid builder exceptions
                            break;
                    }
                }
            }

            return b.ConnectionString;
        }

        // key=value form: strip channel binding entries before using the builder
        var sanitized = StripKeyValue(cs, ["channel_binding", "Channel Binding Mode"]);
        try
        {
            var b = new NpgsqlConnectionStringBuilder(sanitized);
            return b.ConnectionString;
        }
        catch
        {
            return sanitized;
        }
    }

    private static string StripKeyValue(string cs, string[] keysToRemove)
    {
        // Split on ';', remove entries whose key matches any in keysToRemove (case-insensitive)
        var parts = cs.Split(';');
        var kept = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            var kv = part.Split('=', 2);
            var key = kv[0].Trim();
            var skip = keysToRemove.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (!skip) kept.Add(part);
        }
        return string.Join(';', kept);
    }

    private async Task InitializeInBackground()
    {
        try
        {
            await EnsureSchemaAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed ensuring Neon schema on startup");
        }
    }

    public async Task<string> PingAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("select version();", conn);
        var version = (string?)await cmd.ExecuteScalarAsync(ct) ?? "unknown";
        return version;
    }

    public async Task<bool> RegisterUserAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return false;
        if (password.Length < 6) return false; // basic rule

        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Check if user exists
        await using (var checkCmd = new NpgsqlCommand("select 1 from users where username = @u", conn))
        {
            checkCmd.Parameters.AddWithValue("u", username);
            var exists = await checkCmd.ExecuteScalarAsync(ct) != null;
            if (exists) return false;
        }

        // Hash password
        var salt = GenerateSalt(16);
        var hash = HashPassword(password, salt, 100_000);

        await using (var insertCmd = new NpgsqlCommand("insert into users(username, password_hash, password_salt) values(@u,@h,@s)", conn))
        {
            insertCmd.Parameters.AddWithValue("u", username);
            insertCmd.Parameters.AddWithValue("h", hash);
            insertCmd.Parameters.AddWithValue("s", Convert.ToBase64String(salt));
            await insertCmd.ExecuteNonQueryAsync(ct);
        }
        _logger?.LogInformation("Registered user {Username}", username);
        return true;
    }

    public async Task<bool> AuthenticateUserAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return false;
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("select password_hash, password_salt from users where username = @u", conn);
        cmd.Parameters.AddWithValue("u", username);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return false;
        var storedHash = reader.GetString(0);
        var saltBase64 = reader.GetString(1);
        var salt = Convert.FromBase64String(saltBase64);

        var computed = HashPassword(password, salt, ExtractIterations(storedHash));
        var ok = ConstantTimeEquals(storedHash, computed);
        _logger?.LogInformation("Authentication {Result} for user {Username}", ok ? "success" : "fail", username);
        return ok;
    }

    // Store a connection string securely at runtime (e.g. first launch in dev)
    public static async Task StoreDevConnectionStringAsync(string conn)
    {
        await SecureStorage.SetAsync("NEON_CONNECTION_STRING", conn);
    }

    private static string? TryGetFromSecureStorage()
    {
        try
        {
            // Blocking call acceptable during initialization; alternatively refactor to async factory.
            return SecureStorage.GetAsync("NEON_CONNECTION_STRING").GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (_schemaEnsured) return;
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql =
            "create table if not exists users (" +
            " id serial primary key," +
            " username text not null unique," +
            " password_hash text not null," +
            " password_salt text not null," +
            " created_at timestamptz not null default now()" +
            ");";

        await using (var tableCmd = new NpgsqlCommand(sql, conn))
        {
            await tableCmd.ExecuteNonQueryAsync(ct);
        }

        _schemaEnsured = true;
        _logger?.LogInformation("Neon schema ensured (users table)");
    }

    private static byte[] GenerateSalt(int size)
    {
        var salt = new byte[size];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    // Format: PBKDF2$<algo>$<iterations>$<saltBase64>$<hashBase64>
    private static string HashPassword(string password, byte[] salt, int iterations)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32);
        return $"PBKDF2$sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static int ExtractIterations(string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length < 5) return 100_000;
        return int.TryParse(parts[2], out var it) ? it : 100_000;
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
