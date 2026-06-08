namespace PixelChat.Art;

public sealed class BackgroundRemovalOptions
{
    public const string SectionName = "BackgroundRemoval";

    public bool Enabled { get; set; } = true;
    public string DefaultMethod { get; set; } = "auto";
    public string SidecarRoot { get; set; } = string.Empty;
    public string UvVersion { get; set; } = "0.11.16";
    public string UvReleaseBaseUrl { get; set; } = "https://releases.astral.sh/github/uv/releases";
    public string UvArchiveSha256 { get; set; } = string.Empty;
    public string PythonVersion { get; set; } = "3.12";
    public string RembgPackageVersion { get; set; } = "2.0.76";
    public string ModelName { get; set; } = "birefnet-massive";
    public string Acceleration { get; set; } = "gpu";
    public bool AllowCpuFallback { get; set; } = true;
    public string[] AllowedModels { get; set; } =
    [
        "birefnet-massive",
        "birefnet-general",
        "birefnet-dis",
        "birefnet-hrsod",
        "isnet-general-use",
        "isnet-anime",
    ];
    public bool AlphaMatting { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 180;
}
