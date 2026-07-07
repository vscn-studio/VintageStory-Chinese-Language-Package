from __future__ import annotations

import argparse
import concurrent.futures
import datetime as dt
import json
import re
import socket
import sys
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from functools import cmp_to_key
from pathlib import Path
from typing import Any

BUILTIN_VERSION = "builtin"
STATUS_REPOSITORY = "仓库维护"
STATUS_AUTHOR_BUILTIN = "作者内置"


@dataclass(frozen=True)
class OfficialModData:
    name: str
    homepage: str
    latest_version: str
    updated_at: str


@dataclass(frozen=True)
class IndexLookupEntry:
    key: str
    data: dict[str, Any]


@dataclass(frozen=True)
class Contributor:
    name: str
    url: str = ""
    role: str = ""


@dataclass
class ModRecord:
    slug: str
    index_key: str
    chinese_name: str
    english_name: str
    contributors: list[Contributor]
    homepage: str
    repository_modids: list[str]
    api_candidate_modids: list[str]
    repository_versions: list[str]
    repository_latest: str
    status: str = STATUS_REPOSITORY
    official_latest: str = ""
    official_updated_at: str = ""
    official_found: bool = False
    needs_update: bool = False


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", default=".", help="Repository root directory")
    parser.add_argument("--output", required=True, help="Output markdown file path")
    parser.add_argument("--timeout", type=int, default=30, help="HTTP timeout in seconds")
    parser.add_argument("--workers", type=int, default=4, help="Concurrent fetch workers")
    args = parser.parse_args()

    repo_root = Path(args.repo_root).resolve()
    output_path = Path(args.output).resolve()
    timeout = max(1, args.timeout)
    workers = max(1, args.workers)

    index_map = load_index_map(repo_root / "projects" / "assets" / "index.json")
    records = scan_repository(repo_root, index_map)
    official_map = fetch_official_metadata(records, index_map, timeout, workers)

    for record in records:
        official = official_map.get(record.slug)
        if official is None:
            continue

        record.official_found = True
        if official.homepage and not record.homepage:
            record.homepage = official.homepage
        if official.name and record.english_name.casefold() == record.slug.casefold():
            record.english_name = official.name
        record.official_latest = official.latest_version
        record.official_updated_at = official.updated_at
        if record.official_latest and record.status != STATUS_AUTHOR_BUILTIN:
            record.needs_update = compare_versions(record.repository_latest, record.official_latest) < 0

    records.sort(key=lambda item: (not item.needs_update, item.chinese_name.casefold(), item.english_name.casefold()))

    markdown = render_markdown(records)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(markdown, encoding="utf-8")

    print(f"wrote {output_path}")
    print(f"mods={len(records)} outdated={sum(1 for item in records if item.needs_update)} missing_official={sum(1 for item in records if not item.official_found)}")
    return 0


def load_index_map(path: Path) -> dict[str, dict[str, Any]]:
    if not path.is_file():
        return {}

    with path.open("r", encoding="utf-8") as handle:
        payload = json.load(handle)

    result: dict[str, dict[str, Any]] = {}
    if isinstance(payload, dict):
        for key, value in payload.items():
            if isinstance(key, str) and isinstance(value, dict):
                result[key] = value
    return result


