using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using System.Collections.Specialized;
using ServiceStack.Redis;
using Xunit.Extensions;
using System.Web;
using Moq;
using System.Web.SessionState;
using System.Collections;

namespace Harbour.RedisSessionStateStore.Tests
{
    public class RedisSessionStateStoreProviderTests : RedisTest
    {
        private const string KeyName = "Harbour";
        private const string key = KeyName + "/1234";

        private readonly SessionStateItemCollection itemsA;

        public RedisSessionStateStoreProviderTests()
        {
            this.itemsA = new SessionStateItemCollection();
            this.itemsA["name"] = "Felix";
            this.itemsA["age"] = 1;
        }

        [Fact]
        public void Initialize_with_no_configured_clients_manager_can_create_pooled_clients_manager()
        {
            var provider = new RedisSessionStateStoreProvider();
            provider.Initialize("APP_NAME", new NameValueCollection()
            {
                { "Host", "9.9.9.9:999" },
                { "clientType", "pooled" }
            });
            Assert.IsType<PooledRedisClientManager>(provider.ClientManager);
        }

        [Fact]
        public void Initialize_with_no_configured_clients_manager_can_create_basic_clients_manager()
        {
            var provider = new RedisSessionStateStoreProvider();
            provider.Initialize("APP_NAME", new NameValueCollection()
            {
                { "Host", "9.9.9.9:999" },
                { "clientType", "basic" }
            });
            Assert.IsType<BasicRedisClientManager>(provider.ClientManager);
        }

        [Fact]
        public void Initialize_with_specified_clients_manager_should_not_manage_lifetime()
        {
            try
            {
                var clientManager = new Mock<IRedisClientsManager>();
                RedisSessionStateStoreProvider.SetClientManager(clientManager.Object);
                var provider = new RedisSessionStateStoreProvider();
                provider.Initialize(KeyName, new NameValueCollection());

                Assert.Same(clientManager.Object, provider.ClientManager);

                provider.Dispose();

                clientManager.Verify(m => m.Dispose(), Times.Never());
            }
            finally
            {
                RedisSessionStateStoreProvider.ResetClientManager();
            }
        }

        [Fact]
        public void SetItemExpireCallback_is_not_supported()
        {
            Assert.False(this.CreateProvider().SetItemExpireCallback((x, y) => { } ));
        }

        [Fact]
        public void CreateUnitializedItem()
        {
            var provider = this.CreateProvider();
            provider.CreateUninitializedItem(null, "1234", 555);

            AssertState(key,
                locked: false, lockId: 0, lockDate: DateTime.MinValue,
                timeout: 555, flags: SessionStateActions.InitializeItem);
        }

        [Fact]
        public void ResetItemTimeout()
        {
            var provider = this.CreateProvider();

            redis.SetSessionState(key, new RedisSessionState());
            redis.ExpireEntryIn(key, TimeSpan.FromMinutes(10));

            provider.ResetItemTimeout(null, "1234");

            var ttl = redis.GetTimeToLive(key);
            // Default from the web.config.
            Assert.Equal(20, ttl.TotalMinutes);
        }

        [Fact]
        public void RemoveItem_should_remove_if_lockId_matches()
        {
            var provider = this.CreateProvider();
            redis.SetSessionState(key, new RedisSessionState()
            {
                Locked = true,
                LockDate = DateTime.UtcNow,
                LockId = 999
            });

            provider.RemoveItem(null, "1234", 999, null);
            Assert.False(redis.ContainsKey(key));
        }

        [Fact]
        public void RemoveItem_should_not_remove_if_lockId_does_not_match()
        {
            var provider = this.CreateProvider();
            redis.SetSessionState(key, new RedisSessionState()
            {
                Locked = true,
                LockDate = DateTime.UtcNow,
                LockId = 999
            });

            provider.RemoveItem(null, "1234", 111, null);
            Assert.True(redis.ContainsKey(key));
        }

        [Fact]
        public void RemoveItem_should_not_remove_if_session_id_does_not_exist()
        {
            var provider = this.CreateProvider();
            redis.SetSessionState(key, new RedisSessionState()
            {
                Locked = true,
                LockDate = DateTime.UtcNow,
                LockId = 999
            });

            provider.RemoveItem(null, "5678", 999, null);
            Assert.True(redis.ContainsKey(key));
        }

        [Fact]
        public void ReleaseItemExclusive_should_not_remove_lock_if_lockId_does_not_match()
        {
            var provider = this.CreateProvider();

            var lockDate = DateTime.UtcNow;

            redis.SetSessionState(key, new RedisSessionState()
            {
                Locked = true, LockId = 999, LockDate = lockDate
            });

            provider.ReleaseItemExclusive(null, "5678", 111);

            AssertState(key,
                locked: true, lockId: 999, lockDate: lockDate);
        }

        [Fact]
        public void ReleaseItemExclusive_should_not_remove_lock_if_session_id_does_not_exist()
        {
            var provider = this.CreateProvider();

            var lockDate = DateTime.UtcNow;

            redis.SetSessionState(key, new RedisSessionState()
            {
                Locked = true, LockId = 2, LockDate = lockDate
            });

            provider.ReleaseItemExclusive(null, "1234", 111);

            AssertState(key,
                locked: true, lockId: 2, lockDate: lockDate);
        }

