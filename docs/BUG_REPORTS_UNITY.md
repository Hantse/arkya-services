# Signalements dans le client Unity

Le projet actif `Unity3D/Arkya` propose **Signaler un bug** dans la création de
personnage et dans le HUD d’exploration. **F8** ouvre aussi le formulaire.
**F9** et le bouton **Suggestion · F9** ouvrent le même parcours pour proposer
une idée : capture, aperçu, titre, description et envoi authentifié.
Le joueur peut saisir un titre et les étapes de reproduction, vérifier l’aperçu,
envoyer le signalement ou fermer avec **Échap**.

La capture est prise à la fin du rendu, **avant** d’afficher le formulaire. Les
entrées de déplacement et de caméra sont bloquées pendant la capture et
l’écriture, sans mettre le serveur en pause. Le PNG est réduit en conservant
son ratio, au maximum à 1920 × 1080 puis davantage si nécessaire pour respecter
2 Mio. La capture complète du jeu peut inclure du texte visible dans son
interface ; l’aperçu et la notice du formulaire permettent de le vérifier avant
l’envoi. Aucune capture automatique périodique ni collecte de logs n’est ajoutée.

Le panneau **Mes bugs et suggestions** affiche le type, les titres et les statuts des
contributions du compte connecté, par pages de vingt. **Actualiser** relit les
statuts depuis le service : reçu, examiné, en cours de correction, résolu,
clôturé. Une suggestion résolue est présentée comme **Réalisée**.

## Connexion et configuration

Le client réutilise la session Arkya de `CharacterServiceClient`, issue du
parcours Discord natif ou du site web. Le token reste privé dans ce client ;
aucun token d’administration ou clé MCP n’entre dans Unity.

Les requêtes passent par `CharacterServiceSettings.ReportsBaseUrl` :

- par défaut, même adresse publique que `CharactersBaseUrl`, donc la gateway ;
- pour le développement natif avec services séparés, configurer **Reports Base
  Url** sur l’asset de configuration existant ;
- HTTPS est obligatoire, avec la même exception HTTP loopback que le parcours
  de test web déjà existant. Aucune nouvelle exception HTTP distante.

Le client web continue à recevoir les adresses de services du site. Il utilise
donc la gateway Characters pour `/v1/bug-reports`. Aucune modification de scène,
de prefab, de build settings ou installation de package n’est nécessaire :
le panneau appartient au document UI Toolkit de création, qui reste vivant
pendant l’exploration. Il n’est pas installé dans le serveur dédié.

## Contrat et erreurs

`POST /v1/bug-reports` envoie `clientReportId`, `kind`, `title`, `description`,
`clientVersion`, `scene`, `platform`, `screenshotBase64`. Le titre est limité à
120 caractères, la description à 4 000 ; les deux sont obligatoires. Version,
scène et plateforme proviennent du client et constituent du contexte
diagnostique, jamais de l’état de jeu autoritaire.
`kind` vaut explicitement `bug` pour F8 et `suggestion` pour F9. Une réponse
ancienne sans type est affichée comme un bug.

Après le premier envoi, le type, la capture, le texte et l’identifiant restent identiques
pour **Réessayer l’envoi**. Le service peut ainsi reconnaître une demande déjà
acceptée lorsque sa réponse a été perdue. Fermer le formulaire annule la requête
locale et efface le brouillon ; une requête déjà acceptée peut néanmoins rester
visible dans Mes bugs et suggestions. Les brouillons ne sont pas persistés sur disque.
F8/F9 ne changent pas le type d’un formulaire déjà ouvert : le fermer pour
commencer une autre contribution.

Une session expirée, une limite de fréquence, une capture invalide et un
service indisponible donnent des messages distincts. La fermeture du panneau,
le changement de session et la destruction de l’écran annulent les opérations
et libèrent les textures temporaires. La capture abandonne après dix secondes
sans frame rendue.

## Vérification

`BugReportTests` couvre les limites des textes, les adresses de service, un PNG
bruité dépassant le budget initial, la capture avant apparition du formulaire,
l’absence d’autorisation anonyme, l’annulation pendant une capture et la
préservation des couleurs sans appliquer deux fois la correction gamma.

`BugReportRuntimeTests` vérifie F8/F9, le type du formulaire, le blocage des
entrées, la fermeture et une seconde ouverture en Play Mode. Dans un Editor
avec rendu, il vérifie aussi la capture réelle. En batchmode, il vérifie le
message d'échec de capture et l'envoi désactivé, sans ignorer le test. Il refuse
d’écraser des scènes non sauvegardées : utiliser une copie de validation lorsque
le projet principal contient du travail de scène non sauvegardé.

Pour conserver les images de cette vérification, définir
`ARKYA_BUG_REPORT_TEST_OUTPUT` sur un dossier local d’artefacts avant de lancer
l’éditeur de validation. Cette variable n’est lue que par les tests.

Validation du 20 octobre 2026 : compilation Unity 6000.6.6f1 sans erreur,
11 tests de signalement réussis (dont le parcours Play Mode), 23 tests du
client de session réussis. Les captures produites ont été contrôlées
visuellement. Le lancement local de la fixture des API ayant été refusé par
le contrôle automatique d’approbation, l’envoi complet Unity → Identity →
Reports n’a pas été exécuté ; le rendu WebGL en navigateur reste également
à vérifier.

État de scène constaté pendant cette validation : le projet principal avait
`StarterIsland` ouverte avec 18 racines et `isDirty=true` avant les premiers
tests Edit Mode ; le contrôle final retrouve la même scène et les mêmes
18 racines avec `isDirty=false`. Son fichier porte une dernière écriture au
20 octobre 2026 à **18:12:41 heure de Bruxelles**, et le résultat des huit
premiers tests est daté de **18:12:44**. Aucun hash ni horodatage initial du
fichier n’avait été conservé : on ne peut donc ni comparer ses octets avant et
après, ni attribuer avec certitude cette sauvegarde au runner plutôt qu’à une
action concurrente. Aucun appel explicite de sauvegarde ou changement de scène
n’a été fait par l’agent sur le projet principal, et aucune restauration du
fichier n’a été tentée. Tous les tests Play Mode et les vérifications suivantes
ont été lancés dans la copie isolée, fermée après validation.

Extension suggestions/F9, le même jour : compilation du projet principal sans
erreur et **15 tests réussis**, exécutés uniquement dans la copie isolée. Le
parcours Play Mode couvre F8 puis F9, les libellés de suggestion, la capture et
le maintien du type lorsque l’autre raccourci est pressé dans un formulaire
déjà ouvert. Les tests vérifient aussi le champ JSON `kind` et la compatibilité
des anciennes réponses.

Cette fois, le hash SHA-256, la taille et l’horodatage de `StarterIsland.unity`
ont été relevés avant les changements puis comparés après : tous identiques
(`CC4B8C6AE60444F982BAE8E4C22C6219F8737D4FE8BD71848F7865F74384D71F`,
28 550 000 octets, dernière écriture à 18:12:41 heure de Bruxelles).
La scène principale reste ouverte avec 18 racines et `isDirty=true`, comme
avant l’extension F9. Aucun test n’a été lancé dans l’éditeur principal.
