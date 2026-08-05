using UnityEngine;

namespace HardspaceShipbreaker.Archipelago;

/// <summary>F6 toggle: connection + check progress (minimal Phase 3 tracker UI.  Should probably be replaced with PopTracker).</summary>
internal static class ProgressHud
{
    private static bool _visible;
    private static Rect _rect = new(12, 120, 320, 120);

    public static void Toggle() => _visible = !_visible;

    public static void Draw(ArchipelagoClient? client)
    {
        if (!_visible || client == null)
        {
            return;
        }

        var status = client.ConnectionStatusLabel;
        var checkedN = client.LocalCheckedCount;
        var pending = OfflineCheckStore.Count;
        var goal = client.GoalMode switch
        {
            ArchipelagoClient.GoalAtlasScout => "Atlas Scout Tier 5",
            ArchipelagoClient.GoalRank20 => "Rank 20",
            _ => "Debt payoff"
        };

        GUI.Box(_rect, "HS-AP Progress (F6)");
        GUI.Label(new Rect(_rect.x + 10, _rect.y + 28, _rect.width - 20, 22), status);
        GUI.Label(
            new Rect(_rect.x + 10, _rect.y + 50, _rect.width - 20, 22),
            $"Checked (local): {checkedN} · Offline queue: {pending}");
        GUI.Label(new Rect(_rect.x + 10, _rect.y + 72, _rect.width - 20, 22), $"Goal: {goal}");
        GUI.Label(
            new Rect(_rect.x + 10, _rect.y + 94, _rect.width - 20, 22),
            "Universal Tracker: use AP game 'Hardspace Shipbreaker'");
    }
}
