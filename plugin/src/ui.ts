// =============================================================================
// FigForge — UI thread (iframe)
// =============================================================================

import { DEFAULT_EXPORT_OPTIONS } from './types';
import { createSharedBundle, validateBundleName, type BundleFrame } from './asset-bundle';
import {
  type ElementConfig,
  type ExportOptions,
  type ExportScale,
  type TreeNode,
} from './types';

declare const JSZip: any;
declare const __FIGFORGE_VERSION__: string; // injected by esbuild from package.json

const $ = <T extends HTMLElement = HTMLElement>(sel: string) => document.querySelector(sel) as T;
function post(msg: Record<string, unknown>) {
  parent.postMessage({ pluginMessage: msg }, '*');
}

// Show the real version in the header pill.
$('#version').textContent = 'v' + __FIGFORGE_VERSION__;

// ---- per-element config state ----
const excluded = new Set<string>();
const merged = new Set<string>();
const forcedPng = new Set<string>();
const passthrough = new Set<string>();
let currentTrees: TreeNode[] = [];
let exportBusy = false;
let queuedCount = 0;
let canAdd = false;
function updateExportButton() {
  ($('#exportBtn') as HTMLButtonElement).disabled = exportBusy || !queuedCount;
  $('#exportBtn').textContent = exportBusy ? '导出中…' : `导出 ZIP（${queuedCount} 个）`;
  $<HTMLButtonElement>('#addSelectionBtn').disabled = exportBusy || !canAdd;
  $<HTMLButtonElement>('#clearSelectionBtn').disabled = exportBusy || !queuedCount;
}
let selectedId: string | null = null;
let expandedNodes = new Set<string>();

type TreeFilter = 'all' | 'exported' | 'hidden' | 'merged' | 'canonical';
type PreviewZoomMode = 'fit' | 'actual' | 'manual';
interface UiPrefs {
  controls?: Record<string, string | boolean>;
  scale?: string;
  chips?: Record<string, boolean>;
  windowPreset?: string;
  treeFilter?: TreeFilter;
}

const PREFS_KEY = 'figforge.ui.prefs';
// Keep edits in memory until the user explicitly saves. localStorage is only a
// migration source; Figma's main-thread clientStorage owns durable settings.
let workingPrefs: UiPrefs = readLegacyPrefs();
const settingsControls = Array.from(document.querySelectorAll<HTMLInputElement | HTMLSelectElement>(
  'input:not([type="file"]), select'
)).filter((el) => el.id !== 'search');
const controlKey = (el: HTMLInputElement | HTMLSelectElement) => el.id || el.closest('[id]')?.id || '';
const defaultControls = captureControls();
function captureControls(): Record<string, string | boolean> {
  return Object.fromEntries(settingsControls.map((el) => [controlKey(el),
    el instanceof HTMLInputElement && (el.type === 'checkbox' || el.type === 'radio') ? el.checked : el.value]));
}
document.body.inert = true;
const TREE_FILTERS = new Set<TreeFilter>(['all', 'exported', 'hidden', 'merged', 'canonical']);
const hasTreeFilters = Boolean(document.getElementById('treeFilters'));
let activeTreeFilter: TreeFilter = hasTreeFilters ? readTreeFilter(readPrefs().treeFilter) : 'all';
let previewImg: HTMLImageElement | null = null;
let previewObjectUrl: string | null = null;
let previewZoom = 1;
let previewZoomMode: PreviewZoomMode = 'fit';

