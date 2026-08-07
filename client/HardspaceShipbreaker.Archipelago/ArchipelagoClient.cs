using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Models;
using UnityEngine;

namespace HardspaceShipbreaker.Archipelago;

/// <summary>
/// Archipelago session: connect, location checks, item apply, Death Link, auto-goal,
/// offline check queue, auto-reconnect / resync (Phase 3).
/// </summary>
public sealed class ArchipelagoClient
{
    // Must match worlds/HardspaceShipbreaker/items.py BASE_ID + offsets.
    public const long BaseId = 2_026_080_100;

    // goal slot data: 0 = debt_payoff, 1 = atlas_scout, 2 = rank_20
    public const int GoalDebtPayoff = 0;
    public const int GoalAtlasScout = 1;
    public const int GoalRank20 = 2;

    private const string DataStorageOfflineKey = "HardspaceShipbreaker.OfflineChecks";

    private static readonly (string Name, long Id)[] DebugLocations =
    {
        ("Finish Basic Training", BaseId + 100),
        ("Complete First Shift", BaseId + 101),
        ("First Barge Deposit", BaseId + 110),
        ("First Processor Deposit", BaseId + 111),
        ("First Furnace Deposit", BaseId + 112),
        ("Process Aluminum Structure", BaseId + 113),
        ("Furnace Glass", BaseId + 114),
        ("Clear Ship Grade 1", BaseId + 121),
        ("Salvage Fuel Tank", BaseId + 122),
        ("Place First Tether", BaseId + 123),
        ("Salvage Power Cell", BaseId + 124),
        ("Salvage Class I Reactor", BaseId + 125),
        ("Salvage Thruster Class I", BaseId + 130),
        ("Salvage Quasar Thruster", BaseId + 131),
        ("Salvage ECU", BaseId + 132),
        ("Salvage Coolant Tank", BaseId + 133),
        ("Salvage Airlock", BaseId + 134),
        ("Process Titanium Structure", BaseId + 135),
        ("Process Nanocarbon Panel", BaseId + 136),
        ("Clear Ship Grade 4", BaseId + 137),
        ("Clear Ship Grade 7", BaseId + 138),
        ("Reach Certification Rank 5", BaseId + 139),
        ("Reach Certification Rank 10", BaseId + 140),
        ("Reach Certification Rank 15", BaseId + 146),
        ("Reach Certification Rank 20", BaseId + 147),
        ("Recover First Data Drive", BaseId + 141),
        ("Recover 3 Data Drives", BaseId + 148),
        ("Recover 5 Data Drives", BaseId + 149),
        ("Salvage Computer Terminal", BaseId + 143),
        ("Salvage Communications Array", BaseId + 144),
        ("Salvage Airlock Console", BaseId + 145),
        ("Clear a Ghost Ship", BaseId + 183),
        ("Atlas Scout Salvage Tier 5", BaseId + 374),
        ("Hab: Unlock Tethers", BaseId + 200),
        ("Hab: Grapple Strength 1", BaseId + 201),
        ("Hab: Grapple Strength 2", BaseId + 202),
        ("Hab: Grapple Strength 3", BaseId + 203),
        ("Hab: Unlock Demo Charge", BaseId + 211),
        ("Hab: Purchase First Equipment Upgrade", BaseId + 212),
        ("Hab: Scanner Objects", BaseId + 213),
        ("Hab: Scanner Systems", BaseId + 214),
        ("Hab: Charged Push", BaseId + 221),
        ("Hab: Grapple Strength 4", BaseId + 204),
        ("Hab: Grapple Strength 5", BaseId + 205),
        ("Hab: Suit Integrity 1", BaseId + 215),
        ("Hab: Suit Integrity 2", BaseId + 216),
        ("Hab: Heat Resistance 1", BaseId + 230),
        ("Hab: Cryo Resistance 1", BaseId + 235),
        ("Hab: Electrical Resistance 1", BaseId + 240),
        ("Hab: Audio Resynth 1", BaseId + 297),
        ("Hab: Suit Durability 1", BaseId + 346),
    };

