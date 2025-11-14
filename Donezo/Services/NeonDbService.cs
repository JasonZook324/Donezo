using Npgsql;
using Microsoft.Maui.Storage;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace Donezo.Services;

public interface INeonDbService
{
    Task<string> PingAsync(CancellationToken ct = default);
    Task<bool> RegisterUserAsync(string username, string password, string email, string firstName, string lastName, CancellationToken ct = default);
    Task<bool> AuthenticateUserAsync(string username, string password, CancellationToken ct = default);
    Task<int?> GetUserIdAsync(string username, CancellationToken ct = default);
    Task<IReadOnlyList<ListRecord>> GetListsAsync(int userId, CancellationToken ct = default);
    Task<int> CreateListAsync(int userId, string name, bool isDaily, CancellationToken ct = default);
    Task<int> AddItemAsync(int listId, string name, CancellationToken ct = default);
    Task<IReadOnlyList<ItemRecord>> GetItemsAsync(int listId, CancellationToken ct = default);
    Task<int> AddChildItemAsync(int listId, string name, int parentItemId, long expectedRevision, CancellationToken ct = default);
    Task<bool> SetItemCompletedAsync(int itemId, bool completed, CancellationToken ct = default);
    Task<(bool Ok, long NewRevision)> DeleteItemAsync(int itemId, long expectedRevision, CancellationToken ct = default);
    Task<bool> DeleteListAsync(int listId, CancellationToken ct = default);
    Task<int> ResetListAsync(int listId, CancellationToken ct = default);
    Task<bool> SetListDailyAsync(int listId, bool isDaily, CancellationToken ct = default);
    Task<long> GetListRevisionAsync(int listId, CancellationToken ct = default);
    Task<bool?> GetUserThemeDarkAsync(int userId, CancellationToken ct = default);
    Task SetUserThemeDarkAsync(int userId, bool dark, CancellationToken ct = default);
    Task<ItemRecord?> GetItemAsync(int itemId, CancellationToken ct = default);
    Task<IReadOnlyList<ItemRecord>> GetChildrenAsync(int parentItemId, CancellationToken ct = default);
    Task<int> GetDescendantCountAsync(int itemId, CancellationToken ct = default);
    Task<(bool Ok, long NewRevision)> MoveItemAsync(int itemId, int? newParentItemId, long expectedRevision, CancellationToken ct = default);
    Task<(bool Ok, long NewRevision)> SetItemOrderAsync(int itemId, int newOrder, long expectedRevision, CancellationToken ct = default);
    Task<bool> GetItemExpandedAsync(int userId, int itemId, CancellationToken ct = default);
    Task SetItemExpandedAsync(int userId, int itemId, bool expanded, CancellationToken ct = default);
    Task<IDictionary<int, bool>> GetExpandedStatesAsync(int userId, int listId, CancellationToken ct = default);
    Task<(bool Ok, long NewRevision)> RenameItemAsync(int itemId, string newName, long expectedRevision, CancellationToken ct = default);
    Task<(bool Ok, long NewRevision, int Affected)> ResetSubtreeAsync(int rootItemId, long expectedRevision, CancellationToken ct = default);
}

public record ListRecord(int Id, string Name, bool IsDaily);
public record ItemRecord(int Id, int ListId, string Name, bool IsCompleted, int? ParentItemId, bool HasChildren, int ChildrenCount, int IncompleteChildrenCount, int Level, string SortKey, int Order);

public class NeonDbService : INeonDbService
{
    internal const int MaxDepth = 3;
    internal const int OrderStep = 1024;

    private readonly string _connectionString;
    private bool _schemaEnsured;
    private readonly ILogger<NeonDbService>? _logger;

    private const string DebugFallbackConnectionString = "postgresql://neondb_owner:npg_6dAFRg0tBGDT@ep-super-hat-ad6vip1b-pooler.c-2.us-east-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require";

    public NeonDbService(ILogger<NeonDbService>? logger = null)
    {
        _logger = logger;
        var raw = Environment.GetEnvironmentVariable("NEON_CONNECTION_STRING") ?? TryGetFromSecureStorage() ?? DebugFallbackConnectionString;
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("Neon connection string not found.");
        _connectionString = NormalizeConnectionString(raw);
        _ = InitializeInBackground();
    }

    private async Task InitializeInBackground()
    {
        try { await EnsureSchemaAsync(CancellationToken.None); }
        catch (Exception ex) { _logger?.LogError(ex, "Schema init failed"); }
    }

