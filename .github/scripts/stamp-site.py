"""Puts the current app version and the headline of the release notes into the site pages before deploy.

The version comes from APP_VERSION in .github/workflows/build.yml (the same value the release is built with),
the headline from the matching version section in .github/release-notes.md.
The version goes into every page under site/ (the guides show it next to the download button)
and into the SoftwareApplication data on the home page.
"""
import html
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parents[2]
build = (root / ".github/workflows/build.yml").read_text(encoding="utf-8")
notes = (root / ".github/release-notes.md").read_text(encoding="utf-8")
m = re.search(r'APP_VERSION:\s*"(\d+\.\d+\.\d+)"', build)
if not m:
    sys.exit("APP_VERSION not found in build.yml")
version = m.group(1)
# 1.3.0 -> 1.3, 1.2.1 stays 1.2.1
short = version[:-2] if version.endswith(".0") else version

section = re.search(rf"^## Что нового в {re.escape(version)}\s*\n(.*?)(?=^## |\Z)", notes, re.M | re.S)
current_notes = section.group(1) if section else ""
h = re.search(r"\*\*(.+?)\*\*", current_notes)
first_item = re.search(r"^- (.+)", current_notes, re.M)
headline = (h.group(1) if h else first_item.group(1).split(":", 1)[0] if first_item else "Обновление FrameLab").strip().rstrip(".")

for page_path in sorted((root / "site").rglob("*.html")):
    page = page_path.read_text(encoding="utf-8")
    page = re.sub(r'(<span class="ver">)[^<]*(</span>)', lambda x: x.group(1) + short + x.group(2), page)
    page = re.sub(r'("softwareVersion":\s*")[^"]*(")', lambda x: x.group(1) + version + x.group(2), page)
    if headline:
        page = re.sub(r'(<span class="ver-note">)[^<]*(</span>)', lambda x: x.group(1) + html.escape(headline) + x.group(2), page)
    page_path.write_text(page, encoding="utf-8")

print(f"site: version {short}, headline: {headline}")
