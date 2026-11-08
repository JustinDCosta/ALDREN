# Phase 6 acceptance

Setup first (once, about 10 minutes): Settings > Cloud > **Open gateway dashboard**. Create the dashboard account. Add keys only for Google (Gemini), Groq, Cerebras, Mistral, OpenRouter or GitHub Models, and one search engine key (Tavily or Brave) under Models > Search. Fill the chains under Fallback chains: `aldren-fast` (Groq or Cerebras), `aldren-reason` and `aldren-research` (Gemini, Groq, Cerebras or Mistral), `aldren-vision` (Gemini). **Save changes**, then back in Settings > Cloud click **Check the chains again**: each chain should say "ready".

| # | Do | Expected |
|---|---|---|
| 1 | Settings > Cloud | "Gateway running on 127.0.0.1:…", four chains listed, no blocked keys |
| 2 | "look up the latest Nvidia driver version" | The island shows 🌐 Research (online), the tray tooltip says online; an answer with [n] citations in 10–15 s; spoken without the sources or links |
| 3 | Click the island | The conversation window shows the answer and the numbered sources with links |
| 4 | Ask something the local model can't know, without "look up" ("who won yesterday's match?") | "I'd need to look that up online. Want me to?" → say "yes" → research runs; say anything else → nothing goes online |
| 5 | Settings > Activity history | `cloud:search` and `cloud:aldren-research` entries with provider and timing, no prompt text |
| 6 | In the dashboard, add a model from a provider outside the chain's list to `aldren-research` (for example a reseller), Save, "Check the chains again" | That chain shows "disallowed"; research says the chain isn't ready |
| 7 | Enable a key for a provider outside the allowlist, recheck | "Cloud use is blocked: keys are enabled for providers ALDREN doesn't allow"; undo it afterwards |
| 8 | Task Manager: end the gateway's `node.exe`; ask "what time is it" and "tell me a joke" | Local answers keep working; the gateway restarts within a minute |
| 9 | Settings > Cloud: switch the Quick Brain fallback on, turn gaming mode on, and stop the CPU model by asking something | Simple questions get "(answered online)" via aldren-fast; switch it off again afterwards |
| 10 | "look up my password is hunter2" | Refused: secrets never leave the PC |
