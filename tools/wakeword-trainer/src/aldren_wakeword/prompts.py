"""What the recording session asks for: wake-word takes with delivery cues, short commands and room noise."""

from dataclasses import dataclass


@dataclass(frozen=True)
class Prompt:
    kind: str  # "wake", "command" or "noise"
    text: str
    cue: str  # how to say it; for commands, the language ("en" or "fr")
    seconds: float


WAKE_CUES = [
    "normal",
    "quiet",
    "loud",
    "tired",
    "fast",
    "slow",
    "1 m away",
    "2-3 m away",
    "with music playing",
]
WAKE_TAKES_PER_CUE = 9

COMMANDS_EN = [
    "Open Spotify.",
    "Set the volume to thirty percent.",
    "What's on my calendar tomorrow?",
    "Remind me to call mum at seven.",
    "Close all the browser windows.",
    "Turn on focus mode for an hour.",
    "What's the weather like this afternoon?",
    "Pause the music.",
    "Take a note: buy milk and eggs.",
    "Show me my downloads folder.",
    "Move this window to the left monitor.",
    "How long until my next meeting?",
    "Search the web for cheap flights to Lisbon.",
    "Mute notifications until six.",
    "What time is it in Tokyo?",
    "Start a five minute timer.",
    "Switch to gaming mode.",
    "What's on my screen right now?",
    "Open Visual Studio Code in my project folder.",
    "Skip to the next song.",
]

COMMANDS_FR = [
    "Ouvre Discord.",
    "Mets le volume à cinquante pour cent.",
    "Qu'est-ce que j'ai de prévu demain matin ?",
    "Rappelle-moi d'acheter du pain à dix-huit heures.",
    "Ferme toutes les fenêtres.",
    "Lance de la musique.",
    "Quel temps fait-il à Paris ?",
    "Active le mode concentration.",
    "Prends une note : réviser le partiel de maths.",
    "Mets l'ordinateur en veille dans dix minutes.",
]


def session_prompts() -> list[Prompt]:
    wake = [Prompt("wake", "ALDREN", cue, 2.5) for cue in WAKE_CUES for _ in range(WAKE_TAKES_PER_CUE)]
    commands = [Prompt("command", text, "en", 5.0) for text in COMMANDS_EN]
    commands += [Prompt("command", text, "fr", 5.0) for text in COMMANDS_FR]
    noise = [Prompt("noise", "", "stay quiet, normal room sounds", 30.0)]
    return wake + commands + noise
