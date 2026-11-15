# gen_advanced.py - writes src\AdvancedText.cs: the launcher's "Advanced settings" rows (ffx_enh.ini) with labels and help
# texts in the eight languages of the games (us de fr it sp jp kr ch). The C# file keeps the source ASCII (\u escapes).
#   python gen_advanced.py > src\AdvancedText.cs
import sys
L = ['us', 'de', 'fr', 'it', 'sp', 'jp', 'kr', 'ch']
ONOFF = ['1', '0']                                    # values of a switch, shown with the game's own On / Off texts

def T(us, de, fr, it, sp, jp, kr, ch): return [us, de, fr, it, sp, jp, kr, ch]

KEYS = [('96', 'Num 0'), ('97', 'Num 1'), ('98', 'Num 2'), ('99', 'Num 3'), ('100', 'Num 4'), ('101', 'Num 5'), ('102', 'Num 6'),
        ('103', 'Num 7'), ('104', 'Num 8'), ('105', 'Num 9'), ('112', 'F1'), ('113', 'F2'), ('114', 'F3'), ('115', 'F4'), ('116', 'F5'),
        ('117', 'F6'), ('119', 'F8'), ('120', 'F9'), ('121', 'F10'), ('122', 'F11'), ('123', 'F12'), ('16', 'Shift'), ('17', 'Ctrl'),
        ('32', 'Space'), ('9', 'Tab')]
SPEED = [(str(v), '%dx' % v) for v in (2, 3, 4, 5, 6, 8, 10, 15, 20, 30)]
CHORDS = [(c, c) for c in ('L1+L2+R1+R2+START+SELECT', 'L1+R1+START+SELECT', 'L2+R2+START+SELECT', 'L3+R3', 'START+SELECT')]

H = 'header'
ITEMS = []
def header(label): ITEMS.append(dict(kind=H, label=label))
def opt(sec, key, default, label, help, values=None, texts=None):
    ITEMS.append(dict(kind='opt', sec=sec, key=key, default=default, label=label, help=help, values=values or ONOFF, texts=texts))

# ------------------------------------------------------------------ general
header(T('Advanced settings (60 fps patch, from the next game start)',
         'Erweiterte Einstellungen (60-fps-Patch, ab dem n\xe4chsten Spielstart)',
         'Param\xe8tres avanc\xe9s (patch 60 fps, au prochain lancement)',
         'Impostazioni avanzate (patch 60 fps, dal prossimo avvio)',
         'Ajustes avanzados (parche de 60 fps, desde el pr\xf3ximo inicio)',
         '\u8a73\u7d30\u8a2d\u5b9a\uff0860 fps\u30d1\u30c3\u30c1\u3001\u6b21\u56de\u306e\u8d77\u52d5\u304b\u3089\uff09',
         '\uace0\uae09 \uc124\uc815 (60 fps \ud328\uce58, \ub2e4\uc74c \uc2e4\ud589\ubd80\ud130)',
         '\u9032\u968e\u8a2d\u5b9a\uff0860 fps\u4fee\u6b63\u7a0b\u5f0f\uff0c\u4e0b\u6b21\u555f\u52d5\u6642\u751f\u6548\uff09'))

opt('fps60', 'pacing', 'optimized',
    T('Frame pacing', 'Bildtaktung', 'Cadencement des images', 'Cadenza dei fotogrammi', 'Ritmo de fotogramas',
      '\u30d5\u30ec\u30fc\u30e0\u30da\u30fc\u30b7\u30f3\u30b0', '\ud504\ub808\uc784 \ud398\uc774\uc2f1', '\u756b\u683c\u7bc0\u594f'),
    T('Optimized: if the PC cannot hold 60 fps, single frames are left out and the game keeps its normal speed; above 60 fps the rate is lowered to what the PC holds, never below 60. Original: the pacing of the original mod - at 60 fps a slow PC falls back to 30 fps for 20 seconds or longer.',
      'Optimiert: Schafft der PC keine 60 fps, werden einzelne Bilder ausgelassen und das Spiel beh\xe4lt sein normales Tempo; \xfcber 60 fps wird die Rate auf das gesenkt, was der PC schafft, nie unter 60. Original: die Taktung des Original-Mods - bei 60 fps f\xe4llt ein langsamer PC f\xfcr 20 Sekunden oder l\xe4nger auf 30 fps zur\xfcck.',
      'Optimis\xe9 : si le PC ne tient pas 60 fps, des images isol\xe9es sont omises et le jeu garde sa vitesse normale ; au-dessus de 60 fps, la cadence baisse au niveau que le PC tient, jamais sous 60. Original : le cadencement du mod d\'origine - \xe0 60 fps, un PC lent repasse \xe0 30 fps pendant 20 secondes ou plus.',
      'Ottimizzato: se il PC non regge 60 fps, singoli fotogrammi vengono saltati e il gioco mantiene la velocit\xe0 normale; sopra 60 fps la frequenza scende a quanto il PC regge, mai sotto 60. Originale: la cadenza della mod originale - a 60 fps un PC lento torna a 30 fps per 20 secondi o pi\xf9.',
      'Optimizado: si el PC no mantiene 60 fps, se omiten fotogramas sueltos y el juego conserva su velocidad normal; por encima de 60 fps la tasa baja a lo que el PC mantiene, nunca por debajo de 60. Original: el ritmo del mod original - a 60 fps un PC lento vuelve a 30 fps durante 20 segundos o m\xe1s.',
      '\u6700\u9069\u5316\uff1aPC\u304c60 fps\u3092\u7dad\u6301\u3067\u304d\u306a\u3044\u3068\u304d\u306f\u4e00\u90e8\u306e\u30d5\u30ec\u30fc\u30e0\u3092\u7701\u304d\u3001\u30b2\u30fc\u30e0\u306f\u901a\u5e38\u306e\u901f\u5ea6\u3067\u9032\u307f\u307e\u3059\u300260 fps\u3092\u8d85\u3048\u308b\u3068\u304d\u306fPC\u304c\u7dad\u6301\u3067\u304d\u308b\u5024\u307e\u3067\u4e0b\u3052\u307e\u3059\uff0860\u672a\u6e80\u306b\u306f\u306a\u308a\u307e\u305b\u3093\uff09\u3002\u30aa\u30ea\u30b8\u30ca\u30eb\uff1a\u5143\u306eMOD\u306e\u52d5\u4f5c\u300260 fps\u3067\u51e6\u7406\u304c\u9045\u3044\u3068\u300120\u79d2\u4ee5\u4e0a30 fps\u306b\u623b\u308a\u307e\u3059\u3002',
      '\ucd5c\uc801\ud654: PC\uac00 60 fps\ub97c \uc720\uc9c0\ud558\uc9c0 \ubabb\ud558\uba74 \uc77c\ubd80 \ud504\ub808\uc784\uc744 \uac74\ub108\ub6f0\uace0 \uac8c\uc784\uc740 \uc815\uc0c1 \uc18d\ub3c4\ub97c \uc720\uc9c0\ud569\ub2c8\ub2e4. 60 fps \ucd08\uacfc \uc2dc\uc5d0\ub294 PC\uac00 \uc720\uc9c0\ud560 \uc218 \uc788\ub294 \uac12\uae4c\uc9c0 \ub0ae\ucd94\uba70 60 \ubbf8\ub9cc\uc73c\ub85c\ub294 \ub0b4\ub9ac\uc9c0 \uc54a\uc2b5\ub2c8\ub2e4. \uc6d0\ubcf8: \uc6d0\ub798 \ubaa8\ub4dc\uc758 \ubc29\uc2dd - 60 fps\uc5d0\uc11c \ub290\ub9b0 PC\ub294 20\ucd08 \uc774\uc0c1 30 fps\ub85c \ub3cc\uc544\uac11\ub2c8\ub2e4.',
      '\u6700\u4f73\u5316\uff1a\u96fb\u8166\u7121\u6cd5\u7dad\u6301 60 fps \u6642\u6703\u7565\u904e\u500b\u5225\u756b\u683c\uff0c\u904a\u6232\u4fdd\u6301\u6b63\u5e38\u901f\u5ea6\uff1b\u8d85\u904e 60 fps \u6642\u6703\u964d\u81f3\u96fb\u8166\u80fd\u7dad\u6301\u7684\u6578\u503c\uff0c\u4f46\u4e0d\u4f4e\u65bc 60\u3002\u539f\u59cb\uff1a\u539f\u7248\u6a21\u7d44\u7684\u7bc0\u594f\uff0c60 fps \u6642\u8f03\u6162\u7684\u96fb\u8166\u6703\u56de\u5230 30 fps \u9054 20 \u79d2\u4ee5\u4e0a\u3002'),
    ['optimized', 'original'],
    [T('Optimized', 'Optimiert', 'Optimis\xe9', 'Ottimizzato', 'Optimizado', '\u6700\u9069\u5316', '\ucd5c\uc801\ud654', '\u6700\u4f73\u5316'),
     T('Original', 'Original', 'Original', 'Originale', 'Original', '\u30aa\u30ea\u30b8\u30ca\u30eb', '\uc6d0\ubcf8', '\u539f\u59cb')])

opt('fps60', 'menu', '1',
    T('Frame rate in the game\'s menu', 'Framerate im Spielmen\xfc', 'Fr\xe9quence dans le menu du jeu', 'Frequenza nel menu del gioco', 'Fotogramas en el men\xfa del juego',
      '\u30b2\u30fc\u30e0\u5185\u30e1\u30cb\u30e5\u30fc\u306e\u30d5\u30ec\u30fc\u30e0\u30ec\u30fc\u30c8', '\uac8c\uc784 \uba54\ub274\uc758 \ud504\ub808\uc784 \ub808\uc774\ud2b8', '\u904a\u6232\u9078\u55ae\u4e2d\u7684 FPS'),
    T('Shows the row "Frame Rate" in the game\'s own option menu (Esc key, page Video), between V-Sync and Texture Quality, so that the frame rate can be changed during play. Off: the game\'s menu stays exactly as shipped.',
      'Zeigt die Zeile \u201eFramerate\u201c im Optionsmen\xfc des Spiels (Esc-Taste, Seite Video) zwischen VSync und Texturqualit\xe4t, damit die Bildrate w\xe4hrend des Spiels ge\xe4ndert werden kann. Aus: Das Men\xfc des Spiels bleibt genau wie im Original.',
      'Affiche la ligne \xab Fr\xe9quence d\'images \xbb dans le menu d\'options du jeu (touche \xc9chap, page Vid\xe9o), entre Vsync et Qualit\xe9 des textures, pour changer la fr\xe9quence en jouant. D\xe9sactiv\xe9 : le menu du jeu reste tel quel.',
      'Mostra la riga "Frequenza fotogrammi" nel menu opzioni del gioco (tasto Esc, pagina Video), tra Sincronizzazione verticale e Texture, per cambiare la frequenza durante il gioco. Disattivato: il menu del gioco resta come l\'originale.',
      'Muestra la fila "Fotogramas por segundo" en el men\xfa de opciones del juego (tecla Esc, p\xe1gina V\xeddeo), entre Sincronizaci\xf3n vertical y Calidad de las texturas, para cambiarla mientras juegas. Desactivado: el men\xfa del juego queda como el original.',
      '\u30b2\u30fc\u30e0\u306e\u30aa\u30d7\u30b7\u30e7\u30f3\u30e1\u30cb\u30e5\u30fc\uff08Esc\u30ad\u30fc\u3001\u30d3\u30c7\u30aa\uff09\u306e\u5782\u76f4\u540c\u671f\u3068\u30c6\u30af\u30b9\u30c1\u30e3\u30fc\u306e\u30af\u30aa\u30ea\u30c6\u30a3\u30fc\u306e\u9593\u306b\u300c\u30d5\u30ec\u30fc\u30e0\u30ec\u30fc\u30c8\u300d\u3092\u8868\u793a\u3057\u3001\u30d7\u30ec\u30a4\u4e2d\u306b\u5909\u66f4\u3067\u304d\u308b\u3088\u3046\u306b\u3057\u307e\u3059\u3002\u30aa\u30d5\uff1a\u30e1\u30cb\u30e5\u30fc\u306f\u5143\u306e\u307e\u307e\u3067\u3059\u3002',
      '\uac8c\uc784 \uc635\uc158 \uba54\ub274(Esc \ud0a4, \ube44\ub514\uc624 \ud398\uc774\uc9c0)\uc758 Vsync\uc640 \ud14d\uc2a4\ucc98 \ud488\uc9c8 \uc0ac\uc774\uc5d0 "\ud504\ub808\uc784 \ub808\uc774\ud2b8" \ud56d\ubaa9\uc744 \ud45c\uc2dc\ud558\uc5ec \uac8c\uc784 \uc911\uc5d0 \ubcc0\uacbd\ud560 \uc218 \uc788\uac8c \ud569\ub2c8\ub2e4. \ub044\uae30: \uac8c\uc784 \uba54\ub274\ub294 \uc6d0\ub798 \uadf8\ub300\ub85c\uc785\ub2c8\ub2e4.',
      '\u5728\u904a\u6232\u7684\u9078\u9805\u9078\u55ae\uff08Esc \u9375\uff0c\u5f71\u50cf\u9801\u9762\uff09\u7684\u5782\u76f4\u540c\u6b65\u8207\u7d0b\u7406\u54c1\u8cea\u4e4b\u9593\u986f\u793a\u300cFPS\u300d\uff0c\u53ef\u5728\u904a\u6232\u4e2d\u8b8a\u66f4\u3002\u95dc\u9589\uff1a\u904a\u6232\u9078\u55ae\u7dad\u6301\u539f\u6a23\u3002'))

