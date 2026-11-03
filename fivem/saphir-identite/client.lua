-- =====================================================================================
-- SAPHIR — déclaration d'identité : affichage côté joueur (messages et boîtes de dialogue).
-- Aucune donnée du personnage n'est lue ici : le serveur FiveM lit le framework et la base, puis appelle SAPHIR.
-- Boîtes de dialogue : ox_lib (exports) si la ressource est démarrée ; sinon, le joueur reçoit la syntaxe en arguments.
-- =====================================================================================

local AVERTISSEMENT = 'Jeu de rôle : données fictives, n\'indique jamais ta véritable identité.'

local function oxLib(opts)
    if opts and (opts.dialogue == 'arguments') then return false end
    return GetResourceState('ox_lib') == 'started'
end

local function oxNotif(opts)
    if opts and opts.notification == 'chat' then return false end
    return GetResourceState('ox_lib') == 'started'
end

local function chat(texte, genre)
    local couleur = genre == 'erreur' and { 230, 80, 80 } or (genre == 'succes' and { 90, 190, 110 } or { 90, 150, 230 })
    for ligne in (texte .. '\n'):gmatch('([^\n]*)\n') do
        if ligne ~= '' then
            TriggerEvent('chat:addMessage', { color = couleur, multiline = true, args = { 'SAPHIR', ligne } })
        end
    end
end

