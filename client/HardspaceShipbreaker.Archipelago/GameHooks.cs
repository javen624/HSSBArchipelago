using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace HardspaceShipbreaker.Archipelago;

/// <summary>
/// Reflection Harmony hooks verified from docs/phase0 logs.
/// Prefer HandlePositiveSalvage; IsCorrectSalvageOption is a debounced fallback.
/// </summary>
internal static class GameHooks
{
    private static Harmony? _harmony;
    private static ArchipelagoClient? _client;
    private static readonly HashSet<string> RecentIsCorrectKeys = new();
    private static readonly object Gate = new();
    /// <summary>True if Career already finished/skipped tutorial when the current bay session started.</summary>
    private static bool _tutorialAlreadyDoneAtShiftStart;

    public static void Apply(ArchipelagoClient client)
    {
        _client = client;
        var gameAsm = FindGameAssembly();
        if (gameAsm == null)
        {
            Plugin.Log.LogError("[HS-AP] BBI.Unity.Game not found; game hooks skipped.");
            return;
        }

        ItemApplicator.Initialize(gameAsm);
        DeathLinkHooks.Initialize(client);

        _harmony = new Harmony("hardspace.shipbreaker.archipelago.hooks");
        int n = 0;

        // Deposit — primary (void) + fallback (bool)
        n += Patch(gameAsm, "BBI.Unity.Game.BargeEntranceVolume", "HandlePositiveSalvage", useResult: false);
        n += Patch(gameAsm, "BBI.Unity.Game.ProcessorVolume", "HandlePositiveSalvage", useResult: false);
        n += Patch(gameAsm, "BBI.Unity.Game.FurnaceVolume", "HandlePositiveSalvage", useResult: false);
        n += Patch(gameAsm, "BBI.Unity.Game.SalvageAcceptorVolumeBase", "HandlePositiveSalvage", useResult: false);
        n += Patch(gameAsm, "BBI.Unity.Game.SalvageAcceptorVolumeBase", "IsCorrectSalvageOption", useResult: true);

        // Shift / Hab
        n += Patch(gameAsm, "BBI.Unity.Game.LevelCompleteEvent", "GetEvent", useResult: true);
        n += Patch(gameAsm, "BBI.Unity.Game.PostMissionScreen", "UpdateCertXPTotals", prefix: true);
        n += Patch(gameAsm, "BBI.Unity.Game.SceneLoader", "TearDownAndLoadFrontEndAsync", prefix: true);
        n += PatchRewardTier(gameAsm);

        // Death
        n += Patch(gameAsm, "BBI.Unity.Game.PlayerLevelContainerController", "OnDeath", useResult: false);
        n += Patch(gameAsm, "BBI.Unity.Game.Player", "WaitToRespawnPlayer", useResult: true);

        // Rank: observe + gate past milestones without Progressive Certification Rank
        n += PatchCertification(gameAsm);

        // Currency (filler + debt goal later)
        n += Patch(gameAsm, "BBI.Unity.Game.CurrencyController", "ChangeCurrency", useResult: false);

        // Session start — flush queued upgrades/currency
        n += Patch(gameAsm, "BBI.Unity.Game.Main", "StartSession", useResult: false);

        // Hab shop-sanity: purchase → yellow + location check (no grant); AP Apply without yellow
        n += PatchCanPurchaseUpgrade(gameAsm);
        n += PatchPurchaseUpgrade(gameAsm);
        n += PatchApplyUpgrade(gameAsm);
        n += PatchApplyUpgrades(gameAsm);
        n += PatchUpgradeTreeVisuals(gameAsm);
        // Do NOT patch GetCurrentlyAccessibleShipClasses — Harmony out/ref as object?
        // corrupted Hab ship select (empty board) in 0.5.0.

        // Live milestones + bay use gates
        n += PatchTetherHooks(gameAsm);
        n += PatchDataDriveHooks(gameAsm);
        n += PatchBayUseGates(gameAsm);
        n += PatchShipClaimDiagnostics(gameAsm);
        n += PatchDisplayTrainingShip(gameAsm);

        Plugin.Log.LogInfo($"[HS-AP] Applied {n} game hook(s).");
    }

