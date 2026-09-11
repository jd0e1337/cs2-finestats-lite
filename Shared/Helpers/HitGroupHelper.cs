namespace Finestats.Helpers;

public static class HitGroupHelper
{
    // Verified against SwiftlyS2 1.4.10 HitGroup_t; UNUSED, SPECIAL, COUNT and INVALID are unknown.
    public static string Normalize(long hitgroup) => hitgroup switch
    {
        0 => "generic", 1 => "head", 2 => "chest", 3 => "stomach", 4 => "left_arm",
        5 => "right_arm", 6 => "left_leg", 7 => "right_leg", 8 => "neck", 10 => "gear",
        _ => "unknown"
    };
}
