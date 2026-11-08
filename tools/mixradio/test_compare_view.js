// Exercises the production viewer script in an isolated DOM/Canvas harness.
// No browser, window, navigation, network, or actual MixRadio capture is used.
// This verifies interaction logic and pixel calculations, not rendered layout.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const crypto = require('node:crypto');

const template = fs.readFileSync(path.join(__dirname, 'compare-view.html'), 'utf8');
const script = template.match(/<script>\s*([\s\S]*?)<\/script>/)[1];
let checks = 0;
function check(condition, message) { checks++; assert.ok(condition, message); }

class Element {
  constructor() { this.children = []; this.listeners = {}; this.dataset = {}; this.value = ''; this.textContent = ''; this.hidden = false; }
  append(...items) { this.children.push(...items); }
  replaceChildren() { this.children = []; }
  setAttribute() {}
  addEventListener(event, handler) { this.listeners[event] = handler; }
  querySelectorAll(kind) {
    const values = this.children.flatMap(item => typeof item === 'object' ? [item, ...item.querySelectorAll(kind)] : []);
    return values.filter(item => kind === 'input' ? item.tag === 'input' : kind === 'option' && item.tag === 'option');
  }
}

class Canvas extends Element {
  constructor() {
    super(); this.width = 0; this.height = 0; this.draws = []; this.globalAlpha = 1; this.rectangles = [];
    this.context = {
      drawImage: (source, ...args) => {
        this.draws.push({ alpha: this.context.globalAlpha, arguments: args });
        if (args.length !== 8) return; // Composite commands are asserted separately.
        const [x, y, width, height] = args;
        this.pixels = new Uint8ClampedArray(this.width * this.height * 4);
        for (let row = 0; row < height; row++) for (let column = 0; column < width; column++) {
          const start = ((row + y) * source.width + column + x) * 4;
          this.pixels.set(source.pixels.slice(start, start + 4), (row * width + column) * 4);
        }
      },
      getImageData: () => ({ data: this.pixels }),
      putImageData: data => { this.lastPut = data; },
      save() {}, restore() {}, beginPath() {}, clip() {},
      rect: (...args) => this.rectangles.push(args), globalAlpha: 1
    };
  }
  getContext() { return this.context; }
  toBlob(callback) { callback(new Blob(['synthetic difference harness'], { type: 'image/png' })); }
}

function imageFixture(width, height, changed) {
  const pixels = new Uint8ClampedArray(width * height * 4);
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    pixels.set([changed && x >= 60 && y >= 40 ? 100 : 40, 80, 120, 255], (y * width + x) * 4);
  }
  return { width, height, pixels };
}

async function harness({ missing = false, differentSize = false } = {}) {
  const nodes = Object.fromEntries(['payload', 'title', 'provenance', 'notice', 'mode', 'amount', 'amountValue', 'threshold', 'cropControls', 'metrics',
    'sidePanels', 'compositePanel', 'referenceCaption', 'candidateCaption', 'notes', 'reviewer', 'decision', 'export', 'saveReview', 'saveDiff',
    'applyCrops', 'resetCrops', 'actualPixels'].map(id => [id, new Element()]));
  for (const role of ['reference', 'candidate']) nodes[role + 'Canvas'] = new Canvas();
  nodes.composite = new Canvas();
  for (const value of ['side', 'overlay', 'wipe', 'diff']) {
    const option = new Element(); option.tag = 'option'; option.value = value; nodes.mode.append(option);
  }
  nodes.mode.value = 'side'; nodes.amount.value = '50'; nodes.threshold.value = '12';
  const fixtures = { candidate: imageFixture(differentSize ? 128 : 120, 80, true), reference: imageFixture(120, 80, false) };
  const payload = { schemaVersion: 1, title: 'Synthetic viewer logic self-test', candidateKind: 'synthetic', referenceKind: missing ? 'missing' : 'synthetic',
    images: {}, reviewed: false, decision: null, crops: {} };
  for (const role of ['reference', 'candidate']) {
    const fixture = fixtures[role];
    payload.images[role] = role === 'reference' && missing ? null : { name: 'synthetic-' + role, width: fixture.width, height: fixture.height,
      sha256: crypto.createHash('sha256').update(fixture.pixels).digest('hex'), dataUrl: 'synthetic-harness:' + role };
    payload.crops[role] = payload.images[role] ? { x: 0, y: 0, width: fixture.width, height: fixture.height } : null;
  }
  nodes.payload.textContent = JSON.stringify(payload);
  const classes = new Set(), downloads = [];
  const document = {
    body: { classList: { toggle: (name, enabled) => enabled ? classes.add(name) : classes.delete(name) } },
    getElementById: id => nodes[id], querySelector: selector => nodes[selector.slice(1)],
    createElement: tag => {
      if (tag === 'canvas') return new Canvas();
      const element = new Element(); element.tag = tag; element.click = () => downloads.push(element.download); return element;
    }
  };
  const context = vm.createContext({ document, structuredClone, Blob, setTimeout: callback => callback(),
    URL: { createObjectURL: () => 'synthetic-harness:download', revokeObjectURL() {} },
    ImageData: class { constructor(width, height) { this.width = width; this.height = height; this.data = new Uint8ClampedArray(width * height * 4); } },
    Image: class {
      set src(value) { Object.assign(this, fixtures[value.split(':')[1]]); Promise.resolve().then(() => this.onload()); }
    }
  });
  vm.runInContext(script, context, { timeout: 1000, filename: 'compare-view.html' });
  await new Promise(resolve => setImmediate(resolve));
  const metadata = () => JSON.parse(JSON.stringify(vm.runInContext('metadata', context)));
  const event = (id, name = 'input') => nodes[id].listeners[name]({ target: nodes[id] });
  return { nodes, metadata, event, downloads, classes };
}