opt('fps60', 'soft_reset', 'L1+L2+R1+R2+START+SELECT',
    T('Soft reset buttons', 'Soft-Reset-Tasten', 'Touches de r\xe9initialisation', 'Tasti di reset rapido', 'Botones de reinicio r\xe1pido',
      '\u30bd\u30d5\u30c8\u30ea\u30bb\u30c3\u30c8\u306e\u30dc\u30bf\u30f3', '\uc18c\ud504\ud2b8 \ub9ac\uc14b \ubc84\ud2bc', '\u8edf\u91cd\u7f6e\u6309\u9215'),
    T('The controller buttons that have to be held together for a soft reset to the title screen. Used only when "Soft reset" is switched on for the game below.',
      'Die Controller-Tasten, die f\xfcr einen Soft-Reset zum Titelbildschirm gleichzeitig gehalten werden m\xfcssen. Wirkt nur, wenn unten beim jeweiligen Spiel \u201eSoft-Reset\u201c eingeschaltet ist.',
      'Les boutons de la manette \xe0 maintenir ensemble pour revenir \xe0 l\'\xe9cran titre. Utilis\xe9s seulement si \xab R\xe9initialisation \xbb est activ\xe9e plus bas pour le jeu.',
      'I tasti del controller da tenere premuti insieme per tornare alla schermata del titolo. Usati solo se "Reset rapido" \xe8 attivo pi\xf9 sotto per il gioco.',
      'Los botones del mando que hay que mantener pulsados a la vez para volver a la pantalla de t\xedtulo. Solo se usan si "Reinicio r\xe1pido" est\xe1 activado m\xe1s abajo para el juego.',
      '\u540c\u6642\u306b\u62bc\u3057\u7d9a\u3051\u308b\u3068\u30bf\u30a4\u30c8\u30eb\u753b\u9762\u306b\u623b\u308b\u30b3\u30f3\u30c8\u30ed\u30fc\u30e9\u30fc\u306e\u30dc\u30bf\u30f3\u3067\u3059\u3002\u4e0b\u306e\u5404\u30b2\u30fc\u30e0\u306e\u300c\u30bd\u30d5\u30c8\u30ea\u30bb\u30c3\u30c8\u300d\u304c\u30aa\u30f3\u306e\u3068\u304d\u3060\u3051\u4f7f\u308f\u308c\u307e\u3059\u3002',
      '\ud0c0\uc774\ud2c0 \ud654\uba74\uc73c\ub85c \ub3cc\uc544\uac00\ub824\uba74 \ub3d9\uc2dc\uc5d0 \ub204\ub974\uace0 \uc788\uc5b4\uc57c \ud558\ub294 \ucee8\ud2b8\ub864\ub7ec \ubc84\ud2bc\uc785\ub2c8\ub2e4. \uc544\ub798\uc5d0\uc11c \ud574\ub2f9 \uac8c\uc784\uc758 "\uc18c\ud504\ud2b8 \ub9ac\uc14b"\uc774 \ucf1c\uc838 \uc788\uc744 \ub54c\ub9cc \uc0ac\uc6a9\ub429\ub2c8\ub2e4.',
      '\u540c\u6642\u6309\u4f4f\u5373\u53ef\u8fd4\u56de\u6a19\u984c\u756b\u9762\u7684\u63a7\u5236\u5668\u6309\u9215\u3002\u53ea\u6709\u5728\u4e0b\u65b9\u5c0d\u61c9\u904a\u6232\u7684\u300c\u8edf\u91cd\u7f6e\u300d\u958b\u555f\u6642\u624d\u6703\u4f7f\u7528\u3002'),
    [c[0] for c in CHORDS], [[c[1] for c in CHORDS]] * 8)

opt('fps60', 'hotkeys', '0',
    T('Keyboard keys of the patch', 'Tastaturtasten des Patches', 'Touches clavier du patch', 'Tasti della tastiera della patch', 'Teclas del parche',
      '\u30d1\u30c3\u30c1\u306e\u30ad\u30fc\u30dc\u30fc\u30c9\u64cd\u4f5c', '\ud328\uce58 \ud0a4\ubcf4\ub4dc \ud0a4', '\u4fee\u6b63\u7a0b\u5f0f\u9375\u76e4\u6309\u9375'),
    T('Lets the patch read its keyboard keys while the game runs: F7 switches smooth motion off and on, and the fast-forward key (Turbo) works. Off: the patch reads no key at all, so nothing can be switched by accident.',
      'Erlaubt dem Patch, w\xe4hrend des Spiels seine Tastaturtasten zu lesen: F7 schaltet die fl\xfcssige Bewegung aus und ein, und die Vorspultaste (Turbo) funktioniert. Aus: Der Patch liest keine Taste, es kann also nichts versehentlich umgeschaltet werden.',
      'Le patch lit ses touches pendant le jeu : F7 active ou d\xe9sactive le mouvement fluide, et la touche d\'avance rapide (Turbo) fonctionne. D\xe9sactiv\xe9 : le patch ne lit aucune touche, rien ne peut \xeatre chang\xe9 par erreur.',
      'La patch legge i suoi tasti durante il gioco: F7 attiva e disattiva il movimento fluido e il tasto di avanzamento rapido (Turbo) funziona. Disattivato: la patch non legge alcun tasto, quindi niente pu\xf2 cambiare per errore.',
      'El parche lee sus teclas durante el juego: F7 activa y desactiva el movimiento fluido y la tecla de avance r\xe1pido (Turbo) funciona. Desactivado: el parche no lee ninguna tecla, as\xed nada se cambia por error.',
      '\u30d7\u30ec\u30a4\u4e2d\u306b\u30d1\u30c3\u30c1\u304c\u30ad\u30fc\u3092\u8aad\u307f\u53d6\u308a\u307e\u3059\u3002F7\u3067\u6ed1\u3089\u304b\u306a\u52d5\u304d\u3092\u30aa\u30f3/\u30aa\u30d5\u3057\u3001\u65e9\u9001\u308a\u30ad\u30fc\uff08\u30bf\u30fc\u30dc\uff09\u304c\u4f7f\u3048\u307e\u3059\u3002\u30aa\u30d5\uff1a\u30ad\u30fc\u3092\u4e00\u5207\u8aad\u307e\u306a\u3044\u306e\u3067\u3001\u8aa4\u3063\u3066\u5207\u308a\u66ff\u308f\u308b\u3053\u3068\u306f\u3042\u308a\u307e\u305b\u3093\u3002',
      '\uac8c\uc784 \uc911\uc5d0 \ud328\uce58\uac00 \ud0a4\ub97c \uc77d\uc2b5\ub2c8\ub2e4. F7\uc740 \ubd80\ub4dc\ub7ec\uc6b4 \uc6c0\uc9c1\uc784\uc744 \ucf1c\uace0 \ub044\uba70, \ube68\ub9ac \uac10\uae30 \ud0a4(\ud130\ubcf4)\uac00 \uc791\ub3d9\ud569\ub2c8\ub2e4. \ub044\uae30: \ud328\uce58\uac00 \uc5b4\ub5a4 \ud0a4\ub3c4 \uc77d\uc9c0 \uc54a\uc73c\ubbc0\ub85c \uc2e4\uc218\ub85c \ubc14\ub00c\uc9c0 \uc54a\uc2b5\ub2c8\ub2e4.',
      '\u904a\u6232\u4e2d\u7531\u4fee\u6b63\u7a0b\u5f0f\u8b80\u53d6\u6309\u9375\uff1aF7 \u958b\u95dc\u6d41\u66a2\u52d5\u4f5c\uff0c\u5feb\u8f49\u9375\uff08\u6e26\u8f2a\uff09\u53ef\u4f7f\u7528\u3002\u95dc\u9589\uff1a\u4e0d\u8b80\u53d6\u4efb\u4f55\u6309\u9375\uff0c\u4e0d\u6703\u8aa4\u89f8\u5207\u63db\u3002'))

opt('fps60', 'log', '1',
    T('Log file', 'Protokolldatei', 'Fichier journal', 'File di registro', 'Archivo de registro',
      '\u30ed\u30b0\u30d5\u30a1\u30a4\u30eb', '\ub85c\uadf8 \ud30c\uc77c', '\u8a18\u9304\u6a94'),
    T('Writes FFX_enh.log / FFX-2_enh.log into the game folder: what the patch found in the game and switched on, and every change of the frame rate. Helpful when something does not work.',
      'Schreibt FFX_enh.log / FFX-2_enh.log in den Spielordner: was der Patch im Spiel gefunden und eingeschaltet hat, und jeden Wechsel der Bildrate. Hilfreich, wenn etwas nicht funktioniert.',
      '\xc9crit FFX_enh.log / FFX-2_enh.log dans le dossier du jeu : ce que le patch a trouv\xe9 et activ\xe9, et chaque changement de fr\xe9quence. Utile quand quelque chose ne marche pas.',
      'Scrive FFX_enh.log / FFX-2_enh.log nella cartella del gioco: cosa la patch ha trovato e attivato e ogni cambio di frequenza. Utile quando qualcosa non funziona.',
      'Escribe FFX_enh.log / FFX-2_enh.log en la carpeta del juego: lo que el parche encontr\xf3 y activ\xf3 y cada cambio de fotogramas. \xdatil cuando algo no funciona.',
      '\u30b2\u30fc\u30e0\u30d5\u30a9\u30eb\u30c0\u30fc\u306bFFX_enh.log / FFX-2_enh.log\u3092\u66f8\u304d\u51fa\u3057\u307e\u3059\u3002\u30d1\u30c3\u30c1\u304c\u898b\u3064\u3051\u3066\u6709\u52b9\u306b\u3057\u305f\u3082\u306e\u3068\u3001\u30d5\u30ec\u30fc\u30e0\u30ec\u30fc\u30c8\u306e\u5909\u66f4\u3092\u8a18\u9332\u3057\u307e\u3059\u3002\u4e0d\u5177\u5408\u306e\u8abf\u67fb\u306b\u5f79\u7acb\u3061\u307e\u3059\u3002',
      '\uac8c\uc784 \ud3f4\ub354\uc5d0 FFX_enh.log / FFX-2_enh.log\ub97c \uae30\ub85d\ud569\ub2c8\ub2e4. \ud328\uce58\uac00 \ucc3e\uc544\uc11c \ucf20 \uae30\ub2a5\uacfc \ud504\ub808\uc784 \ub808\uc774\ud2b8 \ubcc0\uacbd\uc744 \uae30\ub85d\ud569\ub2c8\ub2e4. \ubb38\uc81c\uac00 \uc788\uc744 \ub54c \uc720\uc6a9\ud569\ub2c8\ub2e4.',
      '\u5728\u904a\u6232\u8cc7\u6599\u593e\u5beb\u5165 FFX_enh.log / FFX-2_enh.log\uff1a\u8a18\u9304\u4fee\u6b63\u7a0b\u5f0f\u627e\u5230\u4e26\u555f\u7528\u7684\u5167\u5bb9\u53ca\u6bcf\u6b21 FPS \u8b8a\u66f4\u3002\u767c\u751f\u554f\u984c\u6642\u5f88\u6709\u5e6b\u52a9\u3002'))

