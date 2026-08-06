using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace HardspaceShipbreaker.Archipelago;

/// <summary>
/// Persists Hab shop-sanity purchases (location IDs) in BepInEx config per room/slot.
/// In-memory <see cref="ItemApplicator"/> paid-set alone is wiped on restart, which let
/// StripUnpaidShopRowsFromHabOwned revert saved Hab yellow rows.
/// </summary>
internal static class HabShopPaidStore
{
    private static readonly object Gate = new();
    private static readonly HashSet<long> Paid = new();
    private static bool _loaded;
    private static string _activeKey = "";

    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _activeKey = Plugin.Instance.HabShopPaidKey.Value ?? "";
            ParseInto(Plugin.Instance.HabShopPaidLocationIds.Value ?? "", Paid);
            if (Paid.Count > 0)
            {
                Plugin.Log.LogInfo(
                    $"[HS-AP] Loaded {Paid.Count} Hab shop paid location(s) for key '{_activeKey}'.");
            }
        }
    }

    /// <summary>Switch persistence to host:port|slot|seed; clears paid IDs when the key changes.</summary>
    public static void SetRoomKey(string key)
    {
        EnsureLoaded();
        lock (Gate)
        {
            key ??= "";
            if (string.Equals(_activeKey, key, StringComparison.Ordinal))
            {
                return;
            }

            _activeKey = key;
            Plugin.Instance.HabShopPaidKey.Value = key;
            Paid.Clear();
            // Keep CSV only when key matches; new key starts empty (profile seed may refill).
            Plugin.Instance.HabShopPaidLocationIds.Value = "";
            PersistUnlocked();
            Plugin.Log.LogInfo($"[HS-AP] Hab shop paid tracking key → '{key}' (cleared prior paid set).");
            // Drop stale Hab-yellow rows from a previous multiworld so they cannot be
            // re-seeded from PlayerProfile.Upgrades into the new seed's paid set.
            ItemApplicator.ClearHabShopYellowExceptFreeStarters();
        }
    }

    public static void CopyInto(HashSet<long> target)
    {
        EnsureLoaded();
        lock (Gate)
        {
            foreach (var id in Paid)
            {
                target.Add(id);
            }
        }
    }

    public static bool Remember(long locationId)
    {
        if (locationId <= 0)
        {
            return false;
        }

        EnsureLoaded();
        lock (Gate)
        {
            if (!Paid.Add(locationId))
            {
                return false;
            }

            PersistUnlocked();
            return true;
        }
    }

    public static void RememberMany(IEnumerable<long> locationIds)
    {
        EnsureLoaded();
        lock (Gate)
        {
            var changed = false;
            foreach (var id in locationIds)
            {
                if (id > 0 && Paid.Add(id))
                {
                    changed = true;
                }
            }

            if (changed)
            {
                PersistUnlocked();
            }
        }
    }

    public static void ClearPaid()
    {
        EnsureLoaded();
        lock (Gate)
        {
            if (Paid.Count == 0 && string.IsNullOrEmpty(Plugin.Instance.HabShopPaidLocationIds.Value))
            {
                return;
            }

            Paid.Clear();
            Plugin.Instance.HabShopPaidLocationIds.Value = "";
            PersistUnlocked();
        }
    }

    private static void PersistUnlocked()
    {
        Plugin.Instance.HabShopPaidLocationIds.Value = ToCsvUnlocked();
        try
        {
            Plugin.Instance.Config.Save();
        }
        catch
        {
            // ignore
        }
    }

    private static string ToCsvUnlocked()
    {
        if (Paid.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder();
        foreach (var id in Paid.OrderBy(x => x))
        {
            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(id.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static void ParseInto(string csv, HashSet<long> target)
    {
        foreach (var part in csv.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (long.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                && id > 0)
            {
                target.Add(id);
            }
        }
    }
}
