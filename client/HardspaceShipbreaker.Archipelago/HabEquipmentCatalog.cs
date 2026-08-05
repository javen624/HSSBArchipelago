using System;
using System.Collections.Generic;

namespace HardspaceShipbreaker.Archipelago;

/// <summary>
/// Maps Hab equipment AP items / shop locations to Unity UpgradeAsset name needles.
/// Keep in sync with worlds/HardspaceShipbreaker/equipment.py.
/// </summary>
internal static class HabEquipmentCatalog
{
    public readonly struct Entry
    {
        public Entry(string itemName, long locationId, string locationName, Func<string, bool> assetMatch)
        {
            ItemName = itemName;
            LocationId = locationId;
            LocationName = locationName;
            AssetMatch = assetMatch;
        }

        public string ItemName { get; }
        public long LocationId { get; }
        public string LocationName { get; }
        public Func<string, bool> AssetMatch { get; }
    }

    private static long Id(int offset) => ArchipelagoClient.BaseId + offset;

    private static bool Has(string n, string a) =>
        n.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsTutorial(string n) =>
        Has(n, "Tutorial") || Has(n, "TUTORIAL");

    /// <summary>
    /// Name-based free starters (also see StartsPurchased on the asset).
    /// UnlockPush ≠ Charged Push; Structure scanner mode is free (not Hab: Scanner Objects).
    /// </summary>
    public static bool IsFreeStarterUpgradeName(string n) =>
        !string.IsNullOrEmpty(n)
        && ((Has(n, "UnlockPush") && !Has(n, "Charged"))
            || Has(n, "ScannerMode_Structure"));

    private static bool IsHelmetO2Capacity(string n, int tier, Func<string, int, bool> nameEndsWithTier) =>
        !IsTutorial(n)
        && Has(n, "HelmetTankCapacity")
        && nameEndsWithTier(n, tier);

    private static bool IsHelmetO2RechargeModule(string n) =>
        !IsTutorial(n)
        && (Has(n, "HelmetRechargeUnlock")
            || (Has(n, "Helmet") && Has(n, "Recharge") && Has(n, "Unlock") && !Has(n, "Rate")));

    private static bool IsHelmetO2RechargeRate(string n, int tier, Func<string, int, bool> nameEndsWithTier) =>
        !IsTutorial(n)
        && Has(n, "HelmetRechargeRate")
        && nameEndsWithTier(n, tier);

