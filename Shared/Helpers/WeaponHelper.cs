namespace Finestats.Helpers;

public static class WeaponHelper
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase)) value = value[7..];
        return Limit(value, 64)?.ToLowerInvariant();
    }

    public static string? Limit(string? value, int maxLength)
        => string.IsNullOrEmpty(value) ? null : value.Length <= maxLength ? value : value[..maxLength];
}
