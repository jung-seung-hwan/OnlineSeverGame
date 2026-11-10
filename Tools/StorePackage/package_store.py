"""Builds the Microsoft Store MSIX package of Jelly Survival from the Unity Windows build.

    python -I Tools/StorePackage/package_store.py               unsigned MSIX for Partner Center upload
    python -I Tools/StorePackage/package_store.py --sign-test   also a copy signed with a local test certificate
    python -I Tools/StorePackage/package_store.py --unity       run the Unity Windows build first

Steps:
  1. copy the Unity build (store_config.json "unityBuildDir", default Client/Build/Windows) to
     artifacts/store/layout/ (minus *_BurstDebugInformation_DoNotShip / *_BackUpThisFolder_ButDontShipItInYourGame)
  2. tile and logo images for the manifest (Assets/*.scale-*.png, Square44x44 targetsize-* plain/unplated/lightunplated)
     from Art/store/icon/ (make_art.py), and resources.pri via makepri
  3. AppxManifest.xml from AppxManifest.template.xml + store_config.json
  4. makeappx pack -> artifacts/store/JellySurvival_<version>_<arch>.msix  (upload this one; the Store signs it)
  5. --sign-test: test_cert.ps1 makes/reuses a self-signed certificate whose subject equals the manifest
     Publisher and signs a copy -> artifacts/store/JellySurvival_<version>_<arch>_testsigned.msix

Package rules: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements
"""
import glob
import json
import os
import shutil
import subprocess
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "artifacts", "store")
LAYOUT = os.path.join(OUT, "layout")
PRI_WORK = os.path.join(OUT, "pri")
ICON_DIR = os.path.join(ROOT, "Art", "store", "icon")
UNITY = os.environ.get("UNITY", r"C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe")


def sdk_tool(name):
    found = sorted(glob.glob(r"C:\Program Files (x86)\Windows Kits\10\bin\10.*\x64\\" + name))
    if not found:
        sys.exit(name + " not found: install the Windows SDK (App packaging tools)")
    return found[-1]


def run(cmd, **kw):
    print(">", " ".join(f'"{c}"' if " " in c else c for c in cmd))
    subprocess.run(cmd, check=True, **kw)


def unity_build():
    log = os.path.join(OUT, "logs", "unity-build.log")
    os.makedirs(os.path.dirname(log), exist_ok=True)
    run([UNITY, "-batchmode", "-nographics", "-projectPath", os.path.join(ROOT, "Client"), "-executeMethod",
         "OnlineSeverGame.Client.EditorTools.ProjectSetup.BuildWindows", "-quit", "-logFile", log])


def copy_build(cfg):
    src = os.path.join(ROOT, cfg["unityBuildDir"])
    if not os.path.exists(os.path.join(src, cfg["executable"])):
        sys.exit("Unity build not found at " + src + " (run with --unity, or build it first)")
    if os.path.exists(LAYOUT):
        shutil.rmtree(LAYOUT)
    skip = shutil.ignore_patterns("*_BurstDebugInformation_DoNotShip", "*_BackUpThisFolder_ButDontShipItInYourGame")
    shutil.copytree(src, LAYOUT, ignore=skip)


# ------------------------------------------------------------------ manifest images

SCALES = (100, 125, 150, 200, 400)
TARGETS = (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)


