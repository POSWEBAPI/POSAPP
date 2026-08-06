// =============================================================
//  AppSettings.cs
//  Add this file to your project: POSAPP/AppSettings.cs
//  This is the typed model that maps to appsettings.json
// =============================================================

using System.Windows.Forms.Design;

public class AppSettings
{
    // "normal" or "d365"
    public string ApiMode { get; set; } = "normal";

    // Set to true if D365 version skips licence check
    public bool SkipLicenseInD365Mode { get; set; } = false;

    public NormalApiSettings? NormalApi { get; set; }
    public D365Settings? D365 { get; set; }
}

public class NormalApiSettings
{
    public string BaseUrl { get; set; } = "https://localhost:7022";
}

public class D365Settings
{
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string D365BaseUrl { get; set; } = "";
    public string ODataVersion { get; set; } = "v9.2";
}


// =============================================================
//  ApiServiceFactory.cs
//  Add this file to your project: POSAPP/ApiServiceFactory.cs
//
//  THIS IS THE SWITCH.
//  Reads ApiMode from appsettings.json and returns the correct service.
//
//  "normal" → NormalApiService  (your own REST API)
//  "d365"   → D365ApiService    (Dynamics 365 OData)
// =============================================================

//public static class ApiServiceFactory
//{
//    public static IApiService Create(AppSettings settings)
//    {
//        // ↓↓↓ THIS IS THE SWITCH YOU ASKED ABOUT ↓↓↓
//        return settings.ApiMode.ToLowerInvariant() switch
//        {
//            "d365" => CreateD365Service(settings),    // D365 exe goes here
//            "normal" => CreateNormalService(settings),  // API exe goes here
//            _ => CreateNormalService(settings)   // default fallback
//        };
//    }

//    private static IUIService CreateNormalService(AppSettings s)
//    {
//        string url = s.NormalApi?.BaseUrl
//            ?? throw new InvalidOperationException(
//                "appsettings.json is missing NormalApi.BaseUrl");

//        return new NormalApiService(url);
//    }

//    private static IApiService CreateD365Service(AppSettings s)
//    {
//        var cfg = s.D365
//            ?? throw new InvalidOperationException(
//                "appsettings.json is missing the D365 section.");

//        if (string.IsNullOrWhiteSpace(cfg.TenantId) ||
//            string.IsNullOrWhiteSpace(cfg.ClientId) ||
//            string.IsNullOrWhiteSpace(cfg.ClientSecret) ||
//            string.IsNullOrWhiteSpace(cfg.D365BaseUrl))
//            throw new InvalidOperationException(
//                "D365 config is incomplete in appsettings.json.\n" +
//                "Please fill in TenantId, ClientId, ClientSecret, D365BaseUrl.");

//        return new D365ApiService(cfg);
//    }
//}