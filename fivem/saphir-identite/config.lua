Config = {}

-- Adresse du serveur SAPHIR (sans « / » final) et clé d'intégration : même valeur que SAPHIR:AccessKey
-- dans appsettings.Local.json du serveur SAPHIR. Ne jamais publier cette clé (elle reste côté serveur FiveM).
Config.SaphirUrl = 'http://127.0.0.1:8091'
Config.SaphirKey = 'CHANGE_ME'

-- Framework : 'esx', 'qbcore' ou 'aucun'.
-- 'aucun' : le joueur saisit aussi NOM, prénom, date de naissance et sexe ; le personnage est identifié par sa licence Rockstar.
Config.Framework = 'qbcore'

-- Nom de la commande (/identite par défaut).
Config.Commande = 'identite'

-- ESX : format de users.dateofbirth ('jj/mm/aaaa' pour esx_identity, 'mm/jj/aaaa' pour certaines versions américaines).
Config.FormatDateEsx = 'jj/mm/aaaa'

-- Nationalité envoyée quand le framework n'en fournit pas (ESX).
Config.NationaliteParDefaut = 'Française'

-- Correspondance licences du framework → catégories de permis SAPHIR.
-- ESX : table user_licenses (type) ; QBCore : PlayerData.metadata.licences (clé = true).
Config.Licences = {
    esx = { drive = 'B', drive_bike = 'A', drive_truck = 'C' },
    qbcore = { driver = 'B' }
}

-- Délai minimal entre deux demandes d'un même joueur (millisecondes).
Config.Delai = 5000

-- Notifications : 'auto' (ox_lib si démarré, sinon chat), 'ox_lib' ou 'chat'.
Config.Notification = 'auto'

-- Boîte de dialogue : 'auto' (ox_lib si démarré, sinon arguments de commande), 'ox_lib' ou 'arguments'.
Config.Dialogue = 'auto'

-- Journal de la console serveur : 'erreurs' ou 'debug'.
Config.Journal = 'erreurs'
