using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Donezo.Tests.Services;

namespace Donezo.Tests.Services
{
    public class NeonDbService_DailyResetTests
    {
        [Fact]
        public async Task DailyToggle_ShouldPersist_OnOwnedList()
        {
            var db = new InMemoryNeonDbService();
            var userId = await db.RegisterAndGetUserAsync("u", "p");
            var listId = await db.CreateListAsync(userId, "My List", isDaily: false);

            var before = (await db.GetOwnedListsAsync(userId)).First(l => l.Id == listId);
            Assert.False(before.IsDaily);

            var ok = await db.SetListDailyAsync(listId, true);
            Assert.True(ok);

            var after = (await db.GetOwnedListsAsync(userId)).First(l => l.Id == listId);
            Assert.True(after.IsDaily);
        }

        [Fact]
        public async Task ResetList_ShouldUncomplete_AllCompletedItems()
        {
            var db = new InMemoryNeonDbService();
            var userId = await db.RegisterAndGetUserAsync("u2", "p");
            var listId = await db.CreateListAsync(userId, "Daily", isDaily: true);
            var a = await db.AddItemAsync(listId, "A");
            var b = await db.AddItemAsync(listId, "B");

            Assert.True(await db.SetItemCompletedByUserAsync(a, userId, completed: true));
            Assert.True(await db.SetItemCompletedByUserAsync(b, userId, completed: true));

            var affected = await db.ResetListAsync(listId);
            Assert.Equal(2, affected);

            var items = await db.GetItemsAsync(listId);
            Assert.All(items, r => Assert.False(r.IsCompleted));
        }
    }
}