def scan_repository(repo_root: Path, index_map: dict[str, dict[str, Any]]) -> list[ModRecord]:
    content_root = repo_root / "projects" / "assets"
    index_lookup = build_index_lookup(index_map)
    records: list[ModRecord] = []
    if not content_root.is_dir():
        return records

    for slug_dir in sorted((path for path in content_root.iterdir() if path.is_dir()), key=lambda path: path.name.casefold()):
        slug = slug_dir.name

        versions: list[str] = []
        modids: list[str] = []
        version_modids_by_version: dict[str, list[str]] = {}
        builtin_versions: set[str] = set()
        for version_dir in sorted((path for path in slug_dir.iterdir() if path.is_dir()), key=lambda path: path.name.casefold()):
            current_version_modids, has_builtin = scan_version_directory(version_dir)
            if current_version_modids:
                versions.append(version_dir.name)
                modids.extend(current_version_modids)
                version_modids_by_version[version_dir.name] = current_version_modids
                if has_builtin:
                    builtin_versions.add(version_dir.name.casefold())

        if not versions:
            continue

        unique_versions = dedupe_sorted_versions(versions)
        repository_latest = unique_versions[0]
        api_candidate_modids = dedupe_case_insensitive(modids)
        latest_modids = version_modids_by_version.get(repository_latest, [])
        unique_modids = dedupe_case_insensitive(latest_modids or api_candidate_modids)
        index_lookup_entry = resolve_index_entry(index_lookup, [slug, *unique_modids, *api_candidate_modids])
        index_entry = index_lookup_entry.data
        if is_index_builtin(index_entry):
            continue

        chinese_name = text_or_default(index_entry.get("translation"), slug)
        english_name = text_or_default(index_entry.get("name"), slug)
        contributors = normalize_contributors(index_entry.get("contributors"))
        homepage = text_or_default(index_entry.get("homepage"), build_homepage_from_index(index_entry))
        status = STATUS_AUTHOR_BUILTIN if repository_latest.casefold() in builtin_versions else STATUS_REPOSITORY

        records.append(
            ModRecord(
                slug=slug,
                index_key=index_lookup_entry.key,
                chinese_name=chinese_name,
                english_name=english_name,
                contributors=contributors,
                homepage=homepage,
                repository_modids=unique_modids,
                api_candidate_modids=api_candidate_modids,
                repository_versions=unique_versions,
                repository_latest=repository_latest,
                status=status,
            )
        )

    return records


def is_index_builtin(index_entry: dict[str, Any]) -> bool:
    return text_or_default(index_entry.get("latestVersion"), "").casefold() == BUILTIN_VERSION


def scan_version_directory(version_dir: Path) -> tuple[list[str], bool]:
    modids: list[str] = []
    has_builtin = False
    for modid_dir in sorted((path for path in version_dir.iterdir() if path.is_dir()), key=lambda path: path.name.casefold()):
        if not modid_dir.is_dir():
            continue
        lang_dir = modid_dir / "lang"
        if (lang_dir / "builtin").is_file():
            modids.append(modid_dir.name)
            has_builtin = True
            continue
        if (lang_dir / "zh-cn.json").is_file():
            modids.append(modid_dir.name)
    return modids, has_builtin


def fetch_official_metadata(
    records: list[ModRecord],
    index_map: dict[str, dict[str, Any]],
    timeout: int,
    workers: int,
) -> dict[str, OfficialModData]:
    result: dict[str, OfficialModData] = {}
    if not records:
        return result
    index_lookup = build_index_lookup(index_map)

    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as executor:
        futures = {
            executor.submit(
                fetch_official_metadata_for_slug,
                record.slug,
                resolve_index_entry(index_lookup, [record.index_key, record.slug, *record.repository_modids, *record.api_candidate_modids]),
                record.api_candidate_modids,
                timeout,
            ): record.slug
            for record in records
        }
        for future in concurrent.futures.as_completed(futures):
            slug = futures[future]
            try:
                official = future.result()
            except Exception as exc:  # noqa: BLE001
                print(f"[warn] {slug}: {exc}", file=sys.stderr)
                continue
            if official is not None:
                result[slug] = official

    return result


def fetch_official_metadata_for_slug(
    slug: str,
    index_lookup_entry: IndexLookupEntry,
    api_candidate_modids: list[str],
    timeout: int,
) -> OfficialModData | None:
    index_entry = index_lookup_entry.data
    candidates = [slug, index_lookup_entry.key]
    candidates.extend(index_entry_candidates(index_lookup_entry.key, index_entry))
    candidates.extend(homepage_candidates(text_or_default(index_entry.get("homepage"), "")))
    candidates.extend(api_candidate_modids)

    seen: set[str] = set()
    for candidate in candidates:
        candidate = candidate.strip()
        candidate_key = candidate.casefold()
        if not candidate or candidate_key in seen:
            continue
        seen.add(candidate_key)
        payload = fetch_json(f"https://mods.vintagestory.at/api/mod/{urllib.parse.quote(candidate, safe='')}", timeout)
        official = convert_official_payload(payload, index_entry)
        if official is not None:
            return official

    return None


