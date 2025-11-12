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
    Task<int?> GetUserIdAsync(string username, CancellationToken ct = default);
    Task<IReadOnlyList<ListRecord>> GetListsAsync(int userId, CancellationToken ct = default);
    Task<int> CreateListAsync(int userId, string name, CancellationToken ct = default);
    Task<IReadOnlyList<ItemRecord>> GetItemsAsync(int listId, CancellationToken ct = default);
    Task<int> AddItemAsync(int listId, string name, CancellationToken ct = default);
    Task<bool> SetItemCompletedAsync(int itemId, bool completed, CancellationToken ct = default);
    Task<bool> DeleteItemAsync(int itemId, CancellationToken ct = default);
    Task<bool> DeleteListAsync(int listId, CancellationToken ct = default);
    Task<int> ResetListAsync(int listId, CancellationToken ct = default);
}

public record ListRecord(int Id, string Name);
public record ItemRecord(int Id, string Name, bool IsCompleted);

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

            var userInfo = uri.UserInfo.Split(':', 2);
            var user = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
            var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";

            var db = uri.AbsolutePath.TrimStart('/');
            var b = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.IsDefaultPort ? 5432 : uri.Port,
                Username = user,
                Password = pass,
                Database = db,
                SslMode = SslMode.Require,
                TrustServerCertificate = true
            };

            // Parse query params; ignore channel_binding, map sslmode
            if (!string.IsNullOrWhiteSpace(uri.Query))
            {
                foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split('=', 2);
                    var key = kv[0].ToLowerInvariant();
                    var val = kv.Length > 1 ? kv[1] : "";

                    switch (key)
                    {
                        case "sslmode":
                            if (Enum.TryParse<SslMode>(val, true, out var mode)) b.SslMode = mode;
                            break;
                        case "trust_server_certificate":
                            if (bool.TryParse(val, out var tsc)) b.TrustServerCertificate = tsc;
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

    private static string StripKeyValue(string cs, string[] keys)
    {
        // Split on ';', remove entries whose key matches any in keysToRemove (case-insensitive)
        var parts = cs.Split(';');
        return string.Join(';', parts.Where(p => { var k = p.Split('=', 2)[0].Trim(); return !keys.Any(x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase)); }));
    }

    private async Task InitializeInBackground()
    {
        try
        {
            await EnsureSchemaAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Schema init failed");
        }
    }

    public async Task<string> PingAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("select version();", conn); return (string?)await cmd.ExecuteScalarAsync(ct) ?? "unknown";
    }

    public async Task<bool> RegisterUserAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || password.Length < 6) return false;
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);

        // Check if user exists
        await using (var check = new NpgsqlCommand("select 1 from users where username=@u", conn))
        {
            check.Parameters.AddWithValue("u", username);
            if (await check.ExecuteScalarAsync(ct) != null) return false;
        }

        // Hash password
        var salt = GenerateSalt(16);
        var hash = HashPassword(password, salt, 100_000);

        await using (var ins = new NpgsqlCommand("insert into users(username,password_hash,password_salt) values(@u,@h,@s)", conn))
        {
            ins.Parameters.AddWithValue("u", username);
            ins.Parameters.AddWithValue("h", hash);
            ins.Parameters.AddWithValue("s", Convert.ToBase64String(salt));
            await ins.ExecuteNonQueryAsync(ct);
        }
        _logger?.LogInformation("Registered user {Username}", username);
        return true;
    }

    public async Task<bool> AuthenticateUserAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return false;
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("select password_hash,password_salt from users where username=@u", conn); cmd.Parameters.AddWithValue("u", username);
        await using var reader = await cmd.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) return false;
        var storedHash = reader.GetString(0); var salt = Convert.FromBase64String(reader.GetString(1));
        var computed = HashPassword(password, salt, ExtractIterations(storedHash));
        var ok = ConstantTimeEquals(storedHash, computed); _logger?.LogInformation("Auth {Result} for {User}", ok?"ok":"fail", username);
        return ok;
    }

    public async Task<int?> GetUserIdAsync(string username, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("select id from users where username=@u", conn); cmd.Parameters.AddWithValue("u", username);
        var result = await cmd.ExecuteScalarAsync(ct); return result is int id ? id : null;
    }

    public async Task<IReadOnlyList<ListRecord>> GetListsAsync(int userId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        var list = new List<ListRecord>();
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("select id,name from lists where user_id=@u order by id", conn); cmd.Parameters.AddWithValue("u", userId);
        await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) list.Add(new ListRecord(r.GetInt32(0), r.GetString(1))); return list;
    }

    public async Task<int> CreateListAsync(int userId, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("List name required", nameof(name));
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("insert into lists(user_id,name) values(@u,@n) returning id", conn); cmd.Parameters.AddWithValue("u", userId); cmd.Parameters.AddWithValue("n", name);
        var id = (int)await cmd.ExecuteScalarAsync(ct); return id;
    }

    public async Task<IReadOnlyList<ItemRecord>> GetItemsAsync(int listId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        var items = new List<ItemRecord>();
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("select id,name,is_completed from items where list_id=@l order by id", conn); cmd.Parameters.AddWithValue("l", listId);
        await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) items.Add(new ItemRecord(r.GetInt32(0), r.GetString(1), r.GetBoolean(2))); return items;
    }

    public async Task<int> AddItemAsync(int listId, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Item name required", nameof(name));
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("insert into items(list_id,name) values(@l,@n) returning id", conn); cmd.Parameters.AddWithValue("l", listId); cmd.Parameters.AddWithValue("n", name);
        var id = (int)await cmd.ExecuteScalarAsync(ct); return id;
    }

    public async Task<bool> SetItemCompletedAsync(int itemId, bool completed, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("update items set is_completed=@c where id=@i", conn); cmd.Parameters.AddWithValue("c", completed); cmd.Parameters.AddWithValue("i", itemId);
        var rows = await cmd.ExecuteNonQueryAsync(ct); return rows == 1;
    }

    public async Task<bool> DeleteItemAsync(int itemId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("delete from items where id=@i", conn); cmd.Parameters.AddWithValue("i", itemId);
        var rows = await cmd.ExecuteNonQueryAsync(ct); return rows == 1;
    }

    public async Task<bool> DeleteListAsync(int listId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("delete from lists where id=@l", conn); cmd.Parameters.AddWithValue("l", listId);
        var rows = await cmd.ExecuteNonQueryAsync(ct); return rows == 1;
    }

    public async Task<int> ResetListAsync(int listId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("update items set is_completed=false where list_id=@l", conn); cmd.Parameters.AddWithValue("l", listId);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    // Store a connection string securely at runtime (e.g. first launch in dev)
    public static async Task StoreDevConnectionStringAsync(string conn) => await SecureStorage.SetAsync("NEON_CONNECTION_STRING", conn);
    private static string? TryGetFromSecureStorage() { try { return SecureStorage.GetAsync("NEON_CONNECTION_STRING").GetAwaiter().GetResult(); } catch { return null; } }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (_schemaEnsured) return;
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        var sql = @"create table if not exists users (
 id serial primary key,
 username text not null unique,
 password_hash text not null,
 password_salt text not null,
 created_at timestamptz not null default now());
create table if not exists lists (
 id serial primary key,
 user_id int not null references users(id) on delete cascade,
 name text not null,
 created_at timestamptz not null default now(),
 constraint uq_lists_user_name unique(user_id,name));
create table if not exists items (
 id serial primary key,
 list_id int not null references lists(id) on delete cascade,
 name text not null,
 is_completed boolean not null default false,
 created_at timestamptz not null default now());";
        await using (var cmd = new NpgsqlCommand(sql, conn)) { await cmd.ExecuteNonQueryAsync(ct); }
        _schemaEnsured = true; _logger?.LogInformation("Schema ensured (users, lists, items)");
    }

    private static byte[] GenerateSalt(int size) { var salt = new byte[size]; RandomNumberGenerator.Fill(salt); return salt; }
    private static string HashPassword(string password, byte[] salt, int iterations)
    { using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256); var hash = pbkdf2.GetBytes(32); return $"PBKDF2$sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}"; }
    private static int ExtractIterations(string stored) { var parts = stored.Split('$'); return parts.Length >= 5 && int.TryParse(parts[2], out var it) ? it : 100_000; }
    private static bool ConstantTimeEquals(string a, string b) { if (a.Length != b.Length) return false; var diff = 0; for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i]; return diff == 0; }
}
