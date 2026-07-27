const fs = require('fs');
const net = require('net');
const path = require('path');
const childProcess = require('child_process');
const vscode = require('vscode');

const PIPE_NAME = '\\\\.\\pipe\\fuguang-desktop-pet';
const ACTION_PIPE_NAME = '\\\\.\\pipe\\fuguang-desktop-pet-actions';

class PetEventBus {
  constructor() {
    this.listeners = new Set();
  }

  subscribe(listener) {
    this.listeners.add(listener);
    return { dispose: () => this.listeners.delete(listener) };
  }

  emit(state, source, durationMs = 0, priority = 10) {
    const event = { state, source, durationMs, priority, timestamp: Date.now() };
    this.notify(event);
    this.sendToDesktop(event);
  }

  notify(event) {
    for (const listener of this.listeners) {
      listener(event);
    }
  }

  command(command, value = '', source = '') {
    this.sendToDesktop({ command, value, source, timestamp: Date.now() });
  }

  sendToDesktop(message) {
    const client = net.createConnection(PIPE_NAME);
    client.on('connect', () => client.end(`${JSON.stringify(message)}\n`));
    client.on('error', () => client.destroy());
  }
}

class PetViewProvider {
  static viewType = 'fuguangPet.view';

  constructor(extensionUri, eventBus, globalState, onAutoStartChanged) {
    this.extensionUri = extensionUri;
    this.eventBus = eventBus;
    this.globalState = globalState;
    this.onAutoStartChanged = onAutoStartChanged;
    this.view = undefined;
    this.pendingState = 'idle';
    this.animationConfig = JSON.parse(fs.readFileSync(path.join(extensionUri.fsPath, 'media', 'pet-animation.json'), 'utf8'));
  }

