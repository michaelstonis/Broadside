"""Builds dictionary/dictionary.html from a slice of Webster's Unabridged Dictionary (1913 edition, public domain in the US).

    python3 samples/documents/tools/webster_extract.py <pg29765.txt>

The input is Project Gutenberg eBook #29765 as plain text (https://www.gutenberg.org/ebooks/29765). Only the dictionary text is used:
the Project Gutenberg header, footer and licence are not copied, and the output does not use the Project Gutenberg name. The slice
runs from the first entry of FIRST until about MAX_CHARS of entry text. Entries containing any word in SKIP are left out, because a
1913 dictionary contains definitions that are offensive today and a sample document does not need them.
"""

import html
import os
import re
import sys

FIRST = 'QUA'
MAX_CHARS = 150_000
SKIP = re.compile(r'\b(negro|negroes|savage|savages|prostitute|harlot|whore|lewd|bastard|idiot|imbecile|squaw|coolie|jew|jews|gypsy|heathen|slut|wench|hottentot)\b', re.I)
HEADWORD = re.compile(r"^[A-Z][A-Z' ;,\-]*$")


def entries(lines):
    i = 0
    n = len(lines)
    while i < n:
        line = lines[i].rstrip()
        if HEADWORD.match(line) and i + 1 < n and lines[i + 1][:1].isupper():
            head = line
            body = []
            i += 1
            while i < n and not (HEADWORD.match(lines[i].rstrip()) and i + 1 < n and lines[i + 1][:1].isupper()):
                body.append(lines[i].rstrip())
                i += 1
            yield head, body
        else:
            i += 1


def render(head, body):
    text = '\n'.join(body).strip()
    first, _, rest = text.partition('\n')
    rest = rest.strip()
    # The first line is "Word, pos. Etym: [...]"; the etymology may run onto the next lines.
    m = re.match(r'([^,]+),(.*)', first)
    word, grammar = (m.group(1), m.group(2)) if m else (head.title(), first)
    word = word.replace('"', 'ˈ').replace('*', '·')  # Webster marks stress with a double quote and syllables with *.
    allrest = (grammar + '\n' + rest).strip()
    etym = ''
    em = re.search(r'Etym:\s*(\[.*?\])', allrest, re.S)
    if em:
        etym = em.group(1)
        allrest = allrest[:em.start()] + allrest[em.end():]
    allrest = re.sub(r'\s*\n\s*\n\s*', ' ', allrest)
    allrest = re.sub(r'\s*\n\s*', ' ', allrest)
    parts = []
    for para in allrest.split(' '):
        para = para.strip()
        if not para:
            continue
        para = html.escape(para.replace('Defn: ', ''))
        para = re.sub(r'^(\d+)\.\s*', r'<b class="n">\1.</b> ', para)
        para = para.replace(' -- ', ' — ').replace('-- ', '— ')
        para = re.sub(r'^Syn\.', '<span class="syn">Syn.</span>', para)
        para = re.sub(r'\((Zoöl|Bot|Mus|Law|Chem|Arch|Naut|Mech|Med|Anat|Geom|Gram|Math|Physics|Zool|Mil|Eccl|Her|Rhet|Logic|Script|Theol|Min|Geol|Astron|Print|Com|Cookery|Falconry|Sport|Mining|Metal|Paint|Pharm|Philos|Pros|Surg|Teleg|Elec|Biol|Physiol|Steam Engine|Shipbuilding)\.\)', r'<i>(\1.)</i>', para)
        parts.append(para)
    grammar_html, *senses = parts or ['']
    out = [f'<p class="entry"><b class="hw">{html.escape(word)}</b>{grammar_html}']
    if etym:
        out.append(f' <span class="etym">{html.escape(etym)}</span>')
    for sense in senses:
        out.append(' ' + sense)
    out.append('</p>')
    return ''.join(out), word


def main():
    lines = open(sys.argv[1], encoding='utf-8').read().split('\n')
    start = next(i for i, l in enumerate(lines) if l.rstrip() == FIRST and i > 1000)
    end = next(i for i, l in enumerate(lines) if l.startswith('*** END OF THE PROJECT GUTENBERG'))
    chunks = []
    total = 0
    skipped = 0
    for head, body in entries(lines[start:end]):
        joined = ' '.join(body)
        if SKIP.search(joined):
            skipped += 1
            continue
        rendered, word = render(head, body)
        chunks.append((word, rendered))
        total += len(rendered)
        if total > MAX_CHARS:
            break

    here = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'dictionary')
    template = open(os.path.join(here, 'template.html'), encoding='utf-8').read()
    body = []
    for word, rendered in chunks:
        # Running heads: the first and last headword on each page, through string-set on every entry.
        body.append(rendered.replace('<p class="entry">', f'<p class="entry" data-hw="{html.escape(word)}">', 1))
    with open(os.path.join(here, 'dictionary.html'), 'w', encoding='utf-8') as f:
        f.write(template.replace('<!-- ENTRIES -->', '\n'.join(body)).replace('{{FIRST}}', html.escape(chunks[0][0]))
                .replace('{{LAST}}', html.escape(chunks[-1][0])))
    print(f'{len(chunks)} entries ({skipped} skipped), {total:,} characters: {chunks[0][0]} to {chunks[-1][0]}')


if __name__ == '__main__':
    main()
