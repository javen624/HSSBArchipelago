using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace HardspaceShipbreaker.Archipelago;

/// <summary>
/// Best-effort item application via reflection into BBI.Unity.Game.
/// </summary>
internal static class ItemApplicator
{
    private static Assembly? _gameAsm;
    private static Type? _currencyControllerType;
    private static Type? _upgradeAssetType;
    private static Type? _upgradeServiceType;
    private static Type? _playerProfileType;
    private static Type? _playerProfileServiceType;
    private static Type? _shipClassAssetType;
    private static Type? _availableShipType;
    private static MethodInfo? _changeCurrency;
    private static MethodInfo? _unlockUpgrade;
    private static MethodInfo? _applyUpgrade;
    private static MethodInfo? _upgradeNameGetter;
    private static MethodInfo? _purchaseUpgrade;
    private static object? _cachedController;
    private static object? _creditsId;
    private static object? _ltId;
    private static readonly List<(string Kind, float Amount)> PendingCurrency = new();
    private static readonly List<string> PendingUpgradeKeys = new();
    private static readonly Dictionary<string, object> CachedShipClasses = new(StringComparer.OrdinalIgnoreCase);
    private static bool _suppressHabShopChecks;
    private static bool _habShopSanity = true;
    // Defaults match APWorld credit_pack_value=normal (1M / 3M / 8M).
    private static float _creditPackSmall = 1_000_000f;
    private static float _creditPackMedium = 3_000_000f;
    private static float _creditPackLarge = 8_000_000f;
    private static readonly HashSet<long> HabShopPaidLocationIds = new();
    /// <summary>Hab-bought (yellow) but not yet granted by an AP item.</summary>
    private static readonly HashSet<object> ShopOwnedPendingGrant = new(ReferenceEqualityComparer.Instance);
    /// <summary>Upgrades granted by AP Apply (buffs on) — may or may not be Hab-yellow.</summary>
    private static readonly HashSet<object> ApGrantedUpgrades = new(ReferenceEqualityComparer.Instance);

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        bool IEqualityComparer<object>.Equals(object? x, object? y) => ReferenceEquals(x, y);