  resolveWebviewView(webviewView) {
    const webview = webviewView.webview;
    webview.options = {
      enableScripts: true,
      localResourceRoots: [vscode.Uri.joinPath(this.extensionUri, 'media')]
    };

    this.view = webviewView;
    const spritesheetUri = webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, 'media', 'spritesheet.webp'));
    webview.html = this.getHtml(webview, spritesheetUri);
    webview.onDidReceiveMessage((message) => {
      if (message?.type === 'ready') {
        this.postState(this.pendingState, 'restore');
        this.postAutoStartSetting();
      } else if (message?.type === 'play' && typeof message.state === 'string') {
        this.eventBus.emit(message.state, 'sidebar');
      } else if (message?.type === 'setAutoStartDesktop' && typeof message.value === 'boolean') {
        void this.updateAutoStartSetting(message.value).catch((error) => {
          console.error('[浮光橙仔] 自动启动设置保存失败', error);
          const detail = error instanceof Error ? error.message : String(error);
          void vscode.window.showErrorMessage(`自动启动设置保存失败：${detail}`);
        });
      }
    });
  }

  async updateAutoStartSetting(value) {
    await this.globalState.update('autoStartDesktop', value);
    if (this.globalState.get('autoStartDesktop') !== value) {
      throw new Error(`扩展全局状态未生效（当前值：${String(this.globalState.get('autoStartDesktop'))}）`);
    }
    if (value) void this.onAutoStartChanged?.();
  }

  postAutoStartSetting() {
    const enabled = this.globalState.get('autoStartDesktop', true);
    this.view?.webview.postMessage({ type: 'autoStartDesktop', value: enabled });
  }

  handleConfigurationChange(event) {
    if (event.affectsConfiguration('fuguangPet.autoStartDesktop')) this.postAutoStartSetting();
  }

  play(event) {
    this.pendingState = event.state;
    this.postState(event.state, event.source, event.durationMs);
  }

  postState(state, source, durationMs = 0) {
    this.view?.webview.postMessage({ type: 'play', state, source, durationMs });
  }

  getHtml(webview, spritesheetUri) {
    const nonce = getNonce();
    const animationConfig = JSON.stringify(this.animationConfig).replace(/</g, '\\u003c');
    return `<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource}; connect-src ${webview.cspSource}; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}';">
  <title>浮光橙仔</title>
</head>
<body>
  <main class="pet-stage">
    <div id="pet" class="pet" role="button" tabindex="0" aria-label="浮光橙仔"></div>
    <div id="status" class="status" aria-live="polite">正在加载动画</div>
    <div class="controls" role="toolbar" aria-label="桌宠动作">
      <button type="button" data-state="idle" title="待机">待机</button>
      <button type="button" data-state="waving" title="挥手">挥手</button>
      <button type="button" data-state="jumping" title="跳跃">跳跃</button>
      <button type="button" data-state="running" title="奔跑">奔跑</button>
      <button type="button" data-state="review" title="检查">检查</button>
    </div>
    <label class="setting-row">
      <span>启动 VS Code 时自动运行桌宠</span>
      <input id="auto-start-desktop" type="checkbox" aria-label="启动 VS Code 时自动运行桌宠">
    </label>
  </main>
  <style nonce="${nonce}">
    :root { color-scheme: light dark; }
    * { box-sizing: border-box; }
    body { margin: 0; color: var(--vscode-foreground); background: var(--vscode-sideBar-background); font-family: var(--vscode-font-family); }
    .pet-stage { min-height: 320px; display: grid; grid-template-rows: minmax(var(--cell-height, 208px), 1fr) auto auto; align-items: center; justify-items: center; gap: 10px; padding: 18px 10px 10px; overflow: hidden; }
    .pet { width: var(--cell-width, 192px); height: var(--cell-height, 208px); background-image: url('${spritesheetUri}'); background-repeat: no-repeat; background-size: var(--sheet-width, 1536px) var(--sheet-height, 1872px); cursor: pointer; transform-origin: 50% 100%; }
    .pet.bounce { animation: bounce 220ms ease-out; }
    .status { min-height: 18px; color: var(--vscode-descriptionForeground); font-size: 12px; }
    .controls { width: min(100%, 340px); display: grid; grid-template-columns: repeat(5, minmax(48px, 1fr)); gap: 6px; }
    .setting-row { width: min(100%, 340px); display: flex; align-items: center; justify-content: space-between; gap: 12px; color: var(--vscode-foreground); font-size: 12px; }
    .setting-row span { min-width: 0; }
    .setting-row input { flex: 0 0 auto; accent-color: var(--vscode-focusBorder); cursor: pointer; }
    button { min-height: 30px; padding: 4px 6px; border: 1px solid var(--vscode-button-border, transparent); border-radius: 4px; color: var(--vscode-button-foreground); background: var(--vscode-button-background); font: inherit; cursor: pointer; }
    button:hover { background: var(--vscode-button-hoverBackground); }
    button:focus-visible, .pet:focus-visible { outline: 1px solid var(--vscode-focusBorder); outline-offset: 2px; }
    button[aria-pressed="true"] { color: var(--vscode-button-secondaryForeground); background: var(--vscode-button-secondaryBackground); }
    @keyframes bounce { 50% { transform: translateY(-8px); } }
    @media (max-width: 260px) { .pet-stage { padding-inline: 4px; } .controls { grid-template-columns: repeat(3, 1fr); } }
  </style>
  <script nonce="${nonce}">
    const vscode = acquireVsCodeApi();
    let states = {};
    const pet = document.getElementById('pet');
    const status = document.getElementById('status');
    const buttons = [...document.querySelectorAll('[data-state]')];
    const autoStartDesktop = document.getElementById('auto-start-desktop');
    let timer;
    let currentState = 'idle';
    let playbackSequence = 0;
    let cellWidth = 192;
    let cellHeight = 208;

    function request(state) { vscode.postMessage({ type: 'play', state }); }

    autoStartDesktop.addEventListener('change', () => {
      vscode.postMessage({ type: 'setAutoStartDesktop', value: autoStartDesktop.checked });
    });

    function play(stateName, source = '', durationMs = 0) {
      if (!states[stateName]) return;
      const state = states[stateName];
      const sequence = ++playbackSequence;
      currentState = stateName;
      clearInterval(timer);
      let frame = 0;
      status.textContent = state.label + (source && source !== 'sidebar' ? ' · ' + source : '');
      pet.setAttribute('aria-label', '浮光橙仔正在' + state.label);
      buttons.forEach((button) => button.setAttribute('aria-pressed', String(button.dataset.state === stateName)));
      pet.classList.remove('bounce');
      void pet.offsetWidth;
      pet.classList.add('bounce');

      function render() {
        pet.style.backgroundPosition = (-frame * cellWidth) + 'px ' + (-state.row * cellHeight) + 'px';
        frame += 1;
        if (frame >= state.frames) {
          if (state.loop) {
            frame = 0;
          } else {
            clearInterval(timer);
            setTimeout(() => play('idle'), 320);
          }
        }
      }

      render();
      timer = setInterval(render, state.intervalMs);
      if (durationMs > 0) {
        setTimeout(() => {
          if (playbackSequence === sequence) play('idle');
        }, durationMs);
      }
    }

    buttons.forEach((button) => button.addEventListener('click', () => request(button.dataset.state)));
    pet.addEventListener('click', () => request('waving'));
    pet.addEventListener('dblclick', () => request('jumping'));
    pet.addEventListener('keydown', (event) => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); request('waving'); } });
    window.addEventListener('message', (event) => {
      if (event.data?.type === 'play') play(event.data.state, event.data.source, event.data.durationMs);
      if (event.data?.type === 'autoStartDesktop') autoStartDesktop.checked = event.data.value === true;
    });
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) clearInterval(timer);
      else play(currentState);
    });
    try {
      const config = ${animationConfig};
      states = config.states;
      cellWidth = config.spritesheet.cellWidth;
      cellHeight = config.spritesheet.cellHeight;
      document.documentElement.style.setProperty('--cell-width', cellWidth + 'px');
      document.documentElement.style.setProperty('--cell-height', cellHeight + 'px');
      document.documentElement.style.setProperty('--sheet-width', config.spritesheet.width + 'px');
      document.documentElement.style.setProperty('--sheet-height', config.spritesheet.height + 'px');
      vscode.postMessage({ type: 'ready' });
    } catch (error) {
      status.textContent = '动画配置加载失败';
    }
  </script>
</body>
</html>`;
  }
}

