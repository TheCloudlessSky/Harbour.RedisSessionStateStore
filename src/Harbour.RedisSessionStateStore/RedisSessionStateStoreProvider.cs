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
    ///       this.clientManager = new PooledRedisClientManager("localhost:6379");
    ///       RedisSessionStateStoreProvider.SetClientManager(this.clientManager);
    ///   }
    ///  
    ///   protected void Application_End()
    ///   {
    ///       this.clientManager.Dispose();
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
        public IRedisClientsManager ClientManager { get { return this.clientManager; } }

        internal RedisSessionStateStoreProvider(Func<HttpContext, HttpStaticObjectsCollection> staticObjectsGetter)
        {
            this.staticObjectsGetter = staticObjectsGetter;
        }

#pragma warning disable 1591

        public RedisSessionStateStoreProvider()
        {
            this.staticObjectsGetter = ctx => SessionStateUtility.GetSessionStaticObjects(ctx);
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
            this.sessionTimeoutMinutes = (int)sessionConfig.Timeout.TotalMinutes;

            lock (locker)
            {
                if (clientManagerStatic == null)
                {
                    var host = config["host"];
                    var clientType = config["clientType"];

                    this.clientManager = this.CreateClientManager(clientType, host);
                    this.manageClientManagerLifetime = true;
                }
                else
                {
                    this.clientManager = clientManagerStatic;
                    this.manageClientManagerLifetime = false;
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

        private string GetSessionIdKey(string id)
        {
            return this.name + "/" + id;
        }

        public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
        {
            var key = this.GetSessionIdKey(id);
            using (var client = this.GetClientAndWatch(key))
            {
                var state = new RedisSessionState()
                {
                    Timeout = timeout,
                    Flags = SessionStateActions.InitializeItem
                };

                this.UpdateSessionState(client, key, state);
            }
        }

        public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
        {
            return new SessionStateStoreData(new SessionStateItemCollection(),
               this.staticObjectsGetter(context),
               timeout);
        }

        public override void InitializeRequest(HttpContext context)
        {
            
        }

        public override void EndRequest(HttpContext context)
        {
            
        }

        private IRedisClient GetClientAndWatch(string key)
        {
            var client = this.clientManager.GetClient();
            client.Watch(key);
            return client;
        }

        public override void ResetItemTimeout(HttpContext context, string id)
        {
            var key = this.GetSessionIdKey(id);
            using (var client = this.GetClientAndWatch(key))
            using (var transaction = client.CreateTransaction())
            {
                transaction.QueueCommand(c => c.ExpireEntryIn(key, TimeSpan.FromMinutes(this.sessionTimeoutMinutes)));
                transaction.Commit();
            }
        }

        public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
        {
            var key = this.GetSessionIdKey(id);
            using (var client = this.GetClientAndWatch(key))
            {
                var stateRaw = client.GetAllEntriesFromHashRaw(key);

                using (var transaction = client.CreateTransaction())
                {
                    RedisSessionState state;
                    if (RedisSessionState.TryParse(stateRaw, out state) && state.Locked && state.LockId == (int)lockId)
                    {
                        transaction.QueueCommand(c => c.Remove(key));
                    }

                    transaction.Commit();
                }
            }
        }

        public override SessionStateStoreData GetItem(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            return this.GetItem(false, context, id, out locked, out lockAge, out lockId, out actions);
        }

        public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            return this.GetItem(true, context, id, out locked, out lockAge, out lockId, out actions);
        }

        private SessionStateStoreData GetItem(bool isExclusive, HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            var key = this.GetSessionIdKey(id);

            locked = false;
            lockAge = TimeSpan.Zero;
            lockId = null;
            actions = SessionStateActions.None;

            using (var client = this.GetClientAndWatch(key))
            {
                var stateRaw = client.GetAllEntriesFromHashRaw(key);

                RedisSessionState state;
                if (!RedisSessionState.TryParse(stateRaw, out state))
                {
                    client.UnWatch();
                    return null;
                }

                actions = state.Flags;
                var items = actions == SessionStateActions.InitializeItem ? new SessionStateItemCollection() : state.Items;

                if (state.Locked)
                {
                    client.UnWatch();
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

                using (var t = client.CreateTransaction())
                {
                    t.QueueCommand(c => c.SetRangeInHashRaw(key, state.ToMap()));
                    t.QueueCommand(c => c.ExpireEntryIn(key, TimeSpan.FromMinutes(state.Timeout)));
                    t.Commit();
                }

                return new SessionStateStoreData(items, this.staticObjectsGetter(context), state.Timeout);
            }
        }

        public override void ReleaseItemExclusive(HttpContext context, string id, object lockId)
        {
            var key = this.GetSessionIdKey(id);
            using (var client = this.GetClientAndWatch(key))
            {
                this.UpdateSessionStateIfLocked(client, key, (int)lockId, state =>
                {
                    state.Locked = false;
                    state.Timeout = this.sessionTimeoutMinutes;
                });
            }
        }

        public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
        {
            var key = this.GetSessionIdKey(id);
            using (var client = this.GetClientAndWatch(key))
            {
                if (newItem)
                {
                    var state = new RedisSessionState()
                    {
                        Items = (SessionStateItemCollection)item.Items,
                        Timeout = item.Timeout,
                    };

                    this.UpdateSessionState(client, key, state);
                }
                else
                {
                    this.UpdateSessionStateIfLocked(client, key, (int)lockId, state =>
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
            var stateRaw = client.GetAllEntriesFromHashRaw(key);
            RedisSessionState state;
            if (RedisSessionState.TryParse(stateRaw, out state) && state.Locked && state.LockId == (int)lockId)
            {
                stateAction(state);
                this.UpdateSessionState(client, key, state);
            }
        }

        private void UpdateSessionState(IRedisClient client, string key, RedisSessionState state)
        {
            using (var t = client.CreateTransaction())
            {
                t.QueueCommand(c => c.SetRangeInHashRaw(key, state.ToMap()));
                t.QueueCommand(c => c.ExpireEntryIn(key, TimeSpan.FromMinutes(state.Timeout)));
                t.Commit();
            }
        }

        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            return false;
        }

        public override void Dispose()
        {
            if (this.manageClientManagerLifetime)
            {
                this.clientManager.Dispose();
            }
        }

#pragma warning restore 1591
    }
}
