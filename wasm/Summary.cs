using System.Text.Json;
using ChestCounter;

public static class Summary
{
    public const int Engine = 3;

    public static string Analyze(Stream stream, string name)
    {
        try
        {
            var m = new ReplayAnalyzer().Analyze(stream, name, "");
            return JsonSerializer.Serialize(new { ok = true, m = Of(m) });
        }
        catch (Exception e)
        {
            return JsonSerializer.Serialize(new { ok = false, error = e.GetType().Name + ": " + e.Message });
        }
    }

    public static object Of(MatchRecord m)
    {
        long? start = m.MatchStartUtc is { } s ? new DateTimeOffset(DateTime.SpecifyKind(s, DateTimeKind.Utc)).ToUnixTimeMilliseconds() : null;
        object Find(TreasureFind f) => new
        {
            n = f.DisplayName, p = f.PrizeId, t = f.ObtainedAtSeconds, g = f.FromGoldenChest, src = f.SourceKind,
            sure = f.SourceCertain, chest = f.SourceIsChest, dest = f.SourceDestroyed, mate = f.Teammate,
        };
        return new
        {
            counts = GameModes.Counts(m), why = GameModes.WhyNot(m), start, dur = m.DurationSeconds, playlist = m.Playlist, map = m.Map,
            custom = m.IsCustomMatch, player = m.PlayerName, epic = m.EpicId, place = m.Placement, players = m.TotalPlayers, kills = m.Eliminations,
            chests = m.ChestsOpened,
            list = m.Chests.Select(c => new { t = c.TimeSeconds, k = c.Kind, d = c.Destroyed }),
            other = m.OtherInteractions,
            treasures = m.TreasureFinds.Select(Find),
            team = m.TeamTreasureFinds.Select(Find),
            seen = m.SeenPrizes.Select(x => new { who = x.Player, p = x.PrizeId, t = x.TimeSeconds }),
            teamChests = m.TeamChestsOpened,
            mates = m.TeamChests.Select(x => new { n = x.Name, bot = x.IsBot, c = x.Chests, d = x.Destroyed }),
            vehSec = m.VehicleSeconds, vehM = m.VehicleMeters, vehMissing = m.VehicleChestsMissing,
        };
    }
}
