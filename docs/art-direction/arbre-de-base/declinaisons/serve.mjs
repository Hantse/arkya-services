import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.dirname(fileURLToPath(import.meta.url));
const base = path.resolve(root, '..');
const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.mjs': 'text/javascript; charset=utf-8', '.glb': 'model/gltf-binary', '.png': 'image/png', '.json': 'application/json; charset=utf-8' };
const server = http.createServer((req, res) => {
  try {
    const urlPath = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    const isBase = urlPath.startsWith('/base/');
    const directory = isBase ? base : root;
    const relativeUrl = isBase ? urlPath.slice(5) : urlPath === '/' ? '/viewer/index.html' : urlPath;
    const file = path.resolve(directory, '.' + relativeUrl);
    const relative = path.relative(directory, file);
    if (relative.startsWith('..') || path.isAbsolute(relative)) {
      res.writeHead(403).end(); return;
    }
    if (!fs.existsSync(file) || !fs.statSync(file).isFile()) {
      res.writeHead(404).end('Fichier introuvable'); return;
    }
    const size = fs.statSync(file).size;
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Content-Length': size, 'Cache-Control': 'no-store' });
    if (req.method === 'HEAD') res.end();
    else fs.createReadStream(file).pipe(res);
  } catch {
    res.writeHead(400).end('Requête invalide');
  }
});
server.listen(Number(process.argv[2] || 0), '127.0.0.1', () => console.log(`http://127.0.0.1:${server.address().port}/viewer/index.html`));