function readLegacyPrefs(): UiPrefs {
  try {
    const raw = localStorage.getItem(PREFS_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === 'object' ? parsed : {};
  } catch {
    return {};
  }
}

function readPrefs(): UiPrefs { return workingPrefs; }
function savePrefs(patch: UiPrefs) { workingPrefs = { ...workingPrefs, ...patch }; }

function readTreeFilter(value: unknown): TreeFilter {
  return typeof value === 'string' && TREE_FILTERS.has(value as TreeFilter) ? (value as TreeFilter) : 'all';
}

// ---------------------------------------------------------------------------
// Header chrome
// ---------------------------------------------------------------------------
const savedPreset = readPrefs().windowPreset;
let lastPreset = savedPreset === 'S' || savedPreset === 'L' ? savedPreset : 'M';
function setSizePresetUi(preset: string) {
  document.querySelectorAll('#sizeSeg button').forEach((x) => {
    x.classList.toggle('active', (x as HTMLElement).dataset.size === preset);
  });
}
setSizePresetUi(lastPreset);
if (lastPreset !== 'M') post({ type: 'resize-ui', preset: lastPreset });
document.querySelectorAll('#sizeSeg button').forEach((b) =>
  b.addEventListener('click', () => {
    lastPreset = (b as HTMLElement).dataset.size!;
    setSizePresetUi(lastPreset);
    savePrefs({ windowPreset: lastPreset });
    post({ type: 'resize-ui', preset: lastPreset });
  })
);

let minimized = false;
const minBtn = $('#minBtn');
minBtn.addEventListener('click', () => {
  minimized = !minimized;
  document.body.classList.toggle('minimized', minimized);
  minBtn.textContent = minimized ? '⤢' : '—';
  minBtn.title = minimized ? 'Restore' : 'Minimize';
  post({ type: 'resize-ui', preset: minimized ? 'mini' : lastPreset });
});

$('#reloadBtn').addEventListener('click', () => post({ type: 'reload' }));
$('#clearLog').addEventListener('click', clearLog);
const savedScale = readPrefs().scale;
if (savedScale) {
  const scaleEl = $('#scale') as HTMLSelectElement;
  if ([...scaleEl.options].some((o) => o.value === savedScale)) scaleEl.value = savedScale;
}
$('#scale').addEventListener('change', () => savePrefs({ scale: ($('#scale') as HTMLSelectElement).value }));
// option chips
function wireChip(id: string) {
  const el = $(`#${id}`);
  const input = el.querySelector('input') as HTMLInputElement;
  const saved = readPrefs().chips?.[id];
  if (typeof saved === 'boolean') input.checked = saved;
  el.classList.toggle('on', input.checked);
  el.addEventListener('click', (e) => {
    if (e.target !== input) {
      e.preventDefault();
      input.checked = !input.checked;
    }
    el.classList.toggle('on', input.checked);
    savePrefs({ chips: { ...(readPrefs().chips || {}), [id]: input.checked } });
  });
}
['optCleanNames'].forEach(wireChip);

// ---------------------------------------------------------------------------
// Tree rendering
// ---------------------------------------------------------------------------
const TYPE_SHORT: Record<string, string> = {
  FRAME: 'FRM', GROUP: 'GRP', TEXT: 'TXT', VECTOR: 'VEC', INSTANCE: 'INS',
  COMPONENT: 'CMP', RECTANGLE: 'RECT', ELLIPSE: 'ELL', LINE: 'LN',
};

function renderTree() {
  const host = $('#tree');
  host.innerHTML = '';
  updateTreeStats();
  updateTreeFilterUi();
  if (!currentTrees.length) {
    host.innerHTML = '<div class="empty">请选择一个或多个同级 Frame、组件或 Group。</div>';
    return;
  }
  const query = ($('#search') as HTMLInputElement).value.trim().toLowerCase();
  const revealingMatches = Boolean(query) || activeTreeFilter !== 'all';

  const walk = (node: TreeNode): HTMLElement | null => {
    const children = node.children || [];
    const expanded = expandedNodes.has(node.id);
    const nameMatch = !query || node.displayName.toLowerCase().includes(query);
    const filterMatch = matchesTreeFilter(node);
    const selfMatch = nameMatch && filterMatch;
    const childEls = (revealingMatches || expanded) ? children.map(walk).filter(Boolean) as HTMLElement[] : [];
    if (!selfMatch && childEls.length === 0) return null;

    const row = document.createElement('div');
    row.className = 'row';
    if (node.id === selectedId) row.classList.add('sel');
    if (excluded.has(node.id) || !node.visible) row.classList.add('hidden');
    row.style.paddingLeft = `${8 + node.depth * 12}px`;

    row.innerHTML = `
      <span class="twirl" title="${children.length ? 'Expand/collapse' : ''}">${children.length ? (expanded || revealingMatches ? '▾' : '▸') : ''}</span>
      <span class="type-tag">${escapeHtml(TYPE_SHORT[node.type] || node.type.slice(0, 3))}</span>
      <span class="tname">${escapeHtml(node.displayName)}</span>
      ${node.canonicalRef ? `<span class="canon-tag" title="canonical: ${escapeHtml(node.canonicalRef)}">${escapeHtml(node.canonicalRef)}</span>` : ''}
    `;
    const twirl = row.querySelector('.twirl') as HTMLElement | null;
    if (twirl && children.length) {
      twirl.style.cursor = 'pointer';
      twirl.addEventListener('click', (e) => {
        e.stopPropagation();
        toggle(expandedNodes, node.id);
        renderTree();
      });
    }

    const eye = miniBtn('👁', excluded.has(node.id) ? '' : 'on', 'Toggle export', () => {
      toggle(excluded, node.id); post({ type: 'toggle-visibility', nodeId: node.id }); renderTree();
    });
    row.appendChild(eye);

    if (node.canMerge) {
      row.appendChild(miniBtn('⊞', merged.has(node.id) ? 'on' : '', 'Merge into one PNG', () => {
        toggle(merged, node.id); post({ type: 'toggle-merge', nodeId: node.id }); renderTree();
      }));
    }
    if (node.canExportPng && node.type === 'TEXT') {
      row.appendChild(miniBtn('P', forcedPng.has(node.id) ? 'on png' : 'png', 'Rasterize as PNG', () => {
        toggle(forcedPng, node.id); post({ type: 'toggle-png', nodeId: node.id }); renderTree();
      }));
    }
    if (node.canPassthrough) {
      const byName = node.passthroughByName === true;
      const on = byName || passthrough.has(node.id);
      const title = byName
        ? 'Pass-through (marked by layer name) — children promoted to the parent'
        : 'Pass-through: skip this container, promote its children to its parent';
      row.appendChild(miniBtn('↑', on ? 'on pass' : 'pass', title, () => {
        if (byName) return; // name-driven — toggle via the layer name, not here
        toggle(passthrough, node.id); post({ type: 'toggle-passthrough', nodeId: node.id }); renderTree();
      }));
    }

    row.addEventListener('click', () => {
      selectedId = node.id;
      post({ type: 'highlight-element', nodeId: node.id });
      post({ type: 'preview-element', nodeId: node.id });
      renderTree();
    });

    const frag = document.createElement('div');
    frag.appendChild(row);
    childEls.forEach((c) => frag.appendChild(c));
    return frag;
  };

  for (const root of currentTrees) {
    const tree = walk(root);
    if (tree) host.appendChild(tree);
  }
  if (!host.childElementCount) host.innerHTML = '<div class="empty">No matching layers.</div>';
}

function miniBtn(label: string, cls: string, title: string, onClick: () => void): HTMLButtonElement {
  const b = document.createElement('button');
  b.className = `mini ${cls}`;
  b.textContent = label;
  b.title = title;
  b.addEventListener('click', (e) => { e.stopPropagation(); onClick(); });
  return b;
}
function toggle(set: Set<string>, id: string) { set.has(id) ? set.delete(id) : set.add(id); }
function escapeHtml(s: string) { return s.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]!)); }

