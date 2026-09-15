#!/usr/bin/env node
/**
 * pack_ue.js —— 基于 free-tex-packer-core（仍在维护的打包引擎）的 UE 图集 CLI
 *
 * 为什么有这个：
 *   odrick/free-tex-packer 的 GUI 壳子 2021 年就停更了，
 *   但它的打包核心 odrick/free-tex-packer-core 到 2026-07 还在更新
 *   （npm 0.3.9，修了 alpha 通道 bug、旋转 bug，加了 WebP）。
 *   这个脚本直接调核心引擎，绕开烂掉的 Electron 壳子。
 *
 * 安装：
 *   npm install free-tex-packer-core
 *
 * 用法：
 *   node pack_ue.js ./frames -o ./out --name fx_hit
 *   node pack_ue.js ./sprites -o ./out --name hero_idle --trim --rotate --padding 2 --extrude 2
 *   node pack_ue.js ./vfx -o ./out --each-subdir          # 每个子目录一张图集
 *   node pack_ue.js ./sprites -o ./out --format json-array
 */

const fs = require('fs');
const path = require('path');

let packAsync;
try {
  ({ packAsync } = require('free-tex-packer-core'));
} catch (e) {
  console.error('找不到 free-tex-packer-core，请先运行: npm install free-tex-packer-core');
  console.error('或设置 NODE_PATH 指向 node_modules 所在目录');
  process.exit(1);
}

const IMG_EXT = new Set(['.png', '.jpg', '.jpeg', '.bmp', '.webp']);

// 把「货架式」装箱器注册进 core 的列表 —— 注册后 getPackerByType() 认它，
// OptimalPacker 的「全试一遍」也会自动把它算进去。见 shelf_packer.js 里的说明。
let SHELF_OK = true;
try {
  require('./shelf_packer').registerShelfPacker();
} catch (e) {
  SHELF_OK = false;
  console.error('  ! 货架装箱器注册失败，退回 core 自带的 MaxRects：' + (e && e.message ? e.message : e));
}

// 读图片宽高（自己解析头部，不依赖 sharp/jimp —— 那两个读一张图要几十毫秒，
// 预排序阶段只需要尺寸，读几百张图的时候差别很明显）
function pngSize(buf) {
  if (buf.length < 24 || buf[0] !== 0x89 || buf[1] !== 0x50) return null;
  // PNG: 8 字节 signature, 4 len, 4 'IHDR', 4 w, 4 h
  return { w: buf.readUInt32BE(16), h: buf.readUInt32BE(20) };
}

function jpegSize(buf) {
  if (buf.length < 4 || buf[0] !== 0xFF || buf[1] !== 0xD8) return null;
  let i = 2;
  while (i + 9 < buf.length) {
    if (buf[i] !== 0xFF) { i++; continue; }
    const marker = buf[i + 1];
    // SOF0/1/2/3/5/6/7/9/10/11/13/14/15 里带尺寸；C4(DHT)/C8(JPG)/CC(DAC) 要跳过
    if (marker >= 0xC0 && marker <= 0xCF && marker !== 0xC4 && marker !== 0xC8 && marker !== 0xCC) {
      return { h: buf.readUInt16BE(i + 5), w: buf.readUInt16BE(i + 7) };
    }
    const len = buf.readUInt16BE(i + 2);
    if (len < 2) return null;
    i += 2 + len;
  }
  return null;
}

function webpSize(buf) {
  if (buf.length < 30) return null;
  if (buf.toString('ascii', 0, 4) !== 'RIFF' || buf.toString('ascii', 8, 12) !== 'WEBP') return null;
  const fmt = buf.toString('ascii', 12, 16);
  if (fmt === 'VP8X') return { w: 1 + buf.readUIntLE(24, 3), h: 1 + buf.readUIntLE(27, 3) };
  if (fmt === 'VP8L') {
    const bits = buf.readUInt32LE(21);
    return { w: 1 + (bits & 0x3FFF), h: 1 + ((bits >> 14) & 0x3FFF) };
  }
  if (fmt === 'VP8 ') return { w: buf.readUInt16LE(26) & 0x3FFF, h: buf.readUInt16LE(28) & 0x3FFF };
  return null;
}

function bmpSize(buf) {
  if (buf.length < 26 || buf[0] !== 0x42 || buf[1] !== 0x4D) return null;
  return { w: buf.readInt32LE(18), h: Math.abs(buf.readInt32LE(22)) };
}

function imageSize(buf) {
  return pngSize(buf) || jpegSize(buf) || webpSize(buf) || bmpSize(buf);
}

