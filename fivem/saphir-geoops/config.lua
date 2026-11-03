Config = {}

-- Adresse du serveur SAPHIR (sans « / » final) et clé d'intégration : même valeur que SAPHIR:AccessKey
-- dans appsettings.Local.json du serveur SAPHIR. Ne jamais publier cette clé.
Config.SaphirUrl = 'http://127.0.0.1:8091'
Config.SaphirKey = 'CHANGE_ME'

-- Intervalle d'envoi en millisecondes (5 secondes par défaut).
Config.Intervalle = 5000

-- Framework : 'esx', 'qbcore' ou 'aucun'.
Config.Framework = 'qbcore'

-- Métiers considérés comme « police » (nom du job ESX / QBCore).
Config.Jobs = { 'police' }

-- QBCore : n'envoyer que les agents en service (PlayerData.job.onduty). Accepte aussi les jobs de type « leo ».
Config.ServiceSeulement = true
Config.TypeJobQb = 'leo'

-- Framework « aucun » : permission ACE requise (ex. add_ace group.police saphir.geoops allow).
Config.Ace = 'saphir.geoops'

-- Nom envoyé : 'rp' (nom du personnage selon le framework) ou 'fivem' (pseudo du joueur).
Config.Nom = 'rp'

-- Journal de la console serveur (erreurs d'envoi). 'debug' affiche aussi chaque envoi.
Config.Journal = 'erreurs'
