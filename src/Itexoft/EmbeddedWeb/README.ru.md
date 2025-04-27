# Встроенный веб‑сервер (EmbeddedWeb)

Исходники находятся в `src/Itexoft/EmbeddedWeb/`; точка входа MSBuild — `src/Itexoft/EmbeddedWeb/Itexoft.targets` (
импортирует `EmbeddedWebAssets.targets`). Набор задач упаковывает фронтенд‑бандлы в ресурсы и поднимает легкий
HTTP‑пайплайн без раскладки файлов на диск.

Ключевые цели:

- Упаковать любые статические веб‑активы (SPA/React/Vue/Angular/HTML) прямо в сборку .NET.
- Поднимать один или несколько самостоятельных HTTP‑хостов без копирования файлов.
- Поддерживать расширенные сценарии: кастомные префиксы ресурсов, SPA‑fallback, параллельные хосты.

Компоненты решения:

1. **Сборка** (`src/Itexoft/EmbeddedWeb/EmbeddedWebAssets.targets`) встраивает файлы бандлов как `EmbeddedResource`.
   При использовании исходников подключайте `src/Itexoft/EmbeddedWeb/Itexoft.targets`.
2. **Рантайм** (`EmbeddedWebServer` в `src/Itexoft/EmbeddedWeb`) регистрирует бандлы и обслуживает их через встроенный
   HTTP‑хост.

---

## Конфигурация сборки

Импортируйте таргет (не требуется при потреблении пакета через `buildTransitive`) и опишите бандлы `EmbeddedWebApp` в
`.csproj`:

```xml
<Import Project="src/Itexoft/EmbeddedWeb/Itexoft.targets" />

<ItemGroup>
  <EmbeddedWebApp Include="Frontend/Admin/**/*" BundleId="admin" />
  <EmbeddedWebApp Include="Frontend/Portal/**/*" BundleId="portal" />
</ItemGroup>
```

Метаданные элемента:

| Metadata        | Описание                                                  |
|-----------------|-----------------------------------------------------------|
| `BundleId`      | Идентификатор бандла (уникален в проекте).                |
| `RootDirectory` | Необязательный корень для вычисления относительных путей. |

Во время `Build`/`Publish` таргет:

1. Собирает файлы под каждый `EmbeddedWebApp`.
2. Встраивает их как `EmbeddedResource` вида `EmbeddedWeb.{BundleId}/{relativePath}`.

`buildTransitive` автоматически подхватывает таргеты в зависимых проектах; импорт выше нужен только при работе из
исходников.

---

## Использование в рантайме

### 1. Регистрация бандлов

Чаще всего достаточно просканировать свою сборку:

```csharp
EmbeddedWebServer.RegisterBundlesFromAssembly(typeof(Program).Assembly);
```

Для ручного подключения используйте префикс ресурсов:

```csharp
EmbeddedWebServer.RegisterBundle(
    "reports",
    typeof(Program).Assembly,
    "EmbeddedWeb.reports/");
```

> Префикс должен заканчиваться на `/` и соответствовать именам embedded‑ресурсов.

### 2. Отдельный HTTP‑хост

```csharp
await using var handle = await EmbeddedWebServer.StartAsync(
    bundleId: "portal",
    port: 5050,
    configureOptions: options =>
    {
        options.StaticFilesCacheDuration = TimeSpan.FromHours(1);
        options.EnableSpaFallback = true;
        options.SpaFallbackFile = "index.html";
    });

Console.WriteLine($"Portal available on http://localhost:5050");
await handle.Completion;
```

`EmbeddedWebHandle` (IPromiseDisposable) предоставляет `Urls`, `Completion`, `StopAsync`.

### 3. Интеграция в существующий ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEmbeddedWebBundles(typeof(Program).Assembly);

var app = builder.Build();
app.MapEmbeddedWebApp("/admin", "admin");
app.MapEmbeddedWebApp("/portal", "portal", opts =>
{
    opts.EnableSpaFallback = false;
    opts.DefaultFileNames.Clear();
    opts.DefaultFileNames.Add("home.html");
});

app.Run();
```

`MapEmbeddedWebApp` строит статический пайплайн на основе зарегистрированных бандлов.

### 4. Повторно используемый WebApplication

```csharp
var app = EmbeddedWebServer.CreateWebApp(
    bundleId: "app1",
    configureBuilder: builder => builder.WebHost.UseTestServer());

await app.StartAsync();
var client = app.GetTestClient();
var html = await client.GetStringAsync("/");
```

Так устроены интеграционные тесты.

---

## Несколько бандлов в одном процессе

```csharp
EmbeddedWebServer.RegisterBundlesFromAssembly(typeof(Program).Assembly);

await using var admin = await EmbeddedWebServer.StartAsync("admin", 5001);
await using var portal = await EmbeddedWebServer.StartAsync("portal", 5002);
```

Каждый хост обслуживает свой набор файлов независимо.

---

## Настройки SPA и статики

`EmbeddedWebOptions` управляет пайплайном:

| Свойство                   | Назначение                                                            |
|----------------------------|-----------------------------------------------------------------------|
| `DefaultFileNames`         | Файлы по умолчанию (стартовые).                                       |
| `EnableSpaFallback`        | Перенаправлять ли неизвестные пути на fallback (по умолчанию `true`). |
| `SpaFallbackFile`          | Конкретный fallback‑файл (по умолчанию первый из DefaultFileNames).   |
| `StaticFilesCacheDuration` | Время кеширования статики.                                            |
| `EnableDirectoryBrowsing`  | Разрешить листинг директорий (по умолчанию выключено).                |
| `OnPrepareResponse`        | Хук для кастомизации ответа.                                          |

Пример:

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

## Кастомные префиксы ресурсов

Если бандл лежит под нестандартным префиксом, передайте его явно:

```csharp
EmbeddedWebServer.RegisterBundle(
    "documentation",
    typeof(Program).Assembly,
    "MyCompany.Project.EmbeddedWeb.documentation/");
```

Префикс должен заканчиваться на `/` и совпадать с именами ресурсов.

---

## Тесты

Проект `src/Itexoft.Tests` покрывает e2e‑сценарии:

- Обслуживание бандлов через `CreateWebApp`.
- SPA‑fallback.
- Запуск отдельных хостов с embedded‑бандлами.
- Проверка не‑seekable потоков и параллельных хостов.

Команда:

```bash
dotnet test src/Itexoft.Tests/Itexoft.Tests.csproj
```

---

## Ограничения и заметки

- Бандлы загружаются в память; очень крупные бандлы могут потребовать иной подход к стримингу.
- Обновление фронтенда требует пересборки приложения.
- Число параллельных хостов ограничено ресурсами процесса.
- MSBuild‑таск встраивает файлы напрямую; имена ресурсов должны начинаться с `EmbeddedWeb.{BundleId}/`.