opt('fps60', 'verbose', '0',
    T('Detailed log', 'Ausf\xfchrliches Protokoll', 'Journal d\xe9taill\xe9', 'Registro dettagliato', 'Registro detallado',
      '\u8a73\u7d30\u30ed\u30b0', '\uc0c1\uc138 \ub85c\uadf8', '\u8a73\u7d30\u8a18\u9304'),
    T('Adds a line with the frame rate every 5 seconds and the periodic reports of the patch to the log file. Only for troubleshooting: the file grows quickly.',
      'Schreibt zus\xe4tzlich alle 5 Sekunden eine Zeile mit der Bildrate und die regelm\xe4\xdfigen Berichte des Patches in die Protokolldatei. Nur zur Fehlersuche: Die Datei w\xe4chst schnell.',
      'Ajoute au journal une ligne de fr\xe9quence toutes les 5 secondes et les rapports p\xe9riodiques du patch. Seulement pour le d\xe9pannage : le fichier grossit vite.',
      'Aggiunge al registro una riga con la frequenza ogni 5 secondi e i rapporti periodici della patch. Solo per la ricerca di problemi: il file cresce in fretta.',
      'A\xf1ade al registro una l\xednea con los fotogramas cada 5 segundos y los informes peri\xf3dicos del parche. Solo para buscar fallos: el archivo crece r\xe1pido.',
      '5\u79d2\u3054\u3068\u306e\u30d5\u30ec\u30fc\u30e0\u30ec\u30fc\u30c8\u3068\u30d1\u30c3\u30c1\u306e\u5b9a\u671f\u5831\u544a\u3092\u30ed\u30b0\u306b\u8ffd\u52a0\u3057\u307e\u3059\u3002\u4e0d\u5177\u5408\u306e\u8abf\u67fb\u7528\u3067\u3001\u30d5\u30a1\u30a4\u30eb\u306f\u3059\u3050\u306b\u5927\u304d\u304f\u306a\u308a\u307e\u3059\u3002',
      '5\ucd08\ub9c8\ub2e4 \ud504\ub808\uc784 \ub808\uc774\ud2b8\uc640 \ud328\uce58\uc758 \uc815\uae30 \ubcf4\uace0\ub97c \ub85c\uadf8\uc5d0 \ucd94\uac00\ud569\ub2c8\ub2e4. \ubb38\uc81c \ud574\uacb0\uc6a9\uc774\uba70 \ud30c\uc77c\uc774 \ube60\ub974\uac8c \ucee4\uc9d1\ub2c8\ub2e4.',
      '\u6bcf 5 \u79d2\u5728\u8a18\u9304\u6a94\u52a0\u5165\u4e00\u884c FPS \u53ca\u4fee\u6b63\u7a0b\u5f0f\u7684\u5b9a\u671f\u5831\u544a\u3002\u50c5\u4f9b\u6392\u9664\u554f\u984c\u4f7f\u7528\uff0c\u6a94\u6848\u6703\u5f88\u5feb\u8b8a\u5927\u3002'))

# ------------------------------------------------------------------ shared texts of both games
def vsync_help(x2):
    extra = T(' FFX-2 itself presents without waiting: recommended on the Steam Deck when the picture tears.',
              ' FFX-2 selbst wartet nicht auf den Bildschirm: auf dem Steam Deck empfohlen, wenn das Bild zerrei\xdft.',
              ' FFX-2 lui-m\xeame n\'attend pas l\'\xe9cran : conseill\xe9 sur le Steam Deck si l\'image se d\xe9chire.',
              ' FFX-2 non attende lo schermo: consigliato sullo Steam Deck se l\'immagine si strappa.',
              ' FFX-2 no espera a la pantalla: recomendado en Steam Deck si la imagen se rompe.',
              'FFX-2\u306f\u753b\u9762\u3092\u5f85\u305f\u305a\u306b\u8868\u793a\u3059\u308b\u306e\u3067\u3001Steam Deck\u3067\u753b\u9762\u304c\u305a\u308c\u308b\u3068\u304d\u306b\u304a\u3059\u3059\u3081\u3067\u3059\u3002',
              ' FFX-2\ub294 \ud654\uba74\uc744 \uae30\ub2e4\ub9ac\uc9c0 \uc54a\uc73c\ubbc0\ub85c \uc2a4\ud300 \ub371\uc5d0\uc11c \ud654\uba74\uc774 \ucc22\uc5b4\uc9c8 \ub54c \uad8c\uc7a5\ud569\ub2c8\ub2e4.',
              'FFX-2 \u672c\u8eab\u4e0d\u7b49\u5f85\u87a2\u5e55\uff0c\u5728 Steam Deck \u4e0a\u756b\u9762\u6495\u88c2\u6642\u5efa\u8b70\u958b\u555f\u3002') if x2 else [''] * 8
    base = T('V-Sync by the patch: every frame waits for the screen refresh, no tearing. Off: the game\'s own V-Sync setting (above) decides. With On the frame rate must not be above the screen\'s refresh rate (Steam Deck: 60 or 90 fps), or the game runs slower.',
             'VSync durch den Patch: Jedes Bild wartet auf den Bildschirm, kein Zerrei\xdfen. Aus: Die VSync-Einstellung des Spiels (oben) entscheidet. Bei Ein darf die Framerate nicht \xfcber der Bildwiederholrate liegen (Steam Deck: 60 oder 90 fps), sonst l\xe4uft das Spiel langsamer.',
             'Vsync par le patch : chaque image attend le rafra\xeechissement de l\'\xe9cran, sans d\xe9chirure. D\xe9sactiv\xe9 : le r\xe9glage Vsync du jeu (plus haut) d\xe9cide. Activ\xe9, la fr\xe9quence ne doit pas d\xe9passer celle de l\'\xe9cran (Steam Deck : 60 ou 90 fps), sinon le jeu ralentit.',
             'Sincronizzazione verticale della patch: ogni fotogramma attende lo schermo, nessuno strappo. Disattivato: decide l\'impostazione del gioco (sopra). Se attivo, la frequenza non deve superare quella dello schermo (Steam Deck: 60 o 90 fps), altrimenti il gioco rallenta.',
             'Sincronizaci\xf3n vertical del parche: cada fotograma espera a la pantalla, sin cortes. Desactivado: decide el ajuste del juego (arriba). Activado, los fotogramas no deben superar la frecuencia de la pantalla (Steam Deck: 60 o 90 fps) o el juego ir\xe1 m\xe1s lento.',
             '\u30d1\u30c3\u30c1\u306b\u3088\u308b\u5782\u76f4\u540c\u671f\uff1a\u5404\u30d5\u30ec\u30fc\u30e0\u304c\u753b\u9762\u306e\u66f4\u65b0\u3092\u5f85\u3061\u3001\u753b\u9762\u304c\u305a\u308c\u307e\u305b\u3093\u3002\u30aa\u30d5\uff1a\u30b2\u30fc\u30e0\u306e\u5782\u76f4\u540c\u671f\u8a2d\u5b9a\uff08\u4e0a\uff09\u306b\u5f93\u3044\u307e\u3059\u3002\u30aa\u30f3\u306e\u3068\u304d\u306f\u30d5\u30ec\u30fc\u30e0\u30ec\u30fc\u30c8\u3092\u753b\u9762\u306e\u30ea\u30d5\u30ec\u30c3\u30b7\u30e5\u30ec\u30fc\u30c8\u4ee5\u4e0b\u306b\u3057\u3066\u304f\u3060\u3055\u3044\uff08Steam Deck\uff1a60\u307e\u305f\u306f90 fps\uff09\u3002',
             '\ud328\uce58\uc758 \uc218\uc9c1 \ub3d9\uae30\ud654: \ubaa8\ub4e0 \ud504\ub808\uc784\uc774 \ud654\uba74 \uac31\uc2e0\uc744 \uae30\ub2e4\ub824 \ud654\uba74\uc774 \ucc22\uc5b4\uc9c0\uc9c0 \uc54a\uc2b5\ub2c8\ub2e4. \ub044\uae30: \uac8c\uc784\uc758 Vsync \uc124\uc815(\uc704)\uc744 \ub530\ub985\ub2c8\ub2e4. \ucf1c\uba74 \ud504\ub808\uc784 \ub808\uc774\ud2b8\uac00 \ud654\uba74 \uc8fc\uc0ac\uc728(\uc2a4\ud300 \ub371: 60 \ub610\ub294 90 fps)\ubcf4\ub2e4 \ub192\uc73c\uba74 \uac8c\uc784\uc774 \ub290\ub824\uc9d1\ub2c8\ub2e4.',
             '\u4fee\u6b63\u7a0b\u5f0f\u7684\u5782\u76f4\u540c\u6b65\uff1a\u6bcf\u500b\u756b\u683c\u7b49\u5f85\u87a2\u5e55\u66f4\u65b0\uff0c\u4e0d\u6703\u6495\u88c2\u3002\u95dc\u9589\uff1a\u7531\u904a\u6232\u7684\u5782\u76f4\u540c\u6b65\u8a2d\u5b9a\uff08\u4e0a\u65b9\uff09\u6c7a\u5b9a\u3002\u958b\u555f\u6642 FPS \u4e0d\u53ef\u9ad8\u65bc\u87a2\u5e55\u66f4\u65b0\u7387\uff08Steam Deck\uff1a60 \u6216 90 fps\uff09\uff0c\u5426\u5247\u904a\u6232\u6703\u8b8a\u6162\u3002')
    return [base[i] + extra[i] for i in range(8)]
VSYNC_L = T('V-Sync (patch)', 'VSync (Patch)', 'Vsync (patch)', 'Sincronizzazione verticale (patch)', 'Sincronizaci\xf3n vertical (parche)',
            '\u5782\u76f4\u540c\u671f\uff08\u30d1\u30c3\u30c1\uff09', 'Vsync (\ud328\uce58)', '\u5782\u76f4\u540c\u6b65\uff08\u4fee\u6b63\u7a0b\u5f0f\uff09')
SMOOTH_L = T('Smooth motion', 'Fl\xfcssige Bewegung', 'Mouvement fluide', 'Movimento fluido', 'Movimiento fluido',
             '\u6ed1\u3089\u304b\u306a\u52d5\u304d', '\ubd80\ub4dc\ub7ec\uc6b4 \uc6c0\uc9c1\uc784', '\u6d41\u66a2\u52d5\u4f5c')
SMOOTH_H = T('The game logic runs 30 times a second. With this, camera and characters are drawn in between, so they move at the full frame rate. Off: they move in 30 steps per second as in the original - only the picture is drawn more often.',
             'Die Spiellogik l\xe4uft 30-mal pro Sekunde. Hiermit werden Kamera und Figuren dazwischen gezeichnet und bewegen sich mit der vollen Framerate. Aus: Sie bewegen sich wie im Original in 30 Schritten pro Sekunde - nur das Bild wird \xf6fter gezeichnet.',
             'La logique du jeu tourne 30 fois par seconde. Avec ceci, la cam\xe9ra et les personnages sont dessin\xe9s entre deux \xe9tapes et bougent \xe0 pleine fr\xe9quence. D\xe9sactiv\xe9 : ils bougent en 30 pas par seconde comme l\'original.',
             'La logica del gioco gira 30 volte al secondo. Con questo, telecamera e personaggi vengono disegnati nel mezzo e si muovono alla frequenza piena. Disattivato: si muovono a 30 passi al secondo come l\'originale.',
             'La l\xf3gica del juego va a 30 pasos por segundo. Con esto, la c\xe1mara y los personajes se dibujan entre medias y se mueven a la tasa completa. Desactivado: se mueven a 30 pasos por segundo como el original.',
             '\u30b2\u30fc\u30e0\u306e\u51e6\u7406\u306f1\u79d2\u306b30\u56de\u3067\u3059\u3002\u3053\u308c\u3092\u30aa\u30f3\u306b\u3059\u308b\u3068\u30ab\u30e1\u30e9\u3068\u30ad\u30e3\u30e9\u30af\u30bf\u30fc\u304c\u305d\u306e\u9593\u3082\u63cf\u304b\u308c\u3001\u30d5\u30ec\u30fc\u30e0\u30ec\u30fc\u30c8\u3069\u304a\u308a\u306b\u52d5\u304d\u307e\u3059\u3002\u30aa\u30d5\uff1a\u30aa\u30ea\u30b8\u30ca\u30eb\u3068\u540c\u3058\u304f1\u79d230\u56de\u306e\u52d5\u304d\u3067\u3059\u3002',
             '\uac8c\uc784 \ub85c\uc9c1\uc740 \ucd08\ub2f9 30\ubc88 \uc2e4\ud589\ub429\ub2c8\ub2e4. \uc774 \uae30\ub2a5\uc73c\ub85c \uce74\uba54\ub77c\uc640 \uce90\ub9ad\ud130\ub97c \uadf8 \uc0ac\uc774\uc5d0\ub3c4 \uadf8\ub824 \uc804\uccb4 \ud504\ub808\uc784 \ub808\uc774\ud2b8\ub85c \uc6c0\uc9c1\uc785\ub2c8\ub2e4. \ub044\uae30: \uc6d0\ubcf8\ucc98\ub7fc \ucd08\ub2f9 30\ubc88 \uc6c0\uc9c1\uc785\ub2c8\ub2e4.',
             '\u904a\u6232\u908f\u8f2f\u6bcf\u79d2\u57f7\u884c 30 \u6b21\u3002\u958b\u555f\u5f8c\u93e1\u982d\u8207\u89d2\u8272\u6703\u5728\u5176\u9593\u88dc\u756b\uff0c\u4ee5\u5b8c\u6574 FPS \u79fb\u52d5\u3002\u95dc\u9589\uff1a\u8207\u539f\u7248\u4e00\u6a23\u6bcf\u79d2 30 \u6b65\u3002')
