"""Turns Aozora Bunko's text of Rashomon (Akutagawa Ryunosuke, 1915) into rashomon/rashomon.json for the vertical sample.

    python3 samples/documents/tools/rashomon_extract.py <rashomon.txt from 127_ruby_150.zip> samples/documents/rashomon/rashomon.json

The input is the Shift_JIS file in https://www.aozora.gr.jp/cards/000879/files/127_ruby_150.zip. The output keeps the title, the
author, every paragraph and the colophon (the source edition and the volunteers who typed and proofread the text, which Aozora Bunko
asks to keep with the text). Ruby (《reading》 after a run of kanji, or after ｜ where the run is marked) becomes [base, reading]
pairs; the two characters the file describes in ［＃...］ notes (not in JIS X 0208) are replaced by the Unicode characters they name.
"""

import json
import re
import sys

# The notes the file uses for characters outside JIS X 0208, and the characters they describe.
GAIJI = {
    "※［＃「てへん＋丑」、第4水準2-12-93］": "扭",  # 扭
    "※［＃「目＋匡」、第3水準1-88-81］": "眶",  # 眶
}

KANJI = re.compile(r"[㐀-鿿豈-﫿々〆ヶ]")


def segments(line: str) -> list:
    """Splits a line into plain strings and [base, ruby] pairs."""
    out: list = []
    text = ""
    i = 0
    while i < len(line):
        ch = line[i]
        if ch == "｜":
            out.append(text)
            text = ""
            i += 1
            continue
        if ch == "《":
            end = line.index("》", i)
            ruby = line[i + 1:end]
            # The base: everything after an explicit ｜, else the run of kanji just before the ruby.
            if out and isinstance(out[-1], str) and text and line[:i].rfind("｜") > line[:i].rfind("》"):
                base, text = text, ""
            else:
                j = len(text)
                while j > 0 and KANJI.match(text[j - 1]):
                    j -= 1
                base, text = text[j:], text[:j]
            out.append(text)
            out.append([base, ruby])
            text = ""
            i = end + 1
            continue
        text += ch
        i += 1
    out.append(text)
    # Merge neighboring strings and drop empty ones.
    merged: list = []
    for part in out:
        if isinstance(part, str):
            if not part:
                continue
            if merged and isinstance(merged[-1], str):
                merged[-1] += part
                continue
        merged.append(part)
    return merged


def main() -> None:
    raw = open(sys.argv[1], "rb").read().decode("shift_jis")
    for note, char in GAIJI.items():
        raw = raw.replace(note, char)
    lines = raw.replace("\r\n", "\n").split("\n")
    title, author = lines[0].strip(), lines[1].strip()
    # The body is between the second rule of hyphens (the end of the notation key) and the colophon.
    rules = [i for i, line in enumerate(lines) if line.startswith("-----")]
    start = rules[1] + 1
    colophon = next(i for i, line in enumerate(lines) if line.startswith("底本："))
    paragraphs, date = [], None
    for line in lines[start:colophon]:
        line = line.rstrip()
        if not line:
            continue
        if line.startswith("［＃地から１字上げ］"):
            date = line.replace("［＃地から１字上げ］", "")
            continue
        if "［＃" in line:
            raise SystemExit(f"unexpected note: {line}")
        paragraphs.append(segments(line))
    tail = [line.rstrip() for line in lines[colophon:] if line.strip()]
    json.dump({"title": title, "author": author, "date": date, "paragraphs": paragraphs, "colophon": tail},
              open(sys.argv[2], "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(f"{len(paragraphs)} paragraphs, {sum(len(''.join(s if isinstance(s, str) else s[0] for s in p)) for p in paragraphs)} characters")


if __name__ == "__main__":
    main()
