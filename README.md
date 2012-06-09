Harbour.RedisSessionStateStore
==============================

This is a [Redis](http://redis.io/) based [SessionStateStoreProvider](http://msdn.microsoft.com/en-us/library/ms178587.aspx)
written in C# using [ServiceStack.Redis](https://github.com/ServiceStack/ServiceStack.Redis).

Installation
------------

1. You can either install using NuGet: `PM> Install-Package Harbour.RedisSessionStateStore`
2. Or install from source: `msbuild .\build\build.proj`

Usage
-----

1. Configure your `web.config` to use the session state provider:

    ```xml
    ...
    <system.web>
      <sessionState mode="Custom" customProvider="RedisSessionStateProvider">
        <providers>
          <clear />
          <add name="RedisSessionStateProvider" 
               type="Harbour.RedisSessionStateStore.RedisSessionStateStoreProvider" 
               host="localhost:6379" clientType="pooled" />
        </providers>
      </sessionState>
    </system.web>
    ...
    ```
This configuration will use a `PooledRedisClientManager` and use the default host
and port (localhost:6379). Alternatively you can use the `host` attribute 
to set a custom host/port. If you wish to change the client manager type to
`BasicRedisClientManager`, you can set the `clientType="basic"`.

If you require that a custom `IClientsManager` be configured (for example if you're
using an IoC container or you wish to only have one `IClientsManager` for your
whole application), you can do the following when the application starts:

    ```csharp
    private IRedisClientsManager clientManager;
      
    protected void Application_Start()
    {
        // Or use your IoC container to wire this up.
        this.clientManager = new PooledRedisClientManager("localhost:6379");
        RedisSessionStateStoreProvider.SetClientManager(this.clientManager);
    }
        
    protected void Application_End()
    {
        this.clientManager.Dispose();
    }
    ```
