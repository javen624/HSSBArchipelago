-- Hardspace Shipbreaker PopTracker — entry point

Tracker:AddItems("items/items.json")
Tracker:AddMaps("maps/maps.json")
Tracker:AddLocations("locations/hab.json")
Tracker:AddLocations("locations/bay.json")
Tracker:AddLocations("locations/mackerel.json")
Tracker:AddLocations("locations/atlas.json")
Tracker:AddLocations("locations/javelin.json")
Tracker:AddLocations("locations/gecko.json")
Tracker:AddLocations("locations/reactor.json")
Tracker:AddLayouts("layouts/items.json")
Tracker:AddLayouts("layouts/tracker.json")

ScriptHost:LoadScript("scripts/generated/item_mapping.lua")
ScriptHost:LoadScript("scripts/generated/location_mapping.lua")
ScriptHost:LoadScript("scripts/generated/location_visibility.lua")
ScriptHost:LoadScript("scripts/logic.lua")

if PopVersion and PopVersion >= "0.18.0" then
    ScriptHost:LoadScript("scripts/autotracking.lua")
end
