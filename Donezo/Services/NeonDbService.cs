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
    Task<IReadOnlyList<ListRecord>> GetListsAsync(int userId, CancellationToken ct = default); // creator lists
    Task<IReadOnlyList<ListRecord>> GetOwnedListsAsync(int userId, CancellationToken ct = default); // owned lists via user_id
    Task<IReadOnlyList<SharedListRecord>> GetSharedListsAsync(int userId, CancellationToken ct = default); // lists shared with user
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
    Task<bool?> GetListHideCompletedAsync(int userId, int listId, CancellationToken ct = default);
    Task SetListHideCompletedAsync(int userId, int listId, bool hideCompleted, CancellationToken ct = default);
    Task<IReadOnlyList<ShareCodeRecord>> GetShareCodesAsync(int listId, CancellationToken ct = default);
    Task<ShareCodeRecord?> CreateShareCodeAsync(int listId, string role, DateTime? expirationUtc, int maxRedeems, CancellationToken ct = default);
    Task<bool> UpdateShareCodeRoleAsync(int shareCodeId, string newRole, CancellationToken ct = default);
    Task<bool> SoftDeleteShareCodeAsync(int shareCodeId, CancellationToken ct = default);
    Task<(bool Ok, MembershipRecord? Membership)> RedeemShareCodeAsync(int listId, int userId, string code, CancellationToken ct = default);
    Task<(bool Ok, int? ListId, ListRecord? List, MembershipRecord? Membership)> RedeemShareCodeByCodeAsync(int userId, string code, CancellationToken ct = default);
    Task<IReadOnlyList<MembershipRecord>> GetMembershipsAsync(int listId, CancellationToken ct = default);
    Task<bool> RevokeMembershipAsync(int listId, int userId, CancellationToken ct = default);
    Task<bool> TransferOwnershipAsync(int listId, int newOwnerUserId, CancellationToken ct = default);
    Task<int?> GetListOwnerUserIdAsync(int listId, CancellationToken ct = default);
    Task<bool> SetItemCompletedByUserAsync(int itemId, int userId, bool completed, CancellationToken ct = default);
}