    /// <summary>
    /// Vanilla hides the ship catalogue when training PATs are met. After AP collect / finished
    /// training location, keep the real bay visible every time ShowJobBoard runs.
    /// </summary>
    private static int PatchDisplayTrainingShip(Assembly gameAsm)
    {
        var type = gameAsm.GetType("BBI.Unity.Game.JobBoardScreenController");
        var method = type?.GetMethod(
            "DisplayTrainingShip",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        if (method == null)
        {
            Plugin.Log.LogWarning("[HS-AP] DisplayTrainingShip not found.");
            return 0;
        }

        try
        {
            _harmony!.CreateProcessor(method)
                .AddPrefix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.DisplayTrainingShipPrefix)))
                .AddPostfix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.DisplayTrainingShipPostfix)))
                .Patch();
            Plugin.Log.LogInfo("[HS-AP] Hooked JobBoardScreenController.DisplayTrainingShip");
            return 1;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] DisplayTrainingShip patch failed: {ex.Message}");
            return 0;
        }
    }

    private static int PatchRewardTier(Assembly gameAsm)
    {
        var type = gameAsm.GetType("BBI.Unity.Game.PostMissionScreen");
        var method = type?.GetMethod(
            "OnRewardTierStateChangedEvent",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
        {
            Plugin.Log.LogWarning("[HS-AP] OnRewardTierStateChangedEvent not found.");
            return 0;
        }

        try
        {
            _harmony!.CreateProcessor(method)
                .AddPostfix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.RewardTierPostfix)))
                .Patch();
            Plugin.Log.LogInfo("[HS-AP] Hooked PostMissionScreen.OnRewardTierStateChangedEvent");
            return 1;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] RewardTier patch failed: {ex.Message}");
            return 0;
        }
    }

    private static int PatchCertification(Assembly gameAsm)
    {
        var type = gameAsm.GetType("BBI.Unity.Game.CertificationService");
        if (type == null)
        {
            Plugin.Log.LogWarning("[HS-AP] CertificationService not found.");
            return 0;
        }

        var count = 0;
        var trySet = type.GetMethod(
            "TrySetCertification",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(int), typeof(bool) },
            null);
        var tryInc = type.GetMethod(
            "TryIncreaseCertification",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(bool) },
            null);

        if (trySet != null)
        {
            try
            {
                _harmony!.CreateProcessor(trySet)
                    .AddPrefix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.TrySetCertificationPrefix)))
                    .AddPostfix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.Postfix)))
                    .Patch();
                Plugin.Log.LogInfo("[HS-AP] Hooked CertificationService.TrySetCertification (gate+observe)");
                count++;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HS-AP] TrySetCertification patch failed: {ex.Message}");
            }
        }

        if (tryInc != null)
        {
            try
            {
                _harmony!.CreateProcessor(tryInc)
                    .AddPrefix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.TryIncreaseCertificationPrefix)))
                    .AddPostfix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.Postfix)))
                    .Patch();
                Plugin.Log.LogInfo("[HS-AP] Hooked CertificationService.TryIncreaseCertification (gate+observe)");
                count++;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HS-AP] TryIncreaseCertification patch failed: {ex.Message}");
            }
        }

        return count;
    }

    private static int PatchCanPurchaseUpgrade(Assembly gameAsm)
    {
        var type = gameAsm.GetType("BBI.Unity.Game.UpgradeService");
        var method = type?.GetMethod(
            "CanPurchaseUpgrade",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
        {
            Plugin.Log.LogWarning("[HS-AP] CanPurchaseUpgrade not found for hard gate.");
            return 0;
        }

        try
        {
            _harmony!.CreateProcessor(method)
                .AddPrefix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.CanPurchaseUpgradePrefix)))
                .Patch();
            Plugin.Log.LogInfo("[HS-AP] Hooked UpgradeService.CanPurchaseUpgrade (shop-sanity: block re-buy after check)");
            return 1;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] CanPurchaseUpgrade patch failed: {ex.Message}");
            return 0;
        }
    }

    private static int PatchPurchaseUpgrade(Assembly gameAsm)
    {
        var type = gameAsm.GetType("BBI.Unity.Game.UpgradeService");
        var methods = type?.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == "PurchaseUpgrade")
            .ToArray() ?? Array.Empty<MethodInfo>();
        if (methods.Length == 0)
        {
            Plugin.Log.LogWarning("[HS-AP] PurchaseUpgrade not found for shop-sanity.");
            return 0;
        }

        var count = 0;
        foreach (var method in methods)
        {
        try
        {
            _harmony!.CreateProcessor(method)
                .AddPrefix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.PurchaseUpgradePrefix)))
                .AddPostfix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.VoidPostfix)))
                .Patch();
            Plugin.Log.LogInfo($"[HS-AP] Hooked UpgradeService.PurchaseUpgrade ({method.GetParameters().Length} params)");
            count++;
        }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HS-AP] PurchaseUpgrade patch failed: {ex.Message}");
            }
        }

        return count;
    }

    private static int PatchApplyUpgrade(Assembly gameAsm)
    {
        var type = gameAsm.GetType("BBI.Unity.Game.UpgradeAsset");
        var method = type?.GetMethod(
            "ApplyUpgrade",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
        {
            Plugin.Log.LogWarning("[HS-AP] UpgradeAsset.ApplyUpgrade not found.");
            return 0;
        }

        try
        {
            _harmony!.CreateProcessor(method)
                .AddPrefix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.ApplyUpgradePrefix)))
                .Patch();
            Plugin.Log.LogInfo("[HS-AP] Hooked UpgradeAsset.ApplyUpgrade (block Hab-yellow until AP grant)");
            return 1;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] ApplyUpgrade patch failed: {ex.Message}");
            return 0;
        }
    }

    private static int PatchApplyUpgrades(Assembly gameAsm)
    {
        var type = gameAsm.GetType("BBI.Unity.Game.PlayerProfile");
        var method = type?.GetMethod(
            "ApplyUpgrades",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
        {
            Plugin.Log.LogWarning("[HS-AP] PlayerProfile.ApplyUpgrades not found.");
            return 0;
        }

        try
        {
            _harmony!.CreateProcessor(method)
                .AddPostfix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.ApplyUpgradesPostfix)))
                .Patch();
            Plugin.Log.LogInfo("[HS-AP] Hooked PlayerProfile.ApplyUpgrades (re-apply AP grants for bay)");
            return 1;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] ApplyUpgrades patch failed: {ex.Message}");
            return 0;
        }
    }

    private static int PatchUpgradeTreeVisuals(Assembly gameAsm)
    {
        var count = 0;
        var buttonType = gameAsm.GetType("BBI.Unity.Game.UpgradeTreeButton");
        var drawUnpurchased = buttonType?.GetMethod(
            "DrawUnpurchasedUpgrade",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (drawUnpurchased == null)
        {
            Plugin.Log.LogWarning("[HS-AP] UpgradeTreeButton.DrawUnpurchasedUpgrade not found.");
        }
        else
        {
            try
            {
                _harmony!.CreateProcessor(drawUnpurchased)
                    .AddPostfix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.DrawUnpurchasedUpgradePostfix)))
                    .Patch();
                Plugin.Log.LogInfo("[HS-AP] Hooked UpgradeTreeButton.DrawUnpurchasedUpgrade (rank-lock badge)");
                count++;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HS-AP] DrawUnpurchasedUpgrade patch failed: {ex.Message}");
            }
        }

        var screenType = gameAsm.GetType("BBI.Unity.Game.UpgradeScreenController");
        var onEnable = screenType?.GetMethod(
            "OnEnable",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (onEnable == null)
        {
            Plugin.Log.LogWarning("[HS-AP] UpgradeScreenController.OnEnable not found.");
            return count;
        }

        try
        {
            _harmony!.CreateProcessor(onEnable)
                .AddPrefix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.UpgradeScreenEnablePrefix)))
                .Patch();
            Plugin.Log.LogInfo("[HS-AP] Hooked UpgradeScreenController.OnEnable (strip false Hab-yellow)");
            count++;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] UpgradeScreen OnEnable patch failed: {ex.Message}");
        }

        return count;
    }

    private static int PatchAccessibleShipClasses(Assembly gameAsm)
    {
        var type = gameAsm.GetType("BBI.Unity.Game.JobBoardUtils");
        var method = type?.GetMethod(
            "GetCurrentlyAccessibleShipClasses",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
        {
            Plugin.Log.LogWarning("[HS-AP] GetCurrentlyAccessibleShipClasses not found.");
            return 0;
        }

        try
        {
            _harmony!.CreateProcessor(method)
                .AddPostfix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.AccessibleShipClassesPostfix)))
                .Patch();
            Plugin.Log.LogInfo("[HS-AP] Hooked JobBoardUtils.GetCurrentlyAccessibleShipClasses");
            return 1;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] AccessibleShipClasses patch failed: {ex.Message}");
            return 0;
        }
    }

    private static int PatchTetherHooks(Assembly gameAsm)
    {
        // TetherController is in global namespace on this build (not BBI.Unity.Game).
        var type = gameAsm.GetType("TetherController")
                   ?? gameAsm.GetType("BBI.Unity.Game.TetherController");
        var method = type?.GetMethod(
            "TryCreateTether",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
        {
            Plugin.Log.LogWarning("[HS-AP] TetherController.TryCreateTether not found.");
            return 0;
        }

        try
        {
            _harmony!.CreateProcessor(method)
                .AddPrefix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.TetherCreatePrefix)))
                .AddPostfix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.TetherCreatePostfix)))
                .Patch();
            Plugin.Log.LogInfo("[HS-AP] Hooked TetherController.TryCreateTether");
            return 1;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] Tether hook failed: {ex.Message}");
            return 0;
        }
    }

    private static int PatchDataDriveHooks(Assembly gameAsm)
    {
        var type = gameAsm.GetType("BBI.Unity.Game.NarrativeItemSystem");
        var method = type?.GetMethod(
            "IdentifyNarrativeEntry",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
        {
            Plugin.Log.LogWarning("[HS-AP] NarrativeItemSystem.IdentifyNarrativeEntry not found.");
            return 0;
        }

        try
        {
            _harmony!.CreateProcessor(method)
                .AddPostfix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.DataDriveIdentifyPostfix)))
                .Patch();
            Plugin.Log.LogInfo("[HS-AP] Hooked NarrativeItemSystem.IdentifyNarrativeEntry");
            return 1;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] Data drive hook failed: {ex.Message}");
            return 0;
        }
    }

    private static int PatchBayUseGates(Assembly gameAsm)
    {
        var count = 0;
        count += PatchBayGate(
            gameAsm,
            new[] { "BBI.Unity.Game.DemoChargeController", "DemoChargeController" },
            "PlaceDemoCharge",
            nameof(GameHookSink.DemoPlacePrefix));
        count += PatchBayGate(
            gameAsm,
            new[] { "BBI.Unity.Game.DemoChargeController", "DemoChargeController" },
            "ThrowDemoCharge",
            nameof(GameHookSink.DemoPlacePrefix));
        // Do NOT gate GrapplingHook.DoPush — vanilla push is often already unlocked;
        // hard-blocking DoPush made grapple push feel broken. AP Charged Push still
        // gates Hab upgrade apply + Progressive Charged Push Force via ItemApplicator.
        return count;
    }

    private static int PatchBayGate(Assembly gameAsm, string[] typeNames, string methodName, string prefixName)
    {
        Type? type = null;
        foreach (var typeName in typeNames)
        {
            type = gameAsm.GetType(typeName);
            if (type != null)
            {
                break;
            }
        }

        var method = type?.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
        {
            Plugin.Log.LogWarning($"[HS-AP] Bay gate target missing: {string.Join("|", typeNames)}.{methodName}");
            return 0;
        }

        try
        {
            var prefix = new HarmonyMethod(typeof(GameHookSink).GetMethod(prefixName, BindingFlags.Static | BindingFlags.Public));
            _harmony!.CreateProcessor(method).AddPrefix(prefix).Patch();
            Plugin.Log.LogInfo($"[HS-AP] Hooked bay gate {type!.Name}.{method.Name}");
            return 1;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] Bay gate {type!.Name}.{methodName} failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>Log claimed Hab card vs bay CurrentShipPreview to catch preview/spawn desync.</summary>
    private static int PatchShipClaimDiagnostics(Assembly gameAsm)
    {
        var count = 0;
        var type = gameAsm.GetType("BBI.Unity.Game.JobBoardScreenController");
        var method = type?.GetMethod(
            "SetupCurrentlySelectedShipForSpawn",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
        {
            Plugin.Log.LogWarning("[HS-AP] SetupCurrentlySelectedShipForSpawn not found.");
        }
        else
        {
            try
            {
                _harmony!.CreateProcessor(method)
                    .AddPostfix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.ShipClaimPostfix)))
                    .Patch();
                Plugin.Log.LogInfo("[HS-AP] Hooked JobBoardScreenController.SetupCurrentlySelectedShipForSpawn");
                count++;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HS-AP] Ship claim diagnostics patch failed: {ex.Message}");
            }
        }

        var msType = gameAsm.GetType("BBI.Unity.Game.ModuleService");
        var spawnDone = msType?.GetMethod(
            "OnShipAllSpawnWrapperEvent",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (spawnDone == null)
        {
            Plugin.Log.LogWarning("[HS-AP] ModuleService.OnShipAllSpawnWrapperEvent not found.");
            return count;
        }

        try
        {
            _harmony!.CreateProcessor(spawnDone)
                .AddPostfix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.ShipSpawnedPostfix)))
                .Patch();
            Plugin.Log.LogInfo("[HS-AP] Hooked ModuleService.OnShipAllSpawnWrapperEvent");
            count++;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] Ship spawn diagnostics patch failed: {ex.Message}");
        }

        return count;
    }

    public static void Unpatch()
    {
        _harmony?.UnpatchSelf();
        _harmony = null;
        _client = null;
        lock (Gate)
        {
            RecentIsCorrectKeys.Clear();
        }
    }

    private static Assembly? FindGameAssembly()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name == "BBI.Unity.Game")
            {
                return asm;
            }
        }

        try
        {
            return Assembly.Load("BBI.Unity.Game");
        }
        catch
        {
            return null;
        }
    }

    private static int Patch(
        Assembly gameAsm,
        string typeName,
        string methodName,
        bool useResult = false,
        bool prefix = false)
    {
        var type = gameAsm.GetType(typeName);
        if (type == null)
        {
            Plugin.Log.LogWarning($"[HS-AP] Hook type missing: {typeName}");
            return 0;
        }

        var methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == methodName)
            .ToArray();
        if (methods.Length == 0)
        {
            Plugin.Log.LogWarning($"[HS-AP] Hook method missing: {typeName}.{methodName}");
            return 0;
        }

        int count = 0;
        foreach (var method in methods)
        {
            try
            {
                var proc = _harmony!.CreateProcessor(method);
                if (prefix)
                {
                    proc.AddPrefix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.Prefix)));
                }
                else if (method.ReturnType == typeof(void) || !useResult)
                {
                    proc.AddPostfix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.VoidPostfix)));
                }
                else
                {
                    proc.AddPostfix(new HarmonyMethod(typeof(GameHookSink), nameof(GameHookSink.Postfix)));
                }

                proc.Patch();
                Plugin.Log.LogInfo($"[HS-AP] Hooked {type.Name}.{method.Name}");
                count++;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HS-AP] Hook failed {type.Name}.{method.Name}: {ex.Message}");
            }
        }

        return count;
    }

    internal static void OnPositiveSalvage(object? volumeInstance, object[]? args, string source = "HandlePositiveSalvage")
    {
        var dest = DestinationFromVolume(volumeInstance);
        var partInfo = DescribeStructurePart(args);
        var hasPart = !string.IsNullOrEmpty(partInfo) && partInfo != "?";
        var isFallback = source == "IsCorrectSalvageOption";

        // IsCorrectSalvageOption: first-destination milestones only (no part identity).
        // Named checks require HandlePositiveSalvage + StructurePart.
        var sentSomething = false;
        switch (dest)
        {
            case SalvageDestination.Barge:
                sentSomething |= TryCheck(ArchipelagoClient.BaseId + 110, "First Barge Deposit", source);
                sentSomething |= TryCheck(ArchipelagoClient.BaseId + 100, "Finish Basic Training", source);
                if (!isFallback && hasPart)
                {
                    sentSomething |= TryNamedComponentChecks(partInfo, SalvageDestination.Barge, source);
                }

                break;
            case SalvageDestination.Processor:
                sentSomething |= TryCheck(ArchipelagoClient.BaseId + 111, "First Processor Deposit", source);
                if (!isFallback && hasPart)
                {
                    sentSomething |= TryAluminumCheck(partInfo, source);
                    sentSomething |= TryNamedComponentChecks(partInfo, SalvageDestination.Processor, source);
                }

                break;
            case SalvageDestination.Furnace:
                sentSomething |= TryCheck(ArchipelagoClient.BaseId + 112, "First Furnace Deposit", source);
                if (!isFallback && hasPart)
                {
                    sentSomething |= TryGlassCheck(partInfo, source);
                    sentSomething |= TryNamedComponentChecks(partInfo, SalvageDestination.Furnace, source);
                }

                break;
        }

        if (sentSomething || (hasPart && !isFallback && LooksNotable(partInfo)))
        {
            Plugin.Log.LogInfo($"[HS-AP] Deposit via {source} dest={dest} part={partInfo}");
        }
    }

    private static bool TryCheck(long id, string name, string source) =>
        _client?.TryCheckLocation(id, name, source) == true;

    /// <summary>Fallback when HandlePositiveSalvage could not be patched (0.1.0 probe bug).</summary>
    internal static void OnCorrectSalvageOption(object? volumeInstance, object[]? args)
    {
        var dest = DestinationFromVolume(volumeInstance);
        if (dest == SalvageDestination.Unknown)
        {
            return;
        }

        // Debounce: IsCorrect fires while objects linger in the volume.
        var entityKey = args is { Length: >= 2 } ? args[1]?.ToString() ?? "?" : "?";
        var key = $"{dest}:{entityKey}";
        lock (Gate)
        {
            if (!RecentIsCorrectKeys.Add(key))
            {
                return;
            }

            if (RecentIsCorrectKeys.Count > 500)
            {
                RecentIsCorrectKeys.Clear();
                RecentIsCorrectKeys.Add(key);
            }
        }

        OnPositiveSalvage(volumeInstance, args: null, source: "IsCorrectSalvageOption");
    }

    internal static void OnShiftComplete(string source = "ShiftComplete")
    {
        // Skipping the tutorial (or finishing Basic Training) must not count as first Career shift.
        if (!_tutorialAlreadyDoneAtShiftStart || IsTutorialShipInPlay())
        {
            Plugin.Log.LogInfo(
                $"[HS-AP] Skipping Complete First Shift [{source}] (tutorialSnapshot={_tutorialAlreadyDoneAtShiftStart}, tutorialShip={IsTutorialShipInPlay()}).");
            _client?.TryCheckLocation(ArchipelagoClient.BaseId + 100, "Finish Basic Training", source);
            return;
        }

        _client?.TryCheckLocation(ArchipelagoClient.BaseId + 101, "Complete First Shift", source);
        // Per-ship salvage tiers fire from OnRewardTierStateChangedEvent, not every shift end.
        // Ship clears only when the bay mass threshold is met (not every shift timer / post-mission).
        if (IsBayCleared())
        {
            TryCheckShipClearsFromPreview(source);
        }
        else
        {
            Plugin.Log.LogInfo($"[HS-AP] Ship clear skipped [{source}]: bay not cleared.");
        }
    }

    /// <summary>LevelComplete / timer path — first shift only (no ship-clear guesses).</summary>
    internal static void OnShiftCompleteFirstShiftOnly(string source)
    {
        if (!_tutorialAlreadyDoneAtShiftStart || IsTutorialShipInPlay())
        {
            Plugin.Log.LogInfo(
                $"[HS-AP] Skipping Complete First Shift [{source}] (tutorialSnapshot={_tutorialAlreadyDoneAtShiftStart}, tutorialShip={IsTutorialShipInPlay()}).");
            _client?.TryCheckLocation(ArchipelagoClient.BaseId + 100, "Finish Basic Training", source);
            return;
        }

        _client?.TryCheckLocation(ArchipelagoClient.BaseId + 101, "Complete First Shift", source);
    }

    private static bool IsBayCleared()
    {
        try
        {
            var gameAsm = FindGameAssembly();
            var msType = gameAsm?.GetType("BBI.Unity.Game.ModuleService");
            var instance = msType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            if (instance == null)
            {
                return false;
            }

            var prop = instance.GetType().GetProperty(
                "IsBayCleared",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return prop?.GetValue(instance) is true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Fire Clear-* locations from the current ship preview archetype / class.</summary>
    internal static void TryCheckShipClearsFromPreview(string source = "ShipClear")
    {
        try
        {
            var hint = DescribeCurrentShip(null);
            if (string.IsNullOrWhiteSpace(hint))
            {
                Plugin.Log.LogInfo($"[HS-AP] Ship clear skipped [{source}]: no ship preview hint.");
                return;
            }

            Plugin.Log.LogInfo($"[HS-AP] Ship clear hint [{source}]: {hint}");
            OnShipClearedHint(hint);

            // Grade milestones from ShipClassAsset.ShipGrade when available.
            var preview = FindCurrentShipPreview();
            var classAsset = preview?.GetType()
                .GetProperty("ShipClassAsset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(preview);
            var gradeObj = classAsset?.GetType()
                .GetProperty("ShipGrade", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(classAsset);
            if (gradeObj != null)
            {
                var grade = Convert.ToInt32(gradeObj);
                if (grade >= 1)
                {
                    _client?.TryCheckLocation(ArchipelagoClient.BaseId + 121, "Clear Ship Grade 1", source);
                }

                if (grade >= 4)
                {
                    _client?.TryCheckLocation(ArchipelagoClient.BaseId + 137, "Clear Ship Grade 4", source);
                }

                if (grade >= 7)
                {
                    _client?.TryCheckLocation(ArchipelagoClient.BaseId + 138, "Clear Ship Grade 7", source);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] TryCheckShipClearsFromPreview failed: {ex.Message}");
        }
    }

    internal static void OnFirstTetherPlaced()
    {
        _client?.TryCheckLocation(ArchipelagoClient.BaseId + 123, "Place First Tether", "Tether");
    }

    internal static void OnDataDriveRecovered()
    {
        var count = ItemApplicator.CountRecoveredDataDrives();
        Plugin.Log.LogInfo($"[HS-AP] Data drive recovered — identified count≈{count}");
        if (count >= 1)
        {
            _client?.TryCheckLocation(ArchipelagoClient.BaseId + 141, "Recover First Data Drive", "DataDrive");
        }

        if (count >= 3)
        {
            _client?.TryCheckLocation(ArchipelagoClient.BaseId + 148, "Recover 3 Data Drives", "DataDrive");
        }

        if (count >= 5)
        {
            _client?.TryCheckLocation(ArchipelagoClient.BaseId + 149, "Recover 5 Data Drives", "DataDrive");
        }
    }

    /// <summary>True during Basic Training / tutorial hulls — skip bay use gates.</summary>
    internal static bool IsTutorialContext()
    {
        return !_tutorialAlreadyDoneAtShiftStart || IsTutorialShipInPlay();
    }

    private static string? _lastClaimedShipHint;

    internal static void OnShipClaimedForSpawn()
    {
        try
        {
            var next = FindNextShipPreview();
            _lastClaimedShipHint = FormatShipPreviewHint(next);
            Plugin.Log.LogInfo($"[HS-AP] Claimed ship for spawn: {_lastClaimedShipHint ?? "(null)"}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] OnShipClaimedForSpawn failed: {ex.Message}");
        }
    }

    private static void LogClaimedVsBayShip()
    {
        try
        {
            // Only compare when we just claimed a catalogue card.
            if (string.IsNullOrWhiteSpace(_lastClaimedShipHint))
            {
                return;
            }

            var bay = FormatShipPreviewHint(FindCurrentShipPreviewOnly());
            Plugin.Log.LogInfo($"[HS-AP] Bay ship after load: {bay ?? "(null)"}");
            if (!string.IsNullOrWhiteSpace(bay)
                && !string.Equals(_lastClaimedShipHint, bay, StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Log.LogWarning(
                    $"[HS-AP] Ship preview/spawn mismatch — claimed '{_lastClaimedShipHint}' but bay loaded '{bay}'. " +
                    "If this persists after 0.5.7 heal, start a new Career (old board save corruption).");
            }

            _lastClaimedShipHint = null;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] LogClaimedVsBayShip failed: {ex.Message}");
        }
    }

    internal static void LogClaimedVsBayShipPublic() => LogClaimedVsBayShip();

    private static object? FindNextShipPreview()
    {
        try
        {
            var gameAsm = FindGameAssembly();
            var msType = gameAsm?.GetType("BBI.Unity.Game.ModuleService");
            var instance = msType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            return instance?.GetType()
                .GetProperty("NextShipPreview", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }

    private static object? FindCurrentShipPreviewOnly()
    {
        try
        {
            var gameAsm = FindGameAssembly();
            var msType = gameAsm?.GetType("BBI.Unity.Game.ModuleService");
            var instance = msType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            return instance?.GetType()
                .GetProperty("CurrentShipPreview", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }

    private static string? FormatShipPreviewHint(object? preview)
    {
        if (preview == null)
        {
            return null;
        }

        var parts = new List<string>();
        AppendShipPreviewHints(parts, preview);
        return parts.Count == 0 ? preview.ToString() : string.Join(" | ", parts);
    }

    private static readonly HashSet<string> LoggedBayBlocks = new(StringComparer.Ordinal);

    internal static void LogBayBlockOnce(string key, string message)
    {
        if (!LoggedBayBlocks.Add(key))
        {
            return;
        }

        Plugin.Log.LogInfo($"[HS-AP] Bay use gated: {message}");
    }

    /// <summary>Salvage goal tier reached on post-mission screen (per ship family).</summary>
    internal static void OnSalvageTierReached(object? postMissionScreen, object? tierEvent)
    {
        if (tierEvent == null)
        {
            return;
        }

        try
        {
            var stateProp = tierEvent.GetType().GetProperty(
                "State",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var stateName = stateProp?.GetValue(tierEvent)?.ToString() ?? "";
            if (!string.Equals(stateName, "Reached", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var tierProp = tierEvent.GetType().GetProperty(
                "Tier",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (tierProp?.GetValue(tierEvent) == null)
            {
                return;
            }

            var raw = Convert.ToInt32(tierProp.GetValue(tierEvent));
            // Game may use 0–4 or 1–5.
            var tier = raw is >= 1 and <= 5 ? raw : raw + 1;
            if (tier is < 1 or > 5)
            {
                return;
            }

            var hint = DescribeCurrentShip(postMissionScreen);
            var family = FamilyFromHint(hint);
            if (family == null)
            {
                Plugin.Log.LogInfo(
                    $"[HS-AP] Salvage tier {tier} reached but ship family unknown (hint='{hint}').");
                return;
            }

            // Family-wide tier (BASE+300…).
            var familyName = $"{family} Salvage Tier {tier}";
            var familyId = ArchipelagoClient.BaseId + 300 + FamilyIndex(family) * 5 + (tier - 1);
            Plugin.Log.LogInfo($"[HS-AP] Salvage tier reached: {familyName} (rawTier={raw})");
            _client?.TryCheckLocation(familyId, familyName, "SalvageTier");

            // Per-variant tier (BASE+350…) when role/hull can be identified.
            var variant = VariantFromHint(family, hint);
            if (variant != null && TryVariantTierLocation(family, variant, tier, out var vName, out var vId))
            {
                Plugin.Log.LogInfo($"[HS-AP] Variant salvage tier reached: {vName}");
                _client?.TryCheckLocation(vId, vName, "SalvageTier");
            }
            else
            {
                Plugin.Log.LogInfo(
                    $"[HS-AP] Salvage tier {tier} on {family}: variant unknown (hint='{hint}').");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[HS-AP] OnSalvageTierReached failed: {ex.Message}");
        }
    }

    // Must match worlds/HardspaceShipbreaker/salvage_tiers.py SHIP_VARIANTS order (BASE+350).
    // Needles include display names AND ModuleConstructionAsset stems from catalog.json.
    private static readonly (string Family, string Variant, string[] Needles)[] ShipVariants =
    {
        // Mackerel_Civilian_Cargo / Civilian_Transit / Science_Shuttle / Industrial_Cargo
        ("Mackerel", "Light Cargo", new[]
        {
            "Civilian_Cargo", "CivilianCargo", "Light Cargo", "LightCargo", "Civ_Cargo", "CivCargo",
            "MkrlCivCargo"
        }),
        ("Mackerel", "Station Hopper", new[]
        {
            "Civilian_Transit", "CivilianTransit", "Station Hopper", "StationHopper", "Station_Hopper",
            "Passenger", "StationJumper", "Hopper"
        }),
        ("Mackerel", "Exolab", new[]
        {
            "Science_Shuttle", "ScienceShuttle", "Exolab", "MkrlScience"
        }),
        ("Mackerel", "Heavy Cargo", new[]
        {
            "Industrial_Cargo", "IndustrialCargo", "Heavy Cargo", "HeavyCargo"
        }),
        // Mistral_*Prototype (Atlas class): PATROL / CARGO / TUG
        ("Atlas", "Scout", new[] { "PATROLPrototype", "PATROL", "Scout", "Patrol" }),
        ("Atlas", "Nomad", new[] { "CARGOPrototype", "Nomad" }),
        ("Atlas", "Roustabout", new[] { "TUGPrototype", "Roustabout", "Tug" }),
        // Javelin_Industrial_Refueling_{Sm,Med,Lrg} / Industrial_Cargo_{Sm,Med,Lrg}
        ("Javelin", "Small Refueling", new[]
        {
            "Refueling_Sm", "RefuelingSm", "Small Refueling", "SmallRefuel", "Small_Refuel"
        }),
        ("Javelin", "Small Heavy Cargo", new[]
        {
            "Cargo_Sm", "CargoSm", "Small Heavy", "SmallHeavy", "Small_Heavy"
        }),
        ("Javelin", "Medium Refueling", new[]
        {
            "Refueling_Med", "RefuelingMed", "Medium Refueling", "MediumRefuel", "Medium_Refuel"
        }),
        ("Javelin", "Medium Heavy Cargo", new[]
        {
            "Cargo_Med", "CargoMed", "Medium Heavy", "MediumHeavy", "Medium_Heavy"
        }),
        ("Javelin", "Large Refueling", new[]
        {
            "Refueling_Lrg", "RefuelingLrg", "Large Refueling", "LargeRefuel", "Large_Refuel"
        }),
        // Gecko_Commercial_Transit / Industrial_Cargo / Science_Stargazer / Industrial_Salvage
        ("Gecko", "Station Hopper", new[]
        {
            "Commercial_Transit", "CommercialTransit", "Station Hopper", "StationHopper",
            "Station_Hopper", "Passenger", "Hopper"
        }),
        ("Gecko", "Heavy Cargo", new[]
        {
            "Industrial_Cargo", "IndustrialCargo", "Heavy Cargo", "HeavyCargo"
        }),
        ("Gecko", "Stargazer", new[] { "Science_Stargazer", "ScienceStargazer", "Stargazer" }),
        ("Gecko", "Salvage Runner", new[]
        {
            "Industrial_Salvage", "IndustrialSalvage", "Salvage Runner", "SalvageRunner",
            "Salvage_Runner", "Runner"
        }),
        // Index 16+ → BASE+455… (after Hab 430–449); must match salvage_tiers.SHIP_VARIANTS.
        ("Javelin", "Large Heavy Cargo", new[]
        {
            "Cargo_Lrg", "CargoLrg", "Industrial_Cargo_Lrg", "Industrial_Cargo_LRG",
            "Large Heavy", "LargeHeavy", "Large_Heavy", "Lrg_Cargo", "LrgCargo"
        }),
    };

    private const int LegacyVariantCount = 16;
    private const int VariantTierBase = 350;
    private const int VariantExtraBase = 455;

    private static int FamilyIndex(string family) =>
        family switch
        {
            "Mackerel" => 0,
            "Atlas" => 1,
            "Javelin" => 2,
            "Gecko" => 3,
            _ => 0
        };

    private static bool TryVariantTierLocation(
        string family,
        string variant,
        int tier,
        out string locationName,
        out long locationId)
    {
        locationName = "";
        locationId = 0;
        for (var i = 0; i < ShipVariants.Length; i++)
        {
            if (!string.Equals(ShipVariants[i].Family, family, StringComparison.Ordinal)
                || !string.Equals(ShipVariants[i].Variant, variant, StringComparison.Ordinal))
            {
                continue;
            }

            locationName = $"{family} {variant} Salvage Tier {tier}";
            var offset = i < LegacyVariantCount
                ? VariantTierBase + i * 5 + (tier - 1)
                : VariantExtraBase + (i - LegacyVariantCount) * 5 + (tier - 1);
            locationId = ArchipelagoClient.BaseId + offset;
            return true;
        }

        return false;
    }

    private static string? VariantFromHint(string family, string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return null;
        }

        // Prefer ModuleConstructionAsset stem — most reliable in live logs
        // e.g. Mackerel_Industrial_Cargo_ModuleConstructionAsset → Heavy Cargo.
        var h = hint!;
        var construction = ExtractConstructionStem(h);
        var search = string.IsNullOrEmpty(construction) ? h : construction + " " + h;

        // Longer / more specific needles first within family.
        string? best = null;
        var bestLen = -1;
        foreach (var entry in ShipVariants)
        {
            if (!string.Equals(entry.Family, family, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var needle in entry.Needles)
            {
                if (search.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (needle.Length > bestLen)
                {
                    bestLen = needle.Length;
                    best = entry.Variant;
                }
            }
        }

        // Javelin size disambiguation from display strings (construction stems already unique).
        if (string.Equals(family, "Javelin", StringComparison.Ordinal) && best == null)
        {
            var cargo = search.IndexOf("Cargo", StringComparison.OrdinalIgnoreCase) >= 0;
            var refuel = search.IndexOf("Refuel", StringComparison.OrdinalIgnoreCase) >= 0;
            if (cargo || refuel)
            {
                var large = search.IndexOf("Lrg", StringComparison.OrdinalIgnoreCase) >= 0
                    || search.IndexOf("Large", StringComparison.OrdinalIgnoreCase) >= 0;
                var medium = search.IndexOf("Med", StringComparison.OrdinalIgnoreCase) >= 0
                    || search.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) >= 0;
                var small = search.IndexOf("Sm", StringComparison.OrdinalIgnoreCase) >= 0
                    || search.IndexOf("Small", StringComparison.OrdinalIgnoreCase) >= 0;

                if (cargo)
                {
                    if (large)
                    {
                        return "Large Heavy Cargo";
                    }

                    if (medium)
                    {
                        return "Medium Heavy Cargo";
                    }

                    if (small)
                    {
                        return "Small Heavy Cargo";
                    }
                }
                else if (refuel)
                {
                    if (large)
                    {
                        return "Large Refueling";
                    }

                    if (medium)
                    {
                        return "Medium Refueling";
                    }

                    if (small)
                    {
                        return "Small Refueling";
                    }
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Pulls e.g. Mackerel_Industrial_Cargo from ...Mackerel_Industrial_Cargo_ModuleConstructionAsset...
    /// </summary>
    private static string ExtractConstructionStem(string hint)
    {
        const string marker = "_ModuleConstruction";
        var idx = hint.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx <= 0)
        {
            return "";
        }

        var start = hint.LastIndexOf(' ', idx);
        start = start < 0 ? 0 : start + 1;
        return hint.Substring(start, idx - start);
    }

    private static string? FamilyFromHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return null;
        }

        var h = hint!;
        if (h.IndexOf("Mackerel", StringComparison.OrdinalIgnoreCase) >= 0
            || h.IndexOf("MKRL", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Mackerel";
        }

        if (h.IndexOf("Javelin", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Javelin";
        }

        if (h.IndexOf("Gecko", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Gecko";
        }

        if (h.IndexOf("Atlas", StringComparison.OrdinalIgnoreCase) >= 0
            || h.IndexOf("Mistral", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Atlas";
        }

        return null;
    }

    private static string DescribeCurrentShip(object? postMissionScreen)
    {
        var parts = new List<string>();
        try
        {
            // Most reliable: ModuleService.CurrentShipPreview.Archetype / ConstructionAssetName.
            AppendShipPreviewHints(parts, FindCurrentShipPreview());

            if (postMissionScreen != null)
            {
                foreach (var fieldName in new[] { "m_ShipName", "m_ShipModel", "m_ShipRole" })
                {
                    var f = postMissionScreen.GetType().GetField(
                        fieldName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var tmp = f?.GetValue(postMissionScreen);
                    var text = ReadTmpText(tmp);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        parts.Add(text!);
                    }
                }
            }

            var gameAsm = FindGameAssembly();
            var mainType = gameAsm?.GetType("BBI.Unity.Game.Main");
            var main = mainType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            var session = main?.GetType()
                .GetProperty("CurrentGameSession", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(main);
            if (session != null)
            {
                foreach (var propName in new[] { "ShipPreview", "CurrentShipPreview", "ModuleConstructionData" })
                {
                    var p = session.GetType().GetProperty(
                        propName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var obj = p?.GetValue(session);
                    if (obj == null)
                    {
                        continue;
                    }

                    AppendShipPreviewHints(parts, obj);
                    parts.Add(obj.ToString() ?? "");
                }
            }
        }
        catch
        {
            // ignore
        }

        return string.Join(" ", parts);
    }

    private static object? FindCurrentShipPreview()
    {
        try
        {
            var gameAsm = FindGameAssembly();
            var msType = gameAsm?.GetType("BBI.Unity.Game.ModuleService");
            var instance = msType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            if (instance == null)
            {
                return null;
            }

            return instance.GetType()
                .GetProperty("CurrentShipPreview", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance)
                ?? instance.GetType()
                    .GetProperty("NextShipPreview", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }

    private static void AppendShipPreviewHints(List<string> parts, object? preview)
    {
        if (preview == null)
        {
            return;
        }

        foreach (var propName in new[]
                 {
                     "Archetype", "Role", "ShipName", "ConstructionAssetName", "Size", "Theme", "CompanyName"
                 })
        {
            try
            {
                var p = preview.GetType().GetProperty(
                    propName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var v = p?.GetValue(preview)?.ToString();
                if (!string.IsNullOrWhiteSpace(v))
                {
                    parts.Add(v!);
                }
            }
            catch
            {
                // ignore
            }
        }

        try
        {
            var classAsset = preview.GetType()
                .GetProperty("ShipClassAsset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(preview);
            if (classAsset != null)
            {
                parts.Add(GetUnityObjectName(classAsset));
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string ReadTmpText(object? tmp)
    {
        if (tmp == null)
        {
            return "";
        }

        try
        {
            foreach (var propName in new[] { "text", "Text" })
            {
                var textProp = tmp.GetType().GetProperty(
                    propName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var t = textProp?.GetValue(tmp)?.ToString();
                if (!string.IsNullOrWhiteSpace(t))
                {
                    return t!;
                }
            }

            foreach (var fieldName in new[] { "m_text", "m_Text" })
            {
                var f = tmp.GetType().GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var t = f?.GetValue(tmp)?.ToString();
                if (!string.IsNullOrWhiteSpace(t))
                {
                    return t!;
                }
            }
        }
        catch
        {
            // ignore
        }

        return tmp.ToString() ?? "";
    }

    private static string GetUnityObjectName(object obj)
    {
        try
        {
            var p = obj.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public);
            var n = p?.GetValue(obj)?.ToString();
            if (!string.IsNullOrWhiteSpace(n))
            {
                return n!;
            }
        }
        catch
        {
            // ignore
        }

        return obj.ToString() ?? "";
    }

    internal static void SendHabShopCheck(long id, string name) =>
        _client?.TryCheckLocation(id, name, "HabShop");

    internal static void OnHabPurchase(object[]? args)
    {
        if (_client is { HabShopSanityEnabled: false })
        {
            return;
        }

        if (ItemApplicator.SuppressHabShopChecks)
        {
            return;
        }

        object? upgrade = null;
        if (args is { Length: > 0 })
        {
            upgrade = args[0];
        }

        // Prefix path already charged + checked mapped shop rows (and skipped vanilla grant).
        if (ItemApplicator.IsUpgradePurchaseBlocked(upgrade))
        {
            return;
        }

        if (ItemApplicator.TryMapHabShopLocation(upgrade, out var id, out var name))
        {
            _client?.TryCheckLocation(id, name, "HabShop");
        }
    }

    /// <summary>
    /// Non-variant bay clears only. Hull progress uses Salvage Tier 1–5
    /// (<see cref="OnSalvageTierReached"/>); variant Clear-* locations were removed.
    /// </summary>
    internal static void OnShipClearedHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return;
        }

        var h = hint!;
        if (h.IndexOf("Ghost", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _client?.TryCheckLocation(ArchipelagoClient.BaseId + 183, "Clear a Ghost Ship", "ShipClear");
        }
    }

    internal static void OnSessionStart()
    {
        Plugin.Log.LogInfo("[HS-AP] Session start — flushing pending item applies.");
        RememberTutorialStateForShift();
        ItemApplicator.OnSessionReady();
    }

    /// <summary>
    /// Capture TutorialCompleted at bay load. Skip-tutorial starts true; tutorial shift starts false.
    /// </summary>
    internal static void RememberTutorialStateForShift()
    {
        _tutorialAlreadyDoneAtShiftStart = ReadTutorialCompleted();
        Plugin.Log.LogInfo(
            $"[HS-AP] Shift tutorial snapshot: TutorialCompleted={_tutorialAlreadyDoneAtShiftStart}");
    }

    private static bool ReadTutorialCompleted()
    {
        try
        {
            var profile = ItemApplicator.TryGetPlayerProfile();
            if (profile == null)
            {
                return false;
            }

            var prop = profile.GetType().GetProperty(
                "TutorialCompleted",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return prop?.GetValue(profile) is true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTutorialShipInPlay()
    {
        try
        {
            var preview = FindCurrentShipPreview();
            if (preview == null)
            {
                return false;
            }

            var tut = preview.GetType().GetProperty(
                          "bIsTutorialShip",
                          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                      ?? preview.GetType().GetProperty(
                          "IsTutorialShip",
                          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return tut?.GetValue(preview) is true;
        }
        catch
        {
            return false;
        }
    }

    internal static void OnCertificationChanged(object? result, object[]? args)
    {
        if (result is bool ok && !ok)
        {
            return;
        }

        if (args is { Length: > 0 } && args[0] != null && int.TryParse(args[0].ToString(), out var index))
        {
            // TrySetCertification(index) → display rank = index + 1.
            var rank = index + 1;
            Plugin.Log.LogInfo($"[HS-AP] Certification → rank {rank} (index {index})");
            ItemApplicator.OnFrontendOrProfileTouch();
            if (rank >= 5)
            {
                _client?.TryCheckLocation(ArchipelagoClient.BaseId + 139, "Reach Certification Rank 5", "Certification");
            }

            if (rank >= 10)
            {
                _client?.TryCheckLocation(ArchipelagoClient.BaseId + 140, "Reach Certification Rank 10", "Certification");
            }

            if (rank >= 15)
            {
                _client?.TryCheckLocation(ArchipelagoClient.BaseId + 146, "Reach Certification Rank 15", "Certification");
            }

            if (rank >= 20)
            {
                _client?.TryCheckLocation(ArchipelagoClient.BaseId + 147, "Reach Certification Rank 20", "Certification");
            }
        }
    }

    internal static void OnCurrencyChanged(object? instance, object[]? args)
    {
        // Signature: ChangeCurrency(AssetTypeID<CurrencyAsset> id, float amount, bool add)
        if (args is not { Length: >= 3 })
        {
            ItemApplicator.RememberController(instance);
            return;
        }

        var amount = Convert.ToSingle(args[1]);
        var add = args[2] is bool b && b;
        ItemApplicator.ObserveCurrencyChange(instance, args[0], amount, add);
    }

    private static bool TryGlassCheck(string partInfo, string source)
    {
        var lower = partInfo.ToLowerInvariant();
        return lower.Contains("glass") && TryCheck(ArchipelagoClient.BaseId + 114, "Furnace Glass", source);
    }

    private static bool TryNamedComponentChecks(string partInfo, SalvageDestination dest, string source)
    {
        if (string.IsNullOrEmpty(partInfo) || partInfo == "?")
        {
            return false;
        }

        var lower = partInfo.ToLowerInvariant();
        var sent = false;
        if (dest == SalvageDestination.Barge)
        {
            if (lower.Contains("reactor"))
            {
                sent |= TryCheck(ArchipelagoClient.BaseId + 125, "Salvage Class I Reactor", source);
            }

            if (lower.Contains("fuel") || lower.Contains("plasmafuel"))
            {
                sent |= TryCheck(ArchipelagoClient.BaseId + 122, "Salvage Fuel Tank", source);
            }

            if (lower.Contains("power") && lower.Contains("cell"))
            {
                sent |= TryCheck(ArchipelagoClient.BaseId + 124, "Salvage Power Cell", source);
            }

            if (lower.Contains("thruster") || lower.Contains("engine"))
            {
                if (lower.Contains("quasar"))
                {
                    sent |= TryCheck(ArchipelagoClient.BaseId + 131, "Salvage Quasar Thruster", source);
                }
                else
                {
                    sent |= TryCheck(ArchipelagoClient.BaseId + 130, "Salvage Thruster Class I", source);
                }
            }

            if (lower.Contains("ecu") || (lower.Contains("coolant") && lower.Contains("exchange")))
            {
                sent |= TryCheck(ArchipelagoClient.BaseId + 132, "Salvage ECU", source);
            }

            if (lower.Contains("coolant") && lower.Contains("tank"))
            {
                sent |= TryCheck(ArchipelagoClient.BaseId + 133, "Salvage Coolant Tank", source);
            }

            if (lower.Contains("airlock") && !lower.Contains("console"))
            {
                sent |= TryCheck(ArchipelagoClient.BaseId + 134, "Salvage Airlock", source);
            }

            if (lower.Contains("airlock") && lower.Contains("console"))
            {
                sent |= TryCheck(ArchipelagoClient.BaseId + 145, "Salvage Airlock Console", source);
            }

            if (lower.Contains("computer") || lower.Contains("terminal"))
            {
                sent |= TryCheck(ArchipelagoClient.BaseId + 143, "Salvage Computer Terminal", source);
            }

            if (lower.Contains("comm") || lower.Contains("antenna") || lower.Contains("array"))
            {
                sent |= TryCheck(ArchipelagoClient.BaseId + 144, "Salvage Communications Array", source);
            }
        }

        return sent;
    }

    private static bool TryAluminumCheck(string partInfo, string source)
    {
        var lower = partInfo.ToLowerInvariant();
        if (lower.Contains("titanium"))
        {
            return TryCheck(ArchipelagoClient.BaseId + 135, "Process Titanium Structure", source);
        }

        if (lower.Contains("nanocarbon") || lower.Contains("nano"))
        {
            return TryCheck(ArchipelagoClient.BaseId + 136, "Process Nanocarbon Panel", source);
        }

        if (!(lower.Contains("aluminum") || lower.Contains("aluminium") || lower.Contains("alumin")))
        {
            return false;
        }

        return TryCheck(ArchipelagoClient.BaseId + 113, "Process Aluminum Structure", source);
    }

    private static bool LooksNotable(string partInfo)
    {
        var lower = partInfo.ToLowerInvariant();
        return lower.Contains("reactor") || lower.Contains("glass") || lower.Contains("fuel")
               || lower.Contains("power") || lower.Contains("aluminum") || lower.Contains("aluminium");
    }

    private static SalvageDestination DestinationFromVolume(object? volumeInstance)
    {
        if (volumeInstance == null)
        {
            return SalvageDestination.Unknown;
        }

        var typeName = volumeInstance.GetType().Name;
        if (typeName.IndexOf("Barge", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return SalvageDestination.Barge;
        }

        if (typeName.IndexOf("Processor", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return SalvageDestination.Processor;
        }

        if (typeName.IndexOf("Furnace", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return SalvageDestination.Furnace;
        }

        // Base class: try SalvageOption property if present.
        try
        {
            var prop = volumeInstance.GetType().GetProperty(
                "SalvageOption",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var opt = prop?.GetValue(volumeInstance)?.ToString() ?? "";
            if (opt.IndexOf("Barge", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return SalvageDestination.Barge;
            }

            if (opt.IndexOf("Processor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return SalvageDestination.Processor;
            }

            if (opt.IndexOf("Furnace", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return SalvageDestination.Furnace;
            }
        }
        catch
        {
            // ignore
        }

        return SalvageDestination.Unknown;
    }

    /// <summary>
    /// HandlePositiveSalvage(EntityCommandBuffer, EntityManager, Entity, StructurePart)
    /// StructurePart is typically args[3].
    /// </summary>
    private static string DescribeStructurePart(object[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return "?";
        }

        object? part = args.Length >= 4 ? args[3] : args.LastOrDefault(a => a != null && a.GetType().Name.Contains("StructurePart"));
        if (part == null)
        {
            return args.Length >= 3 ? $"Entity={args[2]}" : "?";
        }

        try
        {
            // Common Unity / game name surfaces
            foreach (var name in new[] { "name", "Name", "DisplayName", "PartName" })
            {
                var p = part.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null)
                {
                    var v = p.GetValue(part);
                    if (v != null && !string.IsNullOrWhiteSpace(v.ToString()))
                    {
                        return $"{part.GetType().Name}:{v}";
                    }
                }
            }

            var go = part.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(part);
            if (go != null)
            {
                var n = go.GetType().GetProperty("name")?.GetValue(go);
                if (n != null)
                {
                    return $"{part.GetType().Name}:{n}";
                }
            }

            return part.ToString() ?? part.GetType().Name;
        }
        catch
        {
            return part.GetType().Name;
        }
    }

    private enum SalvageDestination
    {
        Unknown,
        Barge,
        Processor,
        Furnace
    }
}

public static class GameHookSink
{
    public static void Prefix(MethodBase __originalMethod, object? __instance, object[]? __args)
    {
        if (__originalMethod.Name == "UpdateCertXPTotals")
        {
            // Real post-mission tally only — not Hab tear-down / skip-tutorial.
            GameHooks.OnShiftComplete("UpdateCertXPTotals");
            return;
        }

        if (__originalMethod.Name == "TearDownAndLoadFrontEndAsync")
        {
            // Returning to Hab (including skip-tutorial) — do NOT count as Complete First Shift.
            ItemApplicator.OnFrontendOrProfileTouch();
        }
    }

    public static void VoidPostfix(MethodBase __originalMethod, object? __instance, object[]? __args)
    {
        switch (__originalMethod.Name)
        {
            case "HandlePositiveSalvage":
                GameHooks.OnPositiveSalvage(__instance, __args);
                break;
            case "OnDeath":
                DeathLinkHooks.OnLocalDeath(__args);
                break;
            case "ChangeCurrency":
                GameHooks.OnCurrencyChanged(__instance, __args);
                break;
            case "StartSession":
                GameHooks.OnSessionStart();
                break;
            case "PurchaseUpgrade":
                GameHooks.OnHabPurchase(__args);
                break;
        }
    }

    public static void DisplayTrainingShipPrefix(object __instance, ref int __state)
    {
        __state = 0;
        try
        {
            var field = __instance.GetType().GetField(
                "mCurrentlyAvailableShips",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            __state = Convert.ToInt32(field?.GetValue(__instance) ?? 0);
        }
        catch
        {
            __state = 0;
        }
    }

    public static void DisplayTrainingShipPostfix(object __instance, int __state)
    {
        // Prefer never hiding a catalogue that DisplayCatalogueShips already filled.
        // Also force after F9 collect / Finish Basic Training even if regen is still empty.
        if (__state <= 0 && !ItemApplicator.ShouldForceJobBoardCatalogue())
        {
            return;
        }

        ItemApplicator.ForceJobBoardCatalogueVisible(__instance, __state);
    }

    public static void ShipClaimPostfix()
    {
        GameHooks.OnShipClaimedForSpawn();
    }

    public static void ShipSpawnedPostfix()
    {
        GameHooks.LogClaimedVsBayShipPublic();
    }

    /// <summary>Block setting certification past the Progressive Cert Rank ceiling.</summary>
    public static bool TrySetCertificationPrefix(int index, bool isDebug, ref bool __result)
    {
        // index is 0-based asset index; ceiling compares display ranks.
        if (ItemApplicator.AllowCertificationTarget(index + 1))
        {
            return true;
        }

        __result = false;
        return false;
    }

    /// <summary>Block vanilla rank-ups that would pass a locked milestone.</summary>
    public static bool TryIncreaseCertificationPrefix(bool isDebug, ref bool __result)
    {
        var current = ItemApplicator.ReadCurrentCertificationRank();
        if (ItemApplicator.AllowCertificationTarget(current + 1))
        {
            return true;
        }

        __result = false;
        return false;
    }

    public static void RewardTierPostfix(object __instance, object ev)
    {
        GameHooks.OnSalvageTierReached(__instance, ev);
    }

    /// <summary>Block applying Hab-yellow upgrades until the AP item grants them.</summary>
    public static bool ApplyUpgradePrefix(object __instance)
    {
        if (ItemApplicator.ShouldBlockUpgradeApply(__instance))
        {
            return false;
        }

        return true;
    }

    public static void ApplyUpgradesPostfix()
    {
        ItemApplicator.ReapplyApGrantedUpgrades();
    }

    public static void UpgradeScreenEnablePrefix()
    {
        ItemApplicator.EnsureHabShopPaidState();
        ItemApplicator.StripUnpaidShopRowsFromHabOwned();
    }

    /// <summary>
    /// Hab shows the cert-lock badge only when CanPurchase's out result is InvalidCertification.
    /// Force that visual when rank is below RequiredTier for shop-mapped rows.
    /// </summary>
    public static void DrawUnpurchasedUpgradePostfix(object __instance)
    {
        ItemApplicator.EnsureHabRankLockVisual(__instance);
    }

    /// <summary>
    /// Sets bool result + UpgradePurchaseResult out (as int). InvalidCertification (1) is
    /// required for UpgradeTreeButton to show the rank-lock badge.
    /// </summary>
    public static bool CanPurchaseUpgradePrefix(object upgrade, ref int result, ref bool __result)
    {
        if (ItemApplicator.TryEvaluateHabShopCanPurchase(upgrade, out var canPurchase, out var purchaseResult))
        {
            __result = canPurchase;
            result = purchaseResult;
            if (!canPurchase && purchaseResult == 0
                && ItemApplicator.IsUpgradePurchaseBlocked(upgrade)
                && ItemApplicator.ShouldLogPurchaseBlock(upgrade))
            {
                Plugin.Log.LogInfo($"[HS-AP] Hab upgrade already bought (yellow): {upgrade}");
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Shop-sanity: charge + location check, skip vanilla grant so AP items control tools.
    /// </summary>
    public static bool PurchaseUpgradePrefix(object upgrade)
    {
        if (!ItemApplicator.TryHandleHabShopSanityPurchase(upgrade, out var id, out var name))
        {
            return true;
        }

        GameHooks.SendHabShopCheck(id, name);
        return false;
    }

    public static void AccessibleShipClassesPostfix(
        ref object? currentHighestAvailableShipClass,
        ref object? highestAvailableShipClassDuringLastRefresh)
    {
        ItemApplicator.AdjustAccessibleShipClasses(
            ref currentHighestAvailableShipClass,
            ref highestAvailableShipClassDuringLastRefresh);
    }

    /// <summary>Block tether deploy without AP Tether Module (tutorial exempt).</summary>
    public static bool TetherCreatePrefix(ref bool __state)
    {
        __state = GameHooks.IsTutorialContext() || ItemApplicator.HasTetherModule;
        if (__state)
        {
            return true;
        }

        GameHooks.LogBayBlockOnce("tether", "Tether deploy blocked — need AP item 'Tether Module'.");
        return false;
    }

    public static void TetherCreatePostfix(bool __state)
    {
        if (__state)
        {
            GameHooks.OnFirstTetherPlaced();
        }
    }

    public static void DataDriveIdentifyPostfix()
    {
        GameHooks.OnDataDriveRecovered();
    }

    /// <summary>Block demo place/throw without AP Demo Charge License.</summary>
    public static bool DemoPlacePrefix()
    {
        if (GameHooks.IsTutorialContext() || ItemApplicator.HasDemoLicense)
        {
            return true;
        }

        GameHooks.LogBayBlockOnce("demo", "Demo Charge blocked — need AP item 'Demo Charge License'.");
        return false;
    }

    /// <summary>
    /// Unused: DoPush is not patched. Kept so old Harmony method names don't confuse diffs.
    /// Charged Push AP item still controls upgrade apply / progressive force flush.
    /// </summary>
    public static bool ChargedPushPrefix() => true;

    public static void Postfix(MethodBase __originalMethod, object? __instance, object[]? __args, object? __result)
    {
        switch (__originalMethod.Name)
        {
            case "IsCorrectSalvageOption":
                if (__result is true)
                {
                    GameHooks.OnCorrectSalvageOption(__instance, __args);
                }

                break;
            case "GetEvent" when __originalMethod.DeclaringType?.Name == "LevelCompleteEvent":
                // Shift timer / level complete — first-shift only. Ship clears wait for
                // UpdateCertXPTotals + IsBayCleared (post-mission) to avoid false clears.
                GameHooks.OnShiftCompleteFirstShiftOnly("LevelCompleteEvent");
                break;
            case "WaitToRespawnPlayer":
                // Respawn after death — softer than OnDeath; useful if OnDeath patch fails.
                DeathLinkHooks.OnLocalDeath(__args);
                break;
            case "TrySetCertification":
                GameHooks.OnCertificationChanged(__result, __args);
                break;
            case "TryIncreaseCertification":
                GameHooks.OnCertificationChanged(__result, __args);
                // Career MP rank-ups only top up 1–2 new-class ships until the shift timer;
                // force a full bay regen so the catalogue replaces the whole list.
                if (__result is true)
                {
                    ItemApplicator.RequestFullJobBoardRefreshAfterRankUp();
                }

                break;
        }
    }
}
