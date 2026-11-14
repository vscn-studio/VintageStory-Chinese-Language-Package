"""Download the most recently published language pack, including prereleases."""
import json
import os
from pathlib import Path
import re
import subprocess


def select_previous(releases):
    for release in sorted(
        (item for item in releases if not item.get("draft")),
        key=lambda item: item.get("published_at") or "",
        reverse=True,
    ):
        assets = [asset for asset in release.get("assets", [])
                  if asset["name"].startswith("VintageStory-Chinese-Language-Package-")
                  and asset["name"].endswith(".zip")]
        if len(assets) > 1:
            raise ValueError(f"Multiple language packs in release {release['tag_name']}")
        if assets:
            return release["tag_name"], assets[0]["name"]
    return None


def main():
    repository = os.environ["GITHUB_REPOSITORY"]
    result = subprocess.run(
        ["gh", "api", "--paginate", "--slurp", f"repos/{repository}/releases?per_page=100"],
        check=True, capture_output=True, text=True,
    )
    selected = select_previous([release for page in json.loads(result.stdout) for release in page])
    path = ""
    if selected:
        tag, name = selected
        if not re.fullmatch(r"VintageStory-Chinese-Language-Package-[A-Za-z0-9._-]+\.zip", name):
            raise ValueError("Invalid release asset name")
        destination = Path("build/previous")
        destination.mkdir(parents=True, exist_ok=True)
        path = str(destination / "previous.zip")
        subprocess.run(["gh", "release", "download", tag, "--repo", repository,
                        "--pattern", name, "--output", path, "--clobber"], check=True)
        print(f"Comparing against {tag}: {path}")
    else:
        print("No previously published language pack; this is the initial package.")
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"previous_package={path}\n")


if __name__ == "__main__":
    main()
