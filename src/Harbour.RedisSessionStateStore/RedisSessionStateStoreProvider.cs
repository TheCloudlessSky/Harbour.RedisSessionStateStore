using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.SessionState;
using System.Collections.Specialized;
using System.Web;
using System.Web.Configuration;
using ServiceStack.Redis;
using System.Configuration.Provider;
using System.IO;
using System.Configuration;
using ServiceStack.Redis.Support.Locking;

namespace Harbour.RedisSessionStateStore
{
    /// <summary>
    /// A SessionStateProvider implementation for Redis using the ServiceStack.Redis client.
    /// </summary>
    /// <example>
    /// In your web.config (with the <code>host</code> and <code>clientType</code>
    /// attributes being optional):
    /// <code>
    /// <![CDATA[
    ///   <system.web>
    ///     <sessionState mode="Custom" customProvider="RedisSessionStateProvider">
    ///       <providers>
    ///         <clear />
    ///         <add name="RedisSessionStateProvider" 
    ///              type="Harbour.RedisSessionStateStore.RedisSessionStateStoreProvider" 
    ///              host="localhost:6379" clientType="pooled" />
    ///       </providers>
    ///     </sessionState>
    ///   </system.web>
    /// ]]>
    /// </code>
    /// If you wish to use a custom <code>IRedisClientsManager</code>, you can 
    /// do the following in your <code>Global.asax.cs</code>:
    /// <code>
    /// <![CDATA[
    ///   private IRedisClientsManager clientManager;
    ///  
    ///   protected void Application_Start()
    ///   {
    ///       // Or use your IoC container to wire this up.
    ///       clientManager = new PooledRedisClientManager("localhost:6379");
    ///       RedisSessionStateStoreProvider.SetClientManager(clientManager);
    ///   }
    ///  
    ///   protected void Application_End()
    ///   {
    ///       clientManager.Dispose();
    ///   }
    /// ]]>
    /// </code>
    /// </example>
    public sealed class RedisSessionStateStoreProvider : SessionStateStoreProviderBase
    {
        private static IRedisClientsManager clientManagerStatic;
        private static object locker = new object();

        private readonly Func<HttpContext, HttpStaticObjectsCollection> staticObjectsGetter;
        private IRedisClientsManager clientManager;
        private bool manageClientManagerLifetime;
        private string name;
        private int sessionTimeoutMinutes;

        /// <summary>
        /// Gets the client manager for the provider.
        /// </summary>
        public IRedisClientsManager ClientManager { get { return clientManager; } }

        internal RedisSessionStateStoreProvider(Func<HttpContext, HttpStaticObjectsCollection> staticObjectsGetter)
        {
            this.staticObjectsGetter = staticObjectsGetter;
        }

#pragma warning disable 1591

        public RedisSessionStateStoreProvider()
        {
            staticObjectsGetter = ctx => SessionStateUtility.GetSessionStaticObjects(ctx);
        }

        /// <summary>
        /// Sets the client manager to be used for the session state provider. 
        /// This client manager's lifetime will not be managed by the RedisSessionStateProvider.
        /// However, if this is not set, a client manager will be created and
        /// managed by the RedisSessionStateProvider.
        /// </summary>
        /// <param name="clientManager"></param>
        public static void SetClientManager(IRedisClientsManager clientManager)
        {
            if (clientManager == null) throw new ArgumentNullException();
            if (clientManagerStatic != null)
            {
                throw new InvalidOperationException("The client manager can only be configured once.");
            }
            clientManagerStatic = clientManager;
        }

        internal static void ResetClientManager()
        {
            clientManagerStatic = null;
        }
        
