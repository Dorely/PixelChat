using ElectronNET.API;
using ElectronNET.API.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PixelChat.Art;
using PixelChat.Auth;
using PixelChat.Chat;
using PixelChat.Components;
using PixelChat.Llm;
using PixelChat.Persistence;
using PixelChat.Persistence.Repositories;
using PixelChat.Secrets;
using PixelChat.Tokens;

var builder = WebApplication.CreateBuilder(args);
var isElectronMode = IsElectronMode(args);
var desktopUrl = isElectronMode ? GetDesktopUrl(builder.Configuration) : null;
var maxInteractiveServerMessageSize = builder.Configuration.GetValue<long?>("Blazor:MaximumReceiveMessageSizeBytes")
    ?? 64L * 1024 * 1024;

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = maxInteractiveServerMessageSize);

builder.Services.AddHttpClient();

if (isElectronMode)
{
    builder.Services.AddElectron();
    builder.UseElectron(args, () => ElectronAppReady(desktopUrl!));
    builder.WebHost.UseUrls(desktopUrl!);
}

builder.Services.AddPixelChatPersistence(builder.Configuration);
builder.Services.AddScoped<ILlmProviderRepository, LlmProviderRepository>();
builder.Services.AddScoped<IOAuthTokenRepository, OAuthTokenRepository>();
builder.Services.AddScoped<IAssistantConversationRepository, AssistantConversationRepository>();
builder.Services.AddScoped<ISecretStore, SqliteSecretStore>();
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.Configure<ImageGenerationOptions>(builder.Configuration.GetSection(ImageGenerationOptions.SectionName));
builder.Services.Configure<SpriteAnimationOptions>(builder.Configuration.GetSection(SpriteAnimationOptions.SectionName));
builder.Services.Configure<BackgroundRemovalOptions>(builder.Configuration.GetSection(BackgroundRemovalOptions.SectionName));
builder.Services.Configure<TokenCountingOptions>(builder.Configuration.GetSection(TokenCountingOptions.SectionName));
builder.Services.AddScoped<ILlmProviderService, LlmProviderService>();
builder.Services.AddScoped<IOpenAIAccountAuthService, OpenAIAccountAuthService>();
builder.Services.AddScoped<IChatClientFactory, ChatClientFactory>();
builder.Services.AddScoped<IImageProvider, OpenAIAccountImageProvider>();
builder.Services.AddScoped<IArtWorkflowService, ArtWorkflowService>();
builder.Services.AddScoped<IAssetAnimationWorkflowService, AssetAnimationWorkflowService>();
builder.Services.AddSingleton<TiktokenTokenCounter>();
builder.Services.AddSingleton<CharEstimateTokenCounter>();
builder.Services.AddSingleton<ITokenCounter, CompositeTokenCounter>();
builder.Services.AddSingleton<IImageTokenEstimator, OpenAIImageTokenEstimator>();
builder.Services.AddSingleton<IChatTokenEstimator, ChatTokenEstimator>();
builder.Services.AddSingleton<IImageGenerationRuntime, ImageGenerationRuntime>();
builder.Services.AddSingleton<IBackgroundRemovalService, RembgBackgroundRemovalService>();
builder.Services.AddSingleton<IWorkspaceVisibleStateStore, WorkspaceVisibleStateStore>();
builder.Services.AddScoped<AssistantToolRegistry>();
builder.Services.AddScoped<IAssistantChatService, AssistantChatService>();
builder.Services.AddSingleton<IWorkspaceChatRuntime, WorkspaceChatRuntime>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseMigrationBootstrapper.MigrateAsync(db);
    if (db.Database.GetDbConnection() is SqliteConnection sqliteConnection)
    {
        await sqliteConnection.OpenAsync();
        SqliteConnectionSettings.ConfigureDatabase(sqliteConnection);
        await sqliteConnection.CloseAsync();
    }
}

await app.Services.GetRequiredService<IImageGenerationRuntime>().ReconcileInterruptedBatchesAsync();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    if (!isElectronMode)
        app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<PixelChat.Components.App>()
    .AddInteractiveServerRenderMode();

app.MapOpenAIAccountOAuth();
app.MapPixelChatArtMedia();

app.Run();

static async Task ElectronAppReady(string desktopUrl)
{
    var options = new BrowserWindowOptions
    {
        Title = "PixelChat",
        Show = false,
        Width = 1440,
        Height = 960,
        MinWidth = 1024,
        MinHeight = 700,
        Center = true,
        IsRunningBlazor = true
    };

    if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        options.AutoHideMenuBar = true;

    var browserWindow = await Electron.WindowManager.CreateWindowAsync(options, desktopUrl);
    browserWindow.OnReadyToShow += () => browserWindow.Show();
}

static bool IsElectronMode(string[] args) =>
    args.Any(IsElectronArgument);

static bool IsElectronArgument(string arg)
{
    var normalized = arg.TrimStart('-', '/');
    return normalized.Equals("electron", StringComparison.OrdinalIgnoreCase)
        || normalized.StartsWith("electronPort=", StringComparison.OrdinalIgnoreCase)
        || normalized.StartsWith("electronPID=", StringComparison.OrdinalIgnoreCase)
        || normalized.StartsWith("electronAuthToken=", StringComparison.OrdinalIgnoreCase);
}

static string GetDesktopUrl(IConfiguration configuration)
{
    var bindHost = configuration["Desktop:BindHost"];
    if (string.IsNullOrWhiteSpace(bindHost))
        throw new InvalidOperationException("Desktop:BindHost must be configured to run the desktop shell.");

    if (bindHost.Contains("://", StringComparison.Ordinal))
        throw new InvalidOperationException("Desktop:BindHost must be a host name only, without a URL scheme.");

    var httpPort = configuration.GetValue<int?>("Desktop:HttpPort")
        ?? throw new InvalidOperationException("Desktop:HttpPort must be configured to run the desktop shell.");
    if (httpPort is <= 0 or > 65535)
        throw new InvalidOperationException("Desktop:HttpPort must be between 1 and 65535.");

    return $"http://{bindHost.Trim()}:{httpPort}";
}