public record ListRecord(int Id, string Name, bool IsDaily);
public record SharedListRecord(int Id, string Name, bool IsDaily, string Role);
public record ItemRecord(int Id, int ListId, string Name, bool IsCompleted, int? ParentItemId, bool HasChildren, int ChildrenCount, int IncompleteChildrenCount, int Level, string SortKey, int Order, int? CompletedByUserId, string? CompletedByUsername);
public record ShareCodeRecord(int Id, int ListId, string Code, string Role, DateTime? ExpirationUtc, int MaxRedeems, int RedeemedCount, bool IsDeleted);
public record MembershipRecord(int Id, int ListId, int UserId, string Username, string Role, DateTime JoinedUtc, bool Revoked, string? Code);

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
        var sql = @"create table if not exists users ( id serial primary key, username text not null unique, email text, first_name text, last_name text, password_hash text not null, password_salt text not null, created_at timestamptz not null default now()); create table if not exists lists ( id serial primary key, user_id int not null references users(id) on delete cascade, name text not null, created_at timestamptz not null default now(), constraint uq_lists_user_name unique(user_id,name)); create table if not exists items ( id serial primary key, list_id int not null references lists(id) on delete cascade, name text not null, is_completed boolean not null default false, created_at timestamptz not null default now()); alter table if exists lists add column if not exists is_daily boolean not null default false; alter table if exists lists add column if not exists last_reset_date date; alter table if exists items add column if not exists ""order"" int not null default 0; alter table if exists items add column if not exists parent_item_id int references items(id) on delete cascade; create index if not exists ix_items_list_parent_order on items(list_id, parent_item_id, ""order""); alter table if exists lists add column if not exists revision bigint not null default 0; create table if not exists user_prefs ( user_id int primary key references users(id) on delete cascade, theme_dark boolean not null default false ); create unique index if not exists ux_users_email_lower on users ((lower(email))) where email is not null; create table if not exists item_ui_state ( user_id int not null references users(id) on delete cascade, item_id int not null references items(id) on delete cascade, expanded boolean not null default true, primary key(user_id,item_id)); create table if not exists user_list_prefs ( user_id int not null references users(id) on delete cascade, list_id int not null references lists(id) on delete cascade, hide_completed boolean not null default false, updated_at timestamptz not null default now(), primary key(user_id,list_id) ); create table if not exists roles ( id serial primary key, name text not null unique ); create table if not exists list_share_codes ( id serial primary key, list_id int not null references lists(id) on delete cascade, code text not null unique, role text not null, expiration timestamptz null, max_redeems int not null default 0, redeemed_count int not null default 0, is_deleted boolean not null default false, created_at timestamptz not null default now(), is_owner_code boolean not null default false ); create index if not exists ix_list_share_codes_list on list_share_codes(list_id); create table if not exists list_memberships ( id serial primary key, list_id int not null references lists(id) on delete cascade, user_id int not null references users(id) on delete cascade, role text not null, joined_at timestamptz not null default now(), revoked boolean not null default false, via_code text, constraint uq_list_membership unique(list_id,user_id) ); create index if not exists ix_list_memberships_list on list_memberships(list_id);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
        // Seed roles
        await using (var seedRoles = new NpgsqlCommand("insert into roles(name) values ('Viewer'),('Contributor'),('Owner') on conflict(name) do nothing", conn))
        { await seedRoles.ExecuteNonQueryAsync(ct); }
        // Add role_id column if missing
        await using (var addRoleIdCol = new NpgsqlCommand("alter table if exists list_share_codes add column if not exists role_id int", conn))
        { await addRoleIdCol.ExecuteNonQueryAsync(ct); }
        // Populate role_id values where null
        await using (var populateRoleIds = new NpgsqlCommand("update list_share_codes set role_id = r.id from roles r where list_share_codes.role_id is null and r.name = list_share_codes.role", conn))
        { await populateRoleIds.ExecuteNonQueryAsync(ct); }
        // Add FK constraint for role_id if not exists and enforce NOT NULL
        string addFkRoleId = @"do $$ begin if not exists (select 1 from pg_constraint where conname = 'fk_list_share_codes_role_id') then alter table list_share_codes add constraint fk_list_share_codes_role_id foreign key(role_id) references roles(id) on update cascade on delete restrict; end if; exception when others then end $$;";
        await using (var fkRoleId = new NpgsqlCommand(addFkRoleId, conn)) { await fkRoleId.ExecuteNonQueryAsync(ct); }
        await using (var setNotNull = new NpgsqlCommand("alter table list_share_codes alter column role_id set not null", conn))
        { try { await setNotNull.ExecuteNonQueryAsync(ct); } catch { } }
        // Existing FKs on role name if not present
        string addFkSql = @"do $$ begin if not exists (select 1 from pg_constraint where conname = 'fk_list_share_codes_role') then alter table list_share_codes add constraint fk_list_share_codes_role foreign key(role) references roles(name) on update cascade on delete restrict; end if; if not exists (select 1 from pg_constraint where conname = 'fk_list_memberships_role') then alter table list_memberships add constraint fk_list_memberships_role foreign key(role) references roles(name) on update cascade on delete restrict; end if; end $$;";
        await using (var addFk = new NpgsqlCommand(addFkSql, conn)) { await addFk.ExecuteNonQueryAsync(ct); }
        // Ensure is_owner_code flag exists
        await using (var addOwnerFlag = new NpgsqlCommand("alter table if exists list_share_codes add column if not exists is_owner_code boolean not null default false", conn))
        { await addOwnerFlag.ExecuteNonQueryAsync(ct); }
        // Track last action user for completion toggles
        await using (var addActionUserCol = new NpgsqlCommand("alter table if exists items add column if not exists last_action_user_id int references users(id)", conn)) { await addActionUserCol.ExecuteNonQueryAsync(ct); }
        await using (var addActionUserNameCol = new NpgsqlCommand("alter table if exists items add column if not exists last_action_username text", conn)) { await addActionUserNameCol.ExecuteNonQueryAsync(ct); }
        // Explicit completion attribution
        await using (var addCompletedByCol = new NpgsqlCommand("alter table if exists items add column if not exists completed_by_user_id int references users(id)", conn)) { await addCompletedByCol.ExecuteNonQueryAsync(ct); }
        // FK for completed_by_user_id
        string addFkSql2 = @"do $$ begin if not exists (select 1 from pg_constraint where conname = 'fk_items_completed_by_user_id') then alter table items add constraint fk_items_completed_by_user_id foreign key(completed_by_user_id) references users(id) on update cascade on delete restrict; end if; end $$;";
        await using (var addFk2 = new NpgsqlCommand(addFkSql2, conn)) { await addFk2.ExecuteNonQueryAsync(ct); }

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

    public async Task<IReadOnlyList<ListRecord>> GetOwnedListsAsync(int userId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        var list = new List<ListRecord>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        // Owned lists: lists.user_id matches user (after transfers, user_id updated)
        var sql = "select l.id,l.name,l.is_daily from lists l where l.user_id=@u order by l.id";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("u", userId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new ListRecord(r.GetInt32(0), r.GetString(1), r.GetBoolean(2)));
        return list;
    }

    public async Task<IReadOnlyList<SharedListRecord>> GetSharedListsAsync(int userId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        var list = new List<SharedListRecord>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        // Shared lists: user has membership not Owner and list.user_id != user
        var sql = @"select l.id,l.name,l.is_daily,m.role from lists l
                     join list_memberships m on m.list_id=l.id and m.user_id=@u and m.revoked=false
                     where m.role <> 'Owner' and l.user_id<>@u order by l.id";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("u", userId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new SharedListRecord(r.GetInt32(0), r.GetString(1), r.GetBoolean(2), r.GetString(3)));
        return list;
    }

    public async Task<int> CreateListAsync(int userId, string name, bool isDaily, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("List name required", nameof(name));
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        int newId;
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            await using (var cmd = new NpgsqlCommand("insert into lists(user_id,name,is_daily,last_reset_date) values(@u,@n,@d,current_date) returning id", conn, tx))
            {
                cmd.Parameters.AddWithValue("u", userId);
                cmd.Parameters.AddWithValue("n", name);
                cmd.Parameters.AddWithValue("d", isDaily);
                newId = (int)await cmd.ExecuteScalarAsync(ct);
            }
            // Insert owner membership
            await using (var insMem = new NpgsqlCommand("insert into list_memberships(list_id,user_id,role,via_code) values(@l,@u,'Owner',null) on conflict(list_id,user_id) do update set role='Owner', revoked=false", conn, tx))
            {
                insMem.Parameters.AddWithValue("l", newId);
                insMem.Parameters.AddWithValue("u", userId);
                await insMem.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        return newId;
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
        const string sql = """
WITH RECURSIVE tree AS (
 SELECT i.id,i.list_id,i.name,i.is_completed,i.parent_item_id,i."order",
        (SELECT count(*) FROM items c WHERE c.parent_item_id=i.id) AS children_count,
        (SELECT count(*) FROM items c WHERE c.parent_item_id=i.id AND c.is_completed=false) AS incomplete_children_count,
        1 AS level,
        lpad(i."order"::text,10,'0') AS sort_key,
        i.completed_by_user_id,
        u.username AS completed_by_username
 FROM items i
 LEFT JOIN users u ON u.id=i.completed_by_user_id
 WHERE i.list_id=@list AND i.parent_item_id IS NULL
 UNION ALL
 SELECT c.id,c.list_id,c.name,c.is_completed,c.parent_item_id,c."order",
        (SELECT count(*) FROM items cc WHERE cc.parent_item_id=c.id) AS children_count,
        (SELECT count(*) FROM items cc WHERE cc.parent_item_id=c.id AND cc.is_completed=false) AS incomplete_children_count,
        p.level+1 AS level,
        p.sort_key || '-' || lpad(c."order"::text,10,'0') AS sort_key,
        c.completed_by_user_id,
        u2.username AS completed_by_username
 FROM items c
 LEFT JOIN users u2 ON u2.id=c.completed_by_user_id
 JOIN tree p ON c.parent_item_id=p.id
)
SELECT * FROM tree ORDER BY sort_key;
""";
        var items = new List<ItemRecord>();
        await using var cmd = new NpgsqlCommand(sql, conn);
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
            var completedByUserId = r.IsDBNull(10) ? (int?)null : r.GetInt32(10);
            var completedByUsername = r.IsDBNull(11) ? null : r.GetString(11);
            items.Add(new ItemRecord(id, listIdVal, name, completed, parent, childrenCount > 0, childrenCount, incompleteChildrenCount, level, sortKey, order, completedByUserId, completedByUsername));
        }
        return items;
    }

    public async Task<bool> SetItemCompletedAsync(int itemId, bool completed, CancellationToken ct = default)
    { return await SetItemCompletedByUserAsync(itemId, 0, completed, ct); }

    public async Task<(bool Ok, long NewRevision)> DeleteItemAsync(int itemId, long expectedRevision, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        int? listId = null;
        await using (var get = new NpgsqlCommand("select list_id from items where id=@i", conn))
        { get.Parameters.AddWithValue("i", itemId); var res = await get.ExecuteScalarAsync(ct); if (res is int li) listId = li; else return (false, 0); }
        long currentRevision; await using (var rev = new NpgsqlCommand("select revision from lists where id=@l", conn)) { rev.Parameters.AddWithValue("l", listId!.Value); var res = await rev.ExecuteScalarAsync(ct); currentRevision = res is long rv ? rv : 0L; }
        if (currentRevision != expectedRevision) return (false, currentRevision);
        long newRevision; await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var del = new NpgsqlCommand("delete from items where id=@i", conn, tx)) { del.Parameters.AddWithValue("i", itemId); if (await del.ExecuteNonQueryAsync(ct) == 0) { await tx.RollbackAsync(ct); return (false, currentRevision); } }
        await using (var upd = new NpgsqlCommand("update lists set revision=revision+1 where id=@l returning revision", conn, tx)) { upd.Parameters.AddWithValue("l", listId!.Value); var res = await upd.ExecuteScalarAsync(ct); newRevision = res is long rv2 ? rv2 : currentRevision + 1; }
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
        { clr.Parameters.AddWithValue("l", listId); affected = await clr.ExecuteNonQueryAsync(ct); }
        await using (var upd = new NpgsqlCommand("update lists set last_reset_date=current_date where id=@l", conn, tx))
        { upd.Parameters.AddWithValue("l", listId); await upd.ExecuteNonQueryAsync(ct); }
        await using (var rev = new NpgsqlCommand("update lists set revision=revision+1 where id=@l", conn, tx))
        { rev.Parameters.AddWithValue("l", listId); await rev.ExecuteNonQueryAsync(ct); }
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
        var sql = """
select i.id,i.list_id,i.name,i.is_completed,i.parent_item_id,i."order",
       (select count(*) from items c where c.parent_item_id=i.id) children_count,
       (select count(*) from items c where c.parent_item_id=i.id and c.is_completed=false) incomplete_children_count,
       i.completed_by_user_id, cu.username as completed_by_username
from items i left join users cu on cu.id=i.completed_by_user_id where i.id=@i;
""";
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
        var completedByUserId = r.IsDBNull(8) ? (int?)null : r.GetInt32(8);
        var completedByUsername = r.IsDBNull(9) ? null : r.GetString(9);
        var hasChildren = childrenCount > 0;
        return new ItemRecord(id, listIdVal, name, completed, parent, hasChildren, childrenCount, incompleteChildrenCount, 0, string.Empty, order, completedByUserId, completedByUsername);
    }

    public async Task<IReadOnlyList<ItemRecord>> GetChildrenAsync(int parentItemId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var sql = """
select i.id,i.list_id,i.name,i.is_completed,i.parent_item_id,i."order",
       (select count(*) from items c where c.parent_item_id=i.id) children_count,
       (select count(*) from items c where c.parent_item_id=i.id and c.is_completed=false) incomplete_children_count,
       i.completed_by_user_id, cu.username as completed_by_username
from items i left join users cu on cu.id=i.completed_by_user_id where i.parent_item_id=@p order by i."order";
""";
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
            var completedByUserId = r.IsDBNull(8) ? (int?)null : r.GetInt32(8);
            var completedByUsername = r.IsDBNull(9) ? null : r.GetString(9);
            var hasChildren = childrenCount > 0;
            list.Add(new ItemRecord(id, listIdVal, name, completed, parent, hasChildren, childrenCount, incompleteChildrenCount, 0, string.Empty, order, completedByUserId, completedByUsername));
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

    // Shared helpers
    private static string? TryGetFromSecureStorage() { try { return SecureStorage.GetAsync("NEON_CONNECTION_STRING").GetAwaiter().GetResult(); } catch { return null; } }
    private static byte[] GenerateSalt(int size) { var b = new byte[size]; RandomNumberGenerator.Fill(b); return b; }
    private static string HashPassword(string password, byte[] salt, int iterations) { using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256); var hash = pbkdf2.GetBytes(32); return $"PBKDF2$sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}"; }
    private static int ExtractIterations(string stored) { var parts = stored.Split('$'); return parts.Length >= 5 && int.TryParse(parts[2], out var it) ? it : 100_000; }
    private static bool ConstantTimeEquals(string a, string b) { if (a.Length != b.Length) return false; int diff = 0; for (int i=0;i<a.Length;i++) diff |= a[i]^b[i]; return diff==0; }
    private static string GenerateShareCode() { const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"; const string digits = "0123456789"; var rnd = new Random(); var sb = new System.Text.StringBuilder(13); for (int i=0;i<3;i++) sb.Append(letters[rnd.Next(letters.Length)]); sb.Append('-'); for(int i=0;i<5;i++) sb.Append(digits[rnd.Next(digits.Length)]); sb.Append('-'); for(int i=0;i<3;i++) sb.Append(letters[rnd.Next(letters.Length)]); return sb.ToString(); }

    private async Task<int> GetItemDepthAsync(NpgsqlConnection conn, int itemId, CancellationToken ct)
    { var sql = "with recursive p as (select id,parent_item_id,1 lvl from items where id=@i union all select it.id,it.parent_item_id,p.lvl+1 from items it join p on it.id=p.parent_item_id) select max(lvl) from p"; await using var cmd = new NpgsqlCommand(sql, conn); cmd.Parameters.AddWithValue("i", itemId); var res = await cmd.ExecuteScalarAsync(ct); return res is int i ? i : res is long l ? (int)l : 1; }
    private async Task<int> GetSubtreeDepthAsync(NpgsqlConnection conn, int itemId, CancellationToken ct)
    { var sql = "with recursive t as (select id,parent_item_id,1 lvl from items where id=@i union all select c.id,c.parent_item_id,t.lvl+1 from items c join t on c.parent_item_id=t.id) select max(lvl) from t"; await using var cmd = new NpgsqlCommand(sql, conn); cmd.Parameters.AddWithValue("i", itemId); var res = await cmd.ExecuteScalarAsync(ct); return res is int i ? i : res is long l ? (int)l : 1; }
    private async Task<long> GetListRevisionInternalAsync(NpgsqlConnection conn, int listId, CancellationToken ct)
    { await using var cmd = new NpgsqlCommand("select revision from lists where id=@l", conn); cmd.Parameters.AddWithValue("l", listId); var res = await cmd.ExecuteScalarAsync(ct); return res is long rv ? rv : 0L; }
    private async Task<long> IncrementRevisionAsync(NpgsqlConnection conn, int listId, NpgsqlTransaction tx, CancellationToken ct)
    { await using var cmd = new NpgsqlCommand("update lists set revision=revision+1 where id=@l returning revision", conn, tx); cmd.Parameters.AddWithValue("l", listId); var res = await cmd.ExecuteScalarAsync(ct); return res is long rv ? rv : 0L; }

    public async Task<int> AddChildItemAsync(int listId, string name, int parentItemId, long expectedRevision, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Item name required", nameof(name));
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        var currentRevision = await GetListRevisionInternalAsync(conn, listId, ct);
        if (currentRevision != expectedRevision) throw new InvalidOperationException("Concurrency mismatch");
        await using (var chk = new NpgsqlCommand("select list_id from items where id=@pid", conn))
        { chk.Parameters.AddWithValue("pid", parentItemId); var ls = await chk.ExecuteScalarAsync(ct); if (ls is int pl) { if (pl != listId) throw new InvalidOperationException("Parent belongs to different list"); } else throw new InvalidOperationException("Parent not found"); }
        var parentDepth = await GetItemDepthAsync(conn, parentItemId, ct); if (parentDepth + 1 > MaxDepth) throw new InvalidOperationException("Depth limit exceeded");
        int order; await using (var ordCmd = new NpgsqlCommand("select coalesce(max(\"order\"),0) from items where list_id=@l and parent_item_id=@p", conn)) { ordCmd.Parameters.AddWithValue("l", listId); ordCmd.Parameters.AddWithValue("p", parentItemId); var maxObj = await ordCmd.ExecuteScalarAsync(ct); var maxOrder = maxObj is int mo ? mo : 0; order = maxOrder + OrderStep; }
        int newId; await using (var tx = await conn.BeginTransactionAsync(ct)) { await using (var ins = new NpgsqlCommand("insert into items(list_id,name,parent_item_id,\"order\") values(@l,@n,@p,@o) returning id", conn, tx)) { ins.Parameters.AddWithValue("l", listId); ins.Parameters.AddWithValue("n", name); ins.Parameters.AddWithValue("p", parentItemId); ins.Parameters.AddWithValue("o", order); newId = (int)await ins.ExecuteScalarAsync(ct); } await IncrementRevisionAsync(conn, listId, tx, ct); await tx.CommitAsync(ct); }
        return newId;
    }

    public async Task<(bool Ok, long NewRevision)> MoveItemAsync(int itemId, int? newParentItemId, long expectedRevision, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct); await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        int listId; int? currentParent; await using (var info = new NpgsqlCommand("select list_id,parent_item_id from items where id=@i", conn)) { info.Parameters.AddWithValue("i", itemId); await using var r = await info.ExecuteReaderAsync(ct); if (!await r.ReadAsync(ct)) return (false,0); listId = r.GetInt32(0); currentParent = r.IsDBNull(1) ? null : r.GetInt32(1); }
        var currentRevision = await GetListRevisionInternalAsync(conn, listId, ct); if (currentRevision != expectedRevision) return (false,currentRevision);
        if (newParentItemId != null) { if (newParentItemId.Value == itemId) return (false,currentRevision); var cycSql = "with recursive d as (select id from items where parent_item_id=@root union all select c.id from items c join d on c.parent_item_id=d.id) select 1 from d where id=@pid limit 1"; await using var cycCmd = new NpgsqlCommand(cycSql, conn); cycCmd.Parameters.AddWithValue("root", itemId); cycCmd.Parameters.AddWithValue("pid", newParentItemId.Value); var cyc = await cycCmd.ExecuteScalarAsync(ct); if (cyc != null) return (false,currentRevision); }
        int subtreeDepth = await GetSubtreeDepthAsync(conn, itemId, ct); int newParentDepth = 0; if (newParentItemId != null) { newParentDepth = await GetItemDepthAsync(conn, newParentItemId.Value, ct); await using (var chk = new NpgsqlCommand("select list_id from items where id=@pid", conn)) { chk.Parameters.AddWithValue("pid", newParentItemId.Value); var ls = await chk.ExecuteScalarAsync(ct); if (ls is int pl && pl != listId) return (false,currentRevision); } }
        if (newParentDepth + subtreeDepth > MaxDepth) return (false,currentRevision);
        int newOrder; await using (var ordCmd = new NpgsqlCommand("select coalesce(max(\"order\"),0) from items where list_id=@l and parent_item_id is not distinct from @p", conn)) { ordCmd.Parameters.AddWithValue("l", listId); if (newParentItemId == null) ordCmd.Parameters.AddWithValue("p", DBNull.Value); else ordCmd.Parameters.AddWithValue("p", newParentItemId.Value); var maxObj = await ordCmd.ExecuteScalarAsync(ct); var maxOrder = maxObj is int mo ? mo : 0; newOrder = maxOrder + OrderStep; }
        long newRevision; await using (var tx = await conn.BeginTransactionAsync(ct)) { await using (var upd = new NpgsqlCommand("update items set parent_item_id=@p,\"order\"=@o where id=@i", conn, tx)) { upd.Parameters.AddWithValue("p", (object?)newParentItemId ?? DBNull.Value); upd.Parameters.AddWithValue("o", newOrder); upd.Parameters.AddWithValue("i", itemId); await upd.ExecuteNonQueryAsync(ct); } newRevision = await IncrementRevisionAsync(conn, listId, tx, ct); await tx.CommitAsync(ct); }
        return (true,newRevision);
    }

    public async Task<(bool Ok, long NewRevision)> SetItemOrderAsync(int itemId, int newOrder, long expectedRevision, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct); await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        int listId; int? parentId; await using (var info = new NpgsqlCommand("select list_id,parent_item_id from items where id=@i", conn)) { info.Parameters.AddWithValue("i", itemId); await using var r = await info.ExecuteReaderAsync(ct); if (!await r.ReadAsync(ct)) return (false,0); listId = r.GetInt32(0); parentId = r.IsDBNull(1)?null:r.GetInt32(1); }
        var currentRevision = await GetListRevisionInternalAsync(conn, listId, ct); if (currentRevision != expectedRevision) return (false,currentRevision);
        long newRevision; await using (var tx = await conn.BeginTransactionAsync(ct)) {
            await using (var upd = new NpgsqlCommand("update items set \"order\"=@o where id=@i", conn)) { upd.Transaction = tx; upd.Parameters.AddWithValue("o", newOrder); upd.Parameters.AddWithValue("i", itemId); await upd.ExecuteNonQueryAsync(ct); }
            var respaceSql = @"with ord as ( select id, row_number() over (order by ""order"", id) rn from items where list_id=@l and parent_item_id is not distinct from @p ) update items i set ""order"" = ord.rn * @step from ord where i.id = ord.id and i.list_id=@l;";
            await using (var respace = new NpgsqlCommand(respaceSql, conn)) { respace.Transaction = tx; respace.Parameters.AddWithValue("l", listId); if (parentId==null) respace.Parameters.AddWithValue("p", DBNull.Value); else respace.Parameters.AddWithValue("p", parentId.Value); respace.Parameters.AddWithValue("step", OrderStep); await respace.ExecuteNonQueryAsync(ct); }
            newRevision = await IncrementRevisionAsync(conn, listId, tx, ct); await tx.CommitAsync(ct); }
        return (true,newRevision);
    }

    public async Task<bool> GetItemExpandedAsync(int userId, int itemId, CancellationToken ct = default)
    { await EnsureSchemaAsync(ct); await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct); await using var cmd = new NpgsqlCommand("select expanded from item_ui_state where user_id=@u and item_id=@i", conn); cmd.Parameters.AddWithValue("u", userId); cmd.Parameters.AddWithValue("i", itemId); var res = await cmd.ExecuteScalarAsync(ct); return res is bool b ? b : true; }

    public async Task SetItemExpandedAsync(int userId, int itemId, bool expanded, CancellationToken ct = default)
    { await EnsureSchemaAsync(ct); await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct); await using var cmd = new NpgsqlCommand("insert into item_ui_state(user_id,item_id,expanded) values(@u,@i,@e) on conflict(user_id,item_id) do update set expanded=excluded.expanded", conn); cmd.Parameters.AddWithValue("u", userId); cmd.Parameters.AddWithValue("i", itemId); cmd.Parameters.AddWithValue("e", expanded); await cmd.ExecuteNonQueryAsync(ct); }

    public async Task<IDictionary<int,bool>> GetExpandedStatesAsync(int userId, int listId, CancellationToken ct = default)
    { await EnsureSchemaAsync(ct); var dict = new Dictionary<int,bool>(); await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct); var sql = "select s.item_id,s.expanded from item_ui_state s join items i on i.id=s.item_id where s.user_id=@u and i.list_id=@l"; await using var cmd = new NpgsqlCommand(sql, conn); cmd.Parameters.AddWithValue("u", userId); cmd.Parameters.AddWithValue("l", listId); await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) dict[r.GetInt32(0)] = r.GetBoolean(1); return dict; }

    public async Task<(bool Ok,long NewRevision)> RenameItemAsync(int itemId, string newName, long expectedRevision, CancellationToken ct = default)
    { if (string.IsNullOrWhiteSpace(newName)) return (false,0); await EnsureSchemaAsync(ct); await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct); int listId; await using (var info = new NpgsqlCommand("select list_id from items where id=@i", conn)) { info.Parameters.AddWithValue("i", itemId); var res = await info.ExecuteScalarAsync(ct); if (res is int li) listId = li; else return (false,0); } var currentRevision = await GetListRevisionInternalAsync(conn, listId, ct); if (currentRevision != expectedRevision) return (false,currentRevision); long newRevision; await using (var tx = await conn.BeginTransactionAsync(ct)) { await using (var upd = new NpgsqlCommand("update items set name=@n where id=@i", conn, tx)) { upd.Parameters.AddWithValue("n", newName); upd.Parameters.AddWithValue("i", itemId); await upd.ExecuteNonQueryAsync(ct); } newRevision = await IncrementRevisionAsync(conn, listId, tx, ct); await tx.CommitAsync(ct); } return (true,newRevision); }

    public async Task<(bool Ok,long NewRevision,int Affected)> ResetSubtreeAsync(int rootItemId, long expectedRevision, CancellationToken ct = default)
    { await EnsureSchemaAsync(ct); await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct); int listId; int? parentId; await using (var info = new NpgsqlCommand("select list_id,parent_item_id from items where id=@i", conn)) { info.Parameters.AddWithValue("i", rootItemId); await using var r = await info.ExecuteReaderAsync(ct); if (!await r.ReadAsync(ct)) return (false,0,0); listId = r.GetInt32(0); parentId = r.IsDBNull(1)?null:r.GetInt32(1); } var currentRevision = await GetListRevisionInternalAsync(conn, listId, ct); if (currentRevision != expectedRevision) return (false,currentRevision,0); long newRevision; int affected=0; await using (var tx = await conn.BeginTransactionAsync(ct)) { var sql = @"with recursive sub as ( select id,parent_item_id from items where id=@root union all select i.id,i.parent_item_id from items i join sub s on i.parent_item_id=s.id ) update items set is_completed=false, completed_by_user_id=null where id in (select id from sub);"; await using (var cmd = new NpgsqlCommand(sql, conn, tx)) { cmd.Parameters.AddWithValue("root", rootItemId); affected = await cmd.ExecuteNonQueryAsync(ct); } int? cur = parentId; while (cur!=null) { await using (var updAnc = new NpgsqlCommand("update items set is_completed=false,completed_by_user_id=null where id=@p", conn, tx)) { updAnc.Parameters.AddWithValue("p", cur.Value); await updAnc.ExecuteNonQueryAsync(ct); } await using (var getp = new NpgsqlCommand("select parent_item_id from items where id=@p", conn, tx)) { getp.Parameters.AddWithValue("p", cur.Value); var res = await getp.ExecuteScalarAsync(ct); cur = res is int pi ? (int?)pi : null; } } newRevision = await IncrementRevisionAsync(conn, listId, tx, ct); await tx.CommitAsync(ct); } return (true,newRevision,affected); }

    public async Task<bool?> GetListHideCompletedAsync(int userId, int listId, CancellationToken ct = default)
    { await EnsureSchemaAsync(ct); await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct); await using var cmd = new NpgsqlCommand("select hide_completed from user_list_prefs where user_id=@u and list_id=@l", conn); cmd.Parameters.AddWithValue("u", userId); cmd.Parameters.AddWithValue("l", listId); var res = await cmd.ExecuteScalarAsync(ct); if (res is null || res is DBNull) return null; return (bool)res; }

    public async Task SetListHideCompletedAsync(int userId, int listId, bool hideCompleted, CancellationToken ct = default)
    { await EnsureSchemaAsync(ct); await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct); var sql = @"insert into user_list_prefs(user_id,list_id,hide_completed,updated_at) values(@u,@l,@h,now()) on conflict(user_id,list_id) do update set hide_completed=excluded.hide_completed, updated_at=excluded.updated_at"; await using var cmd = new NpgsqlCommand(sql, conn); cmd.Parameters.AddWithValue("u", userId); cmd.Parameters.AddWithValue("l", listId); cmd.Parameters.AddWithValue("h", hideCompleted); await cmd.ExecuteNonQueryAsync(ct); }

    private async Task<bool> ShareCodesHasRoleColumnAsync(NpgsqlConnection conn, CancellationToken ct)
    { const string sql = "select 1 from information_schema.columns where table_name='list_share_codes' and column_name='role' limit 1"; await using var cmd = new NpgsqlCommand(sql, conn); var res = await cmd.ExecuteScalarAsync(ct); return res != null; }

    public async Task<IReadOnlyList<ShareCodeRecord>> GetShareCodesAsync(int listId, CancellationToken ct = default)
    { await EnsureSchemaAsync(ct); var list = new List<ShareCodeRecord>(); await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct); var hasRoleCol = await ShareCodesHasRoleColumnAsync(conn, ct); string sql = hasRoleCol ? "select sc.id, sc.list_id, sc.code, sc.role, sc.expiration, sc.max_redeems, sc.redeemed_count, sc.is_deleted from list_share_codes sc where sc.list_id=@l order by sc.created_at desc" : "select sc.id, sc.list_id, sc.code, r.name as role, sc.expiration, sc.max_redeems, sc.redeemed_count, sc.is_deleted from list_share_codes sc left join roles r on r.id=sc.role_id where sc.list_id=@l order by sc.created_at desc"; await using var cmd = new NpgsqlCommand(sql, conn); cmd.Parameters.AddWithValue("l", listId); await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) list.Add(new ShareCodeRecord(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.IsDBNull(4)?null:r.GetDateTime(4), r.GetInt32(5), r.GetInt32(6), r.GetBoolean(7))); return list; }

    public async Task<ShareCodeRecord?> CreateShareCodeAsync(int listId, string role, DateTime? expirationUtc, int maxRedeems, CancellationToken ct = default)
    { if (string.IsNullOrWhiteSpace(role)) return null; await EnsureSchemaAsync(ct); await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct); int? roleId = null; await using (var getRole = new NpgsqlCommand("select id from roles where name=@r", conn)) { getRole.Parameters.AddWithValue("r", role); var rObj = await getRole.ExecuteScalarAsync(ct); roleId = rObj is int rid ? rid : (int?)null; } if (roleId==null) return null; var hasRoleCol = await ShareCodesHasRoleColumnAsync(conn, ct); string code = GenerateShareCode(); int guard=0; bool exists; do { await using var chk = new NpgsqlCommand("select 1 from list_share_codes where code=@c", conn); chk.Parameters.AddWithValue("c", code); exists = (await chk.ExecuteScalarAsync(ct))!=null; if (exists) code = GenerateShareCode(); } while (exists && guard++<10); string insertSql = hasRoleCol ? "insert into list_share_codes(list_id,code,role,role_id,expiration,max_redeems) values(@l,@c,@r,@rid,@e,@m) returning id" : "insert into list_share_codes(list_id,code,role_id,expiration,max_redeems) values(@l,@c,@rid,@e,@m) returning id"; await using var cmd = new NpgsqlCommand(insertSql, conn); cmd.Parameters.AddWithValue("l", listId); cmd.Parameters.AddWithValue("c", code); if (hasRoleCol) cmd.Parameters.AddWithValue("r", role); cmd.Parameters.AddWithValue("rid", roleId.Value); cmd.Parameters.AddWithValue("e", (object?)expirationUtc ?? DBNull.Value); cmd.Parameters.AddWithValue("m", maxRedeems); var idObj = await cmd.ExecuteScalarAsync(ct); if (idObj is int id) return new ShareCodeRecord(id,listId,code,role,expirationUtc,maxRedeems,0,false); return null; }

    public async Task<bool> UpdateShareCodeRoleAsync(int shareCodeId, string newRole, CancellationToken ct = default)
    { if (string.IsNullOrWhiteSpace(newRole)) return false; await EnsureSchemaAsync(ct); await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct); int? roleId=null; await using (var getRole = new NpgsqlCommand("select id from roles where name=@r", conn)) { getRole.Parameters.AddWithValue("r", newRole); var rObj = await getRole.ExecuteScalarAsync(ct); roleId = rObj is int rid ? rid : (int?)null; } if (roleId==null) return false; var hasRoleCol = await ShareCodesHasRoleColumnAsync(conn, ct); int listId; string oldRole; string readSql = hasRoleCol ? "select list_id, role from list_share_codes where id=@i" : "select sc.list_id, r.name as role from list_share_codes sc left join roles r on r.id=sc.role_id where sc.id=@i"; await using (var readCmd = new NpgsqlCommand(readSql, conn)) { readCmd.Parameters.AddWithValue("i", shareCodeId); await using var r = await readCmd.ExecuteReaderAsync(ct); if (!await r.ReadAsync(ct)) return false; listId = r.GetInt32(0); oldRole = r.GetString(1); } if (string.Equals(oldRole,newRole,StringComparison.OrdinalIgnoreCase)) return true;
        await using var tx = await conn.BeginTransactionAsync(ct);
        try {
            string sql = hasRoleCol ? "update list_share_codes set role=@r, role_id=@rid where id=@i" : "update list_share_codes set role_id=@rid where id=@i";
            await using (var cmd = new NpgsqlCommand(sql, conn, tx)) { if (hasRoleCol) cmd.Parameters.AddWithValue("r", newRole); cmd.Parameters.AddWithValue("rid", roleId.Value); cmd.Parameters.AddWithValue("i", shareCodeId); if (await cmd.ExecuteNonQueryAsync(ct)!=1) { await tx.RollbackAsync(ct); return false; } }
            const string updMembersSql = "update list_memberships set role=@newRole where list_id=@l and role=@oldRole";
            await using (var updMembers = new NpgsqlCommand(updMembersSql, conn, tx)) { updMembers.Parameters.AddWithValue("newRole", newRole); updMembers.Parameters.AddWithValue("l", listId); updMembers.Parameters.AddWithValue("oldRole", oldRole); await updMembers.ExecuteNonQueryAsync(ct); }
            await tx.CommitAsync(ct); return true;
        }
        catch { try { await tx.RollbackAsync(ct); } catch { } return false; }
    }

    public async Task<bool> SoftDeleteShareCodeAsync(int shareCodeId, CancellationToken ct = default)
    { await EnsureSchemaAsync(ct); await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct); await using var cmd = new NpgsqlCommand("update list_share_codes set is_deleted=true where id=@i", conn); cmd.Parameters.AddWithValue("i", shareCodeId); return await cmd.ExecuteNonQueryAsync(ct)==1; }

    public async Task<(bool Ok, MembershipRecord? Membership)> RedeemShareCodeAsync(int listId, int userId, string code, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var hasRoleCol = await ShareCodesHasRoleColumnAsync(conn, ct);
        string getSql = hasRoleCol ? "select id,role,expiration,max_redeems,redeemed_count,is_deleted,role_id from list_share_codes where list_id=@l and code=@c" : "select sc.id, r.name as role, sc.expiration, sc.max_redeems, sc.redeemed_count, sc.is_deleted, sc.role_id from list_share_codes sc left join roles r on r.id=sc.role_id where sc.list_id=@l and sc.code=@c";
        int codeId; string role; DateTime? exp; int maxRedeems; int redeemed; bool isDeleted; int? roleId;
        await using (var cmd = new NpgsqlCommand(getSql, conn))
        { cmd.Parameters.AddWithValue("l", listId); cmd.Parameters.AddWithValue("c", code); await using var r = await cmd.ExecuteReaderAsync(ct); if (!await r.ReadAsync(ct)) return (false,null); codeId = r.GetInt32(0); role = r.GetString(1); exp = r.IsDBNull(2)?null:r.GetDateTime(2); maxRedeems = r.GetInt32(3); redeemed = r.GetInt32(4); isDeleted = r.GetBoolean(5); roleId = r.IsDBNull(6)?null:r.GetInt32(6); }
        if (isDeleted || (exp!=null && exp.Value < DateTime.UtcNow) || (maxRedeems>0 && redeemed>=maxRedeems)) return (false,null);
        int? membershipId=null; bool revoked=false; DateTime joined=DateTime.UtcNow; string username=""; string? viaCodeExisting=null;
        await using (var chk = new NpgsqlCommand("select m.id,m.revoked,u.username,m.joined_at,m.role,m.via_code from list_memberships m join users u on u.id=m.user_id where m.list_id=@l and m.user_id=@u", conn))
        { chk.Parameters.AddWithValue("l", listId); chk.Parameters.AddWithValue("u", userId); await using var rr = await chk.ExecuteReaderAsync(ct); if (await rr.ReadAsync(ct)) { membershipId = rr.GetInt32(0); revoked = rr.GetBoolean(1); username = rr.GetString(2); joined = rr.GetDateTime(3); role = rr.GetString(4); viaCodeExisting = rr.IsDBNull(5)?null:rr.GetString(5); if (revoked) return (false,null); } }
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (membershipId==null)
        {
            await using (var ins = new NpgsqlCommand("insert into list_memberships(list_id,user_id,role,via_code) values(@l,@u,@r,@code) returning id", conn, tx))
            { ins.Parameters.AddWithValue("l", listId); ins.Parameters.AddWithValue("u", userId); ins.Parameters.AddWithValue("r", role); ins.Parameters.AddWithValue("code", code); var idObj = await ins.ExecuteScalarAsync(ct); membershipId = idObj as int?; }
            await using (var updCode = new NpgsqlCommand("update list_share_codes set redeemed_count=redeemed_count+1 where id=@i", conn, tx))
            { updCode.Parameters.AddWithValue("i", codeId); await updCode.ExecuteNonQueryAsync(ct); }
            await using (var getUser = new NpgsqlCommand("select username from users where id=@u", conn, tx))
            { getUser.Parameters.AddWithValue("u", userId); var uObj = await getUser.ExecuteScalarAsync(ct); username = uObj as string ?? string.Empty; }
        }
        await tx.CommitAsync(ct);
        var record = new MembershipRecord(membershipId!.Value, listId, userId, username, role, joined, false, viaCodeExisting ?? code);
        return (true,record);
    }

    public async Task<(bool Ok, int? ListId, ListRecord? List, MembershipRecord? Membership)> RedeemShareCodeByCodeAsync(int userId, string code, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        if (string.IsNullOrWhiteSpace(code)) return (false,null,null,null);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var hasRoleCol = await ShareCodesHasRoleColumnAsync(conn, ct);
        string findSql = hasRoleCol ? "select sc.id, sc.list_id, sc.role, sc.expiration, sc.max_redeems, sc.redeemed_count, sc.is_deleted, l.name, l.is_daily from list_share_codes sc join lists l on l.id=sc.list_id where sc.code=@c" : "select sc.id, sc.list_id, r.name as role, sc.expiration, sc.max_redeems, sc.redeemed_count, sc.is_deleted, l.name, l.is_daily from list_share_codes sc left join roles r on r.id=sc.role_id join lists l on l.id=sc.list_id where sc.code=@c";
        int codeId; int listId; string role; DateTime? exp; int maxRedeems; int redeemed; bool isDeleted; string listName; bool isDaily;
        await using (var cmd = new NpgsqlCommand(findSql, conn))
        { cmd.Parameters.AddWithValue("c", code); await using var r = await cmd.ExecuteReaderAsync(ct); if (!await r.ReadAsync(ct)) return (false,null,null,null); codeId = r.GetInt32(0); listId = r.GetInt32(1); role = r.GetString(2); exp = r.IsDBNull(3)?null:r.GetDateTime(3); maxRedeems = r.GetInt32(4); redeemed = r.GetInt32(5); isDeleted = r.GetBoolean(6); listName = r.GetString(7); isDaily = r.GetBoolean(8); }
        if (isDeleted || (exp!=null && exp.Value < DateTime.UtcNow) || (maxRedeems>0 && redeemed>=maxRedeems)) return (false,null,null,null);
        int? membershipId=null; bool revoked=false; DateTime joined=DateTime.UtcNow; string username=""; string? viaCodeExisting=null;
        await using (var chk = new NpgsqlCommand("select m.id,m.revoked,u.username,m.joined_at,m.role,m.via_code from list_memberships m join users u on u.id=m.user_id where m.list_id=@l and m.user_id=@u", conn))
        { chk.Parameters.AddWithValue("l", listId); chk.Parameters.AddWithValue("u", userId); await using var rr = await chk.ExecuteReaderAsync(ct); if (await rr.ReadAsync(ct)) { membershipId = rr.GetInt32(0); revoked = rr.GetBoolean(1); username = rr.GetString(2); joined = rr.GetDateTime(3); role = rr.GetString(4); viaCodeExisting = rr.IsDBNull(5)?null: rr.GetString(5); if (revoked) return (false,null,null,null); } }
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (membershipId==null)
        {
            await using (var ins = new NpgsqlCommand("insert into list_memberships(list_id,user_id,role,via_code) values(@l,@u,@r,@code) returning id", conn, tx))
            { ins.Parameters.AddWithValue("l", listId); ins.Parameters.AddWithValue("u", userId); ins.Parameters.AddWithValue("r", role); ins.Parameters.AddWithValue("code", code); var idObj = await ins.ExecuteScalarAsync(ct); membershipId = idObj as int?; }
            await using (var updCode = new NpgsqlCommand("update list_share_codes set redeemed_count=redeemed_count+1 where id=@i", conn, tx))
            { updCode.Parameters.AddWithValue("i", codeId); await updCode.ExecuteNonQueryAsync(ct); }
            await using (var getUser = new NpgsqlCommand("select username from users where id=@u", conn, tx))
            { getUser.Parameters.AddWithValue("u", userId); var uObj = await getUser.ExecuteScalarAsync(ct); username = uObj as string ?? string.Empty; }
        }
        await tx.CommitAsync(ct);
        var membership = new MembershipRecord(membershipId!.Value, listId, userId, username, role, joined, false, viaCodeExisting ?? code);
        var listRecord = new ListRecord(listId, listName, isDaily);
        return (true, listId, listRecord, membership);
    }

    public async Task<IReadOnlyList<MembershipRecord>> GetMembershipsAsync(int listId, CancellationToken ct = default)
    { await EnsureSchemaAsync(ct); var list = new List<MembershipRecord>(); await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct); var sql = "select m.id,m.list_id,m.user_id,u.username,m.role,m.joined_at,m.revoked,m.via_code from list_memberships m join users u on u.id=m.user_id where m.list_id=@l order by m.joined_at desc"; await using var cmd = new NpgsqlCommand(sql, conn); cmd.Parameters.AddWithValue("l", listId); await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) list.Add(new MembershipRecord(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetDateTime(5), r.GetBoolean(6), r.IsDBNull(7)?null:r.GetString(7))); return list; }

    public async Task<bool> RevokeMembershipAsync(int listId, int userId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        // Do not allow revoking owner membership or list creator
        bool isOwner = false;
        await using (var curCmd = new NpgsqlCommand("select 1 from list_memberships where list_id=@l and user_id=@u and role='Owner' and revoked=false", conn))
        { curCmd.Parameters.AddWithValue("l", listId); curCmd.Parameters.AddWithValue("u", userId); isOwner = (await curCmd.ExecuteScalarAsync(ct)) != null; }
        if (!isOwner)
        {
            await using (var creatorCmd = new NpgsqlCommand("select 1 from lists where id=@l and user_id=@u", conn))
            { creatorCmd.Parameters.AddWithValue("l", listId); creatorCmd.Parameters.AddWithValue("u", userId); if ((await creatorCmd.ExecuteScalarAsync(ct)) != null) isOwner = true; }
        }
        if (isOwner) return false;
        await using var cmd = new NpgsqlCommand("update list_memberships set revoked=true where list_id=@l and user_id=@u and revoked=false", conn); cmd.Parameters.AddWithValue("l", listId); cmd.Parameters.AddWithValue("u", userId); return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> TransferOwnershipAsync(int listId, int newOwnerUserId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        // Current owner is list creator (lists.user_id)
        int? currentOwner = null;
        await using (var curCmd = new NpgsqlCommand("select user_id from lists where id=@l", conn))
        { curCmd.Parameters.AddWithValue("l", listId); var res = await curCmd.ExecuteScalarAsync(ct); if (res is int uid) currentOwner = uid; }
        if (currentOwner == null || currentOwner.Value == newOwnerUserId) return false;
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            // Downgrade previous owner membership role to Contributor if exists
            await using (var updPrev = new NpgsqlCommand("update list_memberships set role='Contributor' where list_id=@l and user_id=@prev and role='Owner'", conn, tx))
            { updPrev.Parameters.AddWithValue("l", listId); updPrev.Parameters.AddWithValue("prev", currentOwner.Value); await updPrev.ExecuteNonQueryAsync(ct); }
            // Ensure previous owner retains (or restores) contributor membership
            bool hadMembership = false; bool wasRevoked = false;
            await using (var chkPrev = new NpgsqlCommand("select revoked from list_memberships where list_id=@l and user_id=@prev", conn, tx))
            { chkPrev.Parameters.AddWithValue("l", listId); chkPrev.Parameters.AddWithValue("prev", currentOwner.Value); await using var r = await chkPrev.ExecuteReaderAsync(ct); if (await r.ReadAsync(ct)) { hadMembership = true; wasRevoked = r.GetBoolean(0); } }
            if (!hadMembership)
            {
                await using (var insPrev = new NpgsqlCommand("insert into list_memberships(list_id,user_id,role,via_code,revoked) values(@l,@u,'Contributor',null,false)", conn, tx))
                { insPrev.Parameters.AddWithValue("l", listId); insPrev.Parameters.AddWithValue("u", currentOwner.Value); await insPrev.ExecuteNonQueryAsync(ct); }
            }
            else if (wasRevoked)
            {
                await using (var restorePrev = new NpgsqlCommand("update list_memberships set role='Contributor', revoked=false where list_id=@l and user_id=@u", conn, tx))
                { restorePrev.Parameters.AddWithValue("l", listId); restorePrev.Parameters.AddWithValue("u", currentOwner.Value); await restorePrev.ExecuteNonQueryAsync(ct); }
            }
            // Promote / create new owner membership
            int affected;
            await using (var updNew = new NpgsqlCommand("update list_memberships set role='Owner' where list_id=@l and user_id=@u and revoked=false", conn, tx))
            { updNew.Parameters.AddWithValue("l", listId); updNew.Parameters.AddWithValue("u", newOwnerUserId); affected = await updNew.ExecuteNonQueryAsync(ct); }
            if (affected == 0)
            {
                await using var insNew = new NpgsqlCommand("insert into list_memberships(list_id,user_id,role,via_code,revoked) values(@l,@u,'Owner',null,false) on conflict(list_id,user_id) do update set role='Owner', revoked=false", conn, tx);
                insNew.Parameters.AddWithValue("l", listId); insNew.Parameters.AddWithValue("u", newOwnerUserId); await insNew.ExecuteNonQueryAsync(ct);
            }
            // Update creator reference
            await using (var updCreator = new NpgsqlCommand("update lists set user_id=@u where id=@l", conn, tx))
            { updCreator.Parameters.AddWithValue("u", newOwnerUserId); updCreator.Parameters.AddWithValue("l", listId); await updCreator.ExecuteNonQueryAsync(ct); }
            await tx.CommitAsync(ct);
            return true;
        }
        catch { try { await tx.RollbackAsync(ct); } catch { } return false; }
    }

    public async Task<int?> GetListOwnerUserIdAsync(int listId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        // Prefer membership role Owner
        await using (var roleCmd = new NpgsqlCommand("select user_id from list_memberships where list_id=@l and role='Owner' and revoked=false", conn))
        { roleCmd.Parameters.AddWithValue("l", listId); var resRole = await roleCmd.ExecuteScalarAsync(ct); if (resRole is int uidRole) return uidRole; }
        // Fallback to list creator
        await using var cmd = new NpgsqlCommand("select user_id from lists where id=@l", conn);
        cmd.Parameters.AddWithValue("l", listId);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is int i ? i : null;
    }

    public async Task<bool> SetItemCompletedByUserAsync(int itemId, int userId, bool completed, CancellationToken ct = default)
    { return await SetItemCompletedByUserInternalAsync(itemId, userId, completed, ct); }

    private async Task<bool> SetItemCompletedByUserInternalAsync(int itemId, int userId, bool completed, CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString); await conn.OpenAsync(ct);
        int listId; int? parentId; bool currentCompleted;
        await using (var info = new NpgsqlCommand("select list_id,parent_item_id,is_completed from items where id=@i", conn))
        { info.Parameters.AddWithValue("i", itemId); await using var r = await info.ExecuteReaderAsync(ct); if (!await r.ReadAsync(ct)) return false; listId = r.GetInt32(0); parentId = r.IsDBNull(1)?null:r.GetInt32(1); currentCompleted = r.GetBoolean(2); }
        if (currentCompleted == completed) return true;
        await using var tx = await conn.BeginTransactionAsync(ct);
        string username=""; if (userId != 0) { await using (var getUser = new NpgsqlCommand("select username from users where id=@u", conn, tx)) { getUser.Parameters.AddWithValue("u", userId); var uObj = await getUser.ExecuteScalarAsync(ct); username = uObj as string ?? string.Empty; } }
        if (completed)
        {
            await using (var chkChildren = new NpgsqlCommand("select count(*) from items where parent_item_id=@i and is_completed=false", conn, tx))
            { chkChildren.Parameters.AddWithValue("i", itemId); var cntObj = await chkChildren.ExecuteScalarAsync(ct); var cnt = cntObj is long l ? (int)l : cntObj is int i ? i : 0; if (cnt > 0) { await tx.RollbackAsync(ct); return false; } }
            await using (var upd = new NpgsqlCommand("update items set is_completed=true,completed_by_user_id=@cuser,last_action_user_id=@u,last_action_username=@un where id=@i", conn, tx))
            { upd.Parameters.AddWithValue("cuser", userId == 0 ? (object?)DBNull.Value : userId); upd.Parameters.AddWithValue("u", userId == 0 ? (object?)DBNull.Value : userId); upd.Parameters.AddWithValue("un", (object?)username ?? DBNull.Value); upd.Parameters.AddWithValue("i", itemId); await upd.ExecuteNonQueryAsync(ct); }
            if (parentId != null)
            {
                await using (var allDone = new NpgsqlCommand("select count(*) from items where parent_item_id=@p and is_completed=false", conn, tx))
                { allDone.Parameters.AddWithValue("p", parentId.Value); var remainObj = await allDone.ExecuteScalarAsync(ct); var remain = remainObj is long l2 ? (int)l2 : remainObj is int i2 ? i2 : 0; if (remain == 0) { await using var updParent = new NpgsqlCommand("update items set is_completed=true,completed_by_user_id=@cuser,last_action_user_id=@u,last_action_username=@un where id=@p", conn, tx); updParent.Parameters.AddWithValue("cuser", userId == 0 ? (object?)DBNull.Value : userId); updParent.Parameters.AddWithValue("u", userId == 0 ? (object?)DBNull.Value : userId); updParent.Parameters.AddWithValue("un", (object?)username ?? DBNull.Value); updParent.Parameters.AddWithValue("p", parentId.Value); await updParent.ExecuteNonQueryAsync(ct); } }
            }
        }
        else
        {
            await using (var upd = new NpgsqlCommand("update items set is_completed=false,completed_by_user_id=null,last_action_user_id=@u,last_action_username=@un where id=@i", conn, tx))
            { upd.Parameters.AddWithValue("u", userId == 0 ? (object?)DBNull.Value : userId); upd.Parameters.AddWithValue("un", (object?)username ?? DBNull.Value); upd.Parameters.AddWithValue("i", itemId); await upd.ExecuteNonQueryAsync(ct); }
            var currentParent = parentId;
            while (currentParent != null)
            {
                await using (var updAnc = new NpgsqlCommand("update items set is_completed=false,completed_by_user_id=null where id=@p", conn, tx))
                { updAnc.Parameters.AddWithValue("p", currentParent.Value); await updAnc.ExecuteNonQueryAsync(ct); }
                int? nextParent = null; await using (var getp = new NpgsqlCommand("select parent_item_id from items where id=@p", conn, tx)) { getp.Parameters.AddWithValue("p", currentParent.Value); var res = await getp.ExecuteScalarAsync(ct); nextParent = res is int ip ? (int?)ip : null; }
                currentParent = nextParent;
            }
        }
        await using (var bump = new NpgsqlCommand("update lists set revision=revision+1 where id=@l", conn, tx)) { bump.Parameters.AddWithValue("l", listId); await bump.ExecuteNonQueryAsync(ct); }
        await tx.CommitAsync(ct);
        return true;
    }
}
