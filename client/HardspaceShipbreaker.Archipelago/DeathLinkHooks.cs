using System;
using System.Reflection;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using UnityEngine;

namespace HardspaceShipbreaker.Archipelago;

/// <summary>
/// Death Link send/receive. Receive uses DebugPlayerHealthService.ForceKillPlayer when available.
/// </summary>
internal static class DeathLinkHooks
{
    private static ArchipelagoClient? _client;
    private static DeathLinkService? _service;
    private static bool _enabled;
    private static DateTime _ignoreLocalUntilUtc = DateTime.MinValue;
    private static DateTime _lastSentUtc = DateTime.MinValue;
    private static MethodInfo? _forceKill;
    private static Type? _debugHealthType;

    public static void Initialize(ArchipelagoClient client)
    {
        _client = client;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name != "BBI.Unity.Game")
            {
                continue;
            }

            _debugHealthType = asm.GetType("BBI.Unity.Game.DebugPlayerHealthService");
            _forceKill = _debugHealthType?.GetMethod(
                "ForceKillPlayer",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            break;
        }

        Plugin.Log.LogInfo($"[HS-AP] DeathLink ForceKill ready={_forceKill != null}");
    }

    public static void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        Plugin.Log.LogInfo($"[HS-AP] Death Link enabled={enabled}");
    }

    public static void Attach(DeathLinkService service)
    {
        _service = service;
        _service.OnDeathLinkReceived += OnDeathLinkReceived;
        Plugin.Log.LogInfo("[HS-AP] Death Link service attached.");
    }

    public static void Detach()
    {
        if (_service != null)
        {
            _service.OnDeathLinkReceived -= OnDeathLinkReceived;
            _service = null;
        }
    }

    public static void OnLocalDeath(object[]? args)
    {
        _client?.TryCheckLocation(ArchipelagoClient.BaseId + 152, "Survive First Clone", "Death");

        if (!_enabled)
        {
            return;
        }

        Plugin.Log.LogInfo("[HS-AP] Local death/respawn path.");
        if (DateTime.UtcNow < _ignoreLocalUntilUtc)
        {
            Plugin.Log.LogInfo("[HS-AP] Local death ignored (Death Link / trap window).");
            return;
        }

        if (_service == null || _client is not { IsConnected: true })
        {
            return;
        }

        if ((DateTime.UtcNow - _lastSentUtc).TotalSeconds < 3)
        {
            return;
        }

        try
        {
            _lastSentUtc = DateTime.UtcNow;
            _service.SendDeathLink(new DeathLink(_client.SlotName ?? "Shipbreaker", "Clone fee collected"));
            Plugin.Log.LogInfo("[HS-AP] Death Link sent.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] Death Link send failed: {ex.Message}");
        }
    }

    public static void ForceLocalDeathFromTrap()
    {
        _ignoreLocalUntilUtc = DateTime.UtcNow.AddSeconds(5);
        if (!TryForceKill())
        {
            Plugin.Log.LogWarning("[HS-AP] Clone Fee Tax: ForceKillPlayer unavailable.");
            _ignoreLocalUntilUtc = DateTime.MinValue;
        }
    }

    private static void OnDeathLinkReceived(DeathLink link)
    {
        Plugin.Log.LogInfo($"[HS-AP] Death Link from {link.Source}: {link.Cause}");
        if (!_enabled)
        {
            Plugin.Log.LogInfo("[HS-AP] Death Link ignored (slot death_link=false).");
            return;
        }

        _ignoreLocalUntilUtc = DateTime.UtcNow.AddSeconds(5);
        if (!TryForceKill())
        {
            Plugin.Log.LogWarning("[HS-AP] Remote Death Link: ForceKillPlayer unavailable.");
            _ignoreLocalUntilUtc = DateTime.MinValue;
        }
    }

    private static bool TryForceKill()
    {
        try
        {
            if (_debugHealthType == null || _forceKill == null)
            {
                return false;
            }

            var instances = Resources.FindObjectsOfTypeAll(_debugHealthType);
            if (instances is not { Length: > 0 })
            {
                // Fallback: Object.FindObjectsOfType
                instances = UnityEngine.Object.FindObjectsOfType(_debugHealthType);
            }

            if (instances is not { Length: > 0 })
            {
                return false;
            }

            _forceKill.Invoke(instances[0], null);
            Plugin.Log.LogInfo("[HS-AP] Applied remote Death Link via ForceKillPlayer.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] ForceKillPlayer failed: {ex.Message}");
            return false;
        }
    }
}
