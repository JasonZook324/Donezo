using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Donezo.Tests.Services;

namespace Donezo.Tests.Services
{
    // Minimal in-memory implementation for unit testing daily reset behavior.
    public class InMemoryNeonDbService : INeonDbService
    {
        private int _nextUserId = 1;
        private int _nextListId = 100;
        private int _nextItemId = 1000;
        private readonly Dictionary<int, (string Username, string Password)> _users = new();
        private readonly Dictionary<int, ListRecord> _lists = new();
        private readonly Dictionary<int, List<ItemRecord>> _itemsByList = new();
        private readonly Dictionary<int, bool> _dailyByList = new();
        private readonly Dictionary<int, long> _revisionByList = new();

        public Task<int> RegisterAndGetUserAsync(string username, string password)
        {
            var id = _nextUserId++;
            _users[id] = (username, password);
            return Task.FromResult(id);
        }

        public Task<bool> AuthenticateUserAsync(string username, string password)
        {
            return Task.FromResult(_users.Values.Any(u => u.Username == username && u.Password == password));
        }

        public Task<int> CreateListAsync(int ownerUserId, string name, bool isDaily)
        {
            var id = _nextListId++;
            _lists[id] = new ListRecord(id, name, isDaily);
            _itemsByList[id] = new List<ItemRecord>();
            _dailyByList[id] = isDaily;
            _revisionByList[id] = 1;
            return Task.FromResult(id);
        }

        public Task<IReadOnlyList<ListRecord>> GetOwnedListsAsync(int userId)
            => Task.FromResult<IReadOnlyList<ListRecord>>(_lists.Values.ToList());

        public Task<int> AddItemAsync(int listId, string name)
        {
            var id = _nextItemId++;
            var order = _itemsByList[listId].Count + 1;
            _itemsByList[listId].Add(new ItemRecord
            {
                Id = id,
                ListId = listId,
                Name = name,
                IsCompleted = false,
                ParentItemId = null,
                HasChildren = false,
                ChildrenCount = 0,
                IncompleteChildrenCount = 0,
                Level = 1,
                Order = order,
                SortKey = name
            });
            _revisionByList[listId]++;
            return Task.FromResult(id);
        }

        public Task<IReadOnlyList<ItemRecord>> GetItemsAsync(int listId)
            => Task.FromResult<IReadOnlyList<ItemRecord>>(_itemsByList[listId].Select(Clone).ToList());

        public Task<ItemRecord?> GetItemAsync(int itemId)
        {
            var rec = _itemsByList.Values.SelectMany(x => x).FirstOrDefault(i => i.Id == itemId);
            return Task.FromResult(rec != null ? Clone(rec) : null);
        }

        public Task<bool> SetItemCompletedByUserAsync(int itemId, int userId, bool completed)
        {
            var rec = _itemsByList.Values.SelectMany(x => x).FirstOrDefault(i => i.Id == itemId);
            if (rec == null) return Task.FromResult(false);
            rec.IsCompleted = completed;
            _revisionByList[rec.ListId]++;
            return Task.FromResult(true);
        }

        public Task<int> ResetListAsync(int listId)
        {
            if (!_itemsByList.TryGetValue(listId, out var listItems)) return Task.FromResult(0);
            int affected = 0;
            foreach (var it in listItems)
            {
                if (it.IsCompleted) { it.IsCompleted = false; affected++; }
            }
            _revisionByList[listId]++;
            return Task.FromResult(affected);
        }

        public Task<bool> SetListDailyAsync(int listId, bool isDaily)
        {
            if (!_lists.ContainsKey(listId)) return Task.FromResult(false);
            _lists[listId] = new ListRecord(listId, _lists[listId].Name, isDaily);
            _dailyByList[listId] = isDaily;
            _revisionByList[listId]++;
            return Task.FromResult(true);
        }

        public Task<long> GetListRevisionAsync(int listId)
            => Task.FromResult(_revisionByList.TryGetValue(listId, out var r) ? r : 0);

        public Task<int?> GetUserIdAsync(string username) => Task.FromResult<int?>(_users.FirstOrDefault(x => x.Value.Username == username).Key);

        private static ItemRecord Clone(ItemRecord r) => new ItemRecord
        {
            Id = r.Id,
            ListId = r.ListId,
            Name = r.Name,
            IsCompleted = r.IsCompleted,
            ParentItemId = r.ParentItemId,
            HasChildren = r.HasChildren,
            ChildrenCount = r.ChildrenCount,
            IncompleteChildrenCount = r.IncompleteChildrenCount,
            Level = r.Level,
            Order = r.Order,
            SortKey = r.SortKey,
            CompletedByUserId = r.CompletedByUserId,
            CompletedByUsername = r.CompletedByUsername,
            CompletedAtUtc = r.CompletedAtUtc
        };
    }
}
