"""Real game screenshots for the Store listing (1920 x 1080, Store minimum is 1366 x 768).

    python -I Tools/StorePackage/take_screenshots.py [--port 7808] [--lang ko,en] [--maps 1,2,3,4] [--only connect,lobby,maps]

For each language: the connect screen with the character picker (no server; the join address is an example from the
documentation range 203.0.113.0/24, Unity drops empty arguments); the lobby of a room
the client hosts itself (-host -demoaddress: example addresses in the "address for your friends" box, docs/work/
player-host.md) with 7 bots, before anyone presses Start; then for each map a local server
fixed to that map (--map N, 8 players, 2 rounds) with 7 bots and the Windows build as the 8th player
(autoplay), shooting every 2.5 s: lobby, countdown, play, side hazards after 20 s, result screen.
Raw shots go to artifacts/store/shots/<lang>/; pick the best into Art/store/screenshots/ by hand
(docs/work/store.md 5).

Uses the Unity build in Client/Build/Windows and the server/bot builds in artifacts/bin (dotnet build first).
The client saves the name/server it connected with (PlayerPrefs) like any -autoconnect run.
"""
import argparse
import os
import subprocess
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CLIENT = os.path.join(ROOT, "Client", "Build", "Windows", "Client.exe")
SERVER = os.path.join(ROOT, "artifacts", "bin", "GameServer", "debug", "OnlineSeverGame.Server.exe")
BOT = os.path.join(ROOT, "artifacts", "bin", "BotClient", "debug", "OnlineSeverGame.BotClient.exe")
SHOTS = os.path.join(ROOT, "artifacts", "store", "shots")
BOT_NAMES = ["Mochi", "Gummy", "Soda", "Berry", "Lemon", "Peach", "Grape"]
PLAYER = {"ko": ("젤리왕", "jelly"), "en": ("JellyKing", "jelly")}
WINDOW = ["-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0"]


def client(lang, args, log):
    return subprocess.Popen([CLIENT, "-lang", lang, *WINDOW, *args, "-logFile", log])


def connect_screen(lang, out):
    name, kind = PLAYER[lang]
    p = client(lang, ["-name", name, "-character", "pudding", "-server", "203.0.113.25:7777", "-screenshot", f"{out}/connect_{{n}}.png",
                      "-screenshotDelay", "4", "-screenshotEvery", "1.5", "-quitAfter", "9"],
               os.path.join(out, "client_connect.log"))
    p.wait()


def lobby_run(lang, port, out):
    """The host's lobby: the client creates the room itself, 4 friends (console bots) join and the host adds 3
    server bots (-bots 3, docs/work/bots.md); nobody presses Start."""
    name, kind = PLAYER[lang]
    cl = client(lang, ["-host", "-port", str(port), "-demoaddress", "-bots", "3", "-name", name, "-character", kind,
                       "-screenshot", f"{out}/lobby_{{n}}.png", "-screenshotDelay", "8", "-screenshotEvery", "2",
                       "-quitAfter", "16"],
                os.path.join(out, "client_lobby.log"))
    time.sleep(4)
    bots = []
    for i, bn in enumerate(BOT_NAMES[:4]):
        bots.append(subprocess.Popen([BOT, "--port", str(port), "--count", "1", "--name", bn,
                                      "--seed", str(900 + i * 17 + (7 if lang == "en" else 0)), "--duration", "14"],
                                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
        time.sleep(0.3)
    cl.wait(timeout=60)
    for b in bots:
        try:
            b.wait(timeout=10)
        except subprocess.TimeoutExpired:
            b.kill()


def map_run(lang, stage, port, out):
    name, kind = PLAYER[lang]
    logs = []
    srv = subprocess.Popen([SERVER, "--port", str(port), "--min-players", "8", "--map", str(stage),
                            "--exit-after-rounds", "2"],
                           stdout=open(os.path.join(out, f"server_map{stage}.log"), "w"), stderr=subprocess.STDOUT)
    time.sleep(1.5)
    cl = client(lang, ["-server", f"127.0.0.1:{port}", "-autoconnect", "-autoplay", "-name", name, "-character", kind,
                       "-screenshot", f"{out}/map{stage}_{{n}}.png", "-screenshotDelay", "6",
                       "-screenshotEvery", "2.5", "-quitAfter", "150"],
                os.path.join(out, f"client_map{stage}.log"))
    time.sleep(5)                      # the client joins first and becomes host
    bots = []
    for i, bn in enumerate(BOT_NAMES[:4]):
        bots.append(subprocess.Popen([BOT, "--port", str(port), "--count", "1", "--name", bn,
                                      "--seed", str(stage * 100 + i * 17 + (7 if lang == "en" else 0)),
                                      "--rounds", "2"],
                                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
        time.sleep(0.3)
    try:
        srv.wait(timeout=240)
    except subprocess.TimeoutExpired:
        srv.kill()
    for b in bots:
        try:
            b.wait(timeout=10)
        except subprocess.TimeoutExpired:
            b.kill()
    cl.wait(timeout=120)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=7808)
    ap.add_argument("--lang", default="ko,en")
    ap.add_argument("--maps", default="1,2,3,4")
    ap.add_argument("--only", default="connect,lobby,maps")
    a = ap.parse_args()
    only = a.only.split(",")
    for lang in a.lang.split(","):
        out = os.path.join(SHOTS, lang).replace("\\", "/")
        os.makedirs(out, exist_ok=True)
        if "connect" in only:
            connect_screen(lang, out)
        if "lobby" in only:
            lobby_run(lang, 7777, out)   # the real default port, as players will see it
        if "maps" in only:
            for m in a.maps.split(","):
                map_run(lang, int(m), a.port, out)
        print("shots in", out)


if __name__ == "__main__":
    main()
