# Versions communes du client et des serveurs

Une release Unity possède une version de build commune au client Windows et au
dedicated Linux, de la forme `0.1.<numéro du run Unity CI>`. Le commit source
complet et l'identifiant du run complètent cette identité. Relancer une tentative
du même run conserve la version ; un nouveau run reçoit une nouvelle version.
La version de build est distincte de `ProtocolVersion` / `BuildId`, qui décrivent
la compatibilité du contrat réseau.

## Construction et publication

La CI fournit la même identité à chaque cible. Le build l'intègre dans
`Application.version` et dans les métadonnées du player. Les rapports de build,
les artefacts et la promotion conservent aussi le commit et le run source.
Le réglage de version du projet et les ressources temporaires sont restaurés
après le build ; le poste de travail ne reçoit pas une version de release par
simple ouverture de l'Editor.

La promotion exige une paire client/dedicated vérifiée avec les mêmes version,
commit et run. Un composant absent ou divergent bloque la publication de la
release complète. Les images restent épinglées par digest : une version lisible
ne remplace pas cette référence immuable ni les contrôles de protocole existants.

Publier les artefacts ne met pas automatiquement à jour tous les realms déjà
déployés. Leur image est figée dans le profil enregistré ; la mise à jour exige
toujours une [release approuvée](REALM_DEPLOYMENT.md#mettre-à-jour-une-image-par-gitops)
ou une [migration de runtime](REALM_RUNTIME_RELEASES.md) si le protocole ou le
plan change. Le client distribué doit correspondre au runtime retenu.

## Versions visibles dans l'admin

Déployer d'abord la nouvelle API Realms et l'Admin, puis les nouveaux dedicateds.
L'ancienne API refuse les propriétés JSON inconnues du heartbeat ; elle ne peut
donc pas recevoir ces nouveaux champs. La nouvelle API accepte les anciens
serveurs sans métadonnées. Cette évolution ne change pas le protocole de jeu.

Dans la fiche d'un realm, les sections **Overview** et **Maintenance** affichent
les versions annoncées par ses dedicateds : version du jeu, commit source,
run de construction et dernier contact. L'image configurée reste consultable
séparément dans les détails de déploiement.

Ces informations viennent du heartbeat HTTPS authentifié du processus dédié,
pas d'une déduction à partir d'un nom d'image ou d'un état Kubernetes `Ready`.
Le service Realms conserve la dernière annonce et l'expose par la route privée
`GET /admin/realms/{realmId}/server-versions`. Les anciennes images qui ne
transmettent pas ces champs restent compatibles et affichent une version
inconnue. Les champs sont facultatifs, bornés et ne contiennent aucun secret.

Une annonce ancienne reste visible comme dernière version connue, avec son
horodatage et un état sans contact récent. Elle ne prouve pas la version du
processus actuellement lancé après un redémarrage. Une panne de cette lecture
est affichée séparément et ne masque pas la fiche du realm.

La promotion commune évite de publier accidentellement deux builds issus de
sources différentes. Elle n'ajoute pas de nouvelle règle d'admission imposant
l'égalité stricte des versions de build ; la compatibilité réseau continue de
reposer sur les contrats explicites existants.

## Vérification du 24 octobre 2026

- Realms : 448 tests réussis ; six tests MongoDB optionnels ignorés, dont la
  projection réelle des versions, faute de `ARKYA_TEST_MONGO`. Le round-trip BSON
  et les contrats HTTP ancien/nouveau sont vérifiés sans MongoDB.
- Admin : 307 contrôles de realms réussis, puis suite complète de la copie de
  travail avec 925 réussites et un test MongoDB ignoré. Un échec TLS intermittent
  lors du premier passage n'est pas réapparu dans les quatre tests TLS ciblés ni
  dans cette seconde suite complète.
- CI : 88 tests Python réussis sur les sources qui seront committées, y compris
  le refus de versions client/dedicated divergentes et d'archives remplacées.
- Unity : compilation dans l'Editor, test dédié/client WebSocket avec heartbeat
  versionné réussi et contrôle en mémoire du stamping/restauration de la version,
  de la ressource temporaire et de son `.meta`. Le projet revient à `0.1.0`.
- Rendu Admin inspecté dans Edge à 1 440 et 390 pixels ; aucun débordement de
  page, tableau défilable sur mobile, états divergent/ancien/inconnu visibles.

Les nouveaux players Windows/Linux complets et leur déploiement sur un realm
réel restent à vérifier dans la CI et lors de l'adoption de la release.
