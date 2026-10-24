import test from 'node:test';
import assert from 'node:assert/strict';
import { parsePublicationBody, renderPublicationBody } from '../dist/publication-body.mjs';

function fixture(body) {
  class Element {
    constructor(tagName, ownerDocument) { this.tagName = tagName; this.ownerDocument = ownerDocument; this.children = []; }
    append(...nodes) { this.children.push(...nodes); }
    replaceChildren(...nodes) { this.children = nodes; }
    set textContent(value) { this.replaceChildren({ textContent: value }); }
    get textContent() { return this.children.map(child => child.textContent).join(''); }
    set innerHTML(_) { throw new Error('Publication text must never be parsed as HTML.'); }
  }
  const document = {
    createElement: tagName => new Element(tagName, document),
    createTextNode: textContent => ({ textContent }),
  };
  const container = document.createElement('div');
  container.textContent = 'Previous article';
  renderPublicationBody(container, body);
  return container;
}

function descendants(node, tagName) {
  return node.children?.flatMap(child => [
    ...(child.tagName === tagName ? [child] : []), ...descendants(child, tagName),
  ]) || [];
}

test('French beta publication keeps prose and promotes its existing accented section labels', () => {
  const body = 'L’Île de l’Aube vous attend. Nous ouvrons les inscriptions à la bêta fermée d’Arkya pour préparer les prochaines sessions de test.\n\n'
    + 'COMMENT S’INSCRIRE\nRendez-vous sur https://arkya.online/beta.html?lang=fr, connectez-vous avec Discord, puis confirmez votre inscription.\n\n'
    + 'COMMENT JOUER APRÈS ATTRIBUTION DE L’ACCÈS\nLa bêta utilise le client Windows x64 et le launcher officiel : https://arkya.online/play.html?lang=fr.';
  const blocks = parsePublicationBody(body);
  assert.deepEqual(blocks.map(block => block.type), ['paragraph', 'heading', 'paragraph', 'heading', 'paragraph']);
  assert.equal(blocks[1].text, 'COMMENT S’INSCRIRE');
  assert.equal(blocks[3].text, 'COMMENT JOUER APRÈS ATTRIBUTION DE L’ACCÈS');
  const rendered = fixture(body);
  assert.equal(descendants(rendered, 'h2').length, 2);
  assert.deepEqual(descendants(rendered, 'a').map(link => link.href), [
    'https://arkya.online/beta.html?lang=fr', 'https://arkya.online/play.html?lang=fr',
  ]);
});

test('English beta publication gives standalone labels a heading without changing prose', () => {
  const body = 'The Isle of Dawn awaits. Registration for Arkya’s closed beta is now open as we prepare the next test sessions.\n\n'
    + 'HOW TO REGISTER\nVisit https://arkya.online/beta.html?lang=en, sign in with Discord, and confirm your registration.\n\n'
    + 'SHARE YOUR FEEDBACK\nIn game, F8 opens a bug report and F9 opens a suggestion.';
  const rendered = fixture(body);
  assert.deepEqual(rendered.children.map(node => node.tagName), ['p', 'h2', 'p', 'h2', 'p']);
  assert.equal(descendants(rendered, 'h2')[0].textContent, 'HOW TO REGISTER');
  assert.equal(descendants(rendered, 'p')[2].textContent, 'In game, F8 opens a bug report and F9 opens a suggestion.');
  assert.equal(descendants(rendered, 'a')[0].href, 'https://arkya.online/beta.html?lang=en');
});

test('published release-note headings can lead directly into consecutive bullets', () => {
  const body = 'INSTALLATION AND PLAY\n'
    + '- A self-contained Windows x64 installer installs the launcher for your user, with folder selection and shortcuts. The .NET runtime is included.\n'
    + '- The game folder is selected separately and can be on another drive.\n\n'
    + 'CONNEXION DISCORD\nLa connexion Discord précède l’accueil du launcher.';
  const blocks = parsePublicationBody(body);
  assert.deepEqual(blocks.map(block => block.type), ['heading', 'list', 'heading', 'paragraph']);
  assert.equal(blocks[1].items.length, 2);
  const rendered = fixture(body);
  assert.deepEqual(rendered.children.map(node => node.tagName), ['h2', 'ul', 'h2', 'p']);
  assert.equal(descendants(rendered, 'li')[1].textContent, 'The game folder is selected separately and can be on another drive.');
});