// 导出格式：type -> fileExt（完整 18 个，取自 exporters/list.json）
const FORMATS = {
  'json-hash':    'JsonHash',      // .json
  'json-array':   'JsonArray',     // .json  数组+filename ← UE Paper2D 用这个
  'xml':          'XML',           // .xml
  'css':          'Css',           // .css
  'old-css':      'OldCss',        // .css   (不允许 trim/rotate)
  'pixi':         'Pixi',          // .json
  'godot-atlas':  'GodotAtlas',    // .tpsheet
  'godot-tileset':'GodotTileset',  // .tpset
  'phaser-hash':  'PhaserHash',    // .json
  'phaser-array': 'PhaserArray',   // .json
  'phaser3':      'Phaser3',       // .json
  'cocos2d':      'Cocos2d',       // .plist
  'unreal':       'Unreal',        // .paper2dsprites (hash 形式，UE 兼容性存疑)
  'starling':     'Starling',      // .xml
  'spine':        'Spine',         // .atlas
  'uikit':        'UIKit',         // .plist (不允许 rotate)
  'unity3d':      'Unity3D',       // .tpsheet (不允许 rotate)
  'egret':        'Egret2D',       // .json   (不允许 trim/rotate)
};

function naturalKey(s) {
  return s.split(/(\d+)/).map(t => (/^\d+$/.test(t) ? parseInt(t, 10) : t.toLowerCase()));
}

