# Familiers totems

Direction de gameplay confirmée le 16 octobre 2026 : **chaque joueur se lie dès le départ à l'âme d'un animal**, son familier « totem ». Les deux premiers animaux sont le **loup** et le **renard**. D'autres espèces pourront être définies ensuite.

## Apparence demandée

Les deux animaux suivent la direction artistique peinte d'Arkya : volumes modelés, silhouettes lisibles, transitions de couleur et ombres colorées. Leur caractère mystique vient du regard et de flux de magie qui parcourent le pelage.

La demande comprend à la fois **douze variantes de pelage par animal** et une **palette d'environ dix à douze teintes par variante**. Les nuances peintes et l'éclairage peuvent produire des couleurs intermédiaires ; ce nombre décrit les teintes de construction de l'apparence.

La [collection de modèles](art-direction/resources/totems/README.md) réunit les sources éditables, les modèles riggés, les variantes et l'atelier de revue. Cette première livraison artistique reste à valider par l'utilisateur.

La passe de finition graphique du 19 octobre 2026 conserve le loup V10, le
renard V2 et leurs 24 pelages. Dans Unity, leur shader peint utilise désormais
l'occlusion ambiante du renderer pour mieux séparer les pattes, le ventre et
les contacts avec le décor. Cet assombrissement concerne la lumière reçue et
préserve l'émission des marques d'âme. Les sources, les poids et les clips ne
sont pas régénérés par cette passe.

## Périmètre de cette étape

Cette étape prépare les deux modèles, leur squelette, leurs pelages et leur présentation animée. Le lien à l'âme est enregistré ici comme une règle de conception ; le rituel de choix, la sauvegarde du totem et son apparition dans le monde restent à implémenter.

Les capacités des totems, leur rôle en combat, leur progression, leur éventuelle mortalité et la possibilité de changer d'animal ne sont pas encore décidés. Aucun bonus n'est déduit d'une espèce ou d'une couleur.

## Frontières de la future intégration

Le serveur devra valider le choix et posséder les décisions de gameplay du familier. La persistance de l'association personnage–totem appartient au service propriétaire du personnage. Les modèles, animations et flux du pelage restent de la présentation ; ils observent l'état reçu sans créer d'état autoritaire dans un MonoBehaviour.

Les identifiants `wolf` et `fox` ainsi que les identifiants de pelage du manifeste sont ceux de la collection artistique. Leur adoption dans un contrat réseau ou persistant demandera un catalogue versionné, une validation serveur et une règle pour les personnages existants.
