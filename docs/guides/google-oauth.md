# Google sign-in: what ALDREN needs from you

Calendar (Phase 4) and Drive (Phase 7) use your own Google Cloud project, so no third party sits between ALDREN and your account. This takes about 10 minutes, once.

1. Go to https://console.cloud.google.com/ and create a project (for example "ALDREN"). It's free.
2. **APIs & Services > Library**: enable **Google Calendar API** (and **Google Drive API** if you want Drive later).
3. **APIs & Services > OAuth consent screen**: choose **External**, fill in the app name and your email, add yourself as a test user, then **Publish app** ("In production"). In "Testing" mode Google expires the sign-in after 7 days. Unverified is fine for personal use; Google shows an "unverified app" warning you can click through.
4. **APIs & Services > Credentials > Create credentials > OAuth client ID**: type **Desktop app**. Download nothing; just keep the **Client ID** and **Client secret** at hand.
5. Tell ALDREN's developer session you're ready. The next step (approved separately) adds the official Google .NET client packages, asks you to paste the client ID and secret into Settings > Google account (stored in Windows Credential Manager, never in a file), and opens Google's sign-in page in your browser. ALDREN asks only for Calendar events access first; Drive scopes come later, only if you enable Drive.

You can revoke access any time at https://myaccount.google.com/permissions.
