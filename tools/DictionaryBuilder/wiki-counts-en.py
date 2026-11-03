"""Counts English words in an English Wikipedia multistream dump, for the optional English Wikipedia dictionary.

Usage: python -I wiki-counts-en.py <enwiki-...-pages-articles-multistream.xml.bz2> <...-multistream-index.txt.bz2>
                                   <output.txt> [--min-count 5] [--workers 8]

The multistream dump is a series of independent bz2 streams of 100 pages each; the index gives their offsets, so
the streams are shared out between worker processes (the English dump is ten times the Arabic one). Like
wiki-counts.py: articles only (namespace 0), redirects skipped, the wiki markup that is not prose removed
(templates, references, tables, links' targets, comments, tags). Words are runs of Latin letters (accents allowed)
with apostrophes only between letters, 2 to 25 characters; possessives («London's») are not kept (the
subtitle list has the contractions such as «it's»). Their case is kept (Wikipedia writes «London», «NASA»),
except that a capitalised word starting a sentence is counted in lower case. Writes "word count" lines, most
frequent first: the format DictionaryBuilder reads with --source. Wikipedia text is CC BY-SA 4.0, so the derived
list and dictionary are CC BY-SA 4.0 too (see THIRD_PARTY_NOTICES.md). Build-time only: nothing here ships with
the app.
"""
import bz2
import html
import os
import re
import sys
from collections import Counter
from multiprocessing import Pool

LETTER = 'A-Za-zÀ-ÖØ-öø-ɏ'
WORD = re.compile(rf"(?<![{LETTER}])[{LETTER}]+(?:['’][{LETTER}]+)*(?![{LETTER}])")
# A capitalised word right after the end of a sentence, a line break or the start of the text.
SENTENCE_WORD = re.compile(rf"(?:\A|(?<=[.!?])[\"”’)]?\s+|(?<=\n)[ \t]*)([A-Z][a-zß-öø-ÿ]+)(?![{LETTER}'’])")
PAGE = re.compile(r'<page>(.*?)</page>', re.S)
NS = re.compile(r'<ns>(\d+)</ns>')
TEXT = re.compile(r'<text[^>]*>(.*?)</text>', re.S)
COMMENT = re.compile(r'<!--.*?-->', re.S)
REF = re.compile(r'<ref[^>/]*?/>|<ref[^>]*?>.*?</ref>', re.S | re.I)
TAG = re.compile(r'<[^>]+>')
TABLE_LINE = re.compile(r'^\s*(\{\||\|\}|\|-|\||!).*$', re.M)
FILE_LINK = re.compile(r'\[\[(?:File|Image|Category|Media):[^\]]*\]\]', re.I)
LINK = re.compile(r'\[\[(?:[^\]|]*\|)?([^\]]*)\]\]')  # keep the shown text
EXTERNAL = re.compile(r'\[https?://[^\s\]]+\s?([^\]]*)\]')
HEADING = re.compile(r'^=+\s*(.*?)\s*=+\s*$', re.M)
TEMPLATE = re.compile(r'\{\{[^{}]*\}\}')
STREAMS_PER_TASK = 2500   # one counter sent back per task: bounds the memory of each worker
STREAMS_PER_READ = 50     # decompressed at once (about 30 MB of text)


def strip_templates(text):
    # {{ ... }} can nest: remove the innermost ones until none is left.
    previous = None
    while previous != text:
        previous = text
        text = TEMPLATE.sub(' ', text)
    return text


def prose(text):
    text = COMMENT.sub(' ', text)
    text = REF.sub(' ', text)
    text = strip_templates(text)
    text = TABLE_LINE.sub(' ', text)
    text = FILE_LINK.sub(' ', text)
    text = LINK.sub(r'\1', text)
    text = EXTERNAL.sub(r'\1', text)
    text = HEADING.sub(r'\1.', text)  # a heading ends like a sentence
    text = TAG.sub(' ', text)
    return text.replace("'''", ' ').replace("''", ' ')  # bold and italic quotes, not apostrophes


def count_text(text, counts):
    # Counted in bulk (findall and Counter run in C), then «However» at the start of a sentence is moved to
    # the word «however»; «London» or «NASA» keep their case. Lengths and ’ are fixed when writing.
    counts.update(WORD.findall(text))
    for word in SENTENCE_WORD.findall(text):
        counts[word] -= 1
        counts[word.lower()] += 1


def count_streams(task):
    path, bounds = task
    counts = Counter()
    articles = 0
    with open(path, 'rb') as dump:
        for i in range(0, len(bounds) - 1, STREAMS_PER_READ):
            start, end = bounds[i], bounds[min(i + STREAMS_PER_READ, len(bounds) - 1)]
            dump.seek(start)
            data = bz2.decompress(dump.read(end - start)).decode('utf-8')
            for page in PAGE.finditer(data):
                body = page.group(1)
                ns = NS.search(body)
                text = TEXT.search(body)
                if not ns or ns.group(1) != '0' or not text or '<redirect' in body:
                    continue
                count_text(prose(html.unescape(text.group(1))), counts)
                articles += 1
    return counts, articles


def offsets(index_path, dump_size):
    starts = set()
    with bz2.open(index_path, 'rt', encoding='utf-8') as index:
        for line in index:
            starts.add(int(line.split(':', 1)[0]))
    return sorted(starts) + [dump_size]


def main():
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    dump, index, output = sys.argv[1], sys.argv[2], sys.argv[3]
    min_count = int(sys.argv[sys.argv.index('--min-count') + 1]) if '--min-count' in sys.argv else 5
    workers = int(sys.argv[sys.argv.index('--workers') + 1]) if '--workers' in sys.argv else os.cpu_count()

    bounds = offsets(index, os.path.getsize(dump))
    tasks = [(dump, bounds[i:i + STREAMS_PER_TASK + 1]) for i in range(0, len(bounds) - 1, STREAMS_PER_TASK)]
    print(f'{len(bounds) - 1} streams in {len(tasks)} tasks, {workers} workers', file=sys.stderr, flush=True)

    counts = Counter()
    articles = 0
    with Pool(workers) as pool:
        for done, (partial, partial_articles) in enumerate(pool.imap_unordered(count_streams, tasks), 1):
            counts.update(partial)
            articles += partial_articles
            print(f'{done}/{len(tasks)} tasks, {articles} articles, {len(counts)} distinct words', file=sys.stderr, flush=True)

    merged = Counter()
    for word, count in counts.items():
        if count > 0 and 2 <= len(word) <= 25 and not word.endswith(("'s", '’s')):
            merged[word.replace('’', "'")] += count
    with open(output, 'w', encoding='utf-8', newline='\n') as out:
        for word, count in merged.most_common():
            if count < min_count:
                break
            out.write(f'{word} {count}\n')
    print(f'done: {articles} articles, {sum(1 for c in merged.values() if c >= min_count)} words written', file=sys.stderr)


if __name__ == '__main__':
    main()
