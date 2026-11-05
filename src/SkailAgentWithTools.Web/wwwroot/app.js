const KEY = 'agent-with-tools.caseId';
const ICONS = { Started: '💬', ModelTool: '🧠', ModelAnswer: '🧠', ToolResult: '🔧', Invalid: '⚠️', Answer: '✅', Handoff: '🙋', Failed: '❌' };
const LABELS = {
  Started: 'customer', ModelTool: 'model decision', ModelAnswer: 'model decision', ToolResult: 'tool result',
  Invalid: 'invalid decision', Answer: 'answer', Handoff: 'handoff', Failed: 'failure',
};
const PHASE_LABELS = { Starting: 'Starting…', Thinking: 'Thinking', UsingTool: 'Using tool', Answered: 'Answered', Escalated: 'Escalated to a human', Failed: 'Failed' };
const BADGES = { Answered: 'done', Escalated: 'warn', Failed: 'failed' };

const $ = (id) => document.getElementById(id);
const els = {
  message: $('message'), ask: $('ask'), state: $('state'), step: $('step'), modelCalls: $('modelCalls'), planner: $('planner'),
  error: $('error'), result: $('result'), timeline: $('timeline'), caseId: $('caseId'), monitor: $('monitor'),
};

let supportCase = null;
let source = null;

const isTerminal = (c) => c && ['Answered', 'Escalated', 'Failed'].includes(c.phase);
const time = (iso) => new Date(iso).toLocaleTimeString('en-US');

function showError(message) {
  els.error.hidden = !message;
  els.error.textContent = message ?? '';
}

function renderResult(r) {
  els.result.hidden = !r;
  els.result.replaceChildren();
  if (!r) return;
  const answered = r.end === 'Answered';
  els.result.className = 'result ' + (answered ? 'ok' : 'warn');
  const title = document.createElement('strong');
  title.textContent = answered ? 'Answered' : 'Escalated to a human';
  els.result.append(title, ` · ${r.text}`);
  if (!answered) {
    const conversation = document.createElement('pre');
    conversation.className = 'conversation';
    conversation.textContent = r.conversation.join('\n');
    els.result.append(conversation);
  }
}

function renderStep(entry) {
  const item = document.createElement('li');
  item.className = `step ${entry.kind}`;
  item.dataset.kind = entry.kind;
  const icon = document.createElement('span');
  icon.className = 'icon';
  icon.setAttribute('role', 'img');
  icon.setAttribute('aria-label', LABELS[entry.kind] ?? entry.kind);
  icon.textContent = ICONS[entry.kind] ?? '•';
  const at = document.createElement('time');
  at.textContent = time(entry.at);
  item.append(icon, at, ` ${entry.text}`);
  return item;
}

function render() {
  const active = supportCase?.active === true;
  els.ask.disabled = active;
  els.ask.textContent = active ? 'Working…' : 'Ask';
  els.message.disabled = active;

  els.state.textContent = supportCase ? PHASE_LABELS[supportCase.phase] : 'Idle';
  els.state.className = 'badge ' + (!supportCase ? 'idle' : BADGES[supportCase.phase] ?? 'active');
  els.step.textContent = supportCase?.step ?? 0;
  els.modelCalls.textContent = supportCase?.modelCalls ?? 0;
  els.planner.textContent = supportCase?.planner ?? '-';
  els.caseId.textContent = supportCase?.caseId ?? '-';
  showError(supportCase?.error);
  renderResult(supportCase?.result);
  els.timeline.replaceChildren(...(supportCase?.timeline ?? []).map(renderStep));
}

function connect(id) {
  source?.close();
  source = new EventSource(`/api/cases/${id}/stream`);
  source.onmessage = (event) => {
    supportCase = JSON.parse(event.data);
    render();
    if (isTerminal(supportCase)) {
      source.close();
      localStorage.removeItem(KEY);
    }
  };
  // EventSource reconnects by itself, and the stream starts with the current state
}

async function ask() {
  els.ask.disabled = true;
  els.message.disabled = true;
  showError(null);
  try {
    const res = await fetch('/api/cases', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: els.message.value }),
    });
    if (!res.ok) throw new Error((await res.json().catch(() => ({}))).error ?? `HTTP ${res.status}`);
    supportCase = await res.json();
    localStorage.setItem(KEY, supportCase.caseId);
    render();
    connect(supportCase.caseId);
  } catch (e) {
    render();
    showError(`Could not start: ${e.message}`);
  }
}

els.ask.addEventListener('click', ask);

fetch('/api/config').then((r) => r.json()).then((c) => {
  if (!els.message.value) els.message.value = c.defaultMessage;
  if (c.monitorUrl) { els.monitor.href = c.monitorUrl; els.monitor.hidden = false; }
}).catch(() => {});

// a reload keeps the page right: the state always comes from the server
const saved = localStorage.getItem(KEY);
if (saved) {
  fetch(`/api/cases/${saved}`).then((r) => (r.ok ? r.json() : null)).then((c) => {
    if (!c) return localStorage.removeItem(KEY);
    supportCase = c;
    els.message.value = c.question;
    render();
    if (isTerminal(c)) localStorage.removeItem(KEY);
    else connect(c.caseId);
  }).catch(() => localStorage.removeItem(KEY));
}
render();
