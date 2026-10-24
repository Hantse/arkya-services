# Migration approuvée du runtime d'un realm

Une image seule ne peut pas modifier le protocole réseau ou la topologie d'un
realm déployé. `Deployment:RuntimeReleases` fournit une transition explicite
pour publier un nouveau runtime en conservant le realm, ses personnages et
son identité de déploiement. Les éditions ordinaires restent limitées aux
realms en brouillon et les publications d'images gardent leurs contrôles.

Chaque entrée approuvée indique `RealmId`, `ProfileId`, `Version`,
`PreviousProfileSha256` et une `Topology` complète. `Automatic` vaut `false`
par défaut : une release valide est alors proposée dans **Maintenance → Update
realm → Install approved version**. L'empreinte SHA-256 du profil courant est lisible dans la console
opérateur et le contrat admin Realms ; elle inclut image, arguments, ports,
secrets référencés, protocole et découpage. Aucun secret n'est inclus.

La transition exige un realm `Ready`, une cible exactement autorisée et un
nouveau profil immuable avec runtime de partitions pris en charge. Les
identifiants des nouveaux serveurs sont disjoints des anciens. Les références
de secrets restent identiques. Le service enregistre d'abord la transition
par comparaison de version et retire ainsi l'admission avant de toucher au
cluster. Le document MongoDB passe au schéma 3, avec snapshot précédent et
historique opérateur `RuntimeMigration`.

Le profil cible peut définir `TopologyServerIdPrefix` pour attribuer de nouveaux
identifiants aux processus, sans renommer les Areas ni leurs partitions. Le
préfixe absent conserve les identifiants et empreintes des profils historiques.
La requête manuelle porte la révision et l'image approuvée affichées, l'opérateur
et un motif ; l'admin demande aussi de recopier l'identifiant du realm. Les
rejeux ne démarrent pas une seconde migration. Les métadonnées approuvées
`GameVersion`, `SourceRevision` et `SourceRunId` permettent de vérifier la
version et la provenance ; elles ne remplacent pas les gardes de migration.

Le déployeur vérifie l'appartenance, l'image, les arguments et les versions
des anciens Deployments, les réduit à zéro par JSON Patch conditionnel et
attend la disparition des réplicas, y compris ceux en terminaison. Une lecture
paginée des Pods aux labels exacts de l'ancien serveur confirme leur absence,
y compris si le Deployment a disparu ou si son compteur des Pods en terminaison
est absent. Une réponse incomplète ou une appartenance inconnue bloque l'opération.
Ce droit `get/list` reste limité au namespace du realm ; les Pods ne sont pas modifiés.
Il ne crée aucun nouveau workload avant cet arrêt. Le Secret `arkya-dedicated` est alors
remplacé uniquement si son contenu est exactement l'ancien jeu de clés
dérivées attendu, avec contrôle de `resourceVersion`. Le droit Kubernetes
`patch` porte uniquement sur ce Secret nommé. Les pertes de réponse et les
reprises réutilisent la transition persistée ; une dérive bloque la migration.

Pendant l'arrêt, le catalogue interne publie séparément les identités des anciens
serveurs dans `retiringServers`. Characters les accepte exclusivement pour le
renouvellement, la sauvegarde et la libération d'une lease déjà possédée. Elles
ne peuvent plus consommer d'admission, lire la topologie ni démarrer un transfert.
Le realm reste absent des admissions prêtes. Les identités sont révoquées à la
fin de la migration ou dès une suppression ; elles ne sont pas exposées dans le
catalogue joueur. Les contrôles existants de lease, époque, session et révision
de sauvegarde continuent de s'appliquer.

L'entrée doit rester approuvée pendant toute la transition. Le realm redevient
`Ready` lorsque chaque serveur attendu expose le nouveau digest. La transition
est alors marquée terminée. Aucun accès direct à MongoDB, remplacement de
realm, déplacement de personnage ou contournement de protocole n'est requis.

## Ordre de publication

1. Compiler/tester et publier les API, l'Admin et les routes de partitions,
   sans activer la transition. Le profil Admin utilise `Recreate` pour Realms
   afin d'éviter le chevauchement de générations du réconciliateur. Attendre
   la fin de ce rollout.
2. Construire et vérifier les clients et le dedicated compatibles, puis
   approuver leur digest. Provisionner les quatre routes, Services et
   identités dédiées finies avec `Automatic=false` ; conserver l'ancien
   identifiant pendant l'arrêt et attendre la fin des trois rollouts.
3. Relever l'empreinte réelle du profil et les joueurs connectés. Sauvegarder
   les joueurs avant l'interruption si nécessaire.
4. Activer la transition explicitement dans GitOps et suivre le realm jusqu'à
   ses quatre serveurs prêts. Le changement interrompt l'accès des clients
   utilisant l'ancien protocole ; leur mise à jour est nécessaire.
5. Publier le nouveau manifeste de téléchargement seulement après ces
   vérifications. Tester ensuite l'admission avec le client distribué.

Pour une intervention manuelle, conserver `Automatic=false` et lancer l'action
dans l'admin après le rollout des API Characters/Realms, de l'Admin et des routes.
La préparation GitOps ne redémarre pas le realm. L'admin affiche la version du
jeu ainsi que les protocoles courant et cible avant la confirmation.

Pour vérifier la publication sans lancer la maintenance, recharger la fiche du
realm, ouvrir `Maintenance` puis `Update realm` et vérifier la version du jeu,
les protocoles et la provenance proposés. Le bouton d'installation doit être
disponible pour une release approuvée applicable. Laisser le motif et la
confirmation vides pendant cette vérification. La présence du bouton ne prouve
ni la migration des serveurs ni l'admission du nouveau client : ces contrôles
se font après le lancement explicite de l'opération.

## Nova : version 0.1.49

Le composant actif `native-nova` prépare la release issue de la CI Unity 49 :
protocole `5 / arkya-town-npcs-v1`, quatre processus `v5-starter-island-01..04`
et la même Area de 1 536 × 1 536, découpée en grille 2 × 2. L'empreinte source
`c652962aa8072e2680ed4406fbcc3b3a6b373743560aa89ff50ef558f4ac1781` a été relue
dans la fiche Nova en ligne avant préparation. Les anciens profil, endpoints et
Services sont conservés. La release reste manuelle et exige un client compatible.
Voir les [manifests et leur vérification](../backend/deploy/gitops/native-nova/README.md).

Le client Windows correspondant est distribué par la
[release Bêta 0.1.49](https://github.com/Hantse/arkya-services/releases/tag/windows-beta-0.1.49).
Son manifeste Bêta doit proposer `0.1.49` avec `launcherSessionProtocol=1`,
indépendant du protocole de jeu `5`. Vérifier aussi cette distribution avant
la maintenance : la préparation d'une migration ne bascule pas le feed du
launcher. Un ancien client de protocole `3` ne voit plus Nova après sa migration.
Le retour à `Ready` du realm ne prouve pas l'admission du client distribué.

Le composant `backend/deploy/gitops/partition-handoff` prépare cette transition
pour StarterIsland (1 536 × 1 536, quatre partitions de 768 × 768). Il reste
inactif tant qu'il n'est pas référencé par le profil Admin et explicitement
activé. Un retour arrière de protocole/topologie demande une autre migration
planifiée ; il ne faut pas rétablir aveuglément les anciennes images.