function escapeRe(s) {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

// core 内部（PackProcessor.js: `Object.keys(images).sort()`）是按名字字典序处理图元的，
// 而我们想要「height desc -> width desc -> name asc」，所以给每张图的名字前面挂一个序号，
// 让字典序跟着我们的顺序走。但 exporters 直接拿这个 path 当 sprite 名
// （exporters/index.js: `let name = item.name;`），于是 `0013_Misaka-Sk2-xxx` 这种名字
// 会原样写进 .paper2dsprites / .tpsheet / .css …… 比哈希名还难看。
//
// 所以在落盘前把序号前缀精确剔掉。用「精确字符串 + 单次替换」而不是 `\d{4}_` 正则：
//   1. 正则会把本来就叫 `0001_idle.png` 的源图误伤；
//   2. 单次替换避免了「A→B 之后又匹配到 B 的规则」这种级联。
function stripSortPrefix(buf, pairs) {
  if (!pairs.length) return buf;
  const map = new Map();
  for (const [pfx, plain] of pairs) if (!map.has(pfx)) map.set(pfx, plain);
  const keys = [...map.keys()].sort((a, b) => b.length - a.length);
  const re = new RegExp(keys.map(escapeRe).join('|'), 'g');
  const s = buf.toString('utf8').replace(re, m => map.get(m));
  return Buffer.from(s, 'utf8');
}

const TEXT_EXT = new Set(['.json', '.paper2dsprites', '.tpsheet', '.tpset',
  '.xml', '.plist', '.css', '.atlas', '.txt']);

function isTextOutput(name) {
  return TEXT_EXT.has(path.extname(name).toLowerCase());
}

// ---------------------------------------------------------------- 输入清单（对接 ZD 工具箱的接口）
//
// 一帧一条：图片 + 精灵名 + 序号。精灵名直接写进图集（不再从文件名反推），
// 序号决定动画帧序并固化成 _sequence.json。格式与 ue_atlas.py 完全一致，见 MANIFEST.md。

const MANIFEST_META_KEYS = ['atlas', 'spritePrefix', 'mode', 'trim', 'padding', 'extrude',
  'rotate', 'cols', 'columns', 'anchor', 'maxSize', 'max_size', 'pot', 'crop'];

function loadManifest(p) {
  const abs = path.resolve(p);
  const base = path.dirname(abs);
  const ext = path.extname(abs).toLowerCase();
  const meta = {};
  let frames = [];

  if (ext === '.json') {
    let data = JSON.parse(fs.readFileSync(abs, 'utf8'));
    if (Array.isArray(data)) data = { frames: data };
    if (!data || !Array.isArray(data.frames)) {
      throw new Error('清单 JSON 的顶层要么是数组，要么是含 frames 数组的对象');
    }
    for (const k of MANIFEST_META_KEYS) {
      if (data[k] !== undefined && data[k] !== null) meta[k] = data[k];
    }
    frames = data.frames.map((fr, i) => {
      if (typeof fr === 'string') fr = { file: fr };
      if (!fr || !fr.file) throw new Error(`清单第 ${i + 1} 帧缺少 file`);
      return { file: fr.file, name: fr.name == null ? null : fr.name,
               index: fr.index == null ? null : fr.index };
    });
  } else {
    // TSV / CSV / 空格分隔，一行一帧；# 开头是注释
    const lines = fs.readFileSync(abs, 'utf8').split(/\r?\n/);
    lines.forEach((raw, n) => {
      const s = raw.replace(/\s+$/, '');
      if (!s.trim() || s.trim().startsWith('#')) return;
      let parts;
      if (s.includes('\t')) parts = s.split('\t');
      else if (s.includes(',')) parts = s.split(',');
      else parts = s.split(/\s+/);
      parts = parts.map(x => x.trim());
      const file = parts[0];
      if (!file) return;
      const name = parts.length > 1 && parts[1] ? parts[1] : null;
      let index = null;
      if (parts.length > 2 && parts[2]) {
        index = parseInt(parts[2], 10);
        if (Number.isNaN(index)) throw new Error(`清单第 ${n + 1} 行序号不是整数：${parts[2]}`);
      }
      frames.push({ file, name, index });
    });
  }

  if (!frames.length) throw new Error('清单里没有任何帧：' + abs);
  frames.forEach((fr, i) => {
    const q = path.isAbsolute(fr.file) ? fr.file : path.resolve(base, fr.file);
    if (!fs.existsSync(q)) throw new Error(`清单第 ${i + 1} 帧的图片不存在：${q}`);
    fr.file = q;
  });
  if (!meta.atlas) meta.atlas = path.basename(abs, path.extname(abs));
  meta.frames = frames;
  return meta;
}

function normalizeManifest(man) {
  const frames = man.frames;
  const n = frames.length;
  const warnings = [];

  let idxs = frames.map(f => (f.index === undefined ? null : f.index));
  if (idxs.every(x => x === null)) {
    idxs = Array.from({ length: n }, (_, i) => i + 1);
  } else {
    const used = idxs.filter(x => x !== null);
    let nxt = used.length ? Math.max(...used) + 1 : 1;
    for (let k = 0; k < n; k++) if (idxs[k] === null) idxs[k] = nxt++;
  }

  const cnt = {};
  idxs.forEach(i => { cnt[i] = (cnt[i] || 0) + 1; });
  const dupIdx = Object.keys(cnt).filter(k => cnt[k] > 1).map(Number).sort((a, b) => a - b);
  if (dupIdx.length) warnings.push('序号重复：' + dupIdx.slice(0, 8).join(', '));

  const prefix = String(man.spritePrefix || man.atlas || 'sprite');
  const width = Math.max(2, String(Math.max(...idxs)).length);
  const names = frames.map((f, k) => (f.name ? String(f.name)
    : `${prefix}_${String(idxs[k]).padStart(width, '0')}`));

  const seen = {};
  names.forEach(nm => { seen[nm] = (seen[nm] || 0) + 1; });
  const dupName = Object.keys(seen).filter(k => seen[k] > 1);
  if (dupName.length) {
    throw new Error('精灵名重复，UE 里会互相覆盖：' + dupName.slice(0, 8).join(', '));
  }

  const entries = frames.map((f, k) => ({ file: f.file, name: names[k], index: idxs[k] }));
  entries.sort((a, b) => a.index - b.index);   // 按序号升序 —— grid 用不到，但顺序是契约
  return { entries, warnings };
}

// core 的 padding 记账和 ue_atlas.py 不一样，这里换算成等价口径。
//
// core：把每张图的 frame 膨胀 `padding*2 + extrude*2`，装完再把左上角内缩 `padding + extrude`。
//       → 两张图**核心内容之间**的实际间隙 = 2*(padding + extrude)
// ue_atlas：间隙 = max(padding, 2*extrude)（resolve_padding，够放两侧的扩边就行）
//
// 所以 core 默认会比你要求的多留 2*extrude 像素/张（padding=2, extrude=1 时是 6px 而不是 2px），
// 17 张图下来就是实打实的面积浪费。这里反解出「间隙刚好等于 max(padding, 2*extrude)」的 core padding。
function resolveCorePadding(userPadding, extrude) {
  const need = Math.max(userPadding, 2 * extrude);
  const raw = (need - 2 * extrude) / 2;
  return Math.max(0, Math.ceil(raw - 1e-9));
}


// ---------------------------------------------------------------- 装箱器选择
//
// core 原生只有 MaxRects 族，而 MaxRects 在图元尺寸接近时**反而差**：
// 实测同一批 17 帧，MaxRects 密度 86.1%、货架式 93.4%、TexturePacker 94.8%。
// 所以默认走 OptimalPacker —— 它会把列表里每个装箱器的每个方法 × 是否旋转全试一遍，
// 注册了 ShelfPacker 之后就是「货架 + MaxRects 全家桶」一起选最优。

function pickPackerClass(name) {
  switch (String(name || 'auto').toLowerCase()) {
    case 'shelf': return SHELF_OK ? 'ShelfPacker' : 'MaxRectsBin';
    case 'maxrects': return 'MaxRectsBin';
    case 'all':
    case 'optimal':
    case 'auto':
    default: return SHELF_OK ? 'OptimalPacker' : 'MaxRectsBin';
  }
}

function pickPackerMethod(name, fallback) {
  // OptimalPacker / ShelfPacker 的方法由它们自己给；只有 MaxRectsBin 用得上用户指定值
  return pickPackerClass(name) === 'MaxRectsBin' ? (fallback || 'BestShortSideFit') : undefined;
}


// ---------------------------------------------------------------- 画布尺寸搜索

// 长宽比惩罚指数。0.4 是实测值：再高会为了方正多花一成面积
// （同一批 17 帧 1.18x/1:1.19 会被换成 1.26x/1:1.03），再低就开始容忍长条。
const ASPECT_POWER = 0.4;

function readJsonOf(res, name) {
  const f = res.find(x => x.name.endsWith('.json') && x.name.startsWith(name))
        || res.find(x => x.name.endsWith('.json'));
  if (!f) return null;
  try { return JSON.parse(f.buffer.toString('utf8')); } catch (e) { return null; }
}

/**
 * 候选画布宽度梯子。
 * side 取 trim 后图元净面积的平方根 —— 也就是「正方形画布」大概要多大。
 * 围着一圈取若干倍率，再加上最宽图元本身，宽度太小的直接丢掉（放不下最宽那张）。
 */
function candidateWidths(area, maxItemW, maxSize, many) {
  const side = Math.max(1, Math.round(Math.sqrt(area)));
  const factors = many
    ? [0.55, 0.65, 0.75, 0.85, 0.95, 1.0, 1.05, 1.15, 1.3, 1.5, 1.8, 2.2]
    : [0.7, 0.85, 1.0, 1.2, 1.5];
  const set = new Set();
  for (const f of factors) set.add(Math.round(side * f));
  for (const m of [maxItemW, maxItemW * 2]) set.add(Math.round(m));
  return [...set].filter(w => w >= maxItemW && w <= maxSize).sort((a, b) => a - b);
}

/**
 * 选一个「又方又满」的画布。
 *
 * 背景：core 的 MaxRectsBin / BestShortSideFit 在图元尺寸接近时会一路往下贴。我们原来把
 * 2048×2048 的箱子丢给它、再让它自己缩到紧凑尺寸，结果 17 帧 Misaka-Sk2 被排成 747×2044
 * 的竖条（1:2.74，右边空一大块），而 TexturePacker 给的是 1132×996（1:1.14）。
 *
 * 修法是把**箱体宽度当变量搜一遍**：固定宽度、给足高度，装完看它自己缩出来的紧凑尺寸。
 * 实测同一批 17 帧：W=1132 时 → 1068×1196（1:1.12），和 TexturePacker 一个形状。
 *
 * 打分 = 面积 × 长宽比^0.8，指数越大越偏向方形。
 * 探测统一用 JsonArray（它有 meta.size 和 spriteSourceSize，和最终格式无关），
 * 选中之后再按用户要的格式正式跑一次，省得每个候选都渲染一遍目标格式。
 */
async function searchCanvas(images, opts, a, name, log) {
  // 装箱器自己会搜宽度的（ShelfPacker 内部扫行宽、OptimalPacker 内部试全部启发式），
  // 就别再在外面套一层宽度梯子 —— 否则等于把它的 binWidth 限死了，反而更差。
  const selfSearching = ['OptimalPacker', 'ShelfPacker'].includes(String(opts.packer));
  if (selfSearching) {
    const r = await packAsync(images, { ...opts, width: a.maxSize, height: a.maxSize });
    const j = readJsonOf(r, name);
    const size = j && j.meta && j.meta.size;
    if (size && log) {
      log(`  画布：${String(opts.packer)} 内部自搜 -> ${size.w}x${size.h}`);
    }
    return size ? { width: a.maxSize, height: a.maxSize, size, tried: 1 } : null;
  }

  const probeOpts = { ...opts, exporter: 'JsonArray', width: a.maxSize, height: a.maxSize };
  const first = await packAsync(images, probeOpts);
  const j0 = readJsonOf(first, name);
  if (!j0 || !j0.frames || !j0.meta || !j0.meta.size) return null;

  let area = 0, maxItemW = 0;
  for (const fr of j0.frames) {
    const s = fr.spriteSourceSize, f = fr.frame;
    const w = s ? s.w : (f ? f.w : 0);
    const h = s ? s.h : (f ? f.h : 0);
    area += w * h;
    maxItemW = Math.max(maxItemW, w);
  }
  if (!area) return null;

  const list = candidateWidths(area, maxItemW, a.maxSize, j0.frames.length <= 128);

  const tried = [];
  let best = null;
  const consider = (w, size) => {
    if (!size || size.w > a.maxSize || size.h > a.maxSize) return;
    const ratio = Math.max(size.w, size.h) / Math.min(size.w, size.h);
    const score = size.w * size.h * Math.pow(ratio, ASPECT_POWER);
    tried.push({ key: `${size.w}x${size.h}`, w, size, score });
    if (!best || score < best.score) best = { w, size, score };
  };

  consider(a.maxSize, j0.meta.size);

  for (const w of list) {
    if (w === a.maxSize) continue;
    let r;
    try {
      r = await packAsync(images, { ...probeOpts, width: w, height: a.maxSize });
    } catch (e) { continue; }
    const j = readJsonOf(r, name);
    if (!j || !j.meta || !j.meta.size) continue;
    consider(w, j.meta.size);
  }

  if (!best) return null;
  tried.sort((x, y) => x.score - y.score);

  const netRatio = best.size.w * best.size.h / area;
  const ratio = Math.max(best.size.w, best.size.h) / Math.min(best.size.w, best.size.h);
  if (log) {
    const distinct = new Set(tried.map(t => t.key)).size;
    log(`  画布搜索：试了 ${tried.length} 个宽度 / ${distinct} 种版面 -> 选中宽度 ${best.w}` +
        ` -> ${best.size.w}x${best.size.h}  (面积 ${netRatio.toFixed(2)}x, 1:${ratio.toFixed(2)})`);
    const alt = [...new Map(tried.map(t => [t.key, t])).values()]
      .filter(t => t.key !== `${best.size.w}x${best.size.h}`)
      .sort((x, y) => x.score - y.score)
      .slice(0, 3)
      .map(t => `${t.size.w}x${t.size.h}(${(t.size.w * t.size.h / area).toFixed(2)}x)`);
    if (alt.length) log(`    次优：${alt.join('  ')}`);
  }
  return { width: best.w, height: a.maxSize, size: best.size, tried: tried.length };
}

function collectImages(dir, recursive) {
  if (fs.statSync(dir).isFile()) return IMG_EXT.has(path.extname(dir).toLowerCase()) ? [dir] : [];
  const out = [];
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const e of entries) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (recursive) out.push(...collectImages(p, true));
    } else if (IMG_EXT.has(path.extname(e.name).toLowerCase())) {
      out.push(p);
    }
  }
  return out.sort((a, b) => {
    const ka = naturalKey(path.basename(a)), kb = naturalKey(path.basename(b));
    for (let i = 0; i < Math.max(ka.length, kb.length); i++) {
      if (ka[i] === undefined) return -1;
      if (kb[i] === undefined) return 1;
      if (ka[i] < kb[i]) return -1;
      if (ka[i] > kb[i]) return 1;
    }
    return 0;
  });
}