def convert_official_payload(payload: Any, index_entry: dict[str, Any]) -> OfficialModData | None:
    if not isinstance(payload, dict):
        return None

    if str(payload.get("statuscode", "")).strip() != "200":
        return None

    mod = payload.get("mod")
    if not isinstance(mod, dict):
        return None

    latest_release = None
    releases = mod.get("releases")
    if isinstance(releases, list):
        sortable = [release for release in releases if isinstance(release, dict) and text_or_default(release.get("modversion"), "")]
        if sortable:
            sortable.sort(key=lambda item: parse_dt(text_or_default(item.get("created"), "")), reverse=True)
            latest_release = sortable[0]

    name = text_or_default(mod.get("name"), text_or_default(index_entry.get("name"), ""))
    homepage = text_or_default(mod.get("urlalias"), "")
    if homepage:
        homepage = f"https://mods.vintagestory.at/{homepage.strip().strip('/')}"
    else:
        homepage = text_or_default(index_entry.get("homepage"), build_homepage_from_index(index_entry))
    if not homepage:
        mod_id = mod.get("modid")
        if isinstance(mod_id, int) and mod_id > 0:
            homepage = f"https://mods.vintagestory.at/show/mod/{mod_id}"

    latest_version = text_or_default(latest_release.get("modversion") if latest_release else "", "")
    updated_at = format_shanghai_time(first_non_empty(
        text_or_default(mod.get("lastreleased"), ""),
        text_or_default(latest_release.get("created") if latest_release else "", ""),
        text_or_default(mod.get("lastmodified"), ""),
        text_or_default(mod.get("created"), ""),
    ))

    if not latest_version:
        return None

    return OfficialModData(
        name=name or "",
        homepage=homepage or "",
        latest_version=latest_version,
        updated_at=updated_at,
    )


def fetch_json(url: str, timeout: int) -> Any:
    last_error: Exception | None = None
    for _ in range(3):
        request = urllib.request.Request(url, headers={"User-Agent": "VSCN-ModVersionCheck/1.0"})
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                return json.loads(response.read().decode("utf-8"))
        except (urllib.error.URLError, TimeoutError, socket.timeout) as exc:
            last_error = exc

    if last_error is not None:
        raise last_error

    raise RuntimeError(f"Failed to fetch {url}")


def parse_dt(value: str) -> dt.datetime:
    value = value.strip()
    if not value:
        return dt.datetime.min
    normalized = value.replace(" ", "T")
    try:
        parsed = dt.datetime.fromisoformat(normalized)
    except ValueError:
        return dt.datetime.min
    if parsed.tzinfo is None:
        return parsed.replace(tzinfo=dt.timezone.utc)
    return parsed


def format_shanghai_time(value: str) -> str:
    if not value:
        return ""

    parsed = parse_dt(value)
    if parsed == dt.datetime.min.replace(tzinfo=dt.timezone.utc):
        return value

    return parsed.astimezone(dt.timezone(dt.timedelta(hours=8))).strftime("%Y-%m-%d %H:%M:%S")


def compare_versions(left: str, right: str) -> int:
    left_parsed = parse_version(left)
    right_parsed = parse_version(right)
    if left_parsed is not None and right_parsed is not None:
        return compare_version_tuple(left_parsed, right_parsed)

    left_clean = left.strip().casefold()
    right_clean = right.strip().casefold()
    if left_clean == right_clean:
        return 0
    return -1 if left_clean < right_clean else 1


def parse_version(value: str) -> tuple[tuple[int, ...], tuple[tuple[int, object], ...] | None] | None:
    value = value.strip()
    if not value:
        return None
    if value[0] in {"v", "V"}:
        value = value[1:]

    match = re.match(r"^(\d+(?:\.\d+)*)(?:-([0-9A-Za-z.-]+))?(?:\+.*)?$", value)
    if not match:
        return None

    core = tuple(int(part) for part in match.group(1).split("."))
    pre = match.group(2)
    if pre is None:
        return core, None

    prerelease: list[tuple[int, object]] = []
    for part in pre.split("."):
        if part.isdigit():
            prerelease.append((0, int(part)))
        else:
            prerelease.append((1, part.casefold()))
    return core, tuple(prerelease)


