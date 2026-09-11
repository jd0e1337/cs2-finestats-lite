using System.Globalization;
using System.Text.RegularExpressions;
using Finestats.Services;

namespace Finestats.Config;

public sealed record ConnectionMessages
{
    public string Message { get; init; } = "Player [green]{name}[/] connected from [yellow]{country}[/] - {rank}";
    public string Ranked { get; init; } = "Rank [gold]#{rank}[/]";
    public string Unranked { get; init; } = "[silver]Unranked[/]";
    public string UnknownCountry { get; init; } = "Unknown country";

    private static readonly Regex Tokens = new(@"\{([a-z_]+)\}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public void Validate()
    {
        foreach (var(text, allowed)in new[]
        {
            (Message, new[] { "name", "country", "country_code", "rank" }),
            (Ranked, new[] { "rank" }),
            (Unranked, Array.Empty<string>()),
            (UnknownCountry, Array.Empty<string>())
        }

        )
        {
            if (text is null || text.Length > 1024 || text.Any(char.IsControl))
                throw new InvalidDataException("Connection messages must be single-line text up to 1024 characters.");
            if (Tokens.Matches(text).Any(m => !allowed.Contains(m.Groups[1].Value)) || Tokens.Replace(text, "").IndexOfAny(['{', '}']) >= 0)
                throw new InvalidDataException("Invalid connection message placeholder.");
        }
    }

    public string Format(string name, string? countryCode, long? rank, StatsConfig config)
    {
        string country = UnknownCountry;
        string code = countryCode is { Length: 2 } && countryCode.All(c => c is >= 'A' and <= 'Z') ? countryCode : "";
        if (code.Length > 0)
        {
            country = code;
            if (config.ConnectCountryNames)
            {
                try
                {
                    country = new RegionInfo(code).EnglishName + " (" + code + ")";
                }
                catch (ArgumentException)
                {
                }
            }
        }

        var values = new Dictionary<string, string>
        {
            ["name"] = StatsCommandClient.Safe(name, config.DisplayNameMaxLength),
            ["country"] = country,
            ["country_code"] = code.Length > 0 ? code : UnknownCountry,
            ["rank"] = rank is> 0 ? Tokens.Replace(Ranked, m => rank.Value.ToString(CultureInfo.InvariantCulture)) : Unranked
        };
        return Tokens.Replace(Message, m => values[m.Groups[1].Value]);
    }
}
