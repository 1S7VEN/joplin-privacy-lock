'use strict';
const fs=require('fs'),path=require('path'),cp=require('child_process');
const root=__dirname, dist=path.join(root,'dist');fs.mkdirSync(dist,{recursive:true});
const compiler=path.join(process.env.WINDIR||'C:\\Windows','Microsoft.NET','Framework64','v4.0.30319','csc.exe');
function run(exe,args){const r=cp.spawnSync(exe,args,{cwd:root,stdio:'inherit',windowsHide:true});if(r.error)throw r.error;if(r.status!==0)throw Error(exe+' failed: '+r.status);}
run(compiler,['/nologo','/target:winexe','/out:'+path.join(dist,'LockHost.exe'),'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Security.dll','/reference:System.Web.Extensions.dll',...['LockHost.cs','LockPolicy.cs','PolicyTests.cs'].map(n=>path.join(root,'src',n))]);
require('esbuild').buildSync({entryPoints:[path.join(root,'src/index-native.js')],bundle:true,platform:'node',format:'iife',outfile:path.join(dist,'index.js')});
fs.copyFileSync(path.join(root,'src/manifest.json'),path.join(dist,'manifest.json'));
run('python',[path.join(root,'package_jpl.py')]);