def compare_version_tuple(
    left: tuple[tuple[int, ...], tuple[tuple[int, object], ...] | None],
    right: tuple[tuple[int, ...], tuple[tuple[int, object], ...] | None],
) -> int:
    left_core, left_pre = left
    right_core, right_pre = right

    max_len = max(len(left_core), len(right_core))
    left_core = left_core + (0,) * (max_len - len(left_core))
    right_core = right_core + (0,) * (max_len - len(right_core))
    if left_core != right_core:
        return -1 if left_core < right_core else 1

    if left_pre is None and right_pre is None:
        return 0
    if left_pre is None:
        return 1
    if right_pre is None:
        return -1

    for left_part, right_part in zip(left_pre, right_pre):
        if left_part == right_part:
            continue
        left_kind, left_value = left_part
        right_kind, right_value = right_part
        if left_kind != right_kind:
            return -1 if left_kind < right_kind else 1
        return -1 if left_value < right_value else 1

    if len(left_pre) == len(right_pre):
        return 0
    return -1 if len(left_pre) < len(right_pre) else 1


def dedupe_sorted_versions(values: list[str]) -> list[str]:
    unique = sorted({value.strip() for value in values if value.strip()}, key=cmp_to_key(compare_versions), reverse=True)
    return unique


def dedupe_case_insensitive(values: list[str]) -> list[str]:
    seen: set[str] = set()
    result: list[str] = []
    for value in values:
        text = text_or_default(value, "")
        if not text:
            continue
        key = text.casefold()
        if key in seen:
            continue
        seen.add(key)
        result.append(text)
    return result


def build_index_lookup(index_map: dict[str, dict[str, Any]]) -> dict[str, IndexLookupEntry]:
    lookup: dict[str, IndexLookupEntry] = {}

    for key, index_entry in index_map.items():
        add_index_lookup_candidate(lookup, key, IndexLookupEntry(key, index_entry), overwrite=True)

    for key, index_entry in index_map.items():
        lookup_entry = resolve_index_entry(lookup, [key])
        for candidate in index_entry_candidates(key, index_entry):
            add_index_lookup_candidate(lookup, candidate, lookup_entry, overwrite=False)

    return lookup


def add_index_lookup_candidate(
    lookup: dict[str, IndexLookupEntry],
    candidate: str,
    lookup_entry: IndexLookupEntry,
    *,
    overwrite: bool,
) -> None:
    normalized = text_or_default(candidate, "").casefold()
    if not normalized:
        return
    if overwrite or normalized not in lookup:
        lookup[normalized] = lookup_entry


def resolve_index_entry(lookup: dict[str, IndexLookupEntry], candidates: list[str]) -> IndexLookupEntry:
    for candidate in candidates:
        normalized = text_or_default(candidate, "").casefold()
        if not normalized:
            continue
        lookup_entry = lookup.get(normalized)
        if lookup_entry is not None:
            return lookup_entry
    return IndexLookupEntry("", {})


def index_entry_candidates(key: str, index_entry: dict[str, Any]) -> list[str]:
    candidates = [key]
    candidates.extend(homepage_candidates(text_or_default(index_entry.get("homepage"), "")))

    alias = text_or_default(index_entry.get("urlalias"), "")
    if alias:
        candidates.append(alias.strip().strip("/"))

    mod_id = index_entry.get("modid")
    if isinstance(mod_id, int) and mod_id > 0:
        candidates.append(str(mod_id))

    return candidates


def homepage_candidates(homepage: str) -> list[str]:
    if not homepage:
        return []

    try:
        parsed = urllib.parse.urlparse(homepage)
    except ValueError:
        return []

    if parsed.netloc.lower() != "mods.vintagestory.at":
        return []

    segments = [segment for segment in parsed.path.split("/") if segment]
    if not segments:
        return []

    if len(segments) >= 3 and segments[0].lower() == "show" and segments[1].lower() == "mod":
        return [segments[2]]

    return [segments[-1]]


def build_homepage_from_index(index_entry: dict[str, Any]) -> str:
    homepage = text_or_default(index_entry.get("homepage"), "")
    if homepage:
        return homepage

    alias = text_or_default(index_entry.get("urlalias"), "")
    if alias:
        return f"https://mods.vintagestory.at/{alias.strip().strip('/')}"

    mod_id = index_entry.get("modid")
    if isinstance(mod_id, int) and mod_id > 0:
        return f"https://mods.vintagestory.at/show/mod/{mod_id}"

    return ""


