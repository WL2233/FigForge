// =============================================================================
// FigForge — plugin main thread (Figma sandbox)
//
// Owns the document: builds the layer tree for the UI and runs ZIP exports.
// =============================================================================

import {
  DEFAULT_EXPORT_OPTIONS,
  DEFAULT_EXPORT_SCALE,
  type ElementConfig,
  type ExportOptions,
  type ExportScale,
} from './types';
import { buildTree } from './traverser';
import { exportDesign } from './exporter';

const WINDOW_PRESETS: Record<string, { w: number; h: number }> = {
  S: { w: 460, h: 600 },
  M: { w: 600, h: 760 },
  L: { w: 820, h: 920 },
  mini: { w: 260, h: 44 },
};

figma.showUI(__html__, { width: WINDOW_PRESETS.M.w, height: WINDOW_PRESETS.M.h, themeColors: true });

// Under manifest "documentAccess": "dynamic-page", reading a non-current page's
// .children / findAll throws unless its pages are loaded first. We load every
// page once at startup and gate the message handler
// on that promise. (Loading does NOT make figma.getNodeById legal — those call
// sites use getNodeByIdAsync.)
const pagesLoaded: Promise<void> = figma.loadAllPagesAsync();

// Per-session UI state we need to remember between messages.
const excluded = new Set<string>();
const merged = new Set<string>();
const forcedPng = new Set<string>();
const passthrough = new Set<string>();

function selectedRoot(): SceneNode | null {
  const sel = figma.currentPage.selection;
  if (sel.length === 0) return null;
  const node = sel[0];
  const ok = ['FRAME', 'COMPONENT', 'COMPONENT_SET', 'INSTANCE', 'GROUP'].includes(node.type);
  return ok ? node : null;
}

// buildTree is async, so two pushSelection runs can overlap (selectionchange
// firing again, or selectionchange racing the startup/explicit reload calls).
// A monotonically-increasing token makes only the LATEST run post to the UI: a
// stale run that resolves second is dropped rather than overwriting fresher
// state. The whole body is wrapped so a buildTree rejection surfaces as a
// no-selection state instead of an unhandled rejection swallowed by `void`.
let pushSelectionToken = 0;
async function pushSelection() {
  const token = ++pushSelectionToken;
  try {
    const root = selectedRoot();
    if (!root) {
      if (token === pushSelectionToken) figma.ui.postMessage({ type: 'no-selection' });
      return;
    }
    const tree = await buildTree(root, excluded);
    if (token !== pushSelectionToken) return; // superseded by a newer pushSelection
    const size = {
      w: (root as unknown as { width?: number }).width ?? 0,
      h: (root as unknown as { height?: number }).height ?? 0,
    };
    let count = 0;
    const walk = (n: typeof tree) => {
      count++;
      n.children.forEach(walk);
    };
    walk(tree);
    figma.ui.postMessage({ type: 'selection-info', name: root.name, elementCount: count, size, tree });
  } catch (e) {
    if (token === pushSelectionToken) {
      figma.ui.postMessage({ type: 'no-selection', error: String((e as Error)?.message || e) });
    }
  }
}

figma.on('selectionchange', () => { void pushSelection(); });
void pushSelection();

// One export at a time. exportDesign isolates layers by MUTATING node.visible
// around each exportAsync call; a second export started mid-run interleaves with
// the first pass and each bakes the other's hidden layers into its PNGs. Each
// export checks this flag and rejects a concurrent request with a visible message —
// never queued silently (the selection/configs may be stale by completion time,
// so the user should re-trigger deliberately). Cleared in `finally` on every path.
let exportInFlight = false;
const EXPORT_BUSY_MESSAGE = 'An export is already running — wait for it to finish, then try again.';