function registerDesktopActionServer(context, eventBus) {
  const server = net.createServer((socket) => {
    let buffer = '';
    socket.setEncoding('utf8');
    socket.on('data', (chunk) => {
      buffer += chunk;
      const lines = buffer.split(/\r?\n/);
      buffer = lines.pop() ?? '';
      for (const line of lines) {
        if (!line.trim()) continue;
        try {
          void handleDesktopAction(JSON.parse(line), eventBus);
        } catch {
          eventBus.emit('failed', '桌宠命令格式无效', 1200);
        }
      }
    });
  });
  server.on('error', (error) => {
    console.error('[浮光橙仔] 桌面动作管道启动失败', error);
  });
  server.listen(ACTION_PIPE_NAME);
  context.subscriptions.push({ dispose: () => server.close() });
}

async function handleDesktopAction(message, eventBus) {
  const action = typeof message?.action === 'string' ? message.action : '';
  if (action === 'focus-state') {
    let state;
    try {
      state = typeof message.value === 'string' ? JSON.parse(message.value) : message.value;
    } catch {
      return;
    }
    eventBus.notify({ state: 'focus-state', source: JSON.stringify(state || {}), timestamp: Date.now() });
    return;
  }
  if (action === 'bug-search-start') {
    const baseline = countErrorDiagnostics();
    await vscode.commands.executeCommand('workbench.actions.view.problems');
    setTimeout(() => eventBus.command('bug-search-result', JSON.stringify({ baseline, diagnostics: countErrorDiagnostics() })), 800);
    return;
  }
  switch (action) {
    case 'open-project':
      await vscode.commands.executeCommand('workbench.action.files.openFolder');
      break;
    case 'open-terminal':
      await vscode.commands.executeCommand('workbench.action.terminal.focus');
      break;
    case 'open-problems':
      await vscode.commands.executeCommand('workbench.actions.view.problems');
      break;
    case 'open-scm':
      await vscode.commands.executeCommand('workbench.view.scm');
      break;
    case 'run-build':
      await runTaskByGroup(vscode.TaskGroup.Build, '构建');
      break;
    case 'run-test':
      await runTaskByGroup(vscode.TaskGroup.Test, '测试');
      break;
    default:
      return;
  }
  eventBus.emit('waving', `已执行：${action}`, 1200);
}