function matchesTreeFilter(node: TreeNode): boolean {
  switch (activeTreeFilter) {
    case 'exported': return !excluded.has(node.id) && node.visible;
    case 'hidden': return excluded.has(node.id) || !node.visible;
    case 'merged': return merged.has(node.id);
    case 'canonical': return Boolean(node.canonicalRef);
    default: return true;
  }
}

function updateTreeStats() {
  const el = document.getElementById('treeStats');
  if (!el) return;
  if (!currentTrees.length) {
    el.textContent = '';
    return;
  }
  const stats = { total: 0, exported: 0, excluded: 0, merged: 0, raster: 0, pass: 0 };
  const walk = (node: TreeNode) => {
    stats.total += 1;
    if (!excluded.has(node.id) && node.visible) stats.exported += 1;
    if (excluded.has(node.id) || !node.visible) stats.excluded += 1;
    if (merged.has(node.id)) stats.merged += 1;
    if (forcedPng.has(node.id)) stats.raster += 1;
    if (passthrough.has(node.id) || node.passthroughByName) stats.pass += 1;
    (node.children || []).forEach(walk);
  };
  currentTrees.forEach(walk);
  const passPart = stats.pass ? ` · pass-through ${stats.pass}` : '';
  el.textContent = `total ${stats.total} · exported ${stats.exported} · excluded ${stats.excluded} · merged ${stats.merged} · raster ${stats.raster}${passPart}`;
}

