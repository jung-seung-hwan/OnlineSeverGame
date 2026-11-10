"""Build the stand-alone game server packages to install on the public server machine.

Usage (repo root):  python -I Tools/ServerPackage/package_server.py [--out artifacts/server] [--rid osx-arm64 ...]

For each runtime it publishes Server/GameServer self-contained (no .NET needed on the server),
adds start scripts and a README, and writes one archive:
  - macOS / Linux: <name>.tar.gz  (scripts with LF line endings and the execute bit set)
  - Windows:       <name>.zip
The archive name carries the protocol version: the server and the players' clients must match.
See docs/work/online-server.md.
"""
import argparse
import io
import os
import re
import shutil
import subprocess
import sys
import tarfile
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DEFAULT_RIDS = ["osx-arm64", "osx-x64", "linux-x64", "win-x64"]
APP = "OnlineSeverGame.Server"
DEFAULT_PORT = 7771
DEFAULT_MIN_PLAYERS = 2

CONF = """# Jelly Survival server settings. Edit, then restart the server.
PORT={port}
MIN_PLAYERS={min_players}
# Map rotation at start: cycle or random. The host can still change it in game.
MAPS=cycle
# Extra server options (see README), for example: --verbose
EXTRA=
"""

START_SH = """#!/bin/bash
# Jelly Survival game server. Run:  ./start.sh   (stop with Ctrl+C)
# Restarts the server if it ever exits, keeps the Mac awake, logs to logs/.
cd "$(dirname "$0")"
source ./server.conf
BIN="./bin/{app}"

# First run on macOS: drop the download quarantine and sign the program for this Mac
# (it was built on Windows, so it has no signature and Apple Silicon would refuse to run it).
if [ "$(uname)" = "Darwin" ]; then
  xattr -dr com.apple.quarantine . 2>/dev/null
  if ! codesign -v "$BIN" >/dev/null 2>&1; then
    codesign --force --sign - "$BIN" || {{ echo "codesign failed"; exit 1; }}
  fi
fi
chmod +x "$BIN"
mkdir -p logs

AWAKE=""
if command -v caffeinate >/dev/null 2>&1; then AWAKE="caffeinate -i"; fi

echo "Jelly Survival server (protocol v{version}) on port $PORT. Logs: $(pwd)/logs/server.log"
while true; do
  echo "=== start $(date) ===" >> logs/server.log
  $AWAKE "$BIN" --port "$PORT" --min-players "$MIN_PLAYERS" --maps "$MAPS" $EXTRA 2>&1 | tee -a logs/server.log
  echo "server exited ($?), restarting in 3 s (Ctrl+C to stop)"
  sleep 3
done
"""

INSTALL_MAC = """#!/bin/bash
# Start the server automatically when this Mac user logs in (and restart it if it stops).
# Uninstall: ./uninstall-autostart-mac.sh
cd "$(dirname "$0")"
DIR="$(pwd)"
LABEL="kr.co.wildmagic.jellysurvival"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
mkdir -p "$HOME/Library/LaunchAgents" logs
cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>$LABEL</string>
  <key>ProgramArguments</key><array><string>/bin/bash</string><string>$DIR/start.sh</string></array>
  <key>WorkingDirectory</key><string>$DIR</string>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <key>StandardOutPath</key><string>$DIR/logs/launchd.log</string>
  <key>StandardErrorPath</key><string>$DIR/logs/launchd.log</string>
</dict></plist>
EOF
launchctl unload "$PLIST" 2>/dev/null
launchctl load "$PLIST"
echo "Installed: $PLIST"
echo "The server runs now and after every login. Log: $DIR/logs/server.log"
"""

UNINSTALL_MAC = """#!/bin/bash
LABEL="kr.co.wildmagic.jellysurvival"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
launchctl unload "$PLIST" 2>/dev/null
rm -f "$PLIST"
pkill -f "bin/{app}" 2>/dev/null
echo "Auto start removed and server stopped."
"""

