"""Counts Arabic words in an Arabic Wikipedia dump, for the optional Wikipedia dictionary.

Usage: python -I wiki-counts.py <arwiki-...-pages-articles.xml.bz2> <output.txt> [--min-count 3]

Reads the compressed dump as a stream (articles only, redirects skipped), removes the wiki markup that
is not prose (templates, references, tables, links' targets, comments, tags), strips tashkeel and
tatweel, and counts runs of Arabic letters of 2 to 25 letters. Writes "word count" lines, most frequent
first: the format DictionaryBuilder reads with --source. Wikipedia text is CC BY-SA 4.0, so the derived
list and dictionary are CC BY-SA 4.0 too (see THIRD_PARTY_NOTICES.md). Build-time only: nothing here
ships with the app.
"""
import bz2
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter

TASHKEEL = re.compile('[ً-ْٰـ]')  # harakat, tanween, dagger alef, tatweel
WORD = re.compile('[ء-غف-يٱ-ۓ]{2,25}')
COMMENT = re.compile(r'<!--.*?-->', re.S)
REF = re.compile(r'<ref[^>/]*?/>|<ref[^>]*?>.*?</ref>', re.S | re.I)
TAG = re.compile(r'<[^>]+>')
TABLE_LINE = re.compile(r'^\s*(\{\||\|\}|\|-|\||!).*$', re.M)
FILE_LINK = re.compile(r'\[\[(?:ملف|صورة|File|Image|تصنيف|Category):[^\]]*\]\]', re.I)
LINK = re.compile(r'\[\[(?:[^\]|]*\|)?([^\]]*)\]\]')  # keep the shown text
EXTERNAL = re.compile(r'\[https?://[^\s\]]+\s?([^\]]*)\]')


def strip_templates(text):
    # {{ ... }} can nest: remove the innermost ones until none is left.
    previous = None
    while previous != text:
        previous = text
        text = re.sub(r'\{\{[^{}]*\}\}', ' ', text)
    return text


def prose(text):
    text = COMMENT.sub(' ', text)
    text = REF.sub(' ', text)
    text = strip_templates(text)
    text = TABLE_LINE.sub(' ', text)
    text = FILE_LINK.sub(' ', text)
    text = LINK.sub(r'\1', text)
    text = EXTERNAL.sub(r'\1', text)
    text = TAG.sub(' ', text)
    return TASHKEEL.sub('', text)


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    source, output = sys.argv[1], sys.argv[2]
    min_count = int(sys.argv[sys.argv.index('--min-count') + 1]) if '--min-count' in sys.argv else 3

    counts = Counter()
    articles = 0
    namespace = None
    with bz2.open(source, 'rb') as stream:
        for _, element in ET.iterparse(stream, events=('end',)):
            tag = element.tag.rsplit('}', 1)[-1]
            if tag == 'ns':
                namespace = element.text
            elif tag == 'text':
                text = element.text or ''
                if namespace == '0' and not text.lstrip().startswith(('#REDIRECT', '#تحويل', '#redirect')):
                    counts.update(WORD.findall(prose(text)))
                    articles += 1
                    if articles % 50000 == 0:
                        print(f'{articles} articles, {len(counts)} distinct words', file=sys.stderr, flush=True)
            elif tag == 'page':
                element.clear()
                namespace = None

    with open(output, 'w', encoding='utf-8', newline='\n') as out:
        for word, count in counts.most_common():
            if count < min_count:
                break
            out.write(f'{word} {count}\n')
    print(f'done: {articles} articles, {sum(1 for c in counts.values() if c >= min_count)} words written', file=sys.stderr)


if __name__ == '__main__':
    main()
