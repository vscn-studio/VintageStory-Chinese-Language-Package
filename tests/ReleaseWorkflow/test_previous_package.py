import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[2] / ".github/scripts/download_previous_package.py"
spec = importlib.util.spec_from_file_location("download_previous_package", SCRIPT)
download = importlib.util.module_from_spec(spec)
spec.loader.exec_module(download)


def release(tag, date, names, **flags):
    return dict(tag_name=tag, published_at=date, assets=[{"name": name} for name in names], **flags)


class PreviousPackageTests(unittest.TestCase):
    def test_selects_latest_published_package_including_prereleases(self):
        old = "VintageStory-Chinese-Language-Package-1.0.0.zip"
        new = "VintageStory-Chinese-Language-Package-1.0.1.zip"
        releases = [
            release("v1", "2026-11-01", [old]),
            release("draft", "2026-11-14", [new], draft=True),
            release("v2", "2026-11-13", [new], prerelease=True),
            release("updater-only", "2026-11-14", ["updater.zip"]),
        ]
        self.assertEqual(("v2", new), download.select_previous(releases))

    def test_rejects_ambiguous_packages(self):
        with self.assertRaises(ValueError):
            download.select_previous([release("v1", "2026-11-01", [
                "VintageStory-Chinese-Language-Package-1.0.0.zip",
                "VintageStory-Chinese-Language-Package-1.0.1.zip",
            ])])

    def test_no_previous_release_is_the_only_initial_release_fallback(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "output"
            with patch.dict(os.environ, {"GITHUB_REPOSITORY": "owner/repo", "GITHUB_OUTPUT": str(output)}):
                with patch.object(download.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "[[]]")):
                    download.main()
                self.assertEqual("previous_package=\n", output.read_text())
                with patch.object(download.subprocess, "run", side_effect=subprocess.CalledProcessError(1, "gh")):
                    with self.assertRaises(subprocess.CalledProcessError):
                        download.main()
                self.assertEqual("previous_package=\n", output.read_text())

    def test_download_failure_does_not_publish_a_baseline(self):
        releases = [release("v1", "2026-11-01", ["VintageStory-Chinese-Language-Package-1.0.0.zip"])]
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "output"
            with patch.dict(os.environ, {"GITHUB_REPOSITORY": "owner/repo", "GITHUB_OUTPUT": str(output)}):
                results = [subprocess.CompletedProcess([], 0, json.dumps([releases])), subprocess.CalledProcessError(1, "gh")]
                with patch.object(download.subprocess, "run", side_effect=results):
                    with self.assertRaises(subprocess.CalledProcessError):
                        download.main()
                self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