SR_L = T('Soft reset', 'Soft-Reset', 'R\xe9initialisation', 'Reset rapido', 'Reinicio r\xe1pido', '\u30bd\u30d5\u30c8\u30ea\u30bb\u30c3\u30c8', '\uc18c\ud504\ud2b8 \ub9ac\uc14b', '\u8edf\u91cd\u7f6e')
def sr_help(battle):
    w = T('in the field and in battle', 'im Feld und im Kampf', 'sur le terrain et en combat', 'sul campo e in battaglia', 'en el campo y en combate',
          '\u30d5\u30a3\u30fc\u30eb\u30c9\u3068\u30d0\u30c8\u30eb\u3067', '\ud544\ub4dc\uc640 \uc804\ud22c\uc5d0\uc11c', '\u5728\u5730\u5716\u8207\u6230\u9b25\u4e2d') if battle else \
        T('in the field (not in battle)', 'im Feld (nicht im Kampf)', 'sur le terrain (pas en combat)', 'sul campo (non in battaglia)', 'en el campo (no en combate)',
          '\u30d5\u30a3\u30fc\u30eb\u30c9\u3067\uff08\u30d0\u30c8\u30eb\u4e2d\u306f\u4e0d\u53ef\uff09', '\ud544\ub4dc\uc5d0\uc11c(\uc804\ud22c \uc911 \uc81c\uc678)', '\u5728\u5730\u5716\u4e0a\uff08\u6230\u9b25\u4e2d\u4e0d\u53ef\uff09')
    return T('Holding the soft reset buttons (above) %s returns to the title screen without restarting the game. Progress since the last save is lost.' % w[0],
             'Halten der Soft-Reset-Tasten (oben) %s kehrt zum Titelbildschirm zur\xfcck, ohne das Spiel neu zu starten. Fortschritt seit dem letzten Speichern geht verloren.' % w[1],
             'Maintenir les touches de r\xe9initialisation (plus haut) %s ram\xe8ne \xe0 l\'\xe9cran titre sans relancer le jeu. La progression depuis la derni\xe8re sauvegarde est perdue.' % w[2],
             'Tenere premuti i tasti di reset (sopra) %s riporta alla schermata del titolo senza riavviare il gioco. I progressi dall\'ultimo salvataggio vanno persi.' % w[3],
             'Mantener los botones de reinicio (arriba) %s vuelve a la pantalla de t\xedtulo sin reiniciar el juego. Se pierde el progreso desde el \xfaltimo guardado.' % w[4],
             '%s\u30bd\u30d5\u30c8\u30ea\u30bb\u30c3\u30c8\u306e\u30dc\u30bf\u30f3\uff08\u4e0a\uff09\u3092\u62bc\u3057\u7d9a\u3051\u308b\u3068\u3001\u30b2\u30fc\u30e0\u3092\u518d\u8d77\u52d5\u305b\u305a\u306b\u30bf\u30a4\u30c8\u30eb\u753b\u9762\u306b\u623b\u308a\u307e\u3059\u3002\u6700\u5f8c\u306e\u30bb\u30fc\u30d6\u4ee5\u964d\u306e\u9032\u884c\u306f\u5931\u308f\u308c\u307e\u3059\u3002' % w[5],
             '%s \uc18c\ud504\ud2b8 \ub9ac\uc14b \ubc84\ud2bc(\uc704)\uc744 \ub204\ub974\uace0 \uc788\uc73c\uba74 \uac8c\uc784\uc744 \ub2e4\uc2dc \uc2dc\uc791\ud558\uc9c0 \uc54a\uace0 \ud0c0\uc774\ud2c0 \ud654\uba74\uc73c\ub85c \ub3cc\uc544\uac11\ub2c8\ub2e4. \ub9c8\uc9c0\ub9c9 \uc800\uc7a5 \uc774\ud6c4\uc758 \uc9c4\ud589\uc740 \uc0ac\ub77c\uc9d1\ub2c8\ub2e4.' % w[6],
             '%s\u6309\u4f4f\u8edf\u91cd\u7f6e\u6309\u9215\uff08\u4e0a\u65b9\uff09\u6703\u8fd4\u56de\u6a19\u984c\u756b\u9762\uff0c\u7121\u9700\u91cd\u65b0\u555f\u52d5\u904a\u6232\u3002\u4e0a\u6b21\u5b58\u6a94\u5f8c\u7684\u9032\u5ea6\u6703\u907a\u5931\u3002' % w[7])
TURBO_L = T('Fast-forward (Turbo)', 'Vorspulen (Turbo)', 'Avance rapide (Turbo)', 'Avanzamento rapido (Turbo)', 'Avance r\xe1pido (Turbo)',
            '\u65e9\u9001\u308a\uff08\u30bf\u30fc\u30dc\uff09', '\ube68\ub9ac \uac10\uae30(\ud130\ubcf4)', '\u5feb\u8f49\uff08\u6e26\u8f2a\uff09')
TURBO_H = T('While the turbo key is held, the whole game runs faster (speed below): walking, battles, cutscenes. Needs "Keyboard keys of the patch" = On.',
            'Solange die Turbo-Taste gehalten wird, l\xe4uft das ganze Spiel schneller (Tempo unten): Laufen, K\xe4mpfe, Zwischensequenzen. Braucht \u201eTastaturtasten des Patches\u201c = Ein.',
            'Tant que la touche Turbo est maintenue, tout le jeu va plus vite (vitesse plus bas) : d\xe9placements, combats, cin\xe9matiques. N\xe9cessite \xab Touches clavier du patch \xbb activ\xe9.',
            'Finch\xe9 il tasto Turbo \xe8 premuto, tutto il gioco va pi\xf9 veloce (velocit\xe0 sotto): camminata, battaglie, filmati. Richiede "Tasti della tastiera della patch" attivo.',
            'Mientras se mantiene la tecla Turbo, todo el juego va m\xe1s r\xe1pido (velocidad abajo): caminar, combates, escenas. Requiere "Teclas del parche" activado.',
            '\u30bf\u30fc\u30dc\u30ad\u30fc\u3092\u62bc\u3057\u3066\u3044\u308b\u9593\u3001\u30b2\u30fc\u30e0\u5168\u4f53\u304c\u901f\u304f\u9032\u307f\u307e\u3059\uff08\u901f\u5ea6\u306f\u4e0b\u3067\u8a2d\u5b9a\uff09\u3002\u300c\u30d1\u30c3\u30c1\u306e\u30ad\u30fc\u30dc\u30fc\u30c9\u64cd\u4f5c\u300d\u3092\u30aa\u30f3\u306b\u3057\u3066\u304f\u3060\u3055\u3044\u3002',
            '\ud130\ubcf4 \ud0a4\ub97c \ub204\ub974\uace0 \uc788\ub294 \ub3d9\uc548 \uac8c\uc784 \uc804\uccb4\uac00 \ube68\ub77c\uc9d1\ub2c8\ub2e4(\uc18d\ub3c4\ub294 \uc544\ub798). "\ud328\uce58 \ud0a4\ubcf4\ub4dc \ud0a4"\ub97c \ucf1c\uc57c \ud569\ub2c8\ub2e4.',
            '\u6309\u4f4f\u6e26\u8f2a\u9375\u6642\uff0c\u6574\u500b\u904a\u6232\u52a0\u901f\uff08\u901f\u5ea6\u898b\u4e0b\u65b9\uff09\u3002\u9700\u5c07\u300c\u4fee\u6b63\u7a0b\u5f0f\u9375\u76e4\u6309\u9375\u300d\u958b\u555f\u3002')
TKEY_L = T('Turbo key', 'Turbo-Taste', 'Touche Turbo', 'Tasto Turbo', 'Tecla Turbo', '\u30bf\u30fc\u30dc\u30ad\u30fc', '\ud130\ubcf4 \ud0a4', '\u6e26\u8f2a\u9375')
TKEY_H = T('The keyboard key that has to be held for fast-forward. Num = number pad. F7 is not offered: it switches smooth motion.',
           'Die Tastaturtaste, die zum Vorspulen gehalten wird. Num = Ziffernblock. F7 gibt es nicht: Damit wird die fl\xfcssige Bewegung umgeschaltet.',
           'La touche \xe0 maintenir pour l\'avance rapide. Num = pav\xe9 num\xe9rique. F7 n\'est pas propos\xe9e : elle change le mouvement fluide.',
           'Il tasto da tenere premuto per l\'avanzamento rapido. Num = tastierino numerico. F7 non c\'\xe8: cambia il movimento fluido.',
           'La tecla que se mantiene para el avance r\xe1pido. Num = teclado num\xe9rico. F7 no est\xe1: cambia el movimiento fluido.',
           '\u65e9\u9001\u308a\u306e\u3068\u304d\u306b\u62bc\u3057\u7d9a\u3051\u308b\u30ad\u30fc\u3067\u3059\u3002Num\uff1d\u30c6\u30f3\u30ad\u30fc\u3002F7\u306f\u6ed1\u3089\u304b\u306a\u52d5\u304d\u306e\u5207\u308a\u66ff\u3048\u306b\u4f7f\u3046\u306e\u3067\u9078\u3079\u307e\u305b\u3093\u3002',
           '\ube68\ub9ac \uac10\uae30\ub97c \uc704\ud574 \ub204\ub974\uace0 \uc788\ub294 \ud0a4\uc785\ub2c8\ub2e4. Num = \uc22b\uc790 \ud0a4\ud328\ub4dc. F7\uc740 \ubd80\ub4dc\ub7ec\uc6b4 \uc6c0\uc9c1\uc784 \uc804\ud658\uc5d0 \uc4f0\uc774\ubbc0\ub85c \uc5c6\uc2b5\ub2c8\ub2e4.',
           '\u5feb\u8f49\u6642\u9700\u6309\u4f4f\u7684\u9375\u3002Num = \u6578\u5b57\u9375\u76e4\u3002F7 \u7528\u65bc\u5207\u63db\u6d41\u66a2\u52d5\u4f5c\uff0c\u6545\u4e0d\u63d0\u4f9b\u3002')
TSPD_L = T('Turbo speed', 'Turbo-Tempo', 'Vitesse Turbo', 'Velocit\xe0 Turbo', 'Velocidad Turbo', '\u30bf\u30fc\u30dc\u306e\u901f\u5ea6', '\ud130\ubcf4 \uc18d\ub3c4', '\u6e26\u8f2a\u901f\u5ea6')
TSPD_H = T('How many times faster the game runs while the turbo key is held. Above 60 fps the factor is only reached roughly (x10 is about x8 at 240 fps).',
           'Wie viel schneller das Spiel l\xe4uft, solange die Turbo-Taste gehalten wird. \xdcber 60 fps wird der Faktor nur ungef\xe4hr erreicht (x10 sind bei 240 fps etwa x8).',
           'Combien de fois plus vite le jeu va tant que la touche Turbo est maintenue. Au-dessus de 60 fps, le facteur n\'est atteint qu\'\xe0 peu pr\xe8s (x10 donne environ x8 \xe0 240 fps).',
           'Quante volte pi\xf9 veloce va il gioco mentre il tasto Turbo \xe8 premuto. Sopra 60 fps il fattore \xe8 solo approssimativo (x10 \xe8 circa x8 a 240 fps).',
           'Cu\xe1ntas veces m\xe1s r\xe1pido va el juego mientras se mantiene la tecla Turbo. Por encima de 60 fps el factor es aproximado (x10 es unos x8 a 240 fps).',
           '\u30bf\u30fc\u30dc\u30ad\u30fc\u3092\u62bc\u3057\u3066\u3044\u308b\u9593\u306e\u901f\u3055\u306e\u500d\u7387\u3067\u3059\u300260 fps\u3092\u8d85\u3048\u308b\u3068\u304a\u304a\u3088\u305d\u306e\u500d\u7387\u306b\u306a\u308a\u307e\u3059\uff08240 fps\u3067x10\u306f\u7d04x8\uff09\u3002',
           '\ud130\ubcf4 \ud0a4\ub97c \ub204\ub974\uace0 \uc788\ub294 \ub3d9\uc548 \uac8c\uc784\uc774 \uba87 \ubc30 \ube68\ub77c\uc9c0\ub294\uc9c0 \uc815\ud569\ub2c8\ub2e4. 60 fps \ucd08\uacfc \uc2dc\uc5d0\ub294 \ub300\ub7b5\uc801\uc785\ub2c8\ub2e4(240 fps\uc5d0\uc11c x10\uc740 \uc57d x8).',
           '\u6309\u4f4f\u6e26\u8f2a\u9375\u6642\u904a\u6232\u52a0\u901f\u7684\u500d\u6578\u3002\u8d85\u904e 60 fps \u6642\u53ea\u80fd\u5927\u81f4\u9054\u5230\uff08240 fps \u6642 x10 \u7d04\u70ba x8\uff09\u3002')

def game_header(name, what):
    return T(name + ' - ' + what[0], name + ' - ' + what[1], name + ' - ' + what[2], name + ' - ' + what[3], name + ' - ' + what[4],
             name + ' - ' + what[5], name + ' - ' + what[6], name + ' - ' + what[7])