def render_markdown(records: list[ModRecord]) -> str:
    lines: list[str] = []
    outdated = [record for record in records if record.needs_update]
    missing = sum(1 for record in records if not record.official_found)

    lines.append("# 模组最新版本检测")
    lines.append("")
    lines.append(f"- 总模组数：{len(records)}")
    lines.append(f"- 需要更新：{len(outdated)}")
    lines.append(f"- 官方数据缺失：{missing}")
    lines.append(f"- 生成时间（UTC+8）：{format_shanghai_time(dt.datetime.now(dt.timezone.utc).strftime('%Y-%m-%d %H:%M:%S'))}")
    lines.append("")
    lines.append("## 待更新模组")
    lines.append("")

    if outdated:
        lines.append("| 模组中文名称 | 模组英文名称 | 模组ID | 贡献者 | 仓库翻译版本 | 模组最新版本 | 模组更新时间 |")
        lines.append("| --- | --- | --- | --- | --- | --- | --- |")
        for record in outdated:
            modids = "<br>".join(escape_cell(modid) for modid in record.repository_modids) if record.repository_modids else "未记录"
            contributors = format_contributors(record.contributors)
            latest_version = escape_cell(record.official_latest) if record.official_latest else "未获取"
            updated_at = escape_cell(record.official_updated_at) if record.official_updated_at else "未获取"
            lines.append(
                f"| {format_link(record.chinese_name, record.homepage)} | {format_link(record.english_name, record.homepage)} | {modids} | {contributors} | "
                f"{escape_cell(record.repository_latest)} | {latest_version} | {updated_at} |"
            )
    else:
        lines.append("- 无")

    lines.append("")
    lines.append("## 模组版本表")
    lines.append("")
    lines.append("| 模组中文名称 | 模组英文名称 | 模组ID | 状态 | 贡献者 | 仓库翻译版本 | 模组最新版本 | 模组更新时间 |")
    lines.append("| --- | --- | --- | --- | --- | --- | --- | --- |")

    for record in records:
        modids = "<br>".join(escape_cell(modid) for modid in record.repository_modids) if record.repository_modids else "未记录"
        contributors = format_contributors(record.contributors)
        repo_versions = "<br>".join(escape_cell(version) for version in record.repository_versions)
        latest_version = escape_cell(record.official_latest) if record.official_latest else "未获取"
        updated_at = escape_cell(record.official_updated_at) if record.official_updated_at else "未获取"
        lines.append(
            f"| {format_link(record.chinese_name, record.homepage)} | {format_link(record.english_name, record.homepage)} | {modids} | {escape_cell(record.status)} | {contributors} | {repo_versions} | {latest_version} | {updated_at} |"
        )

    lines.append("")
    return "\n".join(lines)


def normalize_contributors(value: Any) -> list[Contributor]:
    if not isinstance(value, list):
        return []

    contributors: list[Contributor] = []
    seen: set[tuple[str, str, str]] = set()
    for item in value:
        if isinstance(item, str):
            contributor = Contributor(text_or_default(item, ""))
        elif isinstance(item, dict):
            contributor = Contributor(
                text_or_default(item.get("name"), ""),
                text_or_default(item.get("url"), ""),
                text_or_default(item.get("role"), ""),
            )
        else:
            continue

        if not contributor.name:
            continue

        key = (contributor.name.casefold(), contributor.url.casefold(), contributor.role.casefold())
        if key in seen:
            continue

        seen.add(key)
        contributors.append(contributor)

    return contributors


def format_contributors(contributors: list[Contributor]) -> str:
    if not contributors:
        return "未记录"

    return "<br>".join(format_contributor(contributor) for contributor in contributors)


def format_contributor(contributor: Contributor) -> str:
    label = format_link(contributor.name, contributor.url)
    if contributor.role:
        return f"{label} ({escape_cell(contributor.role)})"
    return label


def format_link(text: str, url: str) -> str:
    label = escape_cell(text or "")
    if not url:
        return label
    safe_url = url.replace("(", "%28").replace(")", "%29")
    return f"[{label}]({safe_url})"


def escape_cell(value: str) -> str:
    return (value or "").replace("\\", "\\\\").replace("|", "\\|").replace("\r", " ").replace("\n", " ").strip()


def text_or_default(value: Any, default: str) -> str:
    if value is None:
        return default
    text = str(value).strip()
    return text if text else default


def first_non_empty(*values: str) -> str:
    for value in values:
        if value:
            return value
    return ""


if __name__ == "__main__":
    raise SystemExit(main())
