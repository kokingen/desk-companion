import { build } from 'esbuild';
import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import Ajv from 'ajv/dist/2020.js';
import standalone from 'ajv/dist/standalone/index.js';
import { avatarCharacterSchema } from '@bible-strong/avatar-core';
const dest = new URL('../../android/app/src/main/assets/companion/', import.meta.url);
await mkdir(dest, {recursive:true});
// Upstream validates through Ajv.compile(). Precompile the two unchanged MIT schemas at build time,
// allowing a strict production CSP without unsafe-eval and avoiding a compiler in the phone runtime.
const legacy=JSON.parse(await readFile(new URL('./node_modules/@bible-strong/avatar-core/dist/avatarDefinition.schema.json',import.meta.url),'utf8'));
const ajv=new Ajv({allErrors:true,strict:true,code:{source:true,esm:true}});
ajv.addSchema(legacy).addSchema(avatarCharacterSchema);
await mkdir(new URL('./generated/',import.meta.url),{recursive:true});
await writeFile(new URL('./generated/validators.mjs',import.meta.url),standalone(ajv,{legacy:legacy.$id,character:avatarCharacterSchema.$id}));
await build({entryPoints:[new URL('./src/main.js',import.meta.url).pathname.replace(/^\/(.:)/,'$1')],bundle:true,format:'iife',target:'chrome89',outfile:new URL('app.js',dest).pathname.replace(/^\/(.:)/,'$1'),minify:true,legalComments:'eof',plugins:[{name:'precompiled-avatar-validation',setup(b){b.onResolve({filter:/^ajv\/dist\/2020\.js$/},()=>({path:new URL('./src/validator-adapter.js',import.meta.url).pathname.replace(/^\/(.:)/,'$1')}))}}]});
for(const f of ['index.html','style.css'])await copyFile(new URL('./src/'+f,import.meta.url),new URL(f,dest));
await copyFile(new URL('./node_modules/@bible-strong/avatar-web/LICENSE',import.meta.url),new URL('AVATAR-WEB-LICENSE.txt',dest));
await copyFile(new URL('./node_modules/@bible-strong/avatar-core/LICENSE',import.meta.url),new URL('AVATAR-CORE-LICENSE.txt',dest));
await mkdir(new URL('licenses/',dest),{recursive:true});
for(const pkg of ['ajv','fast-deep-equal','fast-uri','json-schema-traverse','require-from-string']){
  for(const name of ['LICENSE','LICENSE.txt','LICENSE.md']){try{await copyFile(new URL('./node_modules/'+pkg+'/'+name,import.meta.url),new URL('licenses/'+pkg+'.txt',dest));break;}catch(e){if(e.code!=='ENOENT')throw e;}}
}
