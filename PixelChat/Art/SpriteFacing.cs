using System.Text;

namespace PixelChat.Art;

internal static class SpriteFacing
{
    public const string Center = "center";
    public const string Front = "front";
    public const string Back = "back";
    public const string SideRight = "side_right";
    public const string SideLeft = "side_left";
    public const string ThreeQuarterFrontRight = "three_quarter_front_right";
    public const string ThreeQuarterFrontLeft = "three_quarter_front_left";
    public const string ThreeQuarterBackRight = "three_quarter_back_right";
    public const string ThreeQuarterBackLeft = "three_quarter_back_left";

    public static string Normalize(string? value, string fallback = SideRight)
    {
        var token = Clean(value);
        if (TryNormalizeClean(token, out var normalized))
            return normalized;

        var fallbackToken = Clean(fallback);
        return TryNormalizeClean(fallbackToken, out normalized) ? normalized : SideRight;
    }

    public static double ToYawDegrees(string? facing) =>
        Normalize(facing) switch
        {
            Front or Center => 0d,
            Back => 180d,
            SideRight => 90d,
            SideLeft => -90d,
            ThreeQuarterFrontRight => 45d,
            ThreeQuarterFrontLeft => -45d,
            ThreeQuarterBackRight => 135d,
            ThreeQuarterBackLeft => -135d,
            _ => 90d,
        };

    public static bool IsLeftFacing(string? facing)
    {
        var normalized = Normalize(facing);
        return normalized is SideLeft or ThreeQuarterFrontLeft or ThreeQuarterBackLeft;
    }

    public static string ToPromptPhrase(string? facing) =>
        Normalize(facing) switch
        {
            Front or Center => "front-facing toward the viewer",
            Back => "back-facing away from the viewer",
            SideRight => "side view facing the viewer's right edge",
            SideLeft => "side view facing the viewer's left edge",
            ThreeQuarterFrontRight => "three-quarter front-right view, facing down-right / southeast in viewer space",
            ThreeQuarterFrontLeft => "three-quarter front-left view, facing down-left / southwest in viewer space",
            ThreeQuarterBackRight => "three-quarter back-right view, facing up-right / northeast in viewer space",
            ThreeQuarterBackLeft => "three-quarter back-left view, facing up-left / northwest in viewer space",
            _ => "side view facing the viewer's right edge",
        };

    private static bool TryNormalizeClean(string token, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (ContainsAny(token, "south_west", "southwest", "down_left", "downleft"))
        {
            normalized = ThreeQuarterFrontLeft;
            return true;
        }

        if (ContainsAny(token, "south_east", "southeast", "down_right", "downright"))
        {
            normalized = ThreeQuarterFrontRight;
            return true;
        }

        if (ContainsAny(token, "north_west", "northwest", "up_left", "upleft"))
        {
            normalized = ThreeQuarterBackLeft;
            return true;
        }

        if (ContainsAny(token, "north_east", "northeast", "up_right", "upright"))
        {
            normalized = ThreeQuarterBackRight;
            return true;
        }

        var hasThreeQuarter = token.Contains("three_quarter", StringComparison.Ordinal)
            || token.Contains("threequarter", StringComparison.Ordinal)
            || token.Contains("isometric", StringComparison.Ordinal)
            || token.Contains("diagonal", StringComparison.Ordinal);
        var hasFront = HasToken(token, "front") || HasToken(token, "viewer") || token.Contains("toward_viewer", StringComparison.Ordinal);
        var hasBack = HasToken(token, "back") || token.Contains("away_from_viewer", StringComparison.Ordinal);
        var hasLeft = HasToken(token, "left") || token.Contains("left_facing", StringComparison.Ordinal);
        var hasRight = HasToken(token, "right") || token.Contains("right_facing", StringComparison.Ordinal);

        if (hasThreeQuarter)
        {
            if (hasBack && hasLeft)
                normalized = ThreeQuarterBackLeft;
            else if (hasBack && hasRight)
                normalized = ThreeQuarterBackRight;
            else if (hasLeft)
                normalized = ThreeQuarterFrontLeft;
            else if (hasRight)
                normalized = ThreeQuarterFrontRight;
            else if (hasBack)
                normalized = Back;
            else
                normalized = ThreeQuarterFrontRight;

            return true;
        }

        if (ContainsAny(token, "front_left", "left_front"))
        {
            normalized = ThreeQuarterFrontLeft;
            return true;
        }

        if (ContainsAny(token, "front_right", "right_front"))
        {
            normalized = ThreeQuarterFrontRight;
            return true;
        }

        if (ContainsAny(token, "back_left", "left_back"))
        {
            normalized = ThreeQuarterBackLeft;
            return true;
        }

        if (ContainsAny(token, "back_right", "right_back"))
        {
            normalized = ThreeQuarterBackRight;
            return true;
        }

        if (token is "center" or "centre" or "none" or "fixed_center")
        {
            normalized = Center;
            return true;
        }

        if (token is "front" or "forward")
        {
            normalized = Front;
            return true;
        }

        if (token is "back" or "rear")
        {
            normalized = Back;
            return true;
        }

        if (ContainsAny(token, "side_left", "profile_left", "left_profile") || token is "left")
        {
            normalized = SideLeft;
            return true;
        }

        if (ContainsAny(token, "side_right", "profile_right", "right_profile") || token is "right")
        {
            normalized = SideRight;
            return true;
        }

        if (hasLeft && !hasRight)
        {
            normalized = SideLeft;
            return true;
        }

        if (hasRight && !hasLeft)
        {
            normalized = SideRight;
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string token, params string[] values) =>
        values.Any(value => token.Contains(value, StringComparison.Ordinal));

    private static bool HasToken(string value, string token)
    {
        if (value == token)
            return true;
        return value.StartsWith(token + "_", StringComparison.Ordinal)
            || value.EndsWith("_" + token, StringComparison.Ordinal)
            || value.Contains("_" + token + "_", StringComparison.Ordinal);
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = value.Trim().ToLowerInvariant()
            .Replace("3/4", "three_quarter", StringComparison.Ordinal)
            .Replace("three-quarter", "three_quarter", StringComparison.Ordinal)
            .Replace("three quarter", "three_quarter", StringComparison.Ordinal);
        var builder = new StringBuilder(text.Length);
        var previousUnderscore = false;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                previousUnderscore = false;
            }
            else if (!previousUnderscore)
            {
                builder.Append('_');
                previousUnderscore = true;
            }
        }

        return builder.ToString().Trim('_');
    }
}
