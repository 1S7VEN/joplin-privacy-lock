'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { EventEmitter } = require('node:events');
test('native plugin starts without Electron remote and exposes working commands', async () => {
  const data = fs.mkdtempSync(path.join(os.tmpdir(), 'joplin-lock-entry-'));
  const commands = {}; const calls = []; const settings = {}; let start, onChange;
  const api = {
    plugins: { register: spec => { start = spec.onStart; }, dataDir: async () => data, installationDir: async () => 'C:\\plugins\\local-lock' },
    settings: { registerSection: async () => {}, registerSettings: async defs => { for(const [k,v] of Object.entries(defs)) settings[k]=v.value; }, value: async key => settings[key], onChange: async handler => { onChange=handler; } },
    commands: { register: async c => { commands[c.name] = c; } },
    views: { dialogs: { showMessageBox: async () => assert.fail('Unexpected startup error') }, menuItems: { create: async () => {} }, toolbarButtons: { create: async () => {} } },
  };
  const context = { joplin: api, process: { platform: 'win32', execPath: 'C:\\Joplin\\Joplin.exe' }, require: name => {
    assert.notEqual(name, '@electron/remote');
    if (name === 'child_process') return { spawn: (...args) => { calls.push(args); return new EventEmitter(); } };
    return require(name);
  } };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../src/index-native.js'), 'utf8'), context);
  await start();
  const optionsFile = path.join(data, 'options.json');
  assert.deepEqual(JSON.parse(fs.readFileSync(optionsFile, 'utf8')), {MinimizeEnabled:true,MinimizeDelaySeconds:0,TrayEnabled:true,TrayDelaySeconds:0,IdleMinutes:5});
  settings.minimizeEnabled=false;settings.trayDelaySeconds=30;settings.minimizeDelaySeconds=10;settings.idleMinutes=0;
  await onChange();
  assert.deepEqual(JSON.parse(fs.readFileSync(optionsFile, 'utf8')), {MinimizeEnabled:false,MinimizeDelaySeconds:10,TrayEnabled:true,TrayDelaySeconds:30,IdleMinutes:0});
  settings.trayEnabled=false;settings.minimizeDelaySeconds=-9;settings.trayDelaySeconds=900000;
  await Promise.all([onChange(),onChange()]);
  assert.equal(JSON.parse(fs.readFileSync(optionsFile,'utf8')).TrayDelaySeconds,86400);
  assert.equal(JSON.parse(fs.readFileSync(optionsFile,'utf8')).MinimizeDelaySeconds,0);
  assert.equal(calls[0][1].at(-1), 'start');
  assert.equal(calls[0][2].windowsHide, true);
  assert.ok(calls[0][0].endsWith('LockHost.exe'));
  await commands.localMinimizeLockNow.execute();
  await commands.localMinimizeLockPassword.execute();
  assert.equal(calls[1][1].at(-1), 'lock');
  assert.equal(calls[2][1].at(-1), 'configure');
});