# ------------------------------------------------------------------ FFX
header(game_header('FINAL FANTASY X', T('also Eternal Calm', 'auch Eternal Calm', 'aussi Eternal Calm', 'anche Eternal Calm', 'tambi\xe9n Eternal Calm',
                                         'Eternal Calm\u3082', 'Eternal Calm \ud3ec\ud568', '\u542b Eternal Calm')))
opt('rows', 'VSYNC', '0', VSYNC_L, vsync_help(False))
opt('rows', 'SMOOTH', '1', SMOOTH_L, SMOOTH_H)
opt('rows', 'PERF-002', '1',
    T('Lower the frame rate automatically', 'Framerate automatisch senken', 'Baisser la fr\xe9quence automatiquement', 'Abbassa la frequenza automaticamente', 'Bajar los fotogramas autom\xe1ticamente',
      '\u30d5\u30ec\u30fc\u30e0\u30ec\u30fc\u30c8\u3092\u81ea\u52d5\u3067\u4e0b\u3052\u308b', '\ud504\ub808\uc784 \ub808\uc774\ud2b8 \uc790\ub3d9 \ub0ae\ucd94\uae30', '\u81ea\u52d5\u964d\u4f4e FPS'),
    T('When the PC cannot hold the chosen frame rate, it is lowered in steps of 30 fps so that the game keeps its normal speed, and tried again later. With pacing "Optimized" it never goes below 60. Off: a PC that is too slow runs the game slower.',
      'Schafft der PC die gew\xe4hlte Framerate nicht, wird sie in 30-fps-Schritten gesenkt, damit das Spiel sein normales Tempo beh\xe4lt, und sp\xe4ter erneut versucht. Mit Taktung \u201eOptimiert\u201c nie unter 60. Aus: Ein zu langsamer PC l\xe4sst das Spiel langsamer laufen.',
      'Si le PC ne tient pas la fr\xe9quence choisie, elle baisse par paliers de 30 fps pour que le jeu garde sa vitesse, puis est retent\xe9e plus tard. Avec \xab Optimis\xe9 \xbb, jamais sous 60. D\xe9sactiv\xe9 : un PC trop lent ralentit le jeu.',
      'Se il PC non regge la frequenza scelta, scende a passi di 30 fps cos\xec il gioco mantiene la velocit\xe0, e viene ritentata pi\xf9 tardi. Con "Ottimizzato" mai sotto 60. Disattivato: un PC troppo lento rallenta il gioco.',
      'Si el PC no mantiene los fotogramas elegidos, bajan en pasos de 30 fps para que el juego conserve su velocidad, y se reintenta m\xe1s tarde. Con "Optimizado" nunca baja de 60. Desactivado: un PC lento ralentiza el juego.',
      'PC\u304c\u9078\u3093\u3060\u30d5\u30ec\u30fc\u30e0\u30ec\u30fc\u30c8\u3092\u7dad\u6301\u3067\u304d\u306a\u3044\u3068\u304d\u306f30 fps\u305a\u3064\u4e0b\u3052\u3066\u30b2\u30fc\u30e0\u306e\u901f\u5ea6\u3092\u4fdd\u3061\u3001\u5f8c\u3067\u518d\u3073\u8a66\u3057\u307e\u3059\u3002\u300c\u6700\u9069\u5316\u300d\u3067\u306f60\u672a\u6e80\u306b\u306f\u306a\u308a\u307e\u305b\u3093\u3002\u30aa\u30d5\uff1a\u9045\u3044PC\u3067\u306f\u30b2\u30fc\u30e0\u304c\u9045\u304f\u306a\u308a\u307e\u3059\u3002',
      'PC\uac00 \uc120\ud0dd\ud55c \ud504\ub808\uc784 \ub808\uc774\ud2b8\ub97c \uc720\uc9c0\ud558\uc9c0 \ubabb\ud558\uba74 30 fps\uc529 \ub0ae\ucdb0 \uac8c\uc784 \uc18d\ub3c4\ub97c \uc720\uc9c0\ud558\uace0 \ub098\uc911\uc5d0 \ub2e4\uc2dc \uc2dc\ub3c4\ud569\ub2c8\ub2e4. "\ucd5c\uc801\ud654"\uc5d0\uc11c\ub294 60 \ubbf8\ub9cc\uc73c\ub85c \ub0b4\ub824\uac00\uc9c0 \uc54a\uc2b5\ub2c8\ub2e4. \ub044\uae30: \ub290\ub9b0 PC\uc5d0\uc11c\ub294 \uac8c\uc784\uc774 \ub290\ub824\uc9d1\ub2c8\ub2e4.',
      '\u96fb\u8166\u7121\u6cd5\u7dad\u6301\u6240\u9078 FPS \u6642\uff0c\u6bcf\u6b21\u964d\u4f4e 30 fps \u4ee5\u4fdd\u6301\u904a\u6232\u901f\u5ea6\uff0c\u7a0d\u5f8c\u518d\u5617\u8a66\u3002\u300c\u6700\u4f73\u5316\u300d\u6642\u4e0d\u4f4e\u65bc 60\u3002\u95dc\u9589\uff1a\u96fb\u8166\u592a\u6162\u6642\u904a\u6232\u6703\u8b8a\u6162\u3002'))
opt('rows', 'CLK-013', '1',
    T('Lip-sync in voiced scenes', 'Lippensynchron in vertonten Szenen', 'Synchro labiale des sc\xe8nes parl\xe9es', 'Sincronia labiale nelle scene doppiate', 'Sincron\xeda labial en escenas habladas',
      '\u30dc\u30a4\u30b9\u4ed8\u304d\u30b7\u30fc\u30f3\u306e\u53e3\u306e\u52d5\u304d', '\uc74c\uc131 \uc7a5\uba74 \uc785 \ub9de\ucd94\uae30', '\u914d\u97f3\u5834\u666f\u7684\u53e3\u578b\u540c\u6b65'),
    T('Voiced cutscenes run at the original 30 fps, so that voice and lip movement stay in sync. Off: these scenes run at the full frame rate too, but the lips can drift away from the voice.',
      'Vertonte Zwischensequenzen laufen mit den originalen 30 fps, damit Stimme und Lippenbewegung synchron bleiben. Aus: Auch diese Szenen laufen mit voller Framerate, aber die Lippen k\xf6nnen von der Stimme abweichen.',
      'Les cin\xe9matiques parl\xe9es tournent aux 30 fps d\'origine pour que la voix et les l\xe8vres restent synchrones. D\xe9sactiv\xe9 : elles tournent aussi \xe0 pleine fr\xe9quence, mais les l\xe8vres peuvent se d\xe9caler.',
      'I filmati doppiati vanno ai 30 fps originali, cos\xec voce e labbra restano sincronizzate. Disattivato: vanno anch\'essi alla frequenza piena, ma le labbra possono sfasarsi dalla voce.',
      'Las escenas habladas van a los 30 fps originales para que voz y labios sigan sincronizados. Desactivado: tambi\xe9n van a la tasa completa, pero los labios pueden desfasarse de la voz.',
      '\u30dc\u30a4\u30b9\u4ed8\u304d\u30a4\u30d9\u30f3\u30c8\u306f\u5143\u306e30 fps\u3067\u518d\u751f\u3057\u3001\u58f0\u3068\u53e3\u306e\u52d5\u304d\u3092\u5408\u308f\u305b\u307e\u3059\u3002\u30aa\u30d5\uff1a\u3053\u308c\u3089\u3082\u30d5\u30eb\u30d5\u30ec\u30fc\u30e0\u30ec\u30fc\u30c8\u306b\u306a\u308a\u307e\u3059\u304c\u3001\u53e3\u304c\u305a\u308c\u308b\u3053\u3068\u304c\u3042\u308a\u307e\u3059\u3002',
      '\uc74c\uc131\uc774 \uc788\ub294 \ucef7\uc2e0\uc740 \uc6d0\ub798\uc758 30 fps\ub85c \uc7ac\uc0dd\ub418\uc5b4 \ubaa9\uc18c\ub9ac\uc640 \uc785 \ubaa8\uc591\uc774 \ub9de\uc2b5\ub2c8\ub2e4. \ub044\uae30: \uc774 \uc7a5\uba74\ub3c4 \uc804\uccb4 \ud504\ub808\uc784 \ub808\uc774\ud2b8\ub85c \uc7ac\uc0dd\ub418\uc9c0\ub9cc \uc785\uc774 \uc5b4\uae0b\ub0a0 \uc218 \uc788\uc2b5\ub2c8\ub2e4.',
      '\u914d\u97f3\u904e\u5834\u4ee5\u539f\u59cb 30 fps \u64ad\u653e\uff0c\u8b93\u8072\u97f3\u8207\u53e3\u578b\u4fdd\u6301\u540c\u6b65\u3002\u95dc\u9589\uff1a\u9019\u4e9b\u5834\u666f\u4e5f\u4ee5\u5b8c\u6574 FPS \u64ad\u653e\uff0c\u4f46\u53e3\u578b\u53ef\u80fd\u8207\u8072\u97f3\u932f\u958b\u3002'))
opt('rows', 'SR-003', '0', SR_L, sr_help(False))
opt('rows', 'DEV-TURBO', '0', TURBO_L, TURBO_H)
opt('rows', 'DEV-TURBO-KEY', '96', TKEY_L, TKEY_H, [k[0] for k in KEYS], [[k[1] for k in KEYS]] * 8)
opt('rows', 'DEV-TURBO-SPEED', '10', TSPD_L, TSPD_H, [s[0] for s in SPEED], [[s[1] for s in SPEED]] * 8)
GAMEPLAY = T(' Changes the gameplay.', ' Ver\xe4ndert das Spiel.', ' Modifie le gameplay.', ' Modifica il gameplay.', ' Cambia la jugabilidad.',
             '\u30b2\u30fc\u30e0\u5185\u5bb9\u304c\u5909\u308f\u308a\u307e\u3059\u3002', ' \uac8c\uc784 \ub0b4\uc6a9\uc774 \ubc14\ub01d\ub2c8\ub2e4.', '\u6703\u6539\u8b8a\u904a\u6232\u5167\u5bb9\u3002')
def gp(t): return [t[i] + GAMEPLAY[i] for i in range(8)]
opt('rows', 'QOL-SENSOR', '0',
    T('Sensor for everyone', 'Analyse f\xfcr alle', 'Analyse pour tous', 'Sensore per tutti', 'Sensor para todos',
      '\u5168\u54e1\u306b\u30bb\u30f3\u30b5\u30fc', '\ubaa8\ub450\uc5d0\uac8c \uc13c\uc11c', '\u5168\u54e1\u5177\u5099\u63a2\u6e2c'),
    gp(T('Every party member has the Sensor effect (enemy HP and information when targeting) without needing a weapon with it.',
         'Jedes Gruppenmitglied hat den Effekt \u201eAnalyse\u201c (Gegner-KP und Infos beim Anvisieren), ohne eine Waffe damit zu brauchen.',
         'Chaque membre de l\'\xe9quipe a l\'effet Analyse (PV et infos de l\'ennemi en le ciblant) sans arme qui le porte.',
         'Ogni membro del gruppo ha l\'effetto Sensore (PV e informazioni del nemico mirandolo) senza un\'arma che lo abbia.',
         'Cada miembro del grupo tiene el efecto Sensor (PV e informaci\xf3n del enemigo al apuntarle) sin necesitar un arma con \xe9l.',
         '\u5168\u54e1\u304c\u30bb\u30f3\u30b5\u30fc\u306e\u52b9\u679c\uff08\u72d9\u3063\u305f\u6575\u306eHP\u3068\u60c5\u5831\uff09\u3092\u6301\u3061\u307e\u3059\u3002\u305d\u306e\u80fd\u529b\u306e\u3042\u308b\u6b66\u5668\u306f\u4e0d\u8981\u3067\u3059\u3002',
         '\ubaa8\ub4e0 \ud30c\ud2f0\uc6d0\uc774 \uc13c\uc11c \ud6a8\uacfc(\ub300\uc0c1 \uc801\uc758 HP\uc640 \uc815\ubcf4)\ub97c \uac16\uc2b5\ub2c8\ub2e4. \ud574\ub2f9 \ubb34\uae30\uac00 \ud544\uc694 \uc5c6\uc2b5\ub2c8\ub2e4.',
         '\u5168\u968a\u6210\u54e1\u7686\u5177\u5099\u63a2\u6e2c\u6548\u679c\uff08\u9396\u5b9a\u6575\u4eba\u6642\u986f\u793a HP \u8207\u8cc7\u8a0a\uff09\uff0c\u4e0d\u9700\u8981\u9644\u5e36\u6b64\u80fd\u529b\u7684\u6b66\u5668\u3002')))
