using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Puts the Profile page's account button and the login panel into the scene as real objects, so they
// can be moved, resized and restyled in the editor before pressing Play. Both are styled like the
// "Jogar" button when made; after that, whatever you change on them is kept.
//
// Safe to run again: anything already wired on ProfilePageController is left alone.
[InitializeOnLoad]
public static class LoginUiBuilder
{
    private const string WrapFixedKey = "EstaloLoginTextWrapFixed";
    private const string CenteredKey = "EstaloLoginPanelCentered";

    // Panels built before the labels were set to wrap were saved as No Wrap, so a sentence that did
    // not fit on one line was cut off. Fixed once, automatically, in the open scene -- save it after.
    static LoginUiBuilder()
    {
        if (!EditorPrefs.GetBool(WrapFixedKey, false))
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && MakeLoginTextWrap())
                    EditorPrefs.SetBool(WrapFixedKey, true);
            };
    }

    [InitializeOnLoadMethod]
    private static void CenterOnce()
    {
        if (EditorPrefs.GetBool(CenteredKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && CenterLoginPanel())
                EditorPrefs.SetBool(CenteredKey, true);
        };
    }

    // Moves the panel's contents so that, from the title down to "Fechar", they sit centred in the
    // space ABOVE the bottom bar rather than centred on the whole screen, which left them low. The
    // status line is left out of the measurement -- it is usually empty -- but moves with the rest.
    [MenuItem("Trivia Duel/Setup/Center Login Panel")]
    private static bool CenterLoginPanel()
    {
        GameObject barObject = GameObject.Find("Canvas/UIRoot/BottomBar");
        RectTransform bar = barObject != null ? barObject.transform as RectTransform : null;
        bool moved = false;

        foreach (LoginPanel panel in Object.FindObjectsByType<LoginPanel>(FindObjectsInactive.Include))
        {
            RectTransform area = (RectTransform)panel.transform;
            Vector3[] corners = new Vector3[4];

            area.GetWorldCorners(corners);
            float top = area.InverseTransformPoint(corners[1]).y;
            float bottom = area.InverseTransformPoint(corners[0]).y;

            if (bar != null)
            {
                bar.GetWorldCorners(corners);
                bottom = Mathf.Max(bottom, area.InverseTransformPoint(corners[1]).y);
            }

            float contentTop = float.MinValue, contentBottom = float.MaxValue;

            foreach (RectTransform child in area)
            {
                if (panel.statusText != null && child == panel.statusText.transform)
                    continue;

                child.GetWorldCorners(corners);
                contentTop = Mathf.Max(contentTop, area.InverseTransformPoint(corners[1]).y);
                contentBottom = Mathf.Min(contentBottom, area.InverseTransformPoint(corners[0]).y);
            }

            if (contentTop == float.MinValue)
                continue;

            float shift = (top + bottom) * 0.5f - (contentTop + contentBottom) * 0.5f;

            foreach (RectTransform child in area)
            {
                Undo.RecordObject(child, "Center login panel");
                child.anchoredPosition += new Vector2(0f, shift);
            }

            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            Debug.Log($"LoginUiBuilder: moved the login panel up {shift:0} to centre it above the bottom bar. Save the scene.", panel);
            moved = true;
        }

        return moved;
    }

    [MenuItem("Trivia Duel/Setup/Make Login Text Wrap")]
    private static bool MakeLoginTextWrap()
    {
        bool found = false;

        foreach (LoginPanel panel in Object.FindObjectsByType<LoginPanel>(FindObjectsInactive.Include))
        {
            foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                // Input fields scroll their own text sideways; leave those alone.
                if (text.GetComponentInParent<TMP_InputField>(true) != null)
                    continue;

                Undo.RecordObject(text, "Make login text wrap");
                text.textWrappingMode = TextWrappingModes.Normal;
                text.overflowMode = TextOverflowModes.Overflow;
                EditorUtility.SetDirty(text);
            }

            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            Debug.Log("LoginUiBuilder: the login panel's text now wraps onto more lines. Save the scene.", panel);
            found = true;
        }

        return found;
    }

    [MenuItem("Trivia Duel/Setup/Build Login Buttons")]
    private static void Build()
    {
        ProfilePageController profile =
            Object.FindAnyObjectByType<ProfilePageController>(FindObjectsInactive.Include);

        if (profile == null)
        {
            Debug.LogError("LoginUiBuilder: no ProfilePageController in the open scene.");
            return;
        }

        TMP_Text heading = profile.GetComponentInChildren<TMP_Text>(true);
        TMP_FontAsset font = heading != null ? heading.font : null;

        Undo.RecordObject(profile, "Build login buttons");

        if (profile.accountButton == null)
        {
            profile.accountButton = ProfilePageController.CreateAccountButton(profile.transform, font);
            Undo.RegisterCreatedObjectUndo(profile.accountButton.gameObject, "Build login buttons");
        }

        if (profile.loginPanel == null)
        {
            profile.loginPanel = LoginPanel.CreateUnder(profile.transform, font);
            Undo.RegisterCreatedObjectUndo(profile.loginPanel.gameObject, "Build login buttons");
        }

        EditorUtility.SetDirty(profile);
        EditorSceneManager.MarkSceneDirty(profile.gameObject.scene);
        Selection.activeGameObject = profile.loginPanel.gameObject;

        Debug.Log("LoginUiBuilder: AccountButton and LoginPanel are on the Profile page. LoginPanel starts " +
                  "switched off -- tick it to edit it, untick it again, then save the scene.", profile);
    }
}