function parseArgs(argv) {
  const a = {
    input: null, out: './out', name: 'atlas', format: 'json-array',
    padding: 2, extrude: 0, trim: true, rotate: false, pot: false,
    maxSize: 2048, recursive: false, eachSubdir: false,
    prependFolder: false, keepExt: false, detectIdentical: true,
    search: true, packerMethod: 'BestShortSideFit',
    packer: 'auto', manifest: null, report: null,
  };
  const rest = [];
  for (let i = 0; i < argv.length; i++) {
    const v = argv[i];
    switch (v) {
      case '-o': case '--out': a.out = argv[++i]; break;
      case '--name': a.name = argv[++i]; break;
      case '--format': a.format = argv[++i]; break;
      case '--manifest': a.manifest = argv[++i]; break;
      case '--report': a.report = argv[++i]; break;
      case '--padding': a.padding = parseInt(argv[++i], 10); break;
      case '--extrude': a.extrude = parseInt(argv[++i], 10); break;
      case '--max-size': a.maxSize = parseInt(argv[++i], 10); break;
      case '--trim': a.trim = true; break;
      case '--no-trim': a.trim = false; break;
      case '--rotate': a.rotate = true; break;
      case '--pot': a.pot = true; break;
      case '--no-pot': a.pot = false; break;
      case '--recursive': a.recursive = true; break;
      case '--each-subdir': a.eachSubdir = true; break;
      case '--prepend-folder': a.prependFolder = true; break;
      case '--keep-ext': a.keepExt = true; break;
      case '--detect-identical': a.detectIdentical = true; break;
      case '--no-detect-identical': a.detectIdentical = false; break;
      case '--search': a.search = true; break;
      case '--no-search': a.search = false; break;
      case '--packer-method': a.packerMethod = argv[++i]; break;
      case '--packer': a.packer = argv[++i]; break;
      case '-h': case '--help': a.help = true; break;
      default: rest.push(v);
    }
  }
  if (rest.length) a.input = rest[0];
  return a;
}

