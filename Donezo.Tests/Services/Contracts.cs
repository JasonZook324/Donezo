namespace Donezo.Tests.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    // Minimal contracts for unit tests to avoid referencing the MAUI project.
    public interface INeonDbService
    {
        Task<int> CreateListAsync(int userId, string name, bool isDaily);
        Task<IReadOnlyList<ListRecord>> GetOwnedListsAsync(int userId);
        Task<int> AddItemAsync(int listId, string name);
        Task<IReadOnlyList<ItemRecord>> GetItemsAsync(int listId);
        Task<ItemRecord?> GetItemAsync(int itemId);
        Task<bool> SetItemCompletedByUserAsync(int itemId, int userId, bool completed);
        Task<int> ResetListAsync(int listId);
        Task<bool> SetListDailyAsync(int listId, bool isDaily);
        Task<long> GetListRevisionAsync(int listId);
        Task<int?> GetUserIdAsync(string username);
        Task<bool> AuthenticateUserAsync(string username, string password);
        // Helpers for tests
        Task<int> RegisterAndGetUserAsync(string username, string password);
    }

    public record ListRecord(int Id, string Name, bool IsDaily);
    public record SharedListRecord(int Id, string Name, bool IsDaily, string Role);
    public class ItemRecord
    {
        public int Id { get; set; }
        public int ListId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public int? ParentItemId { get; set; }
        public bool HasChildren { get; set; }
        public int ChildrenCount { get; set; }
        public int IncompleteChildrenCount { get; set; }
        public int Level { get; set; }
        public string SortKey { get; set; } = string.Empty;
        public int Order { get; set; }
        public int? CompletedByUserId { get; set; }
        public string? CompletedByUsername { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    public record ShareCodeRecord(int Id, int ListId, string Code, string Role, DateTime? ExpirationUtc, int MaxRedeems, int RedeemedCount, bool IsDeleted);
    public record MembershipRecord(int Id, int ListId, int UserId, string Username, string Role, DateTime JoinedUtc, bool Revoked, string? Code);
}
