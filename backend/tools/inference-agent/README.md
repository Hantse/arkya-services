# Agent d’inférence CPU Arkya

Ce package Windows x64 lance une démo locale de Maëlle et plusieurs processus Ollama
indépendants. Chaque moteur utilise LFM2.5-1.2B Instruct Q4_K_M ou Qwen3.5-0.8B Q4_K_M.
Le modèle et le nombre de threads se règlent séparément dans le backoffice. Les trois boutons et la saisie
libre passent par de vraies inférences. Le souvenir de livraison reste un scénario
fourni au modèle, sans MongoDB ni mutation du jeu.

## Installation sur une autre machine

Extraire `Arkya-Inference-Agent-Windows-x64.zip`, puis ouvrir PowerShell dans ce dossier.
Pour une machine gérée, créer un jeton temporaire d’inscription dans le backoffice.
Le serveur public doit router `/agent/inference/enroll` et `/agent/inference/heartbeat`.
Exécuter simplement :

```powershell
.\Install.cmd -BackendUrl 'https://inference.example.net'
```

Le jeton temporaire est demandé par une saisie masquée. L’installateur dérive une identité
stable `pc-<nom-du-PC>` et affiche le nom Windows comme nom de machine ; `-AgentId` et
`-DisplayName` permettent de les choisir. Il crée lui-même une clé d’appareil aléatoire
de 256 bits et la conserve dans DPAPI avant toute inscription distante. L’identité,
l’URL backend et l’état d’inscription sont enregistrés dans `config/enrollment-state.json`.
Ce fichier ne contient aucun secret. Une interruption réseau se reprend en relançant
la même commande : la clé existante est réutilisée, y compris sans `agent.json`.
Le serveur reconnaît une inscription déjà créée par cette clé, sans nouveau jeton
temporaire et sans réinitialiser les réglages que l’administrateur a changés entre-temps.
Une autre identité ou un autre backend demande un dossier d’installation distinct.

Aucun jeton n’est inclus dans le ZIP,
dans les URL, dans les arguments des processus persistants ni dans les journaux.
`-EnrollmentToken` accepte un `SecureString` fourni par un gestionnaire de secrets.
Le mode historique reste possible pour un agent créé manuellement dans le backoffice :
`-AgentId 'machine-01' -AgentToken (Read-Host 'Jeton de cet agent' -AsSecureString)`.
Le fichier de jeton est chiffré par DPAPI machine et son dossier est limité au compte
d’installation et à SYSTEM ; en installation SYSTEM, uniquement Administrateurs et SYSTEM.

Sans backend, `Install.cmd` ou `Install-Agent.ps1` installe un mode autonome de trois
moteurs. Ouvrir ensuite **http://127.0.0.1:8767/**. Une première installation doit télécharger
le modèle, environ 731 Mo pour LFM ou 533 Mo pour Qwen, avant que les moteurs soient prêts. Le ZIP complet contient
Python et les binaires CPU d’Ollama ; il ne nécessite aucune installation système de
Python/Ollama. Un ZIP construit sans ces dépendances les télécharge depuis leurs
sources officielles épinglées et vérifie leur SHA256. L’archive officielle complète
Ollama pèse environ 2 Go ; seul son contenu CPU est extrait.

Avec une console administrateur, l’installation par défaut se fait dans
`C:\ProgramData\ArkyaInference`, protégée pour Administrateurs/SYSTEM, et crée une tâche
SYSTEM au démarrage de Windows. Sans élévation, ou avec `-RunAtLogon`, elle se fait dans
`%LOCALAPPDATA%\ArkyaInference` et crée une tâche au démarrage de la session utilisateur.
Les chemins personnalisés exigent le mode utilisateur (`-RunAtLogon`). Toutes les
fenêtres des processus persistants sont masquées. Le démarrage automatique relance
`Start-Agent.ps1`, qui refuse les fichiers dont le SHA256 diffère du manifeste.
Le superviseur PowerShell reste attaché au processus Python et le relance jusqu’à
trois fois, à dix secondes d’intervalle, en cas de sortie anormale (quatre lancements
maximum par exécution du superviseur). La tâche Windows configure aussi jusqu’à trois
reprises du superviseur, à une minute d’intervalle ; cette seconde protection dépend
du comportement du Planificateur. Un arrêt demandé par `Stop-Agent.ps1`, y compris
pendant une attente de reprise, ne déclenche pas de nouvelle inférence ni de relance.

Paramètres utiles :

```powershell
.\Install-Agent.ps1 -RunAtLogon -InitialWorkers 3 -ThreadsPerWorker 4
.\Install-Agent.ps1 -RunAtLogon -ModelId qwen35-08b -InitialWorkers 2 -ThreadsPerWorker 3 -MaxWorkers 6
.\Install-Agent.ps1 -RunAtLogon -InstallDirectory 'D:\ArkyaInference' -DemoPort 8768 -BaseWorkerPort 11541
.\Start-Agent.ps1
.\Stop-Agent.ps1
.\Uninstall-Agent.ps1
# Suppression explicite du cache enregistré :
.\Uninstall-Agent.ps1 -RemoveModelCache
```