START_BAT = """@echo off
rem Jelly Survival game server. Close this window (or Ctrl+C) to stop.
chcp 65001 >nul
cd /d "%~dp0"
set "EXTRA="
for /f "usebackq eol=# tokens=1,* delims==" %%a in ("server.conf") do set "%%a=%%b"
if not exist logs mkdir logs
echo Jelly Survival server (protocol v{version}) on port %PORT%. Allow it if Windows Firewall asks.
:loop
bin\\{app}.exe --port %PORT% --min-players %MIN_PLAYERS% --maps %MAPS% %EXTRA%
echo server exited, restarting in 3 s (close the window to stop)
timeout /t 3 /nobreak >nul
goto loop
"""

README = """Jelly Survival 게임 서버 (프로토콜 v{version}, {rid}, 커밋 {commit})
==========================================================================

이 폴더 하나가 서버 프로그램 전부입니다. 서버 컴퓨터에 .NET을 설치할 필요가 없습니다.
게임(클라이언트)과 서버는 프로토콜 번호가 같아야 접속됩니다. 이 서버는 v{version}입니다.
게임을 새로 배포할 때는 같은 버전의 서버 패키지로 바꿔 주세요.

1. 실행
{run}
   설정은 server.conf 에서 바꿉니다(포트 {port}, 라운드 시작 인원 {min_players}명, 맵 교체 방식).
   서버가 혹시 꺼지면 3초 뒤 저절로 다시 켭니다. 기록은 logs/server.log 에 남습니다.

2. 밖에서 접속되게 하기 (한 번만)
   - 공유기 포트포워딩: TCP {port} -> 서버 컴퓨터의 내부 IP. 내부 IP는 공유기의 DHCP 예약으로 고정하세요.
   - 서버 컴퓨터 방화벽: 처음 실행할 때 "들어오는 연결 허용"을 묻는 창이 뜨면 허용하세요.
   - 서버 컴퓨터가 잠자기에 들어가면 접속이 끊깁니다. 맥은 start.sh 가 caffeinate 로 막아 줍니다.
     디스플레이 꺼짐은 괜찮지만, "시스템 설정 > 에너지"에서 자동 잠자기는 끄는 것을 권합니다.

3. 확인
   - 다른 네트워크(예: 휴대폰 핫스팟)의 윈도우 PC에서 PowerShell:
       Test-NetConnection wildmagic.co.kr -Port {port}
     TcpTestSucceeded : True 이면 열린 것입니다.
   - 같은 공유기 안의 PC에서는 도메인으로 안 붙을 수 있습니다(헤어핀 NAT). 그때는 서버의 내부 IP:{port} 로 접속하세요.

4. 플레이어
   게임 접속 화면의 서버 주소에 wildmagic.co.kr:{port} 를 넣고 접속합니다(포트까지 꼭 적기).
   한 방에 최대 8명이고, {min_players}명이 모이면 라운드가 시작됩니다. 가장 먼저 들어온 사람이 방장입니다.
   비밀번호가 없어서 주소를 아는 누구나 들어올 수 있습니다.

5. 서버 옵션 (server.conf 의 EXTRA 에 추가)
   --verbose            대시·밟기·위험물 근접 등을 모두 기록
   --map N              1 주방, 2 겨울 처마, 3 창고, 4 디저트 가게로 고정해서 시작
   --seed N             무작위 고정(시험용)
"""

RUN_MAC = """   터미널에서 이 폴더로 이동한 뒤:
       ./start.sh
   로그인할 때마다 자동으로 켜지게 하려면(맥):
       ./install-autostart-mac.sh        (끄기: ./uninstall-autostart-mac.sh)
   처음 실행할 때 프로그램에 이 맥용 서명을 붙입니다(윈도우에서 만든 파일이라 필요)."""
RUN_LINUX = """   터미널에서 이 폴더로 이동한 뒤:
       ./start.sh"""
RUN_WIN = """   run-server.bat 을 더블클릭합니다. 창을 닫으면 서버가 꺼집니다."""


def sh(cmd):
    print("+", " ".join(cmd), flush=True)
    subprocess.run(cmd, cwd=ROOT, check=True)


def protocol_version():
    src = open(os.path.join(ROOT, "Shared", "Protocol", "ProtocolConstants.cs"), encoding="utf-8").read()
    return int(re.search(r"Version\s*=\s*(\d+)", src).group(1))


