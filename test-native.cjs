'use strict';
const fs=require('fs'),path=require('path'),cp=require('child_process');
const dir=path.join(__dirname,'tests','native-credentials');
const r=cp.spawnSync(path.join(__dirname,'dist','LockHost.exe'),['--data',dir,'--action','self-test'],{windowsHide:true,stdio:'inherit'});
if(r.error)throw r.error;if(r.status!==0)throw Error('Native tests failed: '+r.status);
console.log(fs.readFileSync(path.join(dir,'self-test-result.txt'),'utf8'));