test('CRLF and CR text has blank-separated paragraphs with single newlines preserved', () => {
  assert.deepEqual(parsePublicationBody('\r\nFirst line\r\nSecond line\r\n\r\n\r\nSecond paragraph\rThird line\r'), [
    { type: 'paragraph', text: 'First line\nSecond line' },
    { type: 'paragraph', text: 'Second paragraph\nThird line' },
  ]);
  assert.equal(fixture('First line\nSecond line').children[0].textContent, 'First line\nSecond line');
});

test('both bullet markers form lists and stop at prose or a blank line', () => {
  assert.deepEqual(parsePublicationBody('Introduction\n- One\n• Two\nFollowing prose\n\n- Separate list'), [
    { type: 'paragraph', text: 'Introduction' }, { type: 'list', items: ['One', 'Two'] },
    { type: 'paragraph', text: 'Following prose' }, { type: 'list', items: ['Separate list'] },
  ]);
});

test('heading inference ignores markup, letterless labels, long lines, and inline capitals', () => {
  for (const text of ['# TITLE', '**TITLE**', '<H2>TITLE</H2>', '[TITLE](destination)', '1234 — !',
    'É'.repeat(101), 'HTTPS://EXAMPLE.COM', 'Prose\nWINDOWS\nMore prose']) {
    assert.deepEqual(parsePublicationBody(text), [{ type: 'paragraph', text }], text);
  }
  assert.deepEqual(parsePublicationBody('ÉTAPES — VERSION 2'), [{ type: 'heading', text: 'ÉTAPES — VERSION 2' }]);
});

test('HTML and Markdown remain literal text and cannot create executable elements', () => {
  const body = '<img src=x onerror="window.newsInjected=true">\n<script>window.newsInjected=true</script>\n'
    + '**bold** [label](javascript:alert(1))\n# Heading';
  const rendered = fixture(body);
  assert.deepEqual(rendered.children.map(node => node.tagName), ['p']);
  assert.equal(rendered.textContent, body);
  for (const tag of ['img', 'script', 'a', 'h1', 'h2']) assert.equal(descendants(rendered, tag).length, 0);
});

test('only valid HTTP(S) destinations become links and literal surrounding markup is preserved', () => {
  const body = '[Guide](https://arkya.online/play.html)\n'
    + 'javascript:alert(1) data:text/html,test ftp://example.com https://[bad] https://user:password@example.com\n'
    + 'http://127.0.0.1:51806/news.html?lang=fr#article';
  const rendered = fixture(body);
  assert.equal(rendered.textContent, body);
  assert.deepEqual(descendants(rendered, 'a').map(link => link.href), [
    'https://arkya.online/play.html', 'http://127.0.0.1:51806/news.html?lang=fr#article',
  ]);
  assert.ok(descendants(rendered, 'a').every(link => link.rel === 'noreferrer'));
});

test('URL sentence punctuation stays outside links while balanced parentheses remain part of URLs', () => {
  const body = 'https://example.com/a.,;!? https://example.com/path_(item). '
    + '(https://example.com/next_(item)). [https://example.com/final].';
  const rendered = fixture(body);
  assert.equal(rendered.textContent, body);
  assert.deepEqual(descendants(rendered, 'a').map(link => link.textContent), [
    'https://example.com/a', 'https://example.com/path_(item)',
    'https://example.com/next_(item)', 'https://example.com/final',
  ]);
});

test('URLs in list items use the same safe rendering beneath a section heading', () => {
  const rendered = fixture('DOWNLOAD\n• Install: https://arkya.online/play.html?lang=en.\n• Feedback: https://arkya.online/news.html');
  assert.equal(descendants(rendered, 'h2').length, 1);
  assert.equal(descendants(rendered, 'li').length, 2);
  assert.equal(descendants(rendered, 'a').length, 2);
  assert.equal(descendants(rendered, 'li')[0].textContent, 'Install: https://arkya.online/play.html?lang=en.');
});

test('rendering an empty publication clears the preceding article and uses no global document', () => {
  assert.deepEqual(parsePublicationBody(' \r\n\n'), []);
  const rendered = fixture('');
  assert.deepEqual(rendered.children, []);
  assert.equal(rendered.textContent, '');
  assert.throws(() => parsePublicationBody(null), TypeError);
});