async function main() {
  const normal = await harness();
  const { nodes, metadata, event } = normal;
  check(nodes.provenance.textContent.includes('Synthetic fixture'), 'Provenance must remain visible.');
  check(nodes.notice.textContent.includes('cannot satisfy runtime'), 'Synthetic fixtures cannot certify runtime parity.');
  check(metadata().metrics.meanAbsoluteRGBA === 3.75, 'Known 25% change must have MAE 3.75/255.');
  check(metadata().metrics.rootMeanSquareRGBA === 15, 'Known pixel values must have RMS 15.');
  check(metadata().metrics.changedPixelPercent === 25, 'Known quadrant change must be 25%.');
  check(nodes.mode.children.every(option => !option.disabled), 'Equal frames enable comparison modes.');

  nodes.mode.value = 'overlay'; event('mode');
  check(nodes.sidePanels.hidden && !nodes.compositePanel.hidden, 'Overlay selects the composite panel.');
  check(nodes.composite.draws.some(draw => draw.alpha === 0.5), 'Overlay uses the requested opacity.');
  nodes.amount.value = '25'; event('amount');
  check(nodes.amountValue.textContent === '25%', 'Amount label follows the slider.');
  nodes.mode.value = 'wipe'; event('mode');
  check(nodes.composite.rectangles.some(rectangle => rectangle[2] === 30), '25% wipe clips at 30 of 120 pixels.');
  nodes.mode.value = 'diff'; event('mode');
  check(nodes.composite.lastPut.width === 120 && nodes.composite.lastPut.height === 80, 'Difference uses original pixel dimensions.');
  nodes.threshold.value = '80'; event('threshold');
  check(metadata().metrics.changedPixelPercent === 0, 'Pixels below a higher threshold are not marked changed.');
  nodes.threshold.value = '300'; event('threshold');
  check(nodes.notice.textContent.includes('between 0 and 255'), 'Invalid thresholds expose an error.');
  event('saveDiff', 'click');
  check(normal.downloads.length === 0, 'Invalid differences must not export an image.');
  nodes.threshold.value = '12'; event('threshold');

  for (const input of nodes.cropControls.querySelectorAll('input')) input.value = { x: 10, y: 10, width: 40, height: 20 }[input.dataset.key];
  event('applyCrops', 'click');
  check(metadata().metrics.width === 40 && metadata().metrics.height === 20, 'Equal crops remain unscaled.');
  check(metadata().metrics.changedPixelPercent === 0, 'The selected unchanged region has no changed pixels.');
  nodes.cropControls.querySelectorAll('input')[0].value = 999;
  event('applyCrops', 'click');
  check(nodes.notice.textContent.includes('outside its input'), 'Invalid crop bounds are rejected.');
  check(metadata().crops.reference.x === 10, 'Rejected crop edits preserve the last valid crop.');
  event('resetCrops', 'click');
  check(metadata().metrics.width === 120 && metadata().metrics.changedPixelPercent === 25, 'Reset restores the original input.');

  nodes.notes.value = 'Synthetic tool fixture observation.'; nodes.decision.value = 'pass';
  event('saveReview', 'click');
  check(!metadata().reviewed && normal.downloads.length === 0, 'A reviewer is required before export.');
  nodes.reviewer.value = 'tool-self-test'; event('saveReview', 'click');
  const exported = JSON.parse(nodes.export.textContent);
  check(exported.reviewed && exported.reviewer === 'tool-self-test', 'Export records the reviewer and decision.');
  check(exported.reviewedAt.endsWith('Z'), 'Review timestamp includes UTC.');
  check(exported.referenceKind === 'synthetic' && exported.candidateKind === 'synthetic', 'Export preserves synthetic provenance.');
  check(!exported.images.reference.dataUrl && !exported.images.candidate.dataUrl, 'Review JSON contains hashes, without embedded pixels.');
  nodes.actualPixels.checked = true; event('actualPixels', 'change');
  check(normal.classes.has('actual-pixels'), 'Actual-pixel mode toggles its display class.');
  check(template.includes('.actual-pixels #composite'), 'Actual-pixel rule applies to the composite canvas too.');
  event('saveDiff', 'click');
  check(normal.downloads.includes('difference.png'), 'A valid difference can be exported.');

  for (const options of [{ missing: true }, { differentSize: true }]) {
    const value = await harness(options);
    check(value.metadata().metrics === null, 'Missing or unequal frames have no pixel score.');
    check(value.nodes.mode.children.filter(option => option.value !== 'side').every(option => option.disabled), 'Unavailable comparisons disable composite modes.');
    check(value.nodes.saveDiff.disabled, 'Unavailable comparisons disable PNG export.');
    value.nodes.mode.value = 'overlay'; value.event('mode');
    check(value.nodes.mode.value === 'side', 'Unavailable overlays fall back to side-by-side inspection.');
  }
  console.log(`PASS: ${checks} comparison viewer checks in an isolated DOM/Canvas harness.`);
  console.log('Native browser rendering and actual MixRadio visual parity remain outside this test.');
}

main().catch(error => { console.error(error); process.exitCode = 1; });