function updateTreeFilterUi() {
  const host = document.getElementById('treeFilters');
  if (!host) return;
  host.querySelectorAll<HTMLButtonElement>('button[data-filter]').forEach((btn) => {
    btn.classList.toggle('active', btn.dataset.filter === activeTreeFilter);
  });
}

function wireTreeFilters() {
  const host = document.getElementById('treeFilters');
  if (!host) return;
  host.addEventListener('click', (e) => {
    const target = e.target;
    const btn = target instanceof HTMLElement ? target.closest<HTMLButtonElement>('button[data-filter]') : null;
    if (!btn) return;
    activeTreeFilter = readTreeFilter(btn.dataset.filter);
    savePrefs({ treeFilter: activeTreeFilter });
    renderTree();
  });
  updateTreeFilterUi();
}

$('#search').addEventListener('input', renderTree);
wireTreeFilters();

// ---------------------------------------------------------------------------
// Preview zoom
// ---------------------------------------------------------------------------
function updatePreviewZoom() {
  if (!previewImg) return;
  if (previewZoomMode === 'fit') {
    previewImg.style.width = '';
    previewImg.style.height = '';
    previewImg.style.maxWidth = '100%';
    previewImg.style.maxHeight = '100%';
    setPreviewZoomLabel('Fit');
    return;
  }
  previewImg.style.maxWidth = 'none';
  previewImg.style.maxHeight = 'none';
  previewImg.style.width = `${Math.max(1, Math.round(previewImg.naturalWidth * previewZoom))}px`;
  previewImg.style.height = 'auto';
  setPreviewZoomLabel(`${Math.round(previewZoom * 100)}%`);
}

function setPreviewZoomLabel(text: string) {
  const label = document.getElementById('previewZoomLabel');
  if (label) label.textContent = text;
}

function setPreviewZoom(mode: PreviewZoomMode, zoom = previewZoom) {
  previewZoomMode = mode;
  previewZoom = Math.min(4, Math.max(0.1, zoom));
  updatePreviewZoom();
}

function wirePreviewZoom() {
  document.getElementById('previewFit')?.addEventListener('click', () => setPreviewZoom('fit', 1));
  document.getElementById('previewActual')?.addEventListener('click', () => setPreviewZoom('actual', 1));
  document.getElementById('previewZoomOut')?.addEventListener('click', () => setPreviewZoom('manual', previewZoomMode === 'fit' ? 0.9 : previewZoom - 0.1));
  document.getElementById('previewZoomIn')?.addEventListener('click', () => setPreviewZoom('manual', previewZoomMode === 'fit' ? 1.1 : previewZoom + 0.1));
  setPreviewZoomLabel('Fit');
}
wirePreviewZoom();