    private static string NormalizeConnectionString(string cs)
    {
        if (cs.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || cs.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(cs, UriKind.Absolute, out var uri)) return cs;
            var userInfo = uri.UserInfo.Split(':', 2);
            var user = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
            var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
            var db = uri.AbsolutePath.TrimStart('/');
            var b = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.IsDefaultPort ? 5432 : uri.Port,
                Username = user,
                Password = pass,
                Database = db,
                SslMode = SslMode.Require
            };
            if (!string.IsNullOrWhiteSpace(uri.Query))
            {
                foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split('=', 2);
                    var key = kv[0].ToLowerInvariant();
                    var val = kv.Length > 1 ? kv[1] : string.Empty;
                    if (key == "sslmode" && Enum.TryParse<SslMode>(val, true, out var mode)) b.SslMode = mode;
                }
            }
            return b.ConnectionString;
        }
        var parts = cs.Split(';');
        var sanitized = string.Join(';', parts.Where(p =>
        {
            var k = p.Split('=', 2)[0].Trim();
            return !string.Equals(k, "channel_binding", StringComparison.OrdinalIgnoreCase) && !string.Equals(k, "Channel Binding Mode", StringComparison.OrdinalIgnoreCase);
        }));
        try { return new NpgsqlConnectionStringBuilder(sanitized).ConnectionString; } catch { return sanitized; }
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (_schemaEnsured) return;
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var sql = @"create table if not exists users ( id serial primary key, username text not null unique, email text, first_name text, last_name text, password_hash text not null, password_salt text not null, created_at timestamptz not null default now()); create table if not exists lists ( id serial primary key, user_id int not null references users(id) on delete cascade, name text not null, created_at timestamptz not null default now(), constraint uq_lists_user_name unique(user_id,name)); create table if not exists items ( id serial primary key, list_id int not null references lists(id) on delete cascade, name text not null, is_completed boolean not null default false, created_at timestamptz not null default now()); alter table if exists lists add column if not exists is_daily boolean not null default false; alter table if exists lists add column if not exists last_reset_date date; alter table if exists items add column if not exists parent_item_id int references items(id) on delete cascade; alter table if exists items add column if not exists ""order"" int not null default 0; create index if not exists ix_items_list_parent_order on items(list_id, parent_item_id, ""order""); alter table if exists lists add column if not exists revision bigint not null default 0; create table if not exists user_prefs ( user_id int primary key references users(id) on delete cascade, theme_dark boolean not null default false ); create unique index if not exists ux_users_email_lower on users ((lower(email))) where email is not null; create table if not exists item_ui_state ( user_id int not null references users(id) on delete cascade, item_id int not null references items(id) on delete cascade, expanded boolean not null default true, primary key(user_id,item_id));";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
        _schemaEnsured = true;
    }

    public async Task<string> PingAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("select version();", conn);
        return (string?)await cmd.ExecuteScalarAsync(ct) ?? "unknown";
    }

    public async Task<bool> RegisterUserAsync(string username, string password, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || password.Length < 6) return false;
        if (string.IsNullOrWhiteSpace(email) || !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) return false;
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName)) return false;
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using (var check = new NpgsqlCommand("select 1 from users where lower(username)=lower(@u) or lower(email)=lower(@e)", conn))
        {
            check.Parameters.AddWithValue("u", username);
            check.Parameters.AddWithValue("e", email);
            if (await check.ExecuteScalarAsync(ct) != null) return false;
        }
        var salt = GenerateSalt(16);
        var hash = HashPassword(password, salt, 100_000);
        await using (var ins = new NpgsqlCommand("insert into users(username,email,first_name,last_name,password_hash,password_salt) values(@u,@e,@f,@l,@h,@s)", conn))
        {
            ins.Parameters.AddWithValue("u", username);
            ins.Parameters.AddWithValue("e", email);
            ins.Parameters.AddWithValue("f", firstName);
            ins.Parameters.AddWithValue("l", lastName);
            ins.Parameters.AddWithValue("h", hash);
            ins.Parameters.AddWithValue("s", Convert.ToBase64String(salt));
            await ins.ExecuteNonQueryAsync(ct);
        }
        return true;
    }

    public async Task<bool> AuthenticateUserAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return false;
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("select password_hash,password_salt from users where username=@u", conn);
        cmd.Parameters.AddWithValue("u", username);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return false;
        var storedHash = r.GetString(0);
        var salt = Convert.FromBase64String(r.GetString(1));
        var computed = HashPassword(password, salt, ExtractIterations(storedHash));
        return ConstantTimeEquals(storedHash, computed);
    }

    public async Task<int?> GetUserIdAsync(string username, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("select id from users where username=@u", conn);
        cmd.Parameters.AddWithValue("u", username);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is int id ? id : null;
    }

    public async Task<IReadOnlyList<ListRecord>> GetListsAsync(int userId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        var list = new List<ListRecord>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("select id,name,is_daily from lists where user_id=@u order by id", conn);
        cmd.Parameters.AddWithValue("u", userId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new ListRecord(r.GetInt32(0), r.GetString(1), r.GetBoolean(2)));
        return list;
    }

    public async Task<int> CreateListAsync(int userId, string name, bool isDaily, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("List name required", nameof(name));
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("insert into lists(user_id,name,is_daily,last_reset_date) values(@u,@n,@d,current_date) returning id", conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("n", name);
        cmd.Parameters.AddWithValue("d", isDaily);
        return (int)await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<int> AddItemAsync(int listId, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Item name required", nameof(name));
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        // Determine next sparse order for root items (parent null)
        int order;
        await using (var ordCmd = new NpgsqlCommand("select coalesce(max(\"order\"),0) from items where list_id=@l and parent_item_id is null", conn))
        {
            ordCmd.Parameters.AddWithValue("l", listId);
            var maxObj = await ordCmd.ExecuteScalarAsync(ct);
            var maxOrder = maxObj is int mo ? mo : 0;
            order = maxOrder + OrderStep;
        }
        int newId;
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            await using (var ins = new NpgsqlCommand("insert into items(list_id,name,\"order\") values(@l,@n,@o) returning id", conn, tx))
            {
                ins.Parameters.AddWithValue("l", listId);
                ins.Parameters.AddWithValue("n", name);
                ins.Parameters.AddWithValue("o", order);
                newId = (int)await ins.ExecuteScalarAsync(ct);
            }
            // bump revision so clients refresh
            await using (var bump = new NpgsqlCommand("update lists set revision=revision+1 where id=@l", conn, tx))
            {
                bump.Parameters.AddWithValue("l", listId);
                await bump.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        return newId;
    }

    public async Task<IReadOnlyList<ItemRecord>> GetItemsAsync(int listId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using (var chk = new NpgsqlCommand("select is_daily,last_reset_date,current_date from lists where id=@l", conn))
        {
            chk.Parameters.AddWithValue("l", listId);
            await using var rr = await chk.ExecuteReaderAsync(ct);
            if (await rr.ReadAsync(ct))
            {
                var isDaily = rr.GetBoolean(0);
                var lastReset = rr.IsDBNull(1) ? (DateOnly?)null : DateOnly.FromDateTime(rr.GetDateTime(1));
                var today = DateOnly.FromDateTime(rr.GetDateTime(2));
                if (isDaily && (lastReset == null || lastReset.Value < today))
                {
                    await rr.DisposeAsync();
                    await using var tx = await conn.BeginTransactionAsync(ct);
                    await using (var u1 = new NpgsqlCommand("update items set is_completed=false where list_id=@l", conn, tx))
                    {
                        u1.Parameters.AddWithValue("l", listId);
                        await u1.ExecuteNonQueryAsync(ct);
                    }
                    await using (var u2 = new NpgsqlCommand("update lists set last_reset_date=current_date where id=@l", conn, tx))
                    {
                        u2.Parameters.AddWithValue("l", listId);
                        await u2.ExecuteNonQueryAsync(ct);
                    }
                    await tx.CommitAsync(ct);
                }
            }
        }
        var sql = @"with recursive tree as ( select i.id,i.list_id,i.name,i.is_completed,i.parent_item_id,i.""order"", (select count(*) from items c where c.parent_item_id=i.id) children_count, (select count(*) from items c where c.parent_item_id=i.id and c.is_completed=false) incomplete_children_count, 1 as level, lpad(i.""order""::text,10,'0') as sort_key from items i where i.list_id=@list and i.parent_item_id is null union all select c.id,c.list_id,c.name,c.is_completed,c.parent_item_id,c.""order"", (select count(*) from items cc where cc.parent_item_id=c.id) children_count, (select count(*) from items cc where cc.parent_item_id=c.id and cc.is_completed=false) incomplete_children_count, p.level+1 as level, p.sort_key || '-' || lpad(c.""order""::text,10,'0') as sort_key from items c join tree p on c.parent_item_id=p.id ) select * from tree order by sort_key;";
        var items = new List<ItemRecord>();
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("list", listId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var id = r.GetInt32(0);
                var listIdVal = r.GetInt32(1);
                var name = r.GetString(2);
                var completed = r.GetBoolean(3);
                var parent = r.IsDBNull(4) ? (int?)null : r.GetInt32(4);
                var order = r.GetInt32(5);
                var childrenCount = r.GetInt32(6);
                var incompleteChildrenCount = r.GetInt32(7);
                var level = r.GetInt32(8);
                var sortKey = r.GetString(9);
                var hasChildren = childrenCount > 0;
                items.Add(new ItemRecord(id, listIdVal, name, completed, parent, hasChildren, childrenCount, incompleteChildrenCount, level, sortKey, order));
            }
        }
        return items;
    }

    public async Task<bool> SetItemCompletedAsync(int itemId, bool completed, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        int listId;
        int? parentId;
        bool currentCompleted;
        await using (var info = new NpgsqlCommand("select list_id,parent_item_id,is_completed from items where id=@i", conn))
        {
            info.Parameters.AddWithValue("i", itemId);
            await using var r = await info.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return false;
            listId = r.GetInt32(0);
            parentId = r.IsDBNull(1) ? null : r.GetInt32(1);
            currentCompleted = r.GetBoolean(2);
        }
        if (currentCompleted == completed) return true;
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (completed)
        {
            await using (var chkChildren = new NpgsqlCommand("select count(*) from items where parent_item_id=@i and is_completed=false", conn, tx))
            {
                chkChildren.Parameters.AddWithValue("i", itemId);
                var cntObj = await chkChildren.ExecuteScalarAsync(ct);
                var cnt = cntObj is long l ? (int)l : cntObj is int i ? i : 0;
                if (cnt > 0) { await tx.RollbackAsync(ct); return false; }
            }
            await using (var upd = new NpgsqlCommand("update items set is_completed=true where id=@i", conn, tx))
            {
                upd.Parameters.AddWithValue("i", itemId);
                await upd.ExecuteNonQueryAsync(ct);
            }
            if (parentId != null)
            {
                await using (var allDone = new NpgsqlCommand("select count(*) from items where parent_item_id=@p and is_completed=false", conn, tx))
                {
                    allDone.Parameters.AddWithValue("p", parentId.Value);
                    var remainObj = await allDone.ExecuteScalarAsync(ct);
                    var remain = remainObj is long l2 ? (int)l2 : remainObj is int i2 ? i2 : 0;
                    if (remain == 0)
                    {
                        await using var updParent = new NpgsqlCommand("update items set is_completed=true where id=@p", conn, tx);
                        updParent.Parameters.AddWithValue("p", parentId.Value);
                        await updParent.ExecuteNonQueryAsync(ct);
                    }
                }
            }
        }
        else
        {
            await using (var upd = new NpgsqlCommand("update items set is_completed=false where id=@i", conn, tx))
            {
                upd.Parameters.AddWithValue("i", itemId);
                await upd.ExecuteNonQueryAsync(ct);
            }
            var currentParent = parentId;
            while (currentParent != null)
            {
                await using (var updAnc = new NpgsqlCommand("update items set is_completed=false where id=@p", conn, tx))
                {
                    updAnc.Parameters.AddWithValue("p", currentParent.Value);
                    await updAnc.ExecuteNonQueryAsync(ct);
                }
                int? nextParent = null;
                await using (var getp = new NpgsqlCommand("select parent_item_id from items where id=@p", conn, tx))
                {
                    getp.Parameters.AddWithValue("p", currentParent.Value);
                    var res = await getp.ExecuteScalarAsync(ct);
                    nextParent = res is int ip ? (int?)ip : null;
                }
                currentParent = nextParent;
            }
        }
        await using (var bump = new NpgsqlCommand("update lists set revision=revision+1 where id=@l", conn, tx))
        {
            bump.Parameters.AddWithValue("l", listId);
            await bump.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<(bool Ok, long NewRevision)> DeleteItemAsync(int itemId, long expectedRevision, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        int? listId = null;
        await using (var get = new NpgsqlCommand("select list_id from items where id=@i", conn))
        {
            get.Parameters.AddWithValue("i", itemId);
            var res = await get.ExecuteScalarAsync(ct);
            if (res is int li) listId = li; else return (false, 0);
        }
        long currentRevision;
        await using (var rev = new NpgsqlCommand("select revision from lists where id=@l", conn))
        {
            rev.Parameters.AddWithValue("l", listId!.Value);
            var res = await rev.ExecuteScalarAsync(ct);
            currentRevision = res is long rv ? rv : 0L;
        }
        if (currentRevision != expectedRevision) return (false, currentRevision);
        long newRevision;
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var del = new NpgsqlCommand("delete from items where id=@i", conn, tx))
        {
            del.Parameters.AddWithValue("i", itemId);
            if (await del.ExecuteNonQueryAsync(ct) == 0)
            {
                await tx.RollbackAsync(ct);
                return (false, currentRevision);
            }
        }
        await using (var upd = new NpgsqlCommand("update lists set revision=revision+1 where id=@l returning revision", conn, tx))
        {
            upd.Parameters.AddWithValue("l", listId!.Value);
            var res = await upd.ExecuteScalarAsync(ct);
            newRevision = res is long rv2 ? rv2 : currentRevision + 1;
        }
        await tx.CommitAsync(ct);
        return (true, newRevision);
    }

    public async Task<bool> DeleteListAsync(int listId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("delete from lists where id=@l", conn);
        cmd.Parameters.AddWithValue("l", listId);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<int> ResetListAsync(int listId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        int affected;
        await using (var clr = new NpgsqlCommand("update items set is_completed=false where list_id=@l", conn, tx))
        {
            clr.Parameters.AddWithValue("l", listId);
            affected = await clr.ExecuteNonQueryAsync(ct);
        }
        await using (var upd = new NpgsqlCommand("update lists set last_reset_date=current_date where id=@l", conn, tx))
        {
            upd.Parameters.AddWithValue("l", listId);
            await upd.ExecuteNonQueryAsync(ct);
        }
        await using (var rev = new NpgsqlCommand("update lists set revision=revision+1 where id=@l", conn, tx))
        {
            rev.Parameters.AddWithValue("l", listId);
            await rev.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return affected;
    }

    public async Task<bool> SetListDailyAsync(int listId, bool isDaily, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("update lists set is_daily=@d, last_reset_date=case when @d then current_date else last_reset_date end where id=@l", conn);
        cmd.Parameters.AddWithValue("d", isDaily);
        cmd.Parameters.AddWithValue("l", listId);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<long> GetListRevisionAsync(int listId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("select revision from lists where id=@l", conn);
        cmd.Parameters.AddWithValue("l", listId);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is long rv ? rv : 0L;
    }

    public async Task<bool?> GetUserThemeDarkAsync(int userId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("select theme_dark from user_prefs where user_id=@u", conn);
        cmd.Parameters.AddWithValue("u", userId);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is DBNull || result is null) return null;
        return (bool)result;
    }

    public async Task SetUserThemeDarkAsync(int userId, bool dark, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"insert into user_prefs(user_id,theme_dark) values(@u,@d) on conflict(user_id) do update set theme_dark=excluded.theme_dark", conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("d", dark);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ItemRecord?> GetItemAsync(int itemId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var sql = @"select i.id,i.list_id,i.name,i.is_completed,i.parent_item_id,i.""order"", (select count(*) from items c where c.parent_item_id=i.id) children_count, (select count(*) from items c where c.parent_item_id=i.id and c.is_completed=false) incomplete_children_count from items i where i.id=@i";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("i", itemId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var id = r.GetInt32(0);
        var listIdVal = r.GetInt32(1);
        var name = r.GetString(2);
        var completed = r.GetBoolean(3);
        var parent = r.IsDBNull(4) ? (int?)null : r.GetInt32(4);
        var order = r.GetInt32(5);
        var childrenCount = r.GetInt32(6);
        var incompleteChildrenCount = r.GetInt32(7);
        var hasChildren = childrenCount > 0;
        return new ItemRecord(id, listIdVal, name, completed, parent, hasChildren, childrenCount, incompleteChildrenCount, 0, string.Empty, order);
    }

    public async Task<IReadOnlyList<ItemRecord>> GetChildrenAsync(int parentItemId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var sql = @"select i.id,i.list_id,i.name,i.is_completed,i.parent_item_id,i.""order"", (select count(*) from items c where c.parent_item_id=i.id) children_count, (select count(*) from items c where c.parent_item_id=i.id and c.is_completed=false) incomplete_children_count from items i where i.parent_item_id=@p order by i.""order""";
        var list = new List<ItemRecord>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("p", parentItemId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var id = r.GetInt32(0);
            var listIdVal = r.GetInt32(1);
            var name = r.GetString(2);
            var completed = r.GetBoolean(3);
            var parent = r.IsDBNull(4) ? (int?)null : r.GetInt32(4);
            var order = r.GetInt32(5);
            var childrenCount = r.GetInt32(6);
            var incompleteChildrenCount = r.GetInt32(7);
            var hasChildren = childrenCount > 0;
            list.Add(new ItemRecord(id, listIdVal, name, completed, parent, hasChildren, childrenCount, incompleteChildrenCount, 0, string.Empty, order));
        }
        return list;
    }

    public async Task<int> GetDescendantCountAsync(int itemId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var sql = "with recursive d as (select id from items where parent_item_id=@i union all select c.id from items c join d on c.parent_item_id=d.id) select count(*) from d";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("i", itemId);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is long l ? (int)l : res is int i ? i : 0;
    }

    private async Task<int> GetItemDepthAsync(NpgsqlConnection conn, int itemId, CancellationToken ct)
    {
        var sql = "with recursive p as (select id,parent_item_id,1 lvl from items where id=@i union all select it.id,it.parent_item_id,p.lvl+1 from items it join p on it.id=p.parent_item_id) select max(lvl) from p";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("i", itemId);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is int i ? i : res is long l ? (int)l : 1;
    }

    private async Task<int> GetSubtreeDepthAsync(NpgsqlConnection conn, int itemId, CancellationToken ct)
    {
        var sql = "with recursive t as (select id,parent_item_id,1 lvl from items where id=@i union all select c.id,c.parent_item_id,t.lvl+1 from items c join t on c.parent_item_id=t.id) select max(lvl) from t";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("i", itemId);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is int i ? i : res is long l ? (int)l : 1;
    }

    private async Task<long> GetListRevisionInternalAsync(NpgsqlConnection conn, int listId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("select revision from lists where id=@l", conn);
        cmd.Parameters.AddWithValue("l", listId);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is long rv ? rv : 0L;
    }

    private async Task<long> IncrementRevisionAsync(NpgsqlConnection conn, int listId, NpgsqlTransaction tx, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("update lists set revision=revision+1 where id=@l returning revision", conn, tx);
        cmd.Parameters.AddWithValue("l", listId);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is long rv ? rv : 0L;
    }

    public async Task<int> AddChildItemAsync(int listId, string name, int parentItemId, long expectedRevision, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Item name required", nameof(name));
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var currentRevision = await GetListRevisionInternalAsync(conn, listId, ct);
        if (currentRevision != expectedRevision) throw new InvalidOperationException("Concurrency mismatch");
        await using (var chk = new NpgsqlCommand("select list_id from items where id=@pid", conn))
        {
            chk.Parameters.AddWithValue("pid", parentItemId);
            var ls = await chk.ExecuteScalarAsync(ct);
            if (ls is int pl)
            {
                if (pl != listId) throw new InvalidOperationException("Parent belongs to different list");
            }
            else throw new InvalidOperationException("Parent not found");
        }
        var parentDepth = await GetItemDepthAsync(conn, parentItemId, ct);
        if (parentDepth + 1 > MaxDepth) throw new InvalidOperationException("Depth limit exceeded");
        int order;
        await using (var ordCmd = new NpgsqlCommand("select coalesce(max(\"order\"),0) from items where list_id=@l and parent_item_id=@p", conn))
        {
            ordCmd.Parameters.AddWithValue("l", listId);
            ordCmd.Parameters.AddWithValue("p", parentItemId);
            var maxObj = await ordCmd.ExecuteScalarAsync(ct);
            var maxOrder = maxObj is int mo ? mo : 0;
            order = maxOrder + OrderStep;
        }
        int newId;
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            await using (var ins = new NpgsqlCommand("insert into items(list_id,name,parent_item_id,\"order\") values(@l,@n,@p,@o) returning id", conn, tx))
            {
                ins.Parameters.AddWithValue("l", listId);
                ins.Parameters.AddWithValue("n", name);
                ins.Parameters.AddWithValue("p", parentItemId);
                ins.Parameters.AddWithValue("o", order);
                newId = (int)await ins.ExecuteScalarAsync(ct);
            }
            await IncrementRevisionAsync(conn, listId, tx, ct);
            await tx.CommitAsync(ct);
        }
        return newId;
    }

    public async Task<(bool Ok, long NewRevision)> MoveItemAsync(int itemId, int? newParentItemId, long expectedRevision, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        int listId; int? currentParent;
        await using (var info = new NpgsqlCommand("select list_id,parent_item_id from items where id=@i", conn))
        {
            info.Parameters.AddWithValue("i", itemId);
            await using var r = await info.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return (false, 0);
            listId = r.GetInt32(0);
            currentParent = r.IsDBNull(1) ? null : r.GetInt32(1);
        }
        var currentRevision = await GetListRevisionInternalAsync(conn, listId, ct);
        if (currentRevision != expectedRevision) return (false, currentRevision);
        if (newParentItemId != null)
        {
            if (newParentItemId.Value == itemId) return (false, currentRevision);
            var cycSql = "with recursive d as (select id from items where parent_item_id=@root union all select c.id from items c join d on c.parent_item_id=d.id) select 1 from d where id=@pid limit 1";
            await using var cycCmd = new NpgsqlCommand(cycSql, conn);
            cycCmd.Parameters.AddWithValue("root", itemId);
            cycCmd.Parameters.AddWithValue("pid", newParentItemId.Value);
            var cyc = await cycCmd.ExecuteScalarAsync(ct);
            if (cyc != null) return (false, currentRevision);
        }
        int subtreeDepth = await GetSubtreeDepthAsync(conn, itemId, ct);
        int newParentDepth = 0;
        if (newParentItemId != null)
        {
            newParentDepth = await GetItemDepthAsync(conn, newParentItemId.Value, ct);
            await using (var chk = new NpgsqlCommand("select list_id from items where id=@pid", conn))
            {
                chk.Parameters.AddWithValue("pid", newParentItemId.Value);
                var ls = await chk.ExecuteScalarAsync(ct);
                if (ls is int pl && pl != listId) return (false, currentRevision);
            }
        }
        if (newParentDepth + subtreeDepth > MaxDepth) return (false, currentRevision);
        int newOrder;
        await using (var ordCmd = new NpgsqlCommand("select coalesce(max(\"order\"),0) from items where list_id=@l and parent_item_id is not distinct from @p", conn))
        {
            ordCmd.Parameters.AddWithValue("l", listId);
            if (newParentItemId == null)
                ordCmd.Parameters.AddWithValue("p", DBNull.Value);
            else
                ordCmd.Parameters.AddWithValue("p", newParentItemId.Value);
            var maxObj = await ordCmd.ExecuteScalarAsync(ct);
            var maxOrder = maxObj is int mo ? mo : 0;
            newOrder = maxOrder + OrderStep;
        }
        long newRevision;
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            await using (var upd = new NpgsqlCommand("update items set parent_item_id=@p,\"order\"=@o where id=@i", conn, tx))
            {
                upd.Parameters.AddWithValue("p", (object?)newParentItemId ?? DBNull.Value);
                upd.Parameters.AddWithValue("o", newOrder);
                upd.Parameters.AddWithValue("i", itemId);
                await upd.ExecuteNonQueryAsync(ct);
            }
            newRevision = await IncrementRevisionAsync(conn, listId, tx, ct);
            await tx.CommitAsync(ct);
        }
        return (true, newRevision);
    }

    public async Task<(bool Ok, long NewRevision)> SetItemOrderAsync(int itemId, int newOrder, long expectedRevision, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        int listId; int? parentId;
        await using (var info = new NpgsqlCommand("select list_id,parent_item_id from items where id=@i", conn))
        {
            info.Parameters.AddWithValue("i", itemId);
            await using var r = await info.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return (false, 0);
            listId = r.GetInt32(0);
            parentId = r.IsDBNull(1) ? null : r.GetInt32(1);
        }
        var currentRevision = await GetListRevisionInternalAsync(conn, listId, ct);
        if (currentRevision != expectedRevision) return (false, currentRevision);
        long newRevision;
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            await using (var upd = new NpgsqlCommand("update items set \"order\"=@o where id=@i", conn, tx))
            {
                upd.Parameters.AddWithValue("o", newOrder);
                upd.Parameters.AddWithValue("i", itemId);
                await upd.ExecuteNonQueryAsync(ct);
            }
            var respaceSql = "with ord as ( select id, row_number() over (order by \"order\", id) rn from items where list_id=@l and parent_item_id is not distinct from @p ) update items i set \"order\" = ord.rn * @step from ord where i.id = ord.id and i.list_id=@l;";
            await using (var respace = new NpgsqlCommand(respaceSql, conn, tx))
            {
                respace.Parameters.AddWithValue("l", listId);
                if (parentId == null)
                    respace.Parameters.AddWithValue("p", DBNull.Value);
                else
                    respace.Parameters.AddWithValue("p", parentId.Value);
                respace.Parameters.AddWithValue("step", OrderStep);
                await respace.ExecuteNonQueryAsync(ct);
            }
            newRevision = await IncrementRevisionAsync(conn, listId, tx, ct);
            await tx.CommitAsync(ct);
        }
        return (true, newRevision);
    }

    public async Task<bool> GetItemExpandedAsync(int userId, int itemId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("select expanded from item_ui_state where user_id=@u and item_id=@i", conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("i", itemId);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is bool b ? b : true;
    }

    public async Task SetItemExpandedAsync(int userId, int itemId, bool expanded, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("insert into item_ui_state(user_id,item_id,expanded) values(@u,@i,@e) on conflict(user_id,item_id) do update set expanded=excluded.expanded", conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("i", itemId);
        cmd.Parameters.AddWithValue("e", expanded);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IDictionary<int, bool>> GetExpandedStatesAsync(int userId, int listId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        var dict = new Dictionary<int, bool>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var sql = "select s.item_id,s.expanded from item_ui_state s join items i on i.id=s.item_id where s.user_id=@u and i.list_id=@l";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("l", listId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            dict[r.GetInt32(0)] = r.GetBoolean(1);
        return dict;
    }

    public async Task<(bool Ok, long NewRevision)> RenameItemAsync(int itemId, string newName, long expectedRevision, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName)) return (false, 0);
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        int listId;
        await using (var info = new NpgsqlCommand("select list_id from items where id=@i", conn))
        {
            info.Parameters.AddWithValue("i", itemId);
            var res = await info.ExecuteScalarAsync(ct);
            if (res is int li) listId = li; else return (false, 0);
        }
        var currentRevision = await GetListRevisionInternalAsync(conn, listId, ct);
        if (currentRevision != expectedRevision) return (false, currentRevision);
        long newRevision;
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            await using (var upd = new NpgsqlCommand("update items set name=@n where id=@i", conn, tx))
            {
                upd.Parameters.AddWithValue("n", newName);
                upd.Parameters.AddWithValue("i", itemId);
                await upd.ExecuteNonQueryAsync(ct);
            }
            newRevision = await IncrementRevisionAsync(conn, listId, tx, ct);
            await tx.CommitAsync(ct);
        }
        return (true, newRevision);
    }

    public async Task<(bool Ok, long NewRevision, int Affected)> ResetSubtreeAsync(int rootItemId, long expectedRevision, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        int listId; int? parentId;
        await using (var info = new NpgsqlCommand("select list_id,parent_item_id from items where id=@i", conn))
        {
            info.Parameters.AddWithValue("i", rootItemId);
            await using var r = await info.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return (false, 0, 0);
            listId = r.GetInt32(0);
            parentId = r.IsDBNull(1) ? null : r.GetInt32(1);
        }
        var currentRevision = await GetListRevisionInternalAsync(conn, listId, ct);
        if (currentRevision != expectedRevision) return (false, currentRevision, 0);
        long newRevision;
        int affected = 0;
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            var sql = @"with recursive sub as ( select id,parent_item_id from items where id=@root union all select i.id,i.parent_item_id from items i join sub s on i.parent_item_id=s.id ) update items set is_completed=false where id in (select id from sub);";
            await using (var cmd = new NpgsqlCommand(sql, conn, tx))
            {
                cmd.Parameters.AddWithValue("root", rootItemId);
                affected = await cmd.ExecuteNonQueryAsync(ct);
            }
            int? cur = parentId;
            while (cur != null)
            {
                await using (var updAnc = new NpgsqlCommand("update items set is_completed=false where id=@p", conn, tx))
                {
                    updAnc.Parameters.AddWithValue("p", cur.Value);
                    await updAnc.ExecuteNonQueryAsync(ct);
                }
                await using (var getp = new NpgsqlCommand("select parent_item_id from items where id=@p", conn, tx))
                {
                    getp.Parameters.AddWithValue("p", cur.Value);
                    var res = await getp.ExecuteScalarAsync(ct);
                    cur = res is int pi ? (int?)pi : null;
                }
            }
            newRevision = await IncrementRevisionAsync(conn, listId, tx, ct);
            await tx.CommitAsync(ct);
        }
        return (true, newRevision, affected);
    }

    public static async Task StoreDevConnectionStringAsync(string connStr) => await SecureStorage.SetAsync("NEON_CONNECTION_STRING", connStr);

    private static string? TryGetFromSecureStorage()
    {
        try { return SecureStorage.GetAsync("NEON_CONNECTION_STRING").GetAwaiter().GetResult(); }
        catch { return null; }
    }

    private static byte[] GenerateSalt(int size)
    {
        var b = new byte[size];
        RandomNumberGenerator.Fill(b);
        return b;
    }

    private static string HashPassword(string password, byte[] salt, int iterations)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32);
        return $"PBKDF2$sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static int ExtractIterations(string stored)
    {
        var parts = stored.Split('$');
        return parts.Length >= 5 && int.TryParse(parts[2], out var it) ? it : 100_000;
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
