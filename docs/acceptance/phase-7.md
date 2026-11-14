# Phase 7 acceptance

Setup first (once): Settings > Browser > **Connect**. Then in Chrome open `chrome://extensions`, switch on **Developer mode**, click **Load unpacked** and pick `E:\AldrenData\dev\aldren\extension`. The card should show ID `mhipkigoofdhdiigilekjfpkhemnjajm`. Back in Settings > Browser, click **Refresh**: "Connected".

| # | Do | Expected |
|---|---|---|
| 1 | Settings > Browser | "Connected: the Companion is running." |
| 2 | "open youtube.com" | YouTube opens in a Chrome tab (the "ACT" badge shows on the extension while it acts) |
| 3 | "search for lofi study music" | A Google search for it opens |
| 4 | "open the second result" | ALDREN reads the page and clicks the second result |
| 5 | "go back", then "scroll down" | The tab goes back, then scrolls |
| 6 | "what tabs do I have open?" | A list of the open tabs |
| 7 | On a page with a search box: "type hello into the search box" | Asks first (Interact level), then types |
| 8 | On a login page: "fill in my password" | Refused: password fields are never filled |
| 9 | On a page with a Submit or Buy button: "click submit" | Asks first (tier 3) |
| 10 | Settings > Browser: set `example.com` to Read only, open example.com, "click the link" | Refused for a read-only site; Reset afterwards |
| 11 | "download this" on a page with a PDF link | The file downloads; an `.exe` link asks first |
| 12 | Close Chrome, then "open youtube.com" | "The ALDREN Companion isn't connected…" (or the app is launched without the page), no crash |
| 13 | Settings > Activity history | browser_* entries with their tiers |

Google (after the OAuth client from [guides/google-oauth.md](../guides/google-oauth.md); Settings > Google account > client ID and secret, **Save client**, **Sign in**, finish in the browser):

| # | Do | Expected |
|---|---|---|
| 14 | Settings > Google account | "Signed in: Calendar and Drive are available." |
| 15 | "what's on my calendar today" | Today's events, or none |
| 16 | "add study session tomorrow at 5 pm to my calendar" | Asks first, creates it, reads it back with the time |
| 17 | "delete the study session from my calendar" | Asks first (tier 3), deletes it |
| 18 | "good morning" | The briefing now includes today's calendar |
| 19 | "find my notes in Drive", then "download the first one" | Search results, then the file in Downloads (a Google Doc as .docx) |
| 20 | Settings > Google account > **Disconnect** | "Not set up" or "Client saved, not signed in yet"; calendar requests say to sign in |
