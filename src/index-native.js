'use strict';
const path = require('path');
const fs = require('fs');
const cp = require('child_process');
joplin.plugins.register({ onStart: async () => {
  if (process.platform !== 'win32') { await joplin.views.dialogs.showMessageBox('此版本仅支持 Windows。'); return; }
  await joplin.settings.registerSection('minimizeLockNative', { label: '隐私与锁定', iconName: 'fa fa-lock' });
  const intSetting = (value, label, maximum) => ({ value, type: 1, public: true, section: 'minimizeLockNative', minimum: 0, maximum, label });
  const boolSetting = label => ({ value: true, type: 3, public: true, section: 'minimizeLockNative', label });
  await joplin.settings.registerSettings({
    minimizeEnabled: boolSetting('最小化时自动锁定'),
    minimizeDelaySeconds: intSetting(0, '最小化后延迟多少秒锁定（0 为立即）', 86400),
    trayEnabled: boolSetting('隐藏到托盘时自动锁定'),
    trayDelaySeconds: intSetting(0, '进入托盘后延迟多少秒锁定（0 为立即）', 86400),
    idleMinutes: intSetting(5, '电脑闲置几分钟后锁定（0 为关闭）', 1440),
  });
  const dir = await joplin.plugins.dataDir();
  fs.mkdirSync(dir, { recursive: true });
  const install = await joplin.plugins.installationDir();
  const helper = path.join(install, 'LockHost.exe');
  let settingsQueue = Promise.resolve();
  function updateSettings() {
    settingsQueue = settingsQueue.catch(() => {}).then(async () => {
      const clamp = (v, max) => Math.max(0, Math.min(max, Math.floor(Number(v) || 0)));
      const options = {
        MinimizeEnabled: !!(await joplin.settings.value('minimizeEnabled')),
        MinimizeDelaySeconds: clamp(await joplin.settings.value('minimizeDelaySeconds'), 86400),
        TrayEnabled: !!(await joplin.settings.value('trayEnabled')),
        TrayDelaySeconds: clamp(await joplin.settings.value('trayDelaySeconds'), 86400),
        IdleMinutes: clamp(await joplin.settings.value('idleMinutes'), 1440),
      };
      const target = path.join(dir, 'options.json');
      fs.writeFileSync(target + '.tmp', JSON.stringify(options));
      fs.renameSync(target + '.tmp', target);
    });
    return settingsQueue;
  }
  await updateSettings();
  await joplin.settings.onChange(updateSettings);
  function run(action) {
    const child = cp.spawn(helper, ['--data', dir, '--exe', process.execPath, '--action', action], { windowsHide: true, stdio: 'ignore' });
    child.on('error', () => { joplin.views.dialogs.showMessageBox('锁定辅助程序未能启动，请查看插件安装目录中的 host-error.txt。'); });
  }
  await joplin.commands.register({ name: 'localMinimizeLockNow', label: '隐私与锁定：立即锁定', iconName: 'fas fa-lock', execute: async () => run('lock') });
  await joplin.commands.register({ name: 'localMinimizeLockPassword', label: '隐私与锁定：设置或修改密码', execute: async () => run('configure') });
  await joplin.views.menuItems.create('localLockNowMenu', 'localMinimizeLockNow', 'tools', { accelerator: 'Ctrl+Alt+L' });
  await joplin.views.menuItems.create('localLockPasswordMenu', 'localMinimizeLockPassword', 'tools');
  await joplin.views.toolbarButtons.create('localLockNowButton', 'localMinimizeLockNow', 'noteToolbar');
  run('start');
} });

