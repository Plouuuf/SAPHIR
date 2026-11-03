fx_version 'cerulean'
game 'gta5'
lua54 'yes'

name 'saphir-identite'
description 'SAPHIR : déclaration en jeu des personnages, véhicules et permis auprès de la police (Val-de-Marne Simulation)'
author 'VDMS'
version '1.0.0'

-- Les données du personnage sont lues côté serveur (framework et base FiveM) ; le client ne fournit que les champs
-- libres de la boîte de dialogue (lieu de naissance, adresse, profession, signalement), contrôlés par le serveur.
-- config.lua reste côté serveur : la clé SAPHIR n'est jamais envoyée aux joueurs.
-- oxmysql (fourni avec ESX et QBCore) est appelé par ses exports : inutile avec Config.Framework = 'aucun'.
client_script 'client.lua'
server_scripts {
    'config.lua',
    'server.lua'
}