function countErrorDiagnostics() {
  return vscode.languages.getDiagnostics().reduce(
    (total, [, diagnostics]) => total + diagnostics.filter((item) => item.severity === vscode.DiagnosticSeverity.Error).length,
    0
  );
}

async function runTaskByGroup(group, label) {
  const tasks = await vscode.tasks.fetchTasks();
  const task = tasks.find((candidate) => candidate.group === group);
  if (!task) {
    void vscode.window.showInformationMessage(`当前工作区没有默认${label}任务。`);
    return;
  }
  await vscode.tasks.executeTask(task);
}

function registerEventDrivenBehavior(context, eventBus, isFocusActive) {
  let editTimer;
  let diagnosticTimer;
  let lastWorkAt = 0;
  let longSessionStartedAt = 0;
  let lastLongSessionAt = 0;
  let lastProblemCount = 0;
  let focusWindowDriftCount = 0;
  let focusWindowDriftWindowStartedAt = 0;
  let lastFocusWindowDriftAt = 0;
  let activeOperations = 0;
  const taskLabels = new Map();
  const enabled = () => vscode.workspace.getConfiguration('fuguangPet').get('eventDriven', true);
  const emit = (state, source, durationMs = 0, priority = 10) => { if (enabled()) eventBus.emit(state, source, durationMs, priority); };
  const beginOperation = (source) => { activeOperations += 1; emit('waiting', source, 0, 40); };
  const endOperation = (source, failed = false) => {
    activeOperations = Math.max(0, activeOperations - 1);
    const state = failed ? 'failed' : activeOperations > 0 ? 'waiting' : 'waving';
    emit(state, source, activeOperations > 0 ? 0 : 1800, failed ? 80 : 45);
  };

  context.subscriptions.push(
    vscode.workspace.onDidChangeTextDocument((event) => {
      if (event.contentChanges.length === 0) return;
      const now = Date.now();
      if (!longSessionStartedAt || now - lastWorkAt > 10 * 60 * 1000) longSessionStartedAt = now;
      lastWorkAt = now;
      clearTimeout(editTimer);
      emit('running', '正在编辑', 0);
      editTimer = setTimeout(() => emit('idle', '编辑暂停', 0), 900);
    }),
    vscode.workspace.onDidSaveTextDocument(() => emit('jumping', '文件已保存', 1200, 25)),
    vscode.window.onDidChangeActiveTextEditor((editor) => { if (editor) emit('review', '切换编辑器', 1600); }),
    vscode.window.onDidChangeWindowState((state) => {
      if (!isFocusActive()) return;
      if (state.focused) return;
      const now = Date.now();
      if (now - focusWindowDriftWindowStartedAt > 2 * 60 * 1000) {
        focusWindowDriftWindowStartedAt = now;
        focusWindowDriftCount = 0;
      }
      focusWindowDriftCount += 1;
      if (focusWindowDriftCount < 2 || now - lastFocusWindowDriftAt < 60 * 1000) return;
      lastFocusWindowDriftAt = now;
      emit('review', '专注中离开 VS Code', 1800, 65);
    }),
    vscode.languages.onDidChangeDiagnostics(() => {
      clearTimeout(diagnosticTimer);
      diagnosticTimer = setTimeout(() => {
        const problemCount = vscode.languages.getDiagnostics().reduce((total, [, diagnostics]) => total + diagnostics.filter((item) => item.severity === vscode.DiagnosticSeverity.Error).length, 0);
        if (problemCount > lastProblemCount) emit('failed', `发现 ${problemCount} 个错误`, 2600, 75);
        lastProblemCount = problemCount;
      }, 600);
    }),
    vscode.debug.onDidStartDebugSession(() => beginOperation('正在调试')),
    vscode.debug.onDidTerminateDebugSession(() => endOperation('调试结束')),
    vscode.tasks.onDidStartTask((event) => {
      const taskName = event.execution?.task?.name || '未命名任务';
      taskLabels.set(event.execution, taskName);
      beginOperation(`任务运行中：${taskName}`);
    }),
    vscode.tasks.onDidEndTaskProcess((event) => {
      const taskName = taskLabels.get(event.execution) || event.execution?.task?.name || '未命名任务';
      taskLabels.delete(event.execution);
      const failed = event.exitCode !== 0;
      const result = failed
        ? `任务失败：${taskName}（退出码 ${event.exitCode ?? '未知'}），可打开问题面板查看详情`
        : `任务成功：${taskName}`;
      endOperation(result, failed);
    }),
    vscode.workspace.onDidChangeTextDocument(() => {
      const now = Date.now();
      if (now - longSessionStartedAt < 60 * 60 * 1000 || now - lastLongSessionAt < 2 * 60 * 60 * 1000) return;
      if (isFocusActive()) return;
      lastLongSessionAt = now;
      eventBus.command('long-session', '', '连续工作已超过 60 分钟。');
    })
  );

  if (typeof vscode.window.onDidStartTerminalShellExecution === 'function') {
    context.subscriptions.push(vscode.window.onDidStartTerminalShellExecution(() => beginOperation('终端命令运行中')));
  }
  if (typeof vscode.window.onDidEndTerminalShellExecution === 'function') {
    context.subscriptions.push(vscode.window.onDidEndTerminalShellExecution((event) => endOperation('终端命令结束', event.exitCode !== undefined && event.exitCode !== 0)));
  }
  context.subscriptions.push({ dispose: () => { clearTimeout(editTimer); clearTimeout(diagnosticTimer); taskLabels.clear(); } });
}

