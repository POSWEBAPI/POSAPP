//using Microsoft.Win32;
//using System.Net.NetworkInformation;
//using System.Security.Cryptography;
//using System.Text;

//namespace POSAPP.License;

//public static class LicenseGuard
//{
//    private const string RegistryKeyPath = @"SOFTWARE\YourCompany\POS";
//    private const string RegValueMAC = "RegisteredMAC";
//    private const string RegValueKey = "LicenseKey";

//    public static bool Validate(out string failReason)
//    {
//        failReason = string.Empty;

//        // 1. Read registry values written at install time
//        string? registeredMac = ReadRegistry(RegValueMAC);
//        string? licenseKey = ReadRegistry(RegValueKey);

//        if (string.IsNullOrWhiteSpace(registeredMac) ||
//            string.IsNullOrWhiteSpace(licenseKey))
//        {
//            failReason = "License registration not found.\n\n" +
//                         "Please reinstall the application or contact your administrator.";
//            return false;
//        }

//        // 2. Get this machine's real MAC addresses
//        string[] currentMacs = GetPhysicalMacs();

//        if (currentMacs.Length == 0)
//        {
//            failReason = "Unable to read network hardware on this machine.\n\n" +
//                         "Ensure a network adapter is present and try again.";
//            return false;
//        }

//        // 3. Compare MACs
//        string normalizedRegistered = NormalizeMac(registeredMac);

//        bool matched = currentMacs
//            .Select(NormalizeMac)
//            .Any(m => m.Equals(normalizedRegistered, StringComparison.OrdinalIgnoreCase));

//        if (!matched)
//        {
//            failReason = "This software is licensed to a different machine.\n\n" +
//                         "Copying the application folder does not transfer the license.\n\n" +
//                         "Please contact your administrator to reactivate.";
//            return false;
//        }

//        // 4. Verify HMAC integrity — detects registry tampering
//        if (!VerifyIntegrityHash(licenseKey, normalizedRegistered))
//        {
//            failReason = "License data has been tampered with.\n\n" +
//                         "Please reinstall the application.";
//            return false;
//        }

//        return true;
//    }

//    private static string[] GetPhysicalMacs()
//    {
//        return NetworkInterface
//            .GetAllNetworkInterfaces()
//            .Where(n =>
//                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
//                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
//                n.OperationalStatus == OperationalStatus.Up &&
//                n.GetPhysicalAddress().ToString().Length == 12)
//            .Select(n => n.GetPhysicalAddress().ToString())
//            .Distinct()
//            .ToArray();
//    }

//    private static string NormalizeMac(string mac)
//        => mac.Replace("-", "").Replace(":", "").ToUpperInvariant().Trim();

//    private static string? ReadRegistry(string valueName)
//    {
//        try
//        {
//            // 🔹 Try LocalMachine (production)
//            using var keyLM64 = RegistryKey.OpenBaseKey(
//                RegistryHive.LocalMachine,
//                RegistryView.Registry64)
//                .OpenSubKey(RegistryKeyPath);

//            var value = keyLM64?.GetValue(valueName) as string;

//            if (!string.IsNullOrWhiteSpace(value))
//                return value;

//            // 🔹 Try LocalMachine 32-bit
//            using var keyLM32 = RegistryKey.OpenBaseKey(
//                RegistryHive.LocalMachine,
//                RegistryView.Registry32)
//                .OpenSubKey(RegistryKeyPath);

//            value = keyLM32?.GetValue(valueName) as string;

//            if (!string.IsNullOrWhiteSpace(value))
//                return value;

//            // 🔹 Fallback → CurrentUser (DEV FIX)
//            using var keyCU = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);

//            return keyCU?.GetValue(valueName) as string;
//        }
//        catch
//        {
//            return null;
//        }
//    }

//    private static bool VerifyIntegrityHash(string licenseKey, string normalizedMac)
//    {
//        try
//        {
//            string? storedHash = ReadRegistry("IntegrityHash");
//            if (string.IsNullOrWhiteSpace(storedHash)) return false;

//            string expected = ComputeHmac(licenseKey, normalizedMac);
//            return storedHash.Equals(expected, StringComparison.OrdinalIgnoreCase);
//        }
//        catch { return false; }
//    }

//    internal static string ComputeHmac(string licenseKey, string normalizedMac)
//    {
//        // !! Change this to your own secret — must match the .iss file exactly !!
//        const string SECRET_SALT = "POS-7f3a91bc-2e84-4d60-ae12-YOUR-UNIQUE-SALT";

//        string payload = $"{licenseKey}|{normalizedMac}";
//        byte[] keyBytes = Encoding.UTF8.GetBytes(SECRET_SALT);
//        byte[] msgBytes = Encoding.UTF8.GetBytes(payload);
//        byte[] hash = HMACSHA256.HashData(keyBytes, msgBytes);
//        return Convert.ToHexString(hash);
//    }
//}