local function message(texte, genre, opts)
    texte = tostring(texte or '')
    if oxNotif(opts) then
        local ok = pcall(function()
            exports.ox_lib:notify({
                title = 'SAPHIR — Déclaration',
                description = texte,
                type = genre == 'erreur' and 'error' or (genre == 'succes' and 'success' or 'inform'),
                duration = 4000 + math.min(#texte, 400) * 40
            })
        end)
        if ok then
            -- Les messages longs (état, liste de véhicules) restent aussi lisibles dans le chat.
            if texte:find('\n') then chat(texte, genre) end
            return
        end
    end
    chat(texte, genre)
end

RegisterNetEvent('saphir-identite:message', function(texte, genre, opts)
    message(texte, genre, opts)
end)

local function repondre(genre, valeurs)
    TriggerServerEvent('saphir-identite:reponse', genre, valeurs)
end

local function vide(v)
    return v == nil or v == ''
end

-- ----- Dialogues ox_lib -----

local CORPULENCES = { 'Mince', 'Moyenne', 'Forte', 'Athlétique', 'Corpulente' }

local function dialogueIdentite(v)
    local lignes, cles = {}, {}
    local function ajouter(cle, ligne) lignes[#lignes + 1] = ligne cles[#cles + 1] = cle end
    local figee = v.identiteFigee == true

    if v.libre then
        ajouter('nom', { type = 'input', label = 'NOM', description = AVERTISSEMENT, default = v.nom, required = true, min = 2, max = 60, disabled = figee })
        ajouter('prenom', { type = 'input', label = 'Prénom(s)', default = v.prenom, required = true, min = 2, max = 60, disabled = figee })
        ajouter('dateNaissance', { type = 'input', label = 'Date de naissance', placeholder = 'JJ/MM/AAAA', default = v.dateNaissance, required = true, max = 10, disabled = figee })
        ajouter('sexe', { type = 'select', label = 'Sexe', default = v.sexe, required = true,
            options = { { value = 'M', label = 'Masculin' }, { value = 'F', label = 'Féminin' }, { value = 'X', label = 'Autre' } } })
    end
    ajouter('lieuNaissance', { type = 'input', label = 'Lieu de naissance', description = (not v.libre) and AVERTISSEMENT or nil,
        placeholder = 'Ex. Créteil (94)', default = v.lieuNaissance, required = not figee, max = 120, disabled = figee })
    ajouter('adresse', { type = 'input', label = 'Adresse', placeholder = 'Ex. 12 rue des Écoles, 94000 Créteil', default = v.adresse, max = 255 })
    ajouter('profession', { type = 'input', label = 'Profession', default = v.profession, max = 120 })
    ajouter('tailleCm', { type = 'number', label = 'Taille (cm) — facultatif', default = v.tailleCm, min = 100, max = 250 })
    local options = {}
    for _, c in ipairs(CORPULENCES) do options[#options + 1] = { value = c, label = c } end
    ajouter('corpulence', { type = 'select', label = 'Corpulence — facultatif', default = v.corpulence, options = options, clearable = true })
    ajouter('signesParticuliers', { type = 'textarea', label = 'Signes particuliers — facultatif', default = v.signesParticuliers, max = 500, autosize = true })

    local titre = v.declare and 'Mise à jour de ton personnage (SAPHIR)' or 'Déclaration de ton personnage (SAPHIR)'
    local resultat = exports.ox_lib:inputDialog(titre, lignes, { allowCancel = true })
    if not resultat then return nil end
    local valeurs = {}
    for i, cle in ipairs(cles) do
        local valeur = resultat[i]
        if not vide(valeur) then valeurs[cle] = valeur end
    end
    if figee then
        -- Champs désactivés : on renvoie les valeurs connues (SAPHIR garde de toute façon l'identité figée).
        valeurs.lieuNaissance = v.lieuNaissance
        if v.libre then valeurs.nom, valeurs.prenom, valeurs.dateNaissance = v.nom, v.prenom, v.dateNaissance end
    end
    return valeurs
end

local function dialoguePermis(v)
    local resultat = exports.ox_lib:inputDialog('Permis de conduire (SAPHIR)', {
        { type = 'input', label = 'Catégories (lues en jeu)', default = v.categories, disabled = true },
        { type = 'input', label = 'Date d\'obtention', placeholder = 'JJ/MM/AAAA', required = true, max = 10,
          description = 'Moins de 3 ans : permis probatoire (6 points). Les points sont gérés par la police.' }
    }, { allowCancel = true })
    if not resultat then return nil end
    return { dateObtention = resultat[2] }
end

local function dialogueAssurance()
    local resultat = exports.ox_lib:inputDialog('Assurance d\'un véhicule (SAPHIR)', {
        { type = 'input', label = 'Plaque', required = true, max = 20 },
        { type = 'input', label = 'Assureur', required = true, max = 100 },
        { type = 'input', label = 'N° de contrat', required = true, max = 60 },
        { type = 'input', label = 'Fin de couverture', placeholder = 'JJ/MM/AAAA', required = true, max = 10 }
    }, { allowCancel = true })
    if not resultat then return nil end
    return { plaque = resultat[1], assureur = resultat[2], contrat = resultat[3], fin = resultat[4] }
end

local function dialogueRetrait()
    local choix = exports.ox_lib:alertDialog({
        header = 'Retirer ce personnage ?',
        content = 'La fiche sera retirée des fichiers de la police (conservée dans l\'historique). '
            .. 'Impossible si la personne figure déjà dans un fichier de police ou une procédure.',
        centered = true,
        cancel = true,
        labels = { confirm = 'Retirer', cancel = 'Annuler' }
    })
    return choix == 'confirm'
end

-- ----- Repli sans ox_lib : syntaxe des arguments -----

local function syntaxe(genre, v, opts)
    local c = '/' .. ((opts and opts.commande) or 'identite')
    if genre == 'identite' then
        if v.libre then
            return c .. ' maj NOM | Prénom | JJ/MM/AAAA | M ou F | Lieu de naissance | Adresse | Profession | Taille (cm) | Corpulence | Signes particuliers'
        end
        return c .. ' maj Lieu de naissance | Adresse | Profession | Taille (cm) | Corpulence | Signes particuliers\n' .. AVERTISSEMENT
    elseif genre == 'permis' then
        return ('%s permis JJ/MM/AAAA (date d\'obtention ; catégories lues en jeu : %s)'):format(c, v.categories or '—')
    elseif genre == 'assurance' then
        return c .. ' assurance PLAQUE | ASSUREUR | N° DE CONTRAT | JJ/MM/AAAA'
    elseif genre == 'retrait' then
        return 'Pour confirmer le retrait de ce personnage, tape : ' .. c .. ' retirer confirmer'
    end
    return c .. ' aide'
end

RegisterNetEvent('saphir-identite:dialogue', function(genre, valeurs, opts)
    valeurs = valeurs or {}
    if not oxLib(opts) then
        return message(syntaxe(genre, valeurs, opts), 'info', opts)
    end
    local ok, resultat = pcall(function()
        if genre == 'identite' then return dialogueIdentite(valeurs)
        elseif genre == 'permis' then return dialoguePermis(valeurs)
        elseif genre == 'assurance' then return dialogueAssurance()
        elseif genre == 'retrait' then return dialogueRetrait()
        end
        return nil
    end)
    if not ok then
        -- ox_lib présent mais incompatible : repli sur la syntaxe en arguments.
        return message(syntaxe(genre, valeurs, opts), 'info', opts)
    end
    repondre(genre, resultat)
end)