// ---------------------------------------------------------------------------
// UI → main
// ---------------------------------------------------------------------------
figma.ui.onmessage = async (msg: { type: string; [k: string]: unknown }) => {
  // Settings do not depend on page loading (which may fail independently).
  if (msg.type === 'load-settings' || msg.type === 'save-settings') {
    const operation = msg.type === 'load-settings' ? 'load' : 'save';
    try {
      if (operation === 'load') {
        const prefs = await figma.clientStorage.getAsync('figforge.ui.prefs');
        figma.ui.postMessage({ type: 'settings-loaded', prefs });
      } else {
        if (!msg.prefs || typeof msg.prefs !== 'object' || Array.isArray(msg.prefs)) throw new Error('Invalid settings');
        await figma.clientStorage.setAsync('figforge.ui.prefs', msg.prefs);
        figma.ui.postMessage({ type: 'settings-saved' });
      }
    } catch (error) {
      figma.ui.postMessage({ type: 'settings-error', operation, error: String(error) });
    }
    return;
  }
  // Wait for every page to load before handling document messages.
  await pagesLoaded;
  switch (msg.type) {
    case 'reload':
      await pushSelection();
      break;

    case 'resize-ui': {
      const preset = WINDOW_PRESETS[(msg.preset as string) || 'M'] || WINDOW_PRESETS.M;
      figma.ui.resize(preset.w, preset.h);
      break;
    }

    case 'toggle-visibility': {
      const id = msg.nodeId as string;
      if (excluded.has(id)) excluded.delete(id);
      else excluded.add(id);
      await pushSelection();
      break;
    }

    case 'toggle-merge': {
      const id = msg.nodeId as string;
      if (merged.has(id)) merged.delete(id);
      else merged.add(id);
      await pushSelection();
      break;
    }

    case 'toggle-png': {
      const id = msg.nodeId as string;
      if (forcedPng.has(id)) forcedPng.delete(id);
      else forcedPng.add(id);
      await pushSelection();
      break;
    }

    case 'toggle-passthrough': {
      const id = msg.nodeId as string;
      if (passthrough.has(id)) passthrough.delete(id);
      else passthrough.add(id);
      await pushSelection();
      break;
    }

    case 'highlight-element': {
      const node = await figma.getNodeByIdAsync(msg.nodeId as string) as SceneNode | null;
      if (node) {
        figma.currentPage.selection = [node];
        figma.viewport.scrollAndZoomIntoView([node]);
      }
      break;
    }

    case 'preview-element':
      await sendPreview(msg.nodeId as string);
      break;

    case 'export': {
      // Rejected as 'status' (error-flagged), NOT 'export-error': ui.ts reacts to
      // export-error by tearing down the progress bar — UI state that belongs to
      // the export that's still legitimately running.
      if (exportInFlight) {
        figma.ui.postMessage({ type: 'status', message: EXPORT_BUSY_MESSAGE, error: true });
        break;
      }
      const root = selectedRoot();
      if (!root) {
        figma.ui.postMessage({ type: 'export-error', message: 'Select a frame to export.' });
        break;
      }
      exportInFlight = true;
      try {
        const scale = (msg.scale as ExportScale) || DEFAULT_EXPORT_SCALE;
        const options: ExportOptions = { ...DEFAULT_EXPORT_OPTIONS, cleanNames: (msg.options as Partial<ExportOptions> | undefined)?.cleanNames === true };
        applyConfigs(msg.elementConfigs as ElementConfig[] | undefined);
        const result = await exportDesign(
          root,
          scale,
          options,
          excluded,
          merged,
          forcedPng,
          passthrough,
          (current, total, label) =>
            figma.ui.postMessage({ type: 'progress', current, total, label })
        );
        figma.ui.postMessage({
          type: 'export-complete',
          manifest: JSON.stringify(result.manifest, null, 2),
          assets: result.assets,
        });
      } catch (e) {
        figma.ui.postMessage({ type: 'export-error', message: String((e as Error)?.message || e) });
      } finally {
        exportInFlight = false;
      }
      break;
    }

    case 'cancel':
      figma.closePlugin();
      break;
  }
};

function applyConfigs(configs: ElementConfig[] | undefined) {
  if (!configs) return;
  for (const c of configs) {
    if (c.excluded) excluded.add(c.id);
    else excluded.delete(c.id);
    if (c.merged) merged.add(c.id);
    else merged.delete(c.id);
    if (c.rasterize) forcedPng.add(c.id);
    else forcedPng.delete(c.id);
    if (c.passthrough) passthrough.add(c.id);
    else passthrough.delete(c.id);
  }
}

async function sendPreview(nodeId: string) {
  const node = await figma.getNodeByIdAsync(nodeId) as SceneNode | null;
  if (!node || !('exportAsync' in node)) return;
  try {
    const bytes = await (node as unknown as {
      exportAsync: (s: ExportSettings) => Promise<Uint8Array>;
    }).exportAsync({ format: 'PNG', constraint: { type: 'SCALE', value: 1 } });
    figma.ui.postMessage({
      type: 'element-preview',
      nodeId,
      name: node.name,
      figmaType: node.type,
      size: { w: (node as unknown as { width: number }).width, h: (node as unknown as { height: number }).height },
      // Uint8Array structured-clones through postMessage — no number[] blow-up.
      imageData: bytes,
    });
  } catch {
    /* unpreviewable (e.g. empty container) — ignore */
  }
}
