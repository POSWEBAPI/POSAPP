
// ── Add / update your UserInfo class ──────────────────────
using System.Text.Json.Serialization;

public class UserInfo
{
    [JsonPropertyName("pKuserID")] public int PKUserID { get; set; }
    [JsonPropertyName("userID")] public int UserID { get; set; }
    [JsonPropertyName("password")] public string Password { get; set; }
    [JsonPropertyName("email")] public string Email { get; set; }
    [JsonPropertyName("mobile")] public string Mobile { get; set; }
    [JsonPropertyName("roleID")] public int RoleID { get; set; }
    [JsonPropertyName("storeID")] public int StoreID { get; set; }
    [JsonPropertyName("companyID")] public int CompanyID { get; set; }
    [JsonPropertyName("createdBy")] public int CreatedBy { get; set; }
    [JsonPropertyName("createdDate")] public DateTime CreatedDate { get; set; }
}