        public override void Initialize(string name, NameValueCollection config)
        {
            if (String.IsNullOrWhiteSpace(name))
            {
                name = "AspNetSession";
            }

            this.name = name;

            var sessionConfig = (SessionStateSection)WebConfigurationManager.GetSection("system.web/sessionState");
            sessionTimeoutMinutes = (int)sessionConfig.Timeout.TotalMinutes;

            lock (locker)
            {
                if (clientManagerStatic == null)
                {
                    var host = config["host"];
                    var clientType = config["clientType"];

                    clientManager = CreateClientManager(clientType, host);
                    manageClientManagerLifetime = true;
                }
                else
                {
                    clientManager = clientManagerStatic;
                    manageClientManagerLifetime = false;
                }
            }

            base.Initialize(name, config);
        }

        private IRedisClientsManager CreateClientManager(string clientType, string host)
        {
            if (String.IsNullOrWhiteSpace(host))
            {
                host = "localhost:6379";
            }

            if (String.IsNullOrWhiteSpace(clientType))
            {
                clientType = "POOLED";
            }

            if (clientType.ToUpper() == "POOLED")
            {
                return new PooledRedisClientManager(host);
            }
            else
            {
                return new BasicRedisClientManager(host);
            }
        }

        private IRedisClient GetClient()
        {
            return clientManager.GetClient();
        }
        
        /// <summary>
        /// Create a distributed lock for cases where more-than-a-transaction
        /// is used but we need to prevent another request from modifying the
        /// session. For example, if we need to get the session, mutate it and
        /// then write it back. We can't use *just* a transaction for this 
        /// approach because the data is returned with the rest of the commands!
        /// </summary>
        /// <param name="client"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        private DisposableDistributedLock GetDistributedLock(IRedisClient client, string key)
        {
            var lockKey = key + "/lock";
            return new DisposableDistributedLock(client, lockKey, acquisitionTimeout: 1/*second*/, lockTimeout: 1/*second*/);
        }

        private string GetSessionIdKey(string id)
        {
            return name + "/" + id;
        }

        public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
        {
            var key = GetSessionIdKey(id);
            using (var client = GetClient())
            {
                var state = new RedisSessionState()
                {
                    Timeout = timeout,
                    Flags = SessionStateActions.InitializeItem
                };

                UpdateSessionState(client, key, state);
            }
        }

        public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
        {
            return new SessionStateStoreData(new SessionStateItemCollection(),
               staticObjectsGetter(context),
               timeout);
        }

        public override void InitializeRequest(HttpContext context)
        {
            
        }

        public override void EndRequest(HttpContext context)
        {
            
        }

        private void UseTransaction(IRedisClient client, Action<IRedisTransaction> action)
        {
            using (var transaction = client.CreateTransaction())
            {
                action(transaction);
                transaction.Commit();
            }
        }

        public override void ResetItemTimeout(HttpContext context, string id)
        {
            var key = GetSessionIdKey(id);
            using (var client = GetClient())
            {
                UseTransaction(client, transaction =>
                {
                    transaction.QueueCommand(c => c.ExpireEntryIn(key, TimeSpan.FromMinutes(sessionTimeoutMinutes)));
                });
            };
        }

        public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
        {
            var key = GetSessionIdKey(id);
            using (var client = GetClient())
            using (var distributedLock = GetDistributedLock(client, key))
            {
                if (distributedLock.LockState == DistributedLock.LOCK_NOT_ACQUIRED)
                {
                    return;
                }

                var stateRaw = client.GetAllEntriesFromHashRaw(key);

                UseTransaction(client, transaction =>
                {
                    RedisSessionState state;
                    if (RedisSessionState.TryParse(stateRaw, out state) && state.Locked && state.LockId == (int)lockId)
                    {
                        transaction.QueueCommand(c => c.Remove(key));
                    }
                });
            }
        }

        public override SessionStateStoreData GetItem(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            return GetItem(false, context, id, out locked, out lockAge, out lockId, out actions);
        }

        public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            return GetItem(true, context, id, out locked, out lockAge, out lockId, out actions);
        }