def fit(src, w, h, fill=0.9):
    """src centred on a transparent w x h canvas, scaled to `fill` of the shorter side."""
    side = round(min(w, h) * fill)
    im = src.resize((side, side), Image.LANCZOS)
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.alpha_composite(im, ((w - side) // 2, (h - side) // 2))
    return canvas


def make_images(assets_dir):
    full = Image.open(os.path.join(ICON_DIR, "app_icon_1024.png")).convert("RGBA")
    small = Image.open(os.path.join(ICON_DIR, "app_icon_small.png")).convert("RGBA")
    os.makedirs(assets_dir, exist_ok=True)
    # (file stem, base width, base height, picture, fill)
    plated = [("Square150x150Logo", 150, 150, full, 0.66), ("Wide310x150Logo", 310, 150, full, 0.66),
              ("SmallTile", 71, 71, full, 0.8), ("LargeTile", 310, 310, full, 0.6),
              ("StoreLogo", 50, 50, full, 1.0), ("Square44x44Logo", 44, 44, full, 1.0)]
    for stem, w, h, pic, f in plated:
        for s in SCALES:
            fit(pic, round(w * s / 100), round(h * s / 100), f).save(
                os.path.join(assets_dir, f"{stem}.scale-{s}.png"))
    # taskbar / Start list / Alt+Tab icons: jelly only below 36 px so it still reads at 16 px
    for t in TARGETS:
        pic = small if t < 36 else full
        im = fit(pic, t, t, 1.0)
        for suffix in ("", "_altform-unplated", "_altform-lightunplated"):
            im.save(os.path.join(assets_dir, f"Square44x44Logo.targetsize-{t}{suffix}.png"))


def make_pri(cfg, makepri):
    """resources.pri indexes only Assets/ (the Unity files are not resources)."""
    if os.path.exists(PRI_WORK):
        shutil.rmtree(PRI_WORK)
    make_images(os.path.join(PRI_WORK, "Assets"))
    shutil.copytree(os.path.join(PRI_WORK, "Assets"), os.path.join(LAYOUT, "Assets"))
    shutil.copyfile(os.path.join(LAYOUT, "AppxManifest.xml"), os.path.join(PRI_WORK, "AppxManifest.xml"))
    cfg_xml = os.path.join(OUT, "priconfig.xml")
    lang = cfg["languages"][0]
    run([makepri, "createconfig", "/cf", cfg_xml, "/dq", lang, "/pv", "10.0.0", "/o"], stdout=subprocess.DEVNULL)
    with open(cfg_xml, encoding="utf-8") as fh:        # one package, not a bundle: keep every scale in resources.pri
        xml = fh.read()
    a, b = xml.find("<packaging>"), xml.find("</packaging>")
    if a >= 0 and b > a:
        xml = xml[:a] + xml[b + len("</packaging>"):]
    with open(cfg_xml, "w", encoding="utf-8") as fh:
        fh.write(xml)
    run([makepri, "new", "/pr", PRI_WORK, "/cf", cfg_xml, "/mn", os.path.join(PRI_WORK, "AppxManifest.xml"),
         "/of", os.path.join(LAYOUT, "resources.pri"), "/o"], stdout=subprocess.DEVNULL)


def write_manifest(cfg):
    with open(os.path.join(HERE, "AppxManifest.template.xml"), encoding="utf-8") as fh:
        text = fh.read()
    res = "\n".join(f'    <Resource Language="{l}" />' for l in cfg["languages"])
    text = text.replace("{resources}", res)
    for k, v in cfg.items():
        if isinstance(v, str):
            text = text.replace("{" + k + "}", v.replace("&", "&amp;").replace("<", "&lt;").replace('"', "&quot;"))
    if "{" in text.split("-->", 1)[1]:
        sys.exit("unfilled placeholder in AppxManifest.xml")
    with open(os.path.join(LAYOUT, "AppxManifest.xml"), "w", encoding="utf-8") as fh:
        fh.write(text)


def main():
    with open(os.path.join(HERE, "store_config.json"), encoding="utf-8") as fh:
        cfg = json.load(fh)
    os.makedirs(os.path.join(OUT, "logs"), exist_ok=True)
    if "--unity" in sys.argv:
        unity_build()
    makeappx, makepri, signtool = sdk_tool("makeappx.exe"), sdk_tool("makepri.exe"), sdk_tool("signtool.exe")
    copy_build(cfg)
    write_manifest(cfg)
    make_pri(cfg, makepri)
    name = f"JellySurvival_{cfg['version']}_{cfg['processorArchitecture']}"
    msix = os.path.join(OUT, name + ".msix")
    with open(os.path.join(OUT, "logs", "makeappx.log"), "w", encoding="utf-8") as log:
        run([makeappx, "pack", "/d", LAYOUT, "/p", msix, "/o", "/h", "SHA256"], stdout=log)
    print("package:", msix, f"({os.path.getsize(msix) / 1e6:.1f} MB)")
    if "--sign-test" in sys.argv:
        signed = os.path.join(OUT, name + "_testsigned.msix")
        shutil.copyfile(msix, signed)
        pfx = os.path.join(OUT, "test-cert", "JellySurvivalTest.pfx")
        run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", os.path.join(HERE, "test_cert.ps1"),
             "-Action", "Create", "-Subject", cfg["publisher"], "-PfxPath", pfx])
        run([signtool, "sign", "/fd", "SHA256", "/f", pfx, "/p", "jelly-test", signed])
        print("test-signed package:", signed)
    # listing bundle: everything Partner Center needs besides the package, next to the MSIX
    listing = os.path.join(OUT, "listing")
    if os.path.exists(listing):
        shutil.rmtree(listing)
    shutil.copytree(os.path.join(ROOT, "Art", "store"), listing)
    shutil.make_archive(os.path.join(OUT, "JellySurvival_store_listing"), "zip", listing)
    print("listing bundle:", os.path.join(OUT, "JellySurvival_store_listing.zip"))
    if "--unsigned-test" in sys.argv:
        # Windows 11 can install an unsigned package (admin, Add-AppxPackage -AllowUnsigned) when the publisher
        # carries this OID; no certificate has to be trusted. The identity differs from the Store one on purpose.
        # https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package
        test = dict(cfg, publisher=cfg["publisher"] + ", OID.2.25.311729368913984317654407730594956997722=1")
        write_manifest(test)
        unsigned = os.path.join(OUT, name + "_unsignedtest.msix")
        with open(os.path.join(OUT, "logs", "makeappx-unsignedtest.log"), "w", encoding="utf-8") as log:
            run([makeappx, "pack", "/d", LAYOUT, "/p", unsigned, "/o", "/h", "SHA256"], stdout=log)
        write_manifest(cfg)
        print("unsigned test package:", unsigned)


if __name__ == "__main__":
    main()