        [Fact]
        public void ReleaseItemExclusive_should_clear_lock_and_reset_timeout_for_locked_session()
        {
            var provider = this.CreateProvider();
            var lockDate = DateTime.UtcNow;

            redis.SetSessionState(key, new RedisSessionState()
            {
                Locked = true, LockId = 222, LockDate = lockDate
            });

            provider.ReleaseItemExclusive(null, "1234", 222);

            AssertState(key,
                locked: false, lockId: 0, lockDate: DateTime.MinValue,
                ttl: 20);
        }

        [Fact]
        public void SetAndReleaseItemExclusive_should_add_new_item_and_set_timeout_if_newItem_is_true()
        {
            var provider = this.CreateProvider();

            var item = this.CreateSessionStoreData(333, new Dictionary<string, object>()
            {
                { "name", "Felix" },
                { "age", 1 }
            });

            provider.SetAndReleaseItemExclusive(null, "1234", item, null, true);

            AssertState(key,
                locked: false,
                flags: 0,
                timeout: 333,
                items: new Hashtable()
                {
                    { "name", "Felix" },
                    { "age", 1 }
                });
        }

        [Fact]
        public void SetAndReleaseItemExclusive_should_not_update_if_lock_id_does_not_match()
        {
            var provider = this.CreateProvider();

            var lockDate = DateTime.UtcNow;
            var validLockId = 999;
            var invalidLockId = 111;

            redis.SetSessionState(key, new RedisSessionState()
            {
                Locked = true, LockId = validLockId, LockDate = lockDate
            });

            var item = this.CreateSessionStoreData(333, new Dictionary<string, object>());

            provider.SetAndReleaseItemExclusive(null, "1234", item, invalidLockId, false);

            AssertState(key, 
                locked: true, lockId: validLockId, lockDate: lockDate);
        }

        [Fact]
        public void SetAndReleaseItemExclusive_should_update_items_and_release_lock_for_locked_session()
        {
            var provider = this.CreateProvider();

            redis.SetSessionState(key, new RedisSessionState()
            {
                Locked = true,
                LockId = 1,
                LockDate = DateTime.UtcNow,
                Items = itemsA
            });

            var updatedItems = this.CreateSessionStoreData(999, new Dictionary<string, object>()
            {
                { "name", "Daisy" },
                { "age", 3 }
            });

            provider.SetAndReleaseItemExclusive(null, "1234", updatedItems, 1, false);

            AssertState(key,
                locked: false, ttl: 999,
                items: new Hashtable()
                {
                    { "name", "Daisy" },
                    { "age", 3 }
                });
        }

        [Fact]
        public void GetItem_should_return_null_and_not_locked_if_no_session_item_is_found()
        {
            var provider = this.CreateProvider();

            bool locked;
            TimeSpan lockAge;
            object lockId;
            SessionStateActions actions;

            var data = provider.GetItem(null, "1234", out locked, out lockAge, out lockId, out actions);

            Assert.Null(data);
            Assert.False(locked);
            Assert.Equal(SessionStateActions.None, actions);
        }

        [Fact]
        public void GetItem_should_return_null_and_locked_if_session_item_is_found_but_is_locked()
        {
            var provider = this.CreateProvider();
            var lockDate = DateTime.UtcNow.AddHours(-1);

            redis.SetSessionState(key, new RedisSessionState()
            {
                Locked = true, LockDate = lockDate, LockId = 1
            });

            bool locked;
            TimeSpan lockAge;
            object lockId;
            SessionStateActions actions;

            var data = provider.GetItem(null, "1234", out locked, out lockAge, out lockId, out actions);

            Assert.Null(data);
            Assert.True(locked);
            AssertInRange(TimeSpan.FromHours(1), lockAge);
            Assert.Equal(1, lockId);
            Assert.Equal(SessionStateActions.None, actions);
        }

        [Fact]
        public void GetItem_should_return_data_and_extend_session_if_session_found_and_not_locked()
        {
            var provider = this.CreateProvider();

            redis.SetSessionState(key, new RedisSessionState()
            {
                Flags = SessionStateActions.None,
                Items = itemsA,
                Timeout = 80
            });

            bool locked;
            TimeSpan lockAge;
            object lockId;
            SessionStateActions actions;

            var data = provider.GetItem(null, "1234", out locked, out lockAge, out lockId, out actions);
            var ttl = redis.GetTimeToLive(key);
            Assert.Equal(80, data.Timeout);
            Assert.Equal(80, ttl.TotalMinutes);

            Assert.False(locked);
            Assert.Equal(SessionStateActions.None, actions);

            AssertStateItems(new Hashtable()
            {
                { "name", "Felix" },
                { "age", 1 }
            }, data.Items);
        }