const HELP = `
pack_ue.js —— 基于 free-tex-packer-core 的 UE 图集 CLI

  node pack_ue.js <输入目录> [选项]

选项:
  -o, --out <dir>      输出目录 (默认 ./out)
      --name <str>     输出文件名 (默认 atlas)
      --manifest <file> 走清单：一帧一条，给「图片、精灵名、序号」(.json / .tsv / .csv)
                        精灵名直接写进图集（不从文件名反推），序号决定动画帧序，
                        并额外产出 <name>_sequence.json。详见 MANIFEST.md
      --report <file>  把机器可读的结果 JSON 写到这个路径（给调用方解析用）
      --format <fmt>   ${Object.keys(FORMATS).join(' | ')}
                       (默认 json-array)
      --padding <n>    间距 (默认 2)
      --extrude <n>    边缘外扩，防 mip 漏色 (默认 0)
      --max-size <n>   最大边长 (默认 2048)
      --trim / --no-trim  裁剪透明边 (默认 --trim，UE 工作流几乎都该开)
      --rotate         允许 90° 旋转
      --pot / --no-pot 尺寸取 2 的幂 (默认 --no-pot，UE5 Paper2D 不强求 POT，更省空间)
      --recursive      递归扫描子目录
      --each-subdir    每个子目录单独出一张图集
      --keep-ext       sprite 名保留扩展名
      --prepend-folder sprite 名带上文件夹前缀
      --detect-identical / --no-detect-identical
                       检测重复图并共用一份 (默认开启)
      --search / --no-search
                       搜索画布尺寸，挑「又方又满」的那个 (默认 --search)。
                       关掉就退回 core 的默认行为：给个方块箱子，常常排出瘦长竖条。
      --packer <name>  auto(默认) | shelf | maxrects
                       auto     = 全试一遍挑最优（含货架式与 MaxRects 的全部启发式）
                       shelf    = 只走货架式；图元尺寸接近时比 MaxRects 省很多
                       maxrects = 只走 MaxRects（core 自带）
                       ※ core 原生没有货架式，是我们挂上去的，见 shelf_packer.js
      --packer-method <m>
                       MaxRects 的启发式，默认 BestShortSideFit
                       (BestShortSideFit | BestLongSideFit | BestAreaFit |
                        BottomLeftRule | ContactPointRule)

关于 .paper2dsprites:
  选 json-array 或 unreal 都会额外写一份数组版（数组 + filename），UE 只稳定认这种。
  unreal 格式下 core 还会产出官方模板版（hash 形式），两者分别存为
  <name>.paper2dsprites 与 <name>_array.paper2dsprites，拖能用的那一个。

示例:
  node pack_ue.js ./fx_frames -o ./out --name fx_hit --padding 2 --extrude 2
  node pack_ue.js ./sprites -o ./out --name hero_idle --format unreal
  node pack_ue.js ./vfx -o ./out --each-subdir
`;