        private SessionStateStoreData GetItem(bool isExclusive, HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            locked = false;
            lockAge = TimeSpan.Zero;
            lockId = null;
            actions = SessionStateActions.None;
            SessionStateStoreData result = null;

            var key = GetSessionIdKey(id);
            using (var client = GetClient())
            using (var distributedLock = GetDistributedLock(client, key))
            {
                if (distributedLock.LockState == DistributedLock.LOCK_NOT_ACQUIRED)
                {
                    return null;
                }

                var stateRaw = client.GetAllEntriesFromHashRaw(key);

                RedisSessionState state;
                if (!RedisSessionState.TryParse(stateRaw, out state))
                {
                    return null;
                }

                actions = state.Flags;

                if (state.Locked)
                {
                    locked = true;
                    lockId = state.LockId;
                    lockAge = DateTime.UtcNow - state.LockDate;
                    return null;
                }

                if (isExclusive)
                {
                    locked = state.Locked = true;
                    state.LockDate = DateTime.UtcNow;
                    lockAge = TimeSpan.Zero;
                    lockId = ++state.LockId;
                }

                state.Flags = SessionStateActions.None;

                UseTransaction(client, transaction =>
                {
                    transaction.QueueCommand(c => c.SetRangeInHashRaw(key, state.ToMap()));
                    transaction.QueueCommand(c => c.ExpireEntryIn(key, TimeSpan.FromMinutes(state.Timeout)));
                });

                var items = actions == SessionStateActions.InitializeItem ? new SessionStateItemCollection() : state.Items;

                result = new SessionStateStoreData(items, staticObjectsGetter(context), state.Timeout);
            }

            return result;
        }

        public override void ReleaseItemExclusive(HttpContext context, string id, object lockId)
        {
            var key = GetSessionIdKey(id);
            using (var client = GetClient())
            {
                UpdateSessionStateIfLocked(client, key, (int)lockId, state =>
                {
                    state.Locked = false;
                    state.Timeout = sessionTimeoutMinutes;
                });
            }
        }

        public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
        {
            var key = GetSessionIdKey(id);
            using (var client = GetClient())
            {
                if (newItem)
                {
                    var state = new RedisSessionState()
                    {
                        Items = (SessionStateItemCollection)item.Items,
                        Timeout = item.Timeout,
                    };

                    UpdateSessionState(client, key, state);
                }
                else
                {
                    UpdateSessionStateIfLocked(client, key, (int)lockId, state =>
                    {
                        state.Items = (SessionStateItemCollection)item.Items;
                        state.Locked = false;
                        state.Timeout = item.Timeout;
                    });
                }
            }
        }

        private void UpdateSessionStateIfLocked(IRedisClient client, string key, int lockId, Action<RedisSessionState> stateAction)
        {
            using (var distributedLock = GetDistributedLock(client, key))
            {
                if (distributedLock.LockState == DistributedLock.LOCK_NOT_ACQUIRED)
                {
                    return;
                }

                var stateRaw = client.GetAllEntriesFromHashRaw(key);
                RedisSessionState state;
                if (RedisSessionState.TryParse(stateRaw, out state) && state.Locked && state.LockId == lockId)
                {
                    stateAction(state);
                    UpdateSessionState(client, key, state);
                }
            }
        }

        private void UpdateSessionState(IRedisClient client, string key, RedisSessionState state)
        {
            UseTransaction(client, transaction =>
            {
                transaction.QueueCommand(c => c.SetRangeInHashRaw(key, state.ToMap()));
                transaction.QueueCommand(c => c.ExpireEntryIn(key, TimeSpan.FromMinutes(state.Timeout)));
            });
        }

        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            // Redis < 2.8 doesn't easily support key expiry notifications.
            // As of Redis 2.8, keyspace notifications (http://redis.io/topics/notifications)
            // can be used. Therefore, if you'd like to support the expiry
            // callback and are using Redis 2.8, you can inherit from this
            // class and implement it.
            return false;
        }

        public override void Dispose()
        {
            if (manageClientManagerLifetime)
            {
                clientManager.Dispose();
            }
        }

#pragma warning restore 1591
    }
}