    public static IReadOnlyList<Entry> Build(Func<string, int, bool> nameEndsWithTier)
    {
        bool T(string n, string family, int tier) =>
            Has(n, family) && nameEndsWithTier(n, tier) && !Has(n, "Tutorial") && !Has(n, "TUTORIAL");

        bool Strength(string n, int tier) =>
            Has(n, "Grapple") && Has(n, "Strength") && nameEndsWithTier(n, tier)
            && !Has(n, "Durability") && !Has(n, "Drain");

        var list = new List<Entry>
        {
            new("Tether Module", Id(200), "Hab: Unlock Tethers",
                n => Has(n, "UnlockTether") || (Has(n, "Tether") && Has(n, "Unlock") && !Has(n, "Amount") && !Has(n, "Lifetime"))),
            new("Grapple Strength 1", Id(201), "Hab: Grapple Strength 1", n => Strength(n, 1)),
            new("Grapple Strength 2", Id(202), "Hab: Grapple Strength 2", n => Strength(n, 2)),
            new("Grapple Strength 3", Id(203), "Hab: Grapple Strength 3", n => Strength(n, 3)),
            new("Grapple Strength 4", Id(204), "Hab: Grapple Strength 4", n => Strength(n, 4)),
            new("Grapple Strength 5", Id(205), "Hab: Grapple Strength 5", n => Strength(n, 5)),
            new("Tethers Amount 1", Id(206), "Hab: Tethers Amount 1", n => T(n, "TethersAmount", 1) || (Has(n, "Tether") && Has(n, "Amount") && nameEndsWithTier(n, 1) && !Has(n, "Tutorial"))),
            new("Tethers Amount 2", Id(207), "Hab: Tethers Amount 2", n => T(n, "TethersAmount", 2) || (Has(n, "Tether") && Has(n, "Amount") && nameEndsWithTier(n, 2))),
            new("Tethers Amount 3", Id(208), "Hab: Tethers Amount 3", n => T(n, "TethersAmount", 3) || (Has(n, "Tether") && Has(n, "Amount") && nameEndsWithTier(n, 3))),
            new("Tethers Lifetime 1", Id(209), "Hab: Tethers Lifetime 1", n => T(n, "TethersLifetime", 1) || (Has(n, "Tether") && Has(n, "Lifetime") && nameEndsWithTier(n, 1))),
            new("Tethers Lifetime 2", Id(210), "Hab: Tethers Lifetime 2", n => T(n, "TethersLifetime", 2) || (Has(n, "Tether") && Has(n, "Lifetime") && nameEndsWithTier(n, 2))),
            new("Tethers Lifetime 3", Id(219), "Hab: Tethers Lifetime 3", n => T(n, "TethersLifetime", 3) || (Has(n, "Tether") && Has(n, "Lifetime") && nameEndsWithTier(n, 3))),
            new("Tethers Lifetime 4", Id(220), "Hab: Tethers Lifetime 4", n => T(n, "TethersLifetime", 4) || (Has(n, "Tether") && Has(n, "Lifetime") && nameEndsWithTier(n, 4))),
            new("Demo Charge License", Id(211), "Hab: Unlock Demo Charge",
                n => Has(n, "UnlockDemo") || (Has(n, "Demo") && Has(n, "Unlock"))),
            // UnlockPush_* is the free/rank-1 starter — not Charged Push. Only UnlockChargedPush.
            new("Charged Push", Id(221), "Hab: Charged Push",
                n => (Has(n, "ChargedPush") || Has(n, "UnlockChargedPush"))
                     && !Has(n, "Force") && !Has(n, "Amount") && !Has(n, "Capacity")
                     && !IsFreeStarterUpgradeName(n)),
            // Structure is a free starter mode; Objects is the Hab shop / progressive tier.
            new("Scanner Objects", Id(213), "Hab: Scanner Objects",
                n => Has(n, "Scanner") && Has(n, "Object") && !Has(n, "Structure") && !Has(n, "System")
                     && !Has(n, "Range") && !Has(n, "Durability") && !Has(n, "Purchase")
                     && !IsFreeStarterUpgradeName(n)),
            new("Scanner Systems", Id(214), "Hab: Scanner Systems",
                n => Has(n, "Scanner") && Has(n, "System") && !Has(n, "Range")
                     && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Scanner Range 1", Id(222), "Hab: Scanner Range 1",
                n => Has(n, "Scanner") && Has(n, "Range") && nameEndsWithTier(n, 1) && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Scanner Range 2", Id(223), "Hab: Scanner Range 2",
                n => Has(n, "Scanner") && Has(n, "Range") && nameEndsWithTier(n, 2) && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Scanner Range 3", Id(224), "Hab: Scanner Range 3",
                n => Has(n, "Scanner") && Has(n, "Range") && nameEndsWithTier(n, 3) && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Scanner Range 4", Id(225), "Hab: Scanner Range 4",
                n => Has(n, "Scanner") && Has(n, "Range") && nameEndsWithTier(n, 4) && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Scanner Range 5", Id(226), "Hab: Scanner Range 5",
                n => Has(n, "Scanner") && Has(n, "Range") && nameEndsWithTier(n, 5) && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Suit Integrity 1", Id(215), "Hab: Suit Integrity 1",
                n => Has(n, "Suit") && (Has(n, "Defence") || Has(n, "Defense") || Has(n, "Integrity"))
                     && nameEndsWithTier(n, 1) && !Has(n, "Durability") && !Has(n, "Shield") && !Has(n, "Purchase")),
            new("Suit Integrity 2", Id(216), "Hab: Suit Integrity 2",
                n => Has(n, "Suit") && (Has(n, "Defence") || Has(n, "Defense") || Has(n, "Integrity"))
                     && nameEndsWithTier(n, 2) && !Has(n, "Durability") && !Has(n, "Shield") && !Has(n, "Purchase")),
            new("Suit Integrity 3", Id(227), "Hab: Suit Integrity 3",
                n => Has(n, "Suit") && (Has(n, "Defence") || Has(n, "Defense") || Has(n, "Integrity"))
                     && nameEndsWithTier(n, 3) && !Has(n, "Durability") && !Has(n, "Shield") && !Has(n, "Purchase")),
            new("Suit Integrity 4", Id(228), "Hab: Suit Integrity 4",
                n => Has(n, "Suit") && (Has(n, "Defence") || Has(n, "Defense") || Has(n, "Integrity"))
                     && nameEndsWithTier(n, 4) && !Has(n, "Durability") && !Has(n, "Shield") && !Has(n, "Purchase")),
            new("Suit Integrity 5", Id(229), "Hab: Suit Integrity 5",
                n => Has(n, "Suit") && (Has(n, "Defence") || Has(n, "Defense") || Has(n, "Integrity"))
                     && nameEndsWithTier(n, 5) && !Has(n, "Durability") && !Has(n, "Shield") && !Has(n, "Purchase")),
            new("Heat Resistance 1", Id(230), "Hab: Heat Resistance 1",
                n => Has(n, "Heat") && (Has(n, "Shield") || Has(n, "Resist")) && nameEndsWithTier(n, 1)),
            new("Heat Resistance 2", Id(231), "Hab: Heat Resistance 2",
                n => Has(n, "Heat") && (Has(n, "Shield") || Has(n, "Resist")) && nameEndsWithTier(n, 2)),
            new("Heat Resistance 3", Id(232), "Hab: Heat Resistance 3",
                n => Has(n, "Heat") && (Has(n, "Shield") || Has(n, "Resist")) && nameEndsWithTier(n, 3)),
            new("Heat Resistance 4", Id(233), "Hab: Heat Resistance 4",
                n => Has(n, "Heat") && (Has(n, "Shield") || Has(n, "Resist")) && nameEndsWithTier(n, 4)),
            new("Heat Resistance 5", Id(234), "Hab: Heat Resistance 5",
                n => Has(n, "Heat") && (Has(n, "Shield") || Has(n, "Resist")) && nameEndsWithTier(n, 5)),
            new("Cryo Resistance 1", Id(235), "Hab: Cryo Resistance 1",
                n => Has(n, "Cryo") && (Has(n, "Shield") || Has(n, "Resist")) && nameEndsWithTier(n, 1)),
            new("Cryo Resistance 2", Id(236), "Hab: Cryo Resistance 2",
                n => Has(n, "Cryo") && (Has(n, "Shield") || Has(n, "Resist")) && nameEndsWithTier(n, 2)),
            new("Cryo Resistance 3", Id(237), "Hab: Cryo Resistance 3",
                n => Has(n, "Cryo") && (Has(n, "Shield") || Has(n, "Resist")) && nameEndsWithTier(n, 3)),
            new("Cryo Resistance 4", Id(238), "Hab: Cryo Resistance 4",
                n => Has(n, "Cryo") && (Has(n, "Shield") || Has(n, "Resist")) && nameEndsWithTier(n, 4)),
            new("Cryo Resistance 5", Id(239), "Hab: Cryo Resistance 5",
                n => Has(n, "Cryo") && (Has(n, "Shield") || Has(n, "Resist")) && nameEndsWithTier(n, 5)),
            new("Electrical Resistance 1", Id(240), "Hab: Electrical Resistance 1",
                n => (Has(n, "Electric") || Has(n, "Electrical")) && (Has(n, "Shield") || Has(n, "Resist"))
                     && nameEndsWithTier(n, 1)),
            new("Electrical Resistance 2", Id(241), "Hab: Electrical Resistance 2",
                n => (Has(n, "Electric") || Has(n, "Electrical")) && (Has(n, "Shield") || Has(n, "Resist"))
                     && nameEndsWithTier(n, 2)),
            new("Electrical Resistance 3", Id(242), "Hab: Electrical Resistance 3",
                n => (Has(n, "Electric") || Has(n, "Electrical")) && (Has(n, "Shield") || Has(n, "Resist"))
                     && nameEndsWithTier(n, 3)),
            new("Electrical Resistance 4", Id(243), "Hab: Electrical Resistance 4",
                n => (Has(n, "Electric") || Has(n, "Electrical")) && (Has(n, "Shield") || Has(n, "Resist"))
                     && nameEndsWithTier(n, 4)),
            new("Electrical Resistance 5", Id(244), "Hab: Electrical Resistance 5",
                n => (Has(n, "Electric") || Has(n, "Electrical")) && (Has(n, "Shield") || Has(n, "Resist"))
                     && nameEndsWithTier(n, 5)),
            new("Cutter Heat Capacity 1", Id(217), "Hab: Cutter Heat 1", n => Has(n, "Cutter") && Has(n, "Heat") && nameEndsWithTier(n, 1)),
            new("Cutter Heat Capacity 2", Id(245), "Hab: Cutter Heat Capacity 2", n => Has(n, "Cutter") && Has(n, "Heat") && nameEndsWithTier(n, 2)),
            new("Cutter Heat Capacity 3", Id(246), "Hab: Cutter Heat Capacity 3", n => Has(n, "Cutter") && Has(n, "Heat") && nameEndsWithTier(n, 3)),
            new("Cutter Cooldown 1", Id(247), "Hab: Cutter Cooldown 1", n => Has(n, "Cutter") && Has(n, "Cooldown") && nameEndsWithTier(n, 1)),
            new("Cutter Cooldown 2", Id(248), "Hab: Cutter Cooldown 2", n => Has(n, "Cutter") && Has(n, "Cooldown") && nameEndsWithTier(n, 2)),
            new("Cutter Cooldown 3", Id(249), "Hab: Cutter Cooldown 3", n => Has(n, "Cutter") && Has(n, "Cooldown") && nameEndsWithTier(n, 3)),
            new("Stinger Range 1", Id(250), "Hab: Stinger Range 1", n => Has(n, "Stinger") && Has(n, "Range") && nameEndsWithTier(n, 1)),
            new("Stinger Range 2", Id(251), "Hab: Stinger Range 2", n => Has(n, "Stinger") && Has(n, "Range") && nameEndsWithTier(n, 2)),
            new("Stinger Range 3", Id(252), "Hab: Stinger Range 3", n => Has(n, "Stinger") && Has(n, "Range") && nameEndsWithTier(n, 3)),
            new("Stinger Range 4", Id(253), "Hab: Stinger Range 4", n => Has(n, "Stinger") && Has(n, "Range") && nameEndsWithTier(n, 4)),
            new("Stinger Range 5", Id(254), "Hab: Stinger Range 5", n => Has(n, "Stinger") && Has(n, "Range") && nameEndsWithTier(n, 5)),
            new("Splitsaw Range 1", Id(255), "Hab: Splitsaw Range 1", n => Has(n, "Splitsaw") && Has(n, "Range") && nameEndsWithTier(n, 1)),
            new("Splitsaw Range 2", Id(256), "Hab: Splitsaw Range 2", n => Has(n, "Splitsaw") && Has(n, "Range") && nameEndsWithTier(n, 2)),
            new("Splitsaw Range 3", Id(257), "Hab: Splitsaw Range 3", n => Has(n, "Splitsaw") && Has(n, "Range") && nameEndsWithTier(n, 3)),
            new("Splitsaw Range 4", Id(258), "Hab: Splitsaw Range 4", n => Has(n, "Splitsaw") && Has(n, "Range") && nameEndsWithTier(n, 4)),
            new("Splitsaw Range 5", Id(259), "Hab: Splitsaw Range 5", n => Has(n, "Splitsaw") && Has(n, "Range") && nameEndsWithTier(n, 5)),
            new("Grapple Range 1", Id(260), "Hab: Grapple Range 1", n => Has(n, "Grapple") && Has(n, "Range") && nameEndsWithTier(n, 1) && !Has(n, "Strength")),
            new("Grapple Range 2", Id(261), "Hab: Grapple Range 2", n => Has(n, "Grapple") && Has(n, "Range") && nameEndsWithTier(n, 2) && !Has(n, "Strength")),
            new("Grapple Range 3", Id(262), "Hab: Grapple Range 3", n => Has(n, "Grapple") && Has(n, "Range") && nameEndsWithTier(n, 3) && !Has(n, "Strength")),
            new("Grapple Range 4", Id(263), "Hab: Grapple Range 4", n => Has(n, "Grapple") && Has(n, "Range") && nameEndsWithTier(n, 4) && !Has(n, "Strength")),
            new("Grapple Range 5", Id(264), "Hab: Grapple Range 5", n => Has(n, "Grapple") && Has(n, "Range") && nameEndsWithTier(n, 5) && !Has(n, "Strength")),
            new("Charged Push Force 1", Id(265), "Hab: Charged Push Force 1", n => Has(n, "Push") && (Has(n, "Force") || Has(n, "Charge")) && nameEndsWithTier(n, 1) && !Has(n, "Unlock")),
            new("Charged Push Force 2", Id(266), "Hab: Charged Push Force 2", n => Has(n, "Push") && (Has(n, "Force") || Has(n, "Charge")) && nameEndsWithTier(n, 2) && !Has(n, "Unlock")),
            new("Charged Push Force 3", Id(267), "Hab: Charged Push Force 3", n => Has(n, "Push") && (Has(n, "Force") || Has(n, "Charge")) && nameEndsWithTier(n, 3) && !Has(n, "Unlock")),
            new("Demo Charges Capacity 1", Id(218), "Hab: Demo Charges Capacity 1", n => Has(n, "Demo") && (Has(n, "Capacity") || Has(n, "Amount")) && nameEndsWithTier(n, 1)),
            new("Demo Charges Capacity 2", Id(268), "Hab: Demo Charges Capacity 2", n => Has(n, "Demo") && (Has(n, "Capacity") || Has(n, "Amount")) && nameEndsWithTier(n, 2)),
            new("Demo Charges Capacity 3", Id(269), "Hab: Demo Charges Capacity 3", n => Has(n, "Demo") && (Has(n, "Capacity") || Has(n, "Amount")) && nameEndsWithTier(n, 3)),
            new("Demo Charges Capacity 4", Id(270), "Hab: Demo Charges Capacity 4", n => Has(n, "Demo") && (Has(n, "Capacity") || Has(n, "Amount")) && nameEndsWithTier(n, 4)),
            new("Demo Charges Capacity 5", Id(271), "Hab: Demo Charges Capacity 5", n => Has(n, "Demo") && (Has(n, "Capacity") || Has(n, "Amount")) && nameEndsWithTier(n, 5)),
            new("Demo Disarming 1", Id(272), "Hab: Demo Disarming 1", n => Has(n, "Demo") && Has(n, "Disarm") && nameEndsWithTier(n, 1)),
            new("Demo Disarming 2", Id(273), "Hab: Demo Disarming 2", n => Has(n, "Demo") && Has(n, "Disarm") && nameEndsWithTier(n, 2)),
            new("Demo Disarming 3", Id(274), "Hab: Demo Disarming 3", n => Has(n, "Demo") && Has(n, "Disarm") && nameEndsWithTier(n, 3)),
            new("Demo Self Cleanup 1", Id(275), "Hab: Demo Self Cleanup 1", n => Has(n, "Demo") && (Has(n, "Cleanup") || Has(n, "Clean")) && nameEndsWithTier(n, 1)),
            new("Demo Self Cleanup 2", Id(276), "Hab: Demo Self Cleanup 2", n => Has(n, "Demo") && (Has(n, "Cleanup") || Has(n, "Clean")) && nameEndsWithTier(n, 2)),
            new("Demo Self Cleanup 3", Id(277), "Hab: Demo Self Cleanup 3", n => Has(n, "Demo") && (Has(n, "Cleanup") || Has(n, "Clean")) && nameEndsWithTier(n, 3)),
            new("Demo Auto-Deploy", Id(278), "Hab: Demo Auto-Deploy",
                n => Has(n, "Demo") && Has(n, "Auto")),
            new("O2 Capacity 1", Id(279), "Hab: O2 Capacity 1",
                n => IsHelmetO2Capacity(n, 1, nameEndsWithTier)),
            new("O2 Capacity 2", Id(280), "Hab: O2 Capacity 2",
                n => IsHelmetO2Capacity(n, 2, nameEndsWithTier)),
            new("O2 Capacity 3", Id(281), "Hab: O2 Capacity 3",
                n => IsHelmetO2Capacity(n, 3, nameEndsWithTier)),
            new("O2 Capacity 4", Id(282), "Hab: O2 Capacity 4",
                n => IsHelmetO2Capacity(n, 4, nameEndsWithTier)),
            new("O2 Capacity 5", Id(283), "Hab: O2 Capacity 5",
                n => IsHelmetO2Capacity(n, 5, nameEndsWithTier)),
            new("O2 Recharge Module", Id(284), "Hab: O2 Recharge Module",
                n => IsHelmetO2RechargeModule(n)),
            new("O2 Recharge 1", Id(285), "Hab: O2 Recharge 1",
                n => IsHelmetO2RechargeRate(n, 1, nameEndsWithTier)),
            new("O2 Recharge 2", Id(286), "Hab: O2 Recharge 2",
                n => IsHelmetO2RechargeRate(n, 2, nameEndsWithTier)),
            new("O2 Recharge 3", Id(287), "Hab: O2 Recharge 3",
                n => IsHelmetO2RechargeRate(n, 3, nameEndsWithTier)),
            new("Thruster Top Speed 1", Id(288), "Hab: Thruster Top Speed 1",
                n => Has(n, "Thruster") && Has(n, "Speed") && nameEndsWithTier(n, 1) && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Thruster Top Speed 2", Id(289), "Hab: Thruster Top Speed 2",
                n => Has(n, "Thruster") && Has(n, "Speed") && nameEndsWithTier(n, 2) && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Thruster Top Speed 3", Id(290), "Hab: Thruster Top Speed 3",
                n => Has(n, "Thruster") && Has(n, "Speed") && nameEndsWithTier(n, 3) && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Thruster Braking 1", Id(291), "Hab: Thruster Braking 1",
                n => Has(n, "Thruster") && Has(n, "Brak") && nameEndsWithTier(n, 1) && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Thruster Braking 2", Id(292), "Hab: Thruster Braking 2",
                n => Has(n, "Thruster") && Has(n, "Brak") && nameEndsWithTier(n, 2) && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Thruster Braking 3", Id(293), "Hab: Thruster Braking 3",
                n => Has(n, "Thruster") && Has(n, "Brak") && nameEndsWithTier(n, 3) && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Thruster Fuel Capacity 1", Id(294), "Hab: Thruster Fuel Capacity 1",
                n => Has(n, "Thruster") && Has(n, "Fuel") && nameEndsWithTier(n, 1) && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Thruster Fuel Capacity 2", Id(295), "Hab: Thruster Fuel Capacity 2",
                n => Has(n, "Thruster") && Has(n, "Fuel") && nameEndsWithTier(n, 2) && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Thruster Fuel Capacity 3", Id(296), "Hab: Thruster Fuel Capacity 3",
                n => Has(n, "Thruster") && Has(n, "Fuel") && nameEndsWithTier(n, 3) && !Has(n, "Durability") && !Has(n, "Purchase")),
            new("Audio Resynth 1", Id(297), "Hab: Audio Resynth 1",
                n => (Has(n, "Audio") || Has(n, "Resynth") || Has(n, "SuitResynth")) && nameEndsWithTier(n, 1)
                     && !Has(n, "Shield") && !Has(n, "Defence") && !Has(n, "Defense")),
            new("Audio Resynth 2", Id(298), "Hab: Audio Resynth 2",
                n => (Has(n, "Audio") || Has(n, "Resynth") || Has(n, "SuitResynth")) && nameEndsWithTier(n, 2)
                     && !Has(n, "Shield") && !Has(n, "Defence") && !Has(n, "Defense")),
            new("Audio Resynth 3", Id(299), "Hab: Audio Resynth 3",
                n => (Has(n, "Audio") || Has(n, "Resynth") || Has(n, "SuitResynth")) && nameEndsWithTier(n, 3)
                     && !Has(n, "Shield") && !Has(n, "Defence") && !Has(n, "Defense")),
            // Rentals (*Purchase*) + durability drains — shop-sanity
            new("Thruster Rental", Id(320), "Hab: Thruster Rental",
                n => Has(n, "Thruster") && Has(n, "Purchase") && !Has(n, "Durability")),
            new("Thruster Durability 1", Id(321), "Hab: Thruster Durability 1",
                n => Has(n, "Thruster") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 1)),
            new("Thruster Durability 2", Id(322), "Hab: Thruster Durability 2",
                n => Has(n, "Thruster") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 2)),
            new("Thruster Durability 3", Id(323), "Hab: Thruster Durability 3",
                n => Has(n, "Thruster") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 3)),
            new("Thruster Durability 4", Id(324), "Hab: Thruster Durability 4",
                n => Has(n, "Thruster") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 4)),
            new("Thruster Durability 5", Id(325), "Hab: Thruster Durability 5",
                n => Has(n, "Thruster") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 5)),
            new("Cutter Rental", Id(326), "Hab: Cutter Rental",
                n => Has(n, "Cutter") && (Has(n, "Purchase") || (Has(n, "Rental") && Has(n, "Upgrade"))) && !Has(n, "Durability") && !Has(n, "Cost")),
            new("Cutter Durability 1", Id(327), "Hab: Cutter Durability 1",
                n => Has(n, "Cutter") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 1)),
            new("Cutter Durability 2", Id(328), "Hab: Cutter Durability 2",
                n => Has(n, "Cutter") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 2)),
            new("Cutter Durability 3", Id(329), "Hab: Cutter Durability 3",
                n => Has(n, "Cutter") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 3)),
            new("Cutter Durability 4", Id(330), "Hab: Cutter Durability 4",
                n => Has(n, "Cutter") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 4)),
            new("Cutter Durability 5", Id(331), "Hab: Cutter Durability 5",
                n => Has(n, "Cutter") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 5)),
            new("Grapple Rental", Id(332), "Hab: Grapple Rental",
                n => Has(n, "Grapple") && Has(n, "Purchase") && !Has(n, "Durability")),
            new("Grapple Durability 1", Id(333), "Hab: Grapple Durability 1",
                n => Has(n, "Grapple") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 1)),
            new("Grapple Durability 2", Id(334), "Hab: Grapple Durability 2",
                n => Has(n, "Grapple") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 2)),
            new("Grapple Durability 3", Id(335), "Hab: Grapple Durability 3",
                n => Has(n, "Grapple") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 3)),
            new("Grapple Durability 4", Id(336), "Hab: Grapple Durability 4",
                n => Has(n, "Grapple") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 4)),
            new("Grapple Durability 5", Id(337), "Hab: Grapple Durability 5",
                n => Has(n, "Grapple") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 5)),
            new("Scanner Rental", Id(338), "Hab: Scanner Rental",
                n => Has(n, "Scanner") && Has(n, "Purchase") && !Has(n, "Durability")),
            new("Scanner Durability 1", Id(339), "Hab: Scanner Durability 1",
                n => Has(n, "Scanner") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 1)),
            new("Scanner Durability 2", Id(340), "Hab: Scanner Durability 2",
                n => Has(n, "Scanner") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 2)),
            new("Scanner Durability 3", Id(341), "Hab: Scanner Durability 3",
                n => Has(n, "Scanner") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 3)),
            new("Scanner Durability 4", Id(342), "Hab: Scanner Durability 4",
                n => Has(n, "Scanner") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 4)),
            new("Scanner Durability 5", Id(343), "Hab: Scanner Durability 5",
                n => Has(n, "Scanner") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 5)),
            new("Helmet Rental", Id(344), "Hab: Helmet Rental",
                n => Has(n, "Helmet") && Has(n, "Purchase") && !Has(n, "Tank") && !Has(n, "Recharge")),
            new("Suit Rental", Id(345), "Hab: Suit Rental",
                n => Has(n, "Suit") && Has(n, "Purchase") && !Has(n, "Durability") && !Has(n, "Integrity")),
            new("Suit Durability 1", Id(346), "Hab: Suit Durability 1",
                n => Has(n, "SuitDurability") && nameEndsWithTier(n, 1)),
            new("Suit Durability 2", Id(347), "Hab: Suit Durability 2",
                n => Has(n, "SuitDurability") && nameEndsWithTier(n, 2)),
            new("Suit Durability 3", Id(348), "Hab: Suit Durability 3",
                n => Has(n, "SuitDurability") && nameEndsWithTier(n, 3)),
            new("Demo Charge Rental", Id(349), "Hab: Demo Charge Rental",
                n => Has(n, "Demo") && Has(n, "Purchase") && !Has(n, "Durability") && !Has(n, "Capacity")),
            new("Demo Durability 1", Id(350), "Hab: Demo Durability 1",
                n => Has(n, "Demo") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 1)),
            new("Demo Durability 2", Id(351), "Hab: Demo Durability 2",
                n => Has(n, "Demo") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 2)),
            new("Demo Durability 3", Id(352), "Hab: Demo Durability 3",
                n => Has(n, "Demo") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 3)),
            new("Demo Durability 4", Id(353), "Hab: Demo Durability 4",
                n => Has(n, "Demo") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 4)),
            new("Demo Durability 5", Id(354), "Hab: Demo Durability 5",
                n => Has(n, "Demo") && Has(n, "DurabilityDrain") && nameEndsWithTier(n, 5)),
        };
        return list;
    }

    /// <summary>
    /// Progressive AP item → ordered tier keys matching <see cref="Entry.ItemName"/>.
    /// Keep in sync with equipment.PROGRESSIVE_EQUIPMENT.
    /// </summary>
    public static readonly Dictionary<string, string[]> ProgressiveTiers = new(StringComparer.Ordinal)
    {
        ["Progressive Grapple Strength"] = new[]
        {
            "Grapple Strength 1", "Grapple Strength 2", "Grapple Strength 3", "Grapple Strength 4", "Grapple Strength 5"
        },
        ["Progressive Tether Amount"] = new[] { "Tethers Amount 1", "Tethers Amount 2", "Tethers Amount 3" },
        ["Progressive Tether Lifetime"] = new[]
        {
            "Tethers Lifetime 1", "Tethers Lifetime 2", "Tethers Lifetime 3", "Tethers Lifetime 4"
        },
        ["Progressive Tethers"] = new[] // legacy combined
        {
            "Tethers Amount 1", "Tethers Amount 2", "Tethers Amount 3", "Tethers Lifetime 1"
        },
        ["Progressive Scanner"] = new[] { "Scanner Objects", "Scanner Systems" },
        ["Progressive Scanner Range"] = new[]
        {
            "Scanner Range 1", "Scanner Range 2", "Scanner Range 3", "Scanner Range 4", "Scanner Range 5"
        },
        ["Progressive Suit Integrity"] = new[]
        {
            "Suit Integrity 1", "Suit Integrity 2", "Suit Integrity 3", "Suit Integrity 4", "Suit Integrity 5"
        },
        ["Progressive Heat Resistance"] = new[]
        {
            "Heat Resistance 1", "Heat Resistance 2", "Heat Resistance 3", "Heat Resistance 4", "Heat Resistance 5"
        },
        ["Progressive Cryo Resistance"] = new[]
        {
            "Cryo Resistance 1", "Cryo Resistance 2", "Cryo Resistance 3", "Cryo Resistance 4", "Cryo Resistance 5"
        },
        ["Progressive Electrical Resistance"] = new[]
        {
            "Electrical Resistance 1", "Electrical Resistance 2", "Electrical Resistance 3",
            "Electrical Resistance 4", "Electrical Resistance 5"
        },
        ["Progressive Cutter Heat"] = new[]
        {
            "Cutter Heat Capacity 1", "Cutter Heat Capacity 2", "Cutter Heat Capacity 3"
        },
        ["Progressive Cutter Cooldown"] = new[]
        {
            "Cutter Cooldown 1", "Cutter Cooldown 2", "Cutter Cooldown 3"
        },
        ["Progressive Cutter"] = new[] // legacy
        {
            "Cutter Heat Capacity 1", "Cutter Cooldown 1", "Cutter Heat Capacity 2"
        },
        ["Progressive Stinger Range"] = new[]
        {
            "Stinger Range 1", "Stinger Range 2", "Stinger Range 3", "Stinger Range 4", "Stinger Range 5"
        },
        ["Progressive Splitsaw Range"] = new[]
        {
            "Splitsaw Range 1", "Splitsaw Range 2", "Splitsaw Range 3", "Splitsaw Range 4", "Splitsaw Range 5"
        },
        ["Progressive Grapple Range"] = new[]
        {
            "Grapple Range 1", "Grapple Range 2", "Grapple Range 3", "Grapple Range 4", "Grapple Range 5"
        },
        ["Progressive Charged Push Force"] = new[]
        {
            "Charged Push Force 1", "Charged Push Force 2", "Charged Push Force 3"
        },
        ["Progressive Demo Charges"] = new[]
        {
            "Demo Charges Capacity 1", "Demo Charges Capacity 2", "Demo Charges Capacity 3",
            "Demo Charges Capacity 4", "Demo Charges Capacity 5"
        },
        ["Progressive Demo Disarming"] = new[] { "Demo Disarming 1", "Demo Disarming 2", "Demo Disarming 3" },
        ["Progressive Demo Self Cleanup"] = new[]
        {
            "Demo Self Cleanup 1", "Demo Self Cleanup 2", "Demo Self Cleanup 3"
        },
        ["Progressive O2 Capacity"] = new[]
        {
            "O2 Capacity 1", "O2 Capacity 2", "O2 Capacity 3", "O2 Capacity 4", "O2 Capacity 5"
        },
        ["Progressive O2 Recharge"] = new[] { "O2 Recharge 1", "O2 Recharge 2", "O2 Recharge 3" },
        ["Progressive Thruster Top Speed"] = new[]
        {
            "Thruster Top Speed 1", "Thruster Top Speed 2", "Thruster Top Speed 3"
        },
        ["Progressive Thruster Braking"] = new[]
        {
            "Thruster Braking 1", "Thruster Braking 2", "Thruster Braking 3"
        },
        ["Progressive Thruster Fuel"] = new[]
        {
            "Thruster Fuel Capacity 1", "Thruster Fuel Capacity 2", "Thruster Fuel Capacity 3"
        },
        ["Progressive Audio Resynth"] = new[] { "Audio Resynth 1", "Audio Resynth 2", "Audio Resynth 3" },
        ["Progressive Thruster Durability"] = new[]
        {
            "Thruster Durability 1", "Thruster Durability 2", "Thruster Durability 3",
            "Thruster Durability 4", "Thruster Durability 5"
        },
        ["Progressive Cutter Durability"] = new[]
        {
            "Cutter Durability 1", "Cutter Durability 2", "Cutter Durability 3",
            "Cutter Durability 4", "Cutter Durability 5"
        },
        ["Progressive Grapple Durability"] = new[]
        {
            "Grapple Durability 1", "Grapple Durability 2", "Grapple Durability 3",
            "Grapple Durability 4", "Grapple Durability 5"
        },
        ["Progressive Scanner Durability"] = new[]
        {
            "Scanner Durability 1", "Scanner Durability 2", "Scanner Durability 3",
            "Scanner Durability 4", "Scanner Durability 5"
        },
        ["Progressive Suit Durability"] = new[]
        {
            "Suit Durability 1", "Suit Durability 2", "Suit Durability 3"
        },
        ["Progressive Demo Durability"] = new[]
        {
            "Demo Durability 1", "Demo Durability 2", "Demo Durability 3",
            "Demo Durability 4", "Demo Durability 5"
        },
    };

    /// <summary>Hab shop location IDs are BaseId+200 … BaseId+349 (equipment.py / HabEquipmentCatalog).</summary>
    public static bool IsHabShopLocationId(long locationId)
    {
        var offset = locationId - ArchipelagoClient.BaseId;
        return offset is >= 200 and <= 349;
    }
}
