-- =====================================================================================
-- SAPHIR — déclaration en jeu des personnages, véhicules et permis auprès de la police (Val-de-Marne Simulation).
-- Routes SAPHIR : {Config.SaphirUrl}/api/integrations/identite/… (en-tête X-Saphir-Key), voir docs/API-CITOYEN.md.
-- Toutes les données du personnage (identité, véhicules possédés, licences) sont lues ICI, côté serveur ;
-- le client ne renvoie que les champs libres de la boîte de dialogue, contrôlés ci-dessous.
-- Données de jeu de rôle : fictives.
-- =====================================================================================

local ESX, QBCore = nil, nil
local RESSOURCE = GetCurrentResourceName()

local function journal(niveau, message)
    if niveau == 'erreur' or Config.Journal == 'debug' then
        print(('[saphir-identite] %s'):format(message))
    end
end

CreateThread(function()
    if Config.Framework == 'esx' then
        local ok, objet = pcall(function() return exports['es_extended']:getSharedObject() end)
        if ok and objet then ESX = objet else journal('erreur', 'ESX introuvable : vérifie Config.Framework.') end
    elseif Config.Framework == 'qbcore' then
        local ok, objet = pcall(function() return exports['qb-core']:GetCoreObject() end)
        if ok and objet then QBCore = objet else journal('erreur', 'QBCore introuvable : vérifie Config.Framework.') end
    end
    if Config.SaphirKey == nil or Config.SaphirKey == '' or Config.SaphirKey == 'CHANGE_ME' then
        journal('erreur', 'Config.SaphirKey non renseignée : les déclarations seront refusées par SAPHIR.')
    end
end)

-- =====================================================================================
-- Outils
-- =====================================================================================

local function options()
    return { notification = Config.Notification, dialogue = Config.Dialogue, commande = Config.Commande, framework = Config.Framework }
end

--- Message au joueur (type : 'info', 'succes', 'erreur'). Les sauts de ligne séparent les lignes du chat.
local function dire(source, texte, genre)
    if type(texte) ~= 'string' or texte == '' then texte = 'Demande traitée.' end
    TriggerClientEvent('saphir-identite:message', source, texte, genre or 'info', options())
end

local function trim(texte)
    if type(texte) ~= 'string' then return nil end
    texte = texte:gsub('^%s+', ''):gsub('%s+$', '')
    if texte == '' then return nil end
    return texte
end

--- Champ libre reçu du client : chaîne, sans caractère de contrôle, tronquée.
local function champ(valeur, max)
    if type(valeur) == 'number' then valeur = tostring(valeur) end
    if type(valeur) ~= 'string' then return nil end
    valeur = valeur:gsub('[%c]', ' ')
    valeur = trim(valeur)
    if not valeur then return nil end
    if #valeur > max then valeur = valeur:sub(1, max) end
    return valeur
end

local function nombre(valeur, min, max)
    local n = tonumber(valeur)
    if not n then return nil end
    n = math.floor(n)
    if n < min or n > max then return nil end
    return n
end

local function urlencode(texte)
    return (tostring(texte):gsub('[^%w%-_%.~]', function(c) return ('%%%02X'):format(string.byte(c)) end))
end

