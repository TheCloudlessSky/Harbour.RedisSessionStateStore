using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using ServiceStack.Redis;

namespace Harbour.RedisSessionStateStore.SampleWeb
{
    // Note: For instructions on enabling IIS6 or IIS7 classic mode, 
    // visit http://go.microsoft.com/?LinkId=9394801

    public class MvcApplication : System.Web.HttpApplication
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
        }

        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapRoute(
                "Default", // Route name
                "{controller}/{action}/{id}", // URL with parameters
                new { controller = "Home", action = "Index", id = UrlParameter.Optional } // Parameter defaults
            );

        }

        private IRedisClientsManager clientManager;

        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();

            RegisterGlobalFilters(GlobalFilters.Filters);
            RegisterRoutes(RouteTable.Routes);

            this.clientManager = new PooledRedisClientManager("localhost:6379");
            RedisSessionStateStoreProvider.SetClientManager(this.clientManager);
            RedisSessionStateStoreProvider.SetOptions(new RedisSessionStateStoreOptions()
            {
                KeySeparator = ":",
                OnDistributedLockNotAcquired = sessionId =>
                {
                    Console.WriteLine("Session \"{0}\" could not establish distributed lock. " +
                                      "This most likely means you have to increase the " +
                                      "DistributedLockAcquireSeconds/DistributedLockTimeoutSeconds.", sessionId);
                }
            });
        }

        protected void Application_End()
        {
            this.clientManager.Dispose();
        }
    }
}