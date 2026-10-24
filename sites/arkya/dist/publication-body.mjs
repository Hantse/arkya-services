const headingCharacters = /^[\p{L}\p{N}\s’'&/():,.!?–—-]+$/u;
const bulletLine = /^\s*[-•]\s+(.+)$/u;

function isHeading(line) {
  return line.length <= 100 && /\p{L}/u.test(line) && headingCharacters.test(line)
    && line === line.toUpperCase() && line !== line.toLowerCase()
    && !/https?:\/\//iu.test(line);
}

export function parsePublicationBody(body) {
  if (typeof body !== 'string') throw new TypeError('A publication body must be text.');
  const blocks = [];
  let paragraph = [];
  let items = [];
  const flushParagraph = () => {
    if (paragraph.length) blocks.push({ type: 'paragraph', text: paragraph.join('\n').trim() });
    paragraph = [];
  };
  const flushList = () => {
    if (items.length) blocks.push({ type: 'list', items });
    items = [];
  };
  for (const line of body.replace(/\r\n?/g, '\n').split('\n')) {
    const text = line.trim();
    if (!text) {
      flushParagraph();
      flushList();
      continue;
    }
    const bullet = line.match(bulletLine);
    if (bullet) {
      flushParagraph();
      items.push(bullet[1].trim());
      continue;
    }
    flushList();
    if (!paragraph.length && isHeading(text)) blocks.push({ type: 'heading', text });
    else paragraph.push(line);
  }
  flushParagraph();
  flushList();
  return blocks;
}

function trimUrl(candidate) {
  const pairs = { ')': '(', ']': '[', '}': '{' };
  const openingPairs = { '(': ')', '[': ']', '{': '}' };
  const balances = { ')': 0, ']': 0, '}': 0 };
  for (const character of candidate) {
    if (character in pairs) balances[character] -= 1;
    else if (character in openingPairs) balances[openingPairs[character]] += 1;
  }
  let end = candidate.length;
  while (end) {
    const last = candidate[end - 1];
    if (/[.,;!?:]/u.test(last)) { end -= 1; continue; }
    if (last in pairs && balances[last] < 0) {
      balances[last] += 1;
      end -= 1;
      continue;
    }
    break;
  }
  return candidate.slice(0, end);
}

function appendText(node, text, document) {
  const urls = /https?:\/\/[^\s<>"'`]+/giu;
  let offset = 0;
  for (const match of text.matchAll(urls)) {
    const value = trimUrl(match[0]);
    let destination;
    try {
      destination = new URL(value);
      if (!['http:', 'https:'].includes(destination.protocol) || destination.username || destination.password) continue;
    } catch { continue; }
    node.append(document.createTextNode(text.slice(offset, match.index)));
    const link = document.createElement('a');
    link.href = destination.href;
    link.rel = 'noreferrer';
    link.textContent = value;
    node.append(link);
    offset = match.index + value.length;
  }
  node.append(document.createTextNode(text.slice(offset)));
}

export function renderPublicationBody(container, body) {
  const document = container.ownerDocument;
  const nodes = parsePublicationBody(body).map(block => {
    const node = document.createElement(block.type === 'heading' ? 'h2' : block.type === 'list' ? 'ul' : 'p');
    if (block.type === 'list') {
      for (const item of block.items) {
        const li = document.createElement('li');
        appendText(li, item, document);
        node.append(li);
      }
    } else appendText(node, block.text, document);
    return node;
  });
  container.replaceChildren(...nodes);
}
