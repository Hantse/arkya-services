# 0002 — Serveur dédié et premiers services backend

## Statut

Acceptée pour la première tranche Identity et Characters.

## Contexte

Arkya est un MMORPG avec une simulation autoritaire Unity. Le serveur dédié doit communiquer avec des services ASP.NET Core pour les comptes et les données persistantes. Deux responsabilités sont nécessaires dès le début : identifier le joueur via Discord, puis gérer les personnages associés à son compte.

L'ancien `Arkya.Backend.Api` fournit déjà un parcours Unity de développement avec connexion locale et annuaire de serveurs mock. Il reste disponible pendant l'introduction des services réels.

## Décision

- La simulation temps réel est exécutée par un serveur dédié Unity avec Netcode for Entities. Les services ASP.NET Core restent hors de la boucle de simulation.
- Créer deux applications .NET 10 indépendantes : `Arkya.Identity.Api` et `Arkya.Characters.Api`.
- Identity possède les comptes Arkya, le lien avec Discord et les sessions. Discord est le fournisseur initial via OAuth2 Authorization Code, scope `identify`, avec secret conservé côté serveur.
- Un challenge Arkya à durée courte et une preuve S256 permettent au client natif de récupérer sa session après une autorisation dans le navigateur. La corrélation OAuth du navigateur utilise un état et un cookie à usage unique.
- Characters possède les personnages, référencés par un identifiant de compte Arkya. Les accès publics et la résolution interne contrôlent systématiquement la propriété.
- Chaque service possède une base MongoDB distincte, des documents versionnés et ses propres index. Il n'accède pas aux collections de l'autre service.
- Commencer par des contrats HTTP JSON sur HTTPS. gRPC unary reste une option pour les échanges internes dont les mesures justifieront le coût d'intégration. Un stream attend un besoin explicite d'événements continus.
- Les sessions sont opaques, aléatoires, révocables et persistées sous forme d'empreinte. Characters les valide auprès d'Identity via une route interne authentifiée.
- Conserver l'ancien parcours local et limiter la connexion de développement à l'environnement `Development`.

## Conséquences

Les propriétaires des données et les frontières réseau sont explicites dès le départ. La création d'un personnage n'a pas besoin d'une jointure MongoDB entre services. L'introspection rend la révocation visible à l'appel suivant et introduit une dépendance à la disponibilité d'Identity ; les appels restent bornés dans le temps.

HTTP JSON permet de vérifier le premier parcours et ses erreurs avant d'optimiser un transport. La décision en faveur de gRPC nécessitera des mesures sur le serveur Unity réel et un client compatible avec le build ciblé. Aucun appel de persistance par tick de simulation n'est prévu.

Cette tranche ne définit pas encore les tickets d'admission, la réservation des personnages, la prévention des connexions simultanées, ni les écritures d'inventaire ou de progression. La route de résolution interne vérifie une identité et un propriétaire ; elle ne vaut pas admission.

Le parcours Unity existant continue d'utiliser la connexion de développement. La bascule vers Discord et le serveur dédié est une prochaine intégration. Une validation réelle de Discord nécessite de créer et configurer une application fournisseur hors du dépôt.

## Références

- [OAuth2 Discord : authorization code, scope identify et state](https://docs.discord.com/developers/topics/oauth2).
- [Microsoft : comparaison gRPC et API HTTP JSON](https://learn.microsoft.com/en-us/aspnet/core/grpc/comparison?view=aspnetcore-10.0).
- [Architecture Arkya](../ARCHITECTURE.md).
- [Configuration et contrats backend](../../backend/README.md).
