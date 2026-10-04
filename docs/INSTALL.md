# Mars Horizon × Archipelago — installation

Prérequis : Mars Horizon (Steam) **version 1.4.2.1**, Archipelago 0.6.4 ou plus récent.

## 1. Installer BepInEx 5 (une seule fois)
1. Télécharge **BepInEx_win_x64_5.4.23.x** (https://github.com/BepInEx/BepInEx/releases, version 5, pas 6).
2. Décompresse-le dans le dossier du jeu (à côté de `Mars Horizon.exe`).
3. Lance le jeu une fois puis quitte : le dossier `BepInEx/` se remplit.

## 2. Installer le plugin
Décompresse `MarsHorizonAP-x.y.z.zip` dans le dossier du jeu : il ajoute `BepInEx/plugins/MarsHorizonAP/`.

## 3. Installer l'APWorld (pour générer une partie)
Copie `mars_horizon.apworld` dans le dossier `custom_worlds` d'Archipelago (lanceur → *Open Folder* → *custom_worlds*).
Écris ton YAML en partant de `MarsHorizon.yaml` (page d'options du jeu dans le lanceur : *Options Creator*).

## 4. Jouer
1. Lance le jeu. Appuie sur **F8** : renseigne le serveur (`archipelago.gg:38281`), ton **slot** et le mot de passe, puis *Connecter*.
   (Ces valeurs sont aussi dans `BepInEx/config/archipelago.marshorizon.cfg` ; `AutoConnect = true` reconnecte au lancement.)
2. **Une fois connecté**, lance une **nouvelle partie** : scénario par défaut, **la même agence que dans ton YAML**.
   Désactive le tutoriel. C'est au démarrage de la partie qu'elle est liée au multiworld.
3. Joue normalement. Les recherches, premières constructions et jalons envoient des checks ; les items reçus
   s'affichent en haut à droite de l'écran.

## Comment ça marche en jeu
- **Rechercher un nœud** envoie un check et fait avancer l'arbre, mais ne débloque **pas** son contenu. Le contenu
  (fusée, payload, bâtiment, mission) arrive avec l'item correspondant, quand quelqu'un te l'envoie.
- Un bâtiment dont tu n'as pas reçu l'item reste non constructible ; une mission dont il manque un élément
  affiche la recherche manquante comme à l'accoutumée.
- Le jeu sauvegarde l'état Archipelago dans ta sauvegarde (rien à gérer à côté). Tu peux jouer hors ligne :
  les checks sont renvoyés à la reconnexion et aucun item n'est donné deux fois.
- Objectif par défaut : réussir la mission finale vers Mars.

## Dépannage
- Pas de fenêtre avec F8 : vérifie que `BepInEx/LogOutput.log` contient `MarsHorizonAP 0.1.0`.
- « Cette sauvegarde appartient à un autre multiworld » : tu as chargé une partie liée à un autre slot.
- Mauvaise agence : la partie n'est pas liée (un message l'indique) ; relance une partie avec l'agence du slot.
- Mise à jour du jeu : le plugin cible la version 1.4.2.1 et le signale dans le log si elle diffère.
