# Embedded Web Server Toolkit

Sources live under `src/Itexoft/EmbeddedWeb/`. This toolkit serves front-end bundles from embedded resources through
lightweight HTTP endpoints.

Key goals:

- Package any static web assets (React/Vue/Angular/SPAs, plain HTML, etc.) directly into a .NET assembly.
- Spin up one or more self-contained HTTP pipelines at runtime without copying files to disk.
- Support advanced delivery scenarios such as custom resource prefixes, SPA fallbacks, and multi-host setups.

The feature is split into:

1. **Build-time resource layout** that embeds static files as `EmbeddedResource` items with names matching
   `EmbeddedWeb.{BundleId}/{relativePath}`.
2. **Runtime API** (`EmbeddedWebServer` in `src/Itexoft/EmbeddedWeb`) that exposes simple entry points to register
   bundles and serve them through the embedded HTTP host.

---

## Build-Time Configuration

Embed the static files directly in the consuming project (`*.csproj`) and assign the canonical resource names:

```xml
<ItemGroup>
  <EmbeddedResource Include="Frontend/Admin/**/*">
    <LogicalName>EmbeddedWeb.admin/%(RecursiveDir)%(Filename)%(Extension)</LogicalName>
  </EmbeddedResource>
  <EmbeddedResource Include="Frontend/Portal/**/*">
    <LogicalName>EmbeddedWeb.portal/%(RecursiveDir)%(Filename)%(Extension)</LogicalName>
  </EmbeddedResource>
</ItemGroup>
```

During `Build`/`Publish`, MSBuild embeds those files into the assembly exactly as declared. `EmbeddedWebServer`
recognizes bundle members by the `EmbeddedWeb.{BundleId}/{relativePath}` prefix, so the `LogicalName` must match that
contract.

---

## Runtime Usage

### 1. Register Embedded Bundles

Most apps only need to scan their own assembly:

```csharp
EmbeddedWebServer.RegisterBundlesFromAssembly(typeof(Program).Assembly);
```

If you need to wire a bundle manually, register by resource prefix:

```csharp
EmbeddedWebServer.RegisterBundle(
    "reports",
    typeof(Program).Assembly,
    "EmbeddedWeb.reports/");
```

> The resource prefix must end with `/` and match the embedded resource names.

### 2. Start a Standalone HTTP Host

Launch a dedicated embedded HTTP host that exposes one bundle:

```csharp
await using var handle = await EmbeddedWebServer.StartAsync(
    bundleId: "portal",
    port: 5050,
    configureOptions: options =>
    {
        options.StaticFilesCacheDuration = TimeSpan.FromHours(1);
        options.EnableSpaFallback = true; // default: true
        options.SpaFallbackFile = "index.html";
    });

Console.WriteLine($"Portal available on http://localhost:5050");
await handle.Completion; // awaits shutdown
```

The returned `EmbeddedWebHandle` implements `IPromiseDisposable` and exposes:

- `Urls` (bound addresses),
- `Completion` (shutdown task),
- `StopAsync` for graceful termination.

### 3. Integrate with Existing ASP.NET Core Apps

Map embedded bundles onto an existing WebApplication:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEmbeddedWebBundles(typeof(Program).Assembly);

var app = builder.Build();
app.MapEmbeddedWebApp("/admin", "admin");               // Serves SPA under /admin
app.MapEmbeddedWebApp("/portal", "portal", opts =>
{
    opts.EnableSpaFallback = false;                     // Static-only
    opts.DefaultFileNames.Clear();
    opts.DefaultFileNames.Add("home.html");
});

app.Run();
```

`MapEmbeddedWebApp` internally creates a static file pipeline with the registered bundle.

### 4. Construct a Reusable WebApplication

When integrating with tests or generic hosts, you can build the pipeline without binding ports:

```csharp
var app = EmbeddedWebServer.CreateWebApp(
    bundleId: "app1",
    configureBuilder: builder => builder.WebHost.UseTestServer());

await app.StartAsync();
var client = app.GetTestClient();
var html = await client.GetStringAsync("/");
```

This is exactly how the integration tests are structured.

---

## Multiple Bundles in One Process

You can register several bundles and spin up independent hosts:

```csharp
EmbeddedWebServer.RegisterBundlesFromAssembly(typeof(Program).Assembly);

await using var admin = await EmbeddedWebServer.StartAsync("admin", 5001);
await using var portal = await EmbeddedWebServer.StartAsync("portal", 5002);
```

Each host maintains its own static file provider, so content is served independently.

---

## SPA Fallback and Static File Options

`EmbeddedWebOptions` governs the static pipeline:

| Property                   | Purpose                                                                   |
|----------------------------|---------------------------------------------------------------------------|
| `DefaultFileNames`         | Files checked by the default files middleware. (defaults to `index.html`) |
| `EnableSpaFallback`        | Whether to rewrite unknown paths to a fallback file (defaults to `true`). |
| `SpaFallbackFile`          | Specific file served for SPA routes (defaults to first default file).     |
| `StaticFilesCacheDuration` | Applies a `Cache-Control` header for static responses.                    |
| `EnableDirectoryBrowsing`  | Exposes directory listings (disabled by default).                         |
| `OnPrepareResponse`        | Hook to customize individual responses.                                   |

Example:

```csharp
EmbeddedWebServer.MapEmbeddedWebApp("/docs", "documentation", options =>
{
    options.EnableSpaFallback = false;
    options.StaticFilesCacheDuration = TimeSpan.FromDays(7);
    options.OnPrepareResponse = ctx =>
        ctx.Context.Response.Headers["X-Docs-Version"] = "2025.10";
});
```

---

## Custom Resource Prefixes

If you need to keep bundles under a custom embedded resource prefix, pass it explicitly:

```csharp
EmbeddedWebServer.RegisterBundle(
    "documentation",
    typeof(Program).Assembly,
    "MyCompany.Project.EmbeddedWeb.documentation/");
```

The prefix must end with `/` and should match the resource names exactly.

---

## Tests

The `src/Itexoft.Tests` project demonstrates end-to-end scenarios:

- Serving bundled assets via `CreateWebApp`.
- SPA fallback behavior.
- Launching standalone hosts with embedded bundles.
- Verifying non-seekable streams and multiple independent hosts.

Run:

```bash
dotnet test src/Itexoft.Tests/Itexoft.Tests.csproj
```

---

## Limitations & Notes

- Bundles are loaded into memory; extremely large bundles may require alternative streaming strategies.
- Updating front-end assets requires rebuilding the application.
- Parallel host counts are bounded by process resources.
- The MSBuild task embeds files directly; resource names must start with `EmbeddedWeb.{BundleId}/`.
