-- Archipelago autotracking for Hardspace Shipbreaker (v0.2)

AUTOTRACKER_ENABLE_DEBUG_LOGGING_AP = false

CUR_INDEX = -1
SLOT_DATA = nil

local function debug_log(msg)
    if AUTOTRACKER_ENABLE_DEBUG_LOGGING_AP then
        print(msg)
    end
end

local function set_progressive_stage(code, stage_idx)
    local obj = Tracker:FindObjectForCode(code)
    if not obj then
        return
    end
    obj.CurrentStage = stage_idx
end

local function mark_location_cleared(location_id)
    local v = LOCATION_MAPPING[location_id]
    if not v then
        return
    end
    local code = v[1]
    if not code then
        return
    end
    local obj = Tracker:FindObjectForCode(code)
    if not obj then
        debug_log(string.format("mark_location_cleared: missing %s", code))
        return
    end
    if code:sub(1, 1) == "@" then
        -- Idempotent: safe if LocationHandler also fires for the same id.
        obj.AvailableChestCount = 0
    else
        obj.Active = true
    end
end

local function apply_checked_locations()
    local checked = Archipelago.CheckedLocations
    if not checked then
        return
    end
    for _, location_id in ipairs(checked) do
        mark_location_cleared(location_id)
    end
end

local function apply_slot_data(slot_data)
    if not slot_data then
        return
    end

    local density = tonumber(slot_data["location_density"]) or 1
    if density < 0 then
        density = 0
    elseif density > 2 then
        density = 2
    end
    set_progressive_stage("setting_density", density)

    local hab = slot_data["hab_shop_sanity"]
    local hab_obj = Tracker:FindObjectForCode("setting_hab_shop")
    if hab_obj then
        if type(hab) == "boolean" then
            hab_obj.Active = hab
        else
            hab_obj.Active = tonumber(hab) ~= 0
        end
    end

    local death = slot_data["death_link"]
    local death_obj = Tracker:FindObjectForCode("setting_death_link")
    if death_obj then
        if type(death) == "boolean" then
            death_obj.Active = death
        else
            death_obj.Active = tonumber(death) ~= 0
        end
    end

    local goal = tonumber(slot_data["goal"]) or 0
    if goal < 0 then
        goal = 0
    elseif goal > 2 then
        goal = 2
    end
    set_progressive_stage("setting_goal", goal)
end

local function clear_locations()
    for _, v in pairs(LOCATION_MAPPING) do
        local code = v[1]
        if code then
            local obj = Tracker:FindObjectForCode(code)
            if obj then
                if code:sub(1, 1) == "@" then
                    obj.AvailableChestCount = obj.ChestCount
                else
                    obj.Active = false
                end
            end
        end
    end
end

local function clear_items()
    for _, v in pairs(ITEM_MAPPING) do
        local code = v[1] and v[1][1]
        local typ = v[2]
        if code and typ then
            local obj = Tracker:FindObjectForCode(code)
            if obj then
                if typ == "toggle" then
                    obj.Active = false
                elseif typ == "progressive" then
                    obj.Active = false
                    obj.CurrentStage = 0
                elseif typ == "consumable" then
                    obj.AcquiredCount = 0
                end
            end
        end
    end
end

function onClear(slot_data)
    debug_log("onClear")
    SLOT_DATA = slot_data
    CUR_INDEX = -1
    clear_locations()
    clear_items()

    set_progressive_stage("setting_density", 1)
    local hab_obj = Tracker:FindObjectForCode("setting_hab_shop")
    if hab_obj then
        hab_obj.Active = true
    end
    local death_obj = Tracker:FindObjectForCode("setting_death_link")
    if death_obj then
        death_obj.Active = false
    end
    set_progressive_stage("setting_goal", 0)

    apply_slot_data(slot_data)

    local mackerel = Tracker:FindObjectForCode("unlock_mackerel")
    if mackerel then
        mackerel.Active = true
    end

    -- Mid-seed / reconnect: mark already-checked locations immediately.
    apply_checked_locations()
end

function onItem(index, item_id, item_name, player_number)
    if index <= CUR_INDEX then
        return
    end
    CUR_INDEX = index
    local v = ITEM_MAPPING[item_id]
    if not v then
        debug_log(string.format("onItem: unknown id %s (%s)", tostring(item_id), tostring(item_name)))
        return
    end
    local code = v[1] and v[1][1]
    local typ = v[2]
    if not code then
        return
    end
    local obj = Tracker:FindObjectForCode(code)
    if not obj then
        debug_log(string.format("onItem: missing object for %s", code))
        return
    end
    if typ == "toggle" then
        obj.Active = true
    elseif typ == "progressive" then
        if obj.Active then
            obj.CurrentStage = obj.CurrentStage + 1
        else
            obj.Active = true
        end
    elseif typ == "consumable" then
        obj.AcquiredCount = obj.AcquiredCount + (obj.Increment or 1)
    end
end

function onLocation(location_id, location_name)
    mark_location_cleared(location_id)
end

Archipelago:AddClearHandler("hs clear", onClear)
Archipelago:AddItemHandler("hs item", onItem)
Archipelago:AddLocationHandler("hs location", onLocation)
