using System.Globalization;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace Finestats.Helpers;

public sealed record ObservedPlayer(int Slot, ulong NativeSessionId, string? Steamid,
    string? Name, int? Team, bool IsBot, bool Authenticated);

public static class PlayerHelper
{
    public static ObservedPlayer? Observe(IPlayer? player)
    {
        if (player is null) return null;
        try
        {
            // IsValid requires a pawn, so it is too strict for connecting/spectating players.
            var controller = player.Controller;
            bool bot = player.IsFakeClient;
            bool authorized = !bot && player.IsAuthorized;
            ulong steamid = authorized ? player.SteamID : 0;
            return new(player.PlayerID, player.SessionId,
                steamid > 0 ? steamid.ToString(CultureInfo.InvariantCulture) : null,
                WeaponHelper.Limit(player.Name, 128),
                controller.IsValid ? controller.TeamNum : null, bot, authorized && steamid > 0);
        }
        catch (ObjectDisposedException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public static bool? IsBlind(IPlayer? player, ISwiftlyCore core)
    {
        try
        {
            var pawn = player?.PlayerPawn;
            if (pawn is not { IsValid: true }) return null;
            float until = pawn.BlindUntilTime.Value, now = core.Engine.GlobalVars.CurrentTime;
            return float.IsFinite(until) && float.IsFinite(now) ? until > now : null;
        }
        catch (ObjectDisposedException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}
