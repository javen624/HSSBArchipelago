using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace HardspaceShipbreaker.Archipelago;

/// <summary>
/// Persists location checks made while disconnected (BepInEx config), flushed on reconnect.
/// </summary>
internal static class OfflineCheckStore
{
    private static readonly object Gate = new();
    private static readonly HashSet<long> Pending = new();
    private static bool _loaded;

    public static int Count
    {
        get
        {
            EnsureLoaded();
            lock (Gate)
            {
                return Pending.Count;
            }
        }
    }

    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            ParseInto(Plugin.Instance.PendingOfflineChecks.Value ?? "", Pending);
            if (Pending.Count > 0)
            {
                Plugin.Log.LogInfo($"[HS-AP] Loaded {Pending.Count} pending offline location check(s).");
            }
        }
    }

    public static bool Enqueue(long locationId)
    {
        EnsureLoaded();
        lock (Gate)
        {
            if (!Pending.Add(locationId))
            {
                return false;
            }

            PersistUnlocked();
            return true;
        }
    }

    public static void MergeCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return;
        }

        EnsureLoaded();
        lock (Gate)
        {
            var before = Pending.Count;
            ParseInto(csv, Pending);
            if (Pending.Count != before)
            {
                PersistUnlocked();
                Plugin.Log.LogInfo(
                    $"[HS-AP] Merged offline checks from DataStorage (+{Pending.Count - before}).");
            }
        }
    }

    public static long[] Snapshot()
    {
        EnsureLoaded();
        lock (Gate)
        {
            return Pending.ToArray();
        }
    }

    public static void Remove(IEnumerable<long> ids)
    {
        EnsureLoaded();
        lock (Gate)
        {
            var changed = false;
            foreach (var id in ids)
            {
                if (Pending.Remove(id))
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

    public static void Clear()
    {
        EnsureLoaded();
        lock (Gate)
        {
            if (Pending.Count == 0)
            {
                return;
            }

            Pending.Clear();
            PersistUnlocked();
        }
    }

    public static string ToCsv()
    {
        EnsureLoaded();
        lock (Gate)
        {
            if (Pending.Count == 0)
            {
                return "";
            }

            var sb = new StringBuilder();
            foreach (var id in Pending.OrderBy(x => x))
            {
                if (sb.Length > 0)
                {
                    sb.Append(',');
                }

                sb.Append(id.ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }
    }

    private static void PersistUnlocked()
    {
        Plugin.Instance.PendingOfflineChecks.Value = ToCsvUnlocked();
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
        if (Pending.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder();
        foreach (var id in Pending.OrderBy(x => x))
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