--- Découpe « a | b | c » en champs (vides conservés).
local function decouper(texte)
    local champs = {}
    if not texte then return champs end
    for morceau in (texte .. '|'):gmatch('([^|]*)|') do
        champs[#champs + 1] = trim(morceau)
    end
    return champs
end

local function discordId(source)
    for _, identifiant in ipairs(GetPlayerIdentifiers(source)) do
        if identifiant:sub(1, 8) == 'discord:' then
            return identifiant:sub(9)
        end
    end
    return nil
end

local function licence(source)
    for _, identifiant in ipairs(GetPlayerIdentifiers(source)) do
        if identifiant:sub(1, 8) == 'license:' then
            return identifiant
        end
    end
    return nil
end

--- Requête oxmysql synchrone (dans un thread) ; nil en cas d'erreur.
local function requete(sql, parametres)
    if GetResourceState('oxmysql') ~= 'started' then
        journal('erreur', 'oxmysql n\'est pas démarré : lecture de la base FiveM impossible.')
        return nil
    end
    local p = promise.new()
    local ok, erreur = pcall(function()
        exports.oxmysql:query(sql, parametres or {}, function(resultat) p:resolve(resultat or {}) end)
    end)
    if not ok then
        journal('erreur', 'Requête impossible : ' .. tostring(erreur))
        return nil
    end
    return Citizen.Await(p)
end

-- =====================================================================================
-- Appels SAPHIR
-- =====================================================================================

--- Appel HTTP vers SAPHIR. cb(ok, donnees_ou_message).
local function saphir(methode, chemin, corps, cb)
    PerformHttpRequest(Config.SaphirUrl .. '/api/integrations/identite' .. chemin, function(code, reponse)
        local donnees = nil
        if reponse and reponse ~= '' then
            local ok, decode = pcall(json.decode, reponse)
            if ok then donnees = decode end
        end
        if code == 200 then
            cb(true, donnees or {})
        elseif code == 401 then
            journal('erreur', 'Clé refusée par SAPHIR : vérifie Config.SaphirKey (SAPHIR:AccessKey).')
            cb(false, 'Service de déclaration indisponible (configuration du serveur). Préviens l\'équipe du serveur.')
        elseif type(donnees) == 'table' and type(donnees.error) == 'string' then
            cb(false, donnees.error)
        else
            journal('erreur', ('SAPHIR a répondu %s : %s'):format(tostring(code), tostring(reponse)))
            cb(false, 'SAPHIR est injoignable pour le moment. Réessaie plus tard.')
        end
    end, methode, corps and json.encode(corps) or '', {
        ['Content-Type'] = 'application/json',
        ['X-Saphir-Key'] = Config.SaphirKey
    })
end

-- =====================================================================================
-- Lecture du personnage (framework)
-- =====================================================================================

local function dateEsx(texte)
    texte = trim(texte)
    if not texte then return nil end
    if Config.FormatDateEsx == 'mm/jj/aaaa' then
        local m, j, a = texte:match('^(%d%d?)/(%d%d?)/(%d%d%d%d)$')
        if m then return ('%02d/%02d/%s'):format(tonumber(j), tonumber(m), a) end
    end
    return texte
end

--- Personnage actuellement joué : { id, nom, prenom, naissance, sexe, nationalite, telephone, taille } ou nil, message.
local function personnage(source)
    if Config.Framework == 'esx' then
        if not ESX then return nil, 'ESX indisponible.' end
        local joueur = ESX.GetPlayerFromId(source)
        if not joueur or not joueur.identifier then return nil, 'Personnage non chargé : réessaie dans un instant.' end
        local p = { id = joueur.identifier }
        local lignes = requete('SELECT * FROM users WHERE identifier = ? LIMIT 1', { joueur.identifier })
        local ligne = lignes and lignes[1]
        if ligne then
            p.prenom, p.nom = ligne.firstname, ligne.lastname
            p.naissance = dateEsx(ligne.dateofbirth)
            p.sexe = ligne.sex
            p.taille = tonumber(ligne.height)
            p.telephone = ligne.phone_number
        else
            local function lire(cle) local ok, v = pcall(function() return joueur.get(cle) end) return ok and v or nil end
            p.prenom, p.nom = lire('firstName'), lire('lastName')
            p.naissance, p.sexe, p.taille = dateEsx(lire('dateofbirth')), lire('sex'), tonumber(lire('height'))
        end
        p.nationalite = Config.NationaliteParDefaut
        if not trim(p.nom) or not trim(p.prenom) then return nil, 'Identité du personnage introuvable (esx_identity).' end
        return p
    elseif Config.Framework == 'qbcore' then
        if not QBCore then return nil, 'QBCore indisponible.' end
        local joueur = QBCore.Functions.GetPlayer(source)
        if not joueur or not joueur.PlayerData or not joueur.PlayerData.citizenid then
            return nil, 'Personnage non chargé : réessaie dans un instant.'
        end
        local info = joueur.PlayerData.charinfo or {}
        return {
            id = joueur.PlayerData.citizenid,
            nom = info.lastname, prenom = info.firstname, naissance = info.birthdate,
            sexe = info.gender ~= nil and tostring(info.gender) or nil,
            nationalite = info.nationality, telephone = info.phone and tostring(info.phone) or nil
        }
    elseif Config.Framework == 'aucun' then
        local id = licence(source)
        if not id then return nil, 'Licence Rockstar introuvable.' end
        return { id = id, libre = true }
    end
    return nil, 'Config.Framework invalide.'
end

--- Joueur (Discord + personnage) pour SAPHIR, ou nil après avoir prévenu le joueur.
local function joueurSaphir(source)
    local discord = discordId(source)
    if not discord then
        dire(source, 'Ton compte Discord n\'est pas relié à FiveM : ouvre Discord, puis reconnecte-toi au serveur.', 'erreur')
        return nil
    end
    local p, erreur = personnage(source)
    if not p then
        dire(source, erreur, 'erreur')
        return nil
    end
    return { discordId = discord, personnageId = p.id, nomJoueur = GetPlayerName(source) }, p
end

-- =====================================================================================
-- Délai et dialogues en attente
-- =====================================================================================

local derniere = {}
local attentes = {}

local function tropTot(source)
    local maintenant = GetGameTimer()
    if derniere[source] and maintenant - derniere[source] < Config.Delai then
        dire(source, 'Patiente quelques secondes avant une nouvelle demande.', 'erreur')
        return true
    end
    derniere[source] = maintenant
    return false
end

local function demanderDialogue(source, genre, valeurs)
    attentes[source] = { genre = genre, expire = GetGameTimer() + 180000 }
    TriggerClientEvent('saphir-identite:dialogue', source, genre, valeurs or {}, options())
end

AddEventHandler('playerDropped', function()
    derniere[source] = nil
    attentes[source] = nil
end)

-- =====================================================================================
-- Actions
-- =====================================================================================

local function texteDate(iso)
    if type(iso) ~= 'string' then return '—' end
    local a, m, j = iso:match('^(%d%d%d%d)-(%d%d)-(%d%d)')
    return a and ('%s/%s/%s'):format(j, m, a) or iso
end

local function resumePersonnage(p)
    local lignes = { ('%s %s, né(e) le %s — %s'):format(p.nom, p.prenom, texteDate(p.dateNaissance), p.reference) }
    if p.identiteFigee then lignes[#lignes + 1] = 'Identité figée : la personne figure dans un fichier de police.' end
    if p.permis then
        lignes[#lignes + 1] = ('Permis %s — %d point(s) — %s'):format(p.permis.categories, p.permis.points, p.permis.situation)
    end
    for _, v in ipairs(p.vehicules or {}) do
        local assurance = v.assurance and (v.assurance.couvert and ('assuré ' .. v.assurance.assureur) or 'assurance expirée') or 'sans assurance déclarée'
        lignes[#lignes + 1] = ('Véhicule %s %s — %s'):format(v.immatriculation, trim((v.marque or '') .. ' ' .. (v.modele or '')) or '', assurance)
    end
    return table.concat(lignes, '\n')
end

local function etat(source, ouvrirDialogue)
    local joueur = joueurSaphir(source)
    if not joueur then return end
    saphir('GET', ('/etat?discordId=%s&personnageId=%s'):format(urlencode(joueur.discordId), urlencode(joueur.personnageId)), nil, function(ok, donnees)
        if not ok then return dire(source, donnees, 'erreur') end
        if ouvrirDialogue then
            local p = donnees.personnage or {}
            return demanderDialogue(source, 'identite', {
                lieuNaissance = p.lieuNaissance, adresse = p.adresse, profession = p.profession, tailleCm = p.tailleCm,
                corpulence = p.corpulence, signesParticuliers = p.signesParticuliers, identiteFigee = p.identiteFigee,
                declare = donnees.personnage ~= nil, libre = Config.Framework == 'aucun',
                nom = p.nom, prenom = p.prenom, dateNaissance = p.dateNaissance and texteDate(p.dateNaissance) or nil, sexe = p.sexe
            })
        end
        local lignes = { ('Personnages déclarés : %d / %d'):format(#(donnees.personnages or {}), donnees.maximum or 0) }
        for _, r in ipairs(donnees.personnages or {}) do
            lignes[#lignes + 1] = ('• %s %s (%s)%s'):format(r.nom, r.prenom, r.reference, r.personnageId == joueur.personnageId and ' — joué' or '')
        end
        if donnees.personnage then
            lignes[#lignes + 1] = resumePersonnage(donnees.personnage)
        else
            lignes[#lignes + 1] = ('Ce personnage n\'est pas déclaré : /%s pour le déclarer.'):format(Config.Commande)
        end
        dire(source, table.concat(lignes, '\n'), 'info')
    end)
end

--- Envoi de la déclaration : identité du framework + champs libres (déjà contrôlés).
local function declarer(source, libres)
    local joueur, p = joueurSaphir(source)
    if not joueur then return end
    local corps = {
        discordId = joueur.discordId, personnageId = joueur.personnageId, nomJoueur = joueur.nomJoueur,
        nom = p.nom, prenom = p.prenom, dateNaissance = p.naissance, sexe = p.sexe, nationalite = p.nationalite,
        telephone = p.telephone, tailleCm = p.taille or libres.tailleCm,
        lieuNaissance = libres.lieuNaissance, adresse = libres.adresse, profession = libres.profession,
        corpulence = libres.corpulence, signesParticuliers = libres.signesParticuliers
    }
    if p.libre then
        -- Sans framework : identité saisie par le joueur (contrôlée par SAPHIR comme le reste).
        corps.nom, corps.prenom, corps.dateNaissance, corps.sexe = libres.nom, libres.prenom, libres.dateNaissance, libres.sexe
        corps.nationalite = Config.NationaliteParDefaut
    end
    saphir('POST', '/declarer', corps, function(ok, donnees)
        if not ok then return dire(source, donnees, 'erreur') end
        local lignes = { donnees.message }
        for _, avertissement in ipairs(donnees.avertissements or {}) do lignes[#lignes + 1] = avertissement end
        dire(source, table.concat(lignes, '\n'), 'succes')
    end)
end

--- Champs libres de l'identité : table reçue du client (dialogue) ou liste d'arguments « a | b | c ».
local function lireLibres(valeurs)
    if type(valeurs) ~= 'table' then return nil end
    return {
        nom = champ(valeurs.nom, 60), prenom = champ(valeurs.prenom, 60), dateNaissance = champ(valeurs.dateNaissance, 10),
        sexe = champ(valeurs.sexe, 1),
        lieuNaissance = champ(valeurs.lieuNaissance, 120), adresse = champ(valeurs.adresse, 255),
        profession = champ(valeurs.profession, 120), tailleCm = nombre(valeurs.tailleCm, 100, 250),
        corpulence = champ(valeurs.corpulence, 40), signesParticuliers = champ(valeurs.signesParticuliers, 500)
    }
end

local function libresDepuisArguments(texte)
    local c = decouper(texte)
    if Config.Framework == 'aucun' then
        return lireLibres({ nom = c[1], prenom = c[2], dateNaissance = c[3], sexe = c[4], lieuNaissance = c[5], adresse = c[6],
            profession = c[7], tailleCm = c[8], corpulence = c[9], signesParticuliers = c[10] })
    end
    return lireLibres({ lieuNaissance = c[1], adresse = c[2], profession = c[3], tailleCm = c[4], corpulence = c[5], signesParticuliers = c[6] })
end

local function retirer(source)
    local joueur = joueurSaphir(source)
    if not joueur then return end
    saphir('POST', '/retirer', joueur, function(ok, donnees)
        if not ok then return dire(source, donnees, 'erreur') end
        dire(source, donnees.message, 'succes')
    end)
end

-- ----- Véhicules -----

local nomsEsx = nil

local function nonSigne(h)
    h = tonumber(h)
    if not h then return nil end
    if h < 0 then h = h + 4294967296 end
    return h
end

--- ESX : modèle (hash) → nom, d'après la table « vehicles » d'esx_vehicleshop si elle existe.
local function nomModeleEsx(hash)
    if nomsEsx == nil then
        nomsEsx = {}
        local lignes = requete('SELECT model, name FROM vehicles', {})
        for _, l in ipairs(lignes or {}) do
            if l.model then nomsEsx[nonSigne(GetHashKey(l.model))] = l.name or l.model end
        end
    end
    return nomsEsx[nonSigne(hash)]
end

--- Véhicules possédés par le personnage (base FiveM). plaque facultative : un seul véhicule.
local function vehiculesPossedes(source, p, plaque)
    local liste = {}
    local function normaliser(t) return (t or ''):upper():gsub('[^%w]', '') end
    local cible = plaque and normaliser(plaque) or nil
    if Config.Framework == 'esx' then
        for _, l in ipairs(requete('SELECT plate, vehicle FROM owned_vehicles WHERE owner = ?', { p.id }) or {}) do
            local modele = nil
            local ok, props = pcall(json.decode, l.vehicle or '')
            if ok and type(props) == 'table' and props.model then modele = nomModeleEsx(props.model) end
            liste[#liste + 1] = { plaque = trim(l.plate), modele = modele }
        end
    elseif Config.Framework == 'qbcore' then
        for _, l in ipairs(requete('SELECT plate, vehicle FROM player_vehicles WHERE citizenid = ?', { p.id }) or {}) do
            local info = QBCore and QBCore.Shared and QBCore.Shared.Vehicles and QBCore.Shared.Vehicles[l.vehicle] or nil
            liste[#liste + 1] = {
                plaque = trim(l.plate),
                marque = info and info.brand or nil,
                modele = info and info.name or l.vehicle
            }
        end
    end
    if cible then
        local filtre = {}
        for _, v in ipairs(liste) do
            if normaliser(v.plaque) == cible then filtre[#filtre + 1] = v end
        end
        return filtre
    end
    return liste
end

local function envoyerVehicules(source, joueur, vehicules)
    local corps = { discordId = joueur.discordId, personnageId = joueur.personnageId, nomJoueur = joueur.nomJoueur, vehicules = vehicules }
    saphir('POST', '/vehicules', corps, function(ok, donnees)
        if not ok then return dire(source, donnees, 'erreur') end
        local lignes = {}
        for _, r in ipairs(donnees.resultats or {}) do
            lignes[#lignes + 1] = ('%s : %s — %s'):format(r.plaque, r.statut, r.message)
        end
        dire(source, table.concat(lignes, '\n'), 'succes')
    end)
end

local function declarerVehicules(source, plaque)
    if Config.Framework == 'aucun' then
        return dire(source, 'Déclaration des véhicules indisponible sans framework (ESX ou QBCore).', 'erreur')
    end
    local joueur, p = joueurSaphir(source)
    if not joueur then return end
    local possedes = vehiculesPossedes(source, p, plaque)
    if #possedes == 0 then
        return dire(source, plaque and 'Tu ne possèdes aucun véhicule avec cette plaque.' or 'Aucun véhicule possédé par ce personnage.', 'erreur')
    end
    local vehicules = {}
    for i, v in ipairs(possedes) do
        if i > 30 then break end
        vehicules[#vehicules + 1] = { plaque = v.plaque, marque = v.marque, modele = v.modele }
    end
    envoyerVehicules(source, joueur, vehicules)
end

--- Assurance d'un véhicule possédé : { plaque, assureur, contrat, fin }.
local function declarerAssurance(source, valeurs)
    if type(valeurs) ~= 'table' then return end
    local plaque = champ(valeurs.plaque, 20)
    local assureur, contrat, fin = champ(valeurs.assureur, 100), champ(valeurs.contrat, 60), champ(valeurs.fin, 10)
    if not plaque or not assureur or not contrat or not fin then
        return dire(source, ('Usage : /%s assurance PLAQUE | ASSUREUR | N° DE CONTRAT | JJ/MM/AAAA (fin)'):format(Config.Commande), 'erreur')
    end
    local joueur, p = joueurSaphir(source)
    if not joueur then return end
    local possedes = Config.Framework == 'aucun' and {} or vehiculesPossedes(source, p, plaque)
    if #possedes == 0 then
        return dire(source, 'Tu ne possèdes aucun véhicule avec cette plaque.', 'erreur')
    end
    local v = possedes[1]
    envoyerVehicules(source, joueur, { { plaque = v.plaque, marque = v.marque, modele = v.modele, assureur = assureur, contrat = contrat, finAssurance = fin } })
end

local function retirerVehicule(source, plaque)
    plaque = champ(plaque, 20)
    if not plaque then
        return dire(source, ('Usage : /%s vehicule-retirer PLAQUE'):format(Config.Commande), 'erreur')
    end
    local joueur = joueurSaphir(source)
    if not joueur then return end
    saphir('POST', '/vehicules/retirer', { discordId = joueur.discordId, personnageId = joueur.personnageId, nomJoueur = joueur.nomJoueur, plaque = plaque },
        function(ok, donnees)
            if not ok then return dire(source, donnees, 'erreur') end
            dire(source, donnees.message, 'succes')
        end)
end

-- ----- Permis -----

local function categoriesPermis(source, p)
    local categories, vues = {}, {}
    local function ajouter(c) if c and not vues[c] then vues[c] = true categories[#categories + 1] = c end end
    if Config.Framework == 'esx' then
        for _, l in ipairs(requete('SELECT type FROM user_licenses WHERE owner = ?', { p.id }) or {}) do
            ajouter(Config.Licences.esx[l.type])
        end
    elseif Config.Framework == 'qbcore' and QBCore then
        local joueur = QBCore.Functions.GetPlayer(source)
        local meta = joueur and joueur.PlayerData.metadata or {}
        for cle, possede in pairs(meta.licences or meta.licenses or {}) do
            if possede then ajouter(Config.Licences.qbcore[cle]) end
        end
    end
    return categories
end

local function declarerPermis(source, obtention)
    obtention = champ(obtention, 10)
    if Config.Framework == 'aucun' then
        return dire(source, 'Déclaration du permis indisponible sans framework (ESX ou QBCore).', 'erreur')
    end
    local joueur, p = joueurSaphir(source)
    if not joueur then return end
    local categories = categoriesPermis(source, p)
    if #categories == 0 then
        return dire(source, 'Ce personnage ne possède aucun permis de conduire en jeu.', 'erreur')
    end
    if not obtention then
        return demanderDialogue(source, 'permis', { categories = table.concat(categories, ', ') })
    end
    saphir('POST', '/permis', { discordId = joueur.discordId, personnageId = joueur.personnageId, nomJoueur = joueur.nomJoueur,
        categories = categories, dateObtention = obtention }, function(ok, donnees)
        if not ok then return dire(source, donnees, 'erreur') end
        dire(source, donnees.message, 'succes')
    end)
end

-- =====================================================================================
-- Commande et réponses des dialogues
-- =====================================================================================

local function aide(source)
    local c = '/' .. Config.Commande
    dire(source, table.concat({
        c .. ' : déclarer ou mettre à jour le personnage joué (' .. c .. ' maj)',
        c .. ' etat : voir tes personnages déclarés',
        c .. ' vehicules [PLAQUE] : déclarer tes véhicules (ou un seul)',
        c .. ' assurance PLAQUE | ASSUREUR | CONTRAT | JJ/MM/AAAA',
        c .. ' vehicule-retirer PLAQUE : retirer un véhicule',
        c .. ' permis [JJ/MM/AAAA] : déclarer ton permis (date d\'obtention)',
        c .. ' retirer : retirer ce personnage des fichiers',
        'Jeu de rôle : données fictives, n\'indique jamais ta véritable identité.'
    }, '\n'), 'info')
end

RegisterCommand(Config.Commande, function(source, args, brut)
    if source == 0 then return print('[saphir-identite] commande réservée aux joueurs.') end
    local action = args[1] and args[1]:lower() or ''
    if action == 'aide' or action == 'help' then return aide(source) end
    if tropTot(source) then return end
    -- Reste de la ligne après l'action (« a | b | c »).
    local reste = trim((brut or ''):gsub('^%S+%s*', ''):gsub('^%S+%s*', ''))
    CreateThread(function()
        if action == '' or action == 'maj' or action == 'declarer' then
            if action ~= '' and reste then
                local libres = libresDepuisArguments(reste)
                return declarer(source, libres)
            end
            if Config.Dialogue == 'arguments' then
                return dire(source, Config.Framework == 'aucun'
                    and ('Usage : /%s maj NOM | Prénom | JJ/MM/AAAA | M ou F | Lieu de naissance | Adresse | Profession | Taille (cm) | Corpulence | Signes particuliers'):format(Config.Commande)
                    or ('Usage : /%s maj Lieu de naissance | Adresse | Profession | Taille (cm) | Corpulence | Signes particuliers'):format(Config.Commande), 'info')
            end
            return etat(source, true)
        elseif action == 'etat' then
            return etat(source, false)
        elseif action == 'retirer' then
            if reste and reste:lower() == 'confirmer' then return retirer(source) end
            return demanderDialogue(source, 'retrait', {})
        elseif action == 'vehicules' or action == 'vehicule' then
            return declarerVehicules(source, reste)
        elseif action == 'vehicule-retirer' or action == 'retirer-vehicule' then
            return retirerVehicule(source, reste)
        elseif action == 'assurance' then
            local c = decouper(reste)
            if not c[1] then return demanderDialogue(source, 'assurance', {}) end
            return declarerAssurance(source, { plaque = c[1], assureur = c[2], contrat = c[3], fin = c[4] })
        elseif action == 'permis' then
            return declarerPermis(source, reste)
        else
            return aide(source)
        end
    end)
end, false)

--- Réponse d'une boîte de dialogue (acceptée seulement si le serveur l'a demandée, dans les 3 minutes).
RegisterNetEvent('saphir-identite:reponse', function(genre, valeurs)
    local source = source
    local attente = attentes[source]
    attentes[source] = nil
    if not attente or attente.genre ~= genre or GetGameTimer() > attente.expire then return end
    if valeurs == nil then return end -- dialogue annulé
    CreateThread(function()
        if genre == 'identite' then
            local libres = lireLibres(valeurs)
            if libres then declarer(source, libres) end
        elseif genre == 'retrait' then
            if valeurs == true then retirer(source) end
        elseif genre == 'permis' then
            if type(valeurs) == 'table' then declarerPermis(source, valeurs.dateObtention) end
        elseif genre == 'assurance' then
            declarerAssurance(source, valeurs)
        end
    end)
end)

-- Suggestion du chat pour chaque joueur qui arrive (commande configurable).
AddEventHandler('playerJoining', function()
    TriggerClientEvent('chat:addSuggestion', source, '/' .. Config.Commande, 'Déclarer ton personnage auprès de la police (SAPHIR)', {
        { name = 'action', help = 'maj, etat, vehicules, assurance, vehicule-retirer, permis, retirer, aide' }
    })
end)

AddEventHandler('onResourceStart', function(nom)
    if nom == RESSOURCE then
        journal('debug', ('Commande /%s prête (framework : %s).'):format(Config.Commande, Config.Framework))
    end
end)
