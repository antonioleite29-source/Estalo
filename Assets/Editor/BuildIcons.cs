using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

// Assigns the app icons for both stores from the artwork in Assets/Art/Logo.
//
// Done in code rather than by dragging into Player Settings because there are three Android icon
// kinds and several iOS slots, and a half-filled set is invisible until a build comes out with
// Unity's default icon on it.
public static class BuildIcons
{
    private const string Square = "Assets/Art/Logo/EstaloLogo.png";
    private const string Round  = "Assets/Art/Logo/EstaloLogoCirculo.png";
    private const string IOS    = "Assets/Art/Logo/EstaloLogo-iOS-1024.png";

    [MenuItem("Trivia Duel/Setup/Apply App Icons")]
    public static void ApplyIcons()
    {
        Texture2D square = AssetDatabase.LoadAssetAtPath<Texture2D>(Square);
        Texture2D round  = AssetDatabase.LoadAssetAtPath<Texture2D>(Round);
        Texture2D ios    = AssetDatabase.LoadAssetAtPath<Texture2D>(IOS);

        if (square == null || round == null || ios == null)
        {
            Debug.LogError("BuildIcons: missing artwork. Expected " + Square + ", " + Round +
                           " and " + IOS + ".");
            return;
        }

        // Adaptive is the modern one: the launcher masks it into whatever shape it likes, which on
        // a Pixel is a circle.
        SetAndroid(AndroidPlatformIconKind.Adaptive, square);

        // Round and Legacy are deprecated in favour of Adaptive, and still used here on purpose.
        // Adaptive hands the launcher a square and lets it crop; Round hands it the circular
        // artwork as drawn. Since a circular version exists and was made deliberately, shipping it
        // beats letting a mask approximate it — and Legacy still covers launchers that only ever
        // ask for a plain square. Warning silenced rather than worked around: this is a choice,
        // not an oversight, and it should read as one.
#pragma warning disable 618
        SetAndroid(AndroidPlatformIconKind.Round, round);
        SetAndroid(AndroidPlatformIconKind.Legacy, square);
#pragma warning restore 618

        // Every iOS slot gets the opaque version. Apple rejects an icon with an alpha channel, and
        // the square artwork has transparent corners — see EstaloLogo-iOS-1024.png.
        NamedBuildTarget iosTarget = NamedBuildTarget.iOS;
        foreach (PlatformIconKind kind in PlayerSettings.GetSupportedIconKinds(iosTarget))
        {
            PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(iosTarget, kind);

            for (int i = 0; i < icons.Length; i++)
                icons[i].SetTexture(ios, 0);

            PlayerSettings.SetPlatformIcons(iosTarget, kind, icons);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("App icons applied: round = EstaloLogoCirculo (Pixel and stock Android), " +
                  "square = EstaloLogo (legacy + adaptive), iOS = EstaloLogo-iOS-1024 (opaque). " +
                  "Now do File > Save Project.");
    }

    private static void SetAndroid(PlatformIconKind kind, Texture2D texture)
    {
        NamedBuildTarget target = NamedBuildTarget.Android;
        PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(target, kind);

        for (int i = 0; i < icons.Length; i++)
            icons[i].SetTexture(texture, 0);

        PlayerSettings.SetPlatformIcons(target, kind, icons);
    }
}
