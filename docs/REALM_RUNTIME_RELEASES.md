# Migration approuvée du runtime d'un realm

Une image seule ne peut pas modifier le protocole réseau ou la topologie d'un
realm déployé. `Deployment:RuntimeReleases` fournit une transition explicite
pour publier un nouveau runtime en conservant le realm, ses personnages et
son identité de déploiement. Les éditions ordinaires restent limitées aux
realms en brouillon et les publications d'images gardent leurs contrôles.

Chaque entrée approuvée indique `RealmId`, `ProfileId`, `Version`,
`PreviousProfileSha256` et une `Topology` complète. `Automatic` vaut `false`
par défaut. L'empreinte SHA-256 du profil courant est lisible dans la console
opérateur et le contrat admin Realms ; elle inclut image, arguments, ports,
secrets référencés, protocole et découpage. Aucun secret n'est inclus.

La transition exige un realm `Ready`, une cible exactement autorisée et un
nouveau profil immuable avec runtime de partitions pris en charge. Les
identifiants des nouveaux serveurs sont disjoints des anciens. Les références
de secrets restent identiques. Le service enregistre d'abord la transition
par comparaison de version et retire ainsi l'admission avant de toucher au
cluster. Le document MongoDB passe au schéma 3, avec snapshot précédent et
historique opérateur `RuntimeMigration`.

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

Le composant `backend/deploy/gitops/partition-handoff` prépare cette transition
pour StarterIsland (1 536 × 1 536, quatre partitions de 768 × 768). Il reste
inactif tant qu'il n'est pas référencé par le profil Admin et explicitement
activé. Un retour arrière de protocole/topologie demande une autre migration
planifiée ; il ne faut pas rétablir aveuglément les anciennes images.
