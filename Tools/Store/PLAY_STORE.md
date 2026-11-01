# Google Play — publication plan

Everything needed to get Estalo onto Google Play, in the order it has to happen. Package name is
already set: `com.tomdeleite.estalo`, product name Estalo, min SDK 25, target SDK 36.

## 1. Developer account

- Google Play Console, personal account, one-off fee of 25 USD.
- Identity verification (a government ID and an address) takes a few days. Start this first,
  because nothing else can be submitted until it clears.
- A personal account created after 13 November 2023 must run a closed test before it may publish
  publicly. That is the 12-testers rule below.

## 2. Closed testing: 12 testers, 14 continuous days

- Upload the game to the **Closed testing** track as an **.aab** (Android App Bundle), not an apk.
  In Unity: Player Settings > Publishing Settings, then Build with "Build App Bundle (Google
  Play)" ticked.
- Add testers by Gmail address, or by a Google Group that holds them.
- At least 12 testers must stay opted in for 14 days in a row. Someone who opts out and back in
  restarts their count, so recruit 15 or more to absorb dropouts.
- Testers must actually install and play; Google looks at engagement, not only at sign-ups.
- The nine people from the September user test are the start of this list.

## 3. Store listing

See `LISTING.md` for the text, and `SCREENSHOTS.md` for the images.

Required before the listing can be reviewed:

- App icon, 512 x 512 px.
- Feature graphic, 1024 x 500 px.
- 2 to 8 phone screenshots, shortest side 1080 px or more.
- Short description, 80 characters.
- Full description, up to 4000 characters.
- Privacy policy at a public URL. `PRIVACY.md` is the text; publish it with GitHub Pages on the
  Estalo repository and use that address.

## 4. Target audience and age

Estalo is a **13+** app, not child-directed. Keep these three answers the same everywhere, because
Google cross-checks them:

- Play Console **Target audience** → tick 13+ age bands only. Ticking any band under 13 puts the app
  into the Google Play Families programme, with its own self-certification and stricter review.
- Unity's **"Will this app be targeted to children"** setting in Project Settings → Services →
  answer **No**.
- `PRIVACY.md` says 13 and over.

Nothing in the game collects email, location or advertising identifiers, so the only child-safety
risk left is a player typing their real name as a display name. The privacy policy asks for a
nickname instead.

## 5. Content rating questionnaire

Answer honestly; for Estalo everything is "no": no violence, no sexual content, no language, no
controlled substances, no gambling, no user-generated content shared publicly. Players do choose a
display name that others see in a match, which is worth declaring under user interaction.

Expected rating: Livre / Everyone.

## 6. Data safety form

Declare exactly what the app does. Draft answers:

| Question | Answer |
|---|---|
| Does your app collect or share any of the required user data types? | Yes |
| Personal info — Name | Collected. Optional. Purpose: app functionality. Not shared. (The display name the player types.) |
| Personal info — User IDs | Collected. Required. Purpose: app functionality, account management. Not shared. (Account key and, if the player signs in, the username.) |
| App activity — Other in-app actions | Collected. Required. Purpose: app functionality. Not shared. (Score, matches played, wins and losses.) |
| App info and performance | Not collected. |
| Device or other IDs | Not collected. |
| Is all user data encrypted in transit? | **See the warning below — answer it truthfully.** |
| Can users request that data be deleted? | Yes, by email. |

**Warning, and a job to do before launch.** The game talks to the server over Unity Netcode's
default UDP transport, with no encryption. Anyone on the same Wi-Fi could read what is sent,
including the Unity login token when a player signs in. Two consequences:

1. The Data safety form cannot claim data is encrypted in transit until this is fixed.
2. Fix it before a public launch: enable DTLS on Unity Transport (`UnityTransport` > Use
   Encryption, with certificates on the server), or route logins so the token never crosses the
   plain channel.

## 7. Production release

- Apply for production access from the Play Console dashboard once the 14 days are complete.
  Google asks how the test went: how many testers, what feedback came back, what changed. The
  September survey results in `../Survey/RESULTS.md` answer that directly.
- Review usually takes a few days, sometimes longer for a first app.

## 8. Timeline from 1 November 2026

| Step | Dates |
|---|---|
| Developer account and verification | 1–7 November |
| Build .aab, upload to closed testing, recruit 15 testers | 7–9 November |
| 14 continuous days of testing | 9–23 November |
| Apply for production access | 23 November |
| Google review | 23–30 November |
| Public on Google Play | end of November / early December 2026 |

Server cost continues after launch: the game needs the dedicated server at 216.238.108.122 to be
running for anyone to play a match.