function registerGitReminder(context, eventBus) {
  let lastReminderAt = 0;
  let lastCeremonyAt = 0;
  const repositoryHeads = new Map();
  const check = async () => {
    if (!vscode.workspace.getConfiguration('fuguangPet').get('gitReminder', true)) return;
    const extension = vscode.extensions.getExtension('vscode.git');
    if (!extension) return;
    const git = extension.isActive ? extension.exports : await extension.activate();
    const repositories = git.getAPI(1).repositories;
    for (const repository of repositories) {
      const head = repository.state.HEAD?.commit || '';
      const previousHead = repositoryHeads.get(repository.rootUri.toString());
      repositoryHeads.set(repository.rootUri.toString(), head);
      if (previousHead && head && previousHead !== head && Date.now() - lastCeremonyAt >= 30 * 1000) {
        lastCeremonyAt = Date.now();
        eventBus.command('commit-ceremony', '', 'Git 提交完成。');
      }
    }
    const changeCount = repositories.reduce((total, repository) => total + repository.state.workingTreeChanges.length + repository.state.indexChanges.length + repository.state.mergeChanges.length, 0);
    if (changeCount === 0 || Date.now() - lastReminderAt < 30 * 60 * 1000) return;
    lastReminderAt = Date.now();
    eventBus.emit('review', `有 ${changeCount} 项 Git 改动尚未提交`, 3200, 35);
  };
  const timer = setInterval(() => { void check(); }, 60 * 1000);
  const saveSubscription = vscode.workspace.onDidSaveTextDocument(() => { void check(); });
  const gitSubscription = vscode.workspace.onDidOpenTextDocument(() => { void check(); });
  context.subscriptions.push(saveSubscription, gitSubscription, { dispose: () => { clearInterval(timer); repositoryHeads.clear(); } });
}

async function startDesktopPet(context, eventBus) {
  const configured = vscode.workspace.getConfiguration('fuguangPet').get('desktopExecutable', '').trim();
  const executable = configured || path.join(context.extensionPath, 'desktop', 'Fuguang.DesktopPet.exe');
  if (!fs.existsSync(executable)) {
    void vscode.window.showErrorMessage(`未找到桌面宠物程序：${executable}`);
    return;
  }
  const process = childProcess.spawn(executable, [], {
    cwd: path.dirname(executable),
    detached: true,
    stdio: 'ignore',
    windowsHide: false
  });
  process.unref();
  setTimeout(() => {
    eventBus.command('show');
    eventBus.emit('idle', 'VS Code 已连接');
  }, 800);
}