    private ArchipelagoSession? _session;
    private readonly HashSet<long> _checkedLocations = new();
    private bool _goalSent;
    private bool _connecting;
    private int _goalMode = GoalDebtPayoff;
    private bool _deathLink;
    private bool _habShopSanity = true;
    private float _debtPollTimer;
    private bool _connectSyncActive = true;
    private int _receiveOrdinal;
    private int _currencyGrantBaseline;
    private bool _currencyKeyIsNew;

    private readonly object _applyGate = new();
    private readonly Queue<(string Name, long ItemId, string From, bool Toast)> _applyQueue = new();
    private float _burstQuietUntil;
    private int _burstApplied;
    private bool _burstDrainNotified;
    private float _configSaveDueAt = -1f;
    private const int AppliesPerFrame = 2;

    private bool _wantConnected;
    private bool _userDisconnect;
    private string _savedServer = "localhost";
    private int _savedPort = 38281;
    private string _savedSlot = "Player1";
    private string _savedPassword = "";
    private float _reconnectAt = -1f;
    private float _reconnectBackoff = 2f;
    private const float ReconnectBackoffMax = 60f;
    private bool _wasConnected;
    private string? _reconnectStatus;

    public bool IsConnected => _session?.Socket.Connected == true;
    public bool IsConnecting => _connecting;
    public bool IsReconnecting => _wantConnected && !IsConnected && !_connecting && _reconnectAt >= 0f;
    public string? LastConnectError { get; private set; }
    public string? SlotName { get; private set; }
    public int GoalMode => _goalMode;
    public bool HabShopSanityEnabled => _habShopSanity;
    public int LocalCheckedCount => _checkedLocations.Count;

    public string ConnectionStatusLabel
    {
        get
        {
            if (IsConnected)
            {
                return $"Connected as '{SlotName}'";
            }

            if (_connecting)
            {
                return "Connecting…";
            }

            if (IsReconnecting)
            {
                var wait = Math.Max(0f, _reconnectAt - Time.unscaledTime);
                return _reconnectStatus ?? $"Reconnecting in {wait:0.0}s…";
            }

            if (OfflineCheckStore.Count > 0)
            {
                return $"Offline ({OfflineCheckStore.Count} queued)";
            }

            return "Disconnected";
        }
    }

