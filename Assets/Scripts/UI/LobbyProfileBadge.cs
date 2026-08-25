using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Your own picture and name, on the lobby page, as a way in to the Profile page.
//
// The profile already existed — it is what gets sent to the other player when a match forms — but
// nothing showed it back to you outside the Profile page itself. So the one screen you sit on
// between matches gave no sign of who you were playing as, and changing it meant remembering the
// Profile tab was where that lived.
public class LobbyProfileBadge : MonoBehaviour
{
    [Tooltip("The picture. Gets a Button so tapping it opens the Profile page.")]
    public Image avatarImage;

    [Tooltip("The label that shows your name.")]
    public TMP_Text nameLabel;

    [Tooltip("Used to open the Profile page when the picture is tapped.")]
    public LobbyPageSwitcher pageSwitcher;

    [Tooltip("Shown before a name has been chosen.")]
    public string placeholderName = "Toque para editar";

    private Button avatarButton;

    private void Awake()
    {
        EnsureButton();
    }

    // Every time the lobby page comes back, not once at startup. Coming back from the Profile page
    // is exactly when the name or picture has just changed, and it is the only route by which they
    // can change at all.
    private void OnEnable()
    {
        Refresh();
    }

    public void Refresh()
    {
        PlayerProfileManager profile = PlayerProfileManager.Instance;

        if (profile == null)
            return;

        if (nameLabel != null)
        {
            string playerName = profile.GetLocalName();

            // The default means nobody has chosen yet — worth saying so, since the whole point of
            // this badge is to be the way in to changing it.
            nameLabel.text = string.IsNullOrWhiteSpace(playerName) || playerName == PlayerProfileManager.DefaultName
                ? placeholderName
                : playerName;
        }

        if (avatarImage == null)
            return;

        Sprite avatar = profile.GetAvatarSprite(profile.GetLocalAvatarIndex());

        if (avatar != null)
        {
            avatarImage.sprite = avatar;
            avatarImage.color = Color.white;
            avatarImage.preserveAspect = true;
        }
    }

    private void EnsureButton()
    {
        if (avatarImage == null)
            return;

        // The picture has to be a raycast target to be tappable at all. An Image dragged in as
        // decoration often is not, and that failure is silent — the button exists and nothing ever
        // reaches it.
        avatarImage.raycastTarget = true;

        avatarButton = avatarImage.GetComponent<Button>();

        // Explicit null check rather than ??: GetComponent hands back a fake null that ?? treats
        // as a real object, which would leave the component unadded and the tap dead.
        if (avatarButton == null)
            avatarButton = avatarImage.gameObject.AddComponent<Button>();

        avatarButton.targetGraphic = avatarImage;

        // No colour tint. Tinting a photograph grey on press looks like a rendering fault rather
        // than feedback, and it is the same lingering-selection problem ButtonSelectionTint exists
        // to clear up.
        avatarButton.transition = Selectable.Transition.None;

        avatarButton.onClick.RemoveListener(OpenProfile);
        avatarButton.onClick.AddListener(OpenProfile);
    }

    public void OpenProfile()
    {
        LobbyPageSwitcher switcher = pageSwitcher != null ? pageSwitcher : FindAnyObjectByType<LobbyPageSwitcher>();

        if (switcher != null)
            switcher.ShowProfilePage();
    }
}