        int IEqualityComparer<object>.GetHashCode(object obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
    private static bool _loggedUpgradeCatalog;
    private static bool _loggedCurrencyNoise;
    private static int _grappleStrengthCount;
    private static bool _tetherModule;
    private static bool _demoLicense;
    private static bool _chargedPush;
    private static bool _o2RechargeModule;
    private static bool _launcherUnlocked;
    private static bool _pendingDemoAutoDeploy;
    private static bool _pendingDemoRental;
    private static bool _pendingLauncherCryo;
    private static bool _pendingLauncherExplosive;
    private static bool _pendingLauncherMagnetic;
    private static int _certRankProgress;
    private static MethodInfo? _trySetCertification;
    private static MethodInfo? _getCertificationRank;
    private static Type? _certificationServiceType;
    private static IReadOnlyList<HabEquipmentCatalog.Entry>? _habEquipment;

    /// <summary>
    /// Progressive Certification Rank ×1..4 unlock milestones 5/10/15/20.
    /// Vanilla MP may fill ranks up to the ceiling (next milestone − 1) until the next PCR is found.
    /// </summary>
    private static readonly int[] CertRankMilestones = { 5, 10, 15, 20 };

    private static bool _suppressCertGate;
    private static bool _loggedCertGate;
    /// <summary>While set, HealStaleJobBoardRefreshCounter will not clear a pending F10 board regen.</summary>
    private static float _suppressBoardHealUntil;
    private static object? _boardRefreshCache;
    private static Coroutine? _boardRefreshCoroutine;
    private static Coroutine? _rankUpBoardRefreshCoroutine;
    /// <summary>When true, currency grants skip per-pack toasts (F9 collect bursts).</summary>
    private static bool _quietCurrencyGrants;
    /// <summary>
    /// After F9 collect / Finish Basic Training checked: vanilla DisplayTrainingShip still
    /// hides the catalogue while training PATs are met — keep forcing the real bay.
    /// </summary>
    private static bool _suppressTrainingShipUi;
    /// <summary>Skip auto board refresh from TrySetCertification while F10 drives its own full regen.</summary>
    private static bool _suppressCertRankBoardRefresh;

    public static void Initialize(Assembly gameAsm)
    {
        _gameAsm = gameAsm;
        _currencyControllerType = gameAsm.GetType("BBI.Unity.Game.CurrencyController");
        _changeCurrency = _currencyControllerType?.GetMethod(
            "ChangeCurrency",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        _upgradeAssetType = gameAsm.GetType("BBI.Unity.Game.UpgradeAsset");
        _upgradeServiceType = gameAsm.GetType("BBI.Unity.Game.UpgradeService");
        _playerProfileType = gameAsm.GetType("BBI.Unity.Game.PlayerProfile");
        _playerProfileServiceType = gameAsm.GetType("BBI.Unity.Game.PlayerProfileService");
        _shipClassAssetType = gameAsm.GetType("BBI.Unity.Game.ShipClassAsset");
        _availableShipType = gameAsm.GetType("BBI.Unity.Game.PlayerProfile+AvailableShip")
                             ?? _playerProfileType?.GetNestedType("AvailableShip", BindingFlags.Public | BindingFlags.NonPublic);

        _certificationServiceType = gameAsm.GetType("BBI.Unity.Game.CertificationService");
        _trySetCertification = _certificationServiceType?.GetMethod(
            "TrySetCertification",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(int), typeof(bool) },
            null);
        _getCertificationRank = _certificationServiceType?.GetMethod(
            "GetCertificationRank",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);

        _unlockUpgrade = _upgradeAssetType?.GetMethod("UnlockUpgrade", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _applyUpgrade = _upgradeAssetType?.GetMethod("ApplyUpgrade", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _upgradeNameGetter = _upgradeAssetType?.GetMethod("get_UpgradeName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                             ?? _upgradeAssetType?.GetProperty("UpgradeName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
        _purchaseUpgrade = _upgradeServiceType != null && _upgradeAssetType != null
            ? _upgradeServiceType.GetMethod(
                "PurchaseUpgrade",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { _upgradeAssetType },
                null)
            : null;

        Plugin.Log.LogInfo(
            $"[HS-AP] ItemApplicator ready (Currency={_changeCurrency != null}, Upgrade={_unlockUpgrade != null}, CertSet={_trySetCertification != null}, ProfileService={_playerProfileServiceType != null}, AvailableShip={_availableShipType != null})");
        _habEquipment = HabEquipmentCatalog.Build(NameEndsWithTier);
    }

    public static void RememberController(object? controller)
    {
        if (controller == null)
        {
            return;
        }

        _cachedController = controller;
        FlushPendingCurrency();
    }

    public static void ObserveCurrencyChange(object? controller, object? currencyAssetId, float amount, bool add)
    {
        RememberController(controller);
        if (currencyAssetId != null)
        {
            ClassifyCurrencyId(currencyAssetId);
        }

        if (!_loggedCurrencyNoise && _creditsId != null && _ltId != null)
        {
            _loggedCurrencyNoise = true;
            Plugin.Log.LogInfo("[HS-AP] CurrencyController live; Credits/LT AssetTypeIDs cached.");
        }
    }

    public static void OnSessionReady()
    {
        LogUpgradeCatalogOnce();
        FlushPendingCurrency();
        FlushPendingUpgrades();
        FlushPendingCertificationRank();
        ClampCertificationToCeiling();
        RepairInflatedWorkPermit();
        HealStaleJobBoardRefreshCounter();
        RepairNegativeMasteryPoints();
        EnsureHabShopPaidState();
        StripUnpaidShopRowsFromHabOwned();
        EnsureFreeStarterUpgradesOwned();
        AutoCheckFreeStarterHabLocations();
        ReapplyOwnedAbilityUnlocks();
        ReapplyApGrantedUpgrades();
        // Do not call TryRecoverEmptyJobBoard — nudging RemainingShiftsTillBoardRefresh=0
        // on empty RawLoadedAvailableShips wiped Hab ship select on fresh careers.
    }

    /// <summary>Hab / frontend return — refresh AP equipment state (no board unlock injection).</summary>
    public static void OnFrontendOrProfileTouch()
    {
        FlushPendingCertificationRank();
        ClampCertificationToCeiling();
        RepairInflatedWorkPermit();
        HealStaleJobBoardRefreshCounter();
        RepairNegativeMasteryPoints();
        EnsureHabShopPaidState();
        StripUnpaidShopRowsFromHabOwned();
        EnsureFreeStarterUpgradesOwned();
        AutoCheckFreeStarterHabLocations();
        ReapplyOwnedAbilityUnlocks();
    }

    /// <summary>Re-add StartsPurchased / free starters to Hab Upgrades if an earlier strip removed them.</summary>
    private static void EnsureFreeStarterUpgradesOwned()
    {
        try
        {
            foreach (var asset in FindUpgradeAssets())
            {
                if (!IsFreeStarterUpgrade(asset) || IsInHabOwnedUpgrades(asset))
                {
                    continue;
                }

                MarkHabOwnedYellowOnly(asset);
                Plugin.Log.LogInfo(
                    $"[HS-AP] Restored free starter Hab-owned: {GetUnityName(asset)}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] EnsureFreeStarterUpgradesOwned failed: {ex.Message}");
        }
    }

    /// <summary>Re-post ability unlocks after bay/Hab load so GrapplingHook picks them up.</summary>
    private static void ReapplyOwnedAbilityUnlocks()
    {
        if (_chargedPush)
        {
            TryUnlockGameAbility("GrapplePush");
            TryUnlockGameAbility("GrappleChargedPush");
        }

        if (_tetherModule)
        {
            TryUnlockGameAbility("GrappleTethers");
        }

        if (_demoLicense)
        {
            TryUnlockGameAbility("DemoCharge");
        }
    }

    /// <summary>PlayerProfile.UnlockAbility — posts UnlockAbilityEvent for live controllers.</summary>
    private static void TryUnlockGameAbility(string abilityEnumName)
    {
        try
        {
            var profile = FindPlayerProfile();
            if (profile == null || _gameAsm == null)
            {
                return;
            }

            var enumType = _gameAsm.GetType("BBI.Unity.Game.UnlockAbilityID");
            if (enumType == null)
            {
                Plugin.Log.LogWarning("[HS-AP] UnlockAbilityID enum missing.");
                return;
            }

            object ability;
            try
            {
                ability = Enum.Parse(enumType, abilityEnumName);
            }
            catch
            {
                Plugin.Log.LogWarning($"[HS-AP] Unknown UnlockAbilityID '{abilityEnumName}'.");
                return;
            }

            var unlock = profile.GetType().GetMethod(
                "UnlockAbility",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { enumType },
                null);
            if (unlock == null)
            {
                Plugin.Log.LogWarning("[HS-AP] PlayerProfile.UnlockAbility not found.");
                return;
            }

            unlock.Invoke(profile, new[] { ability });
            Plugin.Log.LogInfo($"[HS-AP] Unlocked ability {abilityEnumName}.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] UnlockAbility '{abilityEnumName}' failed: {ex.Message}");
        }
    }

    /// <summary>Highest rank vanilla MP / non-AP sets may reach given owned Progressive Cert Rank copies.</summary>
    public static int CertificationRankCeiling
    {
        get
        {
            if (_certRankProgress <= 0)
            {
                // Block milestone 5 until first Progressive Certification Rank.
                return CertRankMilestones[0] - 1;
            }

            if (_certRankProgress >= CertRankMilestones.Length)
            {
                return GetMaxCertificationRankSafe();
            }

            // After unlocking milestone N, can MP up to (next milestone − 1).
            return CertRankMilestones[_certRankProgress] - 1;
        }
    }

    public static int ProgressiveCertRankCount => _certRankProgress;

    /// <summary>
    /// Gate vanilla / non-AP certification changes. Target rank must be ≤ ceiling
    /// (milestones require Progressive Certification Rank).
    /// </summary>
    public static bool AllowCertificationTarget(int targetRank)
    {
        if (_suppressCertGate)
        {
            return true;
        }

        var ceiling = CertificationRankCeiling;
        if (targetRank <= ceiling)
        {
            return true;
        }

        if (!_loggedCertGate)
        {
            _loggedCertGate = true;
            Plugin.Log.LogInfo(
                $"[HS-AP] Cert gate: blocked rank {targetRank} (ceiling={ceiling}, Progressive Cert Rank ×{_certRankProgress}). Find the next Progressive Certification Rank to continue.");
        }

        return false;
    }

    public static int ReadCurrentCertificationRank()
    {
        try
        {
            var service = FindCertificationService();
            if (service != null && _getCertificationRank != null)
            {
                return Convert.ToInt32(_getCertificationRank.Invoke(service, null));
            }

            var profile = FindPlayerProfile();
            var rankProp = _playerProfileType?.GetProperty(
                "CurrentCertificationRank",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (profile != null && rankProp?.GetValue(profile) != null)
            {
                return Convert.ToInt32(rankProp.GetValue(profile));
            }
        }
        catch
        {
            // ignore
        }

        return 0;
    }

    /// <summary>F11 debug: grant one Progressive Certification Rank (raises MP ceiling) + refresh bay list.</summary>
    public static void DebugIncreaseProgressiveCertCap()
    {
        try
        {
            if (_certRankProgress >= CertRankMilestones.Length)
            {
                Plugin.Log.LogInfo(
                    $"[HS-AP] F11: Progressive Cert Rank already max (×{_certRankProgress}, ceiling={CertificationRankCeiling}).");
                ApToastQueue.EnqueueInfo($"PCR already max (×{_certRankProgress})");
                DebugRefreshAvailableShips();
                return;
            }

            _certRankProgress++;
            _loggedCertGate = false;
            var ceiling = CertificationRankCeiling;
            Plugin.Log.LogInfo(
                $"[HS-AP] F11 debug: Progressive Certification Rank now ×{_certRankProgress} — MP ceiling={ceiling}");
            ApToastQueue.EnqueueInfo($"PCR ×{_certRankProgress} (ceiling {ceiling})");
            DebugRefreshAvailableShips();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] F11 PCR bump failed: {ex.Message}");
        }
    }

    /// <summary>F10 debug: raise certification display rank by 1 if within Progressive Cert Rank ceiling.</summary>
    public static void DebugIncreaseCertificationRankByOne()
    {
        try
        {
            if (_trySetCertification == null)
            {
                Plugin.Log.LogWarning("[HS-AP] F10 ignored: CertificationService.TrySetCertification not found.");
                DebugRefreshAvailableShips();
                return;
            }

            var current = ReadCurrentCertificationRank();
            if (current < 1)
            {
                current = 1;
            }

            var target = current + 1;
            var ceiling = CertificationRankCeiling;
            if (!AllowCertificationTarget(target))
            {
                Plugin.Log.LogInfo(
                    $"[HS-AP] F10 blocked: rank {target} exceeds Progressive Cert ceiling {ceiling} (×{_certRankProgress}).");
                ApToastQueue.EnqueueInfo($"Cert blocked (ceiling {ceiling}) — refreshing bay");
                // Still refresh: recovers TRAINING-only / empty bay after F9 collect, etc.
                DebugRefreshAvailableShips();
                return;
            }

            var max = GetMaxCertificationRankSafe();
            if (max >= 1 && current >= max)
            {
                Plugin.Log.LogInfo($"[HS-AP] F10: already at max certification rank ({current}).");
                ApToastQueue.EnqueueInfo($"Cert rank already max ({current})");
                DebugRefreshAvailableShips();
                return;
            }

            // isDebug:true runs GrantSkippedLevelProgress (PATs). Then sync WorkPermit tiers + board.
            _suppressCertRankBoardRefresh = true;
            try
            {
                if (!TrySetCertificationDisplayRank(target, isDebug: true))
                {
                    Plugin.Log.LogWarning($"[HS-AP] F10: TrySetCertification({target}) returned false.");
                    ApToastQueue.EnqueueInfo($"Cert bump failed ({current}→{target})");
                    DebugRefreshAvailableShips();
                    return;
                }

                SyncCertificationServiceAfterDebugRankSet(target);
                ResetMasteryPointsToZero();
            }
            finally
            {
                _suppressCertRankBoardRefresh = false;
            }

            DebugRefreshAvailableShips();
            Plugin.Log.LogInfo($"[HS-AP] F10 debug: certification {current} → {target} (ceiling={ceiling})");
            ApToastQueue.EnqueueInfo($"Cert rank {current} → {target}");
        }
        catch (Exception ex)
        {
            _suppressCertRankBoardRefresh = false;
            Plugin.Log.LogWarning($"[HS-AP] F10 cert bump failed: {ex.Message}");
            DebugRefreshAvailableShips();
        }
    }

    /// <summary>
    /// Career rank-up (TryIncreaseCertification): vanilla only tops up a couple of new-class
    /// slots until the shift timer. Debounce then force a full board regen.
    /// </summary>
    public static void RequestFullJobBoardRefreshAfterRankUp()
    {
        if (_suppressCertRankBoardRefresh)
        {
            return;
        }

        if (_rankUpBoardRefreshCoroutine != null)
        {
            Plugin.Instance.StopCoroutine(_rankUpBoardRefreshCoroutine);
        }

        _rankUpBoardRefreshCoroutine = Plugin.Instance.StartCoroutine(CoDebouncedRankUpBoardRefresh());
    }

    private static IEnumerator CoDebouncedRankUpBoardRefresh()
    {
        // Collapse multi-rank skips / PAT storms into one full regen.
        yield return new WaitForSecondsRealtime(0.35f);
        _rankUpBoardRefreshCoroutine = null;
        Plugin.Log.LogInfo("[HS-AP] Rank-up → full job board refresh.");
        DebugRefreshAvailableShips();
    }

    /// <summary>
    /// TrySetCertification does not update mCurrentRankIndex / WorkPermit. Mirror TryIncreaseCertification's
    /// corporate-tier sync so the job board catalogue unlocks the right ship class.
    /// </summary>
    private static void SyncCertificationServiceAfterDebugRankSet(int displayRank)
    {
        try
        {
            var service = FindCertificationService();
            if (service == null || _certificationServiceType == null)
            {
                return;
            }

            var index = Math.Max(0, displayRank - 1);
            var rankIndexField = _certificationServiceType.GetField(
                "mCurrentRankIndex",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            rankIndexField?.SetValue(service, index);

            _certificationServiceType.GetMethod(
                    "RefreshCertificationCompletion",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null)
                ?.Invoke(service, null);

            Plugin.Log.LogInfo(
                $"[HS-AP] Synced CertificationService index={index} + RefreshCertificationCompletion (WorkPermit/tiers).");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] Cert tier sync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Full job-board regen via RefreshJobBoardShipsAsync (all slots for accessible classes).
    /// Do NOT use RefreshJobBoardShipsFromLoadedDataAsync — that clears the preview maps,
    /// rebuilds from RawLoaded, then wipes RawLoaded (empty bay after leave/reenter).
    /// </summary>
    public static void DebugRefreshAvailableShips()
    {
        try
        {
            var profile = FindPlayerProfile();
            if (profile != null && _playerProfileType != null)
            {
                // Remaining <= 0 → shouldRefreshAllShips (replace every slot, not a 1–2 ship top-up).
                var remainingProp = _playerProfileType.GetProperty(
                    "RemainingShiftsTillBoardRefresh",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (remainingProp != null && remainingProp.CanWrite)
                {
                    remainingProp.SetValue(profile, 0);
                }

                // Keep RankUps during regen so GetCurrentlyAccessibleShipClasses still treats
                // the previous ship class as highestDuringLastRefresh (fills old + new class).
                // ResetJobBoardRefreshCounter clears RankUps when the async path finishes.
            }

            // Prevent HealStale from undoing Remaining=0 while async generation runs.
            _suppressBoardHealUntil = Time.unscaledTime + 25f;

            var csType = _gameAsm?.GetType("BBI.Unity.Game.CertificationService");
            var cs = csType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            csType?.GetMethod(
                    "CacheShipClassUnlocks",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null)
                ?.Invoke(cs, null);

            // Drop stale previews so every slot is null and must regenerate (true full bay).
            ClearBoardPreviewMaps(profile);

            var jbu = _gameAsm?.GetType("BBI.Unity.Game.JobBoardUtils");
            var cacheType = _gameAsm?.GetType("BBI.Unity.Game.AddressableCache");
            var refresh = jbu?.GetMethod(
                "RefreshJobBoardShipsAsync",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                cacheType == null ? Type.EmptyTypes : new[] { cacheType },
                null);
            if (refresh == null || cacheType == null)
            {
                Plugin.Log.LogWarning("[HS-AP] Job board: RefreshJobBoardShipsAsync/AddressableCache missing.");
                return;
            }

            _boardRefreshCache = Activator.CreateInstance(cacheType);
            refresh.Invoke(null, new[] { _boardRefreshCache });
            Plugin.Log.LogInfo("[HS-AP] Job board: RefreshJobBoardShipsAsync started (full regen).");

            if (_boardRefreshCoroutine != null)
            {
                Plugin.Instance.StopCoroutine(_boardRefreshCoroutine);
            }

            _boardRefreshCoroutine = Plugin.Instance.StartCoroutine(CoFinishBoardRefresh());
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] Job board refresh failed: {ex.Message}");
        }
    }

    private static IEnumerator CoFinishBoardRefresh()
    {
        // Do not settle on the first ship — generation is async per slot. Wait until the
        // root JobBoardPreviewHandle finishes and the accessible class has a full slot set.
        for (var i = 0; i < 5; i++)
        {
            yield return null;
        }

        var expected = GetExpectedAccessibleBoardShipCount();
        var deadline = Time.unscaledTime + 25f;
        var stableFrames = 0;
        var lastCount = -1;
        var sawFull = false;

        while (Time.unscaledTime < deadline)
        {
            var handleDone = IsJobBoardPreviewHandleDone();
            var n = CountAccessibleClassBoardPreviews();
            var lower = CountAccessibleClassLowerHazardPreviews();
            var catalogue = n + lower;

            if (catalogue != lastCount)
            {
                lastCount = catalogue;
                stableFrames = 0;
            }
            else
            {
                stableFrames++;
            }

            // Prefer handle completion + full expected count; fall back to stable non-empty.
            if (handleDone && expected > 0 && catalogue >= expected)
            {
                sawFull = true;
                break;
            }

            if (handleDone && catalogue > 0 && stableFrames >= 8)
            {
                sawFull = catalogue >= Math.Max(1, expected);
                break;
            }

            if (!handleDone && expected > 0 && catalogue >= expected && stableFrames >= 12)
            {
                sawFull = true;
                break;
            }

            yield return null;
        }

        // One extra beat so late Completed callbacks can land before UI rebuild.
        yield return null;
        yield return null;

        EnsureJobBoardRefreshCounterHealthy();
        TryRefreshJobBoardScreenUi();

        var finalN = CountAccessibleClassBoardPreviews();
        var finalLower = CountAccessibleClassLowerHazardPreviews();
        var total = CountAllBoardPreviews();
        Plugin.Log.LogInfo(
            $"[HS-AP] Job board refresh settle: accessible={finalN}, lowerHazard={finalLower}, total={total}, expected={expected}, full={sawFull}.");
        ApToastQueue.EnqueueInfo(
            finalN + finalLower > 0
                ? $"Bay refreshed ({finalN + finalLower} ships)"
                : "Bay refresh — reopen Hab if empty");
        _boardRefreshCoroutine = null;
    }

    private static bool IsJobBoardPreviewHandleDone()
    {
        try
        {
            var jbu = _gameAsm?.GetType("BBI.Unity.Game.JobBoardUtils");
            var handle = jbu?.GetProperty(
                    "JobBoardPreviewHandle",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            if (handle == null)
            {
                return false;
            }

            var isDone = handle.GetType().GetProperty(
                    "IsDone",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(handle);
            return isDone is true;
        }
        catch
        {
            return false;
        }
    }

    private static int GetExpectedAccessibleBoardShipCount()
    {
        try
        {
            GetAccessibleShipClasses(out var current, out var lastRefresh);
            var n = 0;
            if (current != null)
            {
                n += ReadShipClassInt(current, "ShipsToGenerateInJobBoard");
                n += ReadShipClassInt(current, "NumShipsFromLowerHazardLevel");
            }

            if (lastRefresh != null && !ReferenceEquals(lastRefresh, current))
            {
                n += ReadShipClassInt(lastRefresh, "ShipsToGenerateInJobBoard");
                n += ReadShipClassInt(lastRefresh, "NumShipsFromLowerHazardLevel");
            }

            return n;
        }
        catch
        {
            return 0;
        }
    }

    private static int ReadShipClassInt(object shipClass, string propName)
    {
        try
        {
            var v = shipClass.GetType()
                .GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(shipClass);
            return Math.Max(0, Convert.ToInt32(v ?? 0));
        }
        catch
        {
            return 0;
        }
    }

    private static void GetAccessibleShipClasses(out object? currentHighest, out object? highestDuringLastRefresh)
    {
        currentHighest = null;
        highestDuringLastRefresh = null;
        try
        {
            var jbu = _gameAsm?.GetType("BBI.Unity.Game.JobBoardUtils");
            var method = jbu?.GetMethod(
                "GetCurrentlyAccessibleShipClasses",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                return;
            }

            var args = new object?[] { null, null };
            method.Invoke(null, args);
            currentHighest = args[0];
            highestDuringLastRefresh = args[1];
        }
        catch
        {
            // ignore
        }
    }

    private static int CountAccessibleClassLowerHazardPreviews()
    {
        try
        {
            var shipClass = GetCurrentAccessibleShipClass();
            if (shipClass == null)
            {
                return 0;
            }

            var profile = FindPlayerProfile();
            var map = _playerProfileType?
                .GetProperty(
                    "ShipClassToLowerHazardLevelPreviewsMap",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(profile) as IDictionary;
            if (map == null || !map.Contains(shipClass))
            {
                return 0;
            }

            return CountNonNullPreviews(map[shipClass] as Array);
        }
        catch
        {
            return 0;
        }
    }

    private static object? GetCurrentAccessibleShipClass()
    {
        GetAccessibleShipClasses(out var current, out _);
        return current;
    }

    private static int CountAccessibleClassBoardPreviews()
    {
        try
        {
            var shipClass = GetCurrentAccessibleShipClass();
            if (shipClass == null)
            {
                return 0;
            }

            var profile = FindPlayerProfile();
            var map = _playerProfileType?
                .GetProperty(
                    "ShipClassToAvailablePreviewsMap",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(profile) as IDictionary;
            if (map == null || !map.Contains(shipClass))
            {
                return 0;
            }

            return CountNonNullPreviews(map[shipClass] as Array);
        }
        catch
        {
            return 0;
        }
    }

    private static int CountAllBoardPreviews()
    {
        try
        {
            var profile = FindPlayerProfile();
            var map = _playerProfileType?
                .GetProperty(
                    "ShipClassToAvailablePreviewsMap",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(profile) as IDictionary;
            if (map == null)
            {
                return 0;
            }

            var n = 0;
            foreach (DictionaryEntry entry in map)
            {
                n += CountNonNullPreviews(entry.Value as Array);
            }

            return n;
        }
        catch
        {
            return 0;
        }
    }

    private static int CountNonNullPreviews(Array? arr)
    {
        if (arr == null)
        {
            return 0;
        }

        var n = 0;
        foreach (var item in arr)
        {
            if (item != null)
            {
                n++;
            }
        }

        return n;
    }

    private static void ClearBoardPreviewMaps(object? profile)
    {
        if (profile == null || _playerProfileType == null)
        {
            return;
        }

        try
        {
            foreach (var propName in new[]
                     {
                         "ShipClassToAvailablePreviewsMap",
                         "ShipClassToLowerHazardLevelPreviewsMap"
                     })
            {
                var map = _playerProfileType
                    .GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(profile) as IDictionary;
                map?.Clear();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] ClearBoardPreviewMaps failed: {ex.Message}");
        }
    }

    private static void EnsureJobBoardRefreshCounterHealthy()
    {
        try
        {
            var profile = FindPlayerProfile();
            if (profile == null || _playerProfileType == null)
            {
                return;
            }

            var remainingProp = _playerProfileType.GetProperty(
                "RemainingShiftsTillBoardRefresh",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var remaining = remainingProp != null
                ? Convert.ToInt32(remainingProp.GetValue(profile) ?? 0)
                : 0;

            // Full RefreshJobBoardShipsAsync should ResetJobBoardRefreshCounter; if Remaining is
            // still 0 the next Hab visit will try another wipe/regen and can show an empty bay.
            if (remaining <= 0)
            {
                _playerProfileType.GetMethod(
                        "ResetJobBoardRefreshCounter",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null)
                    ?.Invoke(profile, null);
                Plugin.Log.LogInfo("[HS-AP] ResetJobBoardRefreshCounter after board regen.");
            }

            var rankUpsProp = _playerProfileType.GetProperty(
                "RankUpsSinceLastBoardRefresh",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (rankUpsProp != null && rankUpsProp.CanWrite)
            {
                rankUpsProp.SetValue(profile, 0);
            }

            _suppressBoardHealUntil = 0f;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] EnsureJobBoardRefreshCounterHealthy failed: {ex.Message}");
        }
    }

    private static void TryRefreshJobBoardScreenUi()
    {
        try
        {
            var ctrlType = _gameAsm?.GetType("BBI.Unity.Game.JobBoardScreenController");
            if (ctrlType == null)
            {
                return;
            }

            var find = typeof(UnityEngine.Object)
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(m =>
                    m.Name == "FindObjectOfType"
                    && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 0);
            var ctrl = find?.MakeGenericMethod(ctrlType).Invoke(null, null);
            if (ctrl == null)
            {
                Plugin.Log.LogInfo("[HS-AP] Job board UI: screen not open — reopen Hab bay to see ships.");
                return;
            }

            // Full rebuild. DisplayTrainingShip (inside ShowJobBoard) may hide the catalogue;
            // Harmony postfix + ForceJobBoardCatalogueVisible restore it when appropriate.
            ctrlType.GetMethod(
                    "ShowJobBoard",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null)
                ?.Invoke(ctrl, null);

            ForceJobBoardCatalogueVisible(ctrl, CountAccessibleClassBoardPreviews());

            ctrlType.GetMethod(
                    "DisplayBoardRefreshInfo",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null)
                ?.Invoke(ctrl, null);

            Plugin.Log.LogInfo("[HS-AP] Job board UI rebuilt (ShowJobBoard + show catalogue).");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogInfo($"[HS-AP] Job board UI refresh skipped ({ex.Message}).");
        }
    }

    /// <summary>Call after F9 collect drain (or when Finish Basic Training is known checked).</summary>
    public static void MarkCareerBayReadyAfterCollect()
    {
        _suppressTrainingShipUi = true;
        EnsureTutorialMarkedComplete();
    }

    public static bool ShouldForceJobBoardCatalogue()
    {
        if (_suppressTrainingShipUi)
        {
            return true;
        }

        try
        {
            if (ReadTutorialCompletedFlag())
            {
                return true;
            }
        }
        catch
        {
            // ignore
        }

        var client = Plugin.Instance?.Client;
        return client != null && client.IsLocationChecked(ArchipelagoClient.BaseId + 100);
    }

    /// <summary>
    /// Vanilla DisplayTrainingShip hides catalogue + zeroes mCurrentlyAvailableShips when any
    /// training PAT is still met. Undo that so the Hab bay stays usable after AP collect / cert.
    /// </summary>
    public static void ForceJobBoardCatalogueVisible(object? jobBoardController, int availableShipCount)
    {
        if (jobBoardController == null)
        {
            return;
        }

        try
        {
            var ctrlType = jobBoardController.GetType();
            ctrlType.GetMethod(
                    "ShowTrainingShip",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(bool) },
                    null)
                ?.Invoke(jobBoardController, new object[] { false });

            ctrlType.GetMethod(
                    "ShowAvailableShipCards",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(bool) },
                    null)
                ?.Invoke(jobBoardController, new object[] { true });

            var countField = ctrlType.GetField(
                "mCurrentlyAvailableShips",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (countField != null && availableShipCount > 0)
            {
                countField.SetValue(jobBoardController, availableShipCount);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] ForceJobBoardCatalogueVisible failed: {ex.Message}");
        }
    }

    private static void EnsureTutorialMarkedComplete()
    {
        try
        {
            var profile = FindPlayerProfile();
            if (profile == null || _playerProfileType == null)
            {
                return;
            }

            var prop = _playerProfileType.GetProperty(
                "TutorialCompleted",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null || !prop.CanWrite)
            {
                return;
            }

            if (prop.GetValue(profile) is true)
            {
                return;
            }

            prop.SetValue(profile, true);
            Plugin.Log.LogInfo("[HS-AP] Set TutorialCompleted=true (career bay / post-collect).");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] EnsureTutorialMarkedComplete failed: {ex.Message}");
        }
    }

    private static bool ReadTutorialCompletedFlag()
    {
        var profile = FindPlayerProfile();
        if (profile == null || _playerProfileType == null)
        {
            return false;
        }

        var prop = _playerProfileType.GetProperty(
            "TutorialCompleted",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return prop?.GetValue(profile) is true;
    }

    private static readonly Dictionary<string, int> ProgressiveCounts = new(StringComparer.Ordinal);

    public static void Apply(string name, long itemId)
    {
        switch (name)
        {
            case "Tether Module":
                _tetherModule = true;
                TryApplyEquipmentTier("Tether Module");
                FlushItemsGatedBy("Tether Module");
                break;
            case "Demo Charge License":
                _demoLicense = true;
                TryApplyEquipmentTier("Demo Charge License");
                FlushItemsGatedBy("Demo Charge License");
                break;
            case "Charged Push":
                _chargedPush = true;
                // ApplyUpgrade alone often misses live GrapplingHook flags; unlock abilities directly.
                TryUnlockGameAbility("GrapplePush");
                TryUnlockGameAbility("GrappleChargedPush");
                if (!TryApplyEquipmentTier("Charged Push"))
                {
                    Plugin.Log.LogWarning(
                        "[HS-AP] Charged Push: no matching UpgradeAsset yet — abilities unlocked via UnlockAbilityID.");
                }

                FlushItemsGatedBy("Charged Push");
                break;
            case "Demo Auto-Deploy":
                if (!_demoLicense)
                {
                    _pendingDemoAutoDeploy = true;
                    Plugin.Log.LogInfo("[HS-AP] Demo Auto-Deploy queued until Demo Charge License is owned.");
                    break;
                }

                TryApplyEquipmentTier("Demo Auto-Deploy");
                break;
            case "Demo Charge Rental":
                if (!_demoLicense)
                {
                    _pendingDemoRental = true;
                    Plugin.Log.LogInfo("[HS-AP] Demo Charge Rental queued until Demo Charge License is owned.");
                    break;
                }

                TryApplyEquipmentTier("Demo Charge Rental");
                break;
            case "O2 Recharge Module":
                _o2RechargeModule = true;
                TryApplyEquipmentTier("O2 Recharge Module");
                FlushItemsGatedBy("O2 Recharge Module");
                break;
            case "Unlock Launcher":
                _launcherUnlocked = true;
                TryApplyEquipmentTier("Unlock Launcher");
                FlushItemsGatedBy("Unlock Launcher");
                break;
            case "Launcher Cryo":
                if (!_launcherUnlocked)
                {
                    _pendingLauncherCryo = true;
                    Plugin.Log.LogInfo("[HS-AP] Launcher Cryo queued until Unlock Launcher is owned.");
                    break;
                }

                TryApplyEquipmentTier("Launcher Cryo");
                break;
            case "Launcher Explosive":
                if (!_launcherUnlocked)
                {
                    _pendingLauncherExplosive = true;
                    Plugin.Log.LogInfo("[HS-AP] Launcher Explosive queued until Unlock Launcher is owned.");
                    break;
                }

                TryApplyEquipmentTier("Launcher Explosive");
                break;
            case "Launcher Magnetic":
                if (!_launcherUnlocked)
                {
                    _pendingLauncherMagnetic = true;
                    Plugin.Log.LogInfo("[HS-AP] Launcher Magnetic queued until Unlock Launcher is owned.");
                    break;
                }

                TryApplyEquipmentTier("Launcher Magnetic");
                break;
            case "Progressive Ship Unlock":
            case "Unlock Atlas":
            case "Unlock Javelin":
            case "Unlock Gecko":
                // Legacy receive only — not placed. Ship families use Progressive Cert Rank + vanilla cert.
                Plugin.Log.LogInfo($"[HS-AP] Ignoring legacy ship unlock '{name}' (PCR-only; bay uses vanilla cert).");
                break;
            case "Progressive Certification Rank":
                _certRankProgress++;
                _loggedCertGate = false;
                Plugin.Log.LogInfo(
                    $"[HS-AP] Progressive Certification Rank now ×{_certRankProgress} — MP ceiling={CertificationRankCeiling} (rank not changed; earn MP in Career to advance).");
                break;
            case "Unlock Mackerel":
                Plugin.Log.LogInfo("[HS-AP] Unlock Mackerel (start / logic).");
                break;
            case "Credit Pack (Small)":
                // Pays toward LYNX debt (DebtCurrency.Amount ↑ → displayed debt ↓).
                // Amounts from slot_data credit_pack_* (YAML credit_pack_value).
                TryAddCurrency("Debt", _creditPackSmall);
                break;
            case "Credit Pack (Medium)":
                TryAddCurrency("Debt", _creditPackMedium);
                break;
            case "Credit Pack (Large)":
                TryAddCurrency("Debt", _creditPackLarge);
                break;
            case "LYNX Token Pack (Small)":
                TryAddCurrency("LT", 5f);
                break;
            case "LYNX Token Pack (Medium)":
                TryAddCurrency("LT", 10f);
                break;
            case "Nothing":
                Plugin.Log.LogInfo("[HS-AP] Ignoring legacy Nothing filler (not placed in 0.5.1+).");
                break;
            case "Clone Fee Tax":
                Plugin.Log.LogInfo("[HS-AP] Trap: Clone Fee Tax — forcing a clone if possible.");
                DeathLinkHooks.ForceLocalDeathFromTrap();
                break;
            default:
                if (TryApplyProgressiveEquipment(name))
                {
                    break;
                }

                if (TryApplyEquipmentTier(name))
                {
                    break;
                }

                Plugin.Log.LogInfo($"[HS-AP] No applicator for '{name}' (id={itemId})");
                break;
        }
    }

    private static bool TryApplyProgressiveEquipment(string progressiveName)
    {
        if (!HabEquipmentCatalog.ProgressiveTiers.TryGetValue(progressiveName, out var tiers))
        {
            return false;
        }

        ProgressiveCounts.TryGetValue(progressiveName, out var count);
        count++;
        ProgressiveCounts[progressiveName] = count;
        Plugin.Log.LogInfo($"[HS-AP] {progressiveName} now ×{count}");

        if (string.Equals(progressiveName, "Progressive Grapple Strength", StringComparison.Ordinal))
        {
            _grappleStrengthCount = count;
        }

        if (count > tiers.Length)
        {
            Plugin.Log.LogInfo($"[HS-AP] {progressiveName} ×{count} exceeds tier list ({tiers.Length}); ignoring extra.");
            return true;
        }

        // First Progressive Demo / Tether / Charged Push also unlocks that tool's license.
        if (count == 1)
        {
            GrantLicenseFromFirstProgressive(progressiveName);
        }

        var license = LicenseRequiredForItem(progressiveName);
        if (license != null && !HasLicense(license))
        {
            Plugin.Log.LogInfo(
                $"[HS-AP] {progressiveName} ×{count} held until license '{license}' is owned (tier '{tiers[count - 1]}').");
            return true;
        }

        return TryApplyEquipmentTier(tiers[count - 1]);
    }

    /// <summary>
    /// Progressive Demo Charges / Tether Amount|Lifetime / Charged Push Force ×1 grants the matching license.
    /// </summary>
    private static void GrantLicenseFromFirstProgressive(string progressiveName)
    {
        switch (progressiveName)
        {
            case "Progressive Tether Amount":
            case "Progressive Tether Lifetime":
            case "Progressive Tethers":
                if (!_tetherModule)
                {
                    Plugin.Log.LogInfo($"[HS-AP] {progressiveName} ×1 grants Tether Module license.");
                    Apply("Tether Module", 0);
                }

                break;
            case "Progressive Demo Charges":
                if (!_demoLicense)
                {
                    Plugin.Log.LogInfo("[HS-AP] Progressive Demo Charges ×1 grants Demo Charge License.");
                    Apply("Demo Charge License", 0);
                }

                break;
            case "Progressive Charged Push Force":
                if (!_chargedPush)
                {
                    Plugin.Log.LogInfo("[HS-AP] Progressive Charged Push Force ×1 grants Charged Push.");
                    Apply("Charged Push", 0);
                }

                break;
            case "Progressive Launcher Range":
                if (!_launcherUnlocked)
                {
                    Plugin.Log.LogInfo("[HS-AP] Progressive Launcher Range ×1 grants Unlock Launcher.");
                    Apply("Unlock Launcher", 0);
                }

                break;
        }
    }

    private static string? LicenseRequiredForItem(string itemName) =>
        itemName switch
        {
            "Progressive Tether Amount" or "Progressive Tether Lifetime" or "Progressive Tethers"
                => "Tether Module",
            "Progressive Demo Charges" or "Progressive Demo Disarming" or "Progressive Demo Self Cleanup"
                or "Progressive Demo Durability" or "Demo Auto-Deploy" or "Demo Charge Rental"
                => "Demo Charge License",
            "Progressive Charged Push Force" => "Charged Push",
            "Progressive O2 Recharge" => "O2 Recharge Module",
            "Progressive Launcher Range" or "Launcher Cryo" or "Launcher Explosive" or "Launcher Magnetic"
                => "Unlock Launcher",
            _ => null
        };

    private static bool HasLicense(string license) =>
        license switch
        {
            "Tether Module" => _tetherModule,
            "Demo Charge License" => _demoLicense,
            "Charged Push" => _chargedPush,
            "O2 Recharge Module" => _o2RechargeModule,
            "Unlock Launcher" => _launcherUnlocked,
            _ => true
        };

    /// <summary>Apply any progressive/single upgrades that were waiting on this license.</summary>
    private static void FlushItemsGatedBy(string license)
    {
        foreach (var kv in HabEquipmentCatalog.ProgressiveTiers)
        {
            if (LicenseRequiredForItem(kv.Key) != license)
            {
                continue;
            }

            if (!ProgressiveCounts.TryGetValue(kv.Key, out var count) || count <= 0)
            {
                continue;
            }

            var tiers = kv.Value;
            var applyThrough = Math.Min(count, tiers.Length);
            Plugin.Log.LogInfo(
                $"[HS-AP] Flushing {kv.Key} ×{applyThrough} after license '{license}'.");
            for (var i = 0; i < applyThrough; i++)
            {
                TryApplyEquipmentTier(tiers[i]);
            }
        }

        if (license == "Demo Charge License" && _pendingDemoAutoDeploy)
        {
            _pendingDemoAutoDeploy = false;
            TryApplyEquipmentTier("Demo Auto-Deploy");
        }

        if (license == "Demo Charge License" && _pendingDemoRental)
        {
            _pendingDemoRental = false;
            TryApplyEquipmentTier("Demo Charge Rental");
        }

        if (license == "Unlock Launcher" && _pendingLauncherCryo)
        {
            _pendingLauncherCryo = false;
            TryApplyEquipmentTier("Launcher Cryo");
        }

        if (license == "Unlock Launcher" && _pendingLauncherExplosive)
        {
            _pendingLauncherExplosive = false;
            TryApplyEquipmentTier("Launcher Explosive");
        }

        if (license == "Unlock Launcher" && _pendingLauncherMagnetic)
        {
            _pendingLauncherMagnetic = false;
            TryApplyEquipmentTier("Launcher Magnetic");
        }
    }

    private static bool TryApplyEquipmentTier(string tierItemName)
    {
        _habEquipment ??= HabEquipmentCatalog.Build(NameEndsWithTier);
        foreach (var entry in _habEquipment)
        {
            if (!string.Equals(entry.ItemName, tierItemName, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(tierItemName, "Tether Module", StringComparison.Ordinal))
            {
                _tetherModule = true;
            }

            if (string.Equals(tierItemName, "Demo Charge License", StringComparison.Ordinal))
            {
                _demoLicense = true;
            }

            if (string.Equals(tierItemName, "Charged Push", StringComparison.Ordinal))
            {
                _chargedPush = true;
            }

            if (string.Equals(tierItemName, "Unlock Launcher", StringComparison.Ordinal))
            {
                _launcherUnlocked = true;
            }

            TryApplyUpgradeKey($"equip:{tierItemName}", entry.AssetMatch);
            return true;
        }

        return false;
    }

    private static bool TryApplyEquipmentItem(string name) => TryApplyEquipmentTier(name);

    public static bool HasTetherModule => _tetherModule;
    public static int GrappleStrengthCount => _grappleStrengthCount;
    public static bool HasDemoLicense => _demoLicense;
    public static bool HasChargedPush => _chargedPush;

    /// <summary>Identified narrative entries (= Hab-recovered data drives).</summary>
    public static int CountRecoveredDataDrives()
    {
        try
        {
            var profile = FindPlayerProfile();
            if (profile == null || _playerProfileType == null)
            {
                return 0;
            }

            var inv = _playerProfileType
                .GetProperty("NarrativeInventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(profile);
            if (inv == null)
            {
                return 0;
            }

            var identified = inv.GetType()
                .GetProperty("CollectedIdentifiedNarrativeEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(inv) as System.Collections.ICollection;
            var unidentified = inv.GetType()
                .GetProperty("CollectedUnidentifiedNarrativeEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(inv) as System.Collections.ICollection;
            return (identified?.Count ?? 0) + (unidentified?.Count ?? 0);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] CountRecoveredDataDrives failed: {ex.Message}");
            return 0;
        }
    }

    public static bool TryReadDebtPaidOff(out bool paid)
    {
        paid = false;
        var profile = FindPlayerProfile();
        if (profile == null || _playerProfileType == null)
        {
            return false;
        }

        var prop = _playerProfileType.GetProperty("DebtPaidOff", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop == null)
        {
            return false;
        }

        paid = prop.GetValue(profile) is true;
        return true;
    }

    private static void LogUpgradeCatalogOnce()
    {
        if (_loggedUpgradeCatalog)
        {
            return;
        }

        var assets = FindUpgradeAssets();
        if (assets.Count == 0)
        {
            return;
        }

        _loggedUpgradeCatalog = true;
        var byName = assets.Select(GetUnityName).Where(n => !string.IsNullOrWhiteSpace(n)).OrderBy(n => n).ToList();
        var tetherNamed = byName.Where(n => n.IndexOf("Tether", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        var grappleNamed = byName.Where(n => n.IndexOf("Grapple", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        var strengthNamed = byName.Where(IsGrappleStrengthAssetName).ToList();
        Plugin.Log.LogInfo(
            $"[HS-AP] Upgrade assets: total={assets.Count}, Tether*={tetherNamed.Count}, Grapple*={grappleNamed.Count}, GrappleStrength*={strengthNamed.Count}");
        if (tetherNamed.Count > 0)
        {
            Plugin.Log.LogInfo($"[HS-AP] Tether assets: {string.Join(" | ", tetherNamed.Take(20))}");
        }

        if (strengthNamed.Count > 0)
        {
            Plugin.Log.LogInfo($"[HS-AP] Grapple Strength assets: {string.Join(" | ", strengthNamed)}");
        }
        else
        {
            Plugin.Log.LogInfo($"[HS-AP] Grapple assets: {string.Join(" | ", grappleNamed.Take(20))}");
        }
    }

    private static bool IsGrappleStrengthAssetName(string n)
    {
        if (n.IndexOf("Grapple", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        if (n.IndexOf("Strength", StringComparison.OrdinalIgnoreCase) < 0
            && n.IndexOf("Force", StringComparison.OrdinalIgnoreCase) < 0
            && n.IndexOf("Pull", StringComparison.OrdinalIgnoreCase) < 0)
        {
            // Some builds use GrapplePower / GrappleMass
            if (n.IndexOf("Power", StringComparison.OrdinalIgnoreCase) < 0
                && n.IndexOf("Mass", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return n.IndexOf("Durability", StringComparison.OrdinalIgnoreCase) < 0
               && n.IndexOf("Drain", StringComparison.OrdinalIgnoreCase) < 0
               && n.IndexOf("Range", StringComparison.OrdinalIgnoreCase) < 0
               && n.IndexOf("Cooldown", StringComparison.OrdinalIgnoreCase) < 0
               && n.IndexOf("Purchase", StringComparison.OrdinalIgnoreCase) < 0
               && n.IndexOf("Rental", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static bool MatchTetherModule(string upgradeName)
    {
        var n = upgradeName;
        if (n.IndexOf("Tether", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        // UnlockTether*_UpgradeAsset / TetherModule*
        if (n.IndexOf("Unlock", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Module", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return n.IndexOf("Amount", StringComparison.OrdinalIgnoreCase) < 0
                   && n.IndexOf("Lifetime", StringComparison.OrdinalIgnoreCase) < 0
                   && n.IndexOf("Quantity", StringComparison.OrdinalIgnoreCase) < 0;
        }

        return false;
    }

    private static bool MatchTetherProgress(string upgradeName, int tier)
    {
        var n = upgradeName;
        if (n.IndexOf("Tether", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        if (n.IndexOf("Amount", StringComparison.OrdinalIgnoreCase) < 0
            && n.IndexOf("Lifetime", StringComparison.OrdinalIgnoreCase) < 0
            && n.IndexOf("Quantity", StringComparison.OrdinalIgnoreCase) < 0
            && n.IndexOf("Duration", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return NameEndsWithTier(n, tier);
    }

    private static bool MatchGrappleStrength(string upgradeName, int tier) =>
        IsGrappleStrengthAssetName(upgradeName) && NameEndsWithTier(upgradeName, tier);

    private static bool NameEndsWithTier(string n, int tier)
    {
        // Prefer explicit tier glued to a known upgrade family token:
        // GrappleStrength1_, HelmetTankCapacity2_, DurabilityDrain3_, Capacity1_
        if (System.Text.RegularExpressions.Regex.IsMatch(
                n,
                $@"(?:Strength|Capacity|Drain|Rate|Range|Amount|Lifetime|Integrity|Defence|Defense|Shield|Speed|Fuel|Heat|Cap|Cooldown|Disarm|Cleanup|Force|Brak\w*|Resist\w*|Resynth|TankCapacity|RechargeRate|Durability(?:Drain)?)[_\s\-]*{tier}(?![0-9])",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return true;
        }

        // GrappleStrength1_UpgradeAsset / *_3_UpgradeAsset
        if (System.Text.RegularExpressions.Regex.IsMatch(
                n,
                $@"(?<![0-9]){tier}(?=_Upgrade)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return true;
        }

        var roman = tier switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            5 => "V",
            _ => null
        };
        return roman != null && System.Text.RegularExpressions.Regex.IsMatch(
            n, $@"(?<![IVX]){roman}(?![IVX])(?:_|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static void TryApplyUpgradeKey(string key, Func<string, bool> nameMatch)
    {
        if (TryApplyMatchingUpgrade(nameMatch, key))
        {
            return;
        }

        if (!PendingUpgradeKeys.Contains(key))
        {
            PendingUpgradeKeys.Add(key);
        }

        Plugin.Log.LogInfo($"[HS-AP] Queued upgrade '{key}' until UpgradeAssets are loaded / session ready.");
    }

    private static void FlushPendingUpgrades()
    {
        if (PendingUpgradeKeys.Count == 0)
        {
            return;
        }

        var pending = PendingUpgradeKeys.ToList();
        PendingUpgradeKeys.Clear();
        foreach (var key in pending)
        {
            Func<string, bool> match = key switch
            {
                "tether_module" => MatchTetherModule,
                "demo_license" => n =>
                    n.IndexOf("Demo", StringComparison.OrdinalIgnoreCase) >= 0
                    && (n.IndexOf("License", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Charge", StringComparison.OrdinalIgnoreCase) >= 0),
                _ when key.StartsWith("grapple_strength_", StringComparison.Ordinal) =>
                    name => int.TryParse(key["grapple_strength_".Length..], out var tier) && MatchGrappleStrength(name, tier),
                _ when key.StartsWith("tether_prog_", StringComparison.Ordinal) =>
                    name => int.TryParse(key["tether_prog_".Length..], out var tier) && MatchTetherProgress(name, tier),
                _ when key.StartsWith("equip:", StringComparison.Ordinal) =>
                    ResolveEquipmentMatch(key["equip:".Length..]) ?? (_ => false),
                _ => _ => false
            };

            if (!TryApplyMatchingUpgrade(match, key))
            {
                PendingUpgradeKeys.Add(key);
            }
        }
    }

    private static bool TryApplyMatchingUpgrade(Func<string, bool> nameMatch, string key)
    {
        var assets = FindUpgradeAssets();
        if (assets.Count == 0)
        {
            return false;
        }

        // Prefer Unity asset-name patterns — BuffableCategory.Tethers is unused on this build (count=0).
        if (key == "tether_module")
        {
            var tether = assets
                .Where(a => MatchTetherModule(GetUnityName(a)))
                .OrderBy(GetRequiredTier)
                .ThenBy(GetUnityName)
                .FirstOrDefault()
                ?? assets.FirstOrDefault(a =>
                {
                    try
                    {
                        var f = a.GetType().GetField("m_RefillTethers", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        return f?.GetValue(a) is true && GetPreviousUpgrade(a) == null;
                    }
                    catch
                    {
                        return false;
                    }
                });
            if (tether != null)
            {
                return UnlockAndApply(tether, DescribeUpgrade(tether));
            }
        }

        if (key.StartsWith("grapple_strength_", StringComparison.Ordinal)
            && int.TryParse(key["grapple_strength_".Length..], out var gTier))
        {
            var strength = assets
                .Where(a => IsGrappleStrengthAssetName(GetUnityName(a)))
                .OrderBy(GetRequiredTier)
                .ThenBy(GetUnityName)
                .ToList();

            var byTier = strength.FirstOrDefault(a => MatchGrappleStrength(GetUnityName(a), gTier));
            if (byTier != null)
            {
                return UnlockAndApply(byTier, DescribeUpgrade(byTier));
            }

            if (gTier >= 1 && gTier <= strength.Count)
            {
                var pick = strength[gTier - 1];
                return UnlockAndApply(pick, DescribeUpgrade(pick));
            }

            Plugin.Log.LogWarning(
                $"[HS-AP] No Grapple Strength asset for tier {gTier}. Known: {string.Join(", ", strength.Select(GetUnityName))}");
        }

        if (key.StartsWith("tether_prog_", StringComparison.Ordinal)
            && int.TryParse(key["tether_prog_".Length..], out var tetherTier))
        {
            var tetherUps = assets
                .Where(a => MatchTetherProgress(GetUnityName(a), tetherTier)
                            || (GetUnityName(a).IndexOf("Tether", StringComparison.OrdinalIgnoreCase) >= 0
                                && (GetUnityName(a).IndexOf("Amount", StringComparison.OrdinalIgnoreCase) >= 0
                                    || GetUnityName(a).IndexOf("Lifetime", StringComparison.OrdinalIgnoreCase) >= 0)))
                .OrderBy(GetRequiredTier)
                .ThenBy(GetUnityName)
                .ToList();

            // If tier-specific filter emptied the list, use all amount/lifetime ordered.
            if (tetherUps.Count == 0)
            {
                tetherUps = assets
                    .Where(a =>
                    {
                        var n = GetUnityName(a);
                        return n.IndexOf("Tether", StringComparison.OrdinalIgnoreCase) >= 0
                               && (n.IndexOf("Amount", StringComparison.OrdinalIgnoreCase) >= 0
                                   || n.IndexOf("Lifetime", StringComparison.OrdinalIgnoreCase) >= 0
                                   || n.IndexOf("Quantity", StringComparison.OrdinalIgnoreCase) >= 0);
                    })
                    .OrderBy(GetRequiredTier)
                    .ThenBy(GetUnityName)
                    .ToList();
            }

            var byTier = tetherUps.FirstOrDefault(a => MatchTetherProgress(GetUnityName(a), tetherTier));
            if (byTier != null)
            {
                return UnlockAndApply(byTier, DescribeUpgrade(byTier));
            }

            if (tetherTier >= 1 && tetherTier <= tetherUps.Count)
            {
                var pick = tetherUps[tetherTier - 1];
                return UnlockAndApply(pick, DescribeUpgrade(pick));
            }
        }

        if (key == "demo_license")
        {
            var demo = assets
                .Where(a =>
                {
                    var n = GetUnityName(a);
                    return n.IndexOf("UnlockDemo", StringComparison.OrdinalIgnoreCase) >= 0
                           || (n.IndexOf("Demo", StringComparison.OrdinalIgnoreCase) >= 0
                               && n.IndexOf("Unlock", StringComparison.OrdinalIgnoreCase) >= 0);
                })
                .OrderBy(GetRequiredTier)
                .FirstOrDefault()
                ?? assets.Where(a => GetEquipmentCategory(a) == "DemoCharge")
                    .OrderBy(GetRequiredTier)
                    .FirstOrDefault();
            if (demo != null)
            {
                return UnlockAndApply(demo, DescribeUpgrade(demo));
            }
        }

        foreach (var asset in assets)
        {
            var n = GetUpgradeName(asset);
            var unity = GetUnityName(asset);
            if (nameMatch(n) || nameMatch(unity))
            {
                return UnlockAndApply(asset, DescribeUpgrade(asset));
            }
        }

        return false;
    }

    private static Func<string, bool>? ResolveEquipmentMatch(string itemName)
    {
        _habEquipment ??= HabEquipmentCatalog.Build(NameEndsWithTier);
        foreach (var entry in _habEquipment)
        {
            if (string.Equals(entry.ItemName, itemName, StringComparison.Ordinal))
            {
                return entry.AssetMatch;
            }
        }

        return null;
    }

    /// <summary>Map a Hab UpgradeAsset to an AP shop location, if any.</summary>
    public static bool TryMapHabShopLocation(object? upgradeAsset, out long locationId, out string locationName)
    {
        locationId = 0;
        locationName = "";
        if (upgradeAsset == null)
        {
            return false;
        }

        // Free / StartsPurchased starters are never shop-sanity locations.
        if (IsFreeStarterUpgrade(upgradeAsset))
        {
            return false;
        }

        var n = GetUnityName(upgradeAsset);
        if (string.IsNullOrEmpty(n) || HabEquipmentCatalog.IsFreeStarterUpgradeName(n))
        {
            return false;
        }

        _habEquipment ??= HabEquipmentCatalog.Build(NameEndsWithTier);
        foreach (var entry in _habEquipment)
        {
            if (!entry.AssetMatch(n))
            {
                continue;
            }

            locationId = entry.LocationId;
            locationName = entry.LocationName;
            return true;
        }

        // No catch-all fallback — unmapped upgrades (rentals, durability, etc.) use vanilla purchase.
        return false;
    }

    public static bool IsFreeStarterUpgrade(object? upgradeAsset)
    {
        if (upgradeAsset == null)
        {
            return false;
        }

        try
        {
            var starts = upgradeAsset.GetType().GetProperty(
                "StartsPurchased",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (starts?.GetValue(upgradeAsset) is true)
            {
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return HabEquipmentCatalog.IsFreeStarterUpgradeName(GetUnityName(upgradeAsset));
    }

    public static void SetHabShopSanity(bool enabled) => _habShopSanity = enabled;

    public static void SetCreditPackAmounts(float small, float medium, float large)
    {
        _creditPackSmall = small > 0f ? small : 1_000_000f;
        _creditPackMedium = medium > 0f ? medium : 3_000_000f;
        _creditPackLarge = large > 0f ? large : 8_000_000f;
        Plugin.Log.LogInfo(
            $"[HS-AP] Credit packs: Small={_creditPackSmall:N0} Medium={_creditPackMedium:N0} Large={_creditPackLarge:N0}");
    }

    public static bool HabShopSanityEnabled => _habShopSanity;

    /// <summary>
    /// Hab shop-sanity: only block re-buy after an actual Hab purchase (yellow),
    /// not merely because the AP location was checked via release/F9.
    /// </summary>
    public static bool IsUpgradePurchaseBlocked(object? upgradeAsset)
    {
        if (!_habShopSanity || upgradeAsset == null || _suppressHabShopChecks)
        {
            return false;
        }

        if (!TryMapHabShopLocation(upgradeAsset, out var id, out _))
        {
            return false;
        }

        // Explicit Hab buy this session / prior MarkHabOwnedYellow.
        if (HabShopPaidLocationIds.Contains(id) || ShopOwnedPendingGrant.Contains(upgradeAsset))
        {
            return true;
        }

        // Persisted Hab yellow in profile (bought earlier, saved).
        return IsInHabOwnedUpgrades(upgradeAsset);
    }

    /// <summary>
    /// Shop-sanity CanPurchase: keep cert-rank, previous-upgrade, and price gates.
    /// Ignore AP-only ownership (Apply without Hab buy) so rows stay purchasable when rank allows.
    /// Sets <paramref name="purchaseResult"/> to match vanilla UpgradePurchaseResult
    /// (InvalidCertification=1 drives the Hab rank-lock badge).
    /// </summary>
    public static bool TryEvaluateHabShopCanPurchase(
        object? upgradeAsset,
        out bool canPurchase,
        out int purchaseResult)
    {
        canPurchase = false;
        purchaseResult = UpgradePurchaseAlreadyHas; // 0
        if (!_habShopSanity || upgradeAsset == null || _suppressHabShopChecks)
        {
            return false;
        }

        if (!TryMapHabShopLocation(upgradeAsset, out _, out _))
        {
            return false;
        }

        // Already bought in Hab → not purchasable (yellow).
        if (IsUpgradePurchaseBlocked(upgradeAsset))
        {
            canPurchase = false;
            purchaseResult = UpgradePurchaseAlreadyHas;
            return true;
        }

        try
        {
            var required = GetRequiredTier(upgradeAsset);
            var rank = ReadCurrentCertificationRank();
            if (rank < required)
            {
                canPurchase = false;
                purchaseResult = UpgradePurchaseInvalidCertification;
                return true;
            }

            if (!IsPreviousUpgradeSatisfiedForShop(upgradeAsset))
            {
                canPurchase = false;
                purchaseResult = UpgradePurchaseInvalidOrder;
                return true;
            }

            if (!CanAffordUpgrade(upgradeAsset))
            {
                canPurchase = false;
                purchaseResult = UpgradePurchaseInsufficientCredits;
                return true;
            }

            canPurchase = true;
            purchaseResult = UpgradePurchaseSuccess;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] TryEvaluateHabShopCanPurchase failed: {ex.Message}");
            return false;
        }
    }

    // BBI.Unity.Game.UpgradeService.UpgradePurchaseResult
    private const int UpgradePurchaseAlreadyHas = 0;
    private const int UpgradePurchaseInvalidCertification = 1;
    private const int UpgradePurchaseInvalidOrder = 2;
    private const int UpgradePurchaseInsufficientCredits = 3;
    private const int UpgradePurchaseSuccess = 4;

    private static bool IsPreviousUpgradeSatisfiedForShop(object upgradeAsset)
    {
        var prevField = upgradeAsset.GetType().GetField(
            "PreviousUpgrade",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var prev = prevField?.GetValue(upgradeAsset);
        if (prev == null || prev is UnityEngine.Object unityObj && unityObj == null)
        {
            return true;
        }

        // Hab yellow, AP grant, or prior Hab shop check/purchase all count as unlocking the chain.
        if (IsInHabOwnedUpgrades(prev) || ApGrantedUpgrades.Contains(prev) || ShopOwnedPendingGrant.Contains(prev))
        {
            return true;
        }

        return TryMapHabShopLocation(prev, out var prevId, out _) && HabShopPaidLocationIds.Contains(prevId);
    }

    private static bool IsInHabOwnedUpgrades(object upgradeAsset)
    {
        try
        {
            var profile = FindPlayerProfile();
            if (profile == null || _playerProfileType == null)
            {
                return false;
            }

            var upgradesProp = _playerProfileType.GetProperty(
                "Upgrades",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var upgrades = upgradesProp?.GetValue(profile);
            var contains = upgrades?.GetType().GetMethod("Contains", new[] { upgradeAsset.GetType() })
                           ?? upgrades?.GetType().GetMethods()
                               .FirstOrDefault(m => m.Name == "Contains" && m.GetParameters().Length == 1);
            return contains?.Invoke(upgrades, new[] { upgradeAsset }) is true;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanAffordUpgrade(object upgradeAsset)
    {
        try
        {
            var priceProp = upgradeAsset.GetType().GetProperty(
                "Price",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var prices = priceProp?.GetValue(upgradeAsset) as Array;
            if (prices == null || prices.Length == 0)
            {
                return true;
            }

            var profile = FindPlayerProfile();
            var currencyController = _playerProfileType
                ?.GetProperty("CurrencyController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(profile);
            var currencies = currencyController?.GetType()
                .GetProperty("Currencies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(currencyController) as IDictionary;
            if (currencies == null)
            {
                return true;
            }

            foreach (var priceObj in prices)
            {
                if (priceObj == null)
                {
                    continue;
                }

                var amount = Convert.ToSingle(
                    priceObj.GetType().GetProperty("Amount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(priceObj) ?? 0);
                var currencyAsset = priceObj.GetType()
                    .GetProperty("CurrencyAsset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(priceObj);
                var id = currencyAsset?.GetType()
                    .GetProperty("ID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(currencyAsset);
                if (id == null || !currencies.Contains(id))
                {
                    return false;
                }

                var instance = currencies[id];
                var have = Convert.ToSingle(
                    instance?.GetType().GetProperty("Amount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(instance) ?? 0);
                if (have < amount)
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// No longer marks Hab rows paid from AP checked locations. Release/F9 checks Hab
    /// locations without a Hab buy — those must stay rank-gated and purchasable.
    /// Hab-paid state comes from real Hab purchases (Upgrades yellow / HabShopPaidLocationIds).
    /// </summary>
    public static void SyncHabShopPaidFromChecked(IEnumerable<long> checkedLocationIds)
    {
        if (!_habShopSanity)
        {
            return;
        }

        var checkedHab = 0;
        foreach (var id in checkedLocationIds)
        {
            if (HabEquipmentCatalog.IsHabShopLocationId(id))
            {
                checkedHab++;
            }
        }

        Plugin.Log.LogInfo(
            $"[HS-AP] Hab shop: {checkedHab} location(s) already checked on server; " +
            "purchase still follows cert rank / Hab buy (not auto-locked by release).");
    }

    /// <summary>
    /// Shop-sanity purchase: charge once, mark Hab-yellow (owned), do NOT grant buffs/abilities.
    /// Returns true if the original PurchaseUpgrade should be skipped; caller sends the location check.
    /// </summary>
    public static bool TryHandleHabShopSanityPurchase(
        object? upgradeAsset,
        out long locationId,
        out string locationName)
    {
        locationId = 0;
        locationName = "";
        if (!_habShopSanity || upgradeAsset == null || _suppressHabShopChecks)
        {
            return false;
        }

        if (!TryMapHabShopLocation(upgradeAsset, out locationId, out locationName))
        {
            return false;
        }

        if (!HabShopPaidLocationIds.Contains(locationId))
        {
            TryChargeUpgradePrice(upgradeAsset);
            HabShopPaidLocationIds.Add(locationId);
            HabShopPaidStore.Remember(locationId);
        }

        MarkHabOwnedYellowOnly(upgradeAsset);
        Plugin.Log.LogInfo(
            $"[HS-AP] Hab shop-sanity '{locationName}' — yellow/owned only (no equipment grant).");
        // First paid Hab equipment purchase also clears the milestone location.
        NotifyFirstEquipmentPurchase();
        return true;
    }

    /// <summary>
    /// Reload persisted Hab buys, seed from profile Upgrades (migration), restore yellow rows,
    /// then callers may Strip unpaid AP-only rows safely.
    /// </summary>
    public static void EnsureHabShopPaidState() => EnsureHabShopPaidState(habCheckedOnServer: -1);

    /// <param name="habCheckedOnServer">
    /// Count of Hab shop locations checked on the AP server. 0 means a fresh multiworld —
    /// do not restore ghost Hab-yellow from a previous seed. Pass -1 when unknown.
    /// </param>
    public static void EnsureHabShopPaidState(int habCheckedOnServer)
    {
        if (!_habShopSanity)
        {
            return;
        }

        try
        {
            HabShopPaidStore.EnsureLoaded();

            if (habCheckedOnServer == 0)
            {
                var clearedYellow = ClearHabShopYellowExceptFreeStarters();
                HabShopPaidStore.ClearPaid();
                HabShopPaidLocationIds.Clear();
                Plugin.Log.LogInfo(
                    $"[HS-AP] Fresh AP room (0 Hab checks) — cleared paid store + {clearedYellow} yellow row(s).");
                return;
            }

            // Rebuild from persisted store so a room/slot key switch cannot leave stale IDs.
            HabShopPaidStore.PurgeNonHabLocationIds();
            HabShopPaidLocationIds.Clear();
            HabShopPaidStore.CopyInto(HabShopPaidLocationIds);

            // Only migrate profile→paid when this room already has Hab checks (real progress).
            if (habCheckedOnServer > 0 || HabShopPaidLocationIds.Count > 0)
            {
                var seeded = SeedHabShopPaidFromProfileUpgrades();
                if (seeded > 0)
                {
                    HabShopPaidStore.RememberMany(HabShopPaidLocationIds);
                }
            }

            RestoreHabPaidYellowRows();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] EnsureHabShopPaidState failed: {ex.Message}");
        }
    }

    private static int SeedHabShopPaidFromProfileUpgrades()
    {
        var profile = FindPlayerProfile();
        if (profile == null || _playerProfileType == null)
        {
            return 0;
        }

        var upgradesProp = _playerProfileType.GetProperty(
            "Upgrades",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (upgradesProp?.GetValue(profile) is not IEnumerable enumerable)
        {
            return 0;
        }

        var added = 0;
        foreach (var entry in enumerable)
        {
            if (entry == null || IsFreeStarterUpgrade(entry))
            {
                continue;
            }

            if (!TryMapHabShopLocation(entry, out var id, out _))
            {
                continue;
            }

            if (HabShopPaidLocationIds.Add(id))
            {
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// Remove Hab shop-mapped rows from PlayerProfile.Upgrades (keep free starters).
    /// Used when switching AP seed so a prior multiworld's yellow rows do not leak.
    /// </summary>
    public static int ClearHabShopYellowExceptFreeStarters()
    {
        try
        {
            var profile = FindPlayerProfile();
            if (profile == null || _playerProfileType == null)
            {
                return 0;
            }

            var upgradesProp = _playerProfileType.GetProperty(
                "Upgrades",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var upgrades = upgradesProp?.GetValue(profile);
            if (upgrades is not IEnumerable enumerable)
            {
                return 0;
            }

            var toRemove = new List<object>();
            foreach (var entry in enumerable)
            {
                if (entry == null || IsFreeStarterUpgrade(entry))
                {
                    continue;
                }

                if (TryMapHabShopLocation(entry, out _, out _))
                {
                    toRemove.Add(entry);
                }
            }

            if (toRemove.Count == 0)
            {
                return 0;
            }

            var remove = upgrades.GetType().GetMethod("Remove", new[] { toRemove[0].GetType() })
                         ?? upgrades.GetType().GetMethods()
                             .FirstOrDefault(m => m.Name == "Remove" && m.GetParameters().Length == 1);
            foreach (var asset in toRemove)
            {
                remove?.Invoke(upgrades, new[] { asset });
                ShopOwnedPendingGrant.Remove(asset);
            }

            HabShopPaidLocationIds.Clear();
            Plugin.Log.LogInfo(
                $"[HS-AP] Cleared {toRemove.Count} Hab shop yellow row(s) after AP room/seed change.");
            return toRemove.Count;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] ClearHabShopYellowExceptFreeStarters failed: {ex.Message}");
            return 0;
        }
    }

    private static int RestoreHabPaidYellowRows()
    {
        if (HabShopPaidLocationIds.Count == 0)
        {
            return 0;
        }

        var assets = FindUpgradeAssets();
        if (assets.Count == 0)
        {
            return 0;
        }

        var byLoc = new Dictionary<long, object>();
        foreach (var asset in assets)
        {
            if (!TryMapHabShopLocation(asset, out var id, out _))
            {
                continue;
            }

            if (!byLoc.ContainsKey(id))
            {
                byLoc[id] = asset;
            }
        }

        var restored = 0;
        foreach (var id in HabShopPaidLocationIds.ToList())
        {
            if (!byLoc.TryGetValue(id, out var asset))
            {
                continue;
            }

            if (IsInHabOwnedUpgrades(asset))
            {
                continue;
            }

            MarkHabOwnedYellowOnly(asset);
            restored++;
        }

        return restored;
    }

    private static bool _firstEquipmentPurchaseChecked;

    /// <summary>Hab: Purchase First Equipment Upgrade (offset 212).</summary>
    public static void NotifyFirstEquipmentPurchase()
    {
        if (_firstEquipmentPurchaseChecked)
        {
            return;
        }

        _firstEquipmentPurchaseChecked = true;
        GameHooks.SendHabShopCheck(ArchipelagoClient.BaseId + 212, "Hab: Purchase First Equipment Upgrade");
    }

    /// <summary>
    /// Free starter upgrades that are also Hab locations (e.g. Scanner Objects if StartsPurchased)
    /// get auto-checked so they are not stuck unpurchasable in the pool.
    /// </summary>
    public static void AutoCheckFreeStarterHabLocations()
    {
        try
        {
            foreach (var asset in FindUpgradeAssets())
            {
                if (!IsFreeStarterUpgrade(asset))
                {
                    continue;
                }

                // Temporarily allow name match for auto-check of known free Hab locs.
                var n = GetUnityName(asset);
                if (HasScannerObjectsName(n))
                {
                    GameHooks.SendHabShopCheck(
                        ArchipelagoClient.BaseId + 213,
                        "Hab: Scanner Objects");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] AutoCheckFreeStarterHabLocations failed: {ex.Message}");
        }
    }

    private static bool HasScannerObjectsName(string n) =>
        n.IndexOf("ScannerMode_Objects", StringComparison.OrdinalIgnoreCase) >= 0
        || (n.IndexOf("Scanner", StringComparison.OrdinalIgnoreCase) >= 0
            && n.IndexOf("Object", StringComparison.OrdinalIgnoreCase) >= 0
            && n.IndexOf("Structure", StringComparison.OrdinalIgnoreCase) < 0
            && n.IndexOf("System", StringComparison.OrdinalIgnoreCase) < 0);

    /// <summary>
    /// Block vanilla ApplyUpgrade for Hab shop-mapped assets until an AP item grants them.
    /// Leaves StartsPurchased (tutorial) upgrades alone.
    /// </summary>
    public static bool ShouldBlockUpgradeApply(object? upgradeAsset)
    {
        if (!_habShopSanity || upgradeAsset == null || _suppressHabShopChecks)
        {
            return false;
        }

        if (ApGrantedUpgrades.Contains(upgradeAsset))
        {
            return false;
        }

        try
        {
            var starts = upgradeAsset.GetType().GetProperty(
                "StartsPurchased",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (starts?.GetValue(upgradeAsset) is true)
            {
                return false;
            }
        }
        catch
        {
            // ignore
        }

        return TryMapHabShopLocation(upgradeAsset, out _, out _);
    }

    /// <summary>
    /// Mark purchased in Hab UI (PlayerProfile.Upgrades) without UnlockUpgrade/ApplyUpgrade.
    /// UnlockUpgrade would grant abilities; Apply would grant buffs.
    /// </summary>
    private static void MarkHabOwnedYellowOnly(object upgradeAsset)
    {
        try
        {
            var profile = FindPlayerProfile();
            if (profile == null || _playerProfileType == null)
            {
                Plugin.Log.LogWarning("[HS-AP] Cannot mark Hab-yellow: no PlayerProfile.");
                return;
            }

            var upgradesProp = _playerProfileType.GetProperty(
                "Upgrades",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var upgrades = upgradesProp?.GetValue(profile);
            if (upgrades == null)
            {
                return;
            }

            var add = upgrades.GetType().GetMethod("Add", new[] { upgradeAsset.GetType() })
                      ?? upgrades.GetType().GetMethod("Add", new[] { typeof(object) })
                      ?? upgrades.GetType().GetMethods()
                          .FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1);
            add?.Invoke(upgrades, new[] { upgradeAsset });
            ShopOwnedPendingGrant.Add(upgradeAsset);

            // Nudge Hab UI the same way PurchaseUpgrade does after paying.
            try
            {
                var gameAsm = _gameAsm ?? FindGameAssemblyFallback();
                var evType = gameAsm?.GetType("BBI.Unity.Game.UpgradePurchasedEvent");
                var getEvent = evType?.GetMethod(
                    "GetEvent",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { upgradeAsset.GetType() },
                    null);
                var ev = getEvent?.Invoke(null, new[] { upgradeAsset });
                if (ev != null)
                {
                    PostEvent(ev);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HS-AP] UpgradePurchasedEvent post failed: {ex.Message}");
            }

            // Vanilla PurchaseUpgrade also posts UpgradePurchasedPAT — bay vending
            // (tether/demo refill) gates on that PAT history entry.
            EnsureUpgradePurchasedPatRecorded(upgradeAsset);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] MarkHabOwnedYellowOnly failed: {ex.Message}");
        }
    }

    private static Assembly? FindGameAssemblyFallback()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name == "BBI.Unity.Game")
            {
                return asm;
            }
        }

        return null;
    }

    private static void PostEvent(object ev)
    {
        try
        {
            var mainType = (_gameAsm ?? FindGameAssemblyFallback())?.GetType("BBI.Unity.Game.Main");
            var eventSystem = mainType?.GetField("EventSystem", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            var post = eventSystem?.GetType().GetMethod("Post", new[] { ev.GetType().BaseType ?? ev.GetType() })
                       ?? eventSystem?.GetType().GetMethods()
                           .FirstOrDefault(m => m.Name == "Post" && m.GetParameters().Length == 1);
            post?.Invoke(eventSystem, new[] { ev });
        }
        catch
        {
            // optional UI refresh
        }
    }

    private static void TryChargeUpgradePrice(object upgradeAsset)
    {
        try
        {
            var priceProp = upgradeAsset.GetType().GetProperty(
                "Price",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var prices = priceProp?.GetValue(upgradeAsset) as Array;
            if (prices == null || prices.Length == 0)
            {
                return;
            }

            foreach (var price in prices)
            {
                if (price == null)
                {
                    continue;
                }

                var amountProp = price.GetType().GetProperty(
                    "Amount",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var currencyProp = price.GetType().GetProperty(
                    "CurrencyAsset",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (amountProp?.GetValue(price) == null || currencyProp?.GetValue(price) == null)
                {
                    continue;
                }

                var amount = Convert.ToSingle(amountProp.GetValue(price));
                if (amount <= 0f)
                {
                    continue;
                }

                var currencyAsset = currencyProp.GetValue(price)!;
                var currencyName = GetUnityName(currencyAsset);
                var currencyId = ExtractAssetTypeId(currencyAsset) ?? currencyAsset;

                // Vanilla PurchaseUpgrade posts CurrencyChangedEvent.Subtract so Hab LT HUD refreshes.
                // ChangeCurrency alone updates the balance but leaves the equipment-screen total stale.
                if (TryPostCurrencyChangedSubtract(currencyId, amount))
                {
                    Plugin.Log.LogInfo(
                        $"[HS-AP] Charged Hab shop price {amount} ({currencyName}) via CurrencyChangedEvent.");
                    continue;
                }

                var controller = ResolveController();
                if (controller == null || _changeCurrency == null)
                {
                    Plugin.Log.LogWarning(
                        $"[HS-AP] Could not charge Hab shop price ({amount}); controller not ready.");
                    continue;
                }

                _changeCurrency.Invoke(controller, new[] { currencyId, -amount, true });
                TryRefreshCurrencyUi();
                Plugin.Log.LogInfo(
                    $"[HS-AP] Charged Hab shop price {amount} ({currencyName}) via ChangeCurrency + UI refresh.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] TryChargeUpgradePrice failed: {ex.Message}");
        }
    }

    private static readonly HashSet<string> LoggedPurchaseBlocks = new();

    public static bool ShouldLogPurchaseBlock(object? upgradeAsset)
    {
        var key = GetUnityName(upgradeAsset ?? "");
        if (string.IsNullOrEmpty(key))
        {
            key = "?";
        }

        return LoggedPurchaseBlocks.Add(key);
    }

    // retained for any remaining callers
    private static List<object> GetLongestChain(List<object> assets, string category)
    {
        var inCat = assets.Where(a => GetEquipmentCategory(a) == category).ToList();
        var roots = inCat.Where(a => GetPreviousUpgrade(a) == null).ToList();
        List<object> best = new();
        foreach (var root in roots)
        {
            var chain = new List<object> { root };
            var current = root;
            while (true)
            {
                var next = inCat.FirstOrDefault(a => ReferenceEquals(GetPreviousUpgrade(a), current));
                if (next == null)
                {
                    break;
                }

                chain.Add(next);
                current = next;
            }

            if (chain.Count > best.Count)
            {
                best = chain;
            }
        }

        if (best.Count == 0)
        {
            best = inCat.OrderBy(GetRequiredTier).ThenBy(GetUnityName).ToList();
        }

        return best;
    }

    public static void EnforceShipCatalogGates()
    {
        // Intentionally no-op: board/cert mutations emptied Hab ship select (0.3.0 / 0.5.0).
        // Ship families use Progressive Certification Rank + vanilla WorkPermit only.
    }

    private static bool UnlockAndApply(object asset, string displayName)
    {
        var prior = _suppressHabShopChecks;
        _suppressHabShopChecks = true;
        try
        {
            // Apply buffs only — do NOT UnlockUpgrade. That marks Hab-yellow and blocks
            // shop-sanity buys. Bay persistence is via ReapplyApGrantedUpgrades after
            // PlayerProfile.ApplyUpgrades (ClearAppliedUpgrades wipes one-shot Applies).
            ShopOwnedPendingGrant.Remove(asset);
            ApGrantedUpgrades.Add(asset);
            _applyUpgrade?.Invoke(asset, null);
            AddToAppliedUpgrades(asset);
            // Undo any prior UnlockUpgrade (0.5.8) so Hab stays buyable until shop check.
            RemoveFromHabOwnedIfUnpaidShopRow(asset);
            // UnlockUpgrade also records UpgradePurchasedPAT + Pending*Refill; ApplyUpgrade
            // does not. Without the PAT, bay vending disables tether/demo restock.
            ApplyUnlockSideEffectsWithoutHabOwnership(asset);
            Plugin.Log.LogInfo($"[HS-AP] Applied upgrade '{displayName}' (AP grant; Hab shop stays buyable)");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] Upgrade apply failed for '{displayName}': {ex.Message}");
            return false;
        }
        finally
        {
            _suppressHabShopChecks = prior;
        }
    }

    /// <summary>
    /// After ClearAppliedUpgrades + ApplyUpgrades (owned Hab rows only), re-fire AP grants
    /// so bay equipment matches received items without Hab-yellow ownership.
    /// </summary>
    public static void ReapplyApGrantedUpgrades()
    {
        if (_reapplyingApGrants || ApGrantedUpgrades.Count == 0 || _applyUpgrade == null)
        {
            return;
        }

        _reapplyingApGrants = true;
        var prior = _suppressHabShopChecks;
        _suppressHabShopChecks = true;
        try
        {
            var n = 0;
            foreach (var asset in ApGrantedUpgrades.ToList())
            {
                if (asset == null)
                {
                    continue;
                }

                _applyUpgrade.Invoke(asset, null);
                AddToAppliedUpgrades(asset);
                ApplyUnlockSideEffectsWithoutHabOwnership(asset);
                n++;
            }

            if (n > 0)
            {
                Plugin.Log.LogInfo($"[HS-AP] Re-applied {n} AP upgrade(s) after bay ApplyUpgrades.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] ReapplyApGrantedUpgrades failed: {ex.Message}");
        }
        finally
        {
            _suppressHabShopChecks = prior;
            _reapplyingApGrants = false;
        }
    }

    /// <summary>
    /// Side effects from vanilla <c>UnlockUpgrade</c> / <c>PurchaseUpgrade</c> that we must
    /// keep without adding the asset to Hab <c>PlayerProfile.Upgrades</c> (shop-sanity).
    /// Bay vending disables tether/demo buys until <c>UpgradePurchasedPAT</c> is in history;
    /// shift auto-restock uses <c>PendingTetherRefill</c> / <c>PendingDemoChargeRefill</c>.
    /// </summary>
    private static void ApplyUnlockSideEffectsWithoutHabOwnership(object upgradeAsset)
    {
        EnsureUpgradePurchasedPatRecorded(upgradeAsset);
        ApplyConsumableRefillFlags(upgradeAsset);
    }

    private static void EnsureUpgradePurchasedPatRecorded(object upgradeAsset)
    {
        try
        {
            var patProp = upgradeAsset.GetType().GetProperty(
                "UpgradePurchasedPAT",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var pat = patProp?.GetValue(upgradeAsset);
            if (pat == null)
            {
                return;
            }

            if (IsPatInHistory(pat))
            {
                return;
            }

            var gameAsm = _gameAsm ?? FindGameAssemblyFallback();
            var evType = gameAsm?.GetType("BBI.Unity.Game.PlayerActionTrackerEvent");
            if (evType == null)
            {
                return;
            }

            // GetEvent(trackingAsset, operationType = Add, operationValue = 1)
            var getEvent = evType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                    m.Name == "GetEvent"
                    && m.GetParameters().Length >= 1
                    && m.GetParameters()[0].ParameterType.IsInstanceOfType(pat));
            if (getEvent == null)
            {
                return;
            }

            var ev = getEvent.Invoke(null, BuildPatEventArgs(getEvent, pat));
            if (ev != null)
            {
                PostEvent(ev);
                Plugin.Log.LogInfo(
                    $"[HS-AP] Recorded UpgradePurchasedPAT for '{GetUnityName(upgradeAsset)}' (bay refill unlock).");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] EnsureUpgradePurchasedPatRecorded failed: {ex.Message}");
        }
    }

    private static object?[] BuildPatEventArgs(MethodInfo getEvent, object pat)
    {
        var ps = getEvent.GetParameters();
        var args = new object?[ps.Length];
        args[0] = pat;
        for (var i = 1; i < ps.Length; i++)
        {
            if (ps[i].HasDefaultValue)
            {
                args[i] = ps[i].DefaultValue;
            }
            else if (ps[i].ParameterType.IsEnum)
            {
                // MathUtility.OperationType.Add == 1 on this build.
                args[i] = Enum.ToObject(ps[i].ParameterType, 1);
            }
            else if (ps[i].ParameterType == typeof(int))
            {
                args[i] = 1;
            }
            else
            {
                args[i] = ps[i].ParameterType.IsValueType
                    ? Activator.CreateInstance(ps[i].ParameterType)
                    : null;
            }
        }

        return args;
    }

    private static bool IsPatInHistory(object patAsset)
    {
        try
        {
            var profile = FindPlayerProfile();
            if (profile == null || _playerProfileType == null)
            {
                return false;
            }

            var histProp = _playerProfileType.GetProperty(
                "PlayerActionTrackerHistory",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (histProp?.GetValue(profile) is not IDictionary history)
            {
                return false;
            }

            return history.Contains(patAsset);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Mirror <c>UpgradeAsset.UnlockUpgrade</c> consumable-refill flags without Hab ownership.
    /// </summary>
    private static void ApplyConsumableRefillFlags(object upgradeAsset)
    {
        try
        {
            var t = upgradeAsset.GetType();
            var refills = t.GetField("m_RefillsConsumables", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (refills?.GetValue(upgradeAsset) is not true)
            {
                return;
            }

            var profile = FindPlayerProfile();
            var profileType = _playerProfileType;
            if (profile == null || profileType == null)
            {
                return;
            }

            void SetPending(string propName, string fieldName)
            {
                var flagField = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (flagField?.GetValue(upgradeAsset) is not true)
                {
                    return;
                }

                var prop = profileType.GetProperty(
                    propName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                prop?.SetValue(profile, true);
            }

            SetPending("PendingFuelRefill", "m_RefillFuel");
            SetPending("PendingTetherRefill", "m_RefillTethers");
            SetPending("PendingDemoChargeRefill", "m_RefillDemoCharges");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] ApplyConsumableRefillFlags failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 0.5.8 UnlockUpgrade left shop rows yellow/purchased-looking. Remove unpaid Hab shop
    /// assets (and any AP-granted assets not Hab-bought) from PlayerProfile.Upgrades so the
    /// tree uses DrawUnpurchasedUpgrade + rank-lock visuals.
    /// </summary>
    public static void StripUnpaidShopRowsFromHabOwned()
    {
        try
        {
            var profile = FindPlayerProfile();
            if (profile == null || _playerProfileType == null || !_habShopSanity)
            {
                return;
            }

            var upgradesProp = _playerProfileType.GetProperty(
                "Upgrades",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var upgrades = upgradesProp?.GetValue(profile);
            if (upgrades == null)
            {
                return;
            }

            var toRemove = new List<object>();
            if (upgrades is System.Collections.IEnumerable enumerable)
            {
                foreach (var entry in enumerable)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    // Keep free starters and real Hab buys.
                    if (IsFreeStarterUpgrade(entry) || ShopOwnedPendingGrant.Contains(entry))
                    {
                        continue;
                    }

                    if (TryMapHabShopLocation(entry, out var id, out _))
                    {
                        if (HabShopPaidLocationIds.Contains(id))
                        {
                            continue;
                        }

                        toRemove.Add(entry);
                        continue;
                    }

                    // AP-granted but not a mapped shop row — still shouldn't look Hab-purchased.
                    if (ApGrantedUpgrades.Contains(entry))
                    {
                        toRemove.Add(entry);
                    }
                }
            }

            if (toRemove.Count == 0)
            {
                return;
            }

            var remove = upgrades.GetType().GetMethod("Remove", new[] { toRemove[0].GetType() })
                         ?? upgrades.GetType().GetMethods()
                             .FirstOrDefault(m => m.Name == "Remove" && m.GetParameters().Length == 1);
            foreach (var asset in toRemove)
            {
                remove?.Invoke(upgrades, new[] { asset });
                ShopOwnedPendingGrant.Remove(asset);
            }

            Plugin.Log.LogWarning(
                $"[HS-AP] Restored Hab shop visuals: removed {toRemove.Count} non-Hab-bought row(s) from owned/yellow.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] StripUnpaidShopRowsFromHabOwned failed: {ex.Message}");
        }
    }

    private static void RemoveFromHabOwnedIfUnpaidShopRow(object asset)
    {
        try
        {
            if (!_habShopSanity
                || !TryMapHabShopLocation(asset, out var id, out _)
                || HabShopPaidLocationIds.Contains(id))
            {
                return;
            }

            var profile = FindPlayerProfile();
            if (profile == null || _playerProfileType == null)
            {
                return;
            }

            var upgradesProp = _playerProfileType.GetProperty(
                "Upgrades",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var upgrades = upgradesProp?.GetValue(profile);
            var remove = upgrades?.GetType().GetMethod("Remove", new[] { asset.GetType() })
                         ?? upgrades?.GetType().GetMethods()
                             .FirstOrDefault(m => m.Name == "Remove" && m.GetParameters().Length == 1);
            remove?.Invoke(upgrades, new[] { asset });
            ShopOwnedPendingGrant.Remove(asset);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// After DrawUnpurchasedUpgrade: force locked color + cert badge when rank is too low.
    /// Vanilla only enables m_InvalidCertificationObject when CanPurchase out == InvalidCertification.
    /// </summary>
    public static void EnsureHabRankLockVisual(object upgradeTreeButton)
    {
        if (!_habShopSanity || upgradeTreeButton == null)
        {
            return;
        }

        try
        {
            var upgradeField = upgradeTreeButton.GetType().GetField(
                "mUpgrade",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var upgrade = upgradeField?.GetValue(upgradeTreeButton);
            if (upgrade == null || !TryMapHabShopLocation(upgrade, out _, out _))
            {
                return;
            }

            if (IsUpgradePurchaseBlocked(upgrade))
            {
                return;
            }

            var required = GetRequiredTier(upgrade);
            var rank = ReadCurrentCertificationRank();
            if (rank >= required)
            {
                return;
            }

            var bg = upgradeTreeButton.GetType().GetField(
                    "m_ButtonBackground",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(upgradeTreeButton);
            var lockedColor = upgradeTreeButton.GetType().GetField(
                    "m_LockedColor",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(upgradeTreeButton);
            if (bg != null && lockedColor != null)
            {
                bg.GetType().GetProperty("color")?.SetValue(bg, lockedColor);
            }

            var reqText = upgradeTreeButton.GetType().GetField(
                    "m_RequiredCertificationText",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(upgradeTreeButton);
            reqText?.GetType().GetProperty("text")?.SetValue(reqText, required.ToString());

            var badge = upgradeTreeButton.GetType().GetField(
                    "m_InvalidCertificationObject",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(upgradeTreeButton) as UnityEngine.GameObject;
            if (badge != null)
            {
                badge.SetActive(true);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] EnsureHabRankLockVisual failed: {ex.Message}");
        }
    }

    private static bool _reapplyingApGrants;

    private static void AddToAppliedUpgrades(object asset)
    {
        try
        {
            var profile = FindPlayerProfile();
            if (profile == null || _playerProfileType == null)
            {
                return;
            }

            var prop = _playerProfileType.GetProperty(
                "AppliedUpgrades",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var applied = prop?.GetValue(profile);
            if (applied == null)
            {
                return;
            }

            var add = applied.GetType().GetMethods()
                .FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1);
            add?.Invoke(applied, new[] { asset });
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// 0.3.0 raised WorkPermit far above CurrentCertificationRank (e.g. 10 at rank 2),
    /// which broke Hab ship select. Clamp WorkPermit back to the cert rank.
    /// </summary>
    private static void RepairInflatedWorkPermit()
    {
        try
        {
            var profile = FindPlayerProfile();
            if (profile == null || _playerProfileType == null)
            {
                return;
            }

            var tiersProp = _playerProfileType.GetProperty(
                "CertificationTiers",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (tiersProp?.GetValue(profile) is not IDictionary tiers)
            {
                return;
            }

            var certType = _gameAsm?.GetType("BBI.Unity.Game.CertificationType");
            if (certType == null)
            {
                return;
            }

            var workPermitKey = Enum.Parse(certType, "WorkPermit");
            var current = 0;
            if (tiers.Contains(workPermitKey) && tiers[workPermitKey] != null)
            {
                current = Convert.ToInt32(tiers[workPermitKey]);
            }

            var rank = 1;
            var rankProp = _playerProfileType.GetProperty(
                "CurrentCertificationRank",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (rankProp?.GetValue(profile) != null)
            {
                rank = Math.Max(1, Convert.ToInt32(rankProp.GetValue(profile)));
            }

            if (current > rank)
            {
                tiers[workPermitKey] = rank;
                Plugin.Log.LogWarning(
                    $"[HS-AP] Repaired inflated WorkPermit {current} → {rank} (cert rank). Reload Hab / start a new Career if UI stays broken.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] RepairInflatedWorkPermit failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Older clients forced RemainingShiftsTillBoardRefresh=0, which triggers a full
    /// board regen during bay load (Step5) and can desync Hab card preview from the
    /// ship that actually spawns. Restore the vanilla counter when the board already
    /// has previews — never force 0.
    /// </summary>
    private static void HealStaleJobBoardRefreshCounter()
    {
        try
        {
            // F10 just requested a board regen — don't undo RemainingShiftsTillBoardRefresh=0.
            if (Time.unscaledTime < _suppressBoardHealUntil)
            {
                return;
            }

            var profile = FindPlayerProfile();
            if (profile == null || _playerProfileType == null)
            {
                return;
            }

            // Do not touch the counter mid-claim / mid-load.
            if (HasPendingNextShipPreview())
            {
                return;
            }

            var remainingProp = _playerProfileType.GetProperty(
                "RemainingShiftsTillBoardRefresh",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (remainingProp == null)
            {
                return;
            }

            var remaining = Convert.ToInt32(remainingProp.GetValue(profile) ?? 0);
            if (remaining > 0)
            {
                return;
            }

            var mapProp = _playerProfileType.GetProperty(
                "ShipClassToAvailablePreviewsMap",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var map = mapProp?.GetValue(profile) as IDictionary;
            if (map == null || map.Count == 0)
            {
                // Empty map + Remaining=0 is vanilla "generate board" — leave alone.
                return;
            }

            var reset = _playerProfileType.GetMethod(
                "ResetJobBoardRefreshCounter",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (reset == null)
            {
                Plugin.Log.LogWarning("[HS-AP] ResetJobBoardRefreshCounter missing; cannot heal board refresh counter.");
                return;
            }

            reset.Invoke(profile, null);
            var restored = Convert.ToInt32(remainingProp.GetValue(profile) ?? 0);

            // Leftover from 0.2–0.3 HigherRankShipIndicesToShow injects.
            var higherProp = _playerProfileType.GetProperty(
                "HigherRankShipIndicesToShow",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var higher = higherProp?.GetValue(profile);
            if (higher != null)
            {
                var countProp = higher.GetType().GetProperty("Count");
                var higherCount = countProp != null ? Convert.ToInt32(countProp.GetValue(higher) ?? 0) : 0;
                if (higherCount > 0)
                {
                    higher.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(higher, null);
                    Plugin.Log.LogInfo("[HS-AP] Cleared stale HigherRankShipIndicesToShow.");
                }
            }

            Plugin.Log.LogWarning(
                $"[HS-AP] Healed stale job-board refresh counter 0 → {restored} (board already had {map.Count} class preview(s)). Prevents preview/spawn desync.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] HealStaleJobBoardRefreshCounter failed: {ex.Message}");
        }
    }

    private static bool HasPendingNextShipPreview()
    {
        try
        {
            var msType = _gameAsm?.GetType("BBI.Unity.Game.ModuleService");
            var instance = msType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            if (instance == null)
            {
                return false;
            }

            var next = instance.GetType()
                .GetProperty("NextShipPreview", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance);
            return next != null;
        }
        catch
        {
            return false;
        }
    }

    private static void FlushPendingCertificationRank()
    {
        // Progressive Certification Rank only raises the MP ceiling (AllowCertificationTarget).
        // Never auto-sets CurrentCertificationRank.
        if (_certRankProgress > 0)
        {
            _loggedCertGate = false;
        }
    }

    /// <summary>
    /// Progressive Certification Rank raises the MP ceiling only. Does <b>not</b> call
    /// TrySetCertification — the player must earn Mastery Points in Career to advance.
    /// </summary>
    private static bool TryApplyCertificationRank(int progressiveCount)
    {
        _loggedCertGate = false;
        Plugin.Log.LogInfo(
            $"[HS-AP] Progressive Cert Rank ×{progressiveCount}: ceiling={CertificationRankCeiling} (no rank jump).");
        return true;
    }

    /// <summary>
    /// Call CertificationService.TrySetCertification with a <b>display</b> rank (1-based).
    /// Game API wants asset index = rank − 1. Second arg is isDebug (grants skipped PATs when true).
    /// </summary>
    private static bool TrySetCertificationDisplayRank(int displayRank, bool isDebug)
    {
        if (_trySetCertification == null || displayRank < 1)
        {
            return false;
        }

        var service = FindCertificationService();
        if (service == null)
        {
            return false;
        }

        var index = displayRank - 1;
        return _trySetCertification.Invoke(service, new object[] { index, isDebug }) is true;
    }

    /// <summary>If a save is already above the AP-allowed ceiling, pull it back.</summary>
    private static void ClampCertificationToCeiling()
    {
        try
        {
            if (_trySetCertification == null)
            {
                return;
            }

            var current = ReadCurrentCertificationRank();
            var ceiling = CertificationRankCeiling;
            if (current <= ceiling || ceiling < 1)
            {
                return;
            }

            var prior = _suppressCertGate;
            _suppressCertGate = true;
            try
            {
                if (TrySetCertificationDisplayRank(ceiling, isDebug: false))
                {
                    ResetMasteryPointsToZero();
                    Plugin.Log.LogWarning(
                        $"[HS-AP] Clamped certification {current} → {ceiling} (need Progressive Cert Rank ×{_certRankProgress + 1} for the next milestone).");
                }
            }
            finally
            {
                _suppressCertGate = prior;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] ClampCertificationToCeiling failed: {ex.Message}");
        }
    }

    /// <summary>
    /// TrySetCertification(debug) raises CurrentCertificationRank without syncing PlayerXPTracker.
    /// Hab shows (CurrentXP − RequiredXP[rank−2]) / (RequiredXP[rank−1] − RequiredXP[rank−2]),
    /// so an early-career CurrentXP at rank 20+ displays as a large negative (e.g. −7130 / 935).
    /// Mirror PlayerXPTracker.DebugLevelUpSetXP: set Current/Previous XP to the prior rank threshold.
    /// </summary>
    private static void ResetMasteryPointsToZero()
    {
        try
        {
            var profile = FindPlayerProfile();
            if (profile == null || _playerProfileType == null)
            {
                return;
            }

            // PlayerProfile.XPTracker is a field, not a property.
            var tracker = _playerProfileType
                              .GetField("XPTracker", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                              ?.GetValue(profile)
                          ?? _playerProfileType
                              .GetProperty("XPTracker", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                              ?.GetValue(profile);
            if (tracker == null)
            {
                Plugin.Log.LogWarning("[HS-AP] XPTracker missing — cannot repair Mastery Points.");
                return;
            }

            var rank = ReadCurrentCertificationRank();
            var threshold = GetCumulativeRequiredXpForRank(rank);
            var t = tracker.GetType();
            foreach (var propName in new[] { "CurrentXP", "PreviousXP", "PendingXP", "CurrentShipXP" })
            {
                var p = t.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p is { CanWrite: true } && (p.PropertyType == typeof(float) || p.PropertyType == typeof(Single)))
                {
                    // Current/Previous must sit on the prior-rank cumulative threshold so the Hab
                    // bar reads 0 / delta instead of (lowXP − highThreshold).
                    var value = propName is "CurrentXP" or "PreviousXP" ? threshold : 0f;
                    p.SetValue(tracker, value);
                }
            }

            Plugin.Log.LogInfo(
                $"[HS-AP] Synced Mastery Points to rank {rank} threshold ({threshold:0} XP) after certification change.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] ResetMasteryPointsToZero failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Cumulative RequiredXP for assets[rank−2] (Hab mRelativeZero). Rank &lt; 2 → 0.
    /// </summary>
    private static float GetCumulativeRequiredXpForRank(int rank)
    {
        if (rank < 2)
        {
            return 0f;
        }

        try
        {
            var mainType = _gameAsm?.GetType("BBI.Unity.Game.Main");
            var instance = mainType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            var settings = instance?.GetType()
                .GetProperty("MainSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance);
            var certSettings = settings?.GetType()
                .GetProperty("CertificationSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(settings);
            var assets = certSettings?.GetType()
                .GetProperty("CertificationLevelAssets", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(certSettings) as Array;
            var idx = rank - 2;
            if (assets == null || idx < 0 || idx >= assets.Length)
            {
                return 0f;
            }

            var asset = assets.GetValue(idx);
            var data = asset?.GetType()
                .GetProperty("Data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(asset);
            var req = data?.GetType()
                .GetProperty("RequiredXP", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(data);
            return req != null ? Convert.ToSingle(req) : 0f;
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>Repair negative Hab MP if CurrentXP sits below the current rank threshold.</summary>
    private static void RepairNegativeMasteryPoints()
    {
        try
        {
            var profile = FindPlayerProfile();
            if (profile == null || _playerProfileType == null)
            {
                return;
            }

            var tracker = _playerProfileType
                .GetField("XPTracker", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(profile);
            if (tracker == null)
            {
                return;
            }

            var currentProp = tracker.GetType()
                .GetProperty("CurrentXP", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (currentProp == null)
            {
                return;
            }

            var currentXp = Convert.ToSingle(currentProp.GetValue(tracker));
            var rank = ReadCurrentCertificationRank();
            var threshold = GetCumulativeRequiredXpForRank(rank);
            if (currentXp + 0.5f >= threshold)
            {
                return;
            }

            Plugin.Log.LogWarning(
                $"[HS-AP] Negative MP detected (CurrentXP={currentXp:0}, rank {rank} threshold={threshold:0}). Repairing.");
            ResetMasteryPointsToZero();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] RepairNegativeMasteryPoints failed: {ex.Message}");
        }
    }

    private static int GetMaxCertificationRankSafe()
    {
        try
        {
            var service = FindCertificationService();
            var method = _certificationServiceType?.GetMethod(
                "GetMaxCertificationRank",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (service != null && method != null)
            {
                return Math.Max(CertRankMilestones[^1], Convert.ToInt32(method.Invoke(service, null)));
            }
        }
        catch
        {
            // ignore
        }

        return 30;
    }

    private static object? FindCertificationService()
    {
        if (_certificationServiceType == null)
        {
            return null;
        }

        try
        {
            var instProp = _certificationServiceType.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var service = instProp?.GetValue(null);
            if (service != null)
            {
                return service;
            }

            var instField = _certificationServiceType.GetField(
                                "Instance",
                                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                            ?? _certificationServiceType.GetField(
                                "<Instance>k__BackingField",
                                BindingFlags.Static | BindingFlags.NonPublic);
            return instField?.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    private static object[] FindJobBoardShipClasses()
    {
        try
        {
            var mainType = _gameAsm?.GetType("BBI.Unity.Game.Main");
            var instance = mainType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            var settings = instance?.GetType().GetProperty("MainSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance);
            var hab = settings?.GetType().GetProperty("HabSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(settings);
            var jobBoard = hab?.GetType().GetProperty("JobBoardSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(hab);
            var shipClasses = jobBoard?.GetType().GetProperty("ShipClasses", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(jobBoard) as Array;
            if (shipClasses != null && shipClasses.Length > 0)
            {
                var list = new object[shipClasses.Length];
                for (var i = 0; i < shipClasses.Length; i++)
                {
                    list[i] = shipClasses.GetValue(i)!;
                    CachedShipClasses[$"grade_idx_{i}"] = list[i];
                }

                return list;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] FindJobBoardShipClasses failed: {ex.Message}");
        }

        if (_shipClassAssetType == null)
        {
            return Array.Empty<object>();
        }

        try
        {
            var all = Resources.FindObjectsOfTypeAll(_shipClassAssetType);
            return all?.Cast<object>().ToArray() ?? Array.Empty<object>();
        }
        catch
        {
            return Array.Empty<object>();
        }
    }

    private static List<object> FindShipArchetypes(string[] needles, object[] grades)
    {
        var found = new List<object>();
        var seen = new HashSet<object>();

        void Consider(object? arch)
        {
            if (arch == null || !seen.Add(arch))
            {
                return;
            }

            var label = DescribeArchetype(arch);
            foreach (var needle in needles)
            {
                if (label.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found.Add(arch);
                    return;
                }
            }
        }

        foreach (var grade in grades)
        {
            try
            {
                var pairs = grade.GetType().GetProperty("GeneratableShips", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(grade) as Array;
                if (pairs == null)
                {
                    continue;
                }

                foreach (var pair in pairs)
                {
                    if (pair == null)
                    {
                        continue;
                    }

                    var archField = pair.GetType().GetField("ShipArchetype", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    Consider(archField?.GetValue(pair));
                }
            }
            catch
            {
                // ignore
            }
        }

        // Addressable fallback for known archetype asset addresses.
        foreach (var needle in needles)
        {
            var address = $"Assets/Content/Data/ShipArchetypeAsset/{needle}_ShipArchetypeAsset.asset";
            var loaded = TryLoadAddressable(address, _gameAsm?.GetType("BBI.Unity.Game.ShipArchetypeAsset"));
            Consider(loaded);
        }

        if (_gameAsm?.GetType("BBI.Unity.Game.ShipArchetypeAsset") is { } archType)
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll(archType);
                if (all != null)
                {
                    foreach (var a in all)
                    {
                        Consider(a);
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        return found;
    }

    private static object? TryLoadAddressable(string address, Type? assetType)
    {
        if (assetType == null)
        {
            return null;
        }

        try
        {
            var addrAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Unity.Addressables");
            var addressables = addrAsm?.GetType("UnityEngine.AddressableAssets.Addressables");
            var load = addressables?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "LoadAssetAsync" && m.IsGenericMethodDefinition
                                     && m.GetParameters().Length == 1
                                     && m.GetParameters()[0].ParameterType == typeof(object));
            if (load == null)
            {
                return null;
            }

            var generic = load.MakeGenericMethod(assetType);
            var handle = generic.Invoke(null, new object[] { address });
            if (handle == null)
            {
                return null;
            }

            var wait = handle.GetType().GetMethod("WaitForCompletion", BindingFlags.Public | BindingFlags.Instance);
            return wait?.Invoke(handle, null);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogInfo($"[HS-AP] Addressable load '{address}' skipped: {ex.GetBaseException().Message}");
            return null;
        }
    }

    private static string DescribeArchetype(object arch)
    {
        try
        {
            var model = arch.GetType().GetProperty("ShipModelName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(arch)?.ToString();
            if (!string.IsNullOrWhiteSpace(model))
            {
                return model!;
            }
        }
        catch
        {
            // ignore
        }

        return (arch as UnityEngine.Object)?.name ?? arch.ToString() ?? "?";
    }

    private static void EnsureProgressiveWorkPermit(object profile, int floor)
    {
        // Disabled in 0.3.1 — raising WorkPermit corrupted Hab ship select.
    }

    private static void InjectArchetypeIntoCertUnlocks(object archetype)
    {
        InjectArchetypeIntoCurrentCertUnlock(archetype);
    }

    /// <summary>Append archetype to current cert rank unlock list only (never remove / never raise WorkPermit).</summary>
    private static void InjectArchetypeIntoCurrentCertUnlock(object archetype)
    {
        var csType = _gameAsm?.GetType("BBI.Unity.Game.CertificationService");
        var instance = csType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(null);
        if (instance == null)
        {
            return;
        }

        var unlocks = csType!.GetProperty("CertLevelUnlocks", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(instance) as IDictionary;
        if (unlocks == null || unlocks.Count == 0)
        {
            return;
        }

        var rank = ReadCurrentCertificationRank();
        object? data = null;
        if (unlocks.Contains(rank))
        {
            data = unlocks[rank];
        }
        else
        {
            // Prefer lowest existing key so we don't invent cert rows.
            foreach (DictionaryEntry entry in unlocks)
            {
                data = entry.Value;
                break;
            }
        }

        if (data == null)
        {
            return;
        }

        var listField = data.GetType().GetField(
            "CertLevelShipArchetypeUnlocks",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (listField?.GetValue(data) is not IList list)
        {
            return;
        }

        foreach (var existing in list)
        {
            if (ReferenceEquals(existing, archetype))
            {
                return;
            }
        }

        list.Add(archetype);
    }

    private static void MarkHigherRankShipIndices(object profile, int minWorkPermitTier, int gradeCount)
    {
        if (_playerProfileType == null)
        {
            return;
        }

        var prop = _playerProfileType.GetProperty(
            "HigherRankShipIndicesToShow",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop?.GetValue(profile) is not IEnumerable setObj)
        {
            return;
        }

        // HashSet<int>.Add via reflection
        var add = setObj.GetType().GetMethod("Add", new[] { typeof(int) });
        var idx = Math.Max(0, Math.Min(gradeCount - 1, minWorkPermitTier - 1));
        add?.Invoke(setObj, new object[] { idx });
    }

    private static void RequestJobBoardRefresh(object profile)
    {
        // Disabled — forcing RemainingShiftsTillBoardRefresh=0 with the accessible-class
        // postfix emptied Hab ship select in 0.5.0.
    }

    /// <summary>
    /// Disabled: treating empty RawLoadedAvailableShips as broken and setting
    /// RemainingShiftsTillBoardRefresh=0 emptied Hab ship select on fresh careers
    /// (list is normally empty until vanilla finishes generating previews).
    /// </summary>
    private static void TryRecoverEmptyJobBoard()
    {
        // no-op
    }

    /// <summary>
    /// Postfix helper kept for reference. Disabled — patching this emptied Hab ship select.
    /// Ship families are PCR + vanilla cert only (no AP unlock gating).
    /// </summary>
    public static void AdjustAccessibleShipClasses(ref object? currentHighest, ref object? highestDuringLastRefresh)
    {
        // no-op (0.5.0 hotfix)
    }

    /// <summary>True while AP is applying an upgrade (skip HabShop location checks).</summary>
    public static bool SuppressHabShopChecks => _suppressHabShopChecks;


    private static string GetShipClassName(object? shipClass)
    {
        if (shipClass == null)
        {
            return "";
        }

        try
        {
            var prop = shipClass.GetType().GetProperty(
                "ClassName",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var n = prop?.GetValue(shipClass)?.ToString();
            if (!string.IsNullOrWhiteSpace(n))
            {
                return n!;
            }
        }
        catch
        {
            // ignore
        }

        return (shipClass as UnityEngine.Object)?.name ?? shipClass.ToString() ?? "";
    }

    /// <summary>Exposed for GameHooks tutorial / shift gating.</summary>
    internal static object? TryGetPlayerProfile() => FindPlayerProfile();

    private static object? FindPlayerProfile()
    {
        try
        {
            // PlayerProfile is a plain CLR object — not findable via Resources.FindObjectsOfTypeAll.
            if (_playerProfileServiceType != null)
            {
                object? service = null;
                var instProp = _playerProfileServiceType.GetProperty(
                    "Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                service = instProp?.GetValue(null);
                if (service == null)
                {
                    var instField = _playerProfileServiceType.GetField(
                        "Instance",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                                    ?? _playerProfileServiceType.GetField(
                                        "<Instance>k__BackingField",
                                        BindingFlags.Static | BindingFlags.NonPublic);
                    service = instField?.GetValue(null);
                }

                if (service != null)
                {
                    var profileProp = _playerProfileServiceType.GetProperty(
                        "Profile",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var profile = profileProp?.GetValue(service);
                    if (profile != null)
                    {
                        return profile;
                    }

                    var profileField = _playerProfileServiceType.GetField(
                        "mProfile",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    profile = profileField?.GetValue(service);
                    if (profile != null)
                    {
                        return profile;
                    }
                }
            }

            // Via UpgradeService.mPlayerProfileService
            var upgradeService = FindUpgradeService();
            if (upgradeService != null)
            {
                var ppsField = upgradeService.GetType().GetField(
                    "mPlayerProfileService",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var pps = ppsField?.GetValue(upgradeService);
                if (pps != null)
                {
                    var profileProp = pps.GetType().GetProperty(
                        "Profile",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var profile = profileProp?.GetValue(pps);
                    if (profile != null)
                    {
                        return profile;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] FindPlayerProfile failed: {ex.Message}");
        }

        return null;
    }

    private static List<object> FindUpgradeAssets()
    {
        var list = new List<object>();
        if (_upgradeAssetType == null)
        {
            return list;
        }

        try
        {
            var all = Resources.FindObjectsOfTypeAll(_upgradeAssetType);
            if (all != null)
            {
                list.AddRange(all.Cast<object>());
            }
        }
        catch
        {
            // ignore
        }

        return list;
    }

    private static object? FindUpgradeService()
    {
        if (_upgradeServiceType == null)
        {
            return null;
        }

        foreach (var name in new[] { "Instance", "instance", "Current" })
        {
            var prop = _upgradeServiceType.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var val = prop?.GetValue(null);
            if (val != null)
            {
                return val;
            }

            var field = _upgradeServiceType.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            val = field?.GetValue(null);
            if (val != null)
            {
                return val;
            }
        }

        try
        {
            var all = Resources.FindObjectsOfTypeAll(_upgradeServiceType);
            if (all is { Length: > 0 })
            {
                return all[0];
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string GetUpgradeName(object asset)
    {
        // Prefer Unity asset name — UpgradeName is often a numeric localization id.
        var unity = GetUnityName(asset);
        if (!string.IsNullOrWhiteSpace(unity) && !IsMostlyNumeric(unity))
        {
            return unity;
        }

        try
        {
            var tool = _upgradeAssetType
                ?.GetProperty("ToolName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(asset)
                ?.ToString();
            if (!string.IsNullOrWhiteSpace(tool) && !IsMostlyNumeric(tool))
            {
                return tool!;
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            var n = _upgradeNameGetter?.Invoke(asset, null)?.ToString();
            if (!string.IsNullOrWhiteSpace(n) && !IsMostlyNumeric(n))
            {
                return n!;
            }
        }
        catch
        {
            // ignore
        }

        return DescribeUpgrade(asset);
    }

    private static string DescribeUpgrade(object asset) =>
        $"{GetUnityName(asset)}/{GetEquipmentCategory(asset)}/t{GetRequiredTier(asset)}";

    private static string GetUnityName(object asset) =>
        (asset as UnityEngine.Object)?.name ?? asset.ToString() ?? "?";

    private static bool IsMostlyNumeric(string s) =>
        s.Length > 0 && s.All(c => char.IsDigit(c) || c == ' ' || c == '-' || c == '.');

    private static string GetEquipmentCategory(object asset)
    {
        try
        {
            var eq = asset.GetType().GetField("m_EquipmentType", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var val = eq?.GetValue(asset);
            if (val != null)
            {
                return val.ToString() ?? "";
            }

            var prop = asset.GetType().GetProperty("EquipmentType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            val = prop?.GetValue(asset);
            return val?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static int GetRequiredTier(object asset)
    {
        try
        {
            var prop = asset.GetType().GetProperty("RequiredTier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop?.GetValue(asset) is int i)
            {
                return i;
            }

            var field = asset.GetType().GetField("m_RequiredTier", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field?.GetValue(asset) is int j)
            {
                return j;
            }
        }
        catch
        {
            // ignore
        }

        return 0;
    }

    private static object? GetPreviousUpgrade(object asset)
    {
        try
        {
            var field = asset.GetType().GetField("PreviousUpgrade", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(asset);
        }
        catch
        {
            return null;
        }
    }

    private static void TryAddCurrency(string kind, float amount)
    {
        try
        {
            var controller = ResolveController();
            EnsureCurrencyIdsResolved(controller);
            var currencyId = ResolveCurrencyId(kind);
            if (controller == null || _changeCurrency == null || currencyId == null)
            {
                PendingCurrency.Add((kind, amount));
                Plugin.Log.LogInfo(
                    $"[HS-AP] Queued {kind} +{amount} (controller={controller != null}, id={currencyId != null}).");
                return;
            }

            GrantNow(controller, kind, amount, currencyId);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] TryAddCurrency failed: {ex.Message}");
        }
    }

    private static void FlushPendingCurrency()
    {
        if (PendingCurrency.Count == 0 || _changeCurrency == null)
        {
            return;
        }

        var controller = ResolveController();
        if (controller == null)
        {
            return;
        }

        EnsureCurrencyIdsResolved(controller);
        var pending = PendingCurrency.ToArray();
        PendingCurrency.Clear();
        foreach (var (kind, amount) in pending)
        {
            var id = ResolveCurrencyId(kind);
            if (id == null)
            {
                PendingCurrency.Add((kind, amount));
                continue;
            }

            try
            {
                GrantNow(controller, kind, amount, id);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HS-AP] Pending grant {kind} failed: {ex.Message}");
                PendingCurrency.Add((kind, amount));
            }
        }
    }

    public static void SetQuietCurrencyGrants(bool quiet) => _quietCurrencyGrants = quiet;

    private static void GrantNow(object controller, string kind, float amount, object currencyId)
    {
        var debtBefore = _quietCurrencyGrants ? 0f : ReadDisplayedDebtRemaining();
        var ltBefore = _quietCurrencyGrants ? 0f : ReadCurrencyAmount(ResolveCurrencyId("LT"));

        // Game UI (debt/LT HUD, Hab contract stats, currency widgets) listens to CurrencyChangedEvent,
        // not CurrencyController.ChangeCurrency. Debug cheats post the event; PlayerProfileService
        // then applies ChangeCurrency. Prefer that path so the HUD updates live.
        if (TryPostCurrencyChangedAdd(currencyId, amount))
        {
            if (!_quietCurrencyGrants)
            {
                Plugin.Log.LogInfo($"[HS-AP] Granted {kind} +{amount} via CurrencyChangedEvent");
            }
        }
        else
        {
            _changeCurrency!.Invoke(controller, new[] { currencyId, amount, true });
            if (!_quietCurrencyGrants)
            {
                TryRefreshCurrencyUi();
                Plugin.Log.LogInfo($"[HS-AP] Granted {kind} +{amount} via ChangeCurrency + UI refresh");
            }
        }

        if (_quietCurrencyGrants)
        {
            return;
        }

        var debtAfter = ReadDisplayedDebtRemaining();
        var ltAfter = ReadCurrencyAmount(ResolveCurrencyId("LT"));

        if (string.Equals(kind, "Debt", StringComparison.Ordinal)
            || string.Equals(kind, "Credits", StringComparison.Ordinal))
        {
            var paid = debtBefore - debtAfter;
            ApToastQueue.EnqueueInfo(
                paid > 0.5f
                    ? $"Debt −{paid:n0}  (now {debtAfter:n0})"
                    : $"Credits +{amount:n0}  (debt {debtAfter:n0})");
        }
        else if (string.Equals(kind, "LT", StringComparison.Ordinal))
        {
            var gained = ltAfter - ltBefore;
            ApToastQueue.EnqueueInfo(
                gained > 0.01f
                    ? $"LT +{gained:n0}  (now {ltAfter:n0})"
                    : $"LT +{amount:n0}");
        }
    }

    /// <summary>
    /// Posts CurrencyChangedEvent.Add so PlayerProfileService applies the change and UI listeners refresh.
    /// </summary>
    private static bool TryPostCurrencyChangedAdd(object currencyId, float amount) =>
        TryPostCurrencyChanged("Add", currencyId, amount);

    /// <summary>
    /// Posts CurrencyChangedEvent.Subtract (vanilla Hab purchase path) so LT HUD updates live.
    /// </summary>
    private static bool TryPostCurrencyChangedSubtract(object currencyId, float amount) =>
        TryPostCurrencyChanged("Subtract", currencyId, amount);

    private static bool TryPostCurrencyChanged(string methodName, object currencyId, float amount)
    {
        try
        {
            var gameAsm = _gameAsm ?? FindGameAssemblyFallback();
            var eventType = gameAsm?.GetType("BBI.Unity.Game.CurrencyChangedEvent");
            var method = eventType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                {
                    if (m.Name != methodName)
                    {
                        return false;
                    }

                    var ps = m.GetParameters();
                    return ps.Length == 2 && ps[1].ParameterType == typeof(float);
                });
            if (method == null)
            {
                return false;
            }

            var ev = method.Invoke(null, new[] { currencyId, amount });
            if (ev == null)
            {
                return false;
            }

            PostEvent(ev);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] CurrencyChangedEvent.{methodName} failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Fallback HUD refresh when CurrencyChangedEvent cannot be posted.</summary>
    private static void TryRefreshCurrencyUi()
    {
        try
        {
            var gameAsm = _gameAsm ?? FindGameAssemblyFallback();
            if (gameAsm == null)
            {
                return;
            }

            InvokeOnAllInstances(gameAsm, "BBI.Unity.Game.PlayerLevelContainerController", "UpdateCurrencyData");
            InvokeOnAllInstances(gameAsm, "BBI.Unity.Game.HabContractStatsController", "UpdateStats");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] Currency UI refresh failed: {ex.Message}");
        }
    }

    private static void InvokeOnAllInstances(Assembly gameAsm, string typeName, string methodName)
    {
        var type = gameAsm.GetType(typeName);
        if (type == null)
        {
            return;
        }

        var method = type.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        if (method == null)
        {
            return;
        }

        foreach (var obj in Resources.FindObjectsOfTypeAll(type))
        {
            if (obj == null)
            {
                continue;
            }

            try
            {
                method.Invoke(obj, null);
            }
            catch
            {
                // inactive / disposed UI instance
            }
        }
    }

    private static object? ResolveController()
    {
        if (_cachedController != null)
        {
            return _cachedController;
        }

        if (_currencyControllerType == null)
        {
            return null;
        }

        foreach (var name in new[] { "Instance", "instance", "Current" })
        {
            var prop = _currencyControllerType.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var val = prop?.GetValue(null);
            if (val != null)
            {
                _cachedController = val;
                return val;
            }
        }

        try
        {
            var all = Resources.FindObjectsOfTypeAll(_currencyControllerType);
            if (all is { Length: > 0 })
            {
                _cachedController = all[0];
                return _cachedController;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static void ClassifyCurrencyId(object currencyAssetId)
    {
        // Live ChangeCurrency traffic — only learn LT from exact LT_ assets (never "Token" fuzz).
        if (_ltId != null && _creditsId != null)
        {
            return;
        }

        if (_gameAsm == null)
        {
            return;
        }

        var currencyAssetType = _gameAsm.GetType("BBI.Unity.Game.CurrencyAsset");
        if (currencyAssetType == null)
        {
            return;
        }

        UnityEngine.Object[] assets;
        try
        {
            assets = Resources.FindObjectsOfTypeAll(currencyAssetType);
        }
        catch
        {
            return;
        }

        foreach (var asset in assets)
        {
            var id = ExtractAssetTypeId(asset);
            if (id == null || !IdsEqual(id, currencyAssetId))
            {
                continue;
            }

            var n = asset.name ?? "";
            if (_ltId == null
                && (string.Equals(n, "LT_CurrencyAsset", StringComparison.OrdinalIgnoreCase)
                    || n.StartsWith("LT_", StringComparison.OrdinalIgnoreCase)))
            {
                _ltId = currencyAssetId;
                Plugin.Log.LogInfo($"[HS-AP] Learned LT id via asset '{n}'");
            }
        }

        EnsureCurrencyIdsResolved(ResolveController());
    }

    /// <summary>
    /// Debt UI uses StartingDebt − DebtCurrency.Amount. Credit packs must Add() to that debt currency
    /// (CurrencyController.CreditsAsset / DebtInterestAsset.Currency), not LT.
    /// </summary>
    private static void EnsureCurrencyIdsResolved(object? controller)
    {
        if (_creditsId == null && controller != null)
        {
            try
            {
                var creditsAsset = controller.GetType()
                    .GetProperty("CreditsAsset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(controller);
                if (creditsAsset != null)
                {
                    _creditsId = ExtractAssetTypeId(creditsAsset);
                    Plugin.Log.LogInfo(
                        $"[HS-AP] Debt/Credits id from CurrencyController.CreditsAsset '{GetUnityName(creditsAsset)}'");
                }
            }
            catch
            {
                // ignore
            }
        }

        if (_creditsId == null)
        {
            var debtAsset = TryGetDebtInterestCurrencyAsset();
            if (debtAsset != null)
            {
                _creditsId = ExtractAssetTypeId(debtAsset);
                Plugin.Log.LogInfo(
                    $"[HS-AP] Debt/Credits id from DebtInterestAsset '{GetUnityName(debtAsset)}'");
            }
        }

        if (_ltId == null && _gameAsm != null)
        {
            try
            {
                var currencyAssetType = _gameAsm.GetType("BBI.Unity.Game.CurrencyAsset");
                if (currencyAssetType != null)
                {
                    foreach (var asset in Resources.FindObjectsOfTypeAll(currencyAssetType))
                    {
                        var n = asset.name ?? "";
                        if (string.Equals(n, "LT_CurrencyAsset", StringComparison.OrdinalIgnoreCase)
                            || n.StartsWith("LT_", StringComparison.OrdinalIgnoreCase))
                        {
                            _ltId = ExtractAssetTypeId(asset);
                            Plugin.Log.LogInfo($"[HS-AP] LT id from asset '{n}'");
                            break;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private static object? TryGetDebtInterestCurrencyAsset()
    {
        try
        {
            var profile = FindPlayerProfile();
            var difficulty = profile?.GetType()
                .GetProperty("DifficultyMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(profile);
            var debtInterest = difficulty?.GetType()
                .GetProperty("DebtInterestAsset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(difficulty);
            if (debtInterest == null)
            {
                return null;
            }

            var data = debtInterest.GetType()
                            .GetField("Data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?.GetValue(debtInterest)
                        ?? debtInterest.GetType()
                            .GetProperty("Data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?.GetValue(debtInterest);
            if (data == null)
            {
                return null;
            }

            return data.GetType()
                       .GetField("Currency", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       ?.GetValue(data)
                   ?? data.GetType()
                       .GetProperty("Currency", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       ?.GetValue(data);
        }
        catch
        {
            return null;
        }
    }

    private static object? ResolveCurrencyId(string kind)
    {
        EnsureCurrencyIdsResolved(ResolveController());

        if (kind is "Debt" or "Credits")
        {
            return _creditsId;
        }

        if (kind == "LT")
        {
            return _ltId;
        }

        return null;
    }

    private static float ReadCurrencyAmount(object? currencyId)
    {
        try
        {
            if (currencyId == null || _currencyControllerType == null)
            {
                return 0f;
            }

            var controller = ResolveController();
            if (controller == null)
            {
                return 0f;
            }

            var getCurrency = _currencyControllerType.GetMethod(
                "GetCurrency",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var instance = getCurrency?.Invoke(controller, new[] { currencyId });
            if (instance == null)
            {
                return 0f;
            }

            var amountProp = instance.GetType().GetProperty(
                "Amount",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return Convert.ToSingle(amountProp?.GetValue(instance) ?? 0f);
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>Displayed LYNX debt = StartingDebtAmount − debtCurrency.Amount.</summary>
    private static float ReadDisplayedDebtRemaining()
    {
        try
        {
            var profile = FindPlayerProfile();
            var difficulty = profile?.GetType()
                .GetProperty("DifficultyMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(profile);
            var starting = difficulty?.GetType()
                .GetProperty("StartingDebtAmount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(difficulty);
            var start = starting != null ? Convert.ToSingle(starting) : 0f;
            var paid = ReadCurrencyAmount(ResolveCurrencyId("Debt"));
            return Math.Max(0f, start - paid);
        }
        catch
        {
            return 0f;
        }
    }

    private static bool IdsEqual(object a, object b) =>
        ReferenceEquals(a, b) || a.Equals(b) || string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);

    private static object? ExtractAssetTypeId(object currencyAsset)
    {
        var idProp = currencyAsset.GetType().GetProperty("ID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     ?? currencyAsset.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (idProp != null)
        {
            return idProp.GetValue(currencyAsset);
        }

        var idField = currencyAsset.GetType().GetField("m_ID", BindingFlags.Instance | BindingFlags.NonPublic)
                      ?? currencyAsset.GetType().GetField("ID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return idField?.GetValue(currencyAsset);
    }
}
