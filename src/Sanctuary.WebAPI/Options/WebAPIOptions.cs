namespace Sanctuary.WebAPI.Options;

public class WebAPIOptions
{
    public const string Section = "WebAPI";

    public string? LaunchArguments { get; set; }

    public bool? MemberByDefault { get; set; }

    public string? WebsiteRoot { get; set; }

    public string? ClientFolder { get; set; }

    public string? PortraitPlateFolder { get; set; }
}