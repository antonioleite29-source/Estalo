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
    // The chosen mode fades back; the one you have not chosen stays at full strength. That reads
    // as the selected button being pressed into the page rather than lit up on top of it, which is
    // the opposite of the usual convention and is what was asked for.
    //
    // Alpha rather than a darker colour, so it works over whatever the button art happens to be
    // instead of only over the flat fill it has today.
    private static readonly Color SelectedMode = new Color(1f, 1f, 1f, 0.5f);
    private static readonly Color UnselectedMode = Color.white;

    [MenuItem("Trivia Duel/Setup/Wire Lobby Profile Badge")]
    public static void Wire()
    {
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

        Image avatar = FindAvatar(page);
        TMP_Text nameLabel = FindNameLabel(page);

        if (avatar == null || nameLabel == null)
        {
            Debug.LogError("LobbyBadgeBuilder: could not find both an Image and a label directly " +
                           "under " + page.name + ". Add the LobbyProfileBadge component yourself " +
                           "and drag them into its slots.", switcher.lobbyPage);
            return;
        }

        LobbyProfileBadge badge = switcher.lobbyPage.GetComponent<LobbyProfileBadge>();

        if (badge == null)
            badge = Undo.AddComponent<LobbyProfileBadge>(switcher.lobbyPage);

        Undo.RecordObject(badge, "Wire lobby profile badge");
        badge.avatarImage = avatar;
        badge.nameLabel = nameLabel;
        badge.pageSwitcher = switcher;
        EditorUtility.SetDirty(badge);

        // The picture has to accept a tap. An Image dropped in as decoration usually does not, and
        // nothing reports that -- the Button is simply never reached.
        Undo.RecordObject(avatar, "Make avatar tappable");
        avatar.raycastTarget = true;
        EditorUtility.SetDirty(avatar);

        Undo.RecordObject(switcher, "Fix mode colours");
        switcher.selectedModeColor = SelectedMode;
        switcher.unselectedModeColor = UnselectedMode;
        EditorUtility.SetDirty(switcher);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log($"LobbyBadgeBuilder: picture = '{avatar.name}', name label = '{nameLabel.name}'. " +
                  "Selected mode is now white, unselected dimmed. Save the scene to keep it.", badge);
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