    public void Connect(string server, int port, string slot, string password)
    {
        if (_connecting)
        {
            Plugin.Log.LogWarning("[HS-AP] Connect skipped: already connecting.");
            return;
        }

        if (IsConnected)
        {
            Plugin.Log.LogWarning("[HS-AP] Connect skipped: already connected.");
            return;
        }

        _savedServer = server;
        _savedPort = port;
        _savedSlot = slot;
        _savedPassword = password ?? "";
        _wantConnected = true;
        _userDisconnect = false;
        _reconnectAt = -1f;

        _connecting = true;
        LastConnectError = null;
        SlotName = slot;
        _connectSyncActive = true;
        _receiveOrdinal = 0;
        try
        {
            TeardownSession();
            _connectSyncActive = true;
            _receiveOrdinal = 0;
            _session = ArchipelagoSessionFactory.CreateSession(server, port);
            _session.MessageLog.OnMessageReceived += OnMessage;
            _session.Items.ItemReceived += OnItemReceived;
            _session.Socket.SocketClosed += OnSocketClosed;
            _session.Socket.ErrorReceived += OnSocketError;

            Plugin.Log.LogInfo($"[HS-AP] Connecting to {server}:{port} as '{slot}'...");
            LoginResult result;
            try
            {
                // MultiClient.Net 6.0.1 has no client compression toggle; AP server negotiates transport.
                result = _session.TryConnectAndLogin(
                    "Hardspace Shipbreaker",
                    slot,
                    ItemsHandlingFlags.AllItems,
                    version: new Version(0, 5, 1),
                    tags: new[] { "DeathLink", "AP" },
                    password: string.IsNullOrEmpty(password) ? null : password
                );
            }
            catch (Exception e)
            {
                result = new LoginFailure(e.GetBaseException().Message);
            }

            if (!result.Successful)
            {
                var fail = (LoginFailure)result;
                LastConnectError = string.Join("; ", fail.Errors);
                Plugin.Log.LogError($"[HS-AP] Login failed: {LastConnectError}");
                TeardownSession();
                ScheduleReconnect("Login failed");
                return;
            }

            var success = (LoginSuccessful)result;
            Plugin.Log.LogInfo($"[HS-AP] Connected. Slot={success.Slot} Team={success.Team}");
            _wasConnected = true;
            _reconnectBackoff = 2f;
            _reconnectAt = -1f;
            _reconnectStatus = null;
            ParseSlotData(success.SlotData);
            // Scope progress persistence to AP seed so a fresh multiworld on the same
            // host/port/slot does not inherit Hab-yellow / currency watermarks.
            var seed = ResolveRoomSeed();
            // Always use a 3-part key (host:port|slot|seed). Legacy 2-part keys are cleared.
            var roomKey = $"{server}:{port}|{slot}|{(string.IsNullOrEmpty(seed) ? "unknown" : seed)}";
            Plugin.Log.LogInfo($"[HS-AP] v{Plugin.PLUGIN_VERSION} roomKey='{roomKey}' seed='{seed}'");
            BeginCurrencyGrantTracking(roomKey);
            FinishCurrencyGrantTrackingAfterLogin();
            HabShopPaidStore.SetRoomKey(roomKey);

            SyncCheckedLocationsFromServer();
            // Fresh AP rooms have no Hab checks — drop any ghost paid/yellow from prior seeds.
            ItemApplicator.EnsureHabShopPaidState(CountCheckedHabShopLocations());
            PullAndMergeDataStorageOfflineChecks();
            FlushOfflineCheckQueue();
            PushOfflineQueueToDataStorage();

            try
            {
                var deathLink = _session.CreateDeathLinkService();
                if (_deathLink)
                {
                    deathLink.EnableDeathLink();
                }

                DeathLinkHooks.Attach(deathLink);
                DeathLinkHooks.SetEnabled(_deathLink);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HS-AP] Death Link setup skipped: {ex.Message}");
            }

            ApToastQueue.EnqueueInfo($"Connected as '{slot}'");
        }
        catch (Exception ex)
        {
            LastConnectError = ex.Message;
            Plugin.Log.LogError($"[HS-AP] Connect exception: {ex}");
            TeardownSession();
            ScheduleReconnect("Connect error");
        }
        finally
        {
            _connecting = false;
        }
    }

    /// <summary>User-initiated disconnect — disables auto-reconnect.</summary>
    public void Disconnect()
    {
        _wantConnected = false;
        _userDisconnect = true;
        _reconnectAt = -1f;
        _reconnectStatus = null;
        TeardownSession();
        Plugin.Log.LogInfo("[HS-AP] Disconnected (user).");
        ApToastQueue.EnqueueInfo("Disconnected");
    }

    private void TeardownSession()
    {
        if (_session == null)
        {
            return;
        }

        try
        {
            _session.MessageLog.OnMessageReceived -= OnMessage;
            _session.Items.ItemReceived -= OnItemReceived;
            _session.Socket.SocketClosed -= OnSocketClosed;
            _session.Socket.ErrorReceived -= OnSocketError;
            if (_session.Socket.Connected)
            {
                _session.Socket.DisconnectAsync();
            }
        }
        catch
        {
            // ignore
        }

        _session = null;
        _connectSyncActive = true;
        _receiveOrdinal = 0;
    }

    private void OnSocketClosed(string reason)
    {
        Plugin.Log.LogWarning($"[HS-AP] Socket closed: {reason}");
        _session = null;
        _connectSyncActive = true;
        _receiveOrdinal = 0;
        _wasConnected = false;
        if (_wantConnected && !_userDisconnect)
        {
            ScheduleReconnect(string.IsNullOrEmpty(reason) ? "Socket closed" : reason);
            ApToastQueue.EnqueueInfo("Connection lost — reconnecting…");
        }
    }

    private void OnSocketError(Exception e, string message)
    {
        Plugin.Log.LogWarning($"[HS-AP] Socket error: {message} ({e.Message})");
    }

    private void ScheduleReconnect(string reason)
    {
        if (!_wantConnected || _userDisconnect || !Plugin.Instance.AutoReconnect.Value)
        {
            return;
        }

        _reconnectBackoff = Math.Min(ReconnectBackoffMax, Math.Max(2f, _reconnectBackoff));
        _reconnectAt = Time.unscaledTime + _reconnectBackoff;
        _reconnectStatus = $"{reason} — retry in {_reconnectBackoff:0}s";
        Plugin.Log.LogInfo($"[HS-AP] Reconnect scheduled in {_reconnectBackoff:0}s ({reason}).");
        _reconnectBackoff = Math.Min(ReconnectBackoffMax, _reconnectBackoff * 2f);
    }

    private void PumpReconnect()
    {
        if (!_wantConnected || _userDisconnect || _connecting || IsConnected)
        {
            return;
        }

        if (!Plugin.Instance.AutoReconnect.Value)
        {
            return;
        }

        if (_reconnectAt < 0f || Time.unscaledTime < _reconnectAt)
        {
            return;
        }

        _reconnectAt = -1f;
        _reconnectStatus = "Reconnecting…";
        Plugin.Log.LogInfo("[HS-AP] Auto-reconnect attempt…");
        Connect(_savedServer, _savedPort, _savedSlot, _savedPassword);
    }

    private void BeginCurrencyGrantTracking(string key)
    {
        var cfg = Plugin.Instance;
        var prevKey = cfg.CurrencyGrantKey.Value ?? "";
        _currencyKeyIsNew = !string.Equals(prevKey, key, StringComparison.Ordinal);

        if (_currencyKeyIsNew)
        {
            cfg.CurrencyGrantKey.Value = key;
            _currencyGrantBaseline = int.MaxValue;
            Plugin.Log.LogInfo(
                $"[HS-AP] Currency grant tracking new key '{key}' — will baseline after login.");
        }
        else
        {
            _currencyGrantBaseline = Math.Max(0, cfg.CurrencyGrantCount.Value);
            Plugin.Log.LogInfo(
                $"[HS-AP] Currency grant baseline={_currencyGrantBaseline} (skip Credit/LT below this on connect replay).");
        }
    }

    private void FinishCurrencyGrantTrackingAfterLogin()
    {
        try
        {
            var count = _session?.Items.AllItemsReceived.Count ?? 0;
            if (_currencyKeyIsNew)
            {
                Plugin.Instance.CurrencyGrantCount.Value = count;
                _currencyGrantBaseline = count;
                _currencyKeyIsNew = false;
                Plugin.Log.LogInfo(
                    $"[HS-AP] Currency grant baseline set to server item count {count} for new key.");
            }
            else
            {
                _currencyGrantBaseline = Math.Max(_currencyGrantBaseline, Plugin.Instance.CurrencyGrantCount.Value);
            }

            if (count == 0)
            {
                _connectSyncActive = false;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] FinishCurrencyGrantTrackingAfterLogin failed: {ex.Message}");
            _currencyGrantBaseline = Plugin.Instance.CurrencyGrantCount.Value;
            _currencyKeyIsNew = false;
        }
    }

    private static bool IsCurrencyFiller(string name) =>
        name.StartsWith("Credit Pack", StringComparison.Ordinal)
        || name.StartsWith("LYNX Token Pack", StringComparison.Ordinal);

    private void AdvanceCurrencyGrantWatermark(int ordinalExclusive)
    {
        var cfg = Plugin.Instance;
        var serverIndex = 0;
        try
        {
            serverIndex = _session?.Items.Index ?? 0;
        }
        catch
        {
            // ignore
        }

        var next = Math.Max(ordinalExclusive, serverIndex);
        if (next > cfg.CurrencyGrantCount.Value)
        {
            cfg.CurrencyGrantCount.Value = next;
        }
    }

    private void SyncCheckedLocationsFromServer()
    {
        try
        {
            var checkedIds = _session!.Locations.AllLocationsChecked;
            var n = 0;
            foreach (var id in checkedIds)
            {
                if (_checkedLocations.Add(id))
                {
                    n++;
                }
            }

            Plugin.Log.LogInfo($"[HS-AP] Synced {checkedIds.Count} checked location(s) from server (+{n} new to local set).");
            ItemApplicator.SyncHabShopPaidFromChecked(_checkedLocations);
            if (_checkedLocations.Contains(BaseId + 100))
            {
                ItemApplicator.MarkCareerBayReadyAfterCollect();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] SyncCheckedLocationsFromServer failed: {ex.Message}");
        }
    }

    private void PullAndMergeDataStorageOfflineChecks()
    {
        if (_session == null)
        {
            return;
        }

        try
        {
            var task = _session.DataStorage[Scope.Slot, DataStorageOfflineKey].GetAsync<string>();
            if (!task.Wait(TimeSpan.FromSeconds(2)))
            {
                Plugin.Log.LogInfo("[HS-AP] DataStorage offline-check pull timed out.");
                return;
            }

            OfflineCheckStore.MergeCsv(task.Result);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogInfo($"[HS-AP] DataStorage offline-check pull skipped: {ex.Message}");
        }
    }

    private void PushOfflineQueueToDataStorage()
    {
        if (_session == null || !IsConnected)
        {
            return;
        }

        try
        {
            var csv = OfflineCheckStore.ToCsv();
            _session.DataStorage[Scope.Slot, DataStorageOfflineKey] = csv ?? "";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogInfo($"[HS-AP] DataStorage offline-check push skipped: {ex.Message}");
        }
    }

    private void FlushOfflineCheckQueue()
    {
        if (_session == null || !IsConnected)
        {
            return;
        }

        var pending = OfflineCheckStore.Snapshot();
        if (pending.Length == 0)
        {
            return;
        }

        var toSend = pending.Where(id => !_session.Locations.AllLocationsChecked.Contains(id)).ToArray();
        foreach (var id in pending)
        {
            _checkedLocations.Add(id);
        }

        if (toSend.Length == 0)
        {
            OfflineCheckStore.Clear();
            PushOfflineQueueToDataStorage();
            Plugin.Log.LogInfo("[HS-AP] Offline queue empty after server sync (already checked).");
            return;
        }

        try
        {
            _session.Locations.CompleteLocationChecks(toSend);
            OfflineCheckStore.Clear();
            PushOfflineQueueToDataStorage();
            Plugin.Log.LogInfo($"[HS-AP] Flushed {toSend.Length} offline location check(s) to server.");
            ApToastQueue.EnqueueInfo($"Flushed {toSend.Length} offline checks");
            ItemApplicator.SyncHabShopPaidFromChecked(_checkedLocations);
            foreach (var id in toSend)
            {
                MaybeFireGoalForLocation(id);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] Flush offline checks failed: {ex.Message}");
        }
    }

    public bool IsLocationChecked(long locationId) => _checkedLocations.Contains(locationId);

    private string ResolveRoomSeed()
    {
        try
        {
            var seed = _session?.RoomState?.Seed;
            if (!string.IsNullOrWhiteSpace(seed))
            {
                return seed.Trim();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] RoomState.Seed read failed: {ex.Message}");
        }

        return "";
    }

    private int CountCheckedHabShopLocations()
    {
        var n = 0;
        foreach (var id in _checkedLocations)
        {
            if (HabEquipmentCatalog.IsHabShopLocationId(id))
            {
                n++;
            }
        }

        return n;
    }

    private void ParseSlotData(Dictionary<string, object>? slotData)
    {
        _goalMode = GoalDebtPayoff;
        _deathLink = false;
        _habShopSanity = true;
        var creditSmall = 1_000_000f;
        var creditMedium = 3_000_000f;
        var creditLarge = 8_000_000f;
        var hasExplicitCreditAmounts = false;
        int? creditPackValue = null;

        if (slotData != null)
        {
            foreach (var kv in slotData)
            {
                Plugin.Log.LogInfo($"[HS-AP] SlotData {kv.Key}={kv.Value}");
                if (kv.Key.Equals("goal", StringComparison.OrdinalIgnoreCase))
                {
                    _goalMode = Convert.ToInt32(kv.Value);
                }
                else if (kv.Key.Equals("death_link", StringComparison.OrdinalIgnoreCase))
                {
                    _deathLink = kv.Value is bool b ? b : Convert.ToInt32(kv.Value) != 0;
                }
                else if (kv.Key.Equals("hab_shop_sanity", StringComparison.OrdinalIgnoreCase))
                {
                    _habShopSanity = kv.Value is bool hb ? hb : Convert.ToInt32(kv.Value) != 0;
                }
                else if (kv.Key.Equals("credit_pack_small", StringComparison.OrdinalIgnoreCase))
                {
                    creditSmall = Convert.ToSingle(kv.Value);
                    hasExplicitCreditAmounts = true;
                }
                else if (kv.Key.Equals("credit_pack_medium", StringComparison.OrdinalIgnoreCase))
                {
                    creditMedium = Convert.ToSingle(kv.Value);
                    hasExplicitCreditAmounts = true;
                }
                else if (kv.Key.Equals("credit_pack_large", StringComparison.OrdinalIgnoreCase))
                {
                    creditLarge = Convert.ToSingle(kv.Value);
                    hasExplicitCreditAmounts = true;
                }
                else if (kv.Key.Equals("credit_pack_value", StringComparison.OrdinalIgnoreCase))
                {
                    creditPackValue = Convert.ToInt32(kv.Value);
                }
            }
        }

        if (!hasExplicitCreditAmounts && creditPackValue is int cpv)
        {
            switch (cpv)
            {
                case 0: // low
                    creditSmall = 250_000f;
                    creditMedium = 1_000_000f;
                    creditLarge = 2_500_000f;
                    break;
                case 2: // high
                    creditSmall = 5_000_000f;
                    creditMedium = 20_000_000f;
                    creditLarge = 40_000_000f;
                    break;
                default: // normal
                    creditSmall = 1_000_000f;
                    creditMedium = 3_000_000f;
                    creditLarge = 8_000_000f;
                    break;
            }
        }

        Plugin.Log.LogInfo($"[HS-AP] GoalMode={_goalMode} DeathLink={_deathLink} HabShopSanity={_habShopSanity}");
        ItemApplicator.SetHabShopSanity(_habShopSanity);
        ItemApplicator.SetCreditPackAmounts(creditSmall, creditMedium, creditLarge);
    }

    private void OnMessage(LogMessage message) => Plugin.Log.LogInfo($"[HS-AP] {message}");

    public void Tick()
    {
        PumpReconnect();
        PumpApplyQueue();
        FlushConfigSaveIfDue();

        if (_wasConnected && _wantConnected && !_connecting && !IsConnected && _reconnectAt < 0f)
        {
            Plugin.Log.LogWarning("[HS-AP] Connection lost (poll).");
            _wasConnected = false;
            TeardownSession();
            ScheduleReconnect("Connection lost");
            ApToastQueue.EnqueueInfo("Connection lost — reconnecting…");
        }
        else if (IsConnected)
        {
            _wasConnected = true;
        }

        var quiet = Time.unscaledTime < _burstQuietUntil;
        ItemApplicator.SetQuietCurrencyGrants(quiet);

        if (!IsConnected || _goalSent || _goalMode != GoalDebtPayoff)
        {
            return;
        }

        _debtPollTimer += Time.unscaledDeltaTime;
        if (_debtPollTimer < 2f)
        {
            return;
        }

        _debtPollTimer = 0f;
        if (ItemApplicator.TryReadDebtPaidOff(out var paid) && paid)
        {
            Plugin.Log.LogInfo("[HS-AP] PlayerProfile.DebtPaidOff=true — sending goal.");
            SendGoal();
        }
    }

    private bool InBurstQuiet => Time.unscaledTime < _burstQuietUntil;

    private void BeginBurstQuiet(float seconds, string reason)
    {
        _burstQuietUntil = Mathf.Max(_burstQuietUntil, Time.unscaledTime + seconds);
        _burstDrainNotified = false;
        ItemApplicator.SetQuietCurrencyGrants(true);
        ApToastQueue.Clear();
        ApToastQueue.EnqueueInfo(reason);
        Plugin.Log.LogInfo($"[HS-AP] Burst quiet mode for {seconds:0}s — {reason}");
    }

    private void PumpApplyQueue()
    {
        for (var i = 0; i < AppliesPerFrame; i++)
        {
            (string Name, long ItemId, string From, bool Toast) job;
            lock (_applyGate)
            {
                if (_applyQueue.Count == 0)
                {
                    break;
                }

                job = _applyQueue.Dequeue();
            }

            try
            {
                ItemApplicator.Apply(job.Name, job.ItemId);
                _burstApplied++;
                if (job.Toast && !InBurstQuiet)
                {
                    ApToastQueue.EnqueueReceived(job.Name, job.From);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HS-AP] Deferred apply '{job.Name}' failed: {ex.Message}");
            }
        }

        int left;
        lock (_applyGate)
        {
            left = _applyQueue.Count;
        }

        if (left > 0 || _burstApplied <= 0)
        {
            return;
        }

        if (!_burstDrainNotified)
        {
            _burstDrainNotified = true;
            ApToastQueue.EnqueueInfo($"Applied {_burstApplied} items");
            ScheduleConfigSave(0.25f);
            Plugin.Log.LogInfo($"[HS-AP] Apply queue drained ({_burstApplied} items).");
            try
            {
                SyncCheckedLocationsFromServer();
                ItemApplicator.MarkCareerBayReadyAfterCollect();
                ItemApplicator.DebugRefreshAvailableShips();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HS-AP] Post-collect bay refresh failed: {ex.Message}");
            }
        }

        if (!InBurstQuiet)
        {
            _burstApplied = 0;
            _burstDrainNotified = false;
            ItemApplicator.SetQuietCurrencyGrants(false);
        }
    }

    private void ScheduleConfigSave(float delaySeconds)
    {
        var due = Time.unscaledTime + delaySeconds;
        if (_configSaveDueAt < 0f || due < _configSaveDueAt)
        {
            _configSaveDueAt = due;
        }
    }

    private void FlushConfigSaveIfDue()
    {
        if (_configSaveDueAt < 0f || Time.unscaledTime < _configSaveDueAt)
        {
            return;
        }

        _configSaveDueAt = -1f;
        try
        {
            Plugin.Instance.Config.Save();
        }
        catch
        {
            // ignore
        }
    }

    private void EnqueueApply(string name, long itemId, string from, bool toast)
    {
        lock (_applyGate)
        {
            _applyQueue.Enqueue((name, itemId, from, toast));
            if (_applyQueue.Count >= 8 && !InBurstQuiet)
            {
                BeginBurstQuiet(90f, "Item flood — applying slowly");
            }
        }
    }

    public void DebugCheckNextLocation(ref int index)
    {
        var len = DebugLocations.Length;
        for (var step = 0; step < len; step++)
        {
            var i = (index + step) % len;
            var entry = DebugLocations[i];
            if (_checkedLocations.Contains(entry.Id))
            {
                continue;
            }

            index = i + 1;
            CheckLocation(entry.Id, entry.Name, "F8");
            return;
        }

        index = 0;
        Plugin.Log.LogInfo("[HS-AP] F8: all debug locations already checked.");
    }

    public bool TryCheckLocation(long locationId, string? debugName = null, string source = "client")
    {
        if (_checkedLocations.Contains(locationId))
        {
            return false;
        }

        CheckLocation(locationId, debugName, source);
        return _checkedLocations.Contains(locationId);
    }

    public void CheckLocation(long locationId, string? debugName = null, string source = "client")
    {
        if (!_checkedLocations.Add(locationId))
        {
            return;
        }

        var locLabel = debugName ?? locationId.ToString();

        if (!IsConnected || _session == null)
        {
            OfflineCheckStore.Enqueue(locationId);
            PushOfflineQueueToDataStorage();
            Plugin.Log.LogInfo(
                $"[HS-AP] Queued offline location check [{source}]: {locLabel} ({locationId})");
            ApToastQueue.EnqueueChecked($"{locLabel} (queued)");
            MaybeFireGoalForLocation(locationId);
            return;
        }

        Plugin.Log.LogInfo($"[HS-AP] Sending location check [{source}]: {locLabel} ({locationId})");
        try
        {
            _session.Locations.CompleteLocationChecks(locationId);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] CompleteLocationChecks failed — queuing offline: {ex.Message}");
            OfflineCheckStore.Enqueue(locationId);
            PushOfflineQueueToDataStorage();
            ApToastQueue.EnqueueChecked($"{locLabel} (queued)");
            MaybeFireGoalForLocation(locationId);
            return;
        }

        ApToastQueue.EnqueueChecked(locLabel);
        MaybeFireGoalForLocation(locationId);
    }

    private void MaybeFireGoalForLocation(long locationId)
    {
        if (locationId == BaseId + 100)
        {
            ItemApplicator.MarkCareerBayReadyAfterCollect();
        }

        if (_goalMode == GoalAtlasScout && locationId == BaseId + 374)
        {
            Plugin.Log.LogInfo("[HS-AP] Atlas Scout Salvage Tier 5 checked — sending goal.");
            SendGoal();
        }

        if (_goalMode == GoalRank20 && locationId == BaseId + 147)
        {
            Plugin.Log.LogInfo("[HS-AP] Rank 20 goal location checked — sending goal.");
            SendGoal();
        }
    }

    public void SendGoal()
    {
        if (!IsConnected || _session == null)
        {
            Plugin.Log.LogWarning("[HS-AP] F9 ignored: not connected.");
            return;
        }

        if (!_goalSent)
        {
            _goalSent = true;
            Plugin.Log.LogInfo("[HS-AP] Sending CLIENT_GOAL status.");
            _session.SetGoalAchieved();
            ApToastQueue.EnqueueInfo("Goal sent");
        }
        else
        {
            Plugin.Log.LogInfo("[HS-AP] Goal already sent — still running release/collect.");
        }

        try
        {
            BeginBurstQuiet(120f, "F9 release/collect — applying items gradually");
            Plugin.Log.LogInfo("[HS-AP] F9: !release");
            _session.Say("!release");
            Plugin.Log.LogInfo("[HS-AP] F9: !collect");
            _session.Say("!collect");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] F9 release/collect failed: {ex.Message}");
        }
    }

    private void OnItemReceived(ReceivedItemsHelper helper)
    {
        var n = 0;
        while (helper.Any())
        {
            var fromSync = _connectSyncActive;
            ProcessReceivedItem(helper.DequeueItem(), fromSync);
            n++;
        }

        try
        {
            var total = _session?.Items.AllItemsReceived.Count ?? 0;
            if (_connectSyncActive && _receiveOrdinal >= total)
            {
                _connectSyncActive = false;
                Plugin.Log.LogInfo(
                    $"[HS-AP] Connect item sync done ({_receiveOrdinal}/{total}); currency watermark={Plugin.Instance.CurrencyGrantCount.Value}.");
                if (_receiveOrdinal > 0)
                {
                    ApToastQueue.EnqueueInfo($"Synced {_receiveOrdinal} items");
                }
            }
        }
        catch
        {
            _connectSyncActive = false;
        }

        if (n > 0)
        {
            ScheduleConfigSave(InBurstQuiet ? 5f : 1.5f);
        }
    }

    private void ProcessReceivedItem(ItemInfo info, bool fromSync)
    {
        var ordinal = _receiveOrdinal++;
        var name = info.ItemName ?? $"item:{info.ItemId}";
        var from = info.Player.Name ?? "?";
        if (!InBurstQuiet)
        {
            Plugin.Log.LogInfo(
                $"[HS-AP] Received item: {name} (id={info.ItemId}, ord={ordinal}, sync={fromSync}) from {from}");
        }

        var skipCurrency = IsCurrencyFiller(name) && fromSync && ordinal < _currencyGrantBaseline;
        if (skipCurrency)
        {
            if (!InBurstQuiet)
            {
                Plugin.Log.LogInfo(
                    $"[HS-AP] Skipping currency re-grant '{name}' (ord={ordinal} < baseline {_currencyGrantBaseline}).");
            }
        }
        else
        {
            EnqueueApply(name, info.ItemId, from, toast: !fromSync);
        }

        AdvanceCurrencyGrantWatermark(ordinal + 1);
    }
}