function activate(context) {
  const eventBus = new PetEventBus();
  const autoStartSetting = vscode.workspace.getConfiguration('fuguangPet').inspect('autoStartDesktop');
  const storedAutoStart = context.globalState.get('autoStartDesktop');
  const configuredAutoStart = autoStartSetting?.globalValue ?? autoStartSetting?.defaultValue ?? true;
  if (storedAutoStart === undefined) void context.globalState.update('autoStartDesktop', configuredAutoStart);
  const provider = new PetViewProvider(context.extensionUri, eventBus, context.globalState, () => startDesktopPet(context, eventBus));
  let focusEndsAt = 0;
  context.subscriptions.push(
    eventBus.subscribe((event) => provider.play(event)),
    vscode.window.registerWebviewViewProvider(PetViewProvider.viewType, provider),
    vscode.workspace.onDidChangeConfiguration((event) => provider.handleConfigurationChange(event)),
    vscode.commands.registerCommand('fuguangPet.open', () => vscode.commands.executeCommand('workbench.view.extension.fuguangPet')),
    vscode.commands.registerCommand('fuguangPet.startDesktop', () => startDesktopPet(context, eventBus)),
    vscode.commands.registerCommand('fuguangPet.showDesktop', () => eventBus.command('show')),
    vscode.commands.registerCommand('fuguangPet.hideDesktop', () => eventBus.command('hide')),
    vscode.commands.registerCommand('fuguangPet.togglePause', () => eventBus.command('toggle-pause')),
    vscode.commands.registerCommand('fuguangPet.exitDesktop', () => eventBus.command('exit')),
    vscode.commands.registerCommand('fuguangPet.startFocus', () => {
      const configuration = vscode.workspace.getConfiguration('fuguangPet');
      const focusMinutes = Math.max(1, Number(configuration.get('focusMinutes', 25)) || 25);
      const breakMinutes = Math.max(1, Number(configuration.get('breakMinutes', 5)) || 5);
      focusEndsAt = Date.now() + focusMinutes * 60 * 1000;
      eventBus.command('focus-start', JSON.stringify({ focusMinutes, breakMinutes }));
    }),
    vscode.commands.registerCommand('fuguangPet.stopFocus', () => {
      focusEndsAt = 0;
      eventBus.command('focus-stop');
    }),
    eventBus.subscribe((event) => {
      if (event.state !== 'focus-state') return;
      let state;
      try { state = JSON.parse(event.source); } catch { state = {}; }
      focusEndsAt = state.state === 'started'
        ? Date.now() + (state.focusMinutes || 25) * 60 * 1000
        : state.state === 'break'
          ? Date.now() + (state.breakMinutes || 5) * 60 * 1000
          : 0;
    }),
    vscode.commands.registerCommand('fuguangPet.remindLater', async () => {
      const input = await vscode.window.showInputBox({
        prompt: '多少分钟后提醒？', value: '20', validateInput: (value) => {
          const minutes = Number(value);
          return Number.isInteger(minutes) && minutes >= 1 && minutes <= 1440 ? undefined : '请输入 1 到 1440 之间的整数。';
        }
      });
      if (input) eventBus.command('remind', String(Number(input)), '稍后提醒时间到了。');
    })
  );
  registerEventDrivenBehavior(context, eventBus, () => focusEndsAt > Date.now());
  registerGitReminder(context, eventBus);
  registerDesktopActionServer(context, eventBus);
  eventBus.emit('idle', 'VS Code 已就绪');
  const today = new Date().toISOString().slice(0, 10);
  if (context.globalState.get('morningCheckDate') !== today) {
    void context.globalState.update('morningCheckDate', today);
    eventBus.command('morning-check', today, '今天第一次进入 VS Code 工作流。');
  }
  const autoStartDesktop = context.globalState.get('autoStartDesktop', configuredAutoStart);
  if (autoStartDesktop) void startDesktopPet(context, eventBus);
}

function getNonce() {
  const characters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  let value = '';
  for (let index = 0; index < 32; index += 1) {
    value += characters.charAt(Math.floor(Math.random() * characters.length));
  }
  return value;
}

module.exports = { activate };