La désinstallation conserve le cache modèle par défaut. `-ModelCache` permet d’en
choisir le chemin. `-NoAutoStart` et `-SkipScheduledTask` servent aux validations.
`-AllowLoopbackHttp` autorise uniquement un backend HTTP sur loopback pour les tests.
L’installation est relançable : elle arrête uniquement son propre agent, conserve
le cache et le jeton de la même inscription, puis met à jour les fichiers.

## Contrôle et fonctionnement

Un agent représente une machine. Il envoie toutes les cinq secondes son état à
`POST {BackendUrl}/agent/inference/heartbeat`, avec authentification Bearer, puis
applique la liste de moteurs demandée par le backoffice. HTTPS est obligatoire hors
test loopback ; les redirections sont refusées. Le backend ne peut sélectionner ni
exécutable, ni commande, ni chemin de fichier. Il sélectionne uniquement les modèles
du catalogue livré et un nombre de threads borné.

La première connexion d’une nouvelle machine gérée est obligatoire avant de créer
des moteurs. Après une première synchronisation, le dernier état désiré est conservé
sur disque ; une coupure du backend conserve cet état, y compris après redémarrage.
Le retrait d’un moteur empêche immédiatement de nouvelles allocations, attend sa
réponse en cours puis ferme son processus. Un moteur défaillant est recréé après un
court délai. Les changements de threads entraînent ce même remplacement.
Les changements de modèle utilisent la même transition : le moteur remplacé termine
sa réponse, ferme son ancien processus, puis redémarre sur son port libéré. Les autres
moteurs conservent leur PID et leurs requêtes en cours. Une requête déjà lancée affiche
le modèle qui a effectivement produit sa réponse.

Le catalogue `models.json` provient de `backend/src/Arkya.ServiceDefaults/InferenceModels.json`
et fait partie du manifeste du package. Aucun identifiant de modèle externe envoyé
par le backend n’est accepté. Qwen est téléchargé depuis une révision Hugging Face fixe,
vérifié par SHA256 et importé avec le template ChatML officiel sans projecteur vision.
Ce chemin contourne le refus du projecteur Qwen par Ollama 0.20.2. Les requêtes Qwen
désactivent le raisonnement (`think=false`). Le cache des poids est partagé ; les
téléchargements et imports sont verrouillés par modèle, sans mélanger les deux caches.

Chaque moteur écoute uniquement sur loopback, à partir de 11441, avec une seule
inférence à la fois. Le pool attribue chaque demande au premier moteur disponible,
avec au plus 12 demandes en attente et un délai d’attente de 30 secondes. La démo
écoute uniquement sur loopback et valide hôte/origine pour refuser les accès depuis
une autre page web. Aucun port n’est exposé au LAN. La démo affiche les vrais délais,
le débit et le moteur ayant répondu ; elle vérifie après l’inférence que la mémoire
GPU rapportée est nulle. Toutes les requêtes imposent aussi `num_gpu=0`.

Les arrêts ne ciblent jamais les autres installations Ollama. Les processus enfants
appartiennent à un job Windows privé ; les scripts vérifient chemin, ligne de
commande et date de création du PID avant de l’arrêter. Les conversations restent
dans la page ; les logs ne contiennent ni dialogues ni credentials.

Cette première version gère la capacité et la démo locale. Elle ne distribue pas les
requêtes de joueurs distants entre machines. Le dimensionnement CPU/RAM dépend de
la machine : trois moteurs avec quatre threads chacun sont une configuration de
départ, pas une garantie de débit. Prévoir plusieurs Go libres pour le modèle et
ses trois instances. La licence du modèle est celle de LiquidAI et doit être
revue pour l’usage commercial prévu.

## Construire le ZIP et vérifier

```powershell
python -m unittest discover -s backend/tools/inference-agent/tests -v
.\backend\tools\inference-agent\Build-Package.ps1 `
  -OllamaDirectory "$env:LOCALAPPDATA\Programs\Ollama" `
  -PythonArchive '.\artifacts\inference-package-deps\python-3.13.7-embeddable-amd64.zip'
```

Le builder vérifie la signature Authenticode et la version 0.20.2 de l’exécutable
Ollama fourni ; il copie uniquement ses DLL CPU. Python 3.13.7 est vérifié contre
le hash officiel de son archive. Les licences Python, Ollama, llama.cpp et ggml sont
incluses. Le manifeste décrit les SHA256 de chaque fichier et leur provenance.
Le ZIP et son SHA256 global sont créés sous `artifacts/inference-agent-package/`.
Les scripts de construction n’incluent ni configuration machine, ni jeton, ni cache,
ni logs. Les dépendances épinglées se trouvent dans `dependencies.json`.

Sources : [Ollama v0.20.2](https://github.com/ollama/ollama/releases/tag/v0.20.2),
[Python 3.13.7](https://www.python.org/ftp/python/3.13.7/windows-3.13.7.json),
[modèle LiquidAI](https://huggingface.co/LiquidAI/LFM2.5-1.2B-Instruct-GGUF).