opt('rows', 'QOL-AP', '0',
    T('AP for the whole party', 'AP f\xfcr die ganze Gruppe', 'PA pour toute l\'\xe9quipe', 'PA per tutto il gruppo', 'PH para todo el grupo',
      '\u5168\u54e1\u306bAP', '\ud30c\ud2f0 \uc804\uc6d0 AP', '\u5168\u968a\u7372\u5f97 AP'),
    gp(T('After a battle every party member earns AP, also those who did not fight.',
         'Nach einem Kampf erh\xe4lt jedes Gruppenmitglied AP, auch wer nicht gek\xe4mpft hat.',
         'Apr\xe8s un combat, chaque membre gagne des PA, m\xeame ceux qui n\'ont pas combattu.',
         'Dopo una battaglia ogni membro guadagna PA, anche chi non ha combattuto.',
         'Tras un combate cada miembro gana PH, tambi\xe9n los que no lucharon.',
         '\u30d0\u30c8\u30eb\u5f8c\u3001\u6226\u3063\u3066\u3044\u306a\u3044\u30e1\u30f3\u30d0\u30fc\u3082\u542b\u3081\u5168\u54e1\u304cAP\u3092\u5f97\u307e\u3059\u3002',
         '\uc804\ud22c \ud6c4 \uc2f8\uc6b0\uc9c0 \uc54a\uc740 \ud30c\ud2f0\uc6d0\uc744 \ud3ec\ud568\ud574 \ubaa8\ub450 AP\ub97c \uc5bb\uc2b5\ub2c8\ub2e4.',
         '\u6230\u9b25\u5f8c\u6240\u6709\u968a\u54e1\u90fd\u7372\u5f97 AP\uff0c\u5305\u62ec\u672a\u53c3\u6230\u8005\u3002')))
opt('rows', 'QOL-AP-KO', '1',
    T('AP also for KO\'d members', 'AP auch f\xfcr K.O.-Mitglieder', 'PA aussi pour les K.O.', 'PA anche per chi \xe8 KO', 'PH tambi\xe9n para los KO',
      '\u6226\u95d8\u4e0d\u80fd\u306e\u30e1\u30f3\u30d0\u30fc\u306b\u3082AP', '\uc804\ud22c\ubd88\ub2a5 \ud30c\ud2f0\uc6d0\ub3c4 AP', '\u7121\u6cd5\u6230\u9b25\u8005\u4ea6\u7372 AP'),
    T('With "AP for the whole party": members who are KO at the end of the battle earn AP too. Off: only members still standing at the end.',
      'Mit \u201eAP f\xfcr die ganze Gruppe\u201c: Auch Mitglieder, die am Ende des Kampfes K.O. sind, erhalten AP. Aus: nur wer am Ende noch steht.',
      'Avec \xab PA pour toute l\'\xe9quipe \xbb : les membres K.O. \xe0 la fin du combat gagnent aussi des PA. D\xe9sactiv\xe9 : seulement ceux encore debout.',
      'Con "PA per tutto il gruppo": anche i membri KO alla fine della battaglia guadagnano PA. Disattivato: solo chi \xe8 ancora in piedi.',
      'Con "PH para todo el grupo": los miembros KO al final del combate tambi\xe9n ganan PH. Desactivado: solo los que siguen en pie.',
      '\u300c\u5168\u54e1\u306bAP\u300d\u304c\u30aa\u30f3\u306e\u3068\u304d\u3001\u6226\u95d8\u7d42\u4e86\u6642\u306b\u6226\u95d8\u4e0d\u80fd\u306e\u30e1\u30f3\u30d0\u30fc\u3082AP\u3092\u5f97\u307e\u3059\u3002\u30aa\u30d5\uff1a\u6700\u5f8c\u306b\u7acb\u3063\u3066\u3044\u308b\u30e1\u30f3\u30d0\u30fc\u3060\u3051\u3067\u3059\u3002',
      '"\ud30c\ud2f0 \uc804\uc6d0 AP"\uac00 \ucf1c\uc838 \uc788\uc744 \ub54c \uc804\ud22c \uc885\ub8cc \uc2dc \uc804\ud22c\ubd88\ub2a5\uc778 \ud30c\ud2f0\uc6d0\ub3c4 AP\ub97c \uc5bb\uc2b5\ub2c8\ub2e4. \ub044\uae30: \ub05d\uae4c\uc9c0 \uc0b4\uc544\ub0a8\uc740 \ud30c\ud2f0\uc6d0\ub9cc.',
      '\u958b\u555f\u300c\u5168\u968a\u7372\u5f97 AP\u300d\u6642\uff0c\u6230\u9b25\u7d50\u675f\u6642\u7121\u6cd5\u6230\u9b25\u7684\u968a\u54e1\u4e5f\u7372\u5f97 AP\u3002\u95dc\u9589\uff1a\u50c5\u9650\u6700\u5f8c\u4ecd\u7ad9\u8457\u7684\u968a\u54e1\u3002'))
opt('rows', 'QOL-AP-RATE', '100',
    T('AP for non-fighters', 'AP f\xfcr Nicht-K\xe4mpfer', 'PA des non-combattants', 'PA per chi non combatte', 'PH para quien no lucha',
      '\u6226\u3063\u3066\u3044\u306a\u3044\u30e1\u30f3\u30d0\u30fc\u306eAP', '\ube44\uc804\ud22c \ud30c\ud2f0\uc6d0 AP', '\u672a\u53c3\u6230\u8005\u7684 AP'),
    T('With "AP for the whole party": how much AP members who did not fight get, in percent of a fighter\'s AP.',
      'Mit \u201eAP f\xfcr die ganze Gruppe\u201c: wie viel AP Mitglieder erhalten, die nicht gek\xe4mpft haben, in Prozent der AP eines K\xe4mpfers.',
      'Avec \xab PA pour toute l\'\xe9quipe \xbb : PA des membres qui n\'ont pas combattu, en pourcentage de ceux d\'un combattant.',
      'Con "PA per tutto il gruppo": quanti PA ricevono i membri che non hanno combattuto, in percentuale di quelli di un combattente.',
      'Con "PH para todo el grupo": cu\xe1ntos PH reciben los miembros que no lucharon, en porcentaje de los de un luchador.',
      '\u300c\u5168\u54e1\u306bAP\u300d\u304c\u30aa\u30f3\u306e\u3068\u304d\u3001\u6226\u3063\u3066\u3044\u306a\u3044\u30e1\u30f3\u30d0\u30fc\u304c\u5f97\u308bAP\u3092\u3001\u6226\u3063\u305f\u30e1\u30f3\u30d0\u30fc\u306b\u5bfe\u3059\u308b\u5272\u5408\u3067\u8a2d\u5b9a\u3057\u307e\u3059\u3002',
      '"\ud30c\ud2f0 \uc804\uc6d0 AP"\uac00 \ucf1c\uc838 \uc788\uc744 \ub54c \uc2f8\uc6b0\uc9c0 \uc54a\uc740 \ud30c\ud2f0\uc6d0\uc774 \ubc1b\ub294 AP\ub97c \uc2f8\uc6b4 \ud30c\ud2f0\uc6d0\uc758 \ube44\uc728(%)\ub85c \uc815\ud569\ub2c8\ub2e4.',
      '\u958b\u555f\u300c\u5168\u968a\u7372\u5f97 AP\u300d\u6642\uff0c\u672a\u53c3\u6230\u968a\u54e1\u7372\u5f97\u7684 AP \u4f54\u53c3\u6230\u8005\u7684\u767e\u5206\u6bd4\u3002'),
    ['25', '50', '75', '100'], [['25 %', '50 %', '75 %', '100 %']] * 8)
opt('rows', 'QOL-RETRY', '0',
    T('Retry after a party wipe', 'Wiederholen nach Niederlage', 'R\xe9essayer apr\xe8s une d\xe9faite', 'Riprova dopo una sconfitta', 'Reintentar tras una derrota',
      '\u5168\u6ec5\u5f8c\u306e\u30ea\u30c8\u30e9\u30a4', '\uc804\uba78 \ud6c4 \uc7ac\uc2dc\ub3c4', '\u5168\u6ec5\u5f8c\u91cd\u8a66'),
    gp(T('After a party wipe the game asks "Retry / Give up". Retry restarts the battle as it began, instead of Game Over.',
         'Nach einer Niederlage fragt das Spiel \u201eWiederholen / Aufgeben\u201c. Wiederholen startet den Kampf so neu, wie er begann, statt Game Over.',
         'Apr\xe8s une d\xe9faite, le jeu demande \xab R\xe9essayer / Abandonner \xbb. R\xe9essayer relance le combat tel qu\'il a commenc\xe9, au lieu du Game Over.',
         'Dopo una sconfitta il gioco chiede "Riprova / Arrenditi". Riprova ricomincia la battaglia come era iniziata, invece del Game Over.',
         'Tras una derrota el juego pregunta "Reintentar / Rendirse". Reintentar reinicia el combate como empez\xf3, en lugar de Game Over.',
         '\u5168\u6ec5\u3057\u305f\u3068\u304d\u300c\u30ea\u30c8\u30e9\u30a4 / \u3042\u304d\u3089\u3081\u308b\u300d\u3092\u9078\u3079\u307e\u3059\u3002\u30ea\u30c8\u30e9\u30a4\u3067\u306f\u30b2\u30fc\u30e0\u30aa\u30fc\u30d0\u30fc\u306e\u4ee3\u308f\u308a\u306b\u6226\u95d8\u3092\u6700\u521d\u304b\u3089\u3084\u308a\u76f4\u3057\u307e\u3059\u3002',
         '\uc804\uba78\ud558\uba74 "\uc7ac\uc2dc\ub3c4 / \ud3ec\uae30"\ub97c \ubb3b\uc2b5\ub2c8\ub2e4. \uc7ac\uc2dc\ub3c4\ub294 \uac8c\uc784 \uc624\ubc84 \ub300\uc2e0 \uc804\ud22c\ub97c \ucc98\uc74c\ubd80\ud130 \ub2e4\uc2dc \uc2dc\uc791\ud569\ub2c8\ub2e4.',
         '\u5168\u6ec5\u5f8c\u904a\u6232\u6703\u8a62\u554f\u300c\u91cd\u8a66 / \u653e\u68c4\u300d\u3002\u91cd\u8a66\u6703\u5f9e\u982d\u958b\u59cb\u8a72\u5834\u6230\u9b25\uff0c\u800c\u4e0d\u662f\u904a\u6232\u7d50\u675f\u3002')))
opt('rows', 'QOL-RETRY-CHORD', '1',
    T('Retry prompt on soft reset', 'Wiederholen-Abfrage beim Soft-Reset', 'Question R\xe9essayer \xe0 la r\xe9initialisation', 'Richiesta Riprova al reset', 'Pregunta Reintentar al reiniciar',
      '\u30bd\u30d5\u30c8\u30ea\u30bb\u30c3\u30c8\u3067\u30ea\u30c8\u30e9\u30a4\u3092\u8868\u793a', '\uc18c\ud504\ud2b8 \ub9ac\uc14b \uc2dc \uc7ac\uc2dc\ub3c4 \ud45c\uc2dc', '\u8edf\u91cd\u7f6e\u6642\u8a62\u554f\u91cd\u8a66'),
    T('With "Retry after a party wipe": the soft reset buttons in battle show the Retry question too. Off: in battle they go straight to the title screen.',
      'Mit \u201eWiederholen nach Niederlage\u201c: Die Soft-Reset-Tasten zeigen im Kampf ebenfalls die Wiederholen-Abfrage. Aus: Im Kampf geht es direkt zum Titelbildschirm.',
      'Avec \xab R\xe9essayer apr\xe8s une d\xe9faite \xbb : les touches de r\xe9initialisation en combat affichent aussi la question. D\xe9sactiv\xe9 : retour direct \xe0 l\'\xe9cran titre.',
      'Con "Riprova dopo una sconfitta": i tasti di reset in battaglia mostrano anche la richiesta. Disattivato: si va subito alla schermata del titolo.',
      'Con "Reintentar tras una derrota": los botones de reinicio en combate muestran tambi\xe9n la pregunta. Desactivado: van directos a la pantalla de t\xedtulo.',
      '\u300c\u5168\u6ec5\u5f8c\u306e\u30ea\u30c8\u30e9\u30a4\u300d\u304c\u30aa\u30f3\u306e\u3068\u304d\u3001\u30d0\u30c8\u30eb\u4e2d\u306e\u30bd\u30d5\u30c8\u30ea\u30bb\u30c3\u30c8\u3067\u3082\u30ea\u30c8\u30e9\u30a4\u3092\u9078\u3079\u307e\u3059\u3002\u30aa\u30d5\uff1a\u3059\u3050\u306b\u30bf\u30a4\u30c8\u30eb\u753b\u9762\u306b\u623b\u308a\u307e\u3059\u3002',
      '"\uc804\uba78 \ud6c4 \uc7ac\uc2dc\ub3c4"\uac00 \ucf1c\uc838 \uc788\uc744 \ub54c \uc804\ud22c \uc911 \uc18c\ud504\ud2b8 \ub9ac\uc14b \ubc84\ud2bc\ub3c4 \uc7ac\uc2dc\ub3c4 \uc9c8\ubb38\uc744 \ud45c\uc2dc\ud569\ub2c8\ub2e4. \ub044\uae30: \ubc14\ub85c \ud0c0\uc774\ud2c0 \ud654\uba74\uc73c\ub85c \uac11\ub2c8\ub2e4.',
      '\u958b\u555f\u300c\u5168\u6ec5\u5f8c\u91cd\u8a66\u300d\u6642\uff0c\u6230\u9b25\u4e2d\u6309\u8edf\u91cd\u7f6e\u6309\u9215\u4e5f\u6703\u8a62\u554f\u91cd\u8a66\u3002\u95dc\u9589\uff1a\u76f4\u63a5\u56de\u5230\u6a19\u984c\u756b\u9762\u3002'))