        [Fact]
        public void GetItemExclusive_should_return_null_and_not_locked_if_no_session_item_is_found()
        {
            var provider = this.CreateProvider();

            bool locked;
            TimeSpan lockAge;
            object lockId;
            SessionStateActions actions;

            var data = provider.GetItemExclusive(null, "1234", out locked, out lockAge, out lockId, out actions);

            Assert.Null(data);
            Assert.False(locked);
            Assert.Equal(SessionStateActions.None, actions);
        }

        [Fact]
        public void GetItemExclusive_should_return_null_and_locked_if_session_item_is_found_but_is_locked()
        {
            var provider = this.CreateProvider();
            var lockDate = DateTime.UtcNow.AddHours(-1);

            redis.SetSessionState(key, new RedisSessionState()
            {
                Locked = true,
                LockDate = lockDate,
                LockId = 1
            });

            bool locked;
            TimeSpan lockAge;
            object lockId;
            SessionStateActions actions;

            var data = provider.GetItemExclusive(null, "1234", out locked, out lockAge, out lockId, out actions);

            Assert.Null(data);
            Assert.True(locked);
            AssertInRange(TimeSpan.FromHours(1), lockAge);
            Assert.Equal(1, lockId);
            Assert.Equal(SessionStateActions.None, actions);
        }

        [Fact]
        public void GetItemExclusive_should_return_data_and_lock_session_and_extend_session_if_session_found_and_not_locked()
        {
            var provider = this.CreateProvider();

            redis.SetSessionState(key, new RedisSessionState()
            {
                Locked = false,
                Flags = SessionStateActions.None,
                Items = itemsA,
                Timeout = 80
            });

            bool locked;
            TimeSpan lockAge;
            object lockId;
            SessionStateActions actions;

            var data = provider.GetItemExclusive(null, "1234", out locked, out lockAge, out lockId, out actions);
            
            AssertState(key,
                ttl: 80,
                locked: true,
                lockId: 1,
                lockDate: DateTime.UtcNow);

            Assert.True(locked);
            Assert.Equal(TimeSpan.Zero, lockAge);
            Assert.Equal(1, lockId);
            Assert.Equal(SessionStateActions.None, actions);

            Assert.Equal(80, data.Timeout);
            AssertStateItems(new Hashtable()
            {
                { "name", "Felix" },
                { "age", 1 }
            }, data.Items);
        }

        private void AssertStateItems(IDictionary expected, ISessionStateItemCollection actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            foreach (var kvp in expected.Cast<DictionaryEntry>())
            {
                var name = (string)kvp.Key;
                Assert.Equal(kvp.Value, actual[name]);
            }
        }

        private RedisSessionState AssertState(string key, bool? locked = null, int? lockId = null, DateTime? lockDate = null, int? timeout = null, SessionStateActions? flags = null, int? ttl = null, IDictionary items = null)
        {
            var data = redis.GetSessionState(key);
            var ttlActual = redis.GetTimeToLive(key);

            if (ttl != null || timeout.HasValue)
            {
                var t = (ttl ?? (int)timeout) * 60;
                Assert.True(Math.Abs(t - ttlActual.TotalSeconds) < 10);
            }

            AssertCloseEnough(DateTime.UtcNow, data.Created);

            if (locked.HasValue) Assert.Equal(locked, data.Locked);
            if (lockId.HasValue) Assert.Equal(lockId, data.LockId);
            if (lockDate.HasValue) AssertCloseEnough(lockDate.Value, data.LockDate);
            if (timeout.HasValue) Assert.Equal(timeout, data.Timeout);
            if (flags.HasValue) Assert.Equal(flags, data.Flags);

            if (items != null)
            {
                AssertStateItems(items, data.Items);
            }

            return data;
        }

        private SessionStateStoreData CreateSessionStoreData(int timeout, IDictionary<string, object> itemsMap)
        {
            var items = new SessionStateItemCollection();
            foreach (var kvp in itemsMap)
            {
                items[kvp.Key] = kvp.Value;
            }

            return new SessionStateStoreData(items, new HttpStaticObjectsCollection(), timeout);
        }

        private void AssertCloseEnough(DateTime expected, DateTime actual, int fuzzSeconds = 10)
        {
            Assert.True((actual - expected).TotalSeconds < fuzzSeconds, "Dates close enough.");
        }

        private void AssertInRange(TimeSpan expected, TimeSpan actual, int fuzz = 10)
        {
            Assert.True(Math.Abs(expected.TotalSeconds - actual.TotalSeconds) < fuzz);
        }

        private RedisSessionStateStoreProvider CreateProvider(string host = null)
        {
            var provider = new RedisSessionStateStoreProvider(ctx => new HttpStaticObjectsCollection());

            provider.Initialize(KeyName, new NameValueCollection()
            {
                { "Host", host ?? this.Host },
                { "clientType", "basic" }
            });

            return provider;
        }
    }

    internal static class RedisClientExtensions
    {
        public static RedisSessionState GetSessionState(this IRedisClient redis, string key)
        {
            RedisSessionState state = null;
            RedisSessionState.TryParse(redis.GetAllEntriesFromHashRaw(key), out state);
            return state;
        }

        public static void SetSessionState(this IRedisClient redis, string key, RedisSessionState state)
        {
            redis.SetRangeInHashRaw(key, state.ToMap());
        }
    }
}
