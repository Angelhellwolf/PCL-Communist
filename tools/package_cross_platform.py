"""Package a self-contained desktop publish. No signing credentials are read."""
import argparse
import hashlib
import os
import platform
from pathlib import Path
import plistlib
import shutil
import subprocess
import tarfile
import zipfile


def package(runtime: str, source: Path, output: Path) -> Path:
    source = source.resolve()
    output = output.resolve()
    if not source.is_dir():
        raise ValueError(f"Missing publish directory: {source}")
    output.mkdir(parents=True, exist_ok=True)
    repo = Path(__file__).resolve().parent.parent
    name = f"PCL-Communist-0.1.0-preview-{runtime}"
    stage = output / name
    if stage.exists():
        # This is a generated staging directory with a fixed child name.
        if stage.parent != output or stage.is_symlink():
            raise ValueError("Unexpected staging path")
        shutil.rmtree(stage)
    stage.mkdir()
    if runtime.startswith("osx-"):
        app = stage / "PCL Communist.app"
        contents = app / "Contents"
        executable_dir = contents / "MacOS"
        resources = contents / "Resources"
        shutil.copytree(source, executable_dir)
        resources.mkdir()
        executable = executable_dir / "PCL.Communist.Desktop"
        executable.chmod(0o755)
        plist = {
            "CFBundleExecutable": executable.name,
            "CFBundleIdentifier": "org.pclcommunist.launcher",
            "CFBundleName": "PCL Communist",
            "CFBundleDisplayName": "PCL Communist",
            "CFBundlePackageType": "APPL",
            "CFBundleShortVersionString": "0.1.0",
            "CFBundleVersion": "1",
            "LSMinimumSystemVersion": "12.0",
            "NSHighResolutionCapable": True,
            "NSRequiresAquaSystemAppearance": False,
        }
        shutil.copyfile(repo / "CrossPlatform/PCL.Communist/Assets/Launcher.icns", resources / "Launcher.icns")
        plist["CFBundleIconFile"] = "Launcher.icns"
        with (contents / "Info.plist").open("wb") as stream:
            plistlib.dump(plist, stream)
        shutil.copyfile(repo / "COMMUNIST-AGREEMENT.txt", resources / "COMMUNIST-AGREEMENT.txt")
        shutil.copytree(repo / "licenses", resources / "licenses")
        # Ad-hoc signing on a Mac makes the local bundle internally consistent; it is not Apple notarization.
        if platform.system() == "Darwin":
            subprocess.run(["codesign", "--force", "--deep", "--sign", "-", str(app)], check=True)
    else:
        shutil.copytree(source, stage, dirs_exist_ok=True)
        shutil.copyfile(repo / "COMMUNIST-AGREEMENT.txt", stage / "COMMUNIST-AGREEMENT.txt")
        shutil.copytree(repo / "licenses", stage / "licenses", dirs_exist_ok=True)
        if runtime.startswith("linux-"):
            (stage / "PCL.Communist.Desktop").chmod(0o755)
    shutil.copyfile(repo / "docs/cross-platform.md", stage / "PREVIEW-README.md")
    if runtime.startswith("win-"):
        archive = output / f"{name}.zip"
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as zip_file:
            for file in sorted(stage.rglob("*")):
                if file.is_file():
                    zip_file.write(file, file.relative_to(output))
    else:
        archive = output / f"{name}.tar.gz"
        with tarfile.open(archive, "w:gz") as tar_file:
            tar_file.add(stage, arcname=stage.name)
    with archive.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    archive.with_suffix(archive.suffix + ".sha256").write_text(f"{digest}  {archive.name}\n", encoding="utf-8")
    print(archive)
    return archive


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--runtime", required=True, choices=["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"])
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    package(args.runtime, args.input, args.output)
