fx_version 'cerulean'
game 'gta5'
lua54 'yes'

name 'saphir-geoops'
description 'SAPHIR GÉOOPS : envoi des positions des agents de police en service au serveur SAPHIR (Val-de-Marne Simulation)'
author 'VDMS'
version '1.0.0'

-- Script serveur uniquement : aucune ressource client, aucune donnée envoyée aux joueurs.
server_scripts {
    'config.lua',
    'server.lua'
}
