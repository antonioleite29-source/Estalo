using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Wires the picture and name that were dropped onto the lobby page, and repairs the mode colours.
//
// Done through the Editor rather than by hand-editing the scene file, so Unity does the
// serialisation. Everything it picks is left in an Inspector slot afterwards, which means a wrong
// guess is a drag-and-drop away from being right rather than a reason to run this again.
public static class LobbyBadgeBuilder
{
    // The chosen mode is see-through; the one you have not chosen is solid. It reads as the
    // selected button being pressed into the page rather than raised off it.
    //
    // Alpha rather than a second colour, so the difference survives whatever artwork ends up on
    // those buttons -- 1v1Button.png is already sitting in the project waiting to go on. A grey
    // would have to be re-picked to suit every new sprite; transparency never does.
    private static readonly Color SelectedMode = new Color(1f, 1f, 1f, 0.675f);
    private static readonly Color UnselectedMode = Color.white;

    // #80B3C8, the same blue as localPlayerOutlineColor -- the ring that marks which avatar is
    // yours. Using the one already in the project rather than a second, nearly identical blue
    // means the Play button and "this side is yours" are visibly the same idea.
    private static readonly Color PlayLabel = new Color32(128, 179, 200, 255);

    [MenuItem("Trivia Duel/Setup/Wire Lobby Profile Badge")]
    public static void Wire()
    {
        // Nothing scene-shaped survives Play mode: Unity discards every change on exit, so a tool
        // run now reports success and leaves no trace. Worse, it reads the RUNNING values --
        // TextFitsItsBox has already shrunk labels to fit by then, so a font authored at 100pt
        // measures 41 and any box sized from it comes out wrong as well as unsaved.
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("LobbyBadgeBuilder: stop Play mode first. Changes made while playing are thrown " +
                           "away, and the values read are the running ones rather than the real ones.");
            return;
        }

        LobbyPageSwitcher switcher = Object.FindAnyObjectByType<LobbyPageSwitcher>(FindObjectsInactive.Include);

        if (switcher == null)
        {
            Debug.LogError("LobbyBadgeBuilder: no LobbyPageSwitcher in the scene.");
            return;
        }

        if (switcher.lobbyPage == null)
        {
            Debug.LogError("LobbyBadgeBuilder: LobbyPageSwitcher has no Lobby Page assigned.");
            return;
        }

        Transform page = switcher.lobbyPage.transform;

        // The parts that do not depend on each other are applied first, so a badge that cannot be
        // found stops being a reason for the colours to go unset too.
        ColourPlayLabel(page);

        Undo.RecordObject(switcher, "Fix mode colours");
        switcher.selectedModeColor = SelectedMode;
        switcher.unselectedModeColor = UnselectedMode;
        EditorUtility.SetDirty(switcher);

        // Both mode buttons are set to Color Tint on the very Image the switcher colours, so
        // Unity's Selectable rewrote that colour on every press and release and put its own white
        // back a frame later. LobbyPageSwitcher also does this at runtime; doing it here as well
        // means the Inspector tells the truth about who owns that colour.
        ReleaseTint(switcher.oneVsOneButtonImage);
        ReleaseTint(switcher.teamFourButtonImage);

        LobbyProfileBadge badge = switcher.lobbyPage.GetComponent<LobbyProfileBadge>();

        // Whatever it was wired to last time wins over searching again. The search skips Images
        // carrying a Selectable, and the badge puts a Button on the avatar the first time it runs
        // -- so without this, running this menu item twice fails on its own previous success.
        Image avatar = badge != null && badge.avatarImage != null ? badge.avatarImage : FindAvatar(page);
        TMP_Text nameLabel = badge != null && badge.nameLabel != null ? badge.nameLabel : FindNameLabel(page);

        if (avatar == null || nameLabel == null)
        {
            Debug.LogWarning("LobbyBadgeBuilder: could not find both an Image and a label directly " +
                             "under " + page.name + ". The colours were still applied; add the " +
                             "LobbyProfileBadge component yourself and drag them into its slots.",
                             switcher.lobbyPage);
        }
        else
        {
            if (badge == null)
                badge = Undo.AddComponent<LobbyProfileBadge>(switcher.lobbyPage);

            Undo.RecordObject(badge, "Wire lobby profile badge");
            badge.avatarImage = avatar;
            badge.nameLabel = nameLabel;
            badge.pageSwitcher = switcher;
            EditorUtility.SetDirty(badge);

            // The picture has to accept a tap. An Image dropped in as decoration usually does not,
            // and nothing reports that -- the Button is simply never reached.
            Undo.RecordObject(avatar, "Make avatar tappable");
            avatar.raycastTarget = true;
            EditorUtility.SetDirty(avatar);

            Debug.Log($"LobbyBadgeBuilder: picture = '{avatar.name}', name label = '{nameLabel.name}'.", badge);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log("LobbyBadgeBuilder: mode colours and the Jogar label applied. Save the scene to keep it.");
    }

    // Found by what it says rather than by which object it sits on: the Start button is called
    // "Button" in the hierarchy, which would match several things, and the word on it is the one
    // unambiguous fact about it.
    private static void ColourPlayLabel(Transform page)
    {
        foreach (TMP_Text label in page.GetComponentsInChildren<TMP_Text>(true))
        {
            string written = label.text.Trim();

            if (written != "Jogar" && written != "Start")
                continue;

            Undo.RecordObject(label, "Colour the Jogar label");
            label.color = PlayLabel;
            EditorUtility.SetDirty(label);
            return;
        }

        Debug.LogWarning("LobbyBadgeBuilder: no label reading 'Jogar' on the lobby page, so its " +
                         "colour was left alone.");
    }

    private static void ReleaseTint(Image buttonImage)
    {
        if (buttonImage == null)
            return;

        Button button = buttonImage.GetComponent<Button>();

        if (button == null || button.transition == Selectable.Transition.None)
            return;

        Undo.RecordObject(button, "Release mode button tint");
        button.transition = Selectable.Transition.None;
        EditorUtility.SetDirty(button);
    }

    // The picture, by being an Image that is a direct child of the page and is not one of the
    // controls already known to live there.
    private static Image FindAvatar(Transform page)
    {
        Image best = null;

        foreach (Transform child in page)
        {
            Image image = child.GetComponent<Image>();

            if (image == null || child.GetComponent<Selectable>() != null)
                continue;

            // Later children win: the badge was added last, and the slider's background came with
            // the page long before it.
            best = image;
        }

        return best;
    }

    // The name, by being the last label on the page. The earlier ones are the title, the IQ
    // readout and the level -- all of which were there before the badge was dropped in.
    private static TMP_Text FindNameLabel(Transform page)
    {
        TMP_Text best = null;

        foreach (Transform child in page)
        {
            TMP_Text text = child.GetComponent<TMP_Text>();

            if (text != null)
                best = text;
        }

        return best;
    }
}
