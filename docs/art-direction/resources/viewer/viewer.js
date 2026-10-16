const $=id=>document.getElementById(id);
const model=$('object');
const format=new Intl.NumberFormat('fr-BE',{maximumFractionDigits:2});
let catalogue,family,shape,material,asset,socketCount=3,pendingView=null,isLoading=true;
const reducedMotion=matchMedia('(prefers-reduced-motion: reduce)');
let effectsEnabled=!reducedMotion.matches;
const familyNames={cailloux:'Cailloux','roches-metalliferes':'Cailloux + métal',cristaux:'Cristaux',buissons:'Buissons',herbes:'Touffes d’herbe',champignons:'Champignons',fleurs:'Fleurs',souches:'Souches','troncs-couches':'Troncs couchés','mares-de-mana':'Mares de mana','outils-invoques':'Outils invoqués',structures:'Structures',reference:'Référence DA'};
// These approved collections predate status metadata in their manifests.
const legacyApprovedFamilies=new Set(['cailloux','roches-metalliferes','cristaux','buissons','herbes','fleurs','reference']);
const descriptions={cailloux:'Quatre volumes sculptés, de la petite pierre à l’affleurement.', 'roches-metalliferes':'Des filons de métal en relief, ancrés dans la roche.',cristaux:'Des formations minérales aux faces peintes et aux pointes lumineuses.',buissons:'Des feuillages peints en masses souples, du coussin bas au buisson dressé.',herbes:'Des brins courbés et effilés, de la petite touffe aux herbes hautes.',champignons:'Des chapeaux sculptés et peints, de la petite colonie au grand parasol.',fleurs:'Des pétales souples et des feuillages peints, de la rosette aux grandes floraisons.',souches:'Des racines sculptées et une écorce peinte, de la coupe basse à la souche ancienne.', 'troncs-couches':'Des bois tombés aux formes noueuses, creuses ou fourchues, avec leurs coupes et leurs fibres peintes.',reference:'La finition de l’arbre validée comme référence pour les autres objets.'};
const metres=value=>Number.isFinite(value)?`${format.format(value)} m`:'—';
descriptions['mares-de-mana']='Trois silhouettes de mana validées comme références DA : la mare d’origine, la petite allongée et la grande à lobes. Bleu, orange ou vert.';
descriptions['outils-invoques']='Proposition à valider : hache, pioche et faucille aux volumes peints, invoquées en bleu, orange ou vert. Jusqu’à trois emplacements vides pourront accueillir des cristaux.';
descriptions.structures='Cinq modules de construction aux surfaces peintes : fondation, murs plein, avec fenêtre ou avec porte et plafond. Trois matières : chaume, bois et pierre. Proposition à valider.';
function url(relative){return new URL(relative,new URL('../',location.href)).href;}
function button(label,active,action){const b=document.createElement('button');b.type='button';b.setAttribute('aria-pressed',String(active));b.addEventListener('click',action);b.textContent=label;return b;}
function findAsset(s=shape,m=material,count=socketCount){return family.assets.find(a=>a.shape===s&&a.material===m&&(!family.socket_counts||a.socket_count===count));}
function socketLabel(count){return `${count} socket${count>1?'s':''} vide${count>1?'s':''}`;}
function updateStructureDetails(){
  const detail=$('selected-contract');
  detail.hidden=family.id!=='structures';
  if(detail.hidden){detail.textContent='';return;}
  const selectedShape=family.shapes.find(s=>s.id===shape);
  const nominal=asset.nominal_dimensions||selectedShape.nominal_dimensions;
  const opening=asset.opening||selectedShape.opening;
  const lines=[];
  if(nominal)lines.push(`Module L × H × P : ${[nominal.width,nominal.height,nominal.depth].map(metres).join(' × ')}`);
  if(opening)lines.push(`Ouverture L × H : ${metres(opening.width)} × ${metres(opening.height)}`);
  if(typeof asset.pivot==='string')lines.push(`Pivot : ${asset.pivot}`);
  detail.textContent=lines.join('\n');
  detail.hidden=!lines.length;
}
function render(){
  $('families').replaceChildren(...catalogue.families.map(f=>{const b=button(familyNames[f.id]||f.label,f===family,()=>selectFamily(f));const n=document.createElement('span');n.className='family-count';n.textContent=f.id==='reference'?'Validée':String(f.assets.length);b.append(n);return b;}));
  $('count').textContent=`${family.assets.length} ${family.assets.length===1?'modèle':'variantes'}`;
  $('family-description').textContent=descriptions[family.id]||family.description;
  const approved=family.artistic_status?family.artistic_status==='approved':legacyApprovedFamilies.has(family.id);
  $('artistic-status').textContent=approved?'Finition peinte · Référence validée':'Finition peinte · Proposition à valider';
  $('shape-count').textContent=family.shapes.length;
  $('material-count').textContent=family.materials.length;
  $('shapes').replaceChildren(...family.shapes.map(s=>{
    const a=findAsset(s.id); const b=button('',s.id===shape,()=>select(s.id,material));b.className='shape';
    const img=document.createElement('img');img.src=url(a.preview);img.alt='';img.loading='lazy';
    const copy=document.createElement('span'),title=document.createElement('strong'),size=document.createElement('small');
    title.textContent=s.label;size.textContent=`H. ${metres(a.height)} · L. ${metres(a.width)}`;copy.append(title,size);
    const artisticStatus=s.artistic_status||(['outils-invoques','structures'].includes(family.id)?family.artistic_status:undefined);
    if(artisticStatus){const state=document.createElement('small');state.className='shape-status';state.textContent=artisticStatus==='approved'?'Référence validée':'Proposition à valider';copy.append(state);}
    b.append(img,copy);return b;
  }));
  $('materials').replaceChildren(...family.materials.map(m=>{
    const b=button('',m.id===material,()=>select(shape,m.id,socketCount,true));b.className='material';
    const dot=document.createElement('span');dot.className='swatch';dot.style.background=m.swatch||'#829281';
    const label=document.createElement('span');label.textContent=m.label;b.append(dot,label);if(m.description)b.title=m.description;return b;
  }));
  $('socket-control').hidden=!family.socket_counts;
  $('sockets').replaceChildren(...(family.socket_counts||[]).map(count=>{const b=button(String(count),count===socketCount,()=>select(shape,material,count,true));b.setAttribute('aria-label',socketLabel(count));return b;}));
  $('size-comparison').hidden=!family.comparison;
  if(family.comparison)$('size-comparison').href=url(family.comparison);
  for(const [id,key] of [['socket-comparison','socket_comparison'],['color-comparison','color_comparison']]){const link=$(id);link.hidden=!family[key];if(family[key])link.href=url(family[key]);}
}
async function reset(defer=false){
  pendingView=null;$('rotate').checked=false;model.autoRotate=false;model.cameraTarget='auto auto auto';model.cameraOrbit=family?.id==='mares-de-mana'?'35deg 52deg 115%':family?.id==='outils-invoques'?'28deg 78deg 128%':family?.id==='structures'?'30deg 65deg 120%':['herbes','fleurs'].includes(family?.id)?'35deg 68deg 125%':'35deg 68deg 110%';model.fieldOfView='35deg';model.resetTurntableRotation(0);
  if(defer||!model.loaded||isLoading)return;
  const loadedUrl=model.src;await model.updateComplete;
  if(model.src===loadedUrl&&model.loaded)model.jumpCameraToGoal();
}
function selectFamily(next){family=next;if(family.socket_counts&&!family.socket_counts.includes(socketCount))socketCount=family.socket_counts.at(-1);const chosen=family.shapes.find(s=>s.id===family.default_shape)||family.shapes[['outils-invoques','structures'].includes(family.id)?0:Math.min(1,family.shapes.length-1)];select(chosen.id,family.materials[0].id);document.querySelector('aside').scrollTop=0;$('families').querySelector('[aria-pressed="true"]').scrollIntoView({block:'nearest',inline:'nearest'});}
function select(nextShape,nextMaterial,nextSocketCount=socketCount,preserve=false){
  const next=findAsset(nextShape,nextMaterial,nextSocketCount);if(!next)return;
  if(preserve&&pendingView){pendingView={...pendingView,url:url(next.glb)};}
  else if(preserve&&model.loaded&&!isLoading){
    const orbit=model.getCameraOrbit(),target=model.getCameraTarget(),fov=model.getFieldOfView(),rotation=model.turntableRotation;
    if([orbit.theta,orbit.phi,orbit.radius,target.x,target.y,target.z,fov,rotation].every(Number.isFinite)&&orbit.radius>0){pendingView={url:url(next.glb),orbit:`${orbit.theta}rad ${orbit.phi}rad ${orbit.radius}m`,target:`${target.x}m ${target.y}m ${target.z}m`,fov:`${fov}deg`,rotation,autoRotate:$('rotate').checked};model.autoRotate=false;}else reset(true);
  }else reset(true);
  shape=nextShape;material=nextMaterial;socketCount=nextSocketCount;asset=next;
  $('selected-family').textContent=familyNames[family.id];
  $('selected-name').textContent=family.shapes.find(s=>s.id===shape).label;
  $('selected-material').textContent=family.materials.find(m=>m.id===material).label+(family.socket_counts?` · ${socketLabel(socketCount)}`:'');
  $('width-label').textContent=family.id==='troncs-couches'?'Longueur':'Largeur';
  for(const key of ['height','width','depth'])$(key).textContent=metres(asset[key]);
  updateStructureDetails();
  $('download-glb').href=url(asset.glb);$('download-blend').href=url(asset.blend);
  model.alt=`${$('selected-name').textContent}, ${$('selected-material').textContent}. Modèle 3D à tourner et zoomer.`;
  $('status').textContent=`Chargement de ${$('selected-name').textContent}…`;
  document.querySelector('.progress').classList.remove('complete');
  const changed=model.src!==url(asset.glb);
  if(changed){isLoading=true;model.pause();$('animation-control').hidden=true;}
  model.src=url(asset.glb);render();
  if(!changed&&model.loaded)loaded();
}
async function loaded(){
  const loadedAsset=asset,loadedUrl=model.src;
  const view=pendingView;
  if(view&&model.src===view.url){model.cameraOrbit=view.orbit;model.cameraTarget=view.target;model.fieldOfView=view.fov;await model.updateComplete;if(model.src!==view.url)return;if(pendingView===view){model.resetTurntableRotation(view.rotation);model.jumpCameraToGoal();model.autoRotate=view.autoRotate;pendingView=null;}}
  else{await model.updateComplete;if(model.src!==loadedUrl)return;model.jumpCameraToGoal();}
  if(model.src!==loadedUrl||asset!==loadedAsset)return;
  const animated=Boolean(loadedAsset.animation&&model.availableAnimations.includes(loadedAsset.animation));
  if(animated){model.animationName=loadedAsset.animation;await model.updateComplete;if(model.src!==loadedUrl||asset!==loadedAsset)return;}
  $('animation-control').hidden=!animated;
  $('animate').checked=animated&&effectsEnabled;
  if(animated&&effectsEnabled)model.play();else model.pause();
  isLoading=false;
  document.querySelector('.progress').classList.add('complete');
  $('status').textContent=`${$('selected-name').textContent} · ${$('selected-material').textContent} · Prêt à explorer`;
}
model.addEventListener('load',loaded);
model.addEventListener('progress',e=>{$('load-fill').style.width=`${Math.min(100,e.detail.totalProgress*100)}%`;});
model.addEventListener('error',()=>{$('status').textContent='Le modèle n’a pas pu être chargé. Rechargez la page.';});
$('reset').addEventListener('click',()=>reset());
$('rotate').addEventListener('change',()=>{if(pendingView)pendingView.autoRotate=$('rotate').checked;model.autoRotate=pendingView?false:$('rotate').checked;});
$('animate').addEventListener('change',()=>{effectsEnabled=$('animate').checked;if(!isLoading&&asset?.animation){if(effectsEnabled)model.play();else model.pause();}});
reducedMotion.addEventListener('change',e=>{if(e.matches){$('rotate').checked=false;if(pendingView)pendingView.autoRotate=false;model.autoRotate=false;effectsEnabled=false;$('animate').checked=false;model.pause();}});
// Preserve painted detail while animated models render continuously.
try{await customElements.whenDefined('model-viewer');customElements.get('model-viewer').minimumRenderScale=1;const response=await fetch('../catalogue.json',{cache:'no-store'});if(!response.ok)throw new Error('Catalogue');catalogue=await response.json();if(!catalogue.families?.length)throw new Error('Empty catalogue');selectFamily(catalogue.families.find(f=>f.id===catalogue.defaultFamily)||catalogue.families[0]);}catch(error){console.error('Library initialization failed',error);$('status').textContent='La bibliothèque n’est pas disponible. Rechargez la page.';}

