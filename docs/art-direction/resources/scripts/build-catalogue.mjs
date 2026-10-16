import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const partial=process.argv.includes('--partial');
const families=[];
const swatches={gris:'#8b9ea5',gres:'#b99260',mous:'#789065',fer:'#8f9ea7',cuivre:'#ba8059',or:'#d3ae54',azur:'#53b5c6',ameth:'#a28bd4',ambre:'#e1a34e'};
for(const id of ['cailloux','roches-metalliferes','cristaux','buissons','herbes','champignons','fleurs','souches','troncs-couches','mares-de-mana','outils-invoques','structures']){
  const manifest=path.join(root,id,'manifest.json');
  if(!fs.existsSync(manifest)){if(partial)continue;throw new Error(`Missing ${id} manifest`);}
  const source=JSON.parse(fs.readFileSync(manifest,'utf8').replace(/^\uFEFF/,''));
  const assets=source.assets.filter(a=>['glb','blend','preview'].every(k=>a[k]&&fs.existsSync(path.join(root,id,a[k]))));
  const expected=id==='structures'?15:id==='outils-invoques'?36:id==='mares-de-mana'?9:12;
  if(!partial&&assets.length!==expected)throw new Error(`${id}: expected ${expected} complete assets, found ${assets.length}`);
  if(id==='outils-invoques'){
    const toolShapes=['hache','pioche','faucille'],toolMaterials=['azur','orange','vert'],socketCounts=[0,1,2,3];
    const matches=(actual,wanted)=>Array.isArray(actual)&&actual.length===wanted.length&&wanted.every(value=>actual.includes(value));
    const dimensionsMatch=matches(source.shapes.map(s=>s.id),toolShapes)&&matches(source.materials.map(m=>m.id),toolMaterials)&&matches(source.socket_counts,socketCounts);
    const complete=dimensionsMatch&&assets.length===expected&&source.assets.length===expected&&toolShapes.every(shape=>toolMaterials.every(material=>socketCounts.every(socketCount=>assets.filter(a=>a.shape===shape&&a.material===material&&a.socket_count===socketCount).length===1)));
    if(!complete){if(partial)continue;throw new Error(`${id}: expected exactly one complete asset for each hache/pioche/faucille × azur/orange/vert × 0/1/2/3 sockets combination`);}
  }
  if(id==='structures'){
    const structureShapes=['fondation','mur-plein','mur-fenetre','mur-porte','plafond'],structureMaterials=['chaume','bois','pierre'];
    const matches=(actual,wanted)=>Array.isArray(actual)&&actual.length===wanted.length&&wanted.every(value=>actual.includes(value));
    const complete=matches(source.shapes.map(s=>s.id),structureShapes)&&matches(source.materials.map(m=>m.id),structureMaterials)&&assets.length===expected&&source.assets.length===expected&&structureShapes.every(shape=>structureMaterials.every(material=>assets.filter(a=>a.shape===shape&&a.material===material).length===1));
    if(!complete){if(partial)continue;throw new Error(`${id}: expected exactly one complete asset for each fondation/mur-plein/mur-fenetre/mur-porte/plafond × chaume/bois/pierre combination`);}
  }
  if(!assets.length)continue;
  const shapes=source.shapes.filter(s=>assets.some(a=>a.shape===s.id));
  const materials=source.materials.filter(m=>shapes.every(s=>assets.some(a=>a.shape===s.id&&a.material===m.id))).map(m=>({...m,swatch:m.swatch||Object.entries(swatches).find(([key])=>m.id.toLowerCase().includes(key))?.[1]||'#7f9386'}));
  const usable=assets.filter(a=>materials.some(m=>m.id===a.material));
  if(!usable.length)continue;
  const toolOptions=id==='outils-invoques'?{default_shape:'faucille',socket_counts:source.socket_counts,socket_comparison:fs.existsSync(path.join(root,id,'comparaison-sockets.png'))?`${id}/comparaison-sockets.png`:null,color_comparison:fs.existsSync(path.join(root,id,'comparaison-couleurs.png'))?`${id}/comparaison-couleurs.png`:null}:{};
  families.push({id,label:source.label||id,description:source.description,artistic_status:source.artistic_status,approved_at:source.approved_at,shapes,materials,...toolOptions,comparison:fs.existsSync(path.join(root,id,'comparaison-tailles.png'))?`${id}/comparaison-tailles.png`:null,assets:usable.map(a=>({...a,glb:`${id}/${a.glb}`,blend:`${id}/${a.blend}`,preview:`${id}/${a.preview}`}))});
}
families.push({id:'reference',label:'Référence DA',shapes:[{id:'arbre',label:'Arbre de référence'}],materials:[{id:'moussu',label:'Ancien moussu',swatch:'#839872'},{id:'mystique',label:'Mystique',swatch:'#718c94'},{id:'brule',label:'Brûlé',swatch:'#9e835f'}],assets:[['03-moussu','moussu'],['01-mystique','mystique'],['02-brule','brule']].map(([stem,material])=>({id:`reference-${material}`,shape:'arbre',material,label:'Arbre de référence',glb:`references/arbre-peint-v3/${stem}/${stem}.glb`,blend:`references/arbre-peint-v3/${stem}/${stem}.blend`,preview:`references/arbre-peint-v3/${stem}/${stem}-preview.png`,height:10.128,width:9.44,depth:9.657}))});
fs.writeFileSync(path.join(root,'catalogue.json'),JSON.stringify({defaultFamily:'outils-invoques',families},null,2)+'\n');
console.log(families.map(f=>`${f.id}: ${f.assets.length}`).join(', '));
