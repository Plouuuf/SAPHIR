-- =====================================================================================
-- SAPHIR GÉOOPS — positions simulées des agents de police en service, envoyées au serveur SAPHIR.
-- POST {Config.SaphirUrl}/api/integrations/geoops/positions, en-tête X-Saphir-Key.
-- Corps : liste de { discordId, serverId, nom, x, y, z, cap, vitesse, vehicule }.
-- Les positions sont celles du jeu : SAPHIR les présente comme « Simulation FiveM », jamais comme un GPS réel.
-- Prérequis : OneSync (les coordonnées des joueurs sont lues côté serveur).
-- =====================================================================================

local ESX, QBCore = nil, nil

local function journal(niveau, message)
    if niveau == 'erreur' or Config.Journal == 'debug' then
        print(('[saphir-geoops] %s'):format(message))
    end
end

CreateThread(function()
    if Config.Framework == 'esx' then
        local ok, objet = pcall(function() return exports['es_extended']:getSharedObject() end)
        if ok then ESX = objet else journal('erreur', 'ESX introuvable : vérifie Config.Framework.') end
    elseif Config.Framework == 'qbcore' then
        local ok, objet = pcall(function() return exports['qb-core']:GetCoreObject() end)
        if ok then QBCore = objet else journal('erreur', 'QBCore introuvable : vérifie Config.Framework.') end
    end
end)

local jobsPolice = {}
for _, job in ipairs(Config.Jobs) do jobsPolice[job] = true end

--- Identifiant Discord du joueur (chiffres seuls) ou nil.
local function discordId(source)
    for _, identifiant in ipairs(GetPlayerIdentifiers(source)) do
        if identifiant:sub(1, 8) == 'discord:' then
            return identifiant:sub(9)
        end
    end
    return nil
end

--- Le joueur est-il un agent de police en service ? Retourne (vrai/faux, nom RP éventuel).
local function agentEnService(source)
    if Config.Framework == 'esx' and ESX then
        local joueur = ESX.GetPlayerFromId(source)
        if not joueur then return false end
        local job = joueur.getJob and joueur.getJob() or joueur.job
        if not job or not jobsPolice[job.name] then return false end
        if Config.ServiceSeulement and job.onDuty == false then return false end
        local nom = joueur.getName and joueur.getName() or nil
        return true, nom
    elseif Config.Framework == 'qbcore' and QBCore then
        local joueur = QBCore.Functions.GetPlayer(source)
        if not joueur then return false end
        local job = joueur.PlayerData.job
        if not job or not (jobsPolice[job.name] or (Config.TypeJobQb and job.type == Config.TypeJobQb)) then return false end
        if Config.ServiceSeulement and not job.onduty then return false end
        local info = joueur.PlayerData.charinfo
        local nom = info and ((info.lastname or ''):upper() .. ' ' .. (info.firstname or '')) or nil
        return true, nom
    elseif Config.Framework == 'aucun' then
        return IsPlayerAceAllowed(source, Config.Ace), nil
    end
    return false
end

--- Vitesse (m/s) à partir du vecteur vitesse de l'entité.
local function vitesse(entite)
    local v = GetEntityVelocity(entite)
    return math.sqrt(v.x * v.x + v.y * v.y + v.z * v.z)
end

local function arrondir(valeur, decimales)
    local facteur = 10 ^ (decimales or 1)
    return math.floor(valeur * facteur + 0.5) / facteur
end

local function collecterPositions()
    local positions = {}
    for _, id in ipairs(GetPlayers()) do
        local source = tonumber(id)
        local enService, nomRp = agentEnService(source)
        if enService then
            local ped = GetPlayerPed(source)
            if ped and ped ~= 0 and DoesEntityExist(ped) then
                local coords = GetEntityCoords(ped)
                local vehicule = GetVehiclePedIsIn(ped, false)
                local entite = (vehicule and vehicule ~= 0) and vehicule or ped
                local plaque = nil
                if vehicule and vehicule ~= 0 then
                    plaque = GetVehicleNumberPlateText(vehicule)
                    if plaque then plaque = plaque:gsub('^%s+', ''):gsub('%s+$', '') end
                end
                positions[#positions + 1] = {
                    discordId = discordId(source),
                    serverId = source,
                    nom = (Config.Nom == 'rp' and nomRp) or GetPlayerName(source),
                    x = arrondir(coords.x, 1),
                    y = arrondir(coords.y, 1),
                    z = arrondir(coords.z, 1),
                    cap = arrondir(GetEntityHeading(entite), 0),
                    vitesse = arrondir(vitesse(entite), 1),
                    vehicule = plaque
                }
            end
        end
    end
    return positions
end

local envoiEnCours = false

local function envoyer(positions)
    if #positions == 0 or envoiEnCours then return end
    envoiEnCours = true
    PerformHttpRequest(Config.SaphirUrl .. '/api/integrations/geoops/positions', function(code, corps)
        envoiEnCours = false
        if code == 200 then
            journal('debug', ('%d position(s) envoyée(s).'):format(#positions))
        elseif code == 401 then
            journal('erreur', 'Clé refusée par SAPHIR : vérifie Config.SaphirKey (SAPHIR:AccessKey).')
        else
            journal('erreur', ('Envoi impossible (code %s) : %s'):format(tostring(code), tostring(corps)))
        end
    end, 'POST', json.encode(positions), {
        ['Content-Type'] = 'application/json',
        ['X-Saphir-Key'] = Config.SaphirKey
    })
end

CreateThread(function()
    if Config.SaphirKey == nil or Config.SaphirKey == '' or Config.SaphirKey == 'CHANGE_ME' then
        journal('erreur', 'Config.SaphirKey non renseignée : aucune position ne sera envoyée.')
        return
    end
    while true do
        Wait(Config.Intervalle)
        local ok, resultat = pcall(collecterPositions)
        if ok then
            envoyer(resultat)
        else
            journal('erreur', 'Lecture des positions impossible : ' .. tostring(resultat))
        end
    end
end)