// ---------------------------------------------------------------------------
// Export
// ---------------------------------------------------------------------------
function parseScale(): ExportScale {
  const v = ($('#scale') as HTMLSelectElement).value;
  const [t, n] = v.split(':');
  const value = parseFloat(n);
  if (t === 'w') return { type: 'width', value };
  if (t === 'h') return { type: 'height', value };
  return { type: 'scale', value };
}

function collectConfigs(): ElementConfig[] {
  const ids = new Set<string>([...excluded, ...merged, ...forcedPng, ...passthrough]);
  return [...ids].map((id) => ({
    id,
    excluded: excluded.has(id),
    merged: merged.has(id),
    rasterize: forcedPng.has(id),
    passthrough: passthrough.has(id),
  }));
}

function currentOptions(): ExportOptions {
  return { ...DEFAULT_EXPORT_OPTIONS, cleanNames: ($('#optCleanNames input') as HTMLInputElement).checked };
}

$('#addSelectionBtn').addEventListener('click', () => post({ type: 'add-selection' }));
$('#clearSelectionBtn').addEventListener('click', () => post({ type: 'clear-selection' }));
$('#exportBtn').addEventListener('click', () => {
  if (exportBusy) return;
  let bundleName: string;
  try { bundleName = validateBundleName($<HTMLInputElement>('#bundleName').value); }
  catch (e) { setStatus((e as Error).message, true); return; }
  exportBusy = true;
  updateExportButton();
  setStatus('Exporting…');
  showProgress(true);
  post({ type: 'export', bundleName, scale: parseScale(), options: currentOptions(), elementConfigs: collectConfigs() });
});

// JSZip arrives from cdnjs at runtime (a <script> tag in ui.html) — offline or
// behind a corporate proxy the tag fails and the global never exists. Checked
// BEFORE any zip work so the failure is an honest, actionable message instead of
// an unhandled throw after the UI had already claimed "Bundle downloaded".
function jszipAvailable(): boolean {
  if (typeof JSZip !== 'undefined') return true;
  setStatus('JSZip failed to load — check network/proxy; bundle not saved.', true);
  return false;
}

// Script tags load synchronously in order, so by the time this module runs the
// JSZip global either exists or never will — surface a blocked cdnjs load the
// moment the UI opens, not at download time.
if (typeof JSZip === 'undefined') {
  setStatus('JSZip failed to load from cdnjs (offline / proxy?) — ZIP export is unavailable.', true);
}

