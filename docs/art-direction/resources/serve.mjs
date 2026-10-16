import http from 'node:http';
import {createReadStream} from 'node:fs';
import {stat} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const root=path.dirname(fileURLToPath(import.meta.url));
const viewerBundle=path.resolve(root,'../arbre-de-base/declinaisons/viewer/model-viewer.min.js');
const mime={'.html':'text/html; charset=utf-8','.css':'text/css; charset=utf-8','.js':'text/javascript; charset=utf-8','.json':'application/json; charset=utf-8','.png':'image/png','.jpg':'image/jpeg','.glb':'model/gltf-binary','.blend':'application/octet-stream','.md':'text/plain; charset=utf-8'};
const server=http.createServer(async(req,res)=>{
  if(!['GET','HEAD'].includes(req.method)){res.writeHead(405);res.end();return;}
  try{
    let pathname=decodeURIComponent(new URL(req.url,'http://localhost').pathname);
    if(pathname==='/')pathname='/viewer/index.html';
    const file=pathname==='/vendor/model-viewer.min.js'?viewerBundle:path.resolve(root,'.'+pathname);
    const relative=path.relative(root,file);
    if(file!==viewerBundle&&(relative.startsWith('..')||path.isAbsolute(relative))){res.writeHead(403);res.end();return;}
    const info=await stat(file);
    if(!info.isFile())throw new Error('Not a file');
    res.writeHead(200,{'Content-Type':mime[path.extname(file)]||'application/octet-stream','Content-Length':info.size,'Cache-Control':'no-store'});
    if(req.method==='HEAD')res.end();else createReadStream(file).pipe(res);
  }catch{res.writeHead(404);res.end('Not found');}
});
server.listen(Number(process.argv[2]??51805),'127.0.0.1',()=>console.log(`ARKYA_RESOURCES_URL=http://127.0.0.1:${server.address().port}/viewer/index.html`));