def git_commit():
    r = subprocess.run(["git", "rev-parse", "--short", "HEAD"], cwd=ROOT, capture_output=True, text=True)
    dirty = subprocess.run(["git", "status", "--porcelain", "--", "Shared", "Server"], cwd=ROOT,
                           capture_output=True, text=True).stdout.strip()
    return r.stdout.strip() + ("-dirty" if dirty else "")


def add_text(tar, name, text, mode):
    data = text.replace("\r\n", "\n").encode("utf-8")
    info = tarfile.TarInfo(name)
    info.size = len(data)
    info.mode = mode
    tar.addfile(info, io.BytesIO(data))


def package(rid, out_dir, version, commit, port, min_players):
    name = f"JellySurvivalServer-v{version}-{rid}"
    stage = os.path.join(out_dir, name)
    shutil.rmtree(stage, ignore_errors=True)
    is_win = rid.startswith("win")
    publish = ["dotnet", "publish", "Server/GameServer", "-c", "Release", "-r", rid, "--self-contained", "true",
               "-o", os.path.join(stage, "bin"), "-p:DebugType=none"]
    if is_win:
        publish += ["-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true"]
    sh(publish)

    run = RUN_WIN if is_win else (RUN_MAC if rid.startswith("osx") else RUN_LINUX)
    fmt = dict(version=version, rid=rid, commit=commit, port=port, min_players=min_players, app=APP, run=run)
    files = {"server.conf": CONF.format(**fmt), "README.txt": README.format(**fmt)}
    if is_win:
        files["run-server.bat"] = START_BAT.format(**fmt).replace("\n", "\r\n")
        files["server.conf"] = files["server.conf"].replace("\n", "\r\n")
        files["README.txt"] = files["README.txt"].replace("\n", "\r\n")
        for fname, text in files.items():
            with open(os.path.join(stage, fname), "w", encoding="utf-8", newline="") as f:
                f.write(text)
        archive = os.path.join(out_dir, name + ".zip")
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as z:
            for root, _, fs in os.walk(stage):
                for f in fs:
                    p = os.path.join(root, f)
                    z.write(p, os.path.join(name, os.path.relpath(p, stage)))
        return archive

    scripts = {"start.sh": START_SH.format(**fmt)}
    if rid.startswith("osx"):
        scripts["install-autostart-mac.sh"] = INSTALL_MAC
        scripts["uninstall-autostart-mac.sh"] = UNINSTALL_MAC.format(**fmt)
    for fname, text in {**files, **scripts}.items():
        with open(os.path.join(stage, fname), "w", encoding="utf-8", newline="\n") as f:
            f.write(text.replace("\r\n", "\n"))
    archive = os.path.join(out_dir, name + ".tar.gz")
    with tarfile.open(archive, "w:gz") as tar:
        for root, dirs, fs in os.walk(stage):
            rel_root = os.path.relpath(root, stage)
            for d in dirs:
                info = tarfile.TarInfo(os.path.normpath(os.path.join(name, rel_root, d)).replace("\\", "/"))
                info.type = tarfile.DIRTYPE
                info.mode = 0o755
                tar.addfile(info)
            for f in fs:
                p = os.path.join(root, f)
                arc = os.path.normpath(os.path.join(name, rel_root, f)).replace("\\", "/")
                executable = f.endswith(".sh") or (rel_root == "bin" and f == APP)
                info = tar.gettarinfo(p, arc)
                info.mode = 0o755 if executable else 0o644
                info.uid = info.gid = 0
                info.uname = info.gname = ""
                with open(p, "rb") as fh:
                    tar.addfile(info, fh)
    return archive


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join("artifacts", "server"))
    ap.add_argument("--rid", nargs="*", default=DEFAULT_RIDS)
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--min-players", type=int, default=DEFAULT_MIN_PLAYERS)
    a = ap.parse_args()
    out_dir = os.path.join(ROOT, a.out)
    os.makedirs(out_dir, exist_ok=True)
    version, commit = protocol_version(), git_commit()
    made = [package(rid, out_dir, version, commit, a.port, a.min_players) for rid in a.rid]
    print("\nprotocol v%d, commit %s" % (version, commit))
    for m in made:
        print("  %s  (%.1f MB)" % (os.path.relpath(m, ROOT), os.path.getsize(m) / 1e6))


if __name__ == "__main__":
    sys.exit(main())
