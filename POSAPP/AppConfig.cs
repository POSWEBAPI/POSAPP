public static class AppConfig
{
    // Set this to "L" for localhost, "G" for the hosted/global API.
    // This is the ONLY place you need to change it.
    public const string ConnectionType = "G";

    public const string LocalUrl = "https://localhost:7022/";
   // public const string GlobalUrl = "https://purplemoonapi.mythitsolutions.co.in/";
    public const string GlobalUrl = "https://eurotexapi.mythitsolutions.co.in";
    // public const string GlobalUrl = "https://Shriposapi.mythitsolutions.co.in";

    public static string BaseUrl =>
        ConnectionType.Equals("L", System.StringComparison.OrdinalIgnoreCase)
            ? LocalUrl
            : GlobalUrl;

    public static bool IsLocal =>
        ConnectionType.Equals("L", System.StringComparison.OrdinalIgnoreCase);
}