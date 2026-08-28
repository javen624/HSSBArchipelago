-- Visibility + small helpers for access/visibility $rules.

--- Return true if location id is in the active pool for current seed options.
function loc_visible(loc_id)
    local id = tonumber(loc_id)
    if not id then
        return true
    end
    local m = LOCATION_VISIBILITY[id]
    if not m then
        return true
    end

    local density = 1
    if Tracker:ProviderCountForCode("setting_density_sparse") > 0 then
        density = 0
    elseif Tracker:ProviderCountForCode("setting_density_full") > 0 then
        density = 2
    end

    local hab_shop = Tracker:ProviderCountForCode("setting_hab_shop") > 0

    local in_density = false
    if density == 0 then
        in_density = m.sparse
    elseif density == 1 then
        in_density = m.standard
    else
        in_density = m.full
    end

    if not in_density then
        return false
    end
    if m.shop and not hab_shop then
        return false
    end
    return true
end

function has_tether()
    return Tracker:ProviderCountForCode("tether_module") > 0
        or Tracker:ProviderCountForCode("progressive_tether_amount") > 0
        or Tracker:ProviderCountForCode("progressive_tether_lifetime") > 0
        or Tracker:ProviderCountForCode("progressive_tethers") > 0
end

function has_demo()
    return Tracker:ProviderCountForCode("demo_charge_license") > 0
        or Tracker:ProviderCountForCode("progressive_demo_charges") > 0
end

function has_atlas()
    return Tracker:ProviderCountForCode("progressive_certification_rank:1") > 0
        or Tracker:ProviderCountForCode("unlock_atlas") > 0
        or Tracker:ProviderCountForCode("progressive_ship_unlock:1") > 0
end

function has_javelin()
    return Tracker:ProviderCountForCode("progressive_certification_rank:1") > 0
        or Tracker:ProviderCountForCode("unlock_javelin") > 0
        or Tracker:ProviderCountForCode("progressive_ship_unlock:2") > 0
end

function has_gecko()
    return Tracker:ProviderCountForCode("progressive_certification_rank:2") > 0
        or Tracker:ProviderCountForCode("unlock_gecko") > 0
        or Tracker:ProviderCountForCode("progressive_ship_unlock:3") > 0
end