opt('rows', 'QOL-FURY', '0',
    T('Fast Fury (Lulu)', 'Schnelle Rage (Lulu)', 'Furie rapide (Lulu)', 'Furia rapida (Lulu)', 'Furia r\xe1pida (Lulu)',
      '\u9023\u7d9a\u9b54\u6cd5\u3092\u9ad8\u901f\u5316\uff08\u30eb\u30fc\u30eb\u30fc\uff09', '\ube60\ub978 \ud4e8\ub9ac(\ub8e8\ub8e8)', '\u5feb\u901f\u9023\u7e8c\u9b54\u6cd5\uff08\u7490\u7490\uff09'),
    gp(T('Lulu\'s Overdrive Fury ends at once and casts this number of spells, instead of turning the stick against the clock.',
         'Lulus Overdrive \u201eRage\u201c endet sofort und wirkt diese Anzahl Zauber, statt gegen die Zeit den Stick zu drehen.',
         'L\'Overdrive Furie de Lulu se termine aussit\xf4t et lance ce nombre de sorts, au lieu de tourner le stick contre la montre.',
         'L\'Overdrive Furia di Lulu finisce subito e lancia questo numero di magie, invece di ruotare la levetta a tempo.',
         'El Turbo Furia de Lulu termina al instante y lanza este n\xfamero de hechizos, en vez de girar la palanca contrarreloj.',
         '\u30eb\u30fc\u30eb\u30fc\u306e\u30aa\u30fc\u30d0\u30fc\u30c9\u30e9\u30a4\u30d6\u304c\u3059\u3050\u306b\u7d42\u308f\u308a\u3001\u3053\u306e\u6570\u306e\u9b54\u6cd5\u3092\u5531\u3048\u307e\u3059\u3002\u6642\u9593\u5185\u306b\u30b9\u30c6\u30a3\u30c3\u30af\u3092\u56de\u3059\u5fc5\u8981\u306f\u3042\u308a\u307e\u305b\u3093\u3002',
         '\ub8e8\ub8e8\uc758 \uc624\ubc84\ub4dc\ub77c\uc774\ube0c\uac00 \ubc14\ub85c \ub05d\ub098\uace0 \uc774 \uc218\ub9cc\ud07c \ub9c8\ubc95\uc744 \uc2dc\uc804\ud569\ub2c8\ub2e4. \uc2dc\uac04 \uc548\uc5d0 \uc2a4\ud2f1\uc744 \ub3cc\ub9b4 \ud544\uc694\uac00 \uc5c6\uc2b5\ub2c8\ub2e4.',
         '\u7490\u7490\u7684\u6975\u9650\u6280\u7acb\u5373\u7d50\u675f\u4e26\u65bd\u653e\u6b64\u6578\u91cf\u7684\u9b54\u6cd5\uff0c\u4e0d\u5fc5\u5728\u6642\u9650\u5167\u8f49\u52d5\u6416\u687f\u3002')),
    ['0', '2', '3', '4', '5'], None)
opt('rows', 'QOL-BTLMENU', '0',
    T('Fast battle menu', 'Schnelles Kampfmen\xfc', 'Menu de combat rapide', 'Menu di battaglia rapido', 'Men\xfa de combate r\xe1pido',
      '\u30d0\u30c8\u30eb\u30e1\u30cb\u30e5\u30fc\u3092\u9ad8\u901f\u5316', '\ube60\ub978 \uc804\ud22c \uba54\ub274', '\u5feb\u901f\u6230\u9b25\u9078\u55ae'),
    T('Battle command windows open at once and take input immediately, without their opening animation.',
      'Die Befehlsfenster im Kampf \xf6ffnen sofort und nehmen Eingaben gleich an, ohne ihre \xd6ffnungsanimation.',
      'Les fen\xeatres de commande en combat s\'ouvrent aussit\xf4t et acceptent les touches tout de suite, sans animation d\'ouverture.',
      'Le finestre dei comandi in battaglia si aprono subito e accettano gli input immediatamente, senza animazione di apertura.',
      'Las ventanas de comandos en combate se abren al instante y aceptan pulsaciones enseguida, sin su animaci\xf3n de apertura.',
      '\u30d0\u30c8\u30eb\u306e\u30b3\u30de\u30f3\u30c9\u30a6\u30a3\u30f3\u30c9\u30a6\u304c\u958b\u304f\u30a2\u30cb\u30e1\u30fc\u30b7\u30e7\u30f3\u306a\u3057\u3067\u3059\u3050\u306b\u958b\u304d\u3001\u3059\u3050\u306b\u64cd\u4f5c\u3067\u304d\u307e\u3059\u3002',
      '\uc804\ud22c \ucee4\ub9e8\ub4dc \ucc3d\uc774 \uc5f4\ub9ac\ub294 \uc560\ub2c8\uba54\uc774\uc158 \uc5c6\uc774 \ubc14\ub85c \uc5f4\ub9ac\uace0 \uc785\ub825\uc744 \ubc1b\uc2b5\ub2c8\ub2e4.',
      '\u6230\u9b25\u6307\u4ee4\u8996\u7a97\u7acb\u5373\u958b\u555f\u4e26\u53ef\u99ac\u4e0a\u64cd\u4f5c\uff0c\u6c92\u6709\u958b\u555f\u52d5\u756b\u3002'))
opt('rows', 'QOL-SAVEICON', '0',
    T('No save chocobo', 'Kein Speicher-Chocobo', 'Pas de chocobo de sauvegarde', 'Nessun chocobo di salvataggio', 'Sin chocobo de guardado',
      '\u30bb\u30fc\u30d6\u4e2d\u306e\u30c1\u30e7\u30b3\u30dc\u3092\u8868\u793a\u3057\u306a\u3044', '\uc800\uc7a5 \ucd08\ucf54\ubcf4 \uc228\uae30\uae30', '\u4e0d\u986f\u793a\u5b58\u6a94\u9678\u884c\u9ce5'),
    T('No running chocobo animation in the corner while the game autosaves. The save itself still happens.',
      'Keine laufende Chocobo-Animation in der Ecke, w\xe4hrend das Spiel automatisch speichert. Gespeichert wird trotzdem.',
      'Pas de chocobo qui court dans le coin pendant la sauvegarde automatique. La sauvegarde a quand m\xeame lieu.',
      'Nessun chocobo che corre nell\'angolo durante il salvataggio automatico. Il salvataggio avviene comunque.',
      'Sin el chocobo que corre en la esquina durante el guardado autom\xe1tico. Se guarda igualmente.',
      '\u30aa\u30fc\u30c8\u30bb\u30fc\u30d6\u4e2d\u306b\u753b\u9762\u306e\u9685\u3067\u8d70\u308b\u30c1\u30e7\u30b3\u30dc\u3092\u8868\u793a\u3057\u307e\u305b\u3093\u3002\u30bb\u30fc\u30d6\u306f\u901a\u5e38\u3069\u304a\u308a\u884c\u308f\u308c\u307e\u3059\u3002',
      '\uc790\ub3d9 \uc800\uc7a5 \uc911 \uad6c\uc11d\uc5d0\uc11c \ub2ec\ub9ac\ub294 \ucd08\ucf54\ubcf4\ub97c \ud45c\uc2dc\ud558\uc9c0 \uc54a\uc2b5\ub2c8\ub2e4. \uc800\uc7a5\uc740 \uadf8\ub300\ub85c \ub429\ub2c8\ub2e4.',
      '\u81ea\u52d5\u5b58\u6a94\u6642\u4e0d\u5728\u89d2\u843d\u986f\u793a\u5954\u8dd1\u7684\u9678\u884c\u9ce5\u3002\u4ecd\u6703\u6b63\u5e38\u5b58\u6a94\u3002'))

# ------------------------------------------------------------------ FFX-2
header(game_header('FINAL FANTASY X-2', T('not Last Mission', 'nicht Last Mission', 'pas Last Mission', 'non Last Mission', 'no Last Mission',
                                           'Last Mission\u3092\u9664\u304f', 'Last Mission \uc81c\uc678', '\u4e0d\u542b Last Mission')))
opt('rows', 'X2-VSYNC', '0', VSYNC_L, vsync_help(True))
opt('rows', 'X2-SMOOTH', '1', SMOOTH_L, SMOOTH_H)
opt('rows', 'X2-MENU-002', '1',
    T('Main menu at 60 fps', 'Hauptmen\xfc mit 60 fps', 'Menu principal \xe0 60 fps', 'Menu principale a 60 fps', 'Men\xfa principal a 60 fps',
      '\u30e1\u30a4\u30f3\u30e1\u30cb\u30e5\u30fc\u309260 fps\u306b', '\uba54\uc778 \uba54\ub274 60 fps', '\u4e3b\u9078\u55ae 60 fps'),
    T('The main menu in the field runs at 60 fps (cursor, panels). The other menus stay at the original 30 fps. Off: the main menu runs at 30 fps too.',
      'Das Hauptmen\xfc im Feld l\xe4uft mit 60 fps (Cursor, Fenster). Die anderen Men\xfcs bleiben bei den originalen 30 fps. Aus: Auch das Hauptmen\xfc l\xe4uft mit 30 fps.',
      'Le menu principal sur le terrain tourne \xe0 60 fps (curseur, fen\xeatres). Les autres menus restent aux 30 fps d\'origine. D\xe9sactiv\xe9 : le menu principal tourne aussi \xe0 30 fps.',
      'Il menu principale sul campo va a 60 fps (cursore, finestre). Gli altri menu restano ai 30 fps originali. Disattivato: anche il menu principale va a 30 fps.',
      'El men\xfa principal en el campo va a 60 fps (cursor, ventanas). Los dem\xe1s men\xfas siguen a los 30 fps originales. Desactivado: el men\xfa principal tambi\xe9n va a 30 fps.',
      '\u30d5\u30a3\u30fc\u30eb\u30c9\u306e\u30e1\u30a4\u30f3\u30e1\u30cb\u30e5\u30fc\uff08\u30ab\u30fc\u30bd\u30eb\u3001\u30a6\u30a3\u30f3\u30c9\u30a6\uff09\u304c60 fps\u3067\u52d5\u304d\u307e\u3059\u3002\u307b\u304b\u306e\u30e1\u30cb\u30e5\u30fc\u306f\u5143\u306e30 fps\u3067\u3059\u3002\u30aa\u30d5\uff1a\u30e1\u30a4\u30f3\u30e1\u30cb\u30e5\u30fc\u308230 fps\u3067\u3059\u3002',
      '\ud544\ub4dc\uc758 \uba54\uc778 \uba54\ub274(\ucee4\uc11c, \ucc3d)\uac00 60 fps\ub85c \uc791\ub3d9\ud569\ub2c8\ub2e4. \ub2e4\ub978 \uba54\ub274\ub294 \uc6d0\ub798\uc758 30 fps\uc785\ub2c8\ub2e4. \ub044\uae30: \uba54\uc778 \uba54\ub274\ub3c4 30 fps\uc785\ub2c8\ub2e4.',
      '\u5730\u5716\u4e0a\u7684\u4e3b\u9078\u55ae\uff08\u6e38\u6a19\u3001\u8996\u7a97\uff09\u4ee5 60 fps \u904b\u4f5c\u3002\u5176\u4ed6\u9078\u55ae\u7dad\u6301\u539f\u59cb 30 fps\u3002\u95dc\u9589\uff1a\u4e3b\u9078\u55ae\u4e5f\u70ba 30 fps\u3002'))
def menu_part(us, de, fr, it, sp, jp, kr, ch):
    pre = T('With "Main menu at 60 fps": ', 'Mit \u201eHauptmen\xfc mit 60 fps\u201c: ', 'Avec \xab Menu principal \xe0 60 fps \xbb : ', 'Con "Menu principale a 60 fps": ',
            'Con "Men\xfa principal a 60 fps": ', '\u300c\u30e1\u30a4\u30f3\u30e1\u30cb\u30e5\u30fc\u309260 fps\u306b\u300d\u304c\u30aa\u30f3\u306e\u3068\u304d\u3001',
            '"\uba54\uc778 \uba54\ub274 60 fps"\uac00 \ucf1c\uc838 \uc788\uc744 \ub54c ', '\u958b\u555f\u300c\u4e3b\u9078\u55ae 60 fps\u300d\u6642\uff0c')
    return [pre[i] + t for i, t in enumerate([us, de, fr, it, sp, jp, kr, ch])]
