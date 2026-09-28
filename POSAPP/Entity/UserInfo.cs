using System.Text.Json.Serialization;

namespace POSAPP
{
    // Matches this API response shape exactly:
    // {"pkUserId":1,"userId":10002,"password":"...","email":"user1@gmail.com",
    //  "mobile":"9876543211","roleId":3,"storeId":1012,"companyId":11,"isSuperAdmin":false}
    public class UserInfo
    {
        [JsonPropertyName("pkUserId")] public int PkUserId { get; set; }
        [JsonPropertyName("userId")] public int UserID { get; set; }
        [JsonPropertyName("password")] public string Password { get; set; }
        [JsonPropertyName("email")] public string Email { get; set; }
        [JsonPropertyName("mobile")] public string Mobile { get; set; }

        // Kept as *ID (uppercase) to match existing usage in login.cs
        // (user.CompanyID, user.StoreID, user.RoleID). JsonPropertyName
        // maps these explicitly to the camelCase JSON keys regardless of
        // PropertyNameCaseInsensitive, so this works either way.
        [JsonPropertyName("roleId")] public int RoleID { get; set; }
        [JsonPropertyName("storeId")] public int StoreID { get; set; }
        [JsonPropertyName("companyId")] public int CompanyID { get; set; }

        [JsonPropertyName("isSuperAdmin")] public bool IsSuperAdmin { get; set; }
    }
}