async function downloadBundle(bundleName: string, frames: BundleFrame[]) {
  try {
    if (!jszipAvailable()) return;
    const zip = new JSZip();
    const bundle = await createSharedBundle(bundleName, frames);
    for (const directory of bundle.directories) zip.folder(directory);
    for (const [path, data] of bundle.files) zip.file(path, data);
    const blob = await zip.generateAsync({ type: 'blob' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${bundleName}.zip`;
    a.click();
    URL.revokeObjectURL(url);
    // Success is only claimed HERE — after the zip blob is handed to the browser.
    setStatus(`已导出 ${frames.length} 个预制体 JSON、${bundle.imageCount} 张共享图片。请将 ZIP 解压到目标目录。`);
  } catch (e) {
    setStatus(`Bundle packaging failed — ${String((e as Error)?.message || e)}; bundle not saved.`, true);
  } finally {
    exportBusy = false;
    showProgress(false);
    updateExportButton();
  }
}

// ---------------------------------------------------------------------------
// Status / progress helpers
// ---------------------------------------------------------------------------
function clockTime(): string {
  const d = new Date();
  const p = (n: number) => n.toString().padStart(2, '0');
  return `${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}

let lastLogReplaceable = false;
// Append a line to the scrollable status / bundle / Unity log. `replace` rewrites
// the previous line in place (used for rapid progress ticks so they don't flood).
// Auto-scrolls only when the user is already at the bottom, so reading history isn't
// yanked away. Full text wraps — nothing is truncated.
function appendLog(text: string, opts: { err?: boolean; dim?: boolean; ok?: boolean; replace?: boolean } = {}) {
  const el = $('#status');
  const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 24;
  let line = opts.replace && lastLogReplaceable && el.lastElementChild ? (el.lastElementChild as HTMLElement) : null;
  if (line) line.textContent = '';
  else { line = document.createElement('div'); el.appendChild(line); }
  line.className = 'log-line' + (opts.err ? ' err' : opts.ok ? ' ok' : opts.dim ? ' dim' : '');
  const ts = document.createElement('span'); ts.className = 'ts'; ts.textContent = clockTime();
  const msg = document.createElement('span'); msg.className = 'msg'; msg.textContent = text;
  line.appendChild(ts); line.appendChild(msg);
  lastLogReplaceable = !!opts.replace;
  while (el.childElementCount > 300) el.removeChild(el.firstElementChild as Node);
  if (atBottom) el.scrollTop = el.scrollHeight;
}

function setStatus(text: string, err = false, replace = false) { appendLog(text, { err, replace }); }
function clearLog() { $('#status').textContent = ''; lastLogReplaceable = false; }
function showProgress(show: boolean) { $('#progress').classList.toggle('show', show); }
function setProgress(pct: number) { ($('#progress > div') as HTMLElement).style.width = `${pct}%`; }

// ---------------------------------------------------------------------------
function applySettings(prefs: UiPrefs) {
  workingPrefs = prefs;
  const values = { ...defaultControls, ...prefs.controls };
  // Migrate settings saved by older UI versions.
  for (const key of ['scale'] as const) {
    if (typeof prefs[key] === 'string') values[key] = prefs[key]!;
  }
  Object.assign(values, prefs.chips || {});
  for (const el of settingsControls) {
    const value = values[controlKey(el)];
    if (el instanceof HTMLInputElement && (el.type === 'checkbox' || el.type === 'radio')) {
      if (typeof value === 'boolean') el.checked = value;
      el.closest('.chip')?.classList.toggle('on', el.checked);
    } else if (typeof value === 'string') {
      if (!(el instanceof HTMLSelectElement) || Array.from(el.options).some((o) => o.value === value)) el.value = value;
    }
  }
  activeTreeFilter = readTreeFilter(prefs.treeFilter);
  renderTree();
  lastPreset = prefs.windowPreset === 'S' || prefs.windowPreset === 'L' ? prefs.windowPreset : 'M';
  setSizePresetUi(lastPreset);
  post({ type: 'resize-ui', preset: minimized ? 'mini' : lastPreset });
}

const saveSettingsBtn = $('#saveSettingsBtn') as HTMLButtonElement;
saveSettingsBtn.addEventListener('click', () => {
  saveSettingsBtn.disabled = true;
  post({ type: 'save-settings', prefs: { ...workingPrefs, controls: captureControls() } });
  setStatus('正在保存设置…');
});
$('#resetSettingsBtn').addEventListener('click', () => {
  applySettings({});
  setStatus('已恢复默认设置；点击“Apply”后，下次打开将使用默认设置。');
});

// ---------------------------------------------------------------------------
// main → ui
// ---------------------------------------------------------------------------
window.onmessage = (event: MessageEvent) => {
  const msg = (event.data && event.data.pluginMessage) || event.data;
  if (!msg || !msg.type) return;

  switch (msg.type) {
    case 'settings-loaded':
      applySettings(msg.prefs && typeof msg.prefs === 'object' ? msg.prefs : workingPrefs);
      document.body.inert = false;
      break;
    case 'settings-saved':
      saveSettingsBtn.disabled = false;
      setStatus('当前设置已保存，下次打开会自动加载。');
      break;
    case 'settings-error':
      document.body.inert = false;
      saveSettingsBtn.disabled = false;
      setStatus(`设置${msg.operation === 'load' ? '读取' : '保存'}失败：${msg.error}`);
      break;
    case 'selection-info':
      const previousRoots = currentTrees.map(tree => tree.id).join(',');
      currentTrees = msg.trees;
      queuedCount = msg.queuedCount;
      canAdd = msg.canAdd;
      selectedId = null;
      if (currentTrees.map(tree => tree.id).join(',') !== previousRoots)
        expandedNodes = new Set(currentTrees.map(tree => tree.id));
      const size = msg.size && Number.isFinite(msg.size.w) && Number.isFinite(msg.size.h)
        ? ` · ${Math.round(msg.size.w)}×${Math.round(msg.size.h)}`
        : '';
      setStatus(`${queuedCount ? `已添加 ${queuedCount} 个` : '当前选择（点击 Add 加入导出）'} · ${msg.name} · ${msg.elementCount} layers${size}`);
      if (msg.selectionError) setStatus(msg.selectionError);
      updateExportButton();
      renderTree();
      break;

    case 'no-selection':
      currentTrees = [];
      queuedCount = 0;
      canAdd = false;
      updateExportButton();
      expandedNodes = new Set();
      setStatus(msg.error || '请选择一个或多个同级 Frame、组件或 Group。');
      renderTree();
      break;

    case 'element-preview': {
      if (previewObjectUrl) URL.revokeObjectURL(previewObjectUrl);
      const blob = new Blob([new Uint8Array(msg.imageData)], { type: 'image/png' });
      const url = URL.createObjectURL(blob);
      previewObjectUrl = url;
      $('#previewHead').textContent = `${msg.name} · ${msg.figmaType} · ${Math.round(msg.size.w)}×${Math.round(msg.size.h)}`;
      $('#previewWrap').innerHTML = '';
      const img = document.createElement('img');
      img.src = url;
      previewImg = img;
      img.onload = () => updatePreviewZoom();
      img.onerror = () => {
        // The blob never decoded (corrupt PNG / revoked URL) — restore the empty
        // state and reclaim the object URL instead of leaving a broken-image icon.
        if (previewObjectUrl === url) {
          URL.revokeObjectURL(url);
          previewObjectUrl = null;
        }
        previewImg = null;
        $('#previewWrap').innerHTML = '<div class="empty">Preview unavailable.</div>';
        setPreviewZoomLabel('Fit');
      };
      $('#previewWrap').appendChild(img);
      updatePreviewZoom();
      break;
    }

    case 'progress':
      setProgress(msg.total ? Math.round((msg.current / msg.total) * 100) : 0);
      setStatus(`Exporting ${msg.label} (${msg.current}/${msg.total})`, false, true); // replace in place
      break;

    // Download helpers report success only after ZIP packaging completes.
    case 'export-complete':
      showProgress(false);
      setProgress(0);
      void downloadBundle(msg.bundleName, msg.frames);

      break;

    case 'export-error':
      exportBusy = false;
      updateExportButton();
      showProgress(false);
      setStatus(msg.message, true);
      break;

    case 'status':
      // `error` is optional — main flags it on e.g. a rejected concurrent export
      // (which can't use 'export-error': that would tear down the progress UI of
      // the export that's still running).
      setStatus(msg.message, !!msg.error);
      break;

    default:
      // A message type main sent that this UI build doesn't handle (version skew
      // or a new path that forgot its UI case). Make it visible instead of
      // silently dropping it, so the gap is diagnosable from the log.
      appendLog(`unhandled message from main: ${String(msg.type)}`);
      break;
  }
};

// Request after installing the receiver, avoiding a startup message race.
post({ type: 'load-settings' });