opt('rows', 'X2-MENU-002-TWEEN', '1',
    T('Main menu: sliding windows', 'Hauptmen\xfc: gleitende Fenster', 'Menu principal : fen\xeatres glissantes', 'Menu principale: finestre scorrevoli', 'Men\xfa principal: ventanas deslizantes',
      '\u30e1\u30a4\u30f3\u30e1\u30cb\u30e5\u30fc\uff1a\u30a6\u30a3\u30f3\u30c9\u30a6\u306e\u52d5\u304d', '\uba54\uc778 \uba54\ub274: \ucc3d \uc2ac\ub77c\uc774\ub4dc', '\u4e3b\u9078\u55ae\uff1a\u8996\u7a97\u6ed1\u52d5'),
    menu_part('the windows and command lists slide in smoothly on every frame. Off: they move in 30 steps per second.',
              'Fenster und Befehlslisten gleiten in jedem Bild fl\xfcssig herein. Aus: Sie bewegen sich in 30 Schritten pro Sekunde.',
              'les fen\xeatres et listes de commandes glissent en douceur \xe0 chaque image. D\xe9sactiv\xe9 : elles bougent en 30 pas par seconde.',
              'finestre ed elenchi dei comandi scorrono fluidi a ogni fotogramma. Disattivato: si muovono a 30 passi al secondo.',
              'las ventanas y listas de comandos se deslizan con suavidad en cada fotograma. Desactivado: se mueven a 30 pasos por segundo.',
              '\u30a6\u30a3\u30f3\u30c9\u30a6\u3084\u30b3\u30de\u30f3\u30c9\u30ea\u30b9\u30c8\u304c\u6bce\u30d5\u30ec\u30fc\u30e0\u6ed1\u3089\u304b\u306b\u52d5\u304d\u307e\u3059\u3002\u30aa\u30d5\uff1a1\u79d230\u56de\u306e\u52d5\u304d\u3067\u3059\u3002',
              '\ucc3d\uacfc \ucee4\ub9e8\ub4dc \ubaa9\ub85d\uc774 \ub9e4 \ud504\ub808\uc784 \ubd80\ub4dc\ub7fd\uac8c \ubbf8\ub044\ub7ec\uc9d1\ub2c8\ub2e4. \ub044\uae30: \ucd08\ub2f9 30\ubc88 \uc6c0\uc9c1\uc785\ub2c8\ub2e4.',
              '\u8996\u7a97\u8207\u6307\u4ee4\u6e05\u55ae\u6bcf\u500b\u756b\u683c\u90fd\u6d41\u66a2\u6ed1\u5165\u3002\u95dc\u9589\uff1a\u6bcf\u79d2 30 \u6b65\u3002'))
opt('rows', 'X2-MENU-002-DRAWREPLAY', '1',
    T('Main menu: window shadows', 'Hauptmen\xfc: Fensterschatten', 'Menu principal : ombres des fen\xeatres', 'Menu principale: ombre delle finestre', 'Men\xfa principal: sombras',
      '\u30e1\u30a4\u30f3\u30e1\u30cb\u30e5\u30fc\uff1a\u30a6\u30a3\u30f3\u30c9\u30a6\u306e\u5f71', '\uba54\uc778 \uba54\ub274: \ucc3d \uadf8\ub9bc\uc790', '\u4e3b\u9078\u55ae\uff1a\u8996\u7a97\u9670\u5f71'),
    menu_part('the shadows of the party windows are drawn on every frame. Off: they can flicker.',
              'Die Schatten der Gruppenfenster werden in jedem Bild gezeichnet. Aus: Sie k\xf6nnen flackern.',
              'les ombres des fen\xeatres de l\'\xe9quipe sont dessin\xe9es \xe0 chaque image. D\xe9sactiv\xe9 : elles peuvent clignoter.',
              'le ombre delle finestre del gruppo vengono disegnate a ogni fotogramma. Disattivato: possono sfarfallare.',
              'las sombras de las ventanas del grupo se dibujan en cada fotograma. Desactivado: pueden parpadear.',
              '\u30d1\u30fc\u30c6\u30a3\u30fc\u30a6\u30a3\u30f3\u30c9\u30a6\u306e\u5f71\u3092\u6bce\u30d5\u30ec\u30fc\u30e0\u63cf\u304d\u307e\u3059\u3002\u30aa\u30d5\uff1a\u3061\u3089\u3064\u304f\u3053\u3068\u304c\u3042\u308a\u307e\u3059\u3002',
              '\ud30c\ud2f0 \ucc3d\uc758 \uadf8\ub9bc\uc790\ub97c \ub9e4 \ud504\ub808\uc784 \uadf8\ub9bd\ub2c8\ub2e4. \ub044\uae30: \uae5c\ube61\uc77c \uc218 \uc788\uc2b5\ub2c8\ub2e4.',
              '\u6bcf\u500b\u756b\u683c\u90fd\u7e6a\u88fd\u968a\u4f0d\u8996\u7a97\u7684\u9670\u5f71\u3002\u95dc\u9589\uff1a\u53ef\u80fd\u6703\u9583\u720d\u3002'))
opt('rows', 'X2-MENU-002-POINT', '1',
    T('Main menu: cursor', 'Hauptmen\xfc: Zeiger', 'Menu principal : curseur', 'Menu principale: cursore', 'Men\xfa principal: cursor',
      '\u30e1\u30a4\u30f3\u30e1\u30cb\u30e5\u30fc\uff1a\u30ab\u30fc\u30bd\u30eb', '\uba54\uc778 \uba54\ub274: \ucee4\uc11c', '\u4e3b\u9078\u55ae\uff1a\u6e38\u6a19'),
    menu_part('the selection pointer moves smoothly on every frame. Off: it moves in 30 steps per second.',
              'Der Auswahlzeiger bewegt sich in jedem Bild fl\xfcssig. Aus: Er bewegt sich in 30 Schritten pro Sekunde.',
              'le pointeur de s\xe9lection bouge en douceur \xe0 chaque image. D\xe9sactiv\xe9 : il bouge en 30 pas par seconde.',
              'il puntatore di selezione si muove fluido a ogni fotogramma. Disattivato: si muove a 30 passi al secondo.',
              'el puntero de selecci\xf3n se mueve con suavidad en cada fotograma. Desactivado: se mueve a 30 pasos por segundo.',
              '\u9078\u629e\u30ab\u30fc\u30bd\u30eb\u304c\u6bce\u30d5\u30ec\u30fc\u30e0\u6ed1\u3089\u304b\u306b\u52d5\u304d\u307e\u3059\u3002\u30aa\u30d5\uff1a1\u79d230\u56de\u306e\u52d5\u304d\u3067\u3059\u3002',
              '\uc120\ud0dd \ucee4\uc11c\uac00 \ub9e4 \ud504\ub808\uc784 \ubd80\ub4dc\ub7fd\uac8c \uc6c0\uc9c1\uc785\ub2c8\ub2e4. \ub044\uae30: \ucd08\ub2f9 30\ubc88 \uc6c0\uc9c1\uc785\ub2c8\ub2e4.',
              '\u9078\u64c7\u6e38\u6a19\u6bcf\u500b\u756b\u683c\u90fd\u6d41\u66a2\u79fb\u52d5\u3002\u95dc\u9589\uff1a\u6bcf\u79d2 30 \u6b65\u3002'))
opt('rows', 'X2-MENU-002-BACKDROP', '1',
    T('Main menu: background', 'Hauptmen\xfc: Hintergrund', 'Menu principal : arri\xe8re-plan', 'Menu principale: sfondo', 'Men\xfa principal: fondo',
      '\u30e1\u30a4\u30f3\u30e1\u30cb\u30e5\u30fc\uff1a\u80cc\u666f', '\uba54\uc778 \uba54\ub274: \ubc30\uacbd', '\u4e3b\u9078\u55ae\uff1a\u80cc\u666f'),
    menu_part('the darkened background fades in and out smoothly on every frame. Off: it fades in 30 steps per second.',
              'Der abgedunkelte Hintergrund blendet in jedem Bild fl\xfcssig ein und aus. Aus: in 30 Schritten pro Sekunde.',
              'l\'arri\xe8re-plan assombri appara\xeet et dispara\xeet en douceur \xe0 chaque image. D\xe9sactiv\xe9 : en 30 pas par seconde.',
              'lo sfondo scurito appare e scompare fluido a ogni fotogramma. Disattivato: a 30 passi al secondo.',
              'el fondo oscurecido aparece y desaparece con suavidad en cada fotograma. Desactivado: a 30 pasos por segundo.',
              '\u6697\u304f\u306a\u308b\u80cc\u666f\u304c\u6bce\u30d5\u30ec\u30fc\u30e0\u6ed1\u3089\u304b\u306b\u5207\u308a\u66ff\u308f\u308a\u307e\u3059\u3002\u30aa\u30d5\uff1a1\u79d230\u56de\u306e\u5909\u5316\u3067\u3059\u3002',
              '\uc5b4\ub450\uc6cc\uc9c0\ub294 \ubc30\uacbd\uc774 \ub9e4 \ud504\ub808\uc784 \ubd80\ub4dc\ub7fd\uac8c \ub098\ud0c0\ub098\uace0 \uc0ac\ub77c\uc9d1\ub2c8\ub2e4. \ub044\uae30: \ucd08\ub2f9 30\ubc88 \ubc14\ub01d\ub2c8\ub2e4.',
              '\u8b8a\u6697\u7684\u80cc\u666f\u6bcf\u500b\u756b\u683c\u90fd\u6d41\u66a2\u6de1\u5165\u6de1\u51fa\u3002\u95dc\u9589\uff1a\u6bcf\u79d2 30 \u6b65\u3002'))
opt('rows', 'X2-SR-003', '0', SR_L, sr_help(True))
opt('rows', 'X2-DEV-TURBO', '0', TURBO_L, TURBO_H)
opt('rows', 'X2-DEV-TURBO-KEY', '96', TKEY_L, TKEY_H, [k[0] for k in KEYS], [[k[1] for k in KEYS]] * 8)
opt('rows', 'X2-DEV-TURBO-SPEED', '10', TSPD_L, TSPD_H, [s[0] for s in SPEED], [[s[1] for s in SPEED]] * 8)

# ------------------------------------------------------------------ C# output
def cs(s):
    out = ''
    for ch in s:
        o = ord(ch)
        if ch == '"': out += '\\"'
        elif ch == '\\': out += '\\\\'
        elif 32 <= o < 127: out += ch
        else: out += '\\u%04x' % o
    return '"' + out + '"'
def arr(a): return 'new[] { ' + ', '.join(cs(x) for x in a) + ' }'
for it in ITEMS:
    for k in ('label', 'help'):
        if k in it: assert len(it[k]) == 8, (it.get('key'), k)
    if it['kind'] != H and it['texts']: assert len(it['texts']) in (2, 4, 5, 8) or True
lines = ['// generated by gen_advanced.py - do not edit by hand. The launcher\'s "Advanced settings" (ffx_enh.ini) with their',
         '// texts in the games\' eight languages (us de fr it sp jp kr ch); SettingsView.Advanced() builds the rows from this table.',
         'namespace FFXLauncher', '{', '    sealed class AdvItem',
         '    {',
         '        public bool Header; public string Sec, Key, Def; public string[] Values; public string[][] Texts; public string[] Label, Help;',
         '    }', '', '    static class AdvancedText', '    {',
         '        public static readonly string[] Langs = ' + arr(L) + ';',
         '        // Texts[value][language]; null = the game\'s own On / Off texts ("1" / "0"), or the value itself',
         '        public static readonly AdvItem[] Items =', '        {']
for it in ITEMS:
    if it['kind'] == H:
        lines.append('            new AdvItem { Header = true, Label = %s },' % arr(it['label']))
        continue
    texts = 'null'
    if it['texts']:
        if it['key'] == 'pacing': per_val = it['texts']                 # [value][lang]
        else: per_val = [[it['texts'][0][v]] * 8 for v in range(len(it['values']))]
        texts = 'new[] { ' + ', '.join(arr(pv) for pv in per_val) + ' }'
    lines.append('            new AdvItem { Sec = %s, Key = %s, Def = %s, Values = %s, Texts = %s,' % (cs(it['sec']), cs(it['key']), cs(it['default']), arr(it['values']), texts))
    lines.append('                Label = %s,' % arr(it['label']))
    lines.append('                Help = %s },' % arr(it['help']))
lines += ['        };', '    }', '}', '']
sys.stdout.write('\r\n'.join(lines))
