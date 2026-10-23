# Sessions administrateur

`Arkya.Admin` conserve les sessions dans sa propre base MongoDB `arkya_admin`,
collection `admin_sessions`, schéma 1. Le déploiement doit fournir
`Mongo__ConnectionString` avec les droits nécessaires à cette base et conserver
les clés ASP.NET Data Protection utilisées pour les cookies d'accès.
Le démarrage vérifie la disponibilité de MongoDB et crée l'index TTL.
Une panne de stockage refuse l'accès avec HTTP 503 ; aucun stockage mémoire
n'est utilisé en production.

La connexion Discord autorisée ou le mot de passe opérateur créent deux cookies :

- `Arkya.Admin`, ticket protégé ASP.NET de 30 minutes ;
- `Arkya.Admin.Refresh`, identifiant de session et secret aléatoire de 256 bits,
  persistant pendant 30 jours, `HttpOnly`, `Secure` en production,
  `SameSite=Strict`, sans domaine partagé.

Le navigateur ne reçoit aucun jeton Discord. Les empreintes SHA-256 des secrets
de renouvellement restent côté serveur. Le cookie d'accès contient uniquement
les claims d'identité, le rôle et l'identifiant de session.

À la prochaine requête après la moitié de la durée d'accès, ou si ce cookie a
expiré, le serveur renouvelle automatiquement l'accès et remplace atomiquement
le secret de renouvellement. La date finale de la session ne glisse jamais.
Après 30 jours, une nouvelle connexion est nécessaire, même si l'interface a été
utilisée continuellement. `AdminSessions__AccessLifetime` accepte 5 minutes à
2 heures ; `AdminSessions__RefreshLifetime` accepte une durée supérieure à celle
d'accès et limitée à 90 jours.

Les requêtes concurrentes utilisant le secret immédiatement précédent disposent
de 10 secondes pour terminer sans nouvelle émission de cookies. Au-delà, le rejeu
d'un secret déjà consommé révoque toute la session, y compris les tickets d'accès
encore valides. Une nouvelle rotation n'a pas lieu dans les 10 secondes suivant
la précédente. Les consultations de fichiers statiques, `/health` et `/mcp`
ne renouvellent pas les sessions navigateur.
L'historique est limité atomiquement à 65 536 rotations par session afin de
borner le document MongoDB, même si un client supprime continuellement son cookie
d'accès ; atteindre ce plafond impose une reconnexion. Ce plafond permet aussi
l'usage continu sur 90 jours avec la durée d'accès minimale de 5 minutes.

Chaque requête authentifiée vérifie l'existence et l'expiration de la session.
L'allowlist Discord est réévaluée lors de l'accès et du renouvellement ; le
changement du mot de passe opérateur invalide les sessions de ce mode. La
déconnexion conserve son POST antiforgery et révoque la session persistée avant
de supprimer les cookies. La connexion et les autres mutations gardent leur
protection antiforgery.

Les anciens cookies d'accès sans session persistée nécessitent une reconnexion
après cette mise à jour. Les secrets de renouvellement ne sont jamais placés dans
une URL, dans le HTML ou dans les appels vers les API métier.

Vérification : `dotnet test backend/tests/Arkya.Admin.Tests/Arkya.Admin.Tests.csproj`.
Le test MongoDB utilise une base jetable préfixée `arkya_admin_test_` si
`ARKYA_TEST_MONGO` est défini ; autrement, il est explicitement ignoré. Les tests
HTTP couvrent aussi la reprise avec le seul cookie refresh, la rotation,
l'expiration absolue, les requêtes concurrentes, le rejeu, la révocation et les
changements d'autorisation.
