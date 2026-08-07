using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace HardspaceShipbreaker.Archipelago;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public const string PLUGIN_GUID = "hardspace.shipbreaker.archipelago";
    public const string PLUGIN_NAME = "Hardspace Shipbreaker Archipelago";
    public const string PLUGIN_VERSION = "0.6.6";

    internal static Plugin Instance { get; private set; } = null!;
    internal static ManualLogSource Log { get; private set; } = null!;

    internal ConfigEntry<string> Server = null!;
    internal ConfigEntry<int> Port = null!;
    internal ConfigEntry<string> Slot = null!;
    internal ConfigEntry<string> Password = null!;
    internal ConfigEntry<bool> AutoConnect = null!;
    internal ConfigEntry<bool> AutoReconnect = null!;
    /// <summary>server:port|slot — currency fillers are tracked per room/slot.</summary>
    internal ConfigEntry<string> CurrencyGrantKey = null!;
    /// <summary>Received-item ordinal watermark; Credit/LT packs at lower ordinals are not re-granted.</summary>
    internal ConfigEntry<int> CurrencyGrantCount = null!;
    /// <summary>Comma-separated location IDs checked while offline; flushed on reconnect.</summary>
    internal ConfigEntry<string> PendingOfflineChecks = null!;
    /// <summary>server:port|slot — Hab shop-sanity paid purchases are tracked per room/slot.</summary>
    internal ConfigEntry<string> HabShopPaidKey = null!;
    /// <summary>Comma-separated Hab shop location IDs paid in Hab (not AP release/F9).</summary>
    internal ConfigEntry<string> HabShopPaidLocationIds = null!;

    private ArchipelagoClient? _client;
    private ConnectionDialog? _connectionDialog;
    private int _debugLocationIndex;

    internal ArchipelagoClient? Client => _client;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        Server = Config.Bind("Connection", "Server", "localhost", "Last Archipelago server host (saved from connect dialog)");
        Port = Config.Bind("Connection", "Port", 38281, "Last Archipelago server port");
        Slot = Config.Bind("Connection", "Slot", "Player1", "Last slot / player name");
        Password = Config.Bind("Connection", "Password", "", "Last room password (optional)");
        AutoConnect = Config.Bind(
            "Connection",
            "AutoConnect",
            false,
            "If true, connect with saved settings on load; otherwise open the connect dialog");
        AutoReconnect = Config.Bind(
            "Connection",
            "AutoReconnect",
            true,
            "If true, retry connection with backoff after an unexpected drop");
        CurrencyGrantKey = Config.Bind(
            "Progress",
            "CurrencyGrantKey",
            "",
            "Internal: room/slot key for currency filler grant tracking");
        CurrencyGrantCount = Config.Bind(
            "Progress",
            "CurrencyGrantCount",
            0,
            "Internal: received-item count through which Credit/LT packs were already granted");
        PendingOfflineChecks = Config.Bind(
            "Progress",
            "PendingOfflineChecks",
            "",
            "Internal: location IDs checked while disconnected (flushed on reconnect)");
        HabShopPaidKey = Config.Bind(
            "Progress",
            "HabShopPaidKey",
            "",
            "Internal: room/slot key for Hab shop paid-location tracking");
        HabShopPaidLocationIds = Config.Bind(
            "Progress",
            "HabShopPaidLocationIds",
            "",
            "Internal: Hab shop location IDs purchased in Hab (shop-sanity yellow)");

        OfflineCheckStore.EnsureLoaded();
        HabShopPaidStore.EnsureLoaded();
        _client = new ArchipelagoClient();
        _connectionDialog = new ConnectionDialog(this);
        Log.LogInfo(
            $"{PLUGIN_NAME} v{PLUGIN_VERSION} loaded. F6 = progress · F7 = connect · F8 = check · F9 = goal · F10/F11 = cert debug.");

        try
        {
            GameHooks.Apply(_client);
        }
        catch (Exception ex)
        {
            Log.LogError($"[HS-AP] GameHooks.Apply failed: {ex}");
        }

        if (AutoConnect.Value)
        {
            _client.Connect(Server.Value, Port.Value, Slot.Value, Password.Value);
            if (!_client.IsConnected)
            {
                _connectionDialog.Visible = true;
                _connectionDialog.SetStatus(_client.LastConnectError ?? "Auto-connect failed — will retry if AutoReconnect is on.");
            }
        }
        else
        {
            _connectionDialog.Visible = true;
        }
    }

    private void Update()
    {
        _client?.Tick();

        if (Input.GetKeyDown(KeyCode.F6))
        {
            ProgressHud.Toggle();
        }

        if (Input.GetKeyDown(KeyCode.F8))
        {
            _client?.DebugCheckNextLocation(ref _debugLocationIndex);
        }

        if (Input.GetKeyDown(KeyCode.F9))
        {
            _client?.SendGoal();
        }

        if (Input.GetKeyDown(KeyCode.F10))
        {
            ItemApplicator.DebugIncreaseCertificationRankByOne();
        }

        if (Input.GetKeyDown(KeyCode.F11))
        {
            ItemApplicator.DebugIncreaseProgressiveCertCap();
        }

        if (Input.GetKeyDown(KeyCode.F7))
        {
            if (_connectionDialog != null)
            {
                _connectionDialog.Visible = !_connectionDialog.Visible;
            }
        }
    }

    private void OnGUI()
    {
        ApToastQueue.Draw();
        ProgressHud.Draw(_client);
        _connectionDialog?.Draw();
    }

    private void OnDestroy()
    {
        GameHooks.Unpatch();
        _client?.Disconnect();
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = Plugin.PLUGIN_GUID;
    public const string PLUGIN_NAME = Plugin.PLUGIN_NAME;
    public const string PLUGIN_VERSION = Plugin.PLUGIN_VERSION;
}