async function packOne(files, a, outdir, name, root, manifestEntries) {
  const fromManifest = Array.isArray(manifestEntries) && manifestEntries.length > 0;
  if (!fromManifest && (!files || !files.length)) {
    console.log(`  ! ${name}: 没有图片，跳过`);
    return null;
  }

  // 关键：free-tex-packer-core 内部按文件名字典序处理 rects（PackProcessor.js），对 MaxRects 是最差排序。
  // TexturePacker 的做法是按 height desc -> width desc -> name asc，
  // 也就是最大的先装，能显著减少碎空洞。下面手工预排，再把文件名换成 sortKey。
  let fileInfo;
  if (fromManifest) {
    // 走清单：精灵名由清单给定，绝不从文件名反推
    fileInfo = manifestEntries.map(e => {
      const buf = fs.readFileSync(e.file);
      const wh = imageSize(buf) || { w: 0, h: 0 };
      return { origPath: e.file, rel: e.name, sortName: e.name,
               w: wh.w, h: wh.h, contents: buf, spriteName: e.name, index: e.index };
    });
  } else {
    fileInfo = files.map(f => {
      // 读尺寸用 sharp/jimp 太慢，直接读文件头即可
      const buf = fs.readFileSync(f);
      const wh = imageSize(buf) || { w: 0, h: 0 };
      // 相对 root 的路径，统一成 / 分隔 —— 交给 core 之后 prependFolderName 才能生效。
      // 原来只传 basename，导致 --prepend-folder 是个静默空操作。
      const rel = path.relative(root || '.', f).split(path.sep).join('/');
      return { origPath: f, rel, sortName: path.basename(f), w: wh.w, h: wh.h, contents: buf };
    });
  }
  fileInfo.sort((a, b) => (b.h - a.h) || (b.w - a.w) || (a.sortName < b.sortName ? -1 : 1));

  // UE Paper2D 拿 sourceSize / spriteSourceSize 反算每帧的 pivot，各帧原始尺寸不同的话，
  // 锚点就各在一处，Flipbook 播放时会上下跳。这一点工具补不回来——只有当输入是
  // 「同一张画布上、带完整透明边」的帧时 sourceSize 才会一致（TexturePacker 就是这么工作的）。
  const warnings = [];
  const srcSizes = new Set(fileInfo.map(f => `${f.w}x${f.h}`));
  if (srcSizes.size > 1) {
    const msg = `${name}: 源帧尺寸不统一（${srcSizes.size} 种）-> UE 里每帧锚点会不一致，播放会跳`;
    console.log(`  ! ${msg}`);
    console.log(`     -> 拿未裁剪的整幅画布帧重新导出，或改用 ue_atlas.py --mode grid（格子统一，锚点自然一致）`);
    warnings.push(msg);
  }

  const images = fileInfo.map((it, i) => ({
    path: String(i).padStart(4, '0') + '_' + it.rel,  // 让 sortKey 主导核心内部排序
    contents: it.contents,
  }));

  // 落盘前要剔掉的「序号_」对照表。
  // 扫目录时 rel 可能是带目录的相对路径（--prepend-folder 用），要把四种写法都列上；
  // 走清单时 rel 就是最终精灵名，一种就够。
  const stripPairs = [];
  fileInfo.forEach((it, i) => {
    const pfx = String(i).padStart(4, '0') + '_';
    if (fromManifest) {
      stripPairs.push([pfx + it.rel, it.rel]);
      return;
    }
    const noExt = it.rel.replace(/\.[^.]+$/, '');
    stripPairs.push([pfx + it.rel, it.rel], [pfx + noExt, noExt],
                    [pfx + it.sortName, it.sortName],
                    [pfx + it.sortName.replace(/\.[^.]+$/, ''), it.sortName.replace(/\.[^.]+$/, '')]);
  });

  const opts = {
    textureName: name,
    width: a.maxSize,
    height: a.maxSize,
    fixedSize: false,
    powerOfTwo: a.pot,
    padding: resolveCorePadding(a.padding, a.extrude),
    extrude: a.extrude,
    allowTrim: a.trim,
    allowRotation: a.rotate,
    detectIdentical: a.detectIdentical,
    // 走清单时名字必须原样落盘：core 的 removeFileExtension 会按 "." 截断，
    // prependFolderName 会按 "/" 拼前缀 —— 都可能把清单给的精灵名改掉，所以两个都关掉。
    removeFileExtension: fromManifest ? false : !a.keepExt,
    prependFolderName: fromManifest ? false : a.prependFolder,
    packer: pickPackerClass(a.packer),
    packerMethod: pickPackerMethod(a.packer, a.packerMethod),
    exporter: FORMATS[a.format] || a.format,
    filter: 'none',
    textureFormat: 'png',
    appInfo: { displayName: 'pack_ue.js', version: '1.0', url: 'https://github.com/odrick/free-tex-packer-core' },
  };

  // 画布尺寸搜索：core 自己不会挑形状，给个方块箱子它照样排出竖条（见 searchCanvas 注释）。
  let chosen = null;
  if (a.search) {
    try {
      chosen = await searchCanvas(images, opts, a, name, s => console.log(s));
    } catch (e) {
      console.log(`  ! ${name}: 画布搜索失败，回退到默认尺寸（${e && e.message ? e.message : e}）`);
    }
  }
  const finalOpts = chosen ? { ...opts, width: chosen.width, height: chosen.height } : opts;

  const out = await packAsync(images, finalOpts);
  fs.mkdirSync(outdir, { recursive: true });

  const written = [];
  for (const item of out) {
    const p = path.join(outdir, item.name);
    const buf = isTextOutput(item.name) ? stripSortPrefix(item.buffer, stripPairs) : item.buffer;
    fs.writeFileSync(p, buf);
    written.push(item.name);
  }

  const pngName = (out.find(f => f.name.endsWith('.png')) || {}).name || `${name}.png`;

  // UE Paper2D 靠扩展名 .paper2dsprites 分派导入器。
  // 实测 UE 只稳定识别「数组 + filename」形式，官方 Unreal 模板的 hash 形式有问题。
  // 所以只要格式与 UE 相关（json-array / unreal），都把数组版额外写一份 .paper2dsprites。
  //
  // 注意：--format unreal 时 core 自己已经产出了 name.paper2dsprites（官方模板、hash 形式）。
  // 数组版必须另起名字，不能盖上去 —— 原来就是写同一个文件名，
  // 于是「两个都留着、哪个能用用哪个」实际只剩一份，README 说的 _array 变体根本不存在。
  let arrJson = null;
  if (a.format === 'json-array' || a.format === 'unreal') {
    const hasOfficial = out.some(f => f.name.endsWith('.paper2dsprites'));
    let arrBuf = out.find(f => f.name.endsWith('.json') && f.name.startsWith(name));
    if (!arrBuf) {
      const alt = await packAsync(images, { ...finalOpts, exporter: 'JsonArray' });
      arrBuf = alt.find(f => f.name.endsWith('.json'));
    }
    if (arrBuf) {
      const target = hasOfficial ? `${name}_array.paper2dsprites` : `${name}.paper2dsprites`;
      const stripped = stripSortPrefix(arrBuf.buffer, stripPairs);
      fs.writeFileSync(path.join(outdir, target), stripped);
      written.push(target);
      try { arrJson = JSON.parse(stripped.toString('utf8')); } catch (e) { arrJson = null; }
    }
  }

  // _sequence.json：按「序号」排好的帧表，动画帧序的**唯一权威**。
  // 同 ue_atlas.py：UE 建 Flipbook 是按 sprite 名排序的，名字一旦无序顺序就丢了；
  // 走清单时序号是外部给的，这里把它连同每帧在图集里的矩形一起固化下来，
  // Unreal 侧脚本可以照它精确建 Flipbook，不用去猜名字排序。
  let seqPath = null;
  if (fromManifest && arrJson && Array.isArray(arrJson.frames)) {
    const idxByName = new Map(manifestEntries.map(e => [e.name, e.index]));
    const seq = arrJson.frames.map(f => ({
      index: idxByName.has(f.filename) ? idxByName.get(f.filename) : 0,
      name: f.filename,
      frame: f.frame,
      rotated: !!f.rotated,
      trimmed: !!f.trimmed,
      spriteSourceSize: f.spriteSourceSize,
      sourceSize: f.sourceSize,
    })).sort((x, y) => (x.index - y.index) || (x.name < y.name ? -1 : 1));
    seqPath = path.join(outdir, `${name}_sequence.json`);
    fs.writeFileSync(seqPath, JSON.stringify({
      atlas: name,
      image: pngName,
      sprites: `${name}.paper2dsprites`,
      texture: (arrJson.meta && arrJson.meta.size) || null,
      frameCount: seq.length,
      order: 'frames 已按 index 升序排列，这个顺序就是动画帧序（第 0 帧在前）',
      frames: seq,
    }, null, 2), 'utf8');
    written.push(`${name}_sequence.json`);
  }

  console.log(`  [${a.format}] ${name} -> ${written.join(', ')}  (${images.length} 帧)`);
  return {
    ok: true, atlas: name, mode: 'pack',
    image: path.join(outdir, pngName),
    sequence: seqPath,
    size: (arrJson && arrJson.meta && arrJson.meta.size) || null,
    frameCount: images.length,
    files: written,
    warnings,
  };
}

