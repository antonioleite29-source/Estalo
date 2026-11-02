using System.Collections.Generic;
using System.Globalization;
using System.Text;

// What a player is allowed to call themselves.
//
// The name a player types is shown to strangers -- on the duel screen, and on the leaderboard -- and
// Estalo is rated 13+, so it cannot be anything at all. Two rules: a length cap, and a list of words
// that are not allowed through. Nothing else is moderated, which is the point: with no chat and no
// free text beyond this one field, the name is the only thing a player can be unpleasant with.
//
// Checked on the phone, so the player is told why, AND on the server, which is the copy that counts:
// the name arrives there from the phone and a changed app could send anything.
//
// The matching is deliberately dim-witted about the tricks people use. It strips accents, turns the
// usual digits and symbols back into letters (0 -> o, 3 -> e, @ -> a, $ -> s), drops everything that
// is not a letter, and flattens runs of the same letter, so "P.U.T.4" and "pppuuta" fail the same way
// "puta" does. It will never catch everything, and that is fine -- it is a speed bump, not a censor.
public static class DisplayNames
{
    // Brawl Stars allows 15; so does this.
    public const int MaxLength = 15;
    public const int MinLength = 2;

    // Not allowed anywhere inside the name. Long enough that they do not turn up inside innocent words.
    private static readonly string[] Blocked =
    {
        "caralho", "caraio", "porra", "merda", "buceta", "boceta", "piroca", "cacete", "foder",
        "fodase", "fodeu", "vagabunda", "vadia", "viadinho", "bichinha", "arrombado", "desgracado",
        "filhadaputa", "fdp", "pqp", "vtnc", "tomanocu", "escroto", "babaca", "otario", "corno",
        "pornô", "porno", "pornhub", "xvideos", "estupro", "estuprador", "pedofilo", "pedofilia",
        "suicidio", "sexo", "transar", "punheta", "siririca",
        "fuck", "shit", "bitch", "cunt", "pussy", "asshole", "nigg", "faggot", "whore", "slut",
        "rape", "nazi", "hitler", "kkk", "retard",
    };

    // Only when they are the whole name, or a whole word in it. Short enough to live inside ordinary
    // words -- "cu" is in "curto", "puta" is in "disputa" -- so a substring match would block those.
    private static readonly string[] BlockedWords =
    {
        "cu", "puta", "putas", "puto", "bosta", "caga", "cagar", "xoxota", "penis", "vagina", "anus",
        "sex", "dick", "cock", "kys", "viado", "viados", "carai", "crl",
        // Nobody gets to look like the game or its staff.
        "admin", "adm", "moderador", "suporte", "estalo", "estalooficial",
    };

    // The name as it will be shown: trimmed, one space between words, no invisible characters, and
    // never longer than the cap. A name that is not allowed comes back as the default, so whatever
    // calls this always ends up with something it can draw.
    public static string Clean(string raw)
    {
        string tidy = Tidy(raw);

        if (tidy.Length < MinLength || IsBlocked(tidy))
            return PlayerProfileManager.DefaultName;

        return tidy;
    }

    // For the field the player types in: true when the name may be used, and when not, why not, in
    // words that can go straight on screen.
    public static bool IsAllowed(string raw, out string reason)
    {
        string tidy = Tidy(raw);

        if (tidy.Length < MinLength)
        {
            reason = $"O nome precisa de pelo menos {MinLength} letras.";
            return false;
        }

        if (IsBlocked(tidy))
        {
            reason = "Esse nome não é permitido. Escolha outro.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    // True when the player typed something longer than the cap, so the field can say so rather than
    // quietly cutting it short.
    public static bool WasShortened(string raw) => Tidy(raw).Length < (raw ?? string.Empty).Trim().Length;

    // --- the rules ------------------------------------------------------

    private static string Tidy(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        StringBuilder builder = new StringBuilder(raw.Length);
        bool lastWasSpace = false;

        foreach (char c in raw.Trim())
        {
            // Zero-width and direction marks: invisible on screen, and used to slip words past a
            // filter and to break the layout of whatever draws the name.
            if (char.IsControl(c) || c == '​' || c == '‌' || c == '‍' || c == '‮' ||
                c == '‎' || c == '‏' || c == '﻿')
                continue;

            if (char.IsWhiteSpace(c))
            {
                if (lastWasSpace || builder.Length == 0)
                    continue;

                builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        string tidy = builder.ToString().Trim();
        return tidy.Length > MaxLength ? tidy.Substring(0, MaxLength).Trim() : tidy;
    }

    private static bool IsBlocked(string tidy)
    {
        string squashed = Normalise(tidy);

        foreach (string word in Blocked)
            if (squashed.Contains(Normalise(word)))
                return true;

        foreach (string part in tidy.Split(' '))
        {
            string normalised = Normalise(part);

            foreach (string word in BlockedWords)
                if (normalised == Normalise(word))
                    return true;
        }

        // The whole name read as one word, for "e s t a l o" and "p-u-t-a".
        foreach (string word in BlockedWords)
            if (squashed == Normalise(word))
                return true;

        return false;
    }

    // Accents off, the usual letter-for-digit swaps undone, everything that is not a letter dropped,
    // and runs of one letter flattened.
    private static string Normalise(string text)
    {
        string withoutAccents = text.Normalize(NormalizationForm.FormD);
        StringBuilder letters = new StringBuilder(withoutAccents.Length);
        char previous = '\0';

        foreach (char raw in withoutAccents)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark)
                continue;

            char c = char.ToLowerInvariant(raw);

            if (Swaps.TryGetValue(c, out char swapped))
                c = swapped;

            if (c < 'a' || c > 'z')
                continue;

            if (c == previous)
                continue;

            letters.Append(c);
            previous = c;
        }

        return letters.ToString();
    }

    private static readonly Dictionary<char, char> Swaps = new Dictionary<char, char>
    {
        { '0', 'o' }, { '1', 'i' }, { '!', 'i' }, { '|', 'i' }, { '3', 'e' }, { '4', 'a' },
        { '@', 'a' }, { '5', 's' }, { '$', 's' }, { '7', 't' }, { '+', 't' }, { '8', 'b' },
        { '9', 'g' }, { '6', 'g' }, { '2', 'z' },
    };
}