(async () => {
  const a = parseArgs(process.argv.slice(2));
  if (a.help || (!a.input && !a.manifest)) { console.log(HELP); process.exit(a.help ? 0 : 1); }

  const outdir = path.resolve(a.out);
  const results = [];

  // ---------------- 清单模式：名字与序号由清单给定 ----------------
  if (a.manifest) {
    const man = loadManifest(a.manifest);
    const { entries, warnings } = normalizeManifest(man);
    const name = a.name && a.name !== 'atlas' ? a.name : (man.atlas || 'atlas');
    // 清单里能覆盖的参数（命令行显式给了就用命令行的）
    const dflt = { padding: 2, extrude: 0, rotate: false, trim: true, maxSize: 2048,
                   pot: false, packerMethod: 'BestShortSideFit' };
    for (const [mk, ak] of [['padding', 'padding'], ['extrude', 'extrude'],
                            ['rotate', 'rotate'], ['trim', 'trim'], ['maxSize', 'maxSize'],
                            ['max_size', 'maxSize'], ['pot', 'pot'],
                            ['packerMethod', 'packerMethod']]) {
      if (man[mk] !== undefined && man[mk] !== null && a[ak] === dflt[ak]) a[ak] = man[mk];
    }
    warnings.forEach(w => console.log(`  ! ${w}`));
    console.log(`清单：${entries.length} 帧 -> 图集 ${name}`);
    const r = await packOne(null, a, outdir, name, null, entries);
    if (r) { r.warnings = r.warnings.concat(warnings); results.push(r); }
  } else {
    const src = path.resolve(a.input);
    if (!fs.existsSync(src)) { console.error('输入路径不存在:', src); process.exit(1); }
    if (a.eachSubdir) {
      const subs = fs.readdirSync(src, { withFileTypes: true })
        .filter(d => d.isDirectory()).map(d => d.name).sort(naturalKey);
      if (!subs.length) { console.error('--each-subdir 但目录下没有子目录'); process.exit(1); }
      console.log(`共 ${subs.length} 个子目录`);
      for (const s of subs) {
        const r = await packOne(collectImages(path.join(src, s), true), a, outdir, s, path.join(src, s));
        if (r) results.push(r);
      }
    } else {
      const files = collectImages(src, a.recursive);
      if (!files.length) { console.error('没有找到图片:', src); process.exit(1); }
      const r = await packOne(files, a, outdir, a.name, src);
      if (r) results.push(r);
    }
  }

  if (a.report) {
    fs.writeFileSync(path.resolve(a.report), JSON.stringify(
      results.length === 1 ? results[0] : { ok: true, outdir, atlasses: results },
      null, 2), 'utf8');
    console.log(`结果 JSON -> ${path.resolve(a.report)}`);
  }

  console.log(`\n完成 -> ${outdir}`);
  if (a.format === 'unreal') {
    console.log('UE: 把 .paper2dsprites 拖进 Content Browser（需启用 Paper2D 插件）');
    console.log('    两个变体都试试：官方模板版 与 _array 版（数组+filename）');
  }
})().catch(e => { console.error('打包失败:', e && e.message ? e.message : e); process.exit(